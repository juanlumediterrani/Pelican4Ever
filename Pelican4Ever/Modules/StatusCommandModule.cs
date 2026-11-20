using System;
using System.Linq;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.Time;
using StardewModdingAPI;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>Comando de consola <c>p4e_status</c> con el estado del servidor y del reloj.</summary>
    public sealed class StatusCommandModule : Framework.IModule
    {
        private Framework.ModContext context = null!;

        public string Name => "StatusCommand";

        public Framework.ModuleSide Side => Framework.ModuleSide.Both;

        public void Register(Framework.ModContext context)
        {
            this.context = context;
            context.AddCommand("p4e_status", "Muestra el estado de Pelican 4Ever (reloj, calendario, jugadores).", OnStatus);
            context.AddCommand("p4e_load_metrics", "Muestra métricas de guardado/serialización y criaturas NITV (TASK-2506).", OnLoadMetrics);
            context.AddCommand("p4e_concurrency", "Muestra o controla telemetría de concurrencia y aforo por mapa: p4e_concurrency [status|on|off|reset|occupancy]", OnConcurrency);
        }

        private void OnConcurrency(string command, string[] args)
        {
            IMonitor log = context.Monitor;
            string arg = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "status";
            if (arg == "on")
            {
                Framework.ConcurrencyMetricsTracker.SetEnabled(true);
                log.Log("[Concurrency] Instrumentación de concurrencia ACTIVADA.", LogLevel.Info);
                return;
            }
            if (arg == "off")
            {
                Framework.ConcurrencyMetricsTracker.SetEnabled(false);
                log.Log("[Concurrency] Instrumentación de concurrencia DESACTIVADA.", LogLevel.Info);
                return;
            }
            if (arg == "reset")
            {
                Framework.ConcurrencyMetricsTracker.Reset();
                log.Log("[Concurrency] Ventana de telemetría reiniciada.", LogLevel.Info);
                return;
            }

            var snap = Framework.ConcurrencyMetricsTracker.GetLatestOrCurrentSnapshot();
            log.Log(
                $"=== Concurrencia Pelican 4Ever (Extended={context.Config.ExtendedConcurrency}, Limite={context.Config.EffectiveMaxConcurrentPlayers}, " +
                $"DefaultMapCap={context.Config.DefaultLocationCapacity}, FarmerDeltaPeriod={context.Config.FarmerDeltaBroadcastPeriod}, " +
                $"Instrumentación={(Framework.ConcurrencyMetricsTracker.IsEnabled ? "ON" : "OFF")}) ===",
                LogLevel.Info);

            if (arg != "occupancy")
            {
                log.Log(
                    $"  Ventana ({snap.WindowDurationSeconds:F1}s, clientes={snap.ConnectedClientsCount}): " +
                    $"tick[p50={snap.TickMedianMs:F2}ms, p95={snap.TickP95Ms:F2}ms, max={snap.TickMaxMs:F2}ms], " +
                    $"cadencia[p50={snap.TickCadenceMedianMs:F2}ms, p95={snap.TickCadenceP95Ms:F2}ms], " +
                    $"overhead={snap.InstrumentationOverheadMs:F2}ms ({snap.InstrumentationOverheadPercent:F2}%)",
                    LogLevel.Info);
                log.Log(
                    $"  Red: payload={snap.PayloadTotalMessages} msgs ({snap.PayloadTotalBytes} B, {snap.PayloadBytesPerSecond:F0} B/s) | " +
                    $"transporteUp={snap.TransportBytesUp} B ({snap.TransportBytesUpPerSecond:F0} B/s) | " +
                    $"type0={snap.Type0FarmerDeltaBytes} B, type6={snap.Type6LocationDeltaBytes} B, type3={snap.Type3LocationIntroBytes} B | " +
                    $"filtrados={snap.RootSync.ResidenceDeltasFilteredOutCount}",
                    LogLevel.Info);
                log.Log(
                    $"  Raíces: updateRoots={snap.RootSync.UpdateRootsCalls} ({snap.RootSync.UpdateRootsTotalMs:F1}ms, max={snap.RootSync.UpdateRootsMaxMs:F2}ms), " +
                    $"locDeltas={snap.RootSync.BroadcastLocationDeltasCalls} ({snap.RootSync.BroadcastLocationDeltasTotalMs:F1}ms, locs={snap.RootSync.LocationsSerializedCount}, bytes={snap.RootSync.LocationsSerializedBytes}) | " +
                    $"CPU={snap.CpuUsagePercent:F1}%, RSS={snap.WorkingSetBytes / (1024 * 1024)} MiB",
                    LogLevel.Info);
            }

            if (snap.LocationOccupancy.Count > 0)
            {
                string occ = string.Join(", ", snap.LocationOccupancy.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}"));
                log.Log($"  Ocupación por mapa: {occ}", LogLevel.Info);
            }
        }

        private void OnLoadMetrics(string command, string[] args)
        {
            IMonitor log = context.Monitor;
            long currentBytes = Framework.LoadMetricsTracker.MeasureCurrentSaveBytes();
            int currentNitv = Framework.LoadMetricsTracker.CountNitvCreatures();
            log.Log($"=== Métricas de carga Pelican 4Ever (actual: xmlBytes={currentBytes}, nitvCreatures={currentNitv}, farmhands={Game1.otherFarmers.Count}) ===", LogLevel.Info);
            LogMetricsSnapshot(log, "Último checkpoint", Framework.LoadMetricsTracker.LastCheckpointMetrics);
            LogMetricsSnapshot(log, "Último rollover", Framework.LoadMetricsTracker.LastRolloverMetrics);
        }

        private static void LogMetricsSnapshot(IMonitor log, string label, Pelican4Ever.Core.Admin.SaveLoadMetricsSnapshot? snap)
        {
            if (snap == null)
            {
                log.Log($"  {label}: sin datos registrados en esta sesión.", LogLevel.Info);
                return;
            }

            log.Log(
                $"  {label} ({snap.RecordedAtUtc}): xmlBytes={snap.XmlTotalBytes}, cloneMs={snap.CloneFarmhandsMs:F1} " +
                $"(farmhands={snap.ClonedFarmhandsCount}, estCloneBytes={snap.EstimatedFarmhandCloneBytes}), " +
                $"serializeMs={snap.SaveSerializeMs:F1}, totalMs={snap.TotalMs:F1}, longestTickMs={snap.LongestTickMs:F1}, nitvCreatures={snap.TotalNitvCreatures}",
                LogLevel.Info);
        }

        private void OnStatus(string command, string[] args)
        {
            var config = context.Config;
            DateTime now = ServerTime.LocalNow(config.TimeZoneId);
            ClockReading clock = GameClock.Read(now.TimeOfDay, config.ParsedDayStart, config.ParsedNightHoldStart);
            var calendar = new ServerCalendar(config.ParsedCalendarEpoch, config.ParsedCalendarDayChangeTime);

            IMonitor log = context.Monitor;
            log.Log($"=== {context.Manifest.Name} {context.Manifest.Version} ===", LogLevel.Info);
            log.Log($"Hora real: {now:yyyy-MM-dd HH:mm:ss} ({(string.IsNullOrEmpty(config.TimeZoneId) ? "local" : config.TimeZoneId)})", LogLevel.Info);
            log.Log($"Reloj esperado: {clock.TimeOfDay}{(clock.IsNightHold ? " (Madrugada: reloj congelado)" : string.Empty)}", LogLevel.Info);

            try
            {
                log.Log($"Fecha esperada: {calendar.GetDate(now)}", LogLevel.Info);
            }
            catch (ArgumentOutOfRangeException)
            {
                log.Log($"Fecha esperada: anterior a la época ({config.CalendarEpoch}).", LogLevel.Info);
            }

            if (!Context.IsWorldReady)
            {
                log.Log("Partida no cargada.", LogLevel.Info);
                return;
            }

            log.Log($"Partida: {Game1.Date.TotalDays} días (Año {Game1.year}, {Game1.currentSeason} {Game1.dayOfMonth}), timeOfDay={Game1.timeOfDay}", LogLevel.Info);
            log.Log($"Host: {Context.IsMainPlayer} | Host dedicado nativo: {Game1.HasDedicatedHost}", LogLevel.Info);

            if (!Context.IsMainPlayer)
                return;

            var state = context.WorldState;
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            var queueSnapshot = ClientSecurityModule.Admission.GetQueueSnapshot();
            var reservations = ClientSecurityModule.Admission.GetReservationsSnapshot(nowUtc);
            long hostId = Game1.MasterPlayer?.UniqueMultiplayerID ?? 0;
            int onlineClients = Game1.getOnlineFarmers().Count(f => f != null && f.UniqueMultiplayerID != hostId);

            log.Log($"Conectados (clientes): {onlineClients}/{config.EffectiveMaxConcurrentPlayers} (Extended={config.ExtendedConcurrency}) | Cola: {queueSnapshot.Count}/{config.AdmissionQueueCapacity} | Reservas: {reservations.Count} | Expulsiones por inactividad: {IdleKickModule.TotalIdleKicks}", LogLevel.Info);
            if (queueSnapshot.Count > 0)
            {
                foreach (var q in queueSnapshot)
                {
                    string preview = q.IdentityKey.Length > 12 ? q.IdentityKey[..12] + "..." : q.IdentityKey;
                    int waitSec = Math.Max(0, (int)(nowUtc - q.EnqueuedAtUtc).TotalSeconds);
                    log.Log($"  [Cola #{q.Position}] {preview} (espera: {waitSec}s)", LogLevel.Info);
                }
            }
            if (reservations.Count > 0)
            {
                foreach (var r in reservations)
                {
                    string preview = r.IdentityKey.Length > 12 ? r.IdentityKey[..12] + "..." : r.IdentityKey;
                    int remSec = Math.Max(0, (int)(r.ExpiresAtUtc - nowUtc).TotalSeconds);
                    log.Log($"  [Reserva] {preview} (restan: {remSec}s)", LogLevel.Info);
                }
            }
            log.Log($"Jugadores registrados: {state.Players.Count} | Puertas asignadas: {state.Doors.Count}/{FarmSlotModule.RegisteredDoors.Count}", LogLevel.Info);
            int today = Game1.Date.TotalDays;
            foreach (PlayerRecord player in state.Players.Values)
            {
                bool online = Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == player.UniqueMultiplayerID);
                string claim = string.IsNullOrEmpty(player.OwnerToken) ? "sin reclamar" : PlayerAuthLogic.MaskToken(player.OwnerToken);
                log.Log($"  - {player.PlayerName} ({player.UniqueMultiplayerID}): {SleepPolicy.GetState(player, today, online)}, granja={player.FarmSlotId ?? "—"}, token={claim}", LogLevel.Info);
            }

            log.Log($"Residencias registradas ({FarmSlotModule.RegisteredDoors.Count} disponibles):", LogLevel.Info);
            foreach (var door in FarmSlotModule.RegisteredDoors)
            {
                string status = state.Doors.TryGetValue(door.DoorId, out var ass)
                    ? $"Ocupada por {(state.Players.TryGetValue(ass.OwnerId, out var p) ? p.PlayerName : ass.OwnerId.ToString())} [Tipo: {ass.FarmType}, Acceso: {ass.Policy}]"
                    : "Libre";
                log.Log($"  [{door.DoorId}] {door.LocationName ?? "Town"} ({door.TownTileX},{door.TownTileY}) -> {door.DefaultSlotId}: {status}", LogLevel.Info);
            }
        }
    }
}
