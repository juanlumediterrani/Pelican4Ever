using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;

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
        private static int pendingOfflineStaminaGain = 0;
        public const string BedLogoutMessageType = "BedLogoutRequest";

        public string Name => "BedLogout";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            // Escuchamos mensajes multijugador en ambos lados (Host para BedLogout, Cliente para OfflineStaminaRestored)
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            ctx.Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;

            // En el Host gestionamos conexiones y desconexiones
            if (Context.IsMainPlayer)
            {
                ctx.Helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
                ctx.Helper.Events.Multiplayer.PeerDisconnected += OnPeerDisconnected;
                ctx.Helper.Events.GameLoop.SaveLoaded += OnHostSaveLoaded;
            }

            ApplyHarmonyPatches(ctx.Harmony);
        }

        private void OnReturnedToTitle(object? sender, StardewModdingAPI.Events.ReturnedToTitleEventArgs e)
        {
            pendingOfflineStaminaGain = 0;
        }

        private void OnUpdateTicked(object? sender, StardewModdingAPI.Events.UpdateTickedEventArgs e)
        {
            if (pendingOfflineStaminaGain > 0 && Context.IsWorldReady && Game1.player != null)
            {
                int gained = pendingOfflineStaminaGain;
                pendingOfflineStaminaGain = 0;
                ApplyOfflineStaminaToLocalPlayer(gained);
            }
        }

        private static void ApplyOfflineStaminaToLocalPlayer(int gained)
        {
            if (Game1.player == null || gained <= 0)
                return;

            float oldStamina = Game1.player.Stamina;
            Game1.player.Stamina = OfflineStaminaRegenLogic.ApplyGain(Game1.player.Stamina, Game1.player.MaxStamina, gained);
            if (Game1.player.Stamina > 0 && Game1.player.exhausted.Value)
            {
                Game1.player.exhausted.Value = false;
            }

            Game1.addHUDMessage(new HUDMessage(I18n.Get("bed_logout.offline_rest", new { gained }), HUDMessage.newQuest_type));
            context?.Monitor.Log($"[BedLogoutModule] Recuperación de energía offline aplicada al jugador local: +{gained} ({oldStamina:F0} -> {Game1.player.Stamina:F0}/{Game1.player.MaxStamina}).", LogLevel.Info);
        }

        private void OnHostSaveLoaded(object? sender, StardewModdingAPI.Events.SaveLoadedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null || context.Config.OfflineRegenFullBarHours <= 0)
                return;

            if (context.Config.AgentHost || Game1.player == null)
                return;

            long hostId = Game1.player.UniqueMultiplayerID;
            if (!context.WorldState.Players.TryGetValue(hostId, out var hostRecord))
                return;

            DateTimeOffset anchor = hostRecord.OfflineStaminaAnchorUtc != DateTimeOffset.MinValue
                ? hostRecord.OfflineStaminaAnchorUtc
                : hostRecord.LastSeenUtc;

            if (anchor == DateTimeOffset.MinValue)
                return;

            TimeSpan offlineDuration = DateTimeOffset.UtcNow - anchor;
            hostRecord.OfflineStaminaAnchorUtc = DateTimeOffset.MinValue;
            hostRecord.LastSeenUtc = DateTimeOffset.UtcNow;

            if (offlineDuration <= TimeSpan.FromSeconds(30))
                return;

            float oldStamina = Game1.player.Stamina;
            float newStamina = OfflineStaminaRegenLogic.CalculateNewStamina(
                currentStamina: oldStamina,
                maxStamina: Game1.player.MaxStamina,
                offlineDuration: offlineDuration,
                fullBarHours: context.Config.OfflineRegenFullBarHours
            );

            float gained = newStamina - oldStamina;
            if (gained > 0.5f)
            {
                int gainedInt = (int)Math.Round(gained);
                ApplyOfflineStaminaToLocalPlayer(gainedInt);
            }
        }

        private void OnPeerConnected(object? sender, StardewModdingAPI.Events.PeerConnectedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null)
                return;

            // Al conectar un jugador, marcamos su actividad en el día actual
            string playerName = Game1.GetPlayer(e.Peer.PlayerID)?.Name ?? string.Empty;
            var player = context.WorldState.GetOrCreatePlayer(e.Peer.PlayerID, playerName);

            DateTime localNow = Core.Time.ServerTime.LocalNow(context.Config.TimeZoneId);
            TimeSpan dayStart = context.Config.ParsedCalendarDayChangeTime;
            TimeSpan graceStart = dayStart - TimeSpan.FromMinutes(context.Config.EarlyMorningGraceMinutes);
            bool inGraceWindow = localNow.TimeOfDay >= graceStart && localNow.TimeOfDay < dayStart;

            if (!inGraceWindow)
            {
                player.ActiveDayPriorToGrace = Game1.Date.TotalDays;
            }

            // Aplicar recuperación gradual de estamina durante su tiempo desconectado (GAP-07 / Offline Regen)
            ApplyOfflineStaminaRegen(player, e.Peer.PlayerID);

            player.CurrentSessionStartUtc = DateTimeOffset.UtcNow;
            player.LastActiveGameDay = Game1.Date.TotalDays;
            player.LastSeenUtc = DateTimeOffset.UtcNow;
            player.OfflineStaminaAnchorUtc = DateTimeOffset.MinValue;
            player.EnsureDailyCounters(Game1.Date.TotalDays);
            context.Monitor.Log($"[BedLogoutModule] Jugador {e.Peer.PlayerID} conectado. Marcado activo para día {Game1.Date.TotalDays} (MadrugadorEnGracia: {inGraceWindow}).", LogLevel.Trace);

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

        private static void ApplyOfflineStaminaRegen(PlayerRecord player, long playerId)
        {
            if (context == null || context.Config.OfflineRegenFullBarHours <= 0)
                return;

            DateTimeOffset anchor = player.OfflineStaminaAnchorUtc;
            if (anchor == DateTimeOffset.MinValue)
            {
                anchor = player.LastSeenUtc;
            }

            if (anchor == DateTimeOffset.MinValue)
                return;

            TimeSpan offlineDuration = DateTimeOffset.UtcNow - anchor;
            if (offlineDuration <= TimeSpan.FromSeconds(30))
                return;

            Farmer? farmer = Game1.GetPlayer(playerId);
            if (farmer == null)
                return;

            float oldStamina = farmer.Stamina;
            float newStamina = OfflineStaminaRegenLogic.CalculateNewStamina(
                currentStamina: oldStamina,
                maxStamina: farmer.MaxStamina,
                offlineDuration: offlineDuration,
                fullBarHours: context.Config.OfflineRegenFullBarHours
            );

            float gained = newStamina - oldStamina;
            if (gained > 0.5f)
            {
                farmer.Stamina = newStamina;
                context.Monitor.Log($"[BedLogoutModule] Jugador {player.PlayerName} ({playerId}) descansó {offlineDuration.TotalMinutes:F1}m offline: +{gained:F0} energía ({newStamina:F0}/{farmer.MaxStamina}).", LogLevel.Info);

                context.Helper.Multiplayer.SendMessage(
                    message: (int)Math.Round(gained),
                    messageType: "OfflineStaminaRestored",
                    modIDs: new[] { context.Manifest.UniqueID },
                    playerIDs: new[] { playerId }
                );
            }
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
                record.OfflineStaminaAnchorUtc = DateTimeOffset.UtcNow;
                record.CurrentSessionStartUtc = DateTimeOffset.MinValue;
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
            if (context == null || e.FromModID != context.Manifest.UniqueID)
                return;

            // En el Host: registrar descanso en cama
            if (Context.IsMainPlayer && e.Type == BedLogoutMessageType)
            {
                long playerId = e.FromPlayerID;
                BedLogoutPayload? payload = null;
                try
                {
                    payload = e.ReadAs<BedLogoutPayload>();
                }
                catch
                {
                    // Compatibilidad hacia atrás si un cliente envía un formato previo
                }

                int restDay = payload?.GameDay > 0 ? payload.GameDay : Game1.Date.TotalDays;
                var record = context.WorldState.GetOrCreatePlayer(playerId, string.Empty);
                record.RestedOnGameDay = restDay;
                record.LastActiveGameDay = restDay;
                record.OfflineStaminaAnchorUtc = DateTimeOffset.UtcNow;

                Farmer? sleepingFarmer = Game1.GetPlayer(playerId);
                if (sleepingFarmer != null && payload != null && !string.IsNullOrEmpty(payload.LocationName))
                {
                    sleepingFarmer.lastSleepLocation.Value = payload.LocationName;
                    sleepingFarmer.lastSleepPoint.Value = new Point(payload.BedTileX, payload.BedTileY);
                    sleepingFarmer.disconnectLocation.Value = payload.LocationName;
                    sleepingFarmer.disconnectPosition.Value = new Vector2(payload.PositionX, payload.PositionY);
                    sleepingFarmer.disconnectDay.Value = restDay;
                }

                try
                {
                    context.Helper.Data.WriteSaveData(ServerWorldState.SaveDataKey, context.WorldState);
                }
                catch (Exception ex)
                {
                    context.Monitor.Log($"[BedLogoutModule] Error al guardar ServerWorldState tras descanso del jugador {playerId}: {ex}", LogLevel.Warn);
                }
                context.Monitor.Log($"[BedLogoutModule] Descanso registrado para jugador {playerId} en día {restDay} (loc: {payload?.LocationName ?? "n/a"}).", LogLevel.Info);
                return;
            }

            // En el Cliente: aplicar recuperación de energía offline al volver
            if (!Context.IsMainPlayer && e.Type == "OfflineStaminaRestored")
            {
                int gained = e.ReadAs<int>();
                if (gained > 0)
                {
                    if (Context.IsWorldReady && Game1.player != null)
                    {
                        ApplyOfflineStaminaToLocalPlayer(gained);
                    }
                    else
                    {
                        pendingOfflineStaminaGain += gained;
                    }
                }
            }
        }

        private void ApplyHarmonyPatches(Harmony harmony)
        {
            try
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(GameLocation), "startSleep"),
                    prefix: new HarmonyMethod(typeof(BedLogoutModule), nameof(Prefix_StartSleep))
                );

                context?.Monitor.Log("[BedLogoutModule] Harmony patch en GameLocation.startSleep aplicado.", LogLevel.Trace);
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

            record.OfflineStaminaAnchorUtc = DateTimeOffset.UtcNow;

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

        public static bool Prefix_StartSleep(GameLocation __instance)
        {
            // Si estamos en un servidor con Pelican 4Ever activo
            if (Game1.IsMultiplayer && context != null)
            {
                var bedSpot = (__instance as FarmHouse)?.GetPlayerBedSpot() ?? Game1.player.TilePoint;
                if (bedSpot == Point.Zero)
                {
                    bedSpot = new Point(ResidenceInteriorLogic.BedPlacement.TargetX, ResidenceInteriorLogic.BedPlacement.TargetY);
                }
                string locationName = __instance.NameOrUniqueName;

                Game1.player.lastSleepLocation.Value = locationName;
                Game1.player.lastSleepPoint.Value = bedSpot;
                Game1.player.disconnectLocation.Value = locationName;
                Game1.player.disconnectPosition.Value = Game1.player.Position;
                Game1.player.disconnectDay.Value = Game1.Date.TotalDays;

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

                    return false; // Cancela el startSleep() nativo
                }

                var payload = new BedLogoutPayload
                {
                    GameDay = Game1.Date.TotalDays,
                    LocationName = locationName,
                    BedTileX = bedSpot.X,
                    BedTileY = bedSpot.Y,
                    PositionX = Game1.player.Position.X,
                    PositionY = Game1.player.Position.Y
                };

                // Cliente granjero: enviar mensaje al Host para registrar el descanso y salir al título
                context.Helper.Multiplayer.SendMessage(
                    message: payload,
                    messageType: BedLogoutMessageType,
                    modIDs: new[] { context.Manifest.UniqueID },
                    playerIDs: new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );

                Game1.addHUDMessage(new HUDMessage(I18n.Get("bed_logout.disconnecting"), HUDMessage.newQuest_type));

                // Salir al menú principal limpiamente tras 1.5s
                DelayedAction.functionAfterDelay(() =>
                {
                    Game1.ExitToTitle();
                }, 1500);

                return false; // Cancela el startSleep() nativo que pausaría/cerraría la sesión para todos
            }

            return true;
        }
    }
}
