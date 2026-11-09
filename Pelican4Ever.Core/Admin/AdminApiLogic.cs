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

        /// <summary>
        /// Genera el contenido HTML de la SPA del Dashboard web integrado.
        /// Versión 2: panel con pestañas (Resumen, Jugadores, Residencias, Festival, Mapa, Acciones).
        /// </summary>
        public static string GetDashboardHtml(string defaultToken = "")
        {
            return @"<!DOCTYPE html>
<html lang=""es"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>Pelican 4Ever — Panel de Control</title>
  <style>
    /* ── Variables ─────────────────────────────────────────── */
    :root {
      --bg:           #12161c;
      --surface:      #1c2230;
      --card-bg:      #222b3a;
      --border:       #2e3a4e;
      --accent:       #6ca0ff;
      --accent-dim:   rgba(108,160,255,.15);
      --success:      #4ac97e;
      --warning:      #e8b84b;
      --danger:       #f06a60;
      --text:         #dde6f0;
      --text-muted:   #7a8fa6;
      --tab-active:   #6ca0ff;
      --shadow:       0 4px 24px rgba(0,0,0,.45);
    }

    /* ── Reset & base ───────────────────────────────────────── */
    *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
    body {
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
      background: var(--bg);
      color: var(--text);
      min-height: 100vh;
    }

    /* ── Header ─────────────────────────────────────────────── */
    header {
      background: var(--surface);
      border-bottom: 1px solid var(--border);
      padding: 14px 24px;
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 12px;
      flex-wrap: wrap;
      box-shadow: var(--shadow);
    }
    .header-brand { display: flex; flex-direction: column; }
    .header-brand h1 { font-size: 1.35rem; color: var(--accent); letter-spacing: .3px; }
    .header-brand small { font-size: .78rem; color: var(--text-muted); margin-top: 2px; }
    .header-controls { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
    .header-controls input[type=""password""] {
      background: var(--bg);
      border: 1px solid var(--border);
      color: var(--text);
      padding: 8px 12px;
      border-radius: 7px;
      width: 230px;
      font-size: .9rem;
    }
    .header-controls input:focus { outline: 2px solid var(--accent); }
    .ping-dot {
      width: 9px; height: 9px; border-radius: 50%;
      background: var(--text-muted);
      transition: background .3s;
    }
    .ping-dot.ok  { background: var(--success); }
    .ping-dot.err { background: var(--danger); }

    /* ── Tab nav ────────────────────────────────────────────── */
    .tab-nav {
      display: flex;
      gap: 0;
      background: var(--surface);
      border-bottom: 1px solid var(--border);
      padding: 0 24px;
      overflow-x: auto;
    }
    .tab-nav button {
      background: none;
      border: none;
      color: var(--text-muted);
      padding: 13px 20px 11px;
      font-size: .92rem;
      font-weight: 500;
      cursor: pointer;
      border-bottom: 3px solid transparent;
      white-space: nowrap;
      transition: color .2s, border-color .2s;
    }
    .tab-nav button:hover { color: var(--text); }
    .tab-nav button.active {
      color: var(--tab-active);
      border-bottom-color: var(--tab-active);
    }
    .tab-nav .tab-icon { margin-right: 6px; font-size: 1rem; }

    /* ── Main layout ────────────────────────────────────────── */
    main { max-width: 1280px; margin: 0 auto; padding: 24px; }
    .tab-panel { display: none; }
    .tab-panel.active { display: block; }

    /* ── KPI Cards ──────────────────────────────────────────── */
    .kpi-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
      gap: 14px;
      margin-bottom: 28px;
    }
    .kpi-card {
      background: var(--card-bg);
      border: 1px solid var(--border);
      border-radius: 10px;
      padding: 18px 20px;
      display: flex;
      flex-direction: column;
      gap: 6px;
    }
    .kpi-card .kpi-label {
      font-size: .75rem;
      text-transform: uppercase;
      letter-spacing: .6px;
      color: var(--text-muted);
    }
    .kpi-card .kpi-value {
      font-size: 1.9rem;
      font-weight: 700;
      line-height: 1;
    }
    .kpi-card .kpi-sub { font-size: .82rem; color: var(--text-muted); }

    /* ── Info rows (Resumen del día) ────────────────────────── */
    .info-row {
      background: var(--card-bg);
      border: 1px solid var(--border);
      border-radius: 10px;
      padding: 18px 20px;
      margin-bottom: 14px;
    }
    .info-row h3 {
      font-size: .8rem;
      text-transform: uppercase;
      letter-spacing: .6px;
      color: var(--text-muted);
      margin-bottom: 10px;
    }
    .info-row .info-body {
      font-size: .95rem;
      display: flex;
      flex-wrap: wrap;
      gap: 16px 32px;
    }
    .info-row .info-pair { display: flex; flex-direction: column; gap: 2px; }
    .info-row .info-pair .lbl { font-size: .75rem; color: var(--text-muted); }
    .info-row .info-pair .val { font-weight: 600; }

    /* ── Festival banner ─────────────────────────────────────── */
    .festival-banner {
      background: linear-gradient(135deg, rgba(232,184,75,.12), rgba(108,160,255,.08));
      border: 1px solid rgba(232,184,75,.4);
      border-radius: 10px;
      padding: 18px 22px;
      margin-bottom: 22px;
      display: flex;
      align-items: center;
      gap: 16px;
    }
    .festival-banner .festival-icon { font-size: 2.2rem; }
    .festival-banner .festival-info h2 { font-size: 1.1rem; color: var(--warning); }
    .festival-banner .festival-info p { font-size: .86rem; color: var(--text-muted); margin-top: 3px; }

    /* ── Event log ──────────────────────────────────────────── */
    .event-log {
      background: var(--card-bg);
      border: 1px solid var(--border);
      border-radius: 10px;
      overflow: hidden;
    }
    .event-log-header {
      padding: 12px 18px;
      border-bottom: 1px solid var(--border);
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-size: .85rem;
      color: var(--text-muted);
    }
    .event-log-body {
      max-height: 280px;
      overflow-y: auto;
      font-size: .83rem;
      font-family: 'JetBrains Mono', 'Fira Code', 'Cascadia Code', monospace;
    }
    .event-log-body .event-item {
      padding: 8px 18px;
      border-bottom: 1px solid var(--border);
      display: flex;
      gap: 12px;
    }
    .event-log-body .event-item:last-child { border-bottom: none; }
    .event-item .ev-time { color: var(--text-muted); min-width: 54px; }
    .event-item .ev-msg { color: var(--text); }

    /* ── Tables ─────────────────────────────────────────────── */
    .section-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 14px;
    }
    .section-header h2 { font-size: 1.1rem; }
    .table-wrap { overflow-x: auto; }
    table {
      width: 100%;
      border-collapse: collapse;
      background: var(--card-bg);
      border-radius: 10px;
      overflow: hidden;
      border: 1px solid var(--border);
    }
    th, td {
      padding: 11px 14px;
      text-align: left;
      border-bottom: 1px solid var(--border);
      font-size: .88rem;
      white-space: nowrap;
    }
    th {
      background: rgba(0,0,0,.25);
      color: var(--text-muted);
      font-size: .75rem;
      text-transform: uppercase;
      letter-spacing: .5px;
      font-weight: 600;
    }
    tbody tr:last-child td { border-bottom: none; }
    tbody tr:hover { background: rgba(255,255,255,.03); }

    /* ── Badges ─────────────────────────────────────────────── */
    .badge {
      display: inline-block;
      padding: 2px 9px;
      border-radius: 20px;
      font-size: .72rem;
      font-weight: 700;
      letter-spacing: .4px;
    }
    .badge-online   { background: rgba(74,201,126,.18); color: var(--success); }
    .badge-offline  { background: rgba(122,143,166,.15); color: var(--text-muted); }
    .badge-warning  { background: rgba(232,184,75,.18); color: var(--warning); }
    .badge-accent   { background: var(--accent-dim); color: var(--accent); }
    .badge-festival { background: rgba(232,184,75,.2); color: var(--warning); }

    /* ── Buttons ─────────────────────────────────────────────── */
    .btn {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      background: var(--accent);
      color: #fff;
      border: none;
      padding: 9px 16px;
      border-radius: 7px;
      cursor: pointer;
      font-weight: 600;
      font-size: .88rem;
      transition: opacity .2s;
    }
    .btn:hover { opacity: .85; }
    .btn-sm { padding: 5px 11px; font-size: .8rem; }
    .btn-ghost {
      background: transparent;
      border: 1px solid var(--border);
      color: var(--text-muted);
    }
    .btn-ghost:hover { background: var(--card-bg); color: var(--text); }
    .btn-warning { background: var(--warning); color: #111; }
    .btn-danger  { background: var(--danger); }
    .actions-bar { display: flex; flex-wrap: wrap; gap: 10px; margin-bottom: 22px; }

    /* ── Map placeholder ─────────────────────────────────────── */
    .map-placeholder {
      background: var(--card-bg);
      border: 2px dashed var(--border);
      border-radius: 12px;
      min-height: 440px;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 14px;
      color: var(--text-muted);
    }
    .map-placeholder .map-icon { font-size: 4rem; opacity: .4; }
    .map-placeholder h2 { font-size: 1.2rem; color: var(--text); opacity: .6; }
    .map-placeholder p { font-size: .88rem; max-width: 420px; text-align: center; line-height: 1.6; }
    .map-placeholder .map-meta {
      margin-top: 6px;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 8px;
      padding: 10px 20px;
      font-size: .8rem;
      display: flex;
      gap: 20px;
      flex-wrap: wrap;
    }
    .map-meta span { color: var(--text-muted); }
    .map-meta strong { color: var(--accent); }

    /* ── Announce form ───────────────────────────────────────── */
    .announce-form {
      background: var(--card-bg);
      border: 1px solid var(--border);
      border-radius: 10px;
      padding: 20px 22px;
      margin-bottom: 22px;
    }
    .announce-form h3 { font-size: .85rem; color: var(--text-muted); text-transform: uppercase; margin-bottom: 14px; }
    .form-row { display: flex; gap: 10px; flex-wrap: wrap; }
    .form-row input[type=""text""], .form-row select {
      flex: 1 1 240px;
      background: var(--bg);
      border: 1px solid var(--border);
      color: var(--text);
      padding: 9px 13px;
      border-radius: 7px;
      font-size: .9rem;
    }
    .form-row input:focus, .form-row select:focus { outline: 2px solid var(--accent); }

    /* ── Rollover info ───────────────────────────────────────── */
    .rollover-card {
      background: var(--card-bg);
      border: 1px solid var(--border);
      border-radius: 10px;
      padding: 18px 22px;
      margin-bottom: 14px;
    }
    .rollover-card h3 { font-size: .8rem; color: var(--text-muted); text-transform: uppercase; margin-bottom: 10px; }
    .rollover-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 10px 24px; }
    .rollover-item { display: flex; flex-direction: column; gap: 2px; }
    .rollover-item .rl-label { font-size: .74rem; color: var(--text-muted); }
    .rollover-item .rl-value { font-size: .95rem; font-weight: 600; }

    /* ── Scrollbar ───────────────────────────────────────────── */
    ::-webkit-scrollbar { width: 6px; height: 6px; }
    ::-webkit-scrollbar-track { background: transparent; }
    ::-webkit-scrollbar-thumb { background: var(--border); border-radius: 3px; }
  </style>
