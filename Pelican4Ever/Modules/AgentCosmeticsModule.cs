using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace Pelican4Ever.Modules
{
    /// <summary>Mensaje del host a un cliente: aplicar (o quitar) el cuerpo de Dr4co a su propio granjero.</summary>
    public class AgentBodyPayload
    {
        public bool Enable { get; set; }
    }

    /// <summary>Mensaje del host a un cliente: aplicar (o quitar) la apariencia de Putre a su propio granjero.</summary>
    public class AgentPutrePayload
    {
        public bool Enable { get; set; }
    }

    /// <summary>
    /// Cuerpos y apariencias cosméticas de los agentes:
    /// <list type="bullet">
    /// <item>comando de host <c>p4e_dr4co_sprite [on|off] [jugador]</c> (consola SMAPI y chat) para el cuerpo de dragón de Dr4co (Fashion Sense);</item>
    /// <item>comando de host <c>p4e_putre_sprite [on|off] [jugador]</c> (consola SMAPI y chat) para la apariencia orca de Putre (preset vanilla);</item>
    /// <item>desplaza la herramienta en uso para que salga de las garras del cuerpo y no de su cara.</item>
    /// </list>
    /// No depende de Fashion Sense: lee y escribe el id del cuerpo en <c>Farmer.modData</c>, que se sincroniza,
    /// así que cada cliente con el mod dibuja bien también las herramientas de los demás.
    /// </summary>
    public sealed class AgentCosmeticsModule : IModule
    {
        private const string Dr4coCommand = "p4e_dr4co_sprite";
        private const string PutreCommand = "p4e_putre_sprite";
        private const string Dr4coMessageType = "AgentBody";
        private const string PutreMessageType = "AgentPutreBody";
        private const string FashionSenseId = "PeacefulEnd.FashionSense";

        private static ModContext? context;
        private ChatBox? replyChat;

        public string Name => "AgentCosmetics";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;
            try
            {
                ctx.Harmony.Patch(
                    original: AccessTools.Method(typeof(Game1), nameof(Game1.drawTool), new[] { typeof(Farmer), typeof(int) }),
                    prefix: new HarmonyMethod(typeof(AgentCosmeticsModule), nameof(Prefix_DrawTool)),
                    finalizer: new HarmonyMethod(typeof(AgentCosmeticsModule), nameof(Finalizer_DrawTool))
                );
                ctx.Monitor.Log("[AgentCosmeticsModule] Parche de herramienta para cuerpos de agente aplicado.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                ctx.Monitor.Log($"[AgentCosmeticsModule] Error al aplicar parches Harmony: {ex}", LogLevel.Error);
            }

            ctx.AddCommand(Dr4coCommand,
                $"Pone o quita el cuerpo de Dr4co (Fashion Sense): {Dr4coCommand} [on|off] [jugador_nombre_o_id]. Sin jugador, al host.",
                OnDr4coCommand);
            ctx.AddCommand(PutreCommand,
                $"Pone o quita la apariencia de Putre (preset vanilla): {PutreCommand} [on|off] [jugador_nombre_o_id]. Sin jugador, al host.",
                OnPutreCommand);

            ctx.Helper.Events.GameLoop.SaveLoaded += (_, _) => RegisterChatCommand();
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
        }

        private void RegisterChatCommand()
        {
            try
            {
                if (!ChatCommands.Exists(Dr4coCommand))
                    ChatCommands.Register(Dr4coCommand, (cmd, chat) => RunFromChat(chat, OnDr4coCommand, cmd),
                        _ => $"Pone o quita el cuerpo de Dr4co: /{Dr4coCommand} [on|off] [jugador]", null,
                        mainOnly: true, multiplayerOnly: false, cheatsOnly: false);
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[AgentCosmetics] Error registrando el comando de chat Dr4co: {ex.Message}", LogLevel.Warn);
            }

            try
            {
                if (!ChatCommands.Exists(PutreCommand))
                    ChatCommands.Register(PutreCommand, (cmd, chat) => RunFromChat(chat, OnPutreCommand, cmd),
                        _ => $"Pone o quita la apariencia de Putre: /{PutreCommand} [on|off] [jugador]", null,
                        mainOnly: true, multiplayerOnly: false, cheatsOnly: false);
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[AgentCosmetics] Error registrando el comando de chat Putre: {ex.Message}", LogLevel.Warn);
            }
        }

        private void RunFromChat(ChatBox chat, Action<string, string[]> handler, string[] cmd)
        {
            replyChat = chat;
            try { handler(cmd[0], cmd.Skip(1).ToArray()); }
            finally { replyChat = null; }
        }

        private void Say(string message, LogLevel level)
        {
            context?.Monitor.Log(message, level);
            replyChat?.addInfoMessage(message);
        }

        private void OnDr4coCommand(string command, string[] args)
        {
            if (context == null)
                return;
            if (!Context.IsWorldReady)
            {
                Say("Partida no cargada.", LogLevel.Warn);
                return;
            }
            if (!Context.IsMainPlayer)
            {
                Say("Este comando solo puede ejecutarse en el Host.", LogLevel.Warn);
                return;
            }

            // [on|off] es opcional: si el primer argumento no lo es, se toma como jugador.
            bool enable = true;
            string[] rest = args;
            if (args.Length > 0 && AgentBodyPreset.ParseToggle(args[0]) is bool parsed)
            {
                enable = parsed;
                rest = args.Skip(1).ToArray();
            }

            Farmer? target = ResolveTarget(rest);
            if (target == null)
                return;

            if (enable && !context.Helper.ModRegistry.IsLoaded(AgentBodyPreset.Dr4coPackId))
                Say($"Aviso: el pack '{AgentBodyPreset.Dr4coPackId}' (agents/Dr4co.FashionSense) no está cargado en este equipo; el cuerpo se guarda pero no se verá hasta instalarlo.", LogLevel.Warn);
            if (enable && !context.Helper.ModRegistry.IsLoaded(FashionSenseId))
                Say("Aviso: Fashion Sense no está instalado en este equipo; el cuerpo no se verá hasta instalarlo.", LogLevel.Warn);

            if (target.IsLocalPlayer)
            {
                Apply(target, enable);
            }
            else if (Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == target.UniqueMultiplayerID))
            {
                // Cada cliente es dueño de su Farmer: se lo pide para que lo aplique él mismo.
                context.Helper.Multiplayer.SendMessage(new AgentBodyPayload { Enable = enable }, Dr4coMessageType,
                    modIDs: new[] { context.Manifest.UniqueID }, playerIDs: new[] { target.UniqueMultiplayerID });
            }
            else
            {
                Say($"{target.Name} no está conectado: el cuerpo se aplicará cuando se conecte y ejecutes el comando de nuevo.", LogLevel.Warn);
                return;
            }

            Say($"{target.Name}: cuerpo de Dr4co {(enable ? "activado" : "desactivado")}.", LogLevel.Info);
        }

        private void OnPutreCommand(string command, string[] args)
        {
            if (context == null)
                return;
            if (!Context.IsWorldReady)
            {
                Say("Partida no cargada.", LogLevel.Warn);
                return;
            }
            if (!Context.IsMainPlayer)
            {
                Say("Este comando solo puede ejecutarse en el Host.", LogLevel.Warn);
                return;
            }

            bool enable = true;
            string[] rest = args;
            if (args.Length > 0 && AgentBodyPreset.ParseToggle(args[0]) is bool parsed)
            {
                enable = parsed;
                rest = args.Skip(1).ToArray();
            }

            Farmer? target = ResolveTarget(rest);
            if (target == null)
                return;

            if (target.IsLocalPlayer)
            {
                ApplyPutre(target, enable);
            }
            else if (Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == target.UniqueMultiplayerID))
            {
                context.Helper.Multiplayer.SendMessage(new AgentPutrePayload { Enable = enable }, PutreMessageType,
                    modIDs: new[] { context.Manifest.UniqueID }, playerIDs: new[] { target.UniqueMultiplayerID });
            }
            else
            {
                Say($"{target.Name} no está conectado: la apariencia se aplicará cuando se conecte y ejecutes el comando de nuevo.", LogLevel.Warn);
                return;
            }

            Say($"{target.Name}: apariencia de Putre {(enable ? "activada" : "desactivada")}.", LogLevel.Info);
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || e.FromModID != context.Manifest.UniqueID)
                return;
            if (!Context.IsWorldReady || e.FromPlayerID != Game1.MasterPlayer.UniqueMultiplayerID)
                return;   // solo el host puede cambiar el cuerpo

            if (e.Type == Dr4coMessageType)
            {
                AgentBodyPayload payload = e.ReadAs<AgentBodyPayload>();
                Apply(Game1.player, payload.Enable);
                context.Monitor.Log($"[AgentCosmetics] El host {(payload.Enable ? "activó" : "desactivó")} el cuerpo de Dr4co.", LogLevel.Info);
            }
            else if (e.Type == PutreMessageType)
            {
                AgentPutrePayload payload = e.ReadAs<AgentPutrePayload>();
                ApplyPutre(Game1.player, payload.Enable);
                context.Monitor.Log($"[AgentCosmetics] El host {(payload.Enable ? "activó" : "desactivó")} la apariencia de Putre.", LogLevel.Info);
            }
        }

        /// <summary>Escribe el modData de Fashion Sense y fuerza a redibujar el granjero.</summary>
        private static void Apply(Farmer farmer, bool enable)
        {
            foreach (KeyValuePair<string, string> pair in AgentBodyPreset.Dr4co(enable))
                farmer.modData[pair.Key] = pair.Value;

            // Lo mismo que hace Fashion Sense al cambiar una apariencia (FashionSense.SetSpriteDirty).
            if (context != null)
            {
                foreach (string field in new[] { "_spriteDirty", "_shirtDirty", "_shoesDirty", "_skinDirty" })
                {
                    try { context.Helper.Reflection.GetField<bool>(farmer.FarmerRenderer, field).SetValue(true); }
                    catch (Exception ex) { context.Monitor.Log($"[AgentCosmetics] No se pudo marcar {field}: {ex.Message}", LogLevel.Trace); }
                }
            }
        }

        /// <summary>Aplica el preset de apariencia vanilla de Putre (o restaura el por defecto) al granjero.</summary>
        public static void ApplyPutre(Farmer farmer, bool enable)
        {
            if (farmer == null)
                return;

            // 1. Limpiar overrides de Fashion Sense para asegurar que se muestre el cuerpo y capas vanilla
            foreach (string key in AgentBodyPreset.FashionSenseOverrideKeys)
            {
                farmer.modData.Remove(key);
            }

            var preset = enable ? AgentBodyPreset.PutrePreset : AgentBodyPreset.DefaultVanillaPreset;

            try
            {
                farmer.changeGender(preset.IsMale);
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[AgentCosmetics] Error al cambiar género: {ex.Message}", LogLevel.Trace);
            }

            try
            {
                farmer.changeSkinColor(preset.Skin, force: true);
                farmer.skin.Value = preset.Skin;
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[AgentCosmetics] Error al cambiar piel: {ex.Message}", LogLevel.Trace);
            }

            try
            {
                farmer.changeHairStyle(preset.Hair);
                farmer.hair.Value = preset.Hair;
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[AgentCosmetics] Error al cambiar pelo: {ex.Message}", LogLevel.Trace);
            }

            try
            {
                farmer.changeShirt(preset.Shirt);
                farmer.shirt.Value = preset.Shirt;
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[AgentCosmetics] Error al cambiar camisa: {ex.Message}", LogLevel.Trace);
            }

            try
            {
                farmer.changePantStyle(preset.Pants);
                farmer.pants.Value = preset.Pants;
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[AgentCosmetics] Error al cambiar pantalones: {ex.Message}", LogLevel.Trace);
            }

            try
            {
                farmer.changeAccessory(preset.Accessory);
                farmer.accessory.Value = preset.Accessory;
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[AgentCosmetics] Error al cambiar accesorio: {ex.Message}", LogLevel.Trace);
            }

            try
            {
                Color eyeColor = new Color(preset.EyeColorR, preset.EyeColorG, preset.EyeColorB);
                farmer.changeEyeColor(eyeColor);
                farmer.newEyeColor.Value = eyeColor;

                Color hairColor = new Color(preset.HairColorR, preset.HairColorG, preset.HairColorB);
                farmer.hairstyleColor.Value = hairColor;

                Color pantsColor = new Color(preset.PantsColorR, preset.PantsColorG, preset.PantsColorB);
                farmer.changePantsColor(pantsColor);
                farmer.pantsColor.Value = pantsColor;
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[AgentCosmetics] Error al cambiar colores: {ex.Message}", LogLevel.Trace);
            }

            try
            {
                farmer.whichPetType = preset.PetType;
                farmer.whichPetBreed = preset.PetBreed;
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[AgentCosmetics] Error al configurar mascota: {ex.Message}", LogLevel.Trace);
            }

            try
            {
                if (enable && !string.IsNullOrEmpty(preset.FavoriteThing) && string.IsNullOrEmpty(farmer.favoriteThing.Value))
                {
                    farmer.favoriteThing.Value = preset.FavoriteThing;
                }
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[AgentCosmetics] Error al fijar cosa favorita: {ex.Message}", LogLevel.Trace);
            }

            if (context != null)
            {
                foreach (string field in new[] { "_spriteDirty", "_shirtDirty", "_shoesDirty", "_skinDirty" })
                {
                    try { context.Helper.Reflection.GetField<bool>(farmer.FarmerRenderer, field).SetValue(true); }
                    catch (Exception ex) { context.Monitor.Log($"[AgentCosmetics] No se pudo marcar {field}: {ex.Message}", LogLevel.Trace); }
                }
            }

            try
            {
                farmer.FarmerSprite?.StopAnimation();
            }
            catch { }
        }

        /// <summary>Sin argumentos devuelve el host; con argumentos busca por id o nombre.</summary>
        private Farmer? ResolveTarget(string[] args)
        {
            if (args.Length == 0)
                return Game1.player;

            string text = string.Join(" ", args).Trim();
            List<Farmer> all = Game1.getAllFarmers().ToList();

            Farmer? found = null;
            if (long.TryParse(text, out long id))
                found = all.FirstOrDefault(f => f.UniqueMultiplayerID == id);
            found ??= all.FirstOrDefault(f => string.Equals(f.Name, text, StringComparison.OrdinalIgnoreCase));

            if (found == null)
                Say($"Jugador no encontrado: '{text}'. Disponibles: {string.Join(", ", all.Where(f => !string.IsNullOrEmpty(f.Name)).Select(f => $"{f.Name} ({f.UniqueMultiplayerID})"))}", LogLevel.Error);
            return found;
        }

        /// <summary>Suma el desplazamiento a <c>armOffset</c> solo durante el dibujo de la herramienta.</summary>
        public static void Prefix_DrawTool(Farmer f, out Vector2 __state)
        {
            __state = Vector2.Zero;
            if (f == null)
                return;

            f.modData.TryGetValue(AgentToolOffset.BodyModDataKey, out string? bodyId);
            (int x, int y) = AgentToolOffset.Get(bodyId, f.FacingDirection);
            if (x == 0 && y == 0)
                return;

            __state = new Vector2(x, y);
            f.armOffset += __state;
        }

        /// <summary>Deshace el desplazamiento aunque el dibujo lance una excepción.</summary>
        public static Exception? Finalizer_DrawTool(Farmer f, Vector2 __state, Exception? __exception)
        {
            if (f != null && __state != Vector2.Zero)
                f.armOffset -= __state;
            return __exception;
        }
    }
}
