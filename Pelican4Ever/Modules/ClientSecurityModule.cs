using System;
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
using StardewValley.Network;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Módulo de seguridad y protección contra usurpación de personajes (Opción A).
    /// Vincula automáticamente cada peón al token único de la máquina/cliente propietario.
    /// </summary>
    public sealed class ClientSecurityModule : IModule
    {
        private static ModContext? context;

        public string Name => "ClientSecurity";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            // Inicializar o verificar identidad del cliente local
            ClientIdentity.Initialize(ctx.Helper);

            // Sincronización en el host al cargar partida
            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;

            // Comandos de consola de administración en el host
            ctx.Helper.ConsoleCommands.Add("p4e_claim_list", "Lista los peones registrados y su estado de posesión/token.", OnConsoleClaimList);
            ctx.Helper.ConsoleCommands.Add("p4e_claim_reset", "Libera la posesión de un peón para que pueda ser reclamado de nuevo: p4e_claim_reset <nombre_o_id>", OnConsoleClaimReset);

            ApplyHarmonyPatches(ctx.Harmony);
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null)
                return;

            SyncServerTokensFromWorldState();
        }

        /// <summary>
        /// Sincroniza serverFarmer.userID.Value con PlayerRecord.OwnerToken en el host.
        /// Necesario para que sendAvailableFarmhands transmita el token en la red nativa.
        /// </summary>
        public static void SyncServerTokensFromWorldState()
        {
            if (context == null || !Context.IsMainPlayer || Game1.netWorldState?.Value?.farmhandData == null)
                return;

            foreach (var (playerId, playerRecord) in context.WorldState.Players)
            {
                if (!string.IsNullOrWhiteSpace(playerRecord.OwnerToken)
                    && Game1.netWorldState.Value.farmhandData.TryGetValue(playerId, out Farmer? f)
                    && f != null)
                {
                    f.userID.Value = playerRecord.OwnerToken;
                }
            }
        }

        private void OnConsoleClaimList(string command, string[] args)
        {
            if (context == null) return;

            if (!Context.IsMainPlayer)
            {
                context.Monitor.Log("Este comando solo puede ejecutarse en el Host.", LogLevel.Warn);
                return;
            }

            var players = context.WorldState.Players.Values.OrderBy(p => p.UniqueMultiplayerID).ToList();
            context.Monitor.Log($"=== Posesión de peones ({players.Count} registrados) ===", LogLevel.Info);
            foreach (var p in players)
            {
                bool isClaimed = PlayerAuthLogic.IsClaimed(p);
                string claimText = isClaimed
                    ? $"Reclamado [Token: {PlayerAuthLogic.MaskToken(p.OwnerToken)}]"
                    : "Libre / Sin reclamar";

                context.Monitor.Log($"  [{p.UniqueMultiplayerID}] {p.PlayerName} (Granja: {p.FarmSlotId ?? "ninguna"}): {claimText}", LogLevel.Info);
            }
        }

        private void OnConsoleClaimReset(string command, string[] args)
        {
            if (context == null) return;

            if (!Context.IsMainPlayer)
            {
                context.Monitor.Log("Este comando solo puede ejecutarse en el Host.", LogLevel.Warn);
                return;
            }

            if (args.Length == 0)
            {
                context.Monitor.Log("Uso: p4e_claim_reset <nombre_o_id>", LogLevel.Warn);
                return;
            }

            string target = string.Join(" ", args).Trim();
            PlayerRecord? found = null;

            if (long.TryParse(target, out long targetId))
            {
                context.WorldState.Players.TryGetValue(targetId, out found);
            }

            if (found == null)
            {
                found = context.WorldState.Players.Values.FirstOrDefault(p =>
                    string.Equals(p.PlayerName, target, StringComparison.OrdinalIgnoreCase));
            }

            if (found == null)
            {
                context.Monitor.Log($"No se encontró ningún jugador registrado con '{target}'.", LogLevel.Error);
                return;
            }

            PlayerAuthLogic.ResetClaim(found);

            // También limpiar el NetString en farmhandData si existe
            if (Game1.netWorldState?.Value?.farmhandData != null
                && Game1.netWorldState.Value.farmhandData.TryGetValue(found.UniqueMultiplayerID, out Farmer? f)
                && f != null)
            {
                f.userID.Value = string.Empty;
            }

            context.Monitor.Log($"Posesión del peón {found.UniqueMultiplayerID} ({found.PlayerName}) restablecida. Ahora está libre para ser reclamado.", LogLevel.Info);
        }

        private void ApplyHarmonyPatches(Harmony harmony)
        {
            try
            {
                // 1. Inyección de token en el cliente al presentarse al servidor
                harmony.Patch(
                    original: AccessTools.Method(typeof(Client), nameof(Client.sendPlayerIntroduction)),
                    prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_Client_SendPlayerIntroduction))
                );

                // 2. Override de getUserID en Client y sus subclases para conexiones IP directa, Steam y GOG Galaxy
                void PatchGetUserID(Type? clientType)
                {
                    if (clientType == null) return;
                    var method = AccessTools.Method(clientType, "getUserID");
                    if (method != null)
                    {
                        harmony.Patch(
                            original: method,
                            prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_Client_GetUserID))
                        );
                    }
                }

                PatchGetUserID(typeof(Client));
                PatchGetUserID(typeof(LidgrenClient));
                PatchGetUserID(AccessTools.TypeByName("StardewValley.SDKs.Steam.SteamNetClient"));
                PatchGetUserID(AccessTools.TypeByName("StardewValley.SDKs.GogGalaxy.GalaxyNetClient"));

                // 3. Sincronización proactiva de tokens antes de enviar la lista de peones al cliente
                harmony.Patch(
                    original: AccessTools.Method(typeof(GameServer), nameof(GameServer.sendAvailableFarmhands)),
                    prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_SendAvailableFarmhands))
                );

                // 4. Validación y reclamo autoritativo en el host (GameServer.checkFarmhandRequest)
                harmony.Patch(
                    original: AccessTools.Method(typeof(GameServer), nameof(GameServer.checkFarmhandRequest)),
                    prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_CheckFarmhandRequest))
                );

                // 5. Bloqueo visual y control de pertenencia de ranuras en FarmhandMenu
                harmony.Patch(
                    original: AccessTools.Constructor(typeof(FarmhandMenu.FarmhandSlot), new[] { typeof(FarmhandMenu), typeof(Farmer) }),
                    postfix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Postfix_FarmhandSlot_Ctor))
                );

                // 6. Etiqueta visual "[De otro jugador]" en las ranuras bloqueadas
                harmony.Patch(
                    original: AccessTools.Method(typeof(FarmhandMenu.FarmhandSlot), "drawSlotName"),
                    postfix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Postfix_FarmhandSlot_DrawSlotName))
                );

                // 7. Ordenación inteligente del menú de peones (personajes propios primero)
                harmony.Patch(
                    original: AccessTools.Constructor(typeof(FarmhandMenu), new[] { typeof(Client) }),
                    postfix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Postfix_FarmhandMenu_Ctor))
                );

                context?.Monitor.Log("[ClientSecurityModule] Parches Harmony de seguridad y posesión aplicados correctamente.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[ClientSecurityModule] Error al aplicar parches Harmony de seguridad: {ex}", LogLevel.Error);
            }
        }

        // ─────────────────────────── Parches Harmony ───────────────────────────

        public static void Prefix_Client_SendPlayerIntroduction(Client __instance)
        {
            string token = ClientIdentity.CurrentToken;
            if (!string.IsNullOrWhiteSpace(token) && Game1.player != null)
            {
                Game1.player.userID.Value = token;
            }
        }

        public static bool Prefix_Client_GetUserID(ref string __result)
        {
            string token = ClientIdentity.CurrentToken;
            if (!string.IsNullOrWhiteSpace(token))
            {
                __result = token;
                return false;
            }
            return true;
        }

        public static bool Prefix_LidgrenClient_GetUserID(ref string __result) => Prefix_Client_GetUserID(ref __result);

        public static void Prefix_SendAvailableFarmhands()
        {
            SyncServerTokensFromWorldState();
        }

        public static bool Prefix_CheckFarmhandRequest(
            GameServer __instance,
            ref string userId,
            string connectionId,
            NetFarmerRoot farmer,
            Action<OutgoingMessage> sendMessage)
        {
            if (farmer?.Value == null)
            {
                __instance.sendAvailableFarmhands(userId, connectionId, sendMessage);
                return false;
            }

            long farmhandId = farmer.Value.UniqueMultiplayerID;
            if (!Game1.netWorldState.Value.farmhandData.TryGetValue(farmhandId, out Farmer? serverFarmer) || serverFarmer == null)
            {
                context?.Monitor.Log($"[ClientSecurity] El peón {farmhandId} pedido por la conexión {connectionId} ya no existe; se reenvía la lista.", LogLevel.Warn);
                __instance.sendAvailableFarmhands(userId, connectionId, sendMessage);
                return false;
            }

            if (context != null)
            {
                string incomingToken = farmer.Value.userID.Value ?? string.Empty;
                var playerRecord = context.WorldState.GetOrCreatePlayer(farmhandId, serverFarmer.Name);

                // Si el peón tiene OwnerToken en WorldState (la fuente de verdad), sincronizar serverFarmer.userID
                if (!string.IsNullOrEmpty(playerRecord.OwnerToken))
                {
                    serverFarmer.userID.Value = playerRecord.OwnerToken;
                }

                var authResult = PlayerAuthLogic.ValidateOrClaim(playerRecord, incomingToken);
                if (authResult == AuthResult.DeniedMismatch || authResult == AuthResult.DeniedNoToken)
                {
                    context.Monitor.Log(
                        $"[ClientSecurity] RECHAZADO: Conexión {connectionId} intentó entrar al peón {farmhandId} ('{serverFarmer.Name}') " +
                        $"con token '{PlayerAuthLogic.MaskToken(incomingToken)}', pero pertenece a '{PlayerAuthLogic.MaskToken(playerRecord.OwnerToken)}'.",
                        LogLevel.Warn);

                    __instance.sendAvailableFarmhands(userId, connectionId, sendMessage);
                    return false;
                }

                if (authResult == AuthResult.Claimed)
                {
                    serverFarmer.userID.Value = incomingToken;
                    context.Monitor.Log(
                        $"[ClientSecurity] Peón {farmhandId} ('{serverFarmer.Name}') reclamado y vinculado al token {PlayerAuthLogic.MaskToken(incomingToken)}.",
                        LogLevel.Info);
                }

                // Propagar el token al argumento userId
                userId = incomingToken;
            }

            return true;
        }

        public static void Postfix_FarmhandSlot_Ctor(FarmhandMenu.FarmhandSlot __instance, Farmer farmer)
        {
            string myToken = ClientIdentity.CurrentToken;
            if (farmer != null)
            {
                string slotToken = farmer.userID.Value ?? string.Empty;
                bool belongsToAnother = PlayerAuthLogic.BelongsToAnotherClient(slotToken, myToken);

                // Sobrescribe el valor calculado por el juego nativo (que comparó contra GetUserID() de Steam/GOG)
                AccessTools.Field(typeof(FarmhandMenu.FarmhandSlot), "_belongsToAnotherPlayer")?.SetValue(__instance, belongsToAnother);
            }
        }

        public static void Postfix_FarmhandSlot_DrawSlotName(FarmhandMenu.FarmhandSlot __instance, SpriteBatch b, int i)
        {
            if (__instance.BelongsToAnotherPlayer())
            {
                var menu = AccessTools.Field(typeof(FarmhandMenu.FarmhandSlot), "menu")?.GetValue(__instance) as FarmhandMenu;
                if (menu != null && i < menu.slotButtons.Count)
                {
                    var btn = menu.slotButtons[i];
                    Vector2 pos = new Vector2(btn.bounds.X + 128 + 36, btn.bounds.Y + 68);
                    Utility.drawTextWithShadow(b, "[De otro jugador]", Game1.smallFont, pos, Color.IndianRed);
                }
            }
        }

        public static void Postfix_FarmhandMenu_Ctor(FarmhandMenu __instance)
        {
            if (__instance.MenuSlots == null || __instance.MenuSlots.Count <= 1)
                return;

            __instance.MenuSlots.Sort((a, b) =>
            {
                int Score(LoadGameMenu.MenuSlot slot)
                {
                    if (slot is FarmhandMenu.FarmhandSlot fs)
                    {
                        if (fs.BelongsToAnotherPlayer())
                            return 2; // Partidas de otros al final
                        if (fs.Farmer.isCustomized.Value)
                            return 0; // Mis personajes primero
                        return 1; // Hueco libre nuevo en medio
                    }
                    return 1;
                }

                return Score(a).CompareTo(Score(b));
            });
        }
    }
}
