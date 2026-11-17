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
