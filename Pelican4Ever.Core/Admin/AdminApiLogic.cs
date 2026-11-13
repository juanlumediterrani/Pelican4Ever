using System;
using System.Collections.Generic;
using Pelican4Ever.Core.Config;

namespace Pelican4Ever.Core.Admin
{
    public sealed record ApiAccessCheckResult(bool Allowed, int StatusCode, string ErrorMessage);

    public class ServerStatusDto
    {
        public string ServerVersion { get; set; } = string.Empty;
        public string RealTimeUtc { get; set; } = string.Empty;
        public int GameTimeOfDay { get; set; }
        public int TotalGameDays { get; set; }
        public string Season { get; set; } = string.Empty;
        public int Year { get; set; }
        public int DayOfMonth { get; set; }
        public bool IsNightHold { get; set; }
        public bool DedicatedHost { get; set; }
        public int OnlinePlayersCount { get; set; }
        public int RegisteredPlayersCount { get; set; }
        public int OccupiedDoorsCount { get; set; }
        public int TotalDoorsCount { get; set; }
        public int ActiveMarketListingsCount { get; set; }
        public List<PlayerStatusDto> Players { get; set; } = new();
        public List<DoorStatusDto> Doors { get; set; } = new();
    }

    public class PlayerStatusDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public string SleepState { get; set; } = string.Empty;
        public string FarmSlotId { get; set; } = string.Empty;
        public int SatietyUsed { get; set; }
        public int PendingMarketGold { get; set; }
    }

    public class DoorStatusDto
    {
        public string DoorId { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public int TileX { get; set; }
        public int TileY { get; set; }
        public string SlotId { get; set; } = string.Empty;
        public bool IsOccupied { get; set; }
        public string OwnerName { get; set; } = string.Empty;
        public string FarmType { get; set; } = string.Empty;
        public string Policy { get; set; } = string.Empty;
    }

    /// <summary>
    /// Lógica de dominio pura para autenticación y respuestas del API de administración (TASK-801, TASK-802 y TASK-803).
    /// </summary>
    public static class AdminApiLogic
    {
        /// <summary>
        /// Comprueba si el token configurado es seguro para arrancar el servidor HTTP.
        /// Rechaza tokens vacíos o iguales al token por defecto (<see cref="ServerConfig.DefaultInsecureAdminToken"/>) (TASK-803).
        /// </summary>
        public static bool IsTokenConfiguredSecurely(string? configuredToken)
        {
            if (string.IsNullOrWhiteSpace(configuredToken))
                return false;

            string trimmed = configuredToken.Trim();
            return !string.Equals(trimmed, ServerConfig.DefaultInsecureAdminToken, StringComparison.Ordinal)
                && !string.Equals(trimmed, ServerConfig.ExampleAdminTokenPlaceholder, StringComparison.Ordinal);
        }

        /// <summary>
        /// Devuelve los prefijos de escucha para <c>HttpListener</c>.
        /// Por defecto solo escucha en loopback (<c>127.0.0.1</c> y <c>localhost</c>) salvo que se habilite <paramref name="bindAllInterfaces"/> (TASK-803).
        /// </summary>
        public static IReadOnlyList<string> GetListenerPrefixes(int port, bool bindAllInterfaces = false)
        {
            if (bindAllInterfaces)
            {
                return new[] { $"http://*:{port}/" };
            }

            return new[]
            {
                $"http://127.0.0.1:{port}/",
                $"http://localhost:{port}/"
            };
        }

        /// <summary>
        /// Valida la cabecera HTTP de autorización con formato Bearer.
        /// </summary>
        public static bool IsAuthorized(string? authHeader, string expectedToken)
        {
            if (string.IsNullOrWhiteSpace(expectedToken))
                return false;

            if (string.IsNullOrWhiteSpace(authHeader))
                return false;

            string trimmed = authHeader.Trim();
            if (trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                string token = trimmed.Substring(7).Trim();
                return string.Equals(token, expectedToken, StringComparison.Ordinal);
            }

            return string.Equals(trimmed, expectedToken, StringComparison.Ordinal);
        }

        /// <summary>
        /// Si una petición que lleva cuerpo (POST, PUT, PATCH) llega sin <c>Content-Length</c> ni
        /// <c>Transfer-Encoding: chunked</c>. <c>HttpListener</c> ya le ha respondido por su cuenta
        /// <c>411 Length Required</c> y cerrado la respuesta, pero la petición sigue llegando al mod: hay que
        /// descartarla sin ejecutar la acción (el cliente ya recibió un error).
        /// </summary>
        public static bool IsMissingRequiredBodyLength(string? method, string? contentLength, string? transferEncoding)
        {
            string m = (method ?? string.Empty).Trim().ToUpperInvariant();
            if (m != "POST" && m != "PUT" && m != "PATCH")
                return false;
            if (!string.IsNullOrWhiteSpace(contentLength))
                return false;
            return transferEncoding == null
                || transferEncoding.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>
        /// Evalúa el acceso tanto para el panel (<c>GET /</c>, <c>GET /dashboard</c>) como para cualquier ruta <c>/api/*</c> (TASK-803).
        /// Sin token válido o si el servidor tiene el token por defecto, responde 401.
        /// </summary>
        public static ApiAccessCheckResult CheckAccess(
            string path,
            string? authHeader,
            string? queryToken,
            string? configuredToken)
        {
            if (!IsTokenConfiguredSecurely(configuredToken))
            {
                return new ApiAccessCheckResult(
                    Allowed: false,
                    StatusCode: 401,
                    ErrorMessage: "No autorizado: el token de administración no está configurado de forma segura.");
            }

            string expected = configuredToken!.Trim();
            bool validHeader = IsAuthorized(authHeader, expected);
            bool validQuery = !string.IsNullOrWhiteSpace(queryToken) && IsAuthorized(queryToken, expected);

            if (!validHeader && !validQuery)
            {
                return new ApiAccessCheckResult(
                    Allowed: false,
                    StatusCode: 401,
                    ErrorMessage: "No autorizado: se requiere un token Bearer válido.");
            }

            return new ApiAccessCheckResult(Allowed: true, StatusCode: 200, ErrorMessage: string.Empty);
        }

    }
}
