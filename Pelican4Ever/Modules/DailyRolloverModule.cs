using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.Time;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Locations;
using StardewValley.Quests;

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

        /// <summary>Hay un guardado del rollover en curso o la secuencia diaria no está en reposo.</summary>
        public static bool IsBusy =>
            instance != null
            && (instance.preCloseSave != null
                || instance.postCloseSave != null
                || instance.rollover.Stage is not (SafeRolloverStage.Idle or SafeRolloverStage.Failed));

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
                ctx.AddCommand("p4e_rollover_now", "Fuerza la secuencia segura de guardado y rollover.", OnForceRollover);

                ctx.Harmony.Patch(
                    original: AccessTools.Method(typeof(Game1), nameof(Game1.queueWeddingsForToday)),
                    postfix: new HarmonyMethod(typeof(DailyRolloverModule), nameof(Postfix_Game1_QueueWeddingsForToday))
                );
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
            lastProcessedOfflineFarmhandDay = Game1.Date.TotalDays;
            CheckAndPlanCatchUp();
            SanitizeStrandedSpouses();
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
                SanitizeStrandedSpouses();
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
                DailyEconomyTelemetry.Record(context, record, endedDay);
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

            // Reparar NPCs varados en cabañas privadas y persistir novedades nocturnas offline
            SanitizeStrandedSpouses();
            context.Helper.Data.WriteSaveData(ServerWorldState.SaveDataKey, context.WorldState);

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
                BroadcastHUDMessage(I18n.Get("rollover.warning_10min"), HUDMessage.newQuest_type);
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
            // Otro guardado en curso (p. ej. el spike de checkpoint); el disparo programado lo reintenta al segundo siguiente.
            if (SaveGame.IsProcessing || CheckpointModule.IsSaving || CheckpointSpikeModule.IsSaving)
            {
                context.Monitor.Log("[DailyRolloverModule] Hay otro guardado en curso; el rollover espera a que termine.", LogLevel.Info);
                return;
            }
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
                PersistenceModule.FlushToSaveData(context, "rollover pre-cierre");
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
                DeliverPendingFarmhandMail();
                PersistenceModule.FlushToSaveData(context, "rollover post-cierre");
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

        /// <summary>
        /// Entrega el correo «para mañana» de los farmhands que ya están desconectados. El juego nativo
        /// solo procesa <c>mailForTomorrow</c> de <c>Game1.player</c> en cada <c>_newDayAfterFade</c>; como
        /// este rollover cierra todos los peers antes de avanzar la fecha, sus cartas y banderas (por
        /// ejemplo <c>copperFound</c>, que desbloquea el evento del Horno) se quedaban atascadas para siempre.
        /// </summary>
        private void DeliverPendingFarmhandMail()
        {
            int deliveredFlags = 0;
            int deliveredLetters = 0;
            int affectedFarmhands = 0;

            foreach (Farmer farmhand in Game1.getOfflineFarmhands())
            {
                if (farmhand == null)
                    continue;
                bool knownPlayer = context.WorldState.Players.ContainsKey(farmhand.UniqueMultiplayerID);
                if (!FarmhandMailDeliveryLogic.ShouldDeliverToFarmhand(farmhand.isUnclaimedFarmhand, knownPlayer, farmhand.mailForTomorrow.Count))
                    continue;

                string[] rawEntries = farmhand.mailForTomorrow.ToArray();
                var planned = FarmhandMailDeliveryLogic.Plan(rawEntries);
                if (planned.Count == 0)
                    continue;

                bool changed = false;
                foreach (PendingFarmhandMail mail in planned)
                {
                    if (mail.DeliverToMailbox)
                    {
                        if (!farmhand.mailbox.Contains(mail.Id) && !farmhand.mailReceived.Contains(mail.Id))
                        {
                            farmhand.mailbox.Add(mail.Id);
                            deliveredLetters++;
                            changed = true;
                        }
                    }
                    else if (farmhand.mailReceived.Add(mail.Id))
                    {
                        deliveredFlags++;
                        changed = true;
                    }
                }

                foreach (string rawEntry in rawEntries)
                    farmhand.mailForTomorrow.Remove(rawEntry);

                if (changed)
                {
                    affectedFarmhands++;
                    context.Monitor.Log(
                        $"[DailyRolloverModule] Farmhand {farmhand.Name} ({farmhand.UniqueMultiplayerID}): correo pendiente entregado ({string.Join(", ", planned.Select(mail => mail.Id))}).",
                        LogLevel.Trace);
                }
            }

            if (affectedFarmhands > 0)
            {
                context.Monitor.Log(
                    $"[DailyRolloverModule] Correo pendiente entregado a {affectedFarmhands} farmhand(s) desconectado(s): {deliveredFlags} bandera(s) y {deliveredLetters} carta(s).",
                    LogLevel.Info);
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
            BroadcastHUDMessage(I18n.Get("rollover.closing_server"), HUDMessage.error_type);
            context.Helper.Multiplayer.SendMessage(
                message: "rollover.disconnecting",
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
            Game1.addHUDMessage(new HUDMessage(I18n.Get(message), HUDMessage.error_type));
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
                // En vanilla Game1._newDayAfterFade() solo limpia chestConsumedMineLevels (> 120) en el jugador local;
                // como este rollover cierra los peers antes de NewDay(0f), los farmhands necesitan esa limpieza aquí.
                ResetOfflineFarmhandSkullCavernChests();

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
        /// Limpia en los farmhands desconectados los cofres diarios de la Caverna Calavera (&gt; 120) antes de
        /// <c>Game1.NewDay</c>, igual que hace el motor nativo solo en <c>Game1.player</c>.
        /// </summary>
        private static void ResetOfflineFarmhandSkullCavernChests()
        {
            int clearedFarmhands = 0;
            int clearedLevels = 0;

            foreach (Farmer farmhand in Game1.getAllFarmhands())
            {
                if (farmhand == null || farmhand.IsMainPlayer)
                    continue;

                int before = farmhand.chestConsumedMineLevels.Count();
                if (before == 0)
                    continue;

                farmhand.chestConsumedMineLevels.RemoveWhere(pair => MineProgressLogic.ShouldResetMineChestOnNewDay(pair.Key));
                int removed = before - farmhand.chestConsumedMineLevels.Count();
                if (removed > 0)
                {
                    clearedFarmhands++;
                    clearedLevels += removed;
                }
            }

            if (clearedLevels > 0)
            {
                context?.Monitor.Log(
                    $"[DailyRolloverModule] Caverna Calavera: limpiados {clearedLevels} cofre(s) diario(s) (>120) en {clearedFarmhands} farmhand(s).",
                    LogLevel.Info);
            }
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

        private static int lastProcessedOfflineFarmhandDay = -1;

        public static void Postfix_Game1_QueueWeddingsForToday()
        {
            instance?.ProcessOfflineFarmhandsOvernight();
        }

        /// <summary>
        /// Ejecuta en el servidor las rutinas diarias de los farmhands desconectados cuando cambia el día en
        /// <see cref="Game1.NewDay"/>: reinicio diario de conversación/regalos, caída diaria exclusiva del
        /// cónyuge (-20 si no se habló), mejoras de herramientas/casa, misiones temporales, divorcios,
        /// memoria de diálogos y bodas autoritativas offline (Opción 4A).
        /// </summary>
        private void ProcessOfflineFarmhandsOvernight()
        {
            try
            {
                if (!Context.IsMainPlayer || Game1.Date.TotalDays == lastProcessedOfflineFarmhandDay)
                    return;

                lastProcessedOfflineFarmhandDay = Game1.Date.TotalDays;
                var onlineIds = Game1.getOnlineFarmers().Select(f => f.UniqueMultiplayerID).ToHashSet();
                bool canHaveWedding = Game1.canHaveWeddingOnDay(Game1.dayOfMonth, Game1.season);
                bool worldStateDirty = false;

                foreach (Farmer farmer in Game1.getAllFarmers())
                {
                    if (farmer == null || farmer.IsMainPlayer || farmer.isUnclaimedFarmhand || onlineIds.Contains(farmer.UniqueMultiplayerID))
                        continue;

                    long uid = farmer.UniqueMultiplayerID;
                    context.WorldState.Players.TryGetValue(uid, out PlayerRecord? record);

                    // 1. Amistades diarias: solo el cónyuge/compañero pierde -20 si no se habló hoy; el resto se congela sin sumar +2.
                    foreach (string npcName in farmer.friendshipData.Keys.ToList())
                    {
                        Friendship friendship = farmer.friendshipData[npcName];
                        bool isSpouseOrRoommate = !string.IsNullOrEmpty(farmer.spouse)
                            && string.Equals(npcName, farmer.spouse, StringComparison.Ordinal)
                            && (friendship.IsMarried() || friendship.IsRoommate());

                        int delta = HouseholdRolloverLogic.EvaluateDailySpouseFriendshipDelta(isSpouseOrRoommate, friendship.TalkedToToday);
                        if (delta != 0)
                        {
                            friendship.Points = Math.Max(0, friendship.Points + delta);
                        }

                        friendship.TalkedToToday = false;
                    }
                    farmer.updateFriendshipGifts(Game1.Date);

                    // 2. Banderas diarias, herramientas y ampliación de casa (equivalente seguro de Farmer.dayupdate)
                    farmer.hasUsedDailyRevive.Value = false;
                    farmer.hasBeenBlessedByStatueToday = false;
                    farmer.acceptedDailyQuest.Set(newValue: false);
                    farmer.dancePartner.Value = null;
                    farmer.festivalScore = 0;
                    farmer.forceTimePass = false;
                    farmer.timeWentToBed.Value = 0;
                    farmer.stats.Set("blessingOfWaters", 0);

                    if (farmer.daysLeftForToolUpgrade.Value > 0)
                    {
                        farmer.daysLeftForToolUpgrade.Value--;
                    }

                    DoorAssignment? door = context.WorldState.FindResidenceOf(uid);
                    Cabin? home = ResolveFarmerCabin(farmer, door);

                    if (farmer.daysUntilHouseUpgrade.Value > 0)
                    {
                        farmer.daysUntilHouseUpgrade.Value--;
                        if (farmer.daysUntilHouseUpgrade.Value <= 0)
                        {
                            int nextLevel = farmer.HouseUpgradeLevel + 1;
                            home?.moveObjectsForHouseUpgrade(nextLevel);
                            farmer.HouseUpgradeLevel = nextLevel;
                            farmer.daysUntilHouseUpgrade.Value = -1;
                            home?.setMapForUpgradeLevel(nextLevel);
                            if (nextLevel >= 3)
                            {
                                farmer.craftingRecipes.TryAdd("Cask", 0);
                                if (home != null && door != null && !string.IsNullOrEmpty(door.FarmSlotId))
                                {
                                    FarmSlotModule.EnsureSlotCellar(door.FarmSlotId, home, door);
                                }
                            }
                            farmer.autoGenerateActiveDialogueEvent("houseUpgrade_" + nextLevel);
                        }
                    }

                    farmer.questLog.RemoveWhere(quest =>
                    {
                        if (quest.IsTimedQuest())
                        {
                            quest.daysLeft.Value--;
                            return quest.daysLeft.Value <= 0 && !quest.completed.Value;
                        }
                        return false;
                    });

                    AdvanceOfflineActiveDialogueEvents(farmer, home);

                    // 3. Divorcio nocturno pendiente
                    if (farmer.divorceTonight.Value)
                    {
                        NPC? exSpouse = farmer.getSpouse();
                        farmer.doDivorce();
                        if (exSpouse != null && exSpouse.getSpouse() == null)
                        {
                            exSpouse.PerformDivorce();
                        }
                        if (record != null && !string.IsNullOrEmpty(record.PendingWeddingSpouse))
                        {
                            record.PendingWeddingSpouse = null;
                            worldStateDirty = true;
                        }
                    }

                    // 4. Boda offline autoritativa (Opción 4A)
                    if (!string.IsNullOrEmpty(farmer.spouse)
                        && farmer.friendshipData.TryGetValue(farmer.spouse, out Friendship? spouseFriendship)
                        && HouseholdRolloverLogic.ShouldCompleteOfflineWedding(
                            farmer.isEngaged(),
                            !string.IsNullOrEmpty(farmer.spouse),
                            spouseFriendship.CountdownToWedding,
                            canHaveWedding))
                    {
                        string spouseName = farmer.spouse;
                        if (door != null && door.OwnerId == uid && !string.IsNullOrEmpty(door.FarmSlotId))
                        {
                            string expectedHome = $"{door.FarmSlotId}{ResidenceUpgradeLogic.CabinHomeSuffix}";
                            if (!string.Equals(farmer.homeLocation.Value, expectedHome, StringComparison.OrdinalIgnoreCase))
                            {
                                farmer.homeLocation.Value = expectedHome;
                                home = ResolveFarmerCabin(farmer, door) ?? home;
                            }
                        }

                        spouseFriendship.Status = FriendshipStatus.Married;
                        spouseFriendship.WeddingDate = new WorldDate(Game1.Date);

                        NPC? wedSpouse = Game1.getCharacterFromName(spouseName);
                        if (wedSpouse != null && home != null)
                        {
                            Game1.prepareSpouseForWedding(farmer);
                            home.showSpouseRoom();
                        }

                        if (wedSpouse != null && !wedSpouse.isRoommate())
                        {
                            farmer.autoGenerateActiveDialogueEvent("married_" + spouseName);
                            if (!farmer.autoGenerateActiveDialogueEvent("married"))
                                farmer.autoGenerateActiveDialogueEvent("married_twice");

                            if (record == null)
                            {
                                record = new PlayerRecord
                                {
                                    UniqueMultiplayerID = uid,
                                    PlayerName = farmer.Name
                                };
                                context.WorldState.Players[uid] = record;
                            }
                            record.PendingWeddingSpouse = spouseName;
                            worldStateDirty = true;
                            context.Monitor.Log($"[DailyRolloverModule] Boda offline completada para {farmer.Name} con {spouseName}; ceremonia diferida al reconectar.", LogLevel.Info);
                        }
                        else if (wedSpouse != null)
                        {
                            farmer.autoGenerateActiveDialogueEvent("roommates_" + spouseName);
                            context.Monitor.Log($"[DailyRolloverModule] Acuerdo de compañero offline completado para {farmer.Name} con {spouseName}.", LogLevel.Info);
                        }
                    }
                }

                if (worldStateDirty)
                {
                    context.Helper.Data.WriteSaveData(ServerWorldState.SaveDataKey, context.WorldState);
                }
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[DailyRolloverModule] Error al procesar rollover offline de farmhands: {ex}", LogLevel.Warn);
            }
        }

        private Cabin? ResolveFarmerCabin(Farmer farmer, DoorAssignment? door)
        {
            if (door != null && door.OwnerId == farmer.UniqueMultiplayerID && !string.IsNullOrEmpty(door.FarmSlotId))
            {
                if (FarmSlotModule.ResolveResidenceCabin($"{door.FarmSlotId}{ResidenceUpgradeLogic.CabinHomeSuffix}", door) is Cabin slotCabin)
                    return slotCabin;
            }

            if (!string.IsNullOrEmpty(farmer.homeLocation.Value))
            {
                return Game1.getLocationFromName(farmer.homeLocation.Value) as Cabin;
            }

            return null;
        }

        private static void AdvanceOfflineActiveDialogueEvents(Farmer farmer, Cabin? home)
        {
            foreach (string key in farmer.activeDialogueEvents.Keys.ToList())
            {
                if (!key.Contains("_memory_", StringComparison.Ordinal))
                {
                    if (!farmer.previousActiveDialogueEvents.ContainsKey(key))
                        farmer.previousActiveDialogueEvents[key] = 0;
                }

                int remaining = farmer.activeDialogueEvents[key] - 1;
                if (remaining < 0)
                {
                    if (key == "pennyRedecorating" && home?.GetSpouseBed() != null)
                    {
                        farmer.activeDialogueEvents[key] = 0;
                    }
                    else
                    {
                        farmer.activeDialogueEvents.Remove(key);
                    }
                }
                else
                {
                    farmer.activeDialogueEvents[key] = remaining;
                }
            }

            foreach (string key in farmer.previousActiveDialogueEvents.Keys.ToList())
            {
                int days = farmer.previousActiveDialogueEvents[key] + 1;
                farmer.previousActiveDialogueEvents[key] = days;
                switch (days)
                {
                    case 1: farmer.activeDialogueEvents[key + "_memory_oneday"] = 4; break;
                    case 7: farmer.activeDialogueEvents[key + "_memory_oneweek"] = 4; break;
                    case 14: farmer.activeDialogueEvents[key + "_memory_twoweeks"] = 4; break;
                    case 28: farmer.activeDialogueEvents[key + "_memory_fourweeks"] = 4; break;
                    case 56: farmer.activeDialogueEvents[key + "_memory_eightweeks"] = 4; break;
                    case 104: farmer.activeDialogueEvents[key + "_memory_oneyear"] = 4; break;
                }
            }
        }

        /// <summary>
        /// Repara cónyuges cuyo mapa por defecto o ubicación actual apunta a una cabaña privada (<c>_Home</c>)
        /// tras desaparecer el dueño o perder el matrimonio sin divorcio limpio.
        /// </summary>
        public static void SanitizeStrandedSpouses()
        {
            if (!Context.IsMainPlayer)
                return;

            try
            {
                Utility.ForEachVillager(npc =>
                {
                    if (npc == null)
                        return true;

                    Farmer? liveSpouse = npc.getSpouse();
                    bool isResidenceCabinMap = !string.IsNullOrEmpty(ResidenceUpgradeLogic.ExtractSlotIdFromCabinName(npc.DefaultMap));
                    bool isDefaultMissing = isResidenceCabinMap && Game1.getLocationFromName(npc.DefaultMap) == null;

                    if (HouseholdRolloverLogic.IsStrandedResidenceNpc(isResidenceCabinMap, liveSpouse != null, isDefaultMissing))
                    {
                        context?.Monitor.Log($"[DailyRolloverModule] Reparando NPC varado '{npc.Name}' (DefaultMap={npc.DefaultMap}).", LogLevel.Warn);
                        npc.PerformDivorce();
                    }
                    else if (liveSpouse == null && !string.IsNullOrEmpty(ResidenceUpgradeLogic.ExtractSlotIdFromCabinName(npc.currentLocation?.NameOrUniqueName)))
                    {
                        context?.Monitor.Log($"[DailyRolloverModule] Devolviendo a su hogar a '{npc.Name}' varado en '{npc.currentLocation?.NameOrUniqueName}'.", LogLevel.Warn);
                        npc.PerformDivorce();
                    }

                    return true;
                });
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[DailyRolloverModule] Error al sanear cónyuges varados: {ex}", LogLevel.Warn);
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
            SaveBackupResult? backup = SaveBackupService.Create(context.Monitor, BackupKind.Daily, compress: false);
            if (backup == null)
                return false;

            LastBackupFileName = backup.FolderName;
            LastBackupCreatedUtc = backup.CreatedUtc.ToString("O");

            AdminApiModule.EventLog.Append(
                HostEventTypes.BackupCreated,
                Game1.Date.TotalDays,
                Game1.timeOfDay,
                $"Copia de seguridad verificada: {backup.FolderName}",
                new Dictionary<string, object?>
                {
                    ["backupName"] = backup.FolderName,
                    ["createdAtUtc"] = LastBackupCreatedUtc,
                    ["fileCount"] = backup.FileCount,
                    ["verifiedBy"] = "SHA-256"
                });

            context.Monitor.Log($"[DailyRolloverModule] Backup verificado en: {backup.FolderPath}", LogLevel.Info);
            SaveBackupService.ApplyRetention(context.Monitor, BackupKind.Daily, keepCheckpoints: 0, keepDailyDays: context.Config.BackupRetentionDays);
            return true;
        }

        /// <summary>
        /// Ajusta de forma segura los fondos de un granjero respetando carteras individuales en Stardew Valley 1.6.
        /// </summary>
        public static void SetFarmerMoney(Farmer farmer, int newMoney)
        {
            if (Game1.player?.team != null && farmer != Game1.player)
            {
                // SetIndividualMoney delegates to GetMoney(farmer), which selects either that
                // farmer's wallet or the shared team wallet. Farmer.Money rejects writes to a
                // non-local farmer even when wallets are shared.
                Game1.player.team.SetIndividualMoney(farmer, newMoney);
            }
            else
            {
                farmer.Money = newMoney;
            }
        }
    }
}
