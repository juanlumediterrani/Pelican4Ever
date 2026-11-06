using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Pelican4Ever.Core.Admin
{
    /// <summary>Tipo de copia de seguridad de la partida.</summary>
    public enum BackupKind
    {
        /// <summary>Copias del rollover diario, en <c>Pelican4Ever_Backups/</c>.</summary>
        Daily,

        /// <summary>Checkpoints intradía, en <c>Pelican4Ever_Backups/checkpoints/</c>.</summary>
        Checkpoint
    }

    /// <summary>Una copia existente en disco.</summary>
    /// <param name="Name">Nombre de la carpeta de la copia.</param>
    /// <param name="Kind">Tipo de copia.</param>
    /// <param name="CreatedUtc">Momento de creación.</param>
    public sealed record BackupEntry(string Name, BackupKind Kind, DateTimeOffset CreatedUtc);

    /// <summary>Lógica pura de rotación de copias de la partida (TASK-1605, gap G7).</summary>
    public static class BackupRetentionPolicy
    {
        /// <summary>Formato de fecha con el que se nombran las copias: <c>&lt;partida&gt;_yyyyMMdd_HHmmss_fff</c> (UTC).</summary>
        public const string TimestampFormat = "yyyyMMdd_HHmmss_fff";

        /// <summary>Nombre de la carpeta de una copia de <paramref name="saveFolderName"/> creada en <paramref name="createdUtc"/>.</summary>
        public static string FolderName(string saveFolderName, DateTimeOffset createdUtc) =>
            $"{saveFolderName}_{createdUtc.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture)}";

        /// <summary>
        /// Lee la fecha del nombre de una copia de <paramref name="saveFolderName"/>. Devuelve <c>false</c> si la carpeta
        /// no es una copia de esa partida (otra partida, una copia parcial <c>.partial</c> o un nombre ajeno).
        /// </summary>
        public static bool TryParseFolderName(string folderName, string saveFolderName, out DateTimeOffset createdUtc)
        {
            createdUtc = default;
            string prefix = saveFolderName + "_";
            if (!folderName.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            string stamp = folderName.Substring(prefix.Length);
            if (!DateTime.TryParseExact(stamp, TimestampFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime parsed))
                return false;

            createdUtc = new DateTimeOffset(parsed, TimeSpan.Zero);
            return true;
        }

        /// <summary>
        /// Copias que sobran. Checkpoints: se conservan los <paramref name="keepCheckpoints"/> más recientes.
        /// Diarias: se conservan las de los últimos <paramref name="keepDailyDays"/> días. Un valor 0 o negativo no
        /// rota ese tipo. Nunca se borra la copia más reciente de cada tipo.
        /// </summary>
        public static IReadOnlyList<BackupEntry> ToDelete(
            IEnumerable<BackupEntry> entries,
            int keepCheckpoints,
            int keepDailyDays,
            DateTimeOffset nowUtc)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));

            var all = entries.ToList();
            var result = new List<BackupEntry>();

            if (keepCheckpoints > 0)
            {
                result.AddRange(all
                    .Where(e => e.Kind == BackupKind.Checkpoint)
                    .OrderByDescending(e => e.CreatedUtc)
                    .Skip(Math.Max(1, keepCheckpoints)));
            }

            if (keepDailyDays > 0)
            {
                DateTimeOffset cutoff = nowUtc - TimeSpan.FromDays(keepDailyDays);
                result.AddRange(all
                    .Where(e => e.Kind == BackupKind.Daily)
                    .OrderByDescending(e => e.CreatedUtc)
                    .Skip(1)
                    .Where(e => e.CreatedUtc < cutoff));
            }

            return result;
        }
    }
}
