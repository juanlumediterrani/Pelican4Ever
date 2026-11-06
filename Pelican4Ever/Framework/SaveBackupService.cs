using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Pelican4Ever.Core.Admin;
using StardewModdingAPI;

namespace Pelican4Ever.Framework
{
    /// <summary>Resultado de una copia verificada de la partida.</summary>
    /// <param name="FolderName">Nombre de la carpeta de la copia.</param>
    /// <param name="FolderPath">Ruta completa de la copia.</param>
    /// <param name="FileCount">Archivos copiados.</param>
    /// <param name="Bytes">Tamaño en disco de la copia.</param>
    /// <param name="CreatedUtc">Momento de creación (el mismo que lleva el nombre).</param>
    public sealed record SaveBackupResult(string FolderName, string FolderPath, int FileCount, long Bytes, DateTimeOffset CreatedUtc);

    /// <summary>
    /// Copias verificadas de la partida actual (TASK-1605): las del rollover diario en <c>Pelican4Ever_Backups/</c> y
    /// las de los checkpoints en <c>Pelican4Ever_Backups/checkpoints/</c>, con compresión opcional y rotación según
    /// <see cref="BackupRetentionPolicy"/>. Solo toca copias de la partida cargada.
    /// </summary>
    public static class SaveBackupService
    {
        public const string RootFolderName = "Pelican4Ever_Backups";
        public const string CheckpointFolderName = "checkpoints";

        /// <summary>Carpeta donde viven las copias de un tipo.</summary>
        public static string FolderFor(BackupKind kind)
        {
            string root = Path.Combine(Constants.DataPath, RootFolderName);
            return kind == BackupKind.Checkpoint ? Path.Combine(root, CheckpointFolderName) : root;
        }

        /// <summary>
        /// Copia la carpeta de la partida actual y verifica cada archivo con SHA-256 (sobre el contenido sin comprimir).
        /// La copia se escribe en <c>&lt;destino&gt;.partial</c> y solo se renombra al final, así que una copia a medias
        /// nunca parece buena. Devuelve <c>null</c> y lo registra si algo falla.
        /// </summary>
        public static SaveBackupResult? Create(IMonitor monitor, BackupKind kind, bool compress)
        {
            string? partial = null;
            try
            {
                string? savePath = Constants.CurrentSavePath;
                string? saveFolder = Constants.SaveFolderName;
                if (string.IsNullOrEmpty(savePath) || string.IsNullOrEmpty(saveFolder) || !Directory.Exists(savePath))
                    throw new IOException("No hay una partida cargada con carpeta en disco.");

                string[] files = Directory.GetFiles(savePath, "*", SearchOption.TopDirectoryOnly);
                if (files.Length == 0)
                    throw new IOException("La carpeta de la partida no contiene archivos que respaldar.");

                string dir = FolderFor(kind);
                Directory.CreateDirectory(dir);
                DateTimeOffset created = DateTimeOffset.UtcNow;
                string name = BackupRetentionPolicy.FolderName(saveFolder, created);
                string dest = Path.Combine(dir, name);
                partial = dest + ".partial";
                Directory.CreateDirectory(partial);

                long bytes = 0;
                foreach (string file in files)
                {
                    string copy = Path.Combine(partial, Path.GetFileName(file) + (compress ? ".gz" : string.Empty));
                    byte[] sourceHash = compress ? CompressWithHash(file, copy) : CopyWithHash(file, copy);
                    byte[] copyHash = HashCopy(copy, compress);
                    if (!CryptographicOperations.FixedTimeEquals(sourceHash, copyHash))
                        throw new IOException($"La verificación SHA-256 falló para {Path.GetFileName(file)}.");
                    bytes += new FileInfo(copy).Length;
                }

                Directory.Move(partial, dest);
                partial = null;
                return new SaveBackupResult(name, dest, files.Length, bytes, created);
            }
            catch (Exception ex)
            {
                if (partial != null && Directory.Exists(partial))
                {
                    try { Directory.Delete(partial, recursive: true); }
                    catch (Exception cleanupError) { monitor.Log($"[SaveBackup] No se pudo retirar la copia parcial {partial}: {cleanupError.Message}", LogLevel.Warn); }
                }
                monitor.Log($"[SaveBackup] Error al crear la copia ({kind}): {ex}", LogLevel.Warn);
                return null;
            }
        }

        /// <summary>
        /// Borra las copias de la partida actual que sobran según <see cref="BackupRetentionPolicy"/>. Las carpetas que no
        /// son copias de esta partida (otras partidas, <c>.partial</c>, <c>spike/</c>, <c>checkpoints/</c>) no se tocan.
        /// </summary>
        /// <returns>Copias borradas.</returns>
        public static int ApplyRetention(IMonitor monitor, BackupKind kind, int keepCheckpoints, int keepDailyDays)
        {
            string? saveFolder = Constants.SaveFolderName;
            string dir = FolderFor(kind);
            if (string.IsNullOrEmpty(saveFolder) || !Directory.Exists(dir))
                return 0;
            if ((kind == BackupKind.Checkpoint && keepCheckpoints <= 0) || (kind == BackupKind.Daily && keepDailyDays <= 0))
                return 0;

            var entries = new List<BackupEntry>();
            foreach (string path in Directory.GetDirectories(dir))
            {
                string name = Path.GetFileName(path);
                if (BackupRetentionPolicy.TryParseFolderName(name, saveFolder, out DateTimeOffset created))
                    entries.Add(new BackupEntry(name, kind, created));
            }

            int deleted = 0;
            foreach (BackupEntry entry in BackupRetentionPolicy.ToDelete(entries, keepCheckpoints, keepDailyDays, DateTimeOffset.UtcNow))
            {
                try
                {
                    Directory.Delete(Path.Combine(dir, entry.Name), recursive: true);
                    deleted++;
                    monitor.Log($"[SaveBackup] Copia antigua retirada por rotación: {entry.Name}", LogLevel.Trace);
                }
                catch (Exception ex)
                {
                    monitor.Log($"[SaveBackup] No se pudo retirar la copia {entry.Name}: {ex.Message}", LogLevel.Warn);
                }
            }

            if (deleted > 0)
                monitor.Log($"[SaveBackup] Rotación ({kind}): {deleted} copias retiradas, {entries.Count - deleted} conservadas.", LogLevel.Info);
            return deleted;
        }

        private static byte[] CopyWithHash(string source, string dest)
        {
            File.Copy(source, dest, overwrite: false);
            using FileStream stream = File.OpenRead(source);
            using SHA256 sha = SHA256.Create();
            return sha.ComputeHash(stream);
        }

        private static byte[] CompressWithHash(string source, string dest)
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (FileStream input = File.OpenRead(source))
            using (FileStream output = new(dest, FileMode.CreateNew))
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            {
                byte[] buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    sha.AppendData(buffer, 0, read);
                    gzip.Write(buffer, 0, read);
                }
            }
            return sha.GetHashAndReset();
        }

        private static byte[] HashCopy(string copy, bool compressed)
        {
            using FileStream file = File.OpenRead(copy);
            using SHA256 sha = SHA256.Create();
            if (!compressed)
                return sha.ComputeHash(file);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            return sha.ComputeHash(gzip);
        }
    }
}
