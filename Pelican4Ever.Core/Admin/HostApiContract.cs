using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pelican4Ever.Core.Admin
{
    /// <summary>
    /// Definición formal de una ruta del espacio versionado <c>/api/v1/host/</c> (TASK-803).
    /// </summary>
    public sealed record HostApiRouteSpec(
        string Method,
        string Path,
        bool MutatesGameState,
        string Description);

    /// <summary>
    /// Estado del reloj, madrugada y último/próximo rollover (<c>GET /api/v1/host/clock</c>).
    /// </summary>
    public sealed class HostClockDto
    {
        public string RealTimeUtc { get; set; } = string.Empty;
        public string LocalTime { get; set; } = string.Empty;
        public string TimeZoneId { get; set; } = string.Empty;
        public int GameTimeOfDay { get; set; }
        public int TotalGameDays { get; set; }
        public int Year { get; set; }
        public string Season { get; set; } = string.Empty;
        public int DayOfMonth { get; set; }
        public bool IsNightHold { get; set; }
        public string NextRolloverLocalTime { get; set; } = string.Empty;
        public int SecondsUntilNextRollover { get; set; }
        public int LastRolloverGameDay { get; set; } = -1;
        public string LastRolloverOutcomeSummary { get; set; } = string.Empty;
        public string LastBackupFileName { get; set; } = string.Empty;
        public string LastBackupCreatedUtc { get; set; } = string.Empty;

        /// <summary>Carpeta de <c>Saves/</c> de la partida cargada: un supervisor comprueba que es la autorizada (TASK-805).</summary>
        public string SaveFolderName { get; set; } = string.Empty;

        /// <summary>Si la partida está abierta como servidor de cooperativo (los jugadores pueden entrar).</summary>
        public bool IsMultiplayerServer { get; set; }
    }

    /// <summary>
    /// Detalle de un jugador para <c>GET /api/v1/host/players</c>.
    /// </summary>
    public sealed class HostPlayerDetailDto
    {
        public long PlayerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public bool IsHost { get; set; }
        public string LocationName { get; set; } = string.Empty;
        public int TileX { get; set; }
        public int TileY { get; set; }
        public string ResidenceSlotId { get; set; } = string.Empty;
        public string ResidenceDoorId { get; set; } = string.Empty;
        public string ResidenceFarmType { get; set; } = string.Empty;
        public bool InFestival { get; set; }
        public string SleepState { get; set; } = string.Empty;
        public int LastActiveGameDay { get; set; } = -1;
        public int RestedOnGameDay { get; set; } = -1;
        public string LastOutcome { get; set; } = string.Empty;
        public int SatietyUsed { get; set; }
        public int WellFedBuffsUsed { get; set; }
        public bool WellFedActive { get; set; }
        public int SpaBuffsUsed { get; set; }
        public bool SpaBuffActive { get; set; }
        public int SatietyGameDay { get; set; } = -1;
        public int PendingMarketGold { get; set; }
        public bool IsClaimed { get; set; }
        public string? OwnerTokenMasked { get; set; }
    }

    /// <summary>
    /// Respuesta de <c>GET /api/v1/host/players</c>.
    /// </summary>
    public sealed class HostPlayersResponseDto
    {
        public int TotalOnline { get; set; }
        public int TotalRegistered { get; set; }
        public List<HostPlayerDetailDto> Players { get; set; } = new();
    }

    /// <summary>
    /// Estado del festival del día para <c>GET /api/v1/host/festival</c>.
    /// </summary>
    public sealed class HostFestivalDto
    {
        public bool IsFestivalToday { get; set; }
        public string FestivalId { get; set; } = string.Empty;
        public string FestivalName { get; set; } = string.Empty;
        public string Season { get; set; } = string.Empty;
        public int DayOfMonth { get; set; }
        public string LocationName { get; set; } = string.Empty;
        public int WindowStart { get; set; }
        public int WindowEnd { get; set; }
        public bool IsWindowOpen { get; set; }
        public bool HasMainEvent { get; set; }
        public bool IsCeremonyFestival { get; set; }
        public List<long> PresentPlayerIds { get; set; } = new();
        public List<long> SignedUpPlayerIds { get; set; } = new();
        public List<long> PlayedPlayerIds { get; set; } = new();
        public int RunsCount { get; set; }
    }

    /// <summary>
    /// Petición de anuncio para <c>POST /api/v1/host/announce</c>.
    /// </summary>
    public sealed class HostAnnounceRequestDto
    {
        public string Message { get; set; } = string.Empty;
        /// <summary>Canal de emisión: "chat", "hud" o "both" (por defecto "both").</summary>
        public string Channel { get; set; } = "both";
    }

    /// <summary>
    /// Petición de reinicio controlado para <c>POST /api/v1/host/restart</c> (TASK-804).
    /// </summary>
    public sealed class HostRestartRequestDto
    {
        public string Reason { get; set; } = string.Empty;
        public bool Immediate { get; set; }
        public int? WarningMinutes { get; set; }
        public bool Cancel { get; set; }
    }

    /// <summary>
    /// Respuesta estándar de acciones de <c>/api/v1/host/*</c>.
    /// </summary>
    public sealed class HostActionResultDto
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = string.Empty;
        public int ExecutedOnThreadId { get; set; }
        public long? EventId { get; set; }
    }

    /// <summary>
    /// Catálogo y enrutador puro del contrato <c>/api/v1/host/</c> (TASK-803 y TASK-804).
    /// </summary>
    public static class HostApiCatalog
    {
        public const string BasePath = "/api/v1/host";

        /// <summary>
        /// Rutas oficiales del espacio versionado <c>/api/v1/host/</c>.
        /// Deben coincidir 1:1 con las documentadas en <c>docs/host-api.md</c>.
        /// </summary>
        public static readonly IReadOnlyList<HostApiRouteSpec> Routes = new List<HostApiRouteSpec>
        {
            new("GET",  "/api/v1/host/clock",             MutatesGameState: false, "Hora del juego y real, madrugada, próximo rollover y resultado/copia del último."),
            new("GET",  "/api/v1/host/players",           MutatesGameState: false, "Jugadores conectados y registrados, ubicación, residencia, presencia en festival y PlayerRecord."),
            new("GET",  "/api/v1/host/festival",          MutatesGameState: false, "Festival de hoy, ventana, ubicación, evento principal, apuntados, tandas y quién ha jugado."),
            new("POST", "/api/v1/host/festival/start",    MutatesGameState: true,  "Inicia la siguiente tanda del evento principal con los jugadores apuntados (/evento empezar)."),
            new("POST", "/api/v1/host/festival/ceremony", MutatesGameState: true,  "Inicia la ceremonia comunitaria en vivo (TASK-702) con los vecinos presentes en el recinto."),
            new("POST", "/api/v1/host/announce",          MutatesGameState: true,  "Envía un aviso global por chat y/o en pantalla (HUD) a los jugadores."),
            new("POST", "/api/v1/host/rest",              MutatesGameState: true,  "Registra el descanso diario del anfitrión sin desconectar la sesión (TASK-804)."),
            new("POST", "/api/v1/host/restart",           MutatesGameState: true,  "Programa o cancela un reinicio controlado del servidor con preaviso (TASK-804)."),
            new("GET",  "/api/v1/host/events",            MutatesGameState: false, "Devuelve la lista incremental de sucesos desde el cursor ?since=N.")
        };

        /// <summary>
        /// Busca la especificación de ruta para un método y path HTTP normalizado (sin query string ni barra final).
        /// </summary>
        public static HostApiRouteSpec? MatchRoute(string method, string path)
        {
            if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(path))
                return null;

            string normPath = NormalizePath(path);
            return Routes.FirstOrDefault(r =>
                string.Equals(r.Method, method.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.Path, normPath, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Normaliza una ruta HTTP eliminando query string y barra final redundante.</summary>
        public static string NormalizePath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return "/";

            string clean = rawPath.Trim();
            int queryIdx = clean.IndexOf('?');
            if (queryIdx >= 0)
                clean = clean.Substring(0, queryIdx);

            if (clean.Length > 1 && clean.EndsWith("/", StringComparison.Ordinal))
                clean = clean.TrimEnd('/');

            return clean.ToLowerInvariant();
        }

        private static readonly Regex MarkdownEndpointHeadingRegex = new(
            @"^###\s+`(GET|POST)\s+(/api/v1/host/[a-z0-9/_-]+)(?:\?[^`]*)?`",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>
        /// Extrae los pares <c>(Method, Path)</c> documentados en cabeceras <c>### `METHOD /api/v1/host/...`</c> de <c>docs/host-api.md</c>.
        /// </summary>
        public static IReadOnlyList<(string Method, string Path)> ExtractDocumentedRoutes(string markdownContent)
        {
            if (string.IsNullOrWhiteSpace(markdownContent))
                return Array.Empty<(string, string)>();

            var list = new List<(string Method, string Path)>();
            foreach (Match match in MarkdownEndpointHeadingRegex.Matches(markdownContent))
            {
                string method = match.Groups[1].Value.ToUpperInvariant();
                string path = NormalizePath(match.Groups[2].Value);
                list.Add((method, path));
            }

            return list;
        }
    }
}
