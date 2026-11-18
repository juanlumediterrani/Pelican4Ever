using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.Admin
{
    /// <summary>Instantánea de métricas de guardado y carga (TASK-2506).</summary>
    public sealed class SaveLoadMetricsSnapshot
    {
        public string Kind { get; set; } = string.Empty;
        public string RecordedAtUtc { get; set; } = string.Empty;
        public long XmlTotalBytes { get; set; }
        public double CloneFarmhandsMs { get; set; }
        public double SaveSerializeMs { get; set; }
        public double TotalMs { get; set; }
        public double LongestTickMs { get; set; }
        public int ClonedFarmhandsCount { get; set; }
        public long EstimatedFarmhandCloneBytes { get; set; }
        public int TotalNitvCreatures { get; set; }
    }

    /// <summary>Helpers puros de instrumentación de carga para el banco de 8–16 jugadores (TASK-2506).</summary>
    public static class LoadInstrumentationLogic
    {
        public static bool IsNitvCreatureTypeName(string? fullTypeName)
        {
            if (string.IsNullOrWhiteSpace(fullTypeName))
                return false;

            return fullTypeName.StartsWith("NatureInTheValley.", StringComparison.Ordinal)
                && fullTypeName.Contains("Creature", StringComparison.Ordinal);
        }

        /// <summary>
        /// Estima de forma determinista el tamaño XML aproximado de un farmhand clonado según inventario, correo y misiones.
        /// </summary>
        public static long EstimateFarmhandBytes(int itemCount, int mailCount, int questCount)
        {
            int safeItems = Math.Max(0, itemCount);
            int safeMail = Math.Max(0, mailCount);
            int safeQuests = Math.Max(0, questCount);
            return 16384L + (safeItems * 512L) + (safeMail * 64L) + (safeQuests * 256L);
        }

        /// <summary>Suma longitudes de archivo válidas ignorando valores negativos.</summary>
        public static long SumByteSizes(IEnumerable<long>? sizes)
        {
            if (sizes == null)
                return 0L;

            long sum = 0L;
            foreach (long size in sizes)
            {
                if (size > 0L)
                    sum += size;
            }
            return sum;
        }

        /// <summary>Formatea la fecha de juego para el manifiesto <c>p4e-backup.json</c>.</summary>
        public static string FormatGameDate(string? seasonName, int dayOfMonth, int year) =>
            $"{(string.IsNullOrWhiteSpace(seasonName) ? "unknown" : seasonName)} {dayOfMonth}, Year {year}";
    }
}
