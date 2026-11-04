using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.Time;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
// Aliases para los DTOs de mapa sin ambigüedad con xTile
using MapTileKind      = Pelican4Ever.Core.Admin.MapTileKind;
using MapPlayerMarker  = Pelican4Ever.Core.Admin.MapPlayerMarker;
using MapWarpInfo      = Pelican4Ever.Core.Admin.MapWarpInfo;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Servidor HTTP local para la API REST de administración, el panel web y la API de anfitrión v1
    /// (TASK-801 a TASK-804). Es opcional: no arranca hasta configurar un token propio, y Pelican 4Ever
    /// funciona igual sin ella. Cualquier herramienta de operación puede usarla; el anfitrión agente de
    /// Stardew Voyager es solo uno de sus clientes posibles.
    /// </summary>
    public sealed class AdminApiModule : IModule
    {
        private static ModContext? context;
        private HttpListener? listener;
        private Thread? listenerThread;
        private volatile bool isRunning;

        /// <summary>Cola segura para ejecutar toda acción de la API en el hilo principal del juego (<c>UpdateTicked</c>) (TASK-803).</summary>
        public static MainThreadActionQueue ActionQueue { get; } = new();

        /// <summary>Registro de sucesos de solo añadir (<c>GET /api/v1/host/events?since=N</c>) (TASK-803).</summary>
        public static HostEventLog EventLog { get; } = new();

        /// <summary>Coordinador de reinicio controlado del servidor (<c>p4e_safe_restart</c> y <c>POST /api/v1/host/restart</c>) (TASK-804).</summary>
        public static SafeRestartCoordinator RestartCoordinator { get; } = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public string Name => "AdminApi";

        public ModuleSide Side => ModuleSide.Host;

        public void Register(ModContext ctx)
        {
            context = ctx;

            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            ctx.Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;

            ctx.Helper.ConsoleCommands.Add(
                "p4e_safe_restart",
                "Programa o cancela un reinicio controlado del servidor (TASK-804). Uso: p4e_safe_restart [motivo] [--immediate] [--minutes N] [--cancel]",
                OnConsoleSafeRestart);
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            StartServer();
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            ActionQueue.ClearPending();
            StopServer();
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            ActionQueue.Drain();

            if (Context.IsWorldReady &&
                RestartCoordinator.ConsumeIfImmediateTimerElapsed(DateTimeOffset.UtcNow, out SafeRestartStatusDto? triggered) &&
                triggered != null)
            {
                ExecuteControlledRestart(triggered);
            }
        }

        /// <summary>
        /// Invocado desde <see cref="DailyRolloverModule"/> justo tras completarse el guardado del cambio de día.
        /// Si había un reinicio controlado en modo por defecto (<c>WaitingForNextSave</c>), lo ejecuta ahora (TASK-804).
        /// </summary>
        public static void NotifySaveCompleted()
        {
            if (RestartCoordinator.ConsumeOnSaveCompleted(out SafeRestartStatusDto? triggered) && triggered != null)
            {
                ExecuteControlledRestart(triggered);
            }
        }

        private static void ExecuteControlledRestart(SafeRestartStatusDto triggered)
        {
            if (context == null)
                return;

            int today = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;
            int time = Context.IsWorldReady ? Game1.timeOfDay : 600;

            string msg = $"[Reinicio Controlado] Ejecutando reinicio del servidor ({triggered.Mode}). Motivo: {triggered.Reason}";
            BroadcastToAllPlayers(msg, "both");
            context.Monitor.Log($"[AdminApiModule] {msg}", LogLevel.Warn);

            EventLog.Append(
                HostEventTypes.RestartExecuted,
                today,
                time,
                msg,
                new Dictionary<string, object?>
                {
                    ["mode"] = triggered.Mode,
                    ["reason"] = triggered.Reason,
                    ["immediate"] = triggered.Immediate
                });

            // Con RestartExitsProcess el juego se cierra al llegar al título y el supervisor lo relanza con el mod
            // actual; si no, la carga automática (AutoHostSaveName) reabre la partida en este mismo proceso
            bool exitsProcess = AutoHostModule.PrepareControlledRestart();
            context.Monitor.Log($"[AdminApiModule] Reinicio controlado: {(exitsProcess ? "se cerrará el proceso" : "vuelta al título")}.", LogLevel.Info);

            DelayedAction.functionAfterDelay(() =>
            {
                Game1.ExitToTitle();
            }, 1000);
        }

        private void OnConsoleSafeRestart(string command, string[] args)
        {
            if (context == null || !Context.IsMainPlayer)
            {
                context?.Monitor.Log("[AdminApiModule] p4e_safe_restart solo está disponible en el Host con una partida cargada.", LogLevel.Warn);
                return;
            }

            bool immediate = false;
            bool cancel = false;
            int? warningMinutes = null;
            var reasonWords = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--immediate", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "-i", StringComparison.OrdinalIgnoreCase))
                {
                    immediate = true;
                }
                else if (string.Equals(arg, "--cancel", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(arg, "-c", StringComparison.OrdinalIgnoreCase))
                {
                    cancel = true;
                }
                else if ((string.Equals(arg, "--minutes", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(arg, "-m", StringComparison.OrdinalIgnoreCase)) &&
                         i + 1 < args.Length &&
                         int.TryParse(args[i + 1], out int parsedMins))
                {
                    warningMinutes = parsedMins;
                    i++;
                }
                else
                {
                    reasonWords.Add(arg);
                }
            }

            string reason = string.Join(" ", reasonWords);
            SafeRestartStatusDto status = ApplySafeRestartOnMainThread(new HostRestartRequestDto
            {
                Reason = reason,
                Immediate = immediate,
                WarningMinutes = warningMinutes,
                Cancel = cancel
            });

            context.Monitor.Log($"[AdminApiModule] {status.WarningAnnouncement}", LogLevel.Info);
        }

        private void StartServer()
        {
            if (context == null || isRunning) return;

            int port = context.Config.AdminApiPort;
            if (port <= 0) return;

            // TASK-803 Criterio 1: con el token por defecto o vacío, el módulo NO arranca y lo registra.
            if (!AdminApiLogic.IsTokenConfiguredSecurely(context.Config.AdminApiToken))
            {
                context.Monitor.Log(
                    "[AdminApiModule] SEGURIDAD: AdminApiToken está vacío o conserva el valor por defecto ('pelican4ever_secret_admin_token'). " +
                    "El servidor HTTP de administración NO arrancará hasta configurar un token único en config.json.",
                    LogLevel.Error);
                return;
            }

            try
            {
                listener = new HttpListener();
                IReadOnlyList<string> prefixes = AdminApiLogic.GetListenerPrefixes(port, context.Config.AdminApiBindAllInterfaces);
                foreach (string prefix in prefixes)
                {
                    listener.Prefixes.Add(prefix);
                }

                listener.Start();
                isRunning = true;

                listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "Pelican4Ever-AdminApi"
                };
                listenerThread.Start();

                string bindScope = context.Config.AdminApiBindAllInterfaces ? "todas las interfaces (*)" : "127.0.0.1 (solo local)";
                context.Monitor.Log($"[AdminApiModule] Servidor API v1 y Dashboard activo en http://127.0.0.1:{port}/ ({bindScope}).", LogLevel.Info);
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[AdminApiModule] Fallo al iniciar el listener HTTP en puerto {port}: {ex.Message}", LogLevel.Error);
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

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = (int)HttpStatusCode.NoContent;
                res.Close();
                return;
            }

            string rawPath = req.Url?.AbsolutePath ?? "/";
            string normPath = HostApiCatalog.NormalizePath(rawPath);
            string method = req.HttpMethod.ToUpperInvariant();

            try
            {
                if (context == null)
                {
                    res.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                    WriteJson(res, new { error = "Contexto del servidor no inicializado." });
                    return;
                }

                // TASK-803 Criterio 1: Sin token válido, GET / y cualquier /api/ responden 401.
                string? authHeader = req.Headers["Authorization"];
                string? queryToken = req.QueryString["token"];
                ApiAccessCheckResult access = AdminApiLogic.CheckAccess(normPath, authHeader, queryToken, context.Config.AdminApiToken);
                if (!access.Allowed)
                {
                    res.StatusCode = access.StatusCode;
                    WriteJson(res, new { error = access.ErrorMessage });
                    return;
                }

                // ── Panel Web Dashboard ──
                if (method == "GET" && (normPath == "/" || normPath == "/dashboard"))
                {
                    ServeDashboard(res);
                    return;
                }

                // ── Endpoints de /api/v1/host/ (TASK-803 y TASK-804) ──
                if (normPath.StartsWith(HostApiCatalog.BasePath, StringComparison.OrdinalIgnoreCase))
                {
                    HandleHostV1Request(method, normPath, req, res);
                    return;
                }

                // ── Endpoints clásicos de /api/ (TASK-801) despachados en el hilo del juego ──
                if (method == "GET" && normPath == "/api/status")
                {
                    ServerStatusDto status = ExecuteOnGameThread(BuildLegacyStatusDto);
                    WriteJson(res, status);
                }
                else if (method == "POST" && normPath == "/api/rollover")
                {
                    ExecuteOnGameThread(() =>
                    {
                        DailyRolloverModule.TriggerRolloverManual();
                        return true;
                    });
                    WriteJson(res, new { success = true, message = "Rollover diario activado con éxito en el hilo principal." });
                }
                else if (method == "POST" && normPath == "/api/mines/reset")
                {
                    ExecuteOnGameThread(() =>
                    {
                        EconomyModule.TriggerMineResetManual();
                        return true;
                    });
                    WriteJson(res, new { success = true, message = "Regeneración de pisos mineros desocupados completada en el hilo principal." });
                }
                else if (method == "POST" && normPath == "/api/festival/reschedule")
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

        private void HandleHostV1Request(string method, string normPath, HttpListenerRequest req, HttpListenerResponse res)
        {
            HostApiRouteSpec? route = HostApiCatalog.MatchRoute(method, normPath);
            if (route == null)
            {
                res.StatusCode = (int)HttpStatusCode.NotFound;
                WriteJson(res, new { error = $"Ruta no encontrada en /api/v1/host: {method} {normPath}" });
                return;
            }

            switch ($"{route.Method} {route.Path}")
            {
                case "GET /api/v1/host/clock":
                {
                    HostClockDto dto = ExecuteOnGameThread(BuildHostClockDto);
                    WriteJson(res, dto);
                    break;
                }

                case "GET /api/v1/host/players":
                {
                    HostPlayersResponseDto dto = ExecuteOnGameThread(BuildHostPlayersDto);
                    WriteJson(res, dto);
                    break;
                }

                case "GET /api/v1/host/festival":
                {
                    HostFestivalDto dto = ExecuteOnGameThread(FestivalParticipationModule.GetHostFestivalSnapshot);
                    WriteJson(res, dto);
                    break;
                }

                case "POST /api/v1/host/festival/start":
                {
                    MainThreadExecutionResult<(bool Started, string Message, long? EventId)> result =
                        ExecuteWithThreadInfoOnGameThread(FestivalParticipationModule.StartRunFromHostApi);

                    WriteJson(res, new HostActionResultDto
                    {
                        Ok = result.Value.Started,
                        Message = result.Value.Message,
                        ExecutedOnThreadId = result.ExecutedOnThreadId,
                        EventId = result.Value.EventId
                    });
                    break;
                }

                case "POST /api/v1/host/festival/ceremony":
                {
                    MainThreadExecutionResult<(bool Started, string Message, long? EventId)> result =
                        ExecuteWithThreadInfoOnGameThread(FestivalParticipationModule.StartCeremonyFromHostApi);

                    WriteJson(res, new HostActionResultDto
                    {
                        Ok = result.Value.Started,
                        Message = result.Value.Message,
                        ExecutedOnThreadId = result.ExecutedOnThreadId,
                        EventId = result.Value.EventId
                    });
                    break;
                }

                case "POST /api/v1/host/announce":
                {
                    HostAnnounceRequestDto? body = ReadJsonBody<HostAnnounceRequestDto>(req);
                    if (body == null || string.IsNullOrWhiteSpace(body.Message))
                    {
                        res.StatusCode = (int)HttpStatusCode.BadRequest;
                        WriteJson(res, new { error = "El campo 'message' es obligatorio." });
                        return;
                    }

                    MainThreadExecutionResult<long> result = ExecuteWithThreadInfoOnGameThread(() =>
                    {
                        BroadcastToAllPlayers(body.Message, body.Channel);
                        int today = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;
                        int time = Context.IsWorldReady ? Game1.timeOfDay : 600;
                        HostEventEntry evt = EventLog.Append(
                            HostEventTypes.HostAnnouncement,
                            today,
                            time,
                            $"Anuncio emitido ({body.Channel}): {body.Message}",
                            new Dictionary<string, object?>
                            {
                                ["message"] = body.Message,
                                ["channel"] = body.Channel
                            });
                        return evt.Id;
                    });

                    WriteJson(res, new HostActionResultDto
                    {
                        Ok = true,
                        Message = $"Anuncio emitido ({body.Channel}).",
                        ExecutedOnThreadId = result.ExecutedOnThreadId,
                        EventId = result.Value
                    });
                    break;
                }

                case "POST /api/v1/host/rest":
                {
                    // El id se lee en el hilo del juego justo tras registrar el descanso: leído después,
                    // otra acción encolada podría haber añadido un suceso y el cliente verificaría el equivocado.
                    MainThreadExecutionResult<(HostRestResult Outcome, long? EventId)> result = ExecuteWithThreadInfoOnGameThread(() =>
                    {
                        HostRestResult rest = BedLogoutModule.RestHostPlayer(triggeredFromApi: true);
                        return (rest, rest.RestRecorded ? EventLog.LastEventId : (long?)null);
                    });

                    WriteJson(res, new HostActionResultDto
                    {
                        Ok = result.Value.Outcome.RestRecorded,
                        Message = result.Value.Outcome.Message,
                        ExecutedOnThreadId = result.ExecutedOnThreadId,
                        EventId = result.Value.EventId
                    });
                    break;
                }

                case "POST /api/v1/host/restart":
                {
                    HostRestartRequestDto body = ReadJsonBody<HostRestartRequestDto>(req) ?? new HostRestartRequestDto();
                    SafeRestartStatusDto status = ExecuteOnGameThread(() => ApplySafeRestartOnMainThread(body));
                    WriteJson(res, status);
                    break;
                }

                case "GET /api/v1/host/events":
                {
                    long sinceId = 0;
                    string? rawSince = req.QueryString["since"];
                    if (!string.IsNullOrWhiteSpace(rawSince))
                        long.TryParse(rawSince, out sinceId);

                    int limit = 500;
                    string? rawLimit = req.QueryString["limit"];
                    if (!string.IsNullOrWhiteSpace(rawLimit) && int.TryParse(rawLimit, out int parsedLimit) && parsedLimit > 0)
                        limit = Math.Min(parsedLimit, 2000);

                    HostEventsResponseDto eventsDto = EventLog.GetSince(sinceId, limit);
                    WriteJson(res, eventsDto);
                    break;
                }

                case "GET /api/v1/host/map":
                {
                    string locationName = req.QueryString["location"] ?? "Town";
                    HostMapDto mapDto = ExecuteOnGameThread(() => BuildHostMapDto(locationName));
                    WriteJson(res, mapDto);
                    break;
                }
            }
        }

        private static T ExecuteOnGameThread<T>(Func<T> action)
        {
            return ActionQueue.EnqueueAsync(action).GetAwaiter().GetResult();
        }

        private static MainThreadExecutionResult<T> ExecuteWithThreadInfoOnGameThread<T>(Func<T> action)
        {
            return ActionQueue.EnqueueWithThreadInfoAsync(action).GetAwaiter().GetResult();
        }

        private static SafeRestartStatusDto ApplySafeRestartOnMainThread(HostRestartRequestDto request)
        {
            int today = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;
            int time = Context.IsWorldReady ? Game1.timeOfDay : 600;

            if (request.Cancel)
            {
                SafeRestartStatusDto cancelled = RestartCoordinator.Cancel();
                BroadcastToAllPlayers(cancelled.WarningAnnouncement, "both");
                cancelled.EventId = EventLog.Append(
                    HostEventTypes.RestartCancelled,
                    today,
                    time,
                    cancelled.WarningAnnouncement).Id;
                return cancelled;
            }

            var onlineIds = Context.IsWorldReady
                ? Game1.getOnlineFarmers().Select(f => f.UniqueMultiplayerID).ToHashSet()
                : new HashSet<long>();

            int unrestedActive = 0;
            if (context != null)
            {
                foreach (var rec in context.WorldState.Players.Values)
                {
                    bool isOnline = onlineIds.Contains(rec.UniqueMultiplayerID);
                    if ((rec.LastActiveGameDay == today || isOnline) && rec.RestedOnGameDay != today)
                        unrestedActive++;
                }
            }

            int warningMins = request.WarningMinutes ?? (context?.Config.SafeRestartWarningMinutes ?? 5);
            SafeRestartStatusDto scheduled = RestartCoordinator.Schedule(
                request.Reason,
                request.Immediate,
                warningMins,
                today,
                time,
                onlineIds.Count,
                unrestedActive,
                DateTimeOffset.UtcNow);

            BroadcastToAllPlayers(scheduled.WarningAnnouncement, "both");

            scheduled.EventId = EventLog.Append(
                HostEventTypes.RestartScheduled,
                today,
                time,
                scheduled.WarningAnnouncement,
                new Dictionary<string, object?>
                {
                    ["mode"] = scheduled.Mode,
                    ["reason"] = scheduled.Reason,
                    ["immediate"] = scheduled.Immediate,
                    ["warningMinutes"] = scheduled.WarningMinutes,
                    ["waitingForNextSave"] = scheduled.WaitingForNextSave,
                    ["unrestedActivePlayers"] = scheduled.ProgressAtRisk.UnrestedActivePlayersCount
                }).Id;

            return scheduled;
        }

        private static void BroadcastToAllPlayers(string message, string? channel)
        {
            if (!Context.IsWorldReady)
                return;

            string normChannel = (channel ?? "both").Trim().ToLowerInvariant();
            if (normChannel is "chat" or "both")
            {
                Game1.chatBox?.addMessage(message, new Color(255, 215, 0));
            }
            if (normChannel is "hud" or "both")
            {
                Game1.addHUDMessage(new HUDMessage(message, HUDMessage.newQuest_type));
            }
        }

        private static HostClockDto BuildHostClockDto()
        {
            var cfg = context!.Config;
            DateTime localNow = ServerTime.LocalNow(cfg.TimeZoneId);
            TimeSpan rolloverTime = cfg.ParsedCalendarDayChangeTime;
            TimeSpan untilRollover = rolloverTime - localNow.TimeOfDay;
            if (untilRollover <= TimeSpan.Zero)
                untilRollover += TimeSpan.FromDays(1);

            DateTime nextRolloverLocal = localNow.Add(untilRollover);
            int today = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;

            return new HostClockDto
            {
                RealTimeUtc = DateTimeOffset.UtcNow.ToString("O"),
                LocalTime = localNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                TimeZoneId = cfg.TimeZoneId,
                GameTimeOfDay = Context.IsWorldReady ? Game1.timeOfDay : 600,
                TotalGameDays = today,
                Year = Context.IsWorldReady ? Game1.year : 1,
                Season = Context.IsWorldReady && Game1.currentSeason != null ? Game1.currentSeason : "spring",
                DayOfMonth = Context.IsWorldReady ? Game1.dayOfMonth : 1,
                IsNightHold = RealTimeClockModule.IsNightHold,
                NextRolloverLocalTime = nextRolloverLocal.ToString("yyyy-MM-ddTHH:mm:ss"),
                SecondsUntilNextRollover = Math.Max(0, (int)untilRollover.TotalSeconds),
                LastRolloverGameDay = context.WorldState.LastRolloverGameDay,
                LastRolloverOutcomeSummary = DailyRolloverModule.LastRolloverOutcomeSummary,
                LastBackupFileName = DailyRolloverModule.LastBackupFileName,
                LastBackupCreatedUtc = DailyRolloverModule.LastBackupCreatedUtc,
                SaveFolderName = Context.IsWorldReady ? Constants.SaveFolderName ?? string.Empty : string.Empty,
                IsMultiplayerServer = Context.IsWorldReady && Game1.IsServer && Game1.server != null
            };
        }

        private static HostPlayersResponseDto BuildHostPlayersDto()
        {
            var state = context!.WorldState;
            int today = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;
            var onlineFarmers = Context.IsWorldReady
                ? Game1.getOnlineFarmers().ToDictionary(f => f.UniqueMultiplayerID, f => f)
                : new Dictionary<long, Farmer>();
            var inFestivalIds = Context.IsWorldReady
                ? FestivalParticipationModule.PlayersInFestival().ToHashSet()
                : new HashSet<long>();
            long hostId = Context.IsWorldReady && Game1.MasterPlayer != null ? Game1.MasterPlayer.UniqueMultiplayerID : 0;

            var response = new HostPlayersResponseDto
            {
                TotalOnline = onlineFarmers.Count,
                TotalRegistered = state.Players.Count
            };

            foreach (var p in state.Players.Values.OrderBy(p => p.UniqueMultiplayerID))
            {
                bool isOnline = onlineFarmers.TryGetValue(p.UniqueMultiplayerID, out Farmer? farmer);
                var residence = state.FindResidenceOf(p.UniqueMultiplayerID);

                response.Players.Add(new HostPlayerDetailDto
                {
                    PlayerId = p.UniqueMultiplayerID,
                    Name = !string.IsNullOrWhiteSpace(p.PlayerName) ? p.PlayerName : (farmer?.Name ?? string.Empty),
                    IsOnline = isOnline,
                    IsHost = p.UniqueMultiplayerID == hostId,
                    LocationName = isOnline ? (farmer?.currentLocation?.NameOrUniqueName ?? string.Empty) : string.Empty,
                    TileX = isOnline && farmer != null ? farmer.TilePoint.X : 0,
                    TileY = isOnline && farmer != null ? farmer.TilePoint.Y : 0,
                    ResidenceSlotId = residence?.FarmSlotId ?? (p.FarmSlotId ?? string.Empty),
                    ResidenceDoorId = residence?.DoorId ?? string.Empty,
                    ResidenceFarmType = residence?.FarmType ?? string.Empty,
                    InFestival = inFestivalIds.Contains(p.UniqueMultiplayerID),
                    SleepState = SleepPolicy.GetState(p, today, isOnline).ToString(),
                    LastActiveGameDay = p.LastActiveGameDay,
                    RestedOnGameDay = p.RestedOnGameDay,
                    LastOutcome = p.LastOutcome.ToString(),
                    SatietyUsed = p.SatietyUsed,
                    WellFedBuffsUsed = p.WellFedBuffsUsed,
                    SpaEnergyUsed = p.SpaEnergyUsed,
                    SatietyGameDay = p.SatietyGameDay,
                    PendingMarketGold = p.PendingMarketGold,
                    IsClaimed = PlayerAuthLogic.IsClaimed(p),
                    OwnerTokenMasked = PlayerAuthLogic.MaskToken(p.OwnerToken)
                });
            }

            return response;
        }

        private static ServerStatusDto BuildLegacyStatusDto()
        {
            var state = context!.WorldState;
            int today = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;
            var onlineIds = Context.IsWorldReady
                ? Game1.getOnlineFarmers().Select(f => f.UniqueMultiplayerID).ToHashSet()
                : new HashSet<long>();

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

            return statusDto;
        }

        // ── /api/v1/host/map ─────────────────────────────────────────────────────────
        // Debe ejecutarse en el hilo del juego (accede a Game1 y xTile).

        private static readonly string[] MapLocationPriority = new[]
        {
            "Town", "Farm", "BusStop", "Forest", "Beach", "Mountain",
            "Mine", "Desert", "Railroad", "Woods", "Sewer", "WitchSwamp"
        };

        private static HostMapDto BuildHostMapDto(string requestedLocation)
        {
            if (!Context.IsWorldReady)
            {
                return new HostMapDto
                {
                    LocationName = requestedLocation,
                    Width = 0, Height = 0,
                    AvailableMaps = new List<string>()
                };
            }

            // Localizaciones disponibles
            var availableMaps = new List<string>();
            foreach (var loc in Game1.locations)
            {
                if (loc?.Name != null)
                    availableMaps.Add(loc.Name);
            }
            availableMaps.Sort();

            // Resolver la localización pedida
            GameLocation? target = Game1.getLocationFromName(requestedLocation)
                ?? Game1.getLocationFromName("Town");

            if (target == null)
            {
                return new HostMapDto
                {
                    LocationName = requestedLocation,
                    Width = 0, Height = 0,
                    AvailableMaps = availableMaps
                };
            }

            var map  = target.Map;
            int w    = map?.Layers[0]?.LayerWidth  ?? 0;
            int h    = map?.Layers[0]?.LayerHeight ?? 0;
            var tiles = new byte[w * h];

            if (map != null && w > 0 && h > 0)
            {
                // Capa de colisiones (Back = suelo, Buildings = objetos/muros, Front = decoración)
                var backLayer     = map.GetLayer("Back");
                var buildingLayer = map.GetLayer("Buildings");
                var frontLayer    = map.GetLayer("Front");

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int idx = y * w + x;
                        var loc2 = target;

                        // Prioridad: colisiones reales > tile sheet
                        bool isPassable = loc2.isTilePassable(new Vector2(x, y))
                            && !loc2.isObjectAtTile(x, y);

                        var backTile  = backLayer?.Tiles[x, y];
                        var bldTile   = buildingLayer?.Tiles[x, y];

                        // Water
                        if (loc2.isWaterTile(x, y))
                        {
                            tiles[idx] = (byte)MapTileKind.Water;
                            continue;
                        }

                        // Building layer: muros, edificios
                        if (bldTile != null)
                        {
                            tiles[idx] = (byte)MapTileKind.Building;
                            continue;
                        }

                        // Back layer nula = fuera del mapa
                        if (backTile == null)
                        {
                            tiles[idx] = (byte)MapTileKind.Unknown;
                            continue;
                        }

                        // Propiedades del tile
                        string? tileType = null;
                        backTile.Properties.TryGetValue("Type", out var typeProp);
                        tileType = typeProp?.ToString();

                        if (string.Equals(tileType, "Water", System.StringComparison.OrdinalIgnoreCase))
                        {
                            tiles[idx] = (byte)MapTileKind.Water;
                            continue;
                        }

                        if (string.Equals(tileType, "Grass", System.StringComparison.OrdinalIgnoreCase)
                            || Core.World.TerrainTileClassifier.IsGrassTileIndex(backTile.TileIndex, tileType))
                        {
                            tiles[idx] = (byte)MapTileKind.Grass;
                            continue;
                        }

                        if (isPassable)
                        {
                            tiles[idx] = (byte)MapTileKind.Open;
                            continue;
                        }

                        tiles[idx] = (byte)MapTileKind.Blocked;
                    }
                }

                // Marcar warps
                if (target.warps != null)
                {
                    foreach (var warp in target.warps)
                    {
                        int wx = warp.X, wy = warp.Y;
                        if (wx >= 0 && wx < w && wy >= 0 && wy < h)
                            tiles[wy * w + wx] = (byte)MapTileKind.Warp;
                    }
                }

                // Marcar objetos del mundo: árboles
                foreach (var tree in target.terrainFeatures.Values)
                {
                    var pos = tree.Tile;
                    int tx = (int)pos.X, ty = (int)pos.Y;
                    if (tx >= 0 && tx < w && ty >= 0 && ty < h)
                    {
                        byte cur = tiles[ty * w + tx];
                        if (cur == (byte)MapTileKind.Open || cur == (byte)MapTileKind.Grass)
                            tiles[ty * w + tx] = (byte)MapTileKind.Tree;
                    }
                }

                // Marcar NPCs
                foreach (var npc in target.characters)
                {
                    int nx = npc.TilePoint.X, ny = npc.TilePoint.Y;
                    if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                        tiles[ny * w + nx] = (byte)MapTileKind.NPC;
                }
            }

            // Jugadores online en esta localización
            var players = new List<MapPlayerMarker>();
            long hostId = Game1.MasterPlayer?.UniqueMultiplayerID ?? 0;
            foreach (var farmer in Game1.getOnlineFarmers())
            {
                if (farmer?.currentLocation?.NameOrUniqueName != target.NameOrUniqueName) continue;
                int fx = farmer.TilePoint.X, fy = farmer.TilePoint.Y;
                players.Add(new MapPlayerMarker
                {
                    PlayerId = farmer.UniqueMultiplayerID,
                    Name     = farmer.Name,
                    TileX    = fx,
                    TileY    = fy,
                    IsHost   = farmer.UniqueMultiplayerID == hostId
                });
                // Marcamos el tile también
                if (fx >= 0 && fx < w && fy >= 0 && fy < h)
                    tiles[fy * w + fx] = (byte)MapTileKind.Player;
            }

            // Warps como objetos
            var warps = new List<MapWarpInfo>();
            if (target.warps != null)
            {
                foreach (var warp in target.warps)
                {
                    warps.Add(new MapWarpInfo
                    {
                        TileX     = warp.X,
                        TileY     = warp.Y,
                        TargetMap = warp.TargetName,
                        TargetX   = warp.TargetX,
                        TargetY   = warp.TargetY
                    });
                }
            }

            return new HostMapDto
            {
                LocationName  = target.NameOrUniqueName,
                Width         = w,
                Height        = h,
                Tiles         = tiles,
                Players       = players,
                Warps         = warps,
                AvailableMaps = availableMaps,
                Season        = Game1.currentSeason ?? "spring",
                GameDay       = Game1.Date?.TotalDays ?? 0
            };
        }

        private void ServeDashboard(HttpListenerResponse res)
        {
            string html = AdminApiLogic.GetDashboardHtml("");
            byte[] bytes = Encoding.UTF8.GetBytes(html);
            res.ContentType = "text/html; charset=utf-8";
            res.ContentLength64 = bytes.Length;
            res.StatusCode = (int)HttpStatusCode.OK;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.OutputStream.Close();
        }

        private void HandleFestivalReschedule(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (context == null) return;

            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
            string body = reader.ReadToEnd();
            using var doc = JsonDocument.Parse(body);
            string orig = doc.RootElement.TryGetProperty("originalDate", out JsonElement origEl) ? (origEl.GetString() ?? "") : "";
            string target = doc.RootElement.TryGetProperty("newDate", out JsonElement targetEl) ? (targetEl.GetString() ?? "") : "";

            if (string.IsNullOrWhiteSpace(orig) || string.IsNullOrWhiteSpace(target))
            {
                res.StatusCode = (int)HttpStatusCode.BadRequest;
                WriteJson(res, new { error = "Se requieren 'originalDate' y 'newDate'." });
                return;
            }

            ExecuteOnGameThread(() =>
            {
                context.WorldState.RescheduledFestivals[orig] = target;
                context.Monitor.Log($"[AdminApiModule] Festival reprogramado: {orig} -> {target}.", LogLevel.Info);
                return true;
            });

            WriteJson(res, new { success = true, message = $"Festival {orig} reprogramado a {target}." });
        }

        private static T? ReadJsonBody<T>(HttpListenerRequest req)
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
            string body = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(body))
                return default;

            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }

        private static void WriteJson(HttpListenerResponse res, object data)
        {
            string json = JsonSerializer.Serialize(data, JsonOptions);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.OutputStream.Close();
        }
    }
}
