using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.Time;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Gestiona aviso y rollover: guarda y respalda a las 02:00, cierra peers y avanza calendario;
    /// el reloj de juego y los horarios siguen empezando a las 06:00.
    /// </summary>
    public sealed class DailyRolloverModule : IModule
    {
        private static ModContext context = null!;
        private static DailyRolloverModule? instance;
        private bool warned10 = false;
        private DateTime lastRolloverBoundaryDate = DateTime.MinValue;
        private readonly SafeRolloverWorkflow rollover = new();
        private IEnumerator<int>? preCloseSave;
        private IEnumerator<int>? postCloseSave;
        private bool dayStartCausedByLoad;
        private DateTimeOffset peerCloseDeadlineUtc = DateTimeOffset.MinValue;
        private bool forcedPeerDisconnect;
        private DateTimeOffset dayAdvanceStartedUtc = DateTimeOffset.MinValue;
        private DateTimeOffset dayAdvanceLastAttemptUtc = DateTimeOffset.MinValue;
        private int dayAdvanceNudges;
        private const string RolloverCloseMessageType = "Pelican4Ever.RolloverClose";
        private static readonly TimeSpan PeerGracePeriod = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan ForcedDisconnectGracePeriod = TimeSpan.FromSeconds(10);

        public static string LastRolloverOutcomeSummary { get; private set; } = string.Empty;
        public static string LastBackupFileName { get; private set; } = string.Empty;
        public static string LastBackupCreatedUtc { get; private set; } = string.Empty;

        public string Name => "DailyRollover";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;
            instance = this;

            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            if (Context.IsMainPlayer)
            {
                ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
                ctx.Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
                ctx.Helper.Events.GameLoop.DayStarted += OnDayStarted;
                ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
                ctx.Helper.ConsoleCommands.Add("p4e_rollover_now", "Fuerza la secuencia segura de guardado y rollover.", OnForceRollover);
            }
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            // El DayStarted inmediato a SaveLoaded lo provoca la carga, no un rollover.
            dayStartCausedByLoad = true;
            lastRolloverBoundaryDate = DateTime.MinValue;
            DateTime localNow = ServerTime.LocalNow(context.Config.TimeZoneId);
            var calendar = new ServerCalendar(context.Config.ParsedCalendarEpoch, context.Config.ParsedCalendarDayChangeTime);
            if (RolloverTiming.IsBoundaryWindow(localNow.TimeOfDay, context.Config.ParsedCalendarDayChangeTime)
                && calendar.GetTotalDays(localNow) <= Game1.Date.TotalDays)
                lastRolloverBoundaryDate = localNow.Date;
            CheckAndPlanCatchUp();
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            int newDay = Game1.Date.TotalDays;
            int endedDay = newDay - 1;
            rollover.DayStarted();
            dayAdvanceNudges = 0;
            postCloseSave?.Dispose();
            postCloseSave = null;

            context.Monitor.Log($"[DailyRolloverModule] Día iniciado: {Game1.Date} (TotalDays: {newDay}). Procesando consecuencias del día {endedDay}...", LogLevel.Info);

            // DayStarted también ocurre al cargar el save. El save de fin de día se escribe ANTES de que
            // LastRolloverGameDay se actualice, así que tras reiniciar ese campo llega desactualizado: la
            // carga nunca cuenta como rollover. Las consecuencias ya las aplicó el proceso que avanzó el día.
            var worldState = context.WorldState;
            bool startedByLoad = dayStartCausedByLoad;
            dayStartCausedByLoad = false;
            if (!RolloverDayGuard.ShouldProcessDayStart(startedByLoad, worldState.LastRolloverGameDay, endedDay))
            {
                context.Monitor.Log($"[DailyRolloverModule] Día {endedDay}: {(startedByLoad ? "inicio causado por carga de partida" : "ya comprometido en el save")}; se omiten penalties y counters repetidos.", LogLevel.Info);
                warned10 = false;
                rollover.ResetAfterDayStarted();
                peerCloseDeadlineUtc = DateTimeOffset.MinValue;
                forcedPeerDisconnect = false;
                return;
            }

            var cfg = context.Config;
            int restedCount = 0;
            int penalizedCount = 0;
            int untouchedCount = 0;

            DateTime localNow = Core.Time.ServerTime.LocalNow(cfg.TimeZoneId);

            foreach (var kvp in worldState.Players)
            {
                var record = kvp.Value;
                bool isOnline = Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == record.UniqueMultiplayerID);
                bool isEarlyRiser = isOnline && SleepPolicy.IsEarlyRiser(record, endedDay, localNow, cfg.ParsedCalendarDayChangeTime, cfg.EarlyMorningGraceMinutes);
                RolloverOutcome outcome = SleepPolicy.Evaluate(record, endedDay, isOnline, isEarlyRiser);
                SleepPolicy.Commit(record, outcome);

                switch (outcome)
                {
                    case RolloverOutcome.Rested:
                        restedCount++;
                        break;
                    case RolloverOutcome.Penalized:
                        penalizedCount++;
                        break;
                    default:
                        untouchedCount++;
                        break;
                }

                // Si el jugador está conectado o en la partida, ajustar energía y aplicar gasto médico si fue penalizado
                Farmer? farmer = Game1.GetPlayer(record.UniqueMultiplayerID);
                if (farmer != null)
                {
                    farmer.Stamina = SleepPolicy.WakeUpEnergy(outcome, farmer.MaxStamina, cfg.PenaltyEnergyFraction, farmer.Stamina);

                    if (outcome == RolloverOutcome.Penalized)
                    {
                        int fee = SleepPolicy.MedicalFee(farmer.Money, cfg.PenaltyGoldPercent, cfg.PenaltyGoldCap);
                        if (fee > 0)
                        {
                            int newMoney = Math.Max(0, farmer.Money - fee);
                            SetFarmerMoney(farmer, newMoney);
                            context.Monitor.Log($"[DailyRolloverModule] Cargo médico de {fee}g aplicado a {record.PlayerName} ({record.UniqueMultiplayerID}).", LogLevel.Info);
                        }
                    }
                }

                // Resetear contadores de comida y spa para el nuevo día y anclar el tiempo offline
                record.EnsureDailyCounters(newDay);
                record.OfflineStaminaAnchorUtc = DateTimeOffset.UtcNow;
            }

            worldState.LastRolloverGameDay = endedDay;
            LastRolloverOutcomeSummary = $"rested={restedCount}, penalized={penalizedCount}, untouched={untouchedCount}";

            // Congelar caída de amistad por inactividad para jugadores desconectados
            FreezeFriendshipDecayForOfflinePlayers();

            // Crear copia de seguridad rotativa del guardado
            CreateSaveBackup();

            AdminApiModule.EventLog.Append(
                HostEventTypes.RolloverCompleted,
                newDay,
                Game1.timeOfDay,
                $"Rollover completado para día cerrado {endedDay} ({LastRolloverOutcomeSummary}).",
                new Dictionary<string, object?>
                {
                    ["closedGameDay"] = endedDay,
                    ["newGameDay"] = newDay,
                    ["rested"] = restedCount,
                    ["penalized"] = penalizedCount,
                    ["untouched"] = untouchedCount,
                    ["backupFileName"] = LastBackupFileName
                });

            // Reiniciar banderas de aviso para el próximo rollover
            warned10 = false;
            rollover.ResetAfterDayStarted();
            peerCloseDeadlineUtc = DateTimeOffset.MinValue;
            forcedPeerDisconnect = false;

            // Notificar al coordinador de reinicio controlado (TASK-804) que el guardado ha concluido
            AdminApiModule.NotifySaveCompleted();
        }

        private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || !Context.IsMainPlayer)
                return;

            ProcessPeerClose();
            WatchNativeDayAdvance();
            var cfg = context.Config;
            DateTime now = ServerTime.LocalNow(cfg.TimeZoneId);
            TimeSpan timeOfDay = now.TimeOfDay;
            TimeSpan rolloverTime = cfg.ParsedCalendarDayChangeTime; // 02:00:00

            // Calcular diferencia hasta el cambio de fecha de las 02:00.
            TimeSpan diff = RolloverTiming.UntilNextBoundary(timeOfDay, rolloverTime);

            // Aviso único al alcanzar la ventana de 10 min, incluso si un tick se retrasó unos segundos.
            if (RolloverTiming.IsWarningDue(diff, warningMinutes: 10) && !warned10)
            {
                warned10 = true;
                BroadcastHUDMessage("Aviso: en 10 minutos se guardará la granja y se cerrarán las conexiones para el cambio de fecha.", HUDMessage.newQuest_type);
            }

            // Inicio de la secuencia diaria exacto a la hora configurada (primeros 3 segundos).
            if (RolloverTiming.IsBoundaryWindow(timeOfDay, rolloverTime))
            {
                // Para evitar disparos repetidos en el mismo minuto
                if (lastRolloverBoundaryDate.Date != now.Date && rollover.Stage == SafeRolloverStage.Idle)
                {
                    context.Monitor.Log($"[DailyRolloverModule] Hora {rolloverTime.Hours:D2}:{rolloverTime.Minutes:D2} alcanzada. Iniciando secuencia de rollover...", LogLevel.Info);
                    ExecuteRollover();
                }
            }
        }

        private void OnForceRollover(string command, string[] args)
        {
            BeginRollover("manual");
        }

        public static void TriggerRolloverManual()
        {
            instance?.BeginRollover("manual");
        }

        private void ExecuteRollover()
        {
            BeginRollover("scheduled");
        }

        private void BeginRollover(string trigger)
        {
            if (context == null || !Context.IsMainPlayer || preCloseSave != null || postCloseSave != null)
                return;
            if (rollover.Begin() != SafeRolloverAction.SaveAndBackup)
                return;

            DateTime localNow = ServerTime.LocalNow(context.Config.TimeZoneId);
            if (trigger == "scheduled" || RolloverTiming.IsBoundaryWindow(localNow.TimeOfDay, context.Config.ParsedCalendarDayChangeTime))
                lastRolloverBoundaryDate = localNow.Date;
            AdminApiModule.EventLog.Append(
                HostEventTypes.RolloverStarted,
                Game1.Date.TotalDays,
                Game1.timeOfDay,
                "Secuencia diaria iniciada: guardar y respaldar antes de cerrar peers.",
                new Dictionary<string, object?> { ["trigger"] = trigger, ["gameDay"] = Game1.Date.TotalDays });
            try
            {
                preCloseSave = SaveGame.Save();
                context.Monitor.Log("[DailyRolloverModule] Guardado nativo pre-cierre iniciado.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                preCloseSave = null;
                rollover.Fail();
                context.Monitor.Log($"[DailyRolloverModule] No se pudo iniciar el guardado pre-cierre; peers permanecen conectados y el día no avanza: {ex}", LogLevel.Error);
            }
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || !Context.IsMainPlayer)
                return;

            PumpPreCloseSave();
            PumpPostCloseSave();
        }

        private void PumpPreCloseSave()
        {
            if (preCloseSave == null)
                return;

            try
            {
                if (preCloseSave.MoveNext())
                    return;

                preCloseSave.Dispose();
                preCloseSave = null;
                bool backupCreated = CreateSaveBackup();
                SafeRolloverAction action = rollover.SaveAndBackupCompleted(backupCreated, Game1.otherFarmers.Count);
                if (!backupCreated)
                {
                    context.Monitor.Log("[DailyRolloverModule] Guardado nativo terminó, pero su backup no quedó verificado; no se desconectan peers ni se avanza el día.", LogLevel.Error);
                    return;
                }

                context.Monitor.Log($"[DailyRolloverModule] Guardado y backup verificados ({LastBackupFileName}). Peers conectados: {Game1.otherFarmers.Count}.", LogLevel.Info);
                if (action == SafeRolloverAction.RequestPeerClose)
                    RequestPeerClose();
                else if (action == SafeRolloverAction.SaveAndBackupAfterClose)
                    StartPostCloseSave();
                else if (action == SafeRolloverAction.AdvanceDay)
                    AdvanceNativeDay();
            }
            catch (Exception ex)
            {
                preCloseSave?.Dispose();
                preCloseSave = null;
                rollover.Fail();
                context.Monitor.Log($"[DailyRolloverModule] Guardado pre-cierre falló; no se desconectan peers ni se avanza el día: {ex}", LogLevel.Error);
            }
        }

        private void StartPostCloseSave()
        {
            if (context == null || !Context.IsMainPlayer || Game1.otherFarmers.Count != 0 || postCloseSave != null)
                return;

            try
            {
                postCloseSave = SaveGame.Save();
                context.Monitor.Log("[DailyRolloverModule] Peers cerrados; guardando su estado final antes de avanzar la fecha.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                postCloseSave = null;
                rollover.Fail();
                context.Monitor.Log($"[DailyRolloverModule] No se pudo iniciar el guardado posterior al cierre; la fecha no avanza: {ex}", LogLevel.Error);
            }
        }

        private void PumpPostCloseSave()
        {
            if (postCloseSave == null)
                return;

            try
            {
                if (postCloseSave.MoveNext())
                    return;

                postCloseSave.Dispose();
                postCloseSave = null;
                bool backupCreated = CreateSaveBackup();
                SafeRolloverAction action = rollover.SaveAndBackupAfterCloseCompleted(backupCreated);
                if (!backupCreated)
                {
                    context.Monitor.Log("[DailyRolloverModule] El guardado posterior al cierre terminó, pero su backup no se verificó; no se avanza la fecha.", LogLevel.Error);
                    return;
                }

                context.Monitor.Log($"[DailyRolloverModule] Estado final de granja y jugadores guardado y respaldado ({LastBackupFileName}).", LogLevel.Info);
                if (action == SafeRolloverAction.AdvanceDay)
                    AdvanceNativeDay();
            }
            catch (Exception ex)
            {
                postCloseSave?.Dispose();
                postCloseSave = null;
                rollover.Fail();
                context.Monitor.Log($"[DailyRolloverModule] Guardado posterior al cierre falló; la fecha no avanza: {ex}", LogLevel.Error);
            }
        }

        private void RequestPeerClose()
        {
            BroadcastHUDMessage("Guardado confirmado. El servidor cerrará esta conexión para cambiar la fecha.", HUDMessage.error_type);
            context.Helper.Multiplayer.SendMessage(
                message: "Guardado confirmado. Desconectando para el cambio de fecha.",
                messageType: RolloverCloseMessageType,
                modIDs: new[] { context.Manifest.UniqueID });
            peerCloseDeadlineUtc = DateTimeOffset.UtcNow + PeerGracePeriod;
            forcedPeerDisconnect = false;
            context.Monitor.Log($"[DailyRolloverModule] Solicitado cierre ordenado; quedan {Game1.otherFarmers.Count} peers.", LogLevel.Info);
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || e.FromModID != context.Manifest.UniqueID || e.Type != RolloverCloseMessageType)
                return;
            if (Context.IsMainPlayer)
                return;

            string message = e.ReadAs<string>();
            Game1.addHUDMessage(new HUDMessage(message, HUDMessage.error_type));
            DelayedAction.functionAfterDelay(() => Game1.ExitToTitle(), 1500);
        }

        private void ProcessPeerClose()
        {
            if (rollover.Stage != SafeRolloverStage.WaitingForPeers)
                return;

            int peers = Game1.otherFarmers.Count;
            SafeRolloverAction action = rollover.PeersChanged(peers);
            if (action == SafeRolloverAction.SaveAndBackupAfterClose)
                StartPostCloseSave();
            if (peers == 0 || DateTimeOffset.UtcNow < peerCloseDeadlineUtc)
                return;

            if (!forcedPeerDisconnect)
            {
                forcedPeerDisconnect = true;
                peerCloseDeadlineUtc = DateTimeOffset.UtcNow + ForcedDisconnectGracePeriod;
                try
                {
                    long[] remaining = Game1.otherFarmers.Keys.ToArray();
                    if (remaining.Length == 0)
                    {
                        ProcessPeerClose();
                        return;
                    }
                    foreach (long playerId in remaining)
                        Game1.server?.kick(playerId);
                    context.Monitor.Log($"[DailyRolloverModule] Solicitado cierre nativo de los {remaining.Length} peers que no salieron tras el margen ordenado.", LogLevel.Warn);
                }
                catch (Exception ex)
                {
                    rollover.Fail();
                    context.Monitor.Log($"[DailyRolloverModule] Error cerrando peers; el día no avanza: {ex}", LogLevel.Error);
                }
                return;
            }

            rollover.Fail();
            context.Monitor.Log($"[DailyRolloverModule] Cierre de peers no confirmado tras la desconexión nativa ({peers} siguen conectados); se cancela el cambio de día.", LogLevel.Error);
        }

        private void AdvanceNativeDay()
        {
            if (!rollover.MarkAdvancingDay())
                return;

            try
            {
                // Game1.NewDay es la transición nativa que ejecuta ReadyCheckDialog al completarse.
                // Todos los farmhands ya se han guardado y desconectado, así que no queda un peer bloqueando el check.
                Game1.NewDay(0f);
                string fade = EnsureNewDayFade();
                dayAdvanceStartedUtc = dayAdvanceLastAttemptUtc = DateTimeOffset.UtcNow;
                dayAdvanceNudges = 0;
                context.Monitor.Log($"[DailyRolloverModule] Cambio de día nativo iniciado tras guardar y cerrar todos los peers ({fade}).", LogLevel.Info);
            }
            catch (Exception ex)
            {
                rollover.Fail();
                context.Monitor.Log($"[DailyRolloverModule] Error al iniciar el cambio de día nativo: {ex}", LogLevel.Error);
            }
        }

        /// <summary>
        /// <c>Game1.NewDay</c> solo marca el cambio de día; el juego lo ejecuta al terminar un fundido a negro
        /// (<c>onFadeToBlackComplete</c>), y <c>NewDay</c> solo lanza ese fundido si el host está en la cama o
        /// desmayado. Con el host despierto (modo anfitrión agente) el día se quedaba esperando hasta la siguiente
        /// puerta que cruzase. Aquí se lanza el mismo fundido que el de la cama, sin desmayar al host.
        /// </summary>
        /// <returns>Descripción para el log de qué fundido lleva el cambio de día.</returns>
        private static string EnsureNewDayFade()
        {
            if (Game1.player.isInBed.Value || Game1.player.passedOut)
                return "host en la cama: fundido nativo";
            if (Game1.fadeToBlack || Game1.globalFade)
                return "fundido ya en curso";

            Game1.nonWarpFade = true;
            Game1.fadeScreenToBlack();
            Game1.player.Halt();
            return "host despierto: fundido lanzado por el mod";
        }

        /// <summary>
        /// Vigila que el cambio de día nativo arranque. Si el juego sigue esperando el fundido sin que haya ninguno
        /// en curso, lo vuelve a lanzar y lo registra como <c>rollover.stalled</c>.
        /// </summary>
        private void WatchNativeDayAdvance()
        {
            if (rollover.Stage != SafeRolloverStage.AdvancingDay)
                return;

            bool fadeInProgress = Game1.fadeToBlack || Game1.globalFade;
            if (!RolloverTiming.ShouldNudgeDayAdvance(Game1.newDay, fadeInProgress, DateTimeOffset.UtcNow - dayAdvanceLastAttemptUtc))
                return;

            dayAdvanceNudges++;
            dayAdvanceLastAttemptUtc = DateTimeOffset.UtcNow;
            int secondsSinceAdvance = (int)(DateTimeOffset.UtcNow - dayAdvanceStartedUtc).TotalSeconds;
            string fade = EnsureNewDayFade();
            context.Monitor.Log(
                $"[DailyRolloverModule] El cambio de día sigue esperando un fundido tras {secondsSinceAdvance} s; se vuelve a lanzar ({fade}, intento {dayAdvanceNudges}).",
                LogLevel.Error);
            AdminApiModule.EventLog.Append(
                HostEventTypes.RolloverStalled,
                Game1.Date.TotalDays,
                Game1.timeOfDay,
                $"El cambio de día no arrancaba; fundido relanzado (intento {dayAdvanceNudges}).",
                new Dictionary<string, object?>
                {
                    ["nudges"] = dayAdvanceNudges,
                    ["secondsSinceAdvance"] = secondsSinceAdvance,
                    ["hostInBed"] = Game1.player.isInBed.Value,
                    ["hostLocation"] = Game1.player.currentLocation?.NameOrUniqueName
                });
        }

        private static void BroadcastHUDMessage(string message, int type)
        {
            Game1.addHUDMessage(new HUDMessage(message, type));
            context?.Monitor.Log($"[HUD] {message}", LogLevel.Info);
        }

        private void FreezeFriendshipDecayForOfflinePlayers()
        {
            try
            {
                int today = Game1.Date.TotalDays;
                var onlineIds = Game1.getOnlineFarmers().Select(f => f.UniqueMultiplayerID).ToHashSet();

                foreach (var kvp in context.WorldState.Players)
                {
                    long uid = kvp.Key;
                    if (!onlineIds.Contains(uid))
                    {
                        Farmer? farmer = Game1.GetPlayer(uid);
                        if (farmer != null)
                        {
                            // En Stardew Valley, la amistad baja -2 puntos cada día que no hablas con un NPC.
                            // Para jugadores offline, sumamos +2 a todos sus vínculos para anular la caída diaria.
                            foreach (var friendship in farmer.friendshipData.Values)
                            {
                                if (friendship.Points > 0 && friendship.Points < 2500)
                                {
                                    friendship.Points = Math.Min(2500, friendship.Points + 2);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[DailyRolloverModule] Error al pausar amistad offline: {ex}", LogLevel.Warn);
            }
        }

        private void CheckAndPlanCatchUp()
        {
            var cfg = context.Config;
            var calendar = new ServerCalendar(cfg.ParsedCalendarEpoch, cfg.ParsedCalendarDayChangeTime);
            DateTime now = ServerTime.LocalNow(cfg.TimeZoneId);

            int savedDays = Game1.Date.TotalDays;
            CatchUpPlan plan = calendar.PlanCatchUp(savedDays, now, cfg.MaxCatchUpRollovers);

            if (plan.IsAhead)
            {
                string expectedDate = calendar.TryGetDate(now, out var date)
                    ? date.ToString()
                    : $"anterior a la época ({cfg.CalendarEpoch}; totalDays={calendar.GetTotalDays(now)})";
                context.Monitor.Log($"[DailyRolloverModule] AVISO: La partida guardada (día {savedDays}) va por delante de la fecha real esperada ({expectedDate}). Revisa CalendarEpoch o el reloj del sistema.", LogLevel.Warn);
            }
            else if (plan.RolloversToRun > 0)
            {
                context.Monitor.Log($"[DailyRolloverModule] Desfase detectado con el calendario real. Plan de recuperación: {plan.RolloversToRun} rollovers pendientes.", LogLevel.Info);
            }
        }

        private bool CreateSaveBackup()
        {
            string? partial = null;
            try
            {
                string? savesPath = Constants.CurrentSavePath;
                if (string.IsNullOrEmpty(savesPath) || !Directory.Exists(savesPath))
                    return false;

                string backupDir = Path.Combine(Constants.DataPath, "Pelican4Ever_Backups");
                Directory.CreateDirectory(backupDir);

                string backupFolderName = $"{Constants.SaveFolderName}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}";
                string dest = Path.Combine(backupDir, backupFolderName);
                partial = dest + ".partial";
                Directory.CreateDirectory(partial);

                string[] files = Directory.GetFiles(savesPath, "*", SearchOption.TopDirectoryOnly);
                if (files.Length == 0)
                    throw new IOException("La carpeta de save no contiene archivos que respaldar.");

                foreach (string file in files)
                {
                    string copy = Path.Combine(partial, Path.GetFileName(file));
                    File.Copy(file, copy, overwrite: false);
                    using FileStream sourceStream = File.OpenRead(file);
                    using FileStream backupStream = File.OpenRead(copy);
                    using SHA256 sha = SHA256.Create();
                    if (!CryptographicOperations.FixedTimeEquals(sha.ComputeHash(sourceStream), sha.ComputeHash(backupStream)))
                        throw new IOException($"La verificación SHA-256 falló para {Path.GetFileName(file)}.");
                }

                Directory.Move(partial, dest);
                LastBackupFileName = backupFolderName;
                LastBackupCreatedUtc = DateTimeOffset.UtcNow.ToString("O");

                AdminApiModule.EventLog.Append(
                    HostEventTypes.BackupCreated,
                    Game1.Date.TotalDays,
                    Game1.timeOfDay,
                    $"Copia de seguridad verificada: {backupFolderName}",
                    new Dictionary<string, object?>
                    {
                        ["backupName"] = backupFolderName,
                        ["createdAtUtc"] = LastBackupCreatedUtc,
                        ["fileCount"] = files.Length,
                        ["verifiedBy"] = "SHA-256"
                    });

                context.Monitor.Log($"[DailyRolloverModule] Backup pre-cierre verificado en: {dest}", LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                if (partial != null && Directory.Exists(partial))
                {
                    try { Directory.Delete(partial, recursive: true); }
                    catch (Exception cleanupError) { context.Monitor.Log($"[DailyRolloverModule] No se pudo retirar el backup parcial {partial}: {cleanupError.Message}", LogLevel.Warn); }
                }
                context.Monitor.Log($"[DailyRolloverModule] Error al crear backup de seguridad: {ex}", LogLevel.Warn);
                return false;
            }
        }

        /// <summary>
        /// Ajusta de forma segura los fondos de un granjero respetando carteras individuales en Stardew Valley 1.6.
        /// </summary>
        public static void SetFarmerMoney(Farmer farmer, int newMoney)
        {
            if (Game1.player?.team != null && Game1.player.team.useSeparateWallets.Value && farmer != Game1.player)
            {
                Game1.player.team.SetIndividualMoney(farmer, newMoney);
            }
            else
            {
                farmer.Money = newMoney;
            }
        }
    }
}
