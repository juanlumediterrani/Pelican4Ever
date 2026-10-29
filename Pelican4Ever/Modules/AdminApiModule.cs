using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Servidor HTTP para API REST de administración y Dashboard web DevOps (TASK-801 y TASK-802).
    /// Ejecuta solo en el Host y proporciona telemetría y control sin comandos de consola.
    /// </summary>
    public sealed class AdminApiModule : IModule
    {
        private static ModContext? context;
        private HttpListener? listener;
        private Thread? listenerThread;
        private volatile bool isRunning;

        public string Name => "AdminApi";

        public ModuleSide Side => ModuleSide.Host;

        public void Register(ModContext ctx)
        {
            context = ctx;

            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            StartServer();
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            StopServer();
        }

        private void StartServer()
        {
            if (context == null || isRunning) return;

            int port = context.Config.AdminApiPort;
            if (port <= 0) return;

            try
            {
                listener = new HttpListener();
                // Prefijos con comodines y fallback local
                try
                {
                    listener.Prefixes.Add($"http://*:{port}/");
                }
                catch
                {
                    listener.Prefixes.Add($"http://localhost:{port}/");
                    listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                }

                listener.Start();
                isRunning = true;

                listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "Pelican4Ever-AdminApi"
                };
                listenerThread.Start();

                context.Monitor.Log($"[AdminApiModule] Servidor API y Dashboard activo en http://localhost:{port}/", LogLevel.Info);
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[AdminApiModule] No se pudo iniciar el listener HTTP en puerto {port}: {ex.Message}. Intentando localhost...", LogLevel.Warn);
                try
                {
                    listener = new HttpListener();
                    listener.Prefixes.Add($"http://localhost:{port}/");
                    listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                    listener.Start();
                    isRunning = true;
                    listenerThread = new Thread(ListenLoop) { IsBackground = true, Name = "Pelican4Ever-AdminApi" };
                    listenerThread.Start();
                    context.Monitor.Log($"[AdminApiModule] Servidor API y Dashboard activo en http://localhost:{port}/", LogLevel.Info);
                }
                catch (Exception fallbackEx)
                {
                    context.Monitor.Log($"[AdminApiModule] Fallo definitivo al iniciar API REST: {fallbackEx.Message}", LogLevel.Error);
                }
            }
        }

        private void StopServer()
        {
            isRunning = false;
            try
            {
                listener?.Stop();
                listener?.Close();
            }
            catch { }
        }

        private void ListenLoop()
        {
            while (isRunning && listener != null && listener.IsListening)
            {
                try
                {
                    HttpListenerContext httpCtx = listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => ProcessRequest(httpCtx));
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (isRunning)
                    {
                        context?.Monitor.Log($"[AdminApiModule] Error en ListenLoop: {ex}", LogLevel.Warn);
                    }
                }
            }
        }

        private void ProcessRequest(HttpListenerContext httpCtx)
        {
            HttpListenerRequest req = httpCtx.Request;
            HttpListenerResponse res = httpCtx.Response;

            // Cabeceras CORS
            res.AddHeader("Access-Control-Allow-Origin", "*");
            res.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            res.AddHeader("Access-Control-Allow-Headers", "Authorization, Content-Type");

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = (int)HttpStatusCode.OK;
                res.Close();
                return;
            }

            string path = req.Url?.AbsolutePath ?? "/";

            try
            {
                if (req.HttpMethod == "GET" && (path == "/" || path == "/dashboard"))
                {
                    ServeDashboard(res);
                }
                else if (req.HttpMethod == "GET" && path == "/api/status")
                {
                    ServeStatus(req, res);
                }
                else if (req.HttpMethod == "POST" && path == "/api/rollover")
                {
                    HandleRollover(req, res);
                }
                else if (req.HttpMethod == "POST" && path == "/api/mines/reset")
                {
                    HandleMinesReset(req, res);
                }
                else if (req.HttpMethod == "POST" && path == "/api/festival/reschedule")
                {
                    HandleFestivalReschedule(req, res);
                }
                else
                {
                    res.StatusCode = (int)HttpStatusCode.NotFound;
                    WriteJson(res, new { error = "Not found" });
                }
            }
            catch (Exception ex)
            {
                res.StatusCode = (int)HttpStatusCode.InternalServerError;
                WriteJson(res, new { error = ex.Message });
            }
        }

        private void ServeDashboard(HttpListenerResponse res)
        {
            string html = AdminApiLogic.GetDashboardHtml(context?.Config.AdminApiToken ?? "");
            byte[] bytes = Encoding.UTF8.GetBytes(html);
            res.ContentType = "text/html; charset=utf-8";
            res.ContentLength64 = bytes.Length;
            res.StatusCode = (int)HttpStatusCode.OK;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.OutputStream.Close();
        }

        private void ServeStatus(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (context == null) return;

            string auth = req.Headers["Authorization"] ?? "";
            if (!AdminApiLogic.IsAuthorized(auth, context.Config.AdminApiToken))
            {
                res.StatusCode = (int)HttpStatusCode.Unauthorized;
                WriteJson(res, new { error = "Unauthorized: Token Bearer inválido o ausente." });
                return;
            }

            var state = context.WorldState;
            int today = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;
            var onlineIds = Game1.getOnlineFarmers().Select(f => f.UniqueMultiplayerID).ToHashSet();

            var statusDto = new ServerStatusDto
            {
                ServerVersion = context.Manifest.Version.ToString(),
                RealTimeUtc = DateTime.UtcNow.ToString("o"),
                GameTimeOfDay = Context.IsWorldReady ? Game1.timeOfDay : 600,
                TotalGameDays = today,
                Season = Context.IsWorldReady && Game1.currentSeason != null ? Game1.currentSeason : "spring",
                Year = Context.IsWorldReady ? Game1.year : 1,
                DayOfMonth = Context.IsWorldReady ? Game1.dayOfMonth : 1,
                IsNightHold = RealTimeClockModule.IsNightHold,
                DedicatedHost = Game1.HasDedicatedHost,
                OnlinePlayersCount = onlineIds.Count,
                RegisteredPlayersCount = state.Players.Count,
                OccupiedDoorsCount = state.Doors.Count,
                TotalDoorsCount = FarmSlotModule.RegisteredDoors.Count,
                ActiveMarketListingsCount = state.MarketListings.Count(l => !l.IsSold)
            };

            foreach (var p in state.Players.Values)
            {
                bool isOnline = onlineIds.Contains(p.UniqueMultiplayerID);
                statusDto.Players.Add(new PlayerStatusDto
                {
                    Id = p.UniqueMultiplayerID,
                    Name = p.PlayerName,
                    IsOnline = isOnline,
                    SleepState = SleepPolicy.GetState(p, today, isOnline).ToString(),
                    FarmSlotId = p.FarmSlotId ?? "—",
                    SatietyUsed = p.SatietyUsed,
                    PendingMarketGold = p.PendingMarketGold
                });
            }

            foreach (var d in FarmSlotModule.RegisteredDoors)
            {
                bool occupied = state.Doors.TryGetValue(d.DoorId, out var ass);
                string ownerName = "";
                if (occupied && ass != null && state.Players.TryGetValue(ass.OwnerId, out var ownerRec))
                {
                    ownerName = ownerRec.PlayerName;
                }

                statusDto.Doors.Add(new DoorStatusDto
                {
                    DoorId = d.DoorId,
                    Location = d.LocationName ?? "Town",
                    TileX = d.TownTileX,
                    TileY = d.TownTileY,
                    SlotId = d.DefaultSlotId,
                    IsOccupied = occupied,
                    OwnerName = ownerName,
                    FarmType = ass?.FarmType ?? "Standard",
                    Policy = ass?.Policy.ToString() ?? "—"
                });
            }

            WriteJson(res, statusDto);
        }

        private void HandleRollover(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (context == null) return;

            string auth = req.Headers["Authorization"] ?? "";
            if (!AdminApiLogic.IsAuthorized(auth, context.Config.AdminApiToken))
            {
                res.StatusCode = (int)HttpStatusCode.Unauthorized;
                WriteJson(res, new { error = "Unauthorized" });
                return;
            }

            DailyRolloverModule.TriggerRolloverManual();
            WriteJson(res, new { success = true, message = "Rollover diario activado con éxito." });
        }

        private void HandleMinesReset(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (context == null) return;

            string auth = req.Headers["Authorization"] ?? "";
            if (!AdminApiLogic.IsAuthorized(auth, context.Config.AdminApiToken))
            {
                res.StatusCode = (int)HttpStatusCode.Unauthorized;
                WriteJson(res, new { error = "Unauthorized" });
                return;
            }

            EconomyModule.TriggerMineResetManual();
            WriteJson(res, new { success = true, message = "Regeneración de pisos mineros desocupados completada." });
        }

        private void HandleFestivalReschedule(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (context == null) return;

            string auth = req.Headers["Authorization"] ?? "";
            if (!AdminApiLogic.IsAuthorized(auth, context.Config.AdminApiToken))
            {
                res.StatusCode = (int)HttpStatusCode.Unauthorized;
                WriteJson(res, new { error = "Unauthorized" });
                return;
            }

            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
            string body = reader.ReadToEnd();
            using var doc = JsonDocument.Parse(body);
            string orig = doc.RootElement.GetProperty("originalDate").GetString() ?? "";
            string target = doc.RootElement.GetProperty("newDate").GetString() ?? "";

            if (string.IsNullOrWhiteSpace(orig) || string.IsNullOrWhiteSpace(target))
            {
                res.StatusCode = (int)HttpStatusCode.BadRequest;
                WriteJson(res, new { error = "Se requieren 'originalDate' y 'newDate'." });
                return;
            }

            context.WorldState.RescheduledFestivals[orig] = target;
            context.Monitor.Log($"[AdminApiModule] Festival reprogramado: {orig} -> {target}.", LogLevel.Info);

            WriteJson(res, new { success = true, message = $"Festival {orig} reprogramado a {target}." });
        }

        private static void WriteJson(HttpListenerResponse res, object data)
        {
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.OutputStream.Close();
        }
    }
}
