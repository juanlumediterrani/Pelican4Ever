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

    // ── Mapa ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tipo de celda para el visor de mapa del panel web.
    /// El valor numérico se serializa directamente al JSON para reducir el tamaño del payload.
    /// </summary>
    public enum MapTileKind : byte
    {
        Unknown    = 0,
        Open       = 1,   // suelo transitable
        Blocked    = 2,   // muro / objeto sólido
        Water      = 3,
        Building   = 4,
        Path       = 5,
        Grass      = 6,
        Tree       = 7,
        Warp       = 8,   // portal / salida
        Mine       = 9,
        NPC        = 10,
        Player     = 11,
    }

    /// <summary>
    /// Datos de un jugador online solapados sobre el mapa.
    /// </summary>
    public sealed class MapPlayerMarker
    {
        public long   PlayerId     { get; set; }
        public string Name         { get; set; } = string.Empty;
        public int    TileX        { get; set; }
        public int    TileY        { get; set; }
        public bool   IsHost       { get; set; }
    }

    /// <summary>
    /// Información de un warp (salida a otro mapa) para mostrarlo en el visor.
    /// </summary>
    public sealed class MapWarpInfo
    {
        public int    TileX      { get; set; }
        public int    TileY      { get; set; }
        public string TargetMap  { get; set; } = string.Empty;
        public int    TargetX    { get; set; }
        public int    TargetY    { get; set; }
    }

    /// <summary>
    /// Snapshot de una localización para el visor de mapa del panel web
    /// (<c>GET /api/v1/host/map?location=Town</c>).
    /// <para>
    /// <c>Tiles</c> es un array plano, fila mayor (row-major): índice = Y * Width + X.
    /// Cada byte es un valor de <see cref="MapTileKind"/>.
    /// </para>
    /// </summary>
    public sealed class HostMapDto
    {
        public string            LocationName { get; set; } = string.Empty;
        public int               Width        { get; set; }
        public int               Height       { get; set; }
        public byte[]            Tiles        { get; set; } = Array.Empty<byte>();
        public List<MapPlayerMarker> Players  { get; set; } = new();
        public List<MapWarpInfo>     Warps    { get; set; } = new();
        /// <summary>Nombres de las localizaciones disponibles para el selector del visor.</summary>
        public List<string>      AvailableMaps { get; set; } = new();
        public string            Season       { get; set; } = string.Empty;
        public int               GameDay      { get; set; }
    }

    // ── Anuncio ─────────────────────────────────────────────────────────────────

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
    /// Petición de préstamo de dibujado para <c>POST /api/v1/host/render</c>. <c>Minutes = 0</c> lo retira.
    /// </summary>
    public sealed class HostRenderRequestDto
    {
        /// <summary>Quién lo pide (1-40 caracteres [A-Za-z0-9._-]); cada uno tiene como mucho un préstamo.</summary>
        public string Requester { get; set; } = "api";
        /// <summary>Minutos de dibujado (1-120, se recorta); 0 retira el préstamo del solicitante.</summary>
        public int Minutes { get; set; } = 10;
    }

    /// <summary>Préstamo vigente en <see cref="HostRenderStatusDto"/>.</summary>
    public sealed class HostRenderLeaseDto
    {
        public string Requester { get; set; } = string.Empty;
        public DateTime ExpiresAtUtc { get; set; }
    }

    /// <summary>Estado del dibujado del host para <c>GET/POST /api/v1/host/render</c>.</summary>
    public sealed class HostRenderStatusDto
    {
        /// <summary><c>HeadlessRendering</c> en config.json.</summary>
        public bool HeadlessRenderingEnabled { get; set; }
        /// <summary>Si el host está dibujando ahora.</summary>
        public bool Drawing { get; set; }
        /// <summary>Motivo (<c>Disabled</c>, <c>Attended</c>, <c>WorldNotReady</c>, <c>DrawRequiredMenu</c>, <c>Lease</c>, <c>Suppressed</c>).</summary>
        public string Reason { get; set; } = string.Empty;
        public List<HostRenderLeaseDto> Leases { get; set; } = new();
        /// <summary>Mensaje de la última petición (vacío en GET).</summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>Petición de <c>POST /api/v1/host/command</c>: un comando de la lista de permitidos y sus argumentos.</summary>
    public sealed class HostCommandRequestDto
    {
        /// <summary>Nombre del comando de consola (p. ej. <c>p4e_status</c>).</summary>
        public string Command { get; set; } = string.Empty;

        /// <summary>Argumentos, ya separados.</summary>
        public List<string> Args { get; set; } = new();
    }

    /// <summary>Un comando de la lista de permitidos, en <c>GET /api/v1/host/commands</c>.</summary>
    public sealed class HostCommandInfoDto
    {
        public string Command { get; set; } = string.Empty;
        /// <summary><c>Read</c>, <c>Safe</c> o <c>Sensitive</c>.</summary>
        public string Risk { get; set; } = string.Empty;
        public string Usage { get; set; } = string.Empty;
        /// <summary>Si el comando está registrado en este servidor ahora.</summary>
        public bool Available { get; set; }
    }

    public sealed class HostCommandCatalogDto
    {
        public List<HostCommandInfoDto> Commands { get; set; } = new();
    }

    /// <summary>Resultado de <c>POST /api/v1/host/command</c>.</summary>
    public sealed class HostCommandResultDto
    {
        public bool Ok { get; set; }
        public string Command { get; set; } = string.Empty;
        public string Risk { get; set; } = string.Empty;
        /// <summary>Líneas que el comando escribió en el registro mientras se ejecutaba (acotadas).</summary>
        public List<string> Output { get; set; } = new();
        public string Message { get; set; } = string.Empty;
        public int ExecutedOnThreadId { get; set; }
        public long? EventId { get; set; }
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
            new("GET",  "/api/v1/host/map",               MutatesGameState: false, "Snapshot tile-by-tile de una localización (?location=Town) para el visor de mapa del panel web."),
            new("POST", "/api/v1/host/festival/start",    MutatesGameState: true,  "Inicia la siguiente tanda del evento principal con los jugadores apuntados (/evento empezar)."),
            new("POST", "/api/v1/host/festival/ceremony", MutatesGameState: true,  "Inicia la ceremonia comunitaria en vivo (TASK-702) con los vecinos presentes en el recinto."),
            new("POST", "/api/v1/host/announce",          MutatesGameState: true,  "Envía un aviso global por chat y/o en pantalla (HUD) a los jugadores."),
            new("POST", "/api/v1/host/rest",              MutatesGameState: true,  "Registra el descanso diario del anfitrión sin desconectar la sesión (TASK-804)."),
            new("POST", "/api/v1/host/restart",           MutatesGameState: true,  "Programa o cancela un reinicio controlado del servidor con preaviso (TASK-804)."),
            new("GET",  "/api/v1/host/events",            MutatesGameState: false, "Devuelve la lista incremental de sucesos desde el cursor ?since=N."),
            new("GET",  "/api/v1/host/commands",          MutatesGameState: false, "Lista cerrada de comandos de consola de Pelican que se pueden ejecutar a distancia, con su riesgo."),
            new("POST", "/api/v1/host/command",           MutatesGameState: true,  "Ejecuta un comando de consola de la lista de permitidos y devuelve lo que escribe en el registro."),
            new("GET",  "/api/v1/host/render",            MutatesGameState: false, "Estado del dibujado del host (servidor sin dibujado) y préstamos vigentes."),
            new("POST", "/api/v1/host/render",            MutatesGameState: false, "Pide o retira un préstamo de dibujado con caducidad (Voyager, Putre, diagnóstico).")
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
