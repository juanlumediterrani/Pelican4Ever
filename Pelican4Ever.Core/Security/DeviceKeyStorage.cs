using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Pelican4Ever.Core.Security
{
    /// <summary>
    /// Modelo de serialización para el archivo de clave privada local del cliente (data/device_key.json).
    /// </summary>
    public sealed class DeviceKeyFile
    {
        public const int CurrentSchemaVersion = 2;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string Pkcs8Base64 { get; set; } = string.Empty;
        public string Fingerprint { get; set; } = string.Empty;
        public string PublicKeyBase64 { get; set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Almacenamiento seguro, atómico y resiliente del par de claves criptográficas del dispositivo en el cliente (TASK-1802).
    /// En Linux/macOS aplica permisos POSIX 0600 (lectura/escritura exclusiva del usuario).
    /// En Windows se apoya en los ACLs del directorio de usuario de SMAPI / Stardew Valley.
    /// </summary>
    public static class DeviceKeyStorage
    {
        [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
        private static extern int ChmodLibc(string pathname, int mode);

        private const int PosixUserReadWrite = 0x180; // 0600 octal = 384 decimal

        /// <summary>
        /// Aplica permisos restringidos de solo lectura/escritura para el usuario actual en sistemas basados en Unix (0600).
        /// En Windows esta llamada es una operación sin efecto (los permisos se heredan del perfil de usuario).
        /// </summary>
        public static bool RestrictFilePermissions(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return false;

            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    int res = ChmodLibc(filePath, PosixUserReadWrite);
                    return res == 0;
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Guarda el par de claves en disco de forma atómica y con permisos protegidos.
        /// Escribe primero en un archivo temporal (.tmp), fuerza la sincronización con el almacenamiento
        /// y reemplaza el archivo final de forma atómica.
        /// </summary>
        public static void SaveAtomic(string filePath, DeviceKeyPair keyPair)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("La ruta del archivo no puede estar vacía.", nameof(filePath));
            if (keyPair == null)
                throw new ArgumentNullException(nameof(keyPair));

            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var model = new DeviceKeyFile
            {
                SchemaVersion = DeviceKeyFile.CurrentSchemaVersion,
                Pkcs8Base64 = Convert.ToBase64String(keyPair.ExportPkcs8PrivateKey()),
                Fingerprint = keyPair.GetFingerprint(),
                PublicKeyBase64 = keyPair.GetPublicKeyBase64(),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            string json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
            string tmpPath = filePath + ".tmp";

            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs))
            {
                writer.Write(json);
                writer.Flush();
                fs.Flush(flushToDisk: true);
            }

            RestrictFilePermissions(tmpPath);

            File.Move(tmpPath, filePath, overwrite: true);
            RestrictFilePermissions(filePath);
        }

        /// <summary>
        /// Carga el par de claves desde el archivo especificado.
        /// Realiza comprobación estricta de integridad (que la clave privada corresponda a la huella declarada).
        /// Lanza excepción si el archivo está corrupto o alterado (fallo cerrado seguro, sin conceder acceso silencioso).
        /// </summary>
        public static DeviceKeyPair Load(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException($"No se encontró el archivo de clave en '{filePath}'.");

            string json = File.ReadAllText(filePath);
            DeviceKeyFile? model;
            try
            {
                model = JsonSerializer.Deserialize<DeviceKeyFile>(json);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"El archivo de clave en '{filePath}' contiene JSON malformado o inválido.", ex);
            }

            if (model == null || string.IsNullOrWhiteSpace(model.Pkcs8Base64))
                throw new InvalidDataException($"El archivo de clave en '{filePath}' no contiene una clave privada válida.");

            byte[] pkcs8;
            try
            {
                pkcs8 = Convert.FromBase64String(model.Pkcs8Base64.Trim());
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("La clave privada en el archivo no es una cadena Base64 válida.", ex);
            }

            DeviceKeyPair keyPair;
            try
            {
                keyPair = DeviceKeyPair.FromPkcs8PrivateKey(pkcs8);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Los bytes de la clave privada no corresponden a un formato PKCS#8 válido.", ex);
            }

            // Verificación de integridad: la clave privada cargada DEBE generar exactamente la misma huella
            if (!string.IsNullOrWhiteSpace(model.Fingerprint) &&
                !string.Equals(keyPair.GetFingerprint(), model.Fingerprint.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                keyPair.Dispose();
                throw new InvalidDataException("Fallo de integridad: la huella calculada no coincide con la huella declarada en el archivo.");
            }

            return keyPair;
        }

        /// <summary>
        /// Carga la clave existente o genera una nueva de forma atómica y segura si no existe.
        /// Si el archivo existe pero está corrupto, la carga falla de forma cerrada y segura sin acceso silencioso.
        /// </summary>
        public static DeviceKeyPair LoadOrGenerate(string filePath, out bool isNew)
        {
            if (File.Exists(filePath))
            {
                isNew = false;
                return Load(filePath);
            }

            // Si quedó un archivo .tmp huérfano por caída de proceso anterior antes del Move
            string tmpPath = filePath + ".tmp";
            if (File.Exists(tmpPath))
            {
                try
                {
                    var recovered = Load(tmpPath);
                    File.Move(tmpPath, filePath, overwrite: true);
                    RestrictFilePermissions(filePath);
                    isNew = false;
                    return recovered;
                }
                catch
                {
                    // Si el .tmp huérfano estaba incompleto o corrupto, se limpia de forma segura
                    try { File.Delete(tmpPath); } catch { }
                }
            }

            var generated = DeviceKeyPair.Generate();
            SaveAtomic(filePath, generated);
            isNew = true;
            return generated;
        }
    }
}