</head>
<body>

  <!-- ── Header ─────────────────────────────────────────────── -->
  <header>
    <div class=""header-brand"">
      <h1>🌾 Pelican 4Ever</h1>
      <small>Panel de Control · Servidor Persistente Stardew Valley 1.6</small>
    </div>
    <div class=""header-controls"">
      <div class=""ping-dot"" id=""pingDot"" title=""Estado de conexión""></div>
      <small id=""lastUpdate"" style=""color:var(--text-muted); font-size:.78rem"">—</small>
      <input type=""password"" id=""tokenInput"" placeholder=""Bearer Token"" value=""" + System.Net.WebUtility.HtmlEncode(defaultToken) + @""" autocomplete=""off"">
      <button class=""btn btn-ghost btn-sm"" onclick=""refreshAll()"">↻ Actualizar</button>
    </div>
  </header>

  <!-- ── Tab nav ────────────────────────────────────────────── -->
  <nav class=""tab-nav"" role=""tablist"">
    <button class=""active"" role=""tab"" onclick=""showTab('tab-overview',this)""><span class=""tab-icon"">📋</span>Resumen</button>
    <button role=""tab"" onclick=""showTab('tab-players',this)""><span class=""tab-icon"">👥</span>Jugadores</button>
    <button role=""tab"" onclick=""showTab('tab-residences',this)""><span class=""tab-icon"">🏠</span>Residencias</button>
    <button role=""tab"" onclick=""showTab('tab-festival',this)""><span class=""tab-icon"">🎪</span>Festival</button>
    <button role=""tab"" onclick=""showTab('tab-map',this)""><span class=""tab-icon"">🗺️</span>Mapa</button>
    <button role=""tab"" onclick=""showTab('tab-actions',this)""><span class=""tab-icon"">⚙️</span>Acciones</button>
  </nav>

  <main>

    <!-- ══════════════════════════════════════════════════════ -->
    <!-- TAB 1: RESUMEN                                         -->
    <!-- ══════════════════════════════════════════════════════ -->
    <section id=""tab-overview"" class=""tab-panel active"" role=""tabpanel"">

      <!-- KPI bar -->
      <div class=""kpi-grid"">
        <div class=""kpi-card"">
          <span class=""kpi-label"">📅 Fecha de Juego</span>
          <span class=""kpi-value"" id=""kpi-date"" style=""font-size:1.3rem"">—</span>
          <span class=""kpi-sub"" id=""kpi-season"">—</span>
        </div>
        <div class=""kpi-card"">
          <span class=""kpi-label"">⏰ Hora en Juego</span>
          <span class=""kpi-value"" id=""kpi-gametime"" style=""font-size:1.4rem"">—</span>
          <span class=""kpi-sub"" id=""kpi-nighthold"">—</span>
        </div>
        <div class=""kpi-card"">
          <span class=""kpi-label"">👥 Jugadores Online</span>
          <span class=""kpi-value"" id=""kpi-online"">—</span>
          <span class=""kpi-sub"" id=""kpi-registered"">de — registrados</span>
        </div>
        <div class=""kpi-card"">
          <span class=""kpi-label"">🏠 Residencias</span>
          <span class=""kpi-value"" id=""kpi-doors"">—</span>
          <span class=""kpi-sub"" id=""kpi-doors-free"">— libres</span>
        </div>
        <div class=""kpi-card"">
          <span class=""kpi-label"">🛒 Mercado Vecinal</span>
          <span class=""kpi-value"" id=""kpi-market"">—</span>
          <span class=""kpi-sub"">Ofertas activas</span>
        </div>
        <div class=""kpi-card"">
          <span class=""kpi-label"">⏳ Próximo Rollover</span>
          <span class=""kpi-value"" id=""kpi-rollover"" style=""font-size:1.3rem"">—</span>
          <span class=""kpi-sub"" id=""kpi-rollover-time"">—</span>
        </div>
      </div>

      <!-- Festival hoy -->
      <div id=""festival-banner"" class=""festival-banner"" style=""display:none"">
        <span class=""festival-icon"">🎉</span>
        <div class=""festival-info"">
          <h2 id=""fest-name"">—</h2>
          <p id=""fest-detail"">—</p>
        </div>
      </div>

      <!-- Clock detail -->
      <div class=""info-row"">
        <h3>🕐 Reloj del Servidor</h3>
        <div class=""info-body"">
          <div class=""info-pair""><span class=""lbl"">Hora Real (UTC)</span><span class=""val"" id=""ov-realtime"">—</span></div>
          <div class=""info-pair""><span class=""lbl"">Hora Local</span><span class=""val"" id=""ov-localtime"">—</span></div>
          <div class=""info-pair""><span class=""lbl"">Zona Horaria</span><span class=""val"" id=""ov-timezone"">—</span></div>
          <div class=""info-pair""><span class=""lbl"">Día Total</span><span class=""val"" id=""ov-totalday"">—</span></div>
          <div class=""info-pair""><span class=""lbl"">Servidor Multijugador</span><span class=""val"" id=""ov-mp"">—</span></div>
          <div class=""info-pair""><span class=""lbl"">Partida Guardada</span><span class=""val"" id=""ov-save"">—</span></div>
        </div>
      </div>

      <!-- Last rollover -->
      <div class=""rollover-card"">
        <h3>📦 Último Rollover Diario</h3>
        <div class=""rollover-grid"">
          <div class=""rollover-item""><span class=""rl-label"">Día de juego</span><span class=""rl-value"" id=""roll-day"">—</span></div>
          <div class=""rollover-item""><span class=""rl-label"">Resultado</span><span class=""rl-value"" id=""roll-outcome"">—</span></div>
          <div class=""rollover-item""><span class=""rl-label"">Backup</span><span class=""rl-value"" id=""roll-backup"">—</span></div>
          <div class=""rollover-item""><span class=""rl-label"">Fecha Backup (UTC)</span><span class=""rl-value"" id=""roll-backup-date"">—</span></div>
        </div>
      </div>

      <!-- Event log -->
      <div class=""event-log"">
        <div class=""event-log-header"">
          <span>📜 Eventos Recientes</span>
          <button class=""btn btn-ghost btn-sm"" onclick=""fetchEvents()"">↻</button>
        </div>
        <div class=""event-log-body"" id=""eventLog"">
          <div id=""eventLogEmpty"" class=""event-item""><span class=""ev-time"">—</span><span class=""ev-msg"" style=""color:var(--text-muted)"">Sin eventos cargados</span></div>
        </div>
      </div>
    </section>

    <!-- ══════════════════════════════════════════════════════ -->
    <!-- TAB 2: JUGADORES                                       -->
    <!-- ══════════════════════════════════════════════════════ -->
    <section id=""tab-players"" class=""tab-panel"" role=""tabpanel"">
      <div class=""section-header"">
        <h2>Jugadores Registrados</h2>
        <button class=""btn btn-ghost btn-sm"" onclick=""fetchPlayers()"">↻ Actualizar</button>
      </div>
      <div class=""table-wrap"">
        <table>
          <thead>
            <tr>
              <th>Jugador</th>
              <th>Estado</th>
              <th>Ubicación</th>
              <th>Residencia</th>
              <th>Descanso</th>
              <th>Saciedad</th>
              <th>Bonos spa</th>
              <th>Oro Mercado</th>
              <th>Festival</th>
              <th>Reclamado</th>
            </tr>
          </thead>
          <tbody id=""playersTable"">
            <tr><td colspan=""10"" style=""text-align:center;color:var(--text-muted);padding:28px"">Cargando jugadores…</td></tr>
          </tbody>
        </table>
      </div>
    </section>

    <!-- ══════════════════════════════════════════════════════ -->
    <!-- TAB 3: RESIDENCIAS                                     -->
    <!-- ══════════════════════════════════════════════════════ -->
    <section id=""tab-residences"" class=""tab-panel"" role=""tabpanel"">
      <div class=""section-header"">
        <h2>Catálogo de Residencias</h2>
        <button class=""btn btn-ghost btn-sm"" onclick=""fetchStatus()"">↻ Actualizar</button>
      </div>
      <div class=""table-wrap"">
        <table>
          <thead>
            <tr>
              <th>ID Puerta</th>
              <th>Ubicación</th>
              <th>Coordenadas</th>
              <th>Hueco</th>
              <th>Propietario</th>
              <th>Tipo Granja</th>
              <th>Política</th>
            </tr>
          </thead>
          <tbody id=""doorsTable"">
            <tr><td colspan=""7"" style=""text-align:center;color:var(--text-muted);padding:28px"">Cargando residencias…</td></tr>
          </tbody>
        </table>
      </div>
    </section>

    <!-- ══════════════════════════════════════════════════════ -->
    <!-- TAB 4: FESTIVAL                                        -->
    <!-- ══════════════════════════════════════════════════════ -->
    <section id=""tab-festival"" class=""tab-panel"" role=""tabpanel"">
      <div class=""section-header"">
        <h2>Festival del Día</h2>
        <button class=""btn btn-ghost btn-sm"" onclick=""fetchFestival()"">↻ Actualizar</button>
      </div>

      <div id=""fest-no-festival"" class=""info-row"" style=""text-align:center;color:var(--text-muted);padding:40px"">
        No hay festival hoy.
      </div>

      <div id=""fest-detail-panel"" style=""display:none"">
        <div class=""info-row"">
          <h3>ℹ️ Información del Festival</h3>
          <div class=""info-body"">
            <div class=""info-pair""><span class=""lbl"">Nombre</span><span class=""val"" id=""fst-name"">—</span></div>
            <div class=""info-pair""><span class=""lbl"">Temporada / Día</span><span class=""val"" id=""fst-date"">—</span></div>
            <div class=""info-pair""><span class=""lbl"">Ubicación</span><span class=""val"" id=""fst-loc"">—</span></div>
            <div class=""info-pair""><span class=""lbl"">Ventana</span><span class=""val"" id=""fst-window"">—</span></div>
            <div class=""info-pair""><span class=""lbl"">Estado ventana</span><span class=""val"" id=""fst-window-open"">—</span></div>
            <div class=""info-pair""><span class=""lbl"">Evento principal</span><span class=""val"" id=""fst-main"">—</span></div>
            <div class=""info-pair""><span class=""lbl"">Ceremonia</span><span class=""val"" id=""fst-ceremony"">—</span></div>
            <div class=""info-pair""><span class=""lbl"">Tandas jugadas</span><span class=""val"" id=""fst-runs"">—</span></div>
          </div>
        </div>

        <div class=""kpi-grid"">
          <div class=""kpi-card"">
            <span class=""kpi-label"">✅ Presentes</span>
            <span class=""kpi-value"" id=""fst-present-count"">—</span>
          </div>
          <div class=""kpi-card"">
            <span class=""kpi-label"">📝 Apuntados</span>
            <span class=""kpi-value"" id=""fst-signed-count"">—</span>
          </div>
          <div class=""kpi-card"">
            <span class=""kpi-label"">🎮 Han Jugado</span>
            <span class=""kpi-value"" id=""fst-played-count"">—</span>
          </div>
        </div>

        <div class=""actions-bar"">
          <button class=""btn"" onclick=""callAction('/api/v1/host/festival/start')"">▶ Iniciar Tanda</button>
          <button class=""btn btn-warning"" onclick=""callAction('/api/v1/host/festival/ceremony')"">🌟 Iniciar Ceremonia</button>
        </div>
      </div>
    </section>

    <!-- ══════════════════════════════════════════════════════ -->
    <!-- TAB 5: MAPA                                            -->
    <!-- ══════════════════════════════════════════════════════ -->
    <section id=""tab-map"" class=""tab-panel"" role=""tabpanel"">
      <div class=""section-header"" style=""margin-bottom:12px"">
        <h2>Mapa del Mundo</h2>
        <div style=""display:flex;gap:8px;align-items:center;flex-wrap:wrap"">
          <select id=""mapLocationSelect"" style=""background:var(--bg);border:1px solid var(--border);color:var(--text);padding:7px 11px;border-radius:7px;font-size:.88rem"">
            <option value=""Town"">Town</option>
          </select>
          <button class=""btn btn-ghost btn-sm"" onclick=""loadMap()"">↻ Cargar</button>
          <button class=""btn btn-ghost btn-sm"" onclick=""mapResetView()"">⊙ Centrar</button>
          <span id=""mapCoords"" style=""font-size:.78rem;color:var(--text-muted);min-width:90px"">—</span>
          <span id=""mapZoomLabel"" style=""font-size:.78rem;color:var(--text-muted)"">100%</span>
        </div>
      </div>

      <!-- Leyenda -->
      <div style=""display:flex;flex-wrap:wrap;gap:6px 14px;margin-bottom:12px;font-size:.76rem;color:var(--text-muted)"">
        <span><span style=""display:inline-block;width:12px;height:12px;background:#3a4a3a;border-radius:2px;vertical-align:middle;margin-right:4px""></span>Suelo</span>
        <span><span style=""display:inline-block;width:12px;height:12px;background:#6ca0ff22;border:1px solid #6ca0ff55;border-radius:2px;vertical-align:middle;margin-right:4px""></span>Agua</span>
        <span><span style=""display:inline-block;width:12px;height:12px;background:#3d4a2a;border-radius:2px;vertical-align:middle;margin-right:4px""></span>Hierba</span>
        <span><span style=""display:inline-block;width:12px;height:12px;background:#2a3244;border-radius:2px;vertical-align:middle;margin-right:4px""></span>Bloqueado</span>
        <span><span style=""display:inline-block;width:12px;height:12px;background:#4a3520;border-radius:2px;vertical-align:middle;margin-right:4px""></span>Edificio</span>
        <span><span style=""display:inline-block;width:12px;height:12px;background:#2d4a1e;border-radius:2px;vertical-align:middle;margin-right:4px""></span>Árbol</span>
        <span><span style=""display:inline-block;width:12px;height:12px;background:#786020;border-radius:2px;vertical-align:middle;margin-right:4px""></span>Camino</span>
        <span><span style=""display:inline-block;width:12px;height:12px;background:#a06030;border-radius:2px;vertical-align:middle;margin-right:4px""></span>Warp</span>
        <span><span style=""display:inline-block;width:12px;height:12px;background:#b0904088;border:1px solid #e8c060;border-radius:2px;vertical-align:middle;margin-right:4px""></span>NPC</span>
        <span><span style=""display:inline-block;width:12px;height:12px;background:#4ac97e88;border:1px solid #4ac97e;border-radius:2px;vertical-align:middle;margin-right:4px""></span>Jugador</span>
      </div>

      <!-- Canvas viewport -->
      <div id=""mapViewport"" style=""
        position:relative;
        width:100%;
        height:600px;
        background:var(--bg);
        border:1px solid var(--border);
        border-radius:10px;
        overflow:hidden;
        cursor:grab;
        user-select:none;
      "">
        <canvas id=""mapCanvas"" style=""position:absolute;top:0;left:0;image-rendering:pixelated""></canvas>
        <!-- Overlay de carga -->
        <div id=""mapLoadOverlay"" style=""
          position:absolute;inset:0;display:flex;flex-direction:column;
          align-items:center;justify-content:center;gap:12px;
          background:rgba(18,22,28,.85);border-radius:10px;z-index:10;
        "">
          <span style=""font-size:2.5rem"">🗺️</span>
          <span style=""color:var(--text-muted);font-size:.9rem"">Selecciona una localización y pulsa Cargar</span>
        </div>
        <!-- Tooltip -->
        <div id=""mapTooltip"" style=""
          display:none;position:absolute;pointer-events:none;
          background:rgba(18,22,28,.92);border:1px solid var(--border);
          border-radius:6px;padding:5px 9px;font-size:.78rem;color:var(--text);
          white-space:nowrap;z-index:20;
        ""></div>
      </div>

      <!-- Player positions list -->
      <div class=""info-row"" style=""margin-top:14px"">
        <h3>📍 Jugadores en esta localización</h3>
        <div id=""map-player-positions"" class=""info-body"" style=""gap:10px 28px"">
          <span style=""color:var(--text-muted)"">Carga un mapa primero</span>
        </div>
      </div>
    </section>

    <!-- ══════════════════════════════════════════════════════ -->
    <!-- TAB 6: ACCIONES                                        -->
    <!-- ══════════════════════════════════════════════════════ -->
    <section id=""tab-actions"" class=""tab-panel"" role=""tabpanel"">
      <h2 style=""margin-bottom:18px"">Acciones del Servidor</h2>

      <!-- Anuncio -->
      <div class=""announce-form"">
        <h3>📢 Enviar Anuncio Global</h3>
        <div class=""form-row"">
          <input type=""text"" id=""announceMsg"" placeholder=""Mensaje para todos los jugadores…"">
          <select id=""announceChannel"">
            <option value=""both"">Chat + HUD</option>
            <option value=""chat"">Solo Chat</option>
            <option value=""hud"">Solo HUD</option>
          </select>
          <button class=""btn"" onclick=""sendAnnounce()"">📤 Enviar</button>
        </div>
      </div>

      <!-- Rollover / reinicio -->
      <div class=""actions-bar"">
        <button class=""btn btn-danger"" onclick=""callAction('/api/rollover')"">Forzar cierre del día</button>
        <button class=""btn btn-ghost"" onclick=""callAction('/api/mines/reset')"">Regenerar minas desocupadas</button>
        <button class=""btn btn-warning"" onclick=""callAction('/api/v1/host/rest')"">😴 Registrar Descanso Anfitrión</button>
        <button class=""btn btn-danger"" onclick=""scheduleRestart()"">🔁 Programar Reinicio</button>
        <button class=""btn btn-ghost"" onclick=""callAction('/api/v1/host/restart', 'POST', {cancel:true})"">✖ Cancelar Reinicio</button>
      </div>

      <!-- Restart form -->
      <div class=""announce-form"">
        <h3>🔁 Programar Reinicio Controlado</h3>
        <div class=""form-row"">
          <input type=""text"" id=""restartReason"" placeholder=""Motivo del reinicio…"">
          <input type=""number"" id=""restartWarnMins"" placeholder=""Aviso (minutos)"" style=""max-width:150px"">
          <button class=""btn btn-warning"" onclick=""scheduleRestart()"">Programar</button>
        </div>
      </div>
    </section>

  </main>

  <script>
    /* ── Estado global ───────────────────────────────────── */
    let lastEventCursor = 0;

    /* ── Auth ────────────────────────────────────────────── */
    function getHeaders(extra) {
      const token = document.getElementById('tokenInput').value.trim();
      return Object.assign(token ? { 'Authorization': 'Bearer ' + token } : {}, extra || {});
    }

    /* ── Tab switching ───────────────────────────────────── */
    function showTab(id, btn) {
      document.querySelectorAll('.tab-panel').forEach(p => p.classList.remove('active'));
      document.querySelectorAll('.tab-nav button').forEach(b => b.classList.remove('active'));
      document.getElementById(id).classList.add('active');
      btn.classList.add('active');
      if (id === 'tab-map') { mapResize(); loadMap(); }
    }

    /* ── Helpers ─────────────────────────────────────────── */
    function fmtTime(t) {
      if (!t) return '—';
      const h = Math.floor(t / 100), m = t % 100;
      return `${String(h).padStart(2,'0')}:${String(m).padStart(2,'0')}`;
    }
    function badge(text, cls) { return `<span class=""badge badge-${cls}"">${text}</span>`; }
    function esc(s) {
      return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/""/g,'&quot;').replace(/'/g,'&#39;');
    }

    async function readJsonResponse(res) {
      const json = await res.json();
      if (!res.ok || json.Ok === false || json.success === false)
        throw new Error(json.Message || json.message || json.error || 'HTTP ' + res.status);
      return json;
    }

    /* ── Ping dot ────────────────────────────────────────── */
    function setPing(ok) {
      const d = document.getElementById('pingDot');
      d.className = 'ping-dot ' + (ok ? 'ok' : 'err');
    }
    function setLastUpdate() {
      document.getElementById('lastUpdate').textContent =
        'Actualizado: ' + new Date().toLocaleTimeString('es-ES');
    }

    /* ────────────────────────────────────────────────────── */
    /*  MAP VIEWER                                             */
    /* ────────────────────────────────────────────────────── */

    // Paleta de colores por MapTileKind (índice = enum value)
    const TILE_PALETTE = [
      '#12161c',   // 0 Unknown  - fondo oscuro
      '#3a4a3a',   // 1 Open     - suelo
      '#1e2535',   // 2 Blocked  - muro
      '#1a3558',   // 3 Water    - agua
      '#4a3520',   // 4 Building - edificio
      '#786020',   // 5 Path     - camino
      '#3d4a2a',   // 6 Grass    - hierba
      '#2d4a1e',   // 7 Tree     - árbol
      '#a06030',   // 8 Warp     - portal
      '#3a2a3a',   // 9 Mine     - mina
      '#b09040',   // 10 NPC     - NPC
      '#4ac97e',   // 11 Player  - jugador
    ];
    const TILE_LABELS = [
      'Desconocido','Suelo','Bloqueado','Agua','Edificio',
      'Camino','Hierba','Árbol','Warp','Mina','NPC','Jugador'
    ];

    // Estado del visor
    const MAP = {
      data:       null,   // HostMapDto
      offscreen:  null,   // OffscreenCanvas (o canvas oculto)
      tileSize:   8,      // px por tile al zoom 1×
      zoom:       1.0,
      panX:       0,      // offset de pan en pixels del canvas
      panY:       0,
      dragging:   false,
      dragStartX: 0,
      dragStartY: 0,
      dragOriginX:0,
      dragOriginY:0,
      animFrame:  null,
      requestId:  0,
    };
    const MIN_ZOOM = 0.25, MAX_ZOOM = 16;

    function mapInit() {
      const vp  = document.getElementById('mapViewport');
      const cvs = document.getElementById('mapCanvas');
      mapResize();

      vp.addEventListener('wheel',     mapOnWheel,     { passive: false });
      vp.addEventListener('mousedown', mapOnMouseDown);
      vp.addEventListener('mousemove', mapOnMouseMove);
      vp.addEventListener('mouseup',   mapOnMouseUp);
      vp.addEventListener('mouseleave',mapOnMouseUp);

      // Touch
      vp.addEventListener('touchstart', mapOnTouchStart, { passive: false });
      vp.addEventListener('touchmove',  mapOnTouchMove,  { passive: false });
      vp.addEventListener('touchend',   mapOnTouchEnd);

      window.addEventListener('resize', mapResize);
    }

    function mapResize() {
      const vp = document.getElementById('mapViewport');
      const cvs = document.getElementById('mapCanvas');
      if (!vp.clientWidth || !vp.clientHeight) return;
      MAP.panX += (vp.clientWidth - cvs.width) / 2;
      MAP.panY += (vp.clientHeight - cvs.height) / 2;
      cvs.width = vp.clientWidth;
      cvs.height = vp.clientHeight;
      mapRender();
    }

    async function loadMap() {
      const sel = document.getElementById('mapLocationSelect');
      const loc = sel?.value || 'Town';
      const requestId = ++MAP.requestId;
      const overlay = document.getElementById('mapLoadOverlay');
      overlay.style.display = 'flex';
      overlay.innerHTML = '<span style=""font-size:2rem"">⏳</span><span style=""color:var(--text-muted);font-size:.9rem"">Cargando mapa…</span>';

      try {
        const res = await fetch(`/api/v1/host/map?location=${encodeURIComponent(loc)}`, { headers: getHeaders() });
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const dto = await readJsonResponse(res);
        if (requestId !== MAP.requestId) return;

        if (!dto.Width || !dto.Height) {
          overlay.innerHTML = '<span style=""font-size:2rem"">⚠️</span><span style=""color:var(--text-muted);font-size:.9rem"">Mapa no disponible (servidor no activo o sin mundo cargado)</span>';
          return;
        }

        dto.Tiles = typeof dto.Tiles === 'string'
          ? Uint8Array.from(atob(dto.Tiles), c => c.charCodeAt(0))
          : Uint8Array.from(dto.Tiles || []);
        if (dto.Tiles.length !== dto.Width * dto.Height)
          throw new Error('La respuesta del mapa está incompleta');
        const locationChanged = !MAP.data || MAP.data.LocationName !== dto.LocationName;
        MAP.data = dto;
        mapResize();

        // Actualizar selector de localizaciones
        if (dto.AvailableMaps?.length) {
          sel.replaceChildren(...dto.AvailableMaps.map(m => new Option(m, m, false, m === dto.LocationName)));
        }

        // Dibujar tiles en offscreen
        mapBuildOffscreen(dto);
        if (locationChanged) mapResetView();
        else mapRender();
        overlay.style.display = 'none';
        mapUpdatePlayerList(dto);
        setPing(true); setLastUpdate();
      } catch(e) {
        if (requestId !== MAP.requestId) return;
        overlay.innerHTML = `<span style=""font-size:2rem"">❌</span><span style=""color:var(--danger);font-size:.9rem"">Error: ${esc(e.message)}</span>`;
        setPing(false);
      }
    }

    function mapBuildOffscreen(dto) {
      const ts = MAP.tileSize;
      const w  = dto.Width * ts;
      const h  = dto.Height * ts;

      let cvs;
      if (typeof OffscreenCanvas !== 'undefined') {
        cvs = new OffscreenCanvas(w, h);
      } else {
        cvs = document.createElement('canvas');
        cvs.width = w; cvs.height = h;
      }

      const ctx = cvs.getContext('2d');
      const tiles = dto.Tiles;  // base64 decodificado al recibir la respuesta

      for (let y = 0; y < dto.Height; y++) {
        for (let x = 0; x < dto.Width; x++) {
          const kind = tiles[y * dto.Width + x] ?? 0;
          ctx.fillStyle = TILE_PALETTE[kind] ?? TILE_PALETTE[0];
          ctx.fillRect(x * ts, y * ts, ts, ts);
        }
      }

      // Grid fino (solo visible a partir de zoom 3×)
      // se dibuja en render para no ensuciar el offscreen

      // Warps: borde naranja
      if (dto.Warps) {
        ctx.strokeStyle = '#e07030';
        ctx.lineWidth = 1;
        for (const wp of dto.Warps) {
          ctx.strokeRect(wp.TileX * ts + .5, wp.TileY * ts + .5, ts - 1, ts - 1);
        }
      }

      MAP.offscreen = cvs;
    }

    function mapResetView() {
      if (!MAP.data) return;
      const vp  = document.getElementById('mapViewport');
      const cvs = document.getElementById('mapCanvas');
      const fw  = cvs.width  / (MAP.data.Width  * MAP.tileSize);
      const fh  = cvs.height / (MAP.data.Height * MAP.tileSize);
      MAP.zoom = Math.min(fw, fh, 2);
      // centra
      const scaledW = MAP.data.Width  * MAP.tileSize * MAP.zoom;
      const scaledH = MAP.data.Height * MAP.tileSize * MAP.zoom;
      MAP.panX = (cvs.width  - scaledW) / 2;
      MAP.panY = (cvs.height - scaledH) / 2;
      mapRender();
    }

    function mapRender() {
      if (MAP.animFrame) cancelAnimationFrame(MAP.animFrame);
      MAP.animFrame = requestAnimationFrame(_mapDraw);
    }

    function _mapDraw() {
      const cvs = document.getElementById('mapCanvas');
      if (!cvs) return;
      const ctx = cvs.getContext('2d');
      const dto = MAP.data;

      ctx.clearRect(0, 0, cvs.width, cvs.height);
      ctx.fillStyle = '#12161c';
      ctx.fillRect(0, 0, cvs.width, cvs.height);

      if (!MAP.offscreen || !dto) return;

      ctx.save();
      ctx.translate(MAP.panX, MAP.panY);
      ctx.scale(MAP.zoom, MAP.zoom);

      // Offscreen tile map
      ctx.imageSmoothingEnabled = false;
      ctx.drawImage(MAP.offscreen, 0, 0);

      const ts = MAP.tileSize;

      // Grid (visible a zoom ≥ 3)
      if (MAP.zoom >= 3) {
        ctx.strokeStyle = 'rgba(255,255,255,0.07)';
        ctx.lineWidth = 0.5 / MAP.zoom;
        for (let x = 0; x <= dto.Width; x++) {
          ctx.beginPath(); ctx.moveTo(x * ts, 0); ctx.lineTo(x * ts, dto.Height * ts); ctx.stroke();
        }
        for (let y = 0; y <= dto.Height; y++) {
          ctx.beginPath(); ctx.moveTo(0, y * ts); ctx.lineTo(dto.Width * ts, y * ts); ctx.stroke();
        }
      }

      // Marcadores de jugadores (círculo con borde, radio 3/4 del tile)
      if (dto.Players) {
        for (const p of dto.Players) {
          const cx = (p.TileX + 0.5) * ts;
          const cy = (p.TileY + 0.5) * ts;
          const r  = ts * 0.65;
          ctx.beginPath();
          ctx.arc(cx, cy, r, 0, Math.PI * 2);
          ctx.fillStyle = p.IsHost ? '#ffd700' : '#4ac97e';
          ctx.fill();
          ctx.strokeStyle = '#fff';
          ctx.lineWidth = 0.8 / MAP.zoom;
          ctx.stroke();
          // Nombre a zoom alto
          if (MAP.zoom >= 5) {
            ctx.fillStyle = '#fff';
            ctx.font = `${ts * 0.7}px sans-serif`;
            ctx.textAlign = 'center';
            ctx.fillText(p.Name, cx, (p.TileY - 0.1) * ts);
          }
        }
      }

      ctx.restore();

      // Zoom label
      document.getElementById('mapZoomLabel').textContent = Math.round(MAP.zoom * 100) + '%';
    }

    /* ── Wheel zoom ──────────────────────────────────────── */
    function mapOnWheel(e) {
      e.preventDefault();
      const vp  = document.getElementById('mapViewport');
      const rect = vp.getBoundingClientRect();
      const mouseX = e.clientX - rect.left;
      const mouseY = e.clientY - rect.top;

      const factor = e.deltaY < 0 ? 1.15 : (1 / 1.15);
      const newZoom = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, MAP.zoom * factor));

      // Zoom towards mouse cursor
      MAP.panX = mouseX - (mouseX - MAP.panX) * (newZoom / MAP.zoom);
      MAP.panY = mouseY - (mouseY - MAP.panY) * (newZoom / MAP.zoom);
      MAP.zoom = newZoom;
      mapRender();
    }

    /* ── Mouse drag ──────────────────────────────────────── */
    function mapOnMouseDown(e) {
      MAP.dragging   = true;
      MAP.dragStartX = e.clientX;
      MAP.dragStartY = e.clientY;
      MAP.dragOriginX = MAP.panX;
      MAP.dragOriginY = MAP.panY;
      document.getElementById('mapViewport').style.cursor = 'grabbing';
    }
    function mapOnMouseMove(e) {
      const vp   = document.getElementById('mapViewport');
      const rect = vp.getBoundingClientRect();
      const mx   = e.clientX - rect.left;
      const my   = e.clientY - rect.top;

      // Tooltip coords
      if (MAP.data) {
        const ts  = MAP.tileSize;
        const tx  = Math.floor((mx - MAP.panX) / (ts * MAP.zoom));
        const ty  = Math.floor((my - MAP.panY) / (ts * MAP.zoom));
        if (tx >= 0 && tx < MAP.data.Width && ty >= 0 && ty < MAP.data.Height) {
          const kind  = MAP.data.Tiles[ty * MAP.data.Width + tx] ?? 0;
          const label = TILE_LABELS[kind] ?? '?';
          // Warp tooltip
          const warp = MAP.data.Warps?.find(w => w.TileX === tx && w.TileY === ty);
          const warpStr = warp ? ` → ${warp.TargetMap} (${warp.TargetX},${warp.TargetY})` : '';
          // Player tooltip
          const player = MAP.data.Players?.find(p => p.TileX === tx && p.TileY === ty);
          const playerStr = player ? ` [${player.Name}${player.IsHost?' ★':''}]` : '';
          const tt = document.getElementById('mapTooltip');
          tt.style.display = 'block';
          tt.style.left = (mx + 14) + 'px';
          tt.style.top  = (my +  8) + 'px';
          tt.textContent = `(${tx}, ${ty}) ${label}${warpStr}${playerStr}`;
          document.getElementById('mapCoords').textContent = `(${tx}, ${ty})`;
        }
      }

      if (!MAP.dragging) return;
      MAP.panX = MAP.dragOriginX + (e.clientX - MAP.dragStartX);
      MAP.panY = MAP.dragOriginY + (e.clientY - MAP.dragStartY);
      mapRender();
    }
    function mapOnMouseUp() {
      MAP.dragging = false;
      document.getElementById('mapViewport').style.cursor = 'grab';
      document.getElementById('mapTooltip').style.display = 'none';
    }

    /* ── Touch pinch-to-zoom ─────────────────────────────── */
    let _lastTouchDist = null;
    let _lastTouchMidX = 0, _lastTouchMidY = 0;
    function mapOnTouchStart(e) {
      if (e.touches.length === 2) {
        const [a, b] = e.touches;
        _lastTouchDist = Math.hypot(b.clientX-a.clientX, b.clientY-a.clientY);
        _lastTouchMidX = (a.clientX + b.clientX) / 2;
        _lastTouchMidY = (a.clientY + b.clientY) / 2;
      } else if (e.touches.length === 1) {
        MAP.dragging = true;
        MAP.dragStartX = e.touches[0].clientX;
        MAP.dragStartY = e.touches[0].clientY;
        MAP.dragOriginX = MAP.panX;
        MAP.dragOriginY = MAP.panY;
      }
    }
    function mapOnTouchMove(e) {
      e.preventDefault();
      if (e.touches.length === 2) {
        const [a, b] = e.touches;
        const dist = Math.hypot(b.clientX-a.clientX, b.clientY-a.clientY);
        const midX = (a.clientX + b.clientX) / 2;
        const midY = (a.clientY + b.clientY) / 2;
        const vp   = document.getElementById('mapViewport');
        const rect = vp.getBoundingClientRect();
        const mx   = midX - rect.left;
        const my   = midY - rect.top;
        if (_lastTouchDist) {
          const factor   = dist / _lastTouchDist;
          const newZoom  = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, MAP.zoom * factor));
          MAP.panX = mx - (mx - MAP.panX) * (newZoom / MAP.zoom);
          MAP.panY = my - (my - MAP.panY) * (newZoom / MAP.zoom);
          MAP.zoom = newZoom;
        }
        _lastTouchDist = dist;
        mapRender();
      } else if (e.touches.length === 1 && MAP.dragging) {
        MAP.panX = MAP.dragOriginX + (e.touches[0].clientX - MAP.dragStartX);
        MAP.panY = MAP.dragOriginY + (e.touches[0].clientY - MAP.dragStartY);
        mapRender();
      }
    }
    function mapOnTouchEnd() {
      MAP.dragging = false;
      _lastTouchDist = null;
    }

    function mapUpdatePlayerList(dto) {
      const container = document.getElementById('map-player-positions');
      const online = (dto.Players || []);
      container.innerHTML = online.length
        ? online.map(p => `<div class=""info-pair"">
            <span class=""lbl"">${badge(p.IsHost?'HOST':'ONLINE', p.IsHost?'warning':'online')} ${esc(p.Name)}</span>
            <span class=""val"">${esc(dto.LocationName)} (${p.TileX}, ${p.TileY})</span>
          </div>`).join('')
        : '<span style=""color:var(--text-muted)"">Ningún jugador en esta localización</span>';
    }

    /* ────────────────────────────────────────────────────── */
    /*  API CALLS                                             */
    /* ────────────────────────────────────────────────────── */

    /* ── /api/status (legacy) ────────────────────────────── */
    async function fetchStatus() {
      try {
        const res = await fetch('/api/status', { headers: getHeaders() });
        if (!res.ok) throw new Error(res.status);
        const d = await readJsonResponse(res);
        setPing(true); setLastUpdate();

        document.getElementById('kpi-online').textContent   = d.OnlinePlayersCount ?? '—';
        document.getElementById('kpi-registered').textContent = `de ${d.RegisteredPlayersCount ?? '—'} registrados`;
        document.getElementById('kpi-doors').textContent    = `${d.OccupiedDoorsCount ?? '—'} / ${d.TotalDoorsCount ?? '—'}`;
        document.getElementById('kpi-doors-free').textContent = `${(d.TotalDoorsCount||0)-(d.OccupiedDoorsCount||0)} libres`;
        document.getElementById('kpi-market').textContent   = d.ActiveMarketListingsCount ?? '—';

        const dTbody = document.getElementById('doorsTable');
        if (d.Doors && d.Doors.length) {
          dTbody.innerHTML = d.Doors.map(dr => `<tr>
            <td><strong>${esc(dr.DoorId)}</strong></td>
            <td>${esc(dr.Location)}</td>
            <td>(${dr.TileX}, ${dr.TileY})</td>
            <td>${esc(dr.SlotId)}</td>
            <td>${dr.OwnerName ? esc(dr.OwnerName) : '<em style=""color:var(--text-muted)"">Libre</em>'}</td>
            <td>${esc(dr.FarmType||'Standard')}</td>
            <td>${badge(esc(dr.Policy||'—'),'accent')}</td>
          </tr>`).join('');
        }
      } catch(e) { setPing(false); }
    }

    /* ── /api/v1/host/clock ──────────────────────────────── */
    async function fetchClock() {
      try {
        const res = await fetch('/api/v1/host/clock', { headers: getHeaders() });
        if (!res.ok) return;
        const c = await readJsonResponse(res);
        setPing(true); setLastUpdate();

        const seasonLabel = {spring:'Primavera',summer:'Verano',fall:'Otoño',winter:'Invierno'};
        const season = seasonLabel[c.Season?.toLowerCase()] ?? c.Season ?? '—';
        document.getElementById('kpi-date').textContent     = `Año ${c.Year}, Día ${c.DayOfMonth}`;
        document.getElementById('kpi-season').textContent   = season;
        document.getElementById('kpi-gametime').textContent = fmtTime(c.GameTimeOfDay);
        document.getElementById('kpi-nighthold').textContent = c.IsNightHold ? '🌙 Madrugada' : '☀️ Activo';
        document.getElementById('kpi-rollover').textContent = c.SecondsUntilNextRollover > 0
          ? Math.floor(c.SecondsUntilNextRollover/60) + ' min'
          : '—';
        document.getElementById('kpi-rollover-time').textContent = c.NextRolloverLocalTime ?? '—';

        document.getElementById('ov-realtime').textContent  = c.RealTimeUtc ?? '—';
        document.getElementById('ov-localtime').textContent = c.LocalTime ?? '—';
        document.getElementById('ov-timezone').textContent  = c.TimeZoneId ?? '—';
        document.getElementById('ov-totalday').textContent  = c.TotalGameDays ?? '—';
        document.getElementById('ov-mp').textContent        = c.IsMultiplayerServer ? 'Sí' : 'No';
        document.getElementById('ov-save').textContent      = c.SaveFolderName ?? '—';

        document.getElementById('roll-day').textContent     = c.LastRolloverGameDay >= 0 ? c.LastRolloverGameDay : '—';
        document.getElementById('roll-outcome').textContent = c.LastRolloverOutcomeSummary || '—';
        document.getElementById('roll-backup').textContent  = c.LastBackupFileName || '—';
        document.getElementById('roll-backup-date').textContent = c.LastBackupCreatedUtc || '—';
      } catch(e) {}
    }

    /* ── /api/v1/host/players ────────────────────────────── */
    async function fetchPlayers() {
      try {
        const res = await fetch('/api/v1/host/players', { headers: getHeaders() });
        if (!res.ok) throw new Error(res.status);
        const d = await readJsonResponse(res);
        setPing(true);

        const pTbody = document.getElementById('playersTable');
        if (!d.Players || !d.Players.length) {
          pTbody.innerHTML = '<tr><td colspan=""10"" style=""text-align:center;color:var(--text-muted);padding:28px"">Sin jugadores registrados</td></tr>';
          return;
        }
        pTbody.innerHTML = d.Players.map(p => `<tr>
          <td><strong>${esc(p.Name||'—')}</strong><br><small style=""color:var(--text-muted)""><code>${p.PlayerId}</code></small></td>
          <td>${badge(p.IsOnline?'ONLINE':'OFFLINE', p.IsOnline?'online':'offline')}</td>
          <td>${esc(p.LocationName||'—')}<br><small style=""color:var(--text-muted)"">(${p.TileX}, ${p.TileY})</small></td>
          <td>${esc(p.ResidenceSlotId||'—')}<br><small style=""color:var(--text-muted)"">${esc(p.ResidenceFarmType||'')}</small></td>
          <td>${esc(p.SleepState||'—')}</td>
          <td>${p.SatietyUsed ?? '—'}</td>
          <td>${p.SpaBuffsUsed ?? '—'}</td>
          <td>${p.PendingMarketGold ?? 0}g</td>
          <td>${p.InFestival ? badge('Sí','festival') : '—'}</td>
          <td>${p.IsClaimed ? badge('✔','online') : badge('No','offline')}</td>
        </tr>`).join('');
      } catch(e) { setPing(false); }
    }

    /* ── /api/v1/host/festival ───────────────────────────── */
    async function fetchFestival() {
      try {
        const res = await fetch('/api/v1/host/festival', { headers: getHeaders() });
        if (!res.ok) return;
        const f = await readJsonResponse(res);

        const banner = document.getElementById('festival-banner');
        const noFest = document.getElementById('fest-no-festival');
        const panel  = document.getElementById('fest-detail-panel');

        if (!f.IsFestivalToday) {
          banner.style.display = 'none';
          noFest.style.display = '';
          panel.style.display  = 'none';
          return;
        }

        banner.style.display = '';
        document.getElementById('fest-name').textContent   = f.FestivalName || f.FestivalId;
        document.getElementById('fest-detail').textContent =
          `${f.Season} ${f.DayOfMonth} · ${f.LocationName} · Ventana ${fmtTime(f.WindowStart)}–${fmtTime(f.WindowEnd)}`;

        noFest.style.display  = 'none';
        panel.style.display   = '';
        document.getElementById('fst-name').textContent    = f.FestivalName || f.FestivalId;
        document.getElementById('fst-date').textContent    = `${f.Season} ${f.DayOfMonth}`;
        document.getElementById('fst-loc').textContent     = f.LocationName;
        document.getElementById('fst-window').textContent  = `${fmtTime(f.WindowStart)} – ${fmtTime(f.WindowEnd)}`;
        document.getElementById('fst-window-open').innerHTML = f.IsWindowOpen
          ? badge('Abierta','online') : badge('Cerrada','offline');
        document.getElementById('fst-main').innerHTML      = f.HasMainEvent ? badge('Sí','accent') : badge('No','offline');
        document.getElementById('fst-ceremony').innerHTML  = f.IsCeremonyFestival ? badge('Sí','warning') : badge('No','offline');
        document.getElementById('fst-runs').textContent    = f.RunsCount ?? 0;
        document.getElementById('fst-present-count').textContent = (f.PresentPlayerIds||[]).length;
        document.getElementById('fst-signed-count').textContent  = (f.SignedUpPlayerIds||[]).length;
        document.getElementById('fst-played-count').textContent  = (f.PlayedPlayerIds||[]).length;
      } catch(e) {}
    }

    /* ── /api/v1/host/events ─────────────────────────────── */
    let eventsLoading = false;
    async function fetchEvents() {
      if (eventsLoading) return;
      eventsLoading = true;
      try {
        const res = await fetch(`/api/v1/host/events?since=${lastEventCursor}`, { headers: getHeaders() });
        if (!res.ok) return;
        const page = await readJsonResponse(res);
        const evts = page.Events || [];
        if (page.LastEventId < lastEventCursor) {
          lastEventCursor = 0;
          document.getElementById('eventLog').replaceChildren();
          return;
        }
        lastEventCursor = Math.max(lastEventCursor, page.LastEventId || 0);
        if (!evts.length) return;

        const log  = document.getElementById('eventLog');
        document.getElementById('eventLogEmpty')?.remove();
        const frag = document.createDocumentFragment();
        evts.slice().reverse().forEach(ev => {
          lastEventCursor = Math.max(lastEventCursor, ev.Id ?? 0);
          const item = document.createElement('div');
          item.className = 'event-item';
          item.innerHTML = `<span class=""ev-time"">${esc(ev.TimestampUtc||'—')}</span><span class=""ev-msg"">${esc(ev.Message||ev.Summary||JSON.stringify(ev))}</span>`;
          frag.appendChild(item);
        });
        log.prepend(frag);
        while (log.children.length > 80) log.removeChild(log.lastChild);
      } catch(e) { setPing(false); }
      finally { eventsLoading = false; }
    }

    /* ── Announce ────────────────────────────────────────── */
    async function sendAnnounce() {
      const msg = document.getElementById('announceMsg').value.trim();
      if (!msg) { alert('Escribe un mensaje primero.'); return; }
      const channel = document.getElementById('announceChannel').value;
      try {
        const res = await fetch('/api/v1/host/announce', {
          method: 'POST',
          headers: getHeaders({'Content-Type':'application/json'}),
          body: JSON.stringify({ Message: msg, Channel: channel })
        });
        const json = await readJsonResponse(res);
        alert(json.Message || 'Anuncio enviado');
        document.getElementById('announceMsg').value = '';
      } catch(e) { alert('Error: ' + e); }
    }

    /* ── Schedule restart ────────────────────────────────── */
    async function scheduleRestart() {
      const reason = document.getElementById('restartReason').value.trim() || 'Reinicio programado';
      const warnStr = document.getElementById('restartWarnMins').value.trim();
      const warnMins = warnStr ? Number(warnStr) : null;
      if (warnMins !== null && (!Number.isInteger(warnMins) || warnMins < 0 || warnMins > 2147483647)) {
        alert('El aviso debe ser un número entero válido de minutos (0 o más).');
        return;
      }
      if (!confirm(`¿Programar reinicio? Motivo: ""${reason}""${warnMins ? ` con aviso de ${warnMins} min` : ''}`)) return;
      try {
        const res = await fetch('/api/v1/host/restart', {
          method: 'POST',
          headers: getHeaders({'Content-Type':'application/json'}),
          body: JSON.stringify({ Reason: reason, WarningMinutes: warnMins, Immediate: false })
        });
        const json = await readJsonResponse(res);
        alert(json.Message || 'Reinicio programado');
      } catch(e) { alert('Error: ' + e); }
    }

    /* ── Generic POST ────────────────────────────────────── */
    async function callAction(url, method, body) {
      const labels = {
        '/api/rollover': 'Forzar el cierre del día y desconectar a los jugadores',
        '/api/mines/reset': 'Regenerar las minas desocupadas',
        '/api/v1/host/rest': 'Registrar el descanso del anfitrión',
        '/api/v1/host/restart': 'Cancelar el reinicio programado',
        '/api/v1/host/festival/start': 'Iniciar la siguiente tanda del festival',
        '/api/v1/host/festival/ceremony': 'Iniciar la ceremonia del festival'
      };
      if (!confirm((labels[url] || 'Ejecutar la acción') + '?')) return;
      try {
        const opts = {
          method: method || 'POST',
          headers: getHeaders(body ? {'Content-Type':'application/json'} : {})
        };
        if (body) opts.body = JSON.stringify(body);
        const res  = await fetch(url, opts);
        const json = await readJsonResponse(res);
        alert(json.Message || json.message || 'Ejecutado');
        refreshAll();
      } catch(e) { alert('Error: ' + e); }
    }

    /* ── Refresh all ─────────────────────────────────────── */
    function refreshAll() {
      fetchStatus();
      fetchClock();
      fetchPlayers();
      fetchFestival();
      fetchEvents();
      // Si el tab de mapa está activo, recargar también
      if (document.getElementById('tab-map')?.classList.contains('active')) {
        loadMap();
      }
    }

    /* ── Init ────────────────────────────────────────────── */
    window.onload = () => {
      const url = new URL(window.location.href);
      const token = url.searchParams.get('token');
      if (token) {
        document.getElementById('tokenInput').value = token;
        url.searchParams.delete('token');
        history.replaceState(null, '', url.pathname + url.search + url.hash);
      }
      mapInit();
      refreshAll();
    };
    setInterval(refreshAll, 10000);
  </script>
</body>
</html>";
        }
    }
}
