using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

    /// <summary>Mensaje del host a un cliente: enlazar (o quitar) la skin nativa completa de 126 cuadros.</summary>
    public class AgentSkinPayload
    {
        public string SkinId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Skins nativas completas (126 cuadros, 24x32 por celda) y apariencia cosmética de los agentes:
    /// <list type="bullet">
    /// <item>comando exclusivo del host <c>p4e_skin &lt;putre|dr4co|off&gt; [jugador]</c> (consola SMAPI y chat) para enlazar un jugador (o al propio host) con una skin;</item>
    /// <item>comandos heredados <c>p4e_dr4co_sprite [on|off] [jugador]</c> y <c>p4e_putre_sprite [on|off] [jugador]</c> enlazados a las skins nativas;</item>
    /// <item>renderizado directo en <c>FarmerRenderer.draw</c> / <c>drawMiniPortrat</c> sin depender de Fashion Sense;</item>
    /// <item>desplaza la herramienta en uso (<see cref="AgentToolOffset"/>) para alinear con las garras del agente.</item>
    /// </list>
    /// </summary>
    public sealed class AgentCosmeticsModule : IModule
    {
        private const string SkinCommand = "p4e_skin";
        private const string Dr4coCommand = "p4e_dr4co_sprite";
        private const string PutreCommand = "p4e_putre_sprite";
        private const string SkinMessageType = "AgentSkin";
        private const string Dr4coMessageType = "AgentBody";
        private const string PutreMessageType = "AgentPutreBody";

        private static ModContext? context;
        private static readonly Dictionary<string, Texture2D> SkinTextureCache = new(StringComparer.OrdinalIgnoreCase);
        private ChatBox? replyChat;

        public string Name => "AgentCosmetics";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;
            try
            {
                var drawFarmerMethod = AccessTools.Method(
                    typeof(FarmerRenderer),
                    nameof(FarmerRenderer.draw),
                    new[]
                    {
                        typeof(SpriteBatch),
                        typeof(FarmerSprite.AnimationFrame),
                        typeof(int),
                        typeof(Rectangle),
                        typeof(Vector2),
                        typeof(Vector2),
                        typeof(float),
                        typeof(int),
                        typeof(Color),
                        typeof(float),
                        typeof(float),
                        typeof(Farmer),
                    });
                if (drawFarmerMethod != null)
                {
                    ctx.Harmony.Patch(
                        original: drawFarmerMethod,
                        prefix: new HarmonyMethod(typeof(AgentCosmeticsModule), nameof(Prefix_DrawFarmer)));
                }
                else
                {
                    ctx.Monitor.Log("[AgentCosmeticsModule] No se encontró FarmerRenderer.draw(12 args) para parches de skin.", LogLevel.Warn);
                }

                var drawMiniPortraitMethod = AccessTools.Method(
                    typeof(FarmerRenderer),
                    nameof(FarmerRenderer.drawMiniPortrat),
                    new[]
                    {
                        typeof(SpriteBatch),
                        typeof(Vector2),
                        typeof(float),
                        typeof(float),
                        typeof(int),
                        typeof(Farmer),
                        typeof(float),
                    });
                if (drawMiniPortraitMethod != null)
                {
                    ctx.Harmony.Patch(
                        original: drawMiniPortraitMethod,
                        prefix: new HarmonyMethod(typeof(AgentCosmeticsModule), nameof(Prefix_DrawMiniPortrait)));
                }

                var drawToolMethod = AccessTools.Method(typeof(Game1), nameof(Game1.drawTool), new[] { typeof(Farmer), typeof(int) });
                if (drawToolMethod != null)
                {
                    ctx.Harmony.Patch(
                        original: drawToolMethod,
                        prefix: new HarmonyMethod(typeof(AgentCosmeticsModule), nameof(Prefix_DrawTool)),
                        finalizer: new HarmonyMethod(typeof(AgentCosmeticsModule), nameof(Finalizer_DrawTool)));
                }

                ctx.Monitor.Log("[AgentCosmeticsModule] Parches de renderizado y herramienta para skins de agente aplicados.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                ctx.Monitor.Log($"[AgentCosmeticsModule] Error al aplicar parches Harmony: {ex}", LogLevel.Error);
            }

            ctx.AddCommand(SkinCommand,
                $"Enlaza una skin nativa (putre|dr4co|off) a un jugador (solo Host; sin jugador aplica al Host): {SkinCommand} <putre|dr4co|off> [jugador_nombre_o_id]",
                OnSkinCommand);
            ctx.AddCommand(Dr4coCommand,
                $"Pone o quita la skin de Dr4co (solo Host; sin jugador aplica al Host): {Dr4coCommand} [on|off] [jugador_nombre_o_id]",
                OnDr4coCommand);
            ctx.AddCommand(PutreCommand,
                $"Pone o quita la skin de Putre (solo Host; sin jugador aplica al Host): {PutreCommand} [on|off] [jugador_nombre_o_id]",
                OnPutreCommand);

            ctx.Helper.Events.GameLoop.SaveLoaded += (_, _) => RegisterChatCommand();
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            ctx.Helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
        }

        private void RegisterChatCommand()
        {
            RegisterSingleChatCommand(SkinCommand, $"Enlaza una skin a un jugador (solo Host): /{SkinCommand} <putre|dr4co|off> [jugador]", OnSkinCommand);
            RegisterSingleChatCommand(Dr4coCommand, $"Pone o quita la skin de Dr4co (solo Host): /{Dr4coCommand} [on|off] [jugador]", OnDr4coCommand);
            RegisterSingleChatCommand(PutreCommand, $"Pone o quita la skin de Putre (solo Host): /{PutreCommand} [on|off] [jugador]", OnPutreCommand);
        }

        private void RegisterSingleChatCommand(string name, string help, Action<string, string[]> handler)
        {
            try
            {
                if (!ChatCommands.Exists(name))
                    ChatCommands.Register(name, (cmd, chat) => RunFromChat(chat, handler, cmd),
                        _ => help, null,
                        mainOnly: true, multiplayerOnly: false, cheatsOnly: false);
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[AgentCosmetics] Error registrando el comando de chat /{name}: {ex.Message}", LogLevel.Warn);
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

        private void OnSkinCommand(string command, string[] args)
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
            if (args.Length == 0 || AgentBodyPreset.ParseSkinId(args[0]) is not string skinId)
            {
                Say($"Uso: {SkinCommand} <{string.Join("|", AgentBodyPreset.AvailableAgentSkins)}|off> [jugador_nombre_o_id]", LogLevel.Warn);
                return;
            }

            Farmer? target = ResolveTarget(args.Skip(1).ToArray());
            if (target == null)
                return;

            AssignAndSyncSkin(target, skinId);
        }

        private void OnDr4coCommand(string command, string[] args)
        {
            HandleAgentToggleCommand(args, AgentBodyPreset.Dr4coSkinId);
        }

        private void OnPutreCommand(string command, string[] args)
        {
            HandleAgentToggleCommand(args, AgentBodyPreset.PutreSkinId);
        }

        private void HandleAgentToggleCommand(string[] args, string skinName)
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

            AssignAndSyncSkin(target, enable ? skinName : string.Empty);
        }

        private void AssignAndSyncSkin(Farmer target, string skinId)
        {
            if (context == null)
                return;

            ApplyAgentSkin(target, skinId);

            bool online = target.IsLocalPlayer || Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == target.UniqueMultiplayerID);
            if (!target.IsLocalPlayer && online)
            {
                context.Helper.Multiplayer.SendMessage(
                    new AgentSkinPayload { SkinId = skinId },
                    SkinMessageType,
                    modIDs: new[] { context.Manifest.UniqueID },
                    playerIDs: new[] { target.UniqueMultiplayerID });
            }

            string label = string.IsNullOrEmpty(skinId)
                ? "desactivada (apariencia vanilla)"
                : $"enlazada a skin '{skinId}'";
            string offlineNote = online ? string.Empty : " (se sincronizará al conectarse)";
            Say($"{target.Name}: skin {label}{offlineNote}.", LogLevel.Info);
        }

        private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
        {
            if (context == null || !Context.IsWorldReady || !Context.IsMainPlayer)
                return;

            Farmer? peerFarmer = Game1.getAllFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == e.Peer.PlayerID);
            if (peerFarmer == null)
                return;

            if (peerFarmer.modData.TryGetValue(AgentBodyPreset.AgentSkinModDataKey, out string? skinId) && !string.IsNullOrWhiteSpace(skinId))
            {
                context.Helper.Multiplayer.SendMessage(
                    new AgentSkinPayload { SkinId = skinId },
                    SkinMessageType,
                    modIDs: new[] { context.Manifest.UniqueID },
                    playerIDs: new[] { e.Peer.PlayerID });
            }
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || e.FromModID != context.Manifest.UniqueID)
                return;
            if (!Context.IsWorldReady || e.FromPlayerID != Game1.MasterPlayer.UniqueMultiplayerID)
                return;   // solo el host puede cambiar el cuerpo

            if (e.Type == SkinMessageType)
            {
                AgentSkinPayload payload = e.ReadAs<AgentSkinPayload>();
                string normalized = AgentBodyPreset.ParseSkinId(payload.SkinId) ?? string.Empty;
                ApplyAgentSkin(Game1.player, normalized);
                context.Monitor.Log($"[AgentCosmetics] El host {(string.IsNullOrEmpty(normalized) ? "desactivó la skin" : $"enlazó la skin '{normalized}'")}.", LogLevel.Info);
            }
            else if (e.Type == Dr4coMessageType)
            {
                AgentBodyPayload payload = e.ReadAs<AgentBodyPayload>();
                ApplyAgentSkin(Game1.player, payload.Enable ? AgentBodyPreset.Dr4coSkinId : string.Empty);
                context.Monitor.Log($"[AgentCosmetics] El host {(payload.Enable ? "activó" : "desactivó")} la skin de Dr4co.", LogLevel.Info);
            }
            else if (e.Type == PutreMessageType)
            {
                AgentPutrePayload payload = e.ReadAs<AgentPutrePayload>();
                ApplyAgentSkin(Game1.player, payload.Enable ? AgentBodyPreset.PutreSkinId : string.Empty);
                context.Monitor.Log($"[AgentCosmetics] El host {(payload.Enable ? "activó" : "desactivó")} la skin de Putre.", LogLevel.Info);
            }
        }

        /// <summary>Enlaza o limpia la skin nativa de 126 cuadros en <c>Farmer.modData</c> y refresca el renderizado.</summary>
        public static void ApplyAgentSkin(Farmer farmer, string skinId)
        {
            if (farmer == null)
                return;

            foreach (string key in AgentBodyPreset.FashionSenseOverrideKeys)
                farmer.modData.Remove(key);

            if (string.IsNullOrWhiteSpace(skinId))
                farmer.modData.Remove(AgentBodyPreset.AgentSkinModDataKey);
            else
                farmer.modData[AgentBodyPreset.AgentSkinModDataKey] = skinId.Trim().ToLowerInvariant();

            MarkFarmerDirty(farmer);
        }

        /// <summary>Aplica el preset de apariencia vanilla de Putre (o restaura el por defecto) al granjero.</summary>
        public static void ApplyPutre(Farmer farmer, bool enable)
        {
            if (farmer == null)
                return;

            foreach (string key in AgentBodyPreset.FashionSenseOverrideKeys)
                farmer.modData.Remove(key);

            var preset = enable ? AgentBodyPreset.PutrePreset : AgentBodyPreset.DefaultVanillaPreset;

            try { farmer.changeGender(preset.IsMale); } catch (Exception ex) { context?.Monitor.Log($"[AgentCosmetics] Error al cambiar género: {ex.Message}", LogLevel.Trace); }
            try { farmer.changeSkinColor(preset.Skin, force: true); farmer.skin.Value = preset.Skin; } catch (Exception ex) { context?.Monitor.Log($"[AgentCosmetics] Error al cambiar piel: {ex.Message}", LogLevel.Trace); }
            try { farmer.changeHairStyle(preset.Hair); farmer.hair.Value = preset.Hair; } catch (Exception ex) { context?.Monitor.Log($"[AgentCosmetics] Error al cambiar pelo: {ex.Message}", LogLevel.Trace); }
            try { farmer.changeShirt(preset.Shirt); farmer.shirt.Value = preset.Shirt; } catch (Exception ex) { context?.Monitor.Log($"[AgentCosmetics] Error al cambiar camisa: {ex.Message}", LogLevel.Trace); }
            try { farmer.changePantStyle(preset.Pants); farmer.pants.Value = preset.Pants; } catch (Exception ex) { context?.Monitor.Log($"[AgentCosmetics] Error al cambiar pantalones: {ex.Message}", LogLevel.Trace); }
            try { farmer.changeAccessory(preset.Accessory); farmer.accessory.Value = preset.Accessory; } catch (Exception ex) { context?.Monitor.Log($"[AgentCosmetics] Error al cambiar accesorio: {ex.Message}", LogLevel.Trace); }

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

            try { farmer.whichPetType = preset.PetType; farmer.whichPetBreed = preset.PetBreed; } catch (Exception ex) { context?.Monitor.Log($"[AgentCosmetics] Error al configurar mascota: {ex.Message}", LogLevel.Trace); }

            try
            {
                if (enable && !string.IsNullOrEmpty(preset.FavoriteThing) && string.IsNullOrEmpty(farmer.favoriteThing.Value))
                    farmer.favoriteThing.Value = preset.FavoriteThing;
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[AgentCosmetics] Error al fijar cosa favorita: {ex.Message}", LogLevel.Trace);
            }

            MarkFarmerDirty(farmer);
            try { farmer.FarmerSprite?.StopAnimation(); } catch { }
        }

        private static void MarkFarmerDirty(Farmer farmer)
        {
            if (context == null || farmer.FarmerRenderer == null)
                return;

            foreach (string field in new[] { "_spriteDirty", "_shirtDirty", "_shoesDirty", "_skinDirty" })
            {
                try { context.Helper.Reflection.GetField<bool>(farmer.FarmerRenderer, field).SetValue(true); }
                catch (Exception ex) { context.Monitor.Log($"[AgentCosmetics] No se pudo marcar {field}: {ex.Message}", LogLevel.Trace); }
            }
        }

        private static Texture2D? LoadSkinTexture(string relativeAssetPath)
        {
            if (context == null)
                return null;

            if (SkinTextureCache.TryGetValue(relativeAssetPath, out Texture2D? cached) && !cached.IsDisposed)
                return cached;

            try
            {
                Texture2D tex = context.Helper.ModContent.Load<Texture2D>(relativeAssetPath);
                SkinTextureCache[relativeAssetPath] = tex;
                return tex;
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[AgentCosmetics] No se pudo cargar la textura '{relativeAssetPath}': {ex.Message}", LogLevel.Warn);
                return null;
            }
        }

        /// <summary>Renderiza directamente la hoja de 126 cuadros (24x32) cuando el jugador tiene una skin nativa enlazada.</summary>
        public static bool Prefix_DrawFarmer(
            FarmerRenderer __instance,
            SpriteBatch b,
            FarmerSprite.AnimationFrame animationFrame,
            int currentFrame,
            Rectangle sourceRect,
            Vector2 position,
            Vector2 origin,
            float layerDepth,
            int facingDirection,
            Color overrideColor,
            float rotation,
            float scale,
            Farmer who)
        {
            if (who == null || __instance == null)
                return true;
            if (!who.modData.TryGetValue(AgentBodyPreset.AgentSkinModDataKey, out string? skinId) || string.IsNullOrWhiteSpace(skinId))
                return true;
            if (currentFrame < 0 || currentFrame >= 126)
                return true;

            var paths = AgentBodyPreset.GetSkinAssetPaths(skinId);
            if (paths == null)
                return true;

            if (currentFrame is 47 or 118 or 119)
                return false;

            bool isCarry = animationFrame.armOffset == 12 || who.IsCarrying();
            string assetPath = isCarry ? paths.Value.CarryAsset : paths.Value.NormalAsset;
            Texture2D? tex = LoadSkinTexture(assetPath);
            if (tex == null)
                return true;

            (int sx, int sy, int sw, int sh) = AgentBodyPreset.GetFrameSourceRect(currentFrame);
            (float ox, float oy) = AgentBodyPreset.GetDrawOriginOffset(skinId, animationFrame.flip);
            Vector2 adjustedOrigin = origin + new Vector2(ox, oy);
            Vector2 drawPos = position + __instance.positionOffset + __instance.rotationAdjustment
                + new Vector2(animationFrame.xOffset * 4f * scale, animationFrame.positionOffset * 4f * scale);
            SpriteEffects effects = animationFrame.flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            b.Draw(
                tex,
                drawPos,
                new Rectangle(sx, sy, sw, sh),
                overrideColor,
                rotation,
                adjustedOrigin,
                4f * scale,
                effects,
                layerDepth);

            return false;
        }

        /// <summary>Dibuja la cabeza del agente en los retratos mini de inventario y UI.</summary>
        public static bool Prefix_DrawMiniPortrait(
            SpriteBatch b,
            Vector2 position,
            float layerDepth,
            float scale,
            int facingDirection,
            Farmer who,
            float alpha)
        {
            if (who == null)
                return true;
            if (!who.modData.TryGetValue(AgentBodyPreset.AgentSkinModDataKey, out string? skinId) || string.IsNullOrWhiteSpace(skinId))
                return true;

            var paths = AgentBodyPreset.GetSkinAssetPaths(skinId);
            if (paths == null)
                return true;

            Texture2D? tex = LoadSkinTexture(paths.Value.NormalAsset);
            if (tex == null)
                return true;

            int frame = facingDirection switch
            {
                0 => 12,
                1 => 6,
                3 => 6,
                _ => 0,
            };
            (int sx, int sy, _, _) = AgentBodyPreset.GetFrameSourceRect(frame);
            SpriteEffects effects = facingDirection == 3 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            int headCropX = string.Equals(skinId, AgentBodyPreset.Dr4coSkinId, StringComparison.OrdinalIgnoreCase) ? 2 : 4;

            b.Draw(
                tex,
                position,
                new Rectangle(sx + headCropX, sy + 2, 16, 15),
                Color.White * alpha,
                0f,
                Vector2.Zero,
                4f * scale,
                effects,
                layerDepth);

            return false;
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

            f.modData.TryGetValue(AgentBodyPreset.AgentSkinModDataKey, out string? skinId);
            f.modData.TryGetValue(AgentToolOffset.BodyModDataKey, out string? bodyId);
            (int x, int y) = AgentToolOffset.GetForPlayer(skinId, bodyId, f.FacingDirection);
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
