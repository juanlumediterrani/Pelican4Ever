using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pelican4Ever.Core.Admin
{
    /// <summary>Entrada por archivo en el manifiesto <c>p4e-backup.json</c> (ADR-0010, TASK-2505).</summary>
    public sealed class BackupManifestFileEntry
    {
        public string RelativePath { get; set; } = string.Empty;
        public long Bytes { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }

    /// <summary>
    /// Manifiesto estructurado escrito junto a cada copia diaria, checkpoint o resguardo <c>pre-restore</c>
    /// para verificación externa por <c>putre-ops</c> (<c>saves.restore</c>).
    /// </summary>
    public sealed class BackupManifest
    {
        public const string FileName = "p4e-backup.json";
        public const int CurrentFormatVersion = 1;

        public int FormatVersion { get; set; } = CurrentFormatVersion;
        public string Kind { get; set; } = string.Empty;
        public string SaveFolderName { get; set; } = string.Empty;
        public int TotalGameDays { get; set; }
        public string GameDate { get; set; } = string.Empty;
        public string CreatedUtc { get; set; } = string.Empty;
        public string ModVersion { get; set; } = string.Empty;
        public string GameVersion { get; set; } = string.Empty;
        public bool Compressed { get; set; }
        public List<BackupManifestFileEntry> Files { get; set; } = new();
    }

    /// <summary>Serialización y validación pura del manifiesto <c>p4e-backup.json</c> (TASK-2505).</summary>
    public static class BackupManifestLogic
    {
        private static readonly Regex Sha256HexRegex = new("^[0-9a-f]{64}$", RegexOptions.Compiled);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static string Serialize(BackupManifest manifest)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));
            return JsonSerializer.Serialize(manifest, JsonOptions);
        }

        public static BackupManifest? Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            return JsonSerializer.Deserialize<BackupManifest>(json, JsonOptions);
        }

        public static IReadOnlyList<string> Validate(BackupManifest? manifest)
        {
            var errors = new List<string>();
            if (manifest == null)
            {
                errors.Add("El manifiesto es null.");
                return errors;
            }

            if (manifest.FormatVersion != BackupManifest.CurrentFormatVersion)
                errors.Add($"FormatVersion no soportado: {manifest.FormatVersion} (esperado {BackupManifest.CurrentFormatVersion}).");

            if (manifest.Kind is not ("Daily" or "Checkpoint" or "PreRestore"))
                errors.Add($"Kind inválido: '{manifest.Kind}'.");

            if (string.IsNullOrWhiteSpace(manifest.SaveFolderName))
                errors.Add("SaveFolderName es obligatorio.");

            if (manifest.TotalGameDays < 0)
                errors.Add($"TotalGameDays no puede ser negativo ({manifest.TotalGameDays}).");

            if (string.IsNullOrWhiteSpace(manifest.CreatedUtc) || !DateTimeOffset.TryParse(manifest.CreatedUtc, out _))
                errors.Add($"CreatedUtc inválido: '{manifest.CreatedUtc}'.");

            if (manifest.Files == null || manifest.Files.Count == 0)
            {
                errors.Add("Files no puede estar vacío.");
                return errors;
            }

            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (BackupManifestFileEntry file in manifest.Files)
            {
                if (file == null)
                {
                    errors.Add("Entrada de archivo nula en Files.");
                    continue;
                }

                if (!IsSafeRelativePath(file.RelativePath))
                    errors.Add($"RelativePath inseguro o inválido: '{file.RelativePath}'.");
                else if (!seenPaths.Add(file.RelativePath))
                    errors.Add($"RelativePath duplicado: '{file.RelativePath}'.");

                if (file.Bytes < 0)
                    errors.Add($"Bytes negativos en '{file.RelativePath}': {file.Bytes}.");

                if (string.IsNullOrEmpty(file.Sha256) || !Sha256HexRegex.IsMatch(file.Sha256))
                    errors.Add($"Sha256 inválido en '{file.RelativePath}'.");
            }

            return errors;
        }

        public static bool IsSafeRelativePath(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return false;

            if (Path.IsPathRooted(relativePath) || relativePath.Contains('\\'))
                return false;

            string[] segments = relativePath.Split('/');
            foreach (string seg in segments)
            {
                if (string.IsNullOrEmpty(seg) || seg is "." or "..")
                    return false;
            }

            return true;
        }
    }
}
