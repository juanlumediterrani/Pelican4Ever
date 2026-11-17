using System;
using System.IO;
using System.Linq;
using Pelican4Ever.Core.Admin;
using StardewModdingAPI;
using StardewValley;

namespace Pelican4Ever.Framework
{
    /// <summary>
    /// Captura e historia reciente de métricas de guardado y carga (<c>TASK-2506</c>) para
    /// checkpoints intradía y backups de rollover diario.
    /// </summary>
    public static class LoadMetricsTracker
    {
        public static SaveLoadMetricsSnapshot? LastSaveMetrics { get; private set; }
        public static SaveLoadMetricsSnapshot? LastCheckpointMetrics { get; private set; }
        public static SaveLoadMetricsSnapshot? LastRolloverMetrics { get; private set; }

        /// <summary>Calcula los bytes totales actuales en la carpeta de la partida en disco.</summary>
        public static long MeasureCurrentSaveBytes()
        {
            string? savePath = Constants.CurrentSavePath;
            if (string.IsNullOrEmpty(savePath) || !Directory.Exists(savePath))
                return 0L;

            try
            {
                var lengths = Directory.GetFiles(savePath, "*", SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path).Length);
                return LoadInstrumentationLogic.SumByteSizes(lengths);
            }
            catch
            {
                return 0L;
            }
        }

        /// <summary>Cuenta criaturas activas del mod Nature in the Valley cargadas en todas las ubicaciones.</summary>
        public static int CountNitvCreatures()
        {
            if (!Context.IsWorldReady)
                return 0;

            int count = 0;
            try
            {
                Utility.ForEachLocation(location =>
                {
                    if (location?.characters == null)
                        return true;

                    foreach (NPC npc in location.characters)
                    {
                        if (LoadInstrumentationLogic.IsNitvCreatureTypeName(npc?.GetType().FullName))
                            count++;
                    }
                    return true;
                });
            }
            catch
            {
                // Ignorar errores de iteración transitoria para no abortar un checkpoint o rollover.
            }
            return count;
        }

        /// <summary>Captura una instantánea y la registra como última métrica global y por tipo.</summary>
        public static SaveLoadMetricsSnapshot CaptureAndRecord(
            string kind,
            double cloneFarmhandsMs,
            double saveSerializeMs,
            double totalMs,
            double longestTickMs,
            int clonedFarmhandsCount,
            long estimatedFarmhandCloneBytes)
        {
            var snapshot = new SaveLoadMetricsSnapshot
            {
                Kind = kind,
                RecordedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                XmlTotalBytes = MeasureCurrentSaveBytes(),
                CloneFarmhandsMs = Math.Round(cloneFarmhandsMs, 2),
                SaveSerializeMs = Math.Round(saveSerializeMs, 2),
                TotalMs = Math.Round(totalMs, 2),
                LongestTickMs = Math.Round(longestTickMs, 2),
                ClonedFarmhandsCount = clonedFarmhandsCount,
                EstimatedFarmhandCloneBytes = estimatedFarmhandCloneBytes,
                TotalNitvCreatures = CountNitvCreatures()
            };

            LastSaveMetrics = snapshot;
            if (string.Equals(kind, "checkpoint", StringComparison.OrdinalIgnoreCase))
                LastCheckpointMetrics = snapshot;
            else if (string.Equals(kind, "rollover", StringComparison.OrdinalIgnoreCase))
                LastRolloverMetrics = snapshot;

            return snapshot;
        }
    }
}
