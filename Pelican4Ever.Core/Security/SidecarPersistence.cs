using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;

namespace Pelican4Ever.Core.Security
{
    /// <summary>
    /// Modelo de serialización para el archivo sidecar Saves/&lt;Partida&gt;/pelican4ever_pairing.json (Schema v2).
    /// El archivo se persiste de forma inmediata y atómica tras cualquier cambio en invitaciones o dispositivos.
    /// NUNCA almacena claves privadas ni tokens secretos.
    /// </summary>
    public sealed class PairingSidecarFile
    {
        public const int CurrentSchemaVersion = 2;

        /// <summary>Versión del esquema de persistencia del sidecar (v2: identidad asimétrica por dispositivo).</summary>
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>Dispositivos autorizados indexados por su huella digital pública (Schema v2).</summary>
        public Dictionary<string, AuthorizedDevice> AuthorizedDevices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Solicitudes de alta pendientes de aprobación por el propietario (Schema v2).</summary>
        public Dictionary<string, DeviceEnrollmentRequest> PendingEnrollments { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Clientes autorizados heredados (Schema v1, conservados para migración).</summary>
        public Dictionary<string, AuthorizedClient> AuthorizedClients { get; set; } = new(StringComparer.Ordinal);

        /// <summary>Invitaciones de emparejamiento activas pendientes de aprobación.</summary>
        public List<PairingInvite> PairingInvites { get; set; } = new();
    }

    /// <summary>
    /// Persistencia atómica, inmediata y resiliente del archivo sidecar de emparejamiento del host (TASK-1802).
    /// </summary>
    public static class SidecarPersistence
    {
        /// <summary>
        /// Guarda el estado de dispositivos e invitaciones en disco de forma atómica e inmediata.
        /// Escribe en un archivo temporal (.tmp), fuerza la sincronización física en disco y reemplaza atómicamente el destino.
        /// </summary>
        public static void SaveAtomic(string path, ServerWorldState state)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("La ruta del archivo no puede estar vacía.", nameof(path));
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            state.AuthorizedDevices ??= new Dictionary<string, AuthorizedDevice>(StringComparer.OrdinalIgnoreCase);
            state.PendingEnrollments ??= new Dictionary<string, DeviceEnrollmentRequest>(StringComparer.OrdinalIgnoreCase);
            state.AuthorizedClients ??= new Dictionary<string, AuthorizedClient>(StringComparer.Ordinal);
            state.PairingInvites ??= new List<PairingInvite>();

            var sidecar = new PairingSidecarFile
            {
                SchemaVersion = PairingSidecarFile.CurrentSchemaVersion,
                AuthorizedDevices = new Dictionary<string, AuthorizedDevice>(state.AuthorizedDevices, StringComparer.OrdinalIgnoreCase),
                PendingEnrollments = new Dictionary<string, DeviceEnrollmentRequest>(state.PendingEnrollments, StringComparer.OrdinalIgnoreCase),
                AuthorizedClients = new Dictionary<string, AuthorizedClient>(state.AuthorizedClients, StringComparer.Ordinal),
                PairingInvites = new List<PairingInvite>(state.PairingInvites)
            };

            string json = JsonSerializer.Serialize(sidecar, new JsonSerializerOptions { WriteIndented = true });
            string tmpPath = path + ".tmp";

            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs))
            {
                writer.Write(json);
                writer.Flush();
                fs.Flush(flushToDisk: true);
            }

            File.Move(tmpPath, path, overwrite: true);
        }

        /// <summary>
        /// Carga y fusiona el archivo sidecar con el estado del servidor.
        /// Soporta recuperación ante corte (si existe un .tmp huérfano con contenido íntegro).
        /// Maneja esquemas v1 y v2 de forma transparente.
        /// </summary>
        public static bool LoadAndMerge(string path, ServerWorldState state, out string? errorMessage)
        {
            errorMessage = null;
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string targetPath = path;

            // Recuperación ante corte: si el archivo final no existe pero hay un .tmp íntegro
            if (!File.Exists(targetPath))
            {
                string tmpCandidate = path + ".tmp";
                if (File.Exists(tmpCandidate))
                {
                    try
                    {
                        string tmpRaw = File.ReadAllText(tmpCandidate);
                        var testParsed = JsonSerializer.Deserialize<PairingSidecarFile>(tmpRaw);
                        if (testParsed != null)
                        {
                            File.Move(tmpCandidate, path, overwrite: true);
                            targetPath = path;
                        }
                    }
                    catch
                    {
                        try { File.Delete(tmpCandidate); } catch { }
                    }
                }
            }

            if (!File.Exists(targetPath))
                return false;

            try
            {
                string raw = File.ReadAllText(targetPath);
                var sidecar = JsonSerializer.Deserialize<PairingSidecarFile>(raw);
                if (sidecar == null)
                {
                    errorMessage = "El archivo sidecar contiene contenido vacío o nulo.";
                    return false;
                }

                state.AuthorizedDevices ??= new Dictionary<string, AuthorizedDevice>(StringComparer.OrdinalIgnoreCase);
                if (sidecar.AuthorizedDevices != null)
                {
                    foreach (var (fp, device) in sidecar.AuthorizedDevices)
                    {
                        if (!string.IsNullOrWhiteSpace(fp) && device != null)
                        {
                            state.AuthorizedDevices[fp.Trim()] = device;
                        }
                    }
                }

                state.PendingEnrollments ??= new Dictionary<string, DeviceEnrollmentRequest>(StringComparer.OrdinalIgnoreCase);
                if (sidecar.PendingEnrollments != null)
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    foreach (var (fp, enroll) in sidecar.PendingEnrollments)
                    {
                        if (!string.IsNullOrWhiteSpace(fp) && enroll != null && !enroll.IsExpired(now))
                        {
                            state.PendingEnrollments[fp.Trim()] = enroll;
                        }
                    }
                }

                state.AuthorizedClients ??= new Dictionary<string, AuthorizedClient>(StringComparer.Ordinal);
                if (sidecar.AuthorizedClients != null)
                {
                    foreach (var (token, client) in sidecar.AuthorizedClients)
                    {
                        if (!string.IsNullOrWhiteSpace(token) && client != null)
                        {
                            state.AuthorizedClients[token.Trim()] = client;
                        }
                    }
                }

                state.PairingInvites ??= new List<PairingInvite>();
                if (sidecar.PairingInvites != null)
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    foreach (PairingInvite invite in sidecar.PairingInvites)
                    {
                        if (invite == null || invite.IsExpired(now))
                            continue;

                        bool exists = state.PairingInvites.Any(i =>
                            string.Equals(i.Code, invite.Code, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(i.Alias, invite.Alias, StringComparison.OrdinalIgnoreCase));

                        if (!exists)
                        {
                            state.PairingInvites.Add(invite);
                        }
                    }
                }

                // Migrar dispositivos desde clientes v1 si es aplicable
                ServerDeviceRegistry.MigrateLegacyClients(state, DateTimeOffset.UtcNow);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Error al leer el archivo sidecar '{targetPath}': {ex.Message}";
                return false;
            }
        }
    }
}
