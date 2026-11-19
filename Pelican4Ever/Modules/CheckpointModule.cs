using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Netcode;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Time;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;
using StardewValley.TerrainFeatures;
using StardewValley.Network;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Checkpoints intradía (TASK-1604, Épica 11): guarda la partida con el pipeline nativo cada
    /// <c>CheckpointIntervalMinutes</c> sin cambiar de día ni desconectar a nadie, y deja una copia verificada en
    /// <c>Pelican4Ever_Backups/checkpoints/</c>. Desactivado por defecto; <c>p4e_checkpoint_now</c> lo lanza a mano.
    /// </summary>
    /// <remarks>
    /// Cuándo se hace lo decide <see cref="CheckpointPolicy"/>. Con farmhands conectados, antes de guardar se copia su
    /// estado vivo a <c>farmhandData</c> con <c>NetRoot.CloneInto</c> (gap G4): el juego guarda esa copia, que solo
    /// refresca al desconectar o dormir, y su <c>saveFarmhands()</c> no sirve a mitad de día porque reinicia al
    /// jugador vivo. Validación y límites en docs/spike-checkpoints.md (TASK-1601).
    /// </remarks>
    public sealed class CheckpointModule : IModule
    {
        private const int AnnounceSeconds = 3;
        private const string NoticeMessageType = "Pelican4Ever.CheckpointNotice";
        private const string RestoredNoticeMessageType = "Pelican4Ever.CheckpointRestoredNotice";

        private static CheckpointModule? instance;
        private ModContext context = null!;
        private IEnumerator<int>? save;
        private readonly Stopwatch total = new();
        private double longestTickMs;
        private bool runningManual;
        private DateTimeOffset? pendingStartUtc;
        private DateTimeOffset? skillSyncDeadlineUtc;
        private bool pendingManual;
        private DateTimeOffset lastCheckpointUtc = DateTimeOffset.UtcNow;
        private int slowStreak;
        private bool activitySinceLast;
        private CheckpointSkipReason lastLoggedSkip = CheckpointSkipReason.None;
        private string? restoredCheckpointTimeFormatted;
        private Action? pendingPostCheckpointCallback;
        private long? lastCheckpointEventId;
        private bool lastCheckpointSucceeded;
        private string lastCheckpointStatusMessage = string.Empty;
        private double lastCloneFarmhandsMs;
        private double lastSaveSerializeMs;
        private int lastClonedFarmhandsCount;
        private long lastEstimatedCloneBytes;

        public string Name => "Checkpoint";

        public ModuleSide Side => ModuleSide.Host;

        /// <summary>Hay un checkpoint guardándose; el rollover y el spike esperan.</summary>
        public static bool IsSaving { get; private set; }

        /// <summary>Último checkpoint terminado (para la API y el estado del servidor).</summary>
        public static SaveBackupResult? LastCheckpoint { get; private set; }

        /// <summary>Duración del tick más largo del último checkpoint, en milisegundos.</summary>
        public static double LastCheckpointLongestTickMs { get; private set; }

        public void Register(ModContext ctx)
        {
            context = ctx;
            instance = this;
            ctx.AddCommand(
                "p4e_checkpoint_now",
                "Guarda la partida ahora sin cambiar de día y deja una copia en Pelican4Ever_Backups/checkpoints/ (Épica 11).",
                (_, _) => RequestManual());
            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.GameLoop.DayStarted += (_, _) => ResetDay();
            ctx.Helper.Events.GameLoop.ReturnedToTitle += (_, _) =>
            {
                skillSyncDeadlineUtc = null;
                restoredCheckpointTimeFormatted = null;
                pendingPostCheckpointCallback = null;
                SkillExperienceModule.EndCheckpoint();
            };
            ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            ctx.Helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
        }

        /// <summary>En los farmhands: muestra avisos de checkpoint inminente o restauración de mitad de día.</summary>
        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (Context.IsMainPlayer || e.FromModID != context.Manifest.UniqueID)
                return;

            if (e.Type == NoticeMessageType)
            {
                Game1.addHUDMessage(new HUDMessage(I18n.Get(e.ReadAs<string>()), HUDMessage.newQuest_type));
            }
            else if (e.Type == RestoredNoticeMessageType)
            {
                string hhmm = e.ReadAs<string>();
                Game1.addHUDMessage(new HUDMessage(I18n.Get("checkpoint.restored_notice", new { time = hhmm }), HUDMessage.newQuest_type));
            }
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            ResetDay();
            restoredCheckpointTimeFormatted = null;

            if (!Context.IsMainPlayer || Game1.Date == null)
                return;

            CheckpointMetadata? restored = CheckpointRestoreLogic.FindMidDayRestoreCheckpoint(context.WorldState, Game1.Date.TotalDays);
            if (restored != null)
            {
                string hhmm = CheckpointRestoreLogic.FormatGameTime(restored.GameTime);
                restoredCheckpointTimeFormatted = hhmm;
                string summary = CheckpointRestoreLogic.BuildRestoreLogSummary(restored);
                context.Monitor.Log($"[Checkpoint] {summary}", LogLevel.Info);
                AppendEvent(HostEventTypes.CheckpointRestored, summary, new Dictionary<string, object?>
                {
                    ["gameDay"] = restored.GameDay,
                    ["checkpointGameTime"] = restored.GameTime,
                    ["checkpointTimeFormatted"] = hhmm,
                    ["savedAtUtc"] = restored.SavedAtUtc,
                    ["backupName"] = restored.BackupName
                });
                RealTimeClockModule.CatchUpVillagerSchedules(Game1.timeOfDay);
            }
        }

        private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
        {
            if (!Context.IsMainPlayer || string.IsNullOrEmpty(restoredCheckpointTimeFormatted))
                return;

            string hhmm = restoredCheckpointTimeFormatted;
            context.Helper.Multiplayer.SendMessage(
                hhmm,
                RestoredNoticeMessageType,
                new[] { context.Manifest.UniqueID },
                new[] { e.Peer.PlayerID });
        }

        private void ResetDay()
        {
            skillSyncDeadlineUtc = null;
            SkillExperienceModule.EndCheckpoint();
            lastCheckpointUtc = DateTimeOffset.UtcNow;
            slowStreak = 0;
            activitySinceLast = false;
            lastLoggedSkip = CheckpointSkipReason.None;
            if (Game1.Date != null && context.WorldState.LastCheckpoint != null && context.WorldState.LastCheckpoint.GameDay != Game1.Date.TotalDays)
                restoredCheckpointTimeFormatted = null;
        }

        /// <summary>Lanza un checkpoint desde la API HTTP en el hilo principal (<c>POST /api/v1/host/checkpoint</c>).</summary>
        public static (bool Ok, string Message, long? EventId) RequestFromHostApi()
        {
            if (instance == null || !Context.IsWorldReady || !Context.IsMainPlayer)
                return (false, "Hace falta una partida cargada y ser el host.", null);

            if (instance.save != null || instance.pendingStartUtc != null || instance.skillSyncDeadlineUtc != null)
                return (false, "Ya hay un checkpoint en marcha.", null);

            CheckpointDecision decision = instance.Decide(manual: true);
            if (!decision.Run)
            {
                long eventId = instance.LogSkipAndReturnEventId(decision, manual: true);
                return (false, $"Checkpoint no ejecutado: {Describe(decision.Reason)}.", eventId);
            }

            if (Game1.otherFarmers.Count == 0)
            {
                instance.pendingManual = true;
                instance.Start(bypassPendingStart: true);
                return (instance.lastCheckpointSucceeded, instance.lastCheckpointStatusMessage, instance.lastCheckpointEventId);
            }

            instance.Schedule(manual: true);
            return (true, "Checkpoint iniciado (aviso a farmhands y sincronización de XP en curso).", null);
        }

        /// <summary>
        /// Intenta ejecutar un checkpoint antes de un reinicio inmediato (<c>--immediate</c>, TASK-1608).
        /// Siempre invoca <paramref name="onFinished"/> cuando concluye, o incluso si el checkpoint se omite o falla.
        /// </summary>
        public static void RunBeforeImmediateRestart(Action onFinished)
        {
            if (onFinished == null)
                return;

            if (instance == null || !Context.IsWorldReady || !Context.IsMainPlayer)
            {
                onFinished();
                return;
            }

            if (instance.save != null || instance.pendingStartUtc != null || instance.skillSyncDeadlineUtc != null)
            {
                instance.pendingPostCheckpointCallback = CombineCallbacks(instance.pendingPostCheckpointCallback, onFinished);
                return;
            }

            CheckpointDecision decision = instance.Decide(manual: true);
            if (!decision.Run)
            {
                instance.LogSkip(decision, manual: true);
                onFinished();
                return;
            }

            instance.pendingManual = true;
            instance.pendingPostCheckpointCallback = onFinished;
            if (Game1.otherFarmers.Count == 0)
            {
                instance.Start(bypassPendingStart: true);
            }
            else
            {
                SkillExperienceModule.BeginCheckpoint(Game1.otherFarmers.Keys);
                instance.skillSyncDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(10);
            }
        }

        private static Action CombineCallbacks(Action? existing, Action added) =>
            existing == null ? added : () => { existing(); added(); };

        private void RequestManual()
        {
            if (!Context.IsWorldReady || !Context.IsMainPlayer)
            {
                context.Monitor.Log("[Checkpoint] Hace falta una partida cargada y ser el host.", LogLevel.Warn);
                return;
            }
            if (save != null || pendingStartUtc != null || skillSyncDeadlineUtc != null)
            {
                context.Monitor.Log("[Checkpoint] Ya hay un checkpoint en marcha.", LogLevel.Warn);
                return;
            }

            CheckpointDecision decision = Decide(manual: true);
            if (!decision.Run)
            {
                context.Monitor.Log($"[Checkpoint] No se lanza: {Describe(decision.Reason)}.", LogLevel.Warn);
                return;
            }
            Schedule(manual: true);
        }

        private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || !Context.IsMainPlayer)
                return;

            if (Game1.otherFarmers.Count > 0 || context.Config.AgentHost)
                activitySinceLast = true;

            if (save != null)
                return;

            if (skillSyncDeadlineUtc != null)
            {
                if (SkillExperienceModule.CheckpointReady(Game1.otherFarmers.Keys))
                {
                    skillSyncDeadlineUtc = null;
                    Start();
                }
                else if (DateTimeOffset.UtcNow >= skillSyncDeadlineUtc.Value)
                {
                    skillSyncDeadlineUtc = null;
                    SkillExperienceModule.EndCheckpoint();
                    lastCheckpointUtc = DateTimeOffset.UtcNow;
                    context.Monitor.Log("[Checkpoint] Cancelado: no se pudo sincronizar la experiencia de todos los farmhands en 10 segundos.", LogLevel.Warn);
                    FlushPendingCallback();
                }
                return;
            }

            if (pendingStartUtc != null)
            {
                if (DateTimeOffset.UtcNow >= pendingStartUtc.Value)
                {
                    pendingStartUtc = null;
                    SkillExperienceModule.BeginCheckpoint(Game1.otherFarmers.Keys);
                    skillSyncDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(10);
                }
                return;
            }

            CheckpointDecision decision = Decide(manual: false);
            if (decision.Run)
            {
                Schedule(manual: false);
                return;
            }

            // Solo se registra un salto cuando tocaba por tiempo, y una vez por motivo, para no llenar el log cada segundo.
            if (decision.Due && decision.Reason != lastLoggedSkip)
            {
                lastLoggedSkip = decision.Reason;
                LogSkip(decision, manual: false);
            }
        }

        private void Schedule(bool manual)
        {
            pendingManual = manual;
            pendingStartUtc = DateTimeOffset.UtcNow.AddSeconds(AnnounceSeconds);
            Game1.addHUDMessage(new HUDMessage(I18n.Get("checkpoint.saving_soon"), HUDMessage.newQuest_type));
            context.Helper.Multiplayer.SendMessage(
                "checkpoint.saving_soon",
                NoticeMessageType,
                modIDs: new[] { context.Manifest.UniqueID });
        }

        private void Start(bool bypassPendingStart = false)
        {
            bool manual = pendingManual;
            if (!bypassPendingStart)
                pendingStartUtc = null;

            // La situación puede haber cambiado durante el aviso.
            CheckpointDecision decision = Decide(manual);
            if (!decision.Run)
            {
                lastCheckpointEventId = LogSkipAndReturnEventId(decision, manual);
                lastCheckpointSucceeded = false;
                lastCheckpointStatusMessage = $"Checkpoint omitido: {Describe(decision.Reason)}.";
                SkillExperienceModule.EndCheckpoint();
                FlushPendingCallback();
                return;
            }

            runningManual = manual;
            longestTickMs = 0;
            lastCloneFarmhandsMs = 0;
            lastSaveSerializeMs = 0;
            lastClonedFarmhandsCount = 0;
            lastEstimatedCloneBytes = 0;
            total.Restart();
            IsSaving = true;
            CheckpointMetadata? previousCheckpoint = context.WorldState.LastCheckpoint;
            try
            {
                CommunityMarketModule.CompleteMarketDeposits();
                CommunityMarketModule.CompleteObservedDeliveries();
                int cloned = 0;
                long estimatedCloneBytes = 0;
                if (Game1.otherFarmers.Count > 0)
                {
                    Stopwatch cloneSw = Stopwatch.StartNew();
                    cloned = CloneLiveFarmhandsIntoSaveData(out estimatedCloneBytes);
                    cloneSw.Stop();
                    lastCloneFarmhandsMs = cloneSw.Elapsed.TotalMilliseconds;
                }
                lastClonedFarmhandsCount = cloned;
                lastEstimatedCloneBytes = estimatedCloneBytes;

                context.WorldState.LastCheckpoint = new CheckpointMetadata
                {
                    GameDay = Game1.Date.TotalDays,
                    GameTime = Game1.timeOfDay,
                    SavedAtUtc = DateTimeOffset.UtcNow.ToString("O")
                };

                if (!PersistenceModule.FlushToSaveData(context, "checkpoint"))
                    throw new IOException("No se pudo volcar ServerWorldState; se cancela el checkpoint para no guardar un estado incoherente.");
                Stopwatch saveSw = Stopwatch.StartNew();
                save = SaveGame.Save();
                context.Monitor.Log($"[Checkpoint] Guardado iniciado ({(manual ? "manual" : "programado")}; farmhands clonados: {cloned}).", LogLevel.Info);
                // SMAPI ejecuta la tarea nativa de Save sincrónicamente. Agotar el enumerador aquí evita
                // ticks de red o del mundo entre el clon de los farmhands y la serialización del mundo.
                while (save.MoveNext()) { }
                saveSw.Stop();
                lastSaveSerializeMs = saveSw.Elapsed.TotalMilliseconds;
                if (Game1.gameMode != Game1.playingGameMode)
                    throw new IOException($"El juego no pudo escribir el checkpoint: {Game1.debugOutput}");
            }
            catch (Exception ex)
            {
                context.WorldState.LastCheckpoint = previousCheckpoint;
                longestTickMs = total.Elapsed.TotalMilliseconds;
                Finish(ex);
                return;
            }
            finally
            {
                SkillExperienceModule.EndCheckpoint();
            }

            longestTickMs = total.Elapsed.TotalMilliseconds;
            Finish(null);
        }

        private void Finish(Exception? failure)
        {
            save?.Dispose();
            save = null;
            IsSaving = false;
            total.Stop();
            lastCheckpointUtc = DateTimeOffset.UtcNow;
            lastLoggedSkip = CheckpointSkipReason.None;

            if (failure != null)
            {
                context.Monitor.Log($"[Checkpoint] El guardado falló; comprueba la partida en disco y las copias verificadas antes de restaurar: {failure}", LogLevel.Error);
                lastCheckpointSucceeded = false;
                lastCheckpointStatusMessage = $"Checkpoint fallido: {failure.Message}";
                lastCheckpointEventId = AppendEvent(HostEventTypes.CheckpointFailed, "Checkpoint fallido.", new Dictionary<string, object?>
                {
                    ["manual"] = runningManual,
                    ["error"] = failure.Message
                });
                FlushPendingCallback();
                return;
            }

            activitySinceLast = false;
            bool slow = CheckpointPolicy.IsSlow(longestTickMs, context.Config.CheckpointMaxDurationMs);
            if (slow)
                slowStreak++;

            SaveBackupResult? backup = SaveBackupService.Create(
                context.Monitor,
                BackupKind.Checkpoint,
                context.Config.CheckpointCompress,
                totalGameDays: Game1.Date.TotalDays,
                gameDate: LoadInstrumentationLogic.FormatGameDate(Game1.currentSeason, Game1.dayOfMonth, Game1.year),
                modVersion: context.Manifest.Version.ToString(),
                gameVersion: Game1.version);
            if (backup != null)
            {
                LastCheckpoint = backup;
                LastCheckpointLongestTickMs = longestTickMs;
                SaveBackupService.ApplyRetention(context.Monitor, BackupKind.Checkpoint, context.Config.CheckpointRetention, keepDailyDays: 0);
            }

            SaveLoadMetricsSnapshot metrics = LoadMetricsTracker.CaptureAndRecord(
                "checkpoint",
                lastCloneFarmhandsMs,
                lastSaveSerializeMs,
                total.Elapsed.TotalMilliseconds,
                longestTickMs,
                lastClonedFarmhandsCount,
                lastEstimatedCloneBytes);

            if (context.WorldState.LastCheckpoint != null)
            {
                context.WorldState.LastCheckpoint.BackupName = backup?.FolderName ?? string.Empty;
                context.WorldState.LastCheckpoint.TotalMs = Math.Round(total.Elapsed.TotalMilliseconds);
                context.WorldState.LastCheckpoint.LongestTickMs = Math.Round(longestTickMs);
            }

            TimeSpan next = CheckpointPolicy.EffectiveInterval(context.Config.CheckpointIntervalMinutes, slowStreak);
            string summaryMsg = $"Checkpoint {(backup != null ? "guardado y copiado" : "guardado sin copia verificada")} ({total.Elapsed.TotalMilliseconds:F0} ms).";
            context.Monitor.Log(
                $"[Checkpoint] Partida guardada en {total.Elapsed.TotalMilliseconds:F0} ms (tick más largo {longestTickMs:F0} ms, clone={metrics.CloneFarmhandsMs:F1} ms, save={metrics.SaveSerializeMs:F1} ms, xml={metrics.XmlTotalBytes} B, NITV={metrics.TotalNitvCreatures}){(slow ? ", LENTO" : string.Empty)}. " +
                $"Copia: {backup?.FolderName ?? "NO VERIFICADA"}. Próximo en {(next == TimeSpan.Zero ? "—" : $"{next.TotalMinutes:F0} min")}.",
                backup == null || slow ? LogLevel.Warn : LogLevel.Info);

            lastCheckpointSucceeded = true;
            lastCheckpointStatusMessage = summaryMsg;
            lastCheckpointEventId = AppendEvent(HostEventTypes.CheckpointCreated, $"Checkpoint {(backup != null ? "guardado y copiado" : "guardado sin copia verificada")}.", new Dictionary<string, object?>
            {
                ["manual"] = runningManual,
                ["backupName"] = backup?.FolderName,
                ["bytes"] = backup?.Bytes,
                ["totalMs"] = Math.Round(total.Elapsed.TotalMilliseconds),
                ["longestTickMs"] = Math.Round(longestTickMs),
                ["slow"] = slow,
                ["nextIntervalMinutes"] = next.TotalMinutes,
                ["xmlTotalBytes"] = metrics.XmlTotalBytes,
                ["cloneFarmhandsMs"] = metrics.CloneFarmhandsMs,
                ["saveSerializeMs"] = metrics.SaveSerializeMs,
                ["clonedFarmhandsCount"] = metrics.ClonedFarmhandsCount,
                ["estimatedFarmhandCloneBytes"] = metrics.EstimatedFarmhandCloneBytes,
                ["totalNitvCreatures"] = metrics.TotalNitvCreatures
            });

            if (pendingPostCheckpointCallback != null)
            {
                FlushPendingCallback();
                return;
            }

            AdminApiModule.NotifySaveCompleted(isCheckpoint: true);
        }

        private void FlushPendingCallback()
        {
            Action? cb = pendingPostCheckpointCallback;
            pendingPostCheckpointCallback = null;
            cb?.Invoke();
        }

        private CheckpointDecision Decide(bool manual)
        {
            ServerConfigView cfg = new(context);
            return CheckpointPolicy.Decide(new CheckpointInputs
            {
                Manual = manual,
                IntervalMinutes = cfg.IntervalMinutes,
                SlowStreak = slowStreak,
                SinceLastCheckpoint = DateTimeOffset.UtcNow - lastCheckpointUtc,
                UntilRollover = RolloverTiming.UntilNextBoundary(
                    ServerTime.LocalNow(context.Config.TimeZoneId).TimeOfDay,
                    context.Config.ParsedCalendarDayChangeTime),
                RolloverWarningMinutes = cfg.RolloverWarningMinutes,
                SaveInProgress = save != null || SaveGame.IsProcessing || CheckpointSpikeModule.IsSaving,
                RolloverActive = DailyRolloverModule.IsBusy,
                FestivalOrEvent = IsFestivalOrEvent(),
                DestructiveSaveCleanup = HasDestructiveSaveCleanup(),
                ActivitySinceLast = activitySinceLast,
                OnlineFarmhands = Game1.otherFarmers.Count,
                CloneFarmhands = context.Config.CheckpointCloneFarmhands
            });
        }

        /// <summary>Festival, evento del host o algún jugador en una ubicación temporal (festivales, cinemáticas).</summary>
        private static bool IsFestivalOrEvent() =>
            Game1.isFestival()
            || Game1.eventUp
            || Game1.CurrentEvent != null
            || Game1.getOnlineFarmers().Any(f => f.currentLocation == null || f.currentLocation.IsTemporary);

        /// <summary>cleanupBeforeSave elimina estos elementos también en los interiores de edificios.</summary>
        private static bool HasDestructiveSaveCleanup()
        {
            bool destructive = false;
            Utility.ForEachLocation(location =>
            {
                destructive = location.largeTerrainFeatures.Any(f => f is Tent)
                    || location.characters.Any(npc => npc is Junimo)
                    || (location.Name == "WitchHut" && location.characters.Count > 0);
                return !destructive;
            });
            return destructive;
        }

        private int CloneLiveFarmhandsIntoSaveData(out long estimatedCloneBytes)
        {
            var roots = Game1.otherFarmers.Roots.ToList();
            foreach (var pair in roots)
            {
                Farmer? farmer = pair.Value.TargetValue;
                BedLogoutModule.PreserveRestStamina(farmer);
                if (farmer == null || !float.IsFinite(farmer.Stamina)
                    || farmer.experiencePoints.Any(xp => xp < 0)
                    || farmer.Items.Any(item => item != null && item.Stack <= 0))
                    throw new InvalidOperationException($"El granjero {pair.Key} contiene datos inválidos; se conserva el último checkpoint.");
            }

            int cloned = 0;
            estimatedCloneBytes = 0L;
            foreach (KeyValuePair<long, NetRoot<Farmer>> pair in roots)
            {
                if (pair.Value is not NetFarmerRoot root)
                    throw new InvalidOperationException($"No se puede clonar el farmhand {pair.Key}; se cancela el checkpoint.");
                if (!Game1.netWorldState.Value.farmhandData.FieldDict.TryGetValue(pair.Key, out NetRef<Farmer>? stored))
                {
                    throw new InvalidOperationException($"{root.Value?.Name} ({pair.Key}) no tiene entrada en farmhandData; se cancela el checkpoint.");
                }
                root.CloneInto(stored);
                Farmer? value = root.Value;
                estimatedCloneBytes += LoadInstrumentationLogic.EstimateFarmhandBytes(
                    value?.Items.Count ?? 0,
                    value?.mailReceived.Count ?? 0,
                    value?.questLog.Count ?? 0);
                cloned++;
            }
            return cloned;
        }

        private void LogSkip(CheckpointDecision decision, bool manual) =>
            LogSkipAndReturnEventId(decision, manual);

        private long LogSkipAndReturnEventId(CheckpointDecision decision, bool manual)
        {
            context.Monitor.Log($"[Checkpoint] {(manual ? "Manual" : "Programado")} no se hace: {Describe(decision.Reason)}.", manual ? LogLevel.Warn : LogLevel.Info);
            return AppendEvent(HostEventTypes.CheckpointSkipped, $"Checkpoint omitido: {Describe(decision.Reason)}.", new Dictionary<string, object?>
            {
                ["manual"] = manual,
                ["reason"] = decision.Reason.ToString()
            });
        }

        private static long AppendEvent(string type, string summary, Dictionary<string, object?> data) =>
            AdminApiModule.EventLog.Append(type, Game1.Date.TotalDays, Game1.timeOfDay, summary, data).Id;

        private static string Describe(CheckpointSkipReason reason) => reason switch
        {
            CheckpointSkipReason.Disabled => "checkpoints desactivados (CheckpointIntervalMinutes = 0)",
            CheckpointSkipReason.NotDue => "aún no toca",
            CheckpointSkipReason.NoActivity => "sin actividad desde el último",
            CheckpointSkipReason.SaveInProgress => "hay otro guardado en curso",
            CheckpointSkipReason.RolloverActive => "el rollover diario está en marcha",
            CheckpointSkipReason.NearRollover => "queda poco para el rollover, que ya guardará",
            CheckpointSkipReason.FestivalOrEvent => "hay un festival, un evento o un jugador en una ubicación temporal",
            CheckpointSkipReason.FarmhandsWithoutClone => "hay farmhands conectados y CheckpointCloneFarmhands está desactivado",
            CheckpointSkipReason.DestructiveSaveCleanup => "el guardado eliminaría tiendas de campaña, Junimos o personajes de WitchHut",
            _ => reason.ToString()
        };

        /// <summary>Lectura de la configuración que usa la política.</summary>
        private readonly struct ServerConfigView
        {
            public ServerConfigView(ModContext context)
            {
                IntervalMinutes = context.Config.CheckpointIntervalMinutes;
                int[] warnings = context.Config.RolloverWarningMinutes ?? Array.Empty<int>();
                RolloverWarningMinutes = warnings.Length == 0 ? 10 : warnings.Max();
            }

            public int IntervalMinutes { get; }

            public int RolloverWarningMinutes { get; }
        }
    }
}
