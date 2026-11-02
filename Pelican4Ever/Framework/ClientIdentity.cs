using System;
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
    /// Gestiona la identidad persistente de este cliente/máquina mediante un token secreto único (Opción A).
    /// Se persiste tanto en GlobalData de SMAPI como en data/client_token.json para resiliencia ante reinstalaciones.
    /// </summary>
    public static class ClientIdentity
    {
        private const string GlobalDataKey = "pelican4ever-client-token";
        private const string JsonPath = "data/client_token.json";

        private static string? cachedToken;

        /// <summary>Token único asignado a esta instalación/máquina.</summary>
        public static string CurrentToken => cachedToken ?? string.Empty;

        /// <summary>
        /// Inicializa o recupera el token del cliente.
        /// </summary>
        public static string Initialize(IModHelper helper)
        {
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
    }
}
