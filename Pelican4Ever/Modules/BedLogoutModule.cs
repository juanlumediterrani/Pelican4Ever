using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Permite al jugador meterse en la cama, seleccionar "Descansar y Desconectar",
    /// registrar su estado Rested en el Host y salir al menú sin bloquear al resto (TASK-102 / ADR-0004).
    /// En modo <c>AgentHost</c> (TASK-804), el anfitrión registra su descanso sin desconectarse.
    /// </summary>
    public sealed class BedLogoutModule : IModule
    {
        private static ModContext? context;
        public const string BedLogoutMessageType = "BedLogoutRequest";

        public string Name => "BedLogout";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            // En el Host escuchamos mensajes de clientes pidiendo desconexión tras descansar en la cama
            if (Context.IsMainPlayer)
            {
                ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
                ctx.Helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
                ctx.Helper.Events.Multiplayer.PeerDisconnected += OnPeerDisconnected;
            }

            ApplyHarmonyPatches(ctx.Harmony);
        }

        private void OnPeerConnected(object? sender, StardewModdingAPI.Events.PeerConnectedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null)
                return;

            // Al conectar un jugador, marcamos su actividad en el día actual
            string playerName = Game1.GetPlayer(e.Peer.PlayerID)?.Name ?? string.Empty;
            var player = context.WorldState.GetOrCreatePlayer(e.Peer.PlayerID, playerName);
            player.LastActiveGameDay = Game1.Date.TotalDays;
            player.LastSeenUtc = DateTimeOffset.UtcNow;
            player.EnsureDailyCounters(Game1.Date.TotalDays);
            context.Monitor.Log($"[BedLogoutModule] Jugador {e.Peer.PlayerID} conectado. Marcado activo para día {Game1.Date.TotalDays}.", LogLevel.Trace);

            AdminApiModule.EventLog.Append(
                HostEventTypes.PlayerConnected,
                Game1.Date.TotalDays,
                Game1.timeOfDay,
                $"Jugador conectado: {(string.IsNullOrWhiteSpace(player.PlayerName) ? e.Peer.PlayerID.ToString() : player.PlayerName)}",
                new Dictionary<string, object?>
                {
                    ["playerId"] = e.Peer.PlayerID,
                    ["playerName"] = player.PlayerName
                });
        }

        private void OnPeerDisconnected(object? sender, StardewModdingAPI.Events.PeerDisconnectedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null)
                return;

            string playerName = Game1.GetPlayer(e.Peer.PlayerID)?.Name
                ?? (context.WorldState.Players.TryGetValue(e.Peer.PlayerID, out var rec) ? rec.PlayerName : string.Empty);

            if (context.WorldState.Players.TryGetValue(e.Peer.PlayerID, out var record))
            {
                record.LastSeenUtc = DateTimeOffset.UtcNow;
            }

            AdminApiModule.EventLog.Append(
                HostEventTypes.PlayerDisconnected,
                Game1.Date.TotalDays,
                Game1.timeOfDay,
                $"Jugador desconectado: {(string.IsNullOrWhiteSpace(playerName) ? e.Peer.PlayerID.ToString() : playerName)}",
                new Dictionary<string, object?>
                {
                    ["playerId"] = e.Peer.PlayerID,
                    ["playerName"] = playerName
                });
        }

        private void OnModMessageReceived(object? sender, StardewModdingAPI.Events.ModMessageReceivedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null)
                return;

            if (e.FromModID == context.Manifest.UniqueID && e.Type == BedLogoutMessageType)
            {
                long playerId = e.FromPlayerID;
                var record = context.WorldState.GetOrCreatePlayer(playerId, string.Empty);
                record.RestedOnGameDay = Game1.Date.TotalDays;
                record.LastActiveGameDay = Game1.Date.TotalDays;
                context.Monitor.Log($"[BedLogoutModule] Descanso registrado para jugador {playerId} en día {Game1.Date.TotalDays}.", LogLevel.Info);
            }
        }

        private void ApplyHarmonyPatches(Harmony harmony)
        {
            try
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(GameLocation), nameof(GameLocation.answerDialogueAction)),
                    prefix: new HarmonyMethod(typeof(BedLogoutModule), nameof(Prefix_AnswerDialogueAction))
                );

                context?.Monitor.Log("[BedLogoutModule] Harmony patch en GameLocation.answerDialogueAction aplicado.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[BedLogoutModule] Error al aplicar parches Harmony: {ex}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Registra el descanso del jugador anfitrión (desde la cama o vía <c>POST /api/v1/host/rest</c>) (TASK-804).
        /// </summary>
        public static HostRestResult RestHostPlayer(bool triggeredFromApi = false)
        {
            if (context == null || !Context.IsMainPlayer)
            {
                return new HostRestResult(false, -1, false, "Solo el anfitrión con una partida cargada puede registrar su descanso.");
            }

            int today = Game1.Date.TotalDays;
            var record = context.WorldState.GetOrCreatePlayer(Game1.player.UniqueMultiplayerID, Game1.player.Name);
            bool stayOnline = triggeredFromApi || context.Config.AgentHost;

            HostRestResult result = AgentHostLogic.ApplyRest(
                record,
                today,
                isHostPlayer: true,
                agentHostEnabled: stayOnline);

            AdminApiModule.EventLog.Append(
                HostEventTypes.HostRested,
                today,
                Game1.timeOfDay,
                $"Descanso del anfitrión registrado para el día {today} (sin desconexión: {!result.ShouldDisconnectSession}).",
                new Dictionary<string, object?>
                {
                    ["playerId"] = Game1.player.UniqueMultiplayerID,
                    ["playerName"] = Game1.player.Name,
                    ["gameDay"] = today,
                    ["agentHost"] = context.Config.AgentHost,
                    ["triggeredFromApi"] = triggeredFromApi
                });

            context.Monitor.Log($"[BedLogoutModule] {result.Message} (Día {today}).", LogLevel.Info);
            return result;
        }

        public static bool Prefix_AnswerDialogueAction(string questionAndAnswer, ref bool __result)
        {
            if (questionAndAnswer == "Sleep_Yes")
            {
                // Si estamos en un servidor con Pelican 4Ever activo
                if (Game1.IsMultiplayer && context != null)
                {
                    // Si es el propio Host quien duerme en la cama (TASK-804)
                    if (Context.IsMainPlayer)
                    {
                        HostRestResult hostRest = RestHostPlayer(triggeredFromApi: false);
                        Game1.addHUDMessage(new HUDMessage(hostRest.Message, HUDMessage.newQuest_type));

                        if (hostRest.ShouldDisconnectSession)
                        {
                            DelayedAction.functionAfterDelay(() =>
                            {
                                Game1.ExitToTitle();
                            }, 1500);
                        }

                        __result = true;
                        return false; // Cancela el startSleep() nativo
                    }

                    // Cliente granjero: enviar mensaje al Host para registrar el descanso y salir al título
                    context.Helper.Multiplayer.SendMessage(
                        message: Game1.Date.TotalDays,
                        messageType: BedLogoutMessageType,
                        modIDs: new[] { context.Manifest.UniqueID },
                        playerIDs: new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                    );

                    Game1.addHUDMessage(new HUDMessage("Descanso registrado. Desconectando de forma segura...", HUDMessage.newQuest_type));

                    // Salir al menú principal limpiamente tras 1.5s
                    DelayedAction.functionAfterDelay(() =>
                    {
                        Game1.ExitToTitle();
                    }, 1500);

                    __result = true;
                    return false; // Cancela el startSleep() nativo que pausaría/cerraría la sesión para todos
                }
            }

            return true;
        }
    }
}
