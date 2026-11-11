using System;
using System.IO;
using Pelican4Ever.Core.Security;
using StardewModdingAPI;

namespace Pelican4Ever.Framework
{
    /// <summary>
    /// Modelo de persistencia para el archivo local data/client_token.json.
    /// </summary>
    public sealed class ClientTokenFile
    {
        public string Token { get; set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gestiona la identidad persistente de este cliente/máquina mediante un par de claves asimétricas ECDSA P-256 (Protocolo P4E\x02)
    /// y preserva compatibilidad con el token heredado durante la transición.
    /// </summary>
    public static class ClientIdentity
    {
        private const string GlobalDataKey = "pelican4ever-client-token";
        private const string GlobalDataDeviceKey = "pelican4ever-device-key";
        private const string JsonPath = "data/client_token.json";
        private const string DeviceKeyJsonPath = "data/device_key.json";

        private static string? cachedToken;
        private static DeviceKeyPair? cachedKeyPair;

        /// <summary>Token único asignado a esta instalación/máquina (compatibilidad heredada).</summary>
        public static string CurrentToken => cachedToken ?? string.Empty;

        /// <summary>Par de claves asimétricas ECDSA NIST P-256 de este dispositivo.</summary>
        public static DeviceKeyPair? DeviceKey => cachedKeyPair;

        /// <summary>Huella digital pública del dispositivo actual (SHA-256 de la clave pública SPKI).</summary>
        public static string DeviceFingerprint => cachedKeyPair?.GetFingerprint() ?? string.Empty;

        /// <summary>
        /// Inicializa o recupera el token del cliente y el par de claves asimétricas del dispositivo.
        /// </summary>
        public static string Initialize(IModHelper helper)
        {
            InitializeDeviceKey(helper);

            if (!string.IsNullOrWhiteSpace(cachedToken))
                return cachedToken;

            // 0. Variable de entorno opcional para depuración o multi-instancia en el mismo equipo
            string? envToken = Environment.GetEnvironmentVariable("P4E_CLIENT_TOKEN");
            if (!string.IsNullOrWhiteSpace(envToken))
            {
                cachedToken = envToken.Trim();
                return cachedToken;
            }

            // 1. Intentar leer de SMAPI GlobalData
            string? token = helper.Data.ReadGlobalData<string>(GlobalDataKey);

            // 2. Si no existe, intentar leer de data/client_token.json
            if (string.IsNullOrWhiteSpace(token))
            {
                var file = helper.Data.ReadJsonFile<ClientTokenFile>(JsonPath);
                if (!string.IsNullOrWhiteSpace(file?.Token))
                {
                    token = file.Token.Trim();
                }
            }

            // 3. Si aún no existe, generar un nuevo GUID único
            if (string.IsNullOrWhiteSpace(token))
            {
                token = Guid.NewGuid().ToString("N");
            }

            token = token.Trim();
            cachedToken = token;

            // Asegurar que quede guardado en ambos almacenamientos
            try
            {
                helper.Data.WriteGlobalData(GlobalDataKey, token);
                helper.Data.WriteJsonFile(JsonPath, new ClientTokenFile
                {
                    Token = token,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
            }
            catch
            {
                // No bloquear la inicialización si hay problemas de I/O
            }

            return token;
        }

        /// <summary>
        /// Inicializa o carga la clave privada del dispositivo. Si no existe, genera una nueva clave NIST P-256
        /// con persistencia atómica y permisos restringidos (0600 en Linux).
        /// </summary>
        public static DeviceKeyPair InitializeDeviceKey(IModHelper helper)
        {
            if (cachedKeyPair != null)
                return cachedKeyPair;

            string fullPath = Path.Combine(helper.DirectoryPath, DeviceKeyJsonPath);

            try
            {
                cachedKeyPair = DeviceKeyStorage.LoadOrGenerate(fullPath, out bool isNew);
                if (isNew)
                {
                    try
                    {
                        string pkcs8Base64 = Convert.ToBase64String(cachedKeyPair.ExportPkcs8PrivateKey());
                        helper.Data.WriteGlobalData(GlobalDataDeviceKey, pkcs8Base64);
                    }
                    catch
                    {
                        // Respaldo secundario
                    }
                }
                return cachedKeyPair;
            }
            catch (Exception ex)
            {
                // Si la carga falló por corrupción, intentar recuperar desde GlobalData si está disponible
                string? globalDataKey = helper.Data.ReadGlobalData<string>(GlobalDataDeviceKey);
                if (!string.IsNullOrWhiteSpace(globalDataKey))
                {
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(globalDataKey.Trim());
                        cachedKeyPair = DeviceKeyPair.FromPkcs8PrivateKey(bytes);
                        DeviceKeyStorage.SaveAtomic(fullPath, cachedKeyPair);
                        return cachedKeyPair;
                    }
                    catch
                    {
                        // Clave en GlobalData también corrupta
                    }
                }

                // Fallo cerrado seguro: relanzar para que no se conceda acceso silencioso con claves falsas
                throw new InvalidOperationException($"No se pudo inicializar la clave del dispositivo: {ex.Message}", ex);
            }
        }
    }
}
