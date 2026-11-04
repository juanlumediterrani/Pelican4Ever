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
      <input type=""password"" id=""tokenInput"" placeholder=""Bearer Token"" value=""" + defaultToken + @""" autocomplete=""off"">
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
          <div class=""event-item""><span class=""ev-time"">—</span><span class=""ev-msg"" style=""color:var(--text-muted)"">Sin eventos cargados</span></div>
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
              <th>Energía Spa</th>
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
      <div class=""section-header"">
        <h2>Mapa del Mundo</h2>
      </div>

      <!-- Player positions quick list (available today) -->
      <div class=""info-row"" style=""margin-bottom:18px"">
        <h3>📍 Posiciones de Jugadores Online</h3>
        <div id=""map-player-positions"" class=""info-body"" style=""gap:10px 28px"">
          <span style=""color:var(--text-muted)"">Cargando…</span>
        </div>
      </div>

      <!-- Map viewer placeholder -->
      <div class=""map-placeholder"">
        <span class=""map-icon"">🗺️</span>
        <h2>Visor de Mapa Tile-by-Tile</h2>
        <p>Aquí se integrará el visor de mapa interactivo al estilo <em>Overviewer / DynMap</em>, renderizando las capas de tiles de Stardew Valley en tiempo real desde los datos del servidor.</p>
        <div class=""map-meta"">
          <span>Formato previsto: <strong>Tiles PNG (SMAPI xTile)</strong></span>
          <span>Capas: <strong>Town, Farm, Mine…</strong></span>
          <span>Jugadores: <strong>marcadores en vivo</strong></span>
          <span>Estado: <strong>Planificado</strong></span>
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
    /* ── Estado ──────────────────────────────────────────── */
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
    }

    /* ── Helpers ─────────────────────────────────────────── */
    function fmtTime(t) {
      if (!t) return '—';
      const h = Math.floor(t / 100), m = t % 100;
      return `${String(h).padStart(2,'0')}:${String(m).padStart(2,'0')}`;
    }
    function badge(text, cls) { return `<span class=""badge badge-${cls}"">${text}</span>`; }
    function esc(s) {
      return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
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

    /* ── /api/status (legacy endpoint) ──────────────────── */
    async function fetchStatus() {
      try {
        const res = await fetch('/api/status', { headers: getHeaders() });
        if (!res.ok) throw new Error(res.status);
        const d = await res.json();
        setPing(true); setLastUpdate();

        /* KPIs */
        document.getElementById('kpi-online').textContent   = d.OnlinePlayersCount ?? '—';
        document.getElementById('kpi-registered').textContent = `de ${d.RegisteredPlayersCount ?? '—'} registrados`;
        document.getElementById('kpi-doors').textContent    = `${d.OccupiedDoorsCount ?? '—'} / ${d.TotalDoorsCount ?? '—'}`;
        document.getElementById('kpi-doors-free').textContent = `${(d.TotalDoorsCount||0)-(d.OccupiedDoorsCount||0)} libres`;
        document.getElementById('kpi-market').textContent   = d.ActiveMarketListingsCount ?? '—';

        /* Residences table */
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
        const c = await res.json();
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

        /* Overview detail */
        document.getElementById('ov-realtime').textContent  = c.RealTimeUtc ?? '—';
        document.getElementById('ov-localtime').textContent = c.LocalTime ?? '—';
        document.getElementById('ov-timezone').textContent  = c.TimeZoneId ?? '—';
        document.getElementById('ov-totalday').textContent  = c.TotalGameDays ?? '—';
        document.getElementById('ov-mp').textContent        = c.IsMultiplayerServer ? 'Sí' : 'No';
        document.getElementById('ov-save').textContent      = c.SaveFolderName ?? '—';

        /* Rollover summary */
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
        const d = await res.json();
        setPing(true);

        /* Map positions in map tab */
        const mapPos = document.getElementById('map-player-positions');
        const online = (d.Players || []).filter(p => p.IsOnline);
        mapPos.innerHTML = online.length
          ? online.map(p => `<div class=""info-pair"">
              <span class=""lbl"">${badge('ONLINE','online')} ${esc(p.Name)}</span>
              <span class=""val"">${esc(p.LocationName)} (${p.TileX}, ${p.TileY})</span>
            </div>`).join('')
          : '<span style=""color:var(--text-muted)"">Ningún jugador online</span>';

        /* Players table */
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
          <td>${p.SpaEnergyUsed ?? '—'}</td>
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
        const f = await res.json();

        const banner = document.getElementById('festival-banner');
        const noFest = document.getElementById('fest-no-festival');
        const panel  = document.getElementById('fest-detail-panel');

        if (!f.IsFestivalToday) {
          banner.style.display = 'none';
          noFest.style.display = '';
          panel.style.display  = 'none';
          return;
        }

        /* Overview banner */
        banner.style.display = '';
        document.getElementById('fest-name').textContent   = f.FestivalName || f.FestivalId;
        document.getElementById('fest-detail').textContent =
          `${f.Season} ${f.DayOfMonth} · ${f.LocationName} · Ventana ${fmtTime(f.WindowStart)}–${fmtTime(f.WindowEnd)}`;

        /* Festival tab */
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
    async function fetchEvents() {
      try {
        const res = await fetch(`/api/v1/host/events?since=${lastEventCursor}`, { headers: getHeaders() });
        if (!res.ok) return;
        const evts = await res.json();
        if (!evts || !evts.length) return;

        const log  = document.getElementById('eventLog');
        const frag = document.createDocumentFragment();
        evts.forEach(ev => {
          lastEventCursor = Math.max(lastEventCursor, ev.Id ?? 0);
          const item = document.createElement('div');
          item.className = 'event-item';
          item.innerHTML = `<span class=""ev-time"">${esc(ev.Time||ev.Timestamp||'—')}</span><span class=""ev-msg"">${esc(ev.Message||ev.Summary||JSON.stringify(ev))}</span>`;
          frag.appendChild(item);
        });

        /* Keep only last 80 entries */
        log.prepend(frag);
        while (log.children.length > 80) log.removeChild(log.lastChild);
      } catch(e) {}
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
        const json = await res.json();
        alert(json.Message || 'Anuncio enviado');
        document.getElementById('announceMsg').value = '';
      } catch(e) { alert('Error: ' + e); }
    }

    /* ── Schedule restart ────────────────────────────────── */
    async function scheduleRestart() {
      const reason = document.getElementById('restartReason').value.trim() || 'Reinicio programado';
      const warnStr = document.getElementById('restartWarnMins').value.trim();
      const warnMins = warnStr ? parseInt(warnStr, 10) : null;
      if (!confirm(`¿Programar reinicio? Motivo: ""${reason}""${warnMins ? ` con aviso de ${warnMins} min` : ''}`)) return;
      try {
        const res = await fetch('/api/v1/host/restart', {
          method: 'POST',
          headers: getHeaders({'Content-Type':'application/json'}),
          body: JSON.stringify({ Reason: reason, WarningMinutes: warnMins, Immediate: !warnMins })
        });
        const json = await res.json();
        alert(json.Message || 'Reinicio programado');
      } catch(e) { alert('Error: ' + e); }
    }

    /* ── Generic POST action ─────────────────────────────── */
    async function callAction(url, method, body) {
      if (!confirm(`¿Confirmar: ${url}?`)) return;
      try {
        const opts = {
          method: method || 'POST',
          headers: getHeaders(body ? {'Content-Type':'application/json'} : {})
        };
        if (body) opts.body = JSON.stringify(body);
        const res  = await fetch(url, opts);
        const json = await res.json();
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
    }

    /* ── Init ────────────────────────────────────────────── */
    window.onload = refreshAll;
    setInterval(refreshAll, 10000);
  </script>
</body>
</html>";
        }
    }
}
