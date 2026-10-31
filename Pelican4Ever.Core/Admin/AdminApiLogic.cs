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
            return !string.Equals(trimmed, ServerConfig.DefaultInsecureAdminToken, StringComparison.Ordinal);
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
        /// </summary>
        public static string GetDashboardHtml(string defaultToken = "")
        {
            return @"<!DOCTYPE html>
<html lang=""es"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>Pelican 4Ever - Panel de Control</title>
  <style>
    :root {
      --bg: #1a1e24;
      --card-bg: #242a33;
      --accent: #58a6ff;
      --success: #3fb950;
      --warning: #d29922;
      --danger: #f85149;
      --text: #e6edf3;
      --text-muted: #8b949e;
      --border: #30363d;
    }
    body {
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
      background: var(--bg);
      color: var(--text);
      margin: 0;
      padding: 20px;
    }
    .container { max-width: 1100px; margin: 0 auto; }
    header { display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid var(--border); padding-bottom: 15px; margin-bottom: 25px; }
    h1 { margin: 0; font-size: 1.6rem; color: var(--accent); }
    .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 15px; margin-bottom: 25px; }
    .card { background: var(--card-bg); border: 1px solid var(--border); border-radius: 8px; padding: 18px; }
    .card h3 { margin: 0 0 10px 0; font-size: 0.9rem; color: var(--text-muted); text-transform: uppercase; }
    .card .value { font-size: 1.8rem; font-weight: bold; }
    .btn { background: var(--accent); color: #fff; border: none; padding: 10px 16px; border-radius: 6px; cursor: pointer; font-weight: 600; margin-right: 10px; margin-bottom: 10px; }
    .btn:hover { opacity: 0.9; }
    .btn-danger { background: var(--danger); }
    .btn-warning { background: var(--warning); }
    .section-title { font-size: 1.2rem; margin: 25px 0 15px 0; border-bottom: 1px solid var(--border); padding-bottom: 8px; }
    table { width: 100%; border-collapse: collapse; background: var(--card-bg); border-radius: 8px; overflow: hidden; }
    th, td { padding: 12px; text-align: left; border-bottom: 1px solid var(--border); font-size: 0.95rem; }
    th { background: #1f242c; color: var(--text-muted); }
    .badge { display: inline-block; padding: 3px 8px; border-radius: 12px; font-size: 0.75rem; font-weight: bold; }
    .badge-online { background: rgba(63, 185, 80, 0.2); color: var(--success); }
    .badge-offline { background: rgba(139, 148, 158, 0.2); color: var(--text-muted); }
    .actions-bar { margin: 20px 0; display: flex; flex-wrap: wrap; gap: 10px; }
    #tokenInput { background: #161b22; border: 1px solid var(--border); color: var(--text); padding: 8px 12px; border-radius: 6px; width: 260px; }
  </style>
</head>
<body>
  <div class=""container"">
    <header>
      <div>
        <h1>Pelican 4Ever Dashboard</h1>
        <small style=""color: var(--text-muted)"">Servidor Persistente 24/7 Stardew Valley 1.6</small>
      </div>
      <div>
        <input type=""password"" id=""tokenInput"" placeholder=""Bearer Token"" value=""" + defaultToken + @""">
        <button class=""btn"" onclick=""fetchStatus()"">Actualizar</button>
      </div>
    </header>

    <div class=""grid"">
      <div class=""card"">
        <h3>Fecha de Juego</h3>
        <div class=""value"" id=""gameDate"">—</div>
        <small id=""gameClock"" style=""color: var(--text-muted)"">—</small>
      </div>
      <div class=""card"">
        <h3>Jugadores Conectados</h3>
        <div class=""value"" id=""onlineCount"">—</div>
        <small id=""registeredCount"" style=""color: var(--text-muted)"">—</small>
      </div>
      <div class=""card"">
        <h3>Residencias Asignadas</h3>
        <div class=""value"" id=""doorsCount"">—</div>
        <small id=""doorsAvailable"" style=""color: var(--text-muted)"">—</small>
      </div>
      <div class=""card"">
        <h3>Mercado Vecinal</h3>
        <div class=""value"" id=""marketListings"">—</div>
        <small style=""color: var(--text-muted)"">Ofertas activas</small>
      </div>
    </div>

    <div class=""actions-bar"">
      <button class=""btn btn-warning"" onclick=""callAction('/api/rollover')"">Forzar Rollover Diario</button>
      <button class=""btn"" onclick=""callAction('/api/mines/reset')"">Reiniciar Minas (Shift)</button>
    </div>

    <h2 class=""section-title"">Jugadores en el Servidor</h2>
    <table>
      <thead>
        <tr>
          <th>Jugador</th>
          <th>ID</th>
          <th>Estado</th>
          <th>Descanso</th>
          <th>Granja</th>
          <th>Saciedad Usada</th>
          <th>Oro Pendiente</th>
        </tr>
      </thead>
      <tbody id=""playersTable"">
        <tr><td colspan=""7"" style=""text-align: center; color: var(--text-muted)"">Cargando datos...</td></tr>
      </tbody>
    </table>

    <h2 class=""section-title"">Catálogo de Residencias (16 Slots)</h2>
    <table>
      <thead>
        <tr>
          <th>ID Puerta</th>
          <th>Ubicación</th>
          <th>Coordenadas</th>
          <th>Hueco Asignado</th>
          <th>Propietario</th>
          <th>Tipo Granja</th>
          <th>Política</th>
        </tr>
      </thead>
      <tbody id=""doorsTable"">
        <tr><td colspan=""7"" style=""text-align: center; color: var(--text-muted)"">Cargando residencias...</td></tr>
      </tbody>
    </table>
  </div>

  <script>
    function getHeaders() {
      const token = document.getElementById('tokenInput').value.trim();
      return token ? { 'Authorization': 'Bearer ' + token } : {};
    }

    async function fetchStatus() {
      try {
        const res = await fetch('/api/status', { headers: getHeaders() });
        if (!res.ok) throw new Error('Error HTTP: ' + res.status);
        const data = await res.json();
        
        document.getElementById('gameDate').textContent = `Año ${data.Year}, ${data.Season} ${data.DayOfMonth}`;
        document.getElementById('gameClock').textContent = `Hora: ${data.GameTimeOfDay} ${data.IsNightHold ? '(Madrugada)' : ''}`;
        document.getElementById('onlineCount').textContent = `${data.OnlinePlayersCount} Online`;
        document.getElementById('registeredCount').textContent = `Total registrados: ${data.RegisteredPlayersCount}`;
        document.getElementById('doorsCount').textContent = `${data.OccupiedDoorsCount} / ${data.TotalDoorsCount}`;
        document.getElementById('doorsAvailable').textContent = `Libres: ${data.TotalDoorsCount - data.OccupiedDoorsCount}`;
        document.getElementById('marketListings').textContent = `${data.ActiveMarketListingsCount}`;

        const pTbody = document.getElementById('playersTable');
        pTbody.innerHTML = '';
        data.Players.forEach(p => {
          const tr = document.createElement('tr');
          tr.innerHTML = `
            <td><strong>${p.Name || '—'}</strong></td>
            <td><code>${p.Id}</code></td>
            <td><span class=""badge ${p.IsOnline ? 'badge-online' : 'badge-offline'}"">${p.IsOnline ? 'ONLINE' : 'OFFLINE'}</span></td>
            <td>${p.SleepState}</td>
            <td>${p.FarmSlotId || '—'}</td>
            <td>${p.SatietyUsed}</td>
            <td>${p.PendingMarketGold}g</td>
          `;
          pTbody.appendChild(tr);
        });

        const dTbody = document.getElementById('doorsTable');
        dTbody.innerHTML = '';
        data.Doors.forEach(d => {
          const tr = document.createElement('tr');
          tr.innerHTML = `
            <td><strong>${d.DoorId}</strong></td>
            <td>${d.Location}</td>
            <td>(${d.TileX}, ${d.TileY})</td>
            <td>${d.SlotId}</td>
            <td>${d.OwnerName || '<em style=""color:var(--text-muted)"">Libre</em>'}</td>
            <td>${d.FarmType || 'Standard'}</td>
            <td><span class=""badge badge-offline"">${d.Policy || '—'}</span></td>
          `;
          dTbody.appendChild(tr);
        });
      } catch (err) {
        console.error(err);
      }
    }

    async function callAction(url) {
      if (!confirm('¿Confirmar ejecución de ' + url + '?')) return;
      try {
        const res = await fetch(url, { method: 'POST', headers: getHeaders() });
        const json = await res.json();
        alert(json.message || 'Comando ejecutado');
        fetchStatus();
      } catch (err) {
        alert('Error ejecutando comando: ' + err);
      }
    }

    window.onload = fetchStatus;
    setInterval(fetchStatus, 5000);
  </script>
</body>
</html>";
        }
    }
}
