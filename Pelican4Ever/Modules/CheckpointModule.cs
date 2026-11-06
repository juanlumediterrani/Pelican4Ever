using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Netcode;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Time;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
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
    /// jugador vivo. Pendiente de verificar en partida con el spike de TASK-1601.
    /// </remarks>
    public sealed class CheckpointModule : IModule
    {
        private const int AnnounceSeconds = 3;
        private const string NoticeMessageType = "Pelican4Ever.CheckpointNotice";

        private ModContext context = null!;
        private IEnumerator<int>? save;
        private readonly Stopwatch total = new();
        private double longestTickMs;
        private bool runningManual;
        private DateTimeOffset? pendingStartUtc;
        private bool pendingManual;
        private DateTimeOffset lastCheckpointUtc = DateTimeOffset.UtcNow;
        private int slowStreak;
        private bool activitySinceLast;
        private CheckpointSkipReason lastLoggedSkip = CheckpointSkipReason.None;

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
            ctx.Helper.ConsoleCommands.Add(
                "p4e_checkpoint_now",
                "Guarda la partida ahora sin cambiar de día y deja una copia en Pelican4Ever_Backups/checkpoints/ (Épica 11).",
                (_, _) => RequestManual());
            ctx.Helper.Events.GameLoop.SaveLoaded += (_, _) => ResetDay();
            ctx.Helper.Events.GameLoop.DayStarted += (_, _) => ResetDay();
            ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
            ctx.Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
        }

        /// <summary>En los farmhands: muestra el aviso de que el host va a guardar (el juego se congelará un momento).</summary>
        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (Context.IsMainPlayer || e.FromModID != context.Manifest.UniqueID || e.Type != NoticeMessageType)
                return;
            Game1.addHUDMessage(new HUDMessage(e.ReadAs<string>(), HUDMessage.newQuest_type));
        }

        private void ResetDay()
        {
            lastCheckpointUtc = DateTimeOffset.UtcNow;
            slowStreak = 0;
            activitySinceLast = false;
            lastLoggedSkip = CheckpointSkipReason.None;
        }

        private void RequestManual()
        {
            if (!Context.IsWorldReady || !Context.IsMainPlayer)
            {
                context.Monitor.Log("[Checkpoint] Hace falta una partida cargada y ser el host.", LogLevel.Warn);
                return;
            }
            if (save != null || pendingStartUtc != null)
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

            if (pendingStartUtc != null)
            {
                if (DateTimeOffset.UtcNow >= pendingStartUtc.Value)
                    Start();
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
            Game1.addHUDMessage(new HUDMessage("Guardando el mundo en unos segundos…", HUDMessage.newQuest_type));
            context.Helper.Multiplayer.SendMessage(
                "Guardando el mundo en unos segundos…",
                NoticeMessageType,
                modIDs: new[] { context.Manifest.UniqueID });
        }

        private void Start()
        {
            bool manual = pendingManual;
            pendingStartUtc = null;

            // La situación puede haber cambiado durante el aviso.
            CheckpointDecision decision = Decide(manual);
            if (!decision.Run)
            {
                LogSkip(decision, manual);
                return;
            }

            try
            {
                int cloned = 0;
                if (Game1.otherFarmers.Count > 0)
                    cloned = CloneLiveFarmhandsIntoSaveData();

                PersistenceModule.FlushToSaveData(context, "checkpoint");
                runningManual = manual;
                longestTickMs = 0;
                total.Restart();
                save = SaveGame.Save();
                IsSaving = true;
                context.Monitor.Log($"[Checkpoint] Guardado iniciado ({(manual ? "manual" : "programado")}; farmhands clonados: {cloned}).", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Finish(ex);
            }
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (save == null)
                return;

            var step = Stopwatch.StartNew();
            bool more;
            try
            {
                more = save.MoveNext();
            }
            catch (Exception ex)
            {
                Finish(ex);
                return;
            }

            longestTickMs = Math.Max(longestTickMs, step.Elapsed.TotalMilliseconds);
            if (!more)
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
                context.Monitor.Log($"[Checkpoint] El guardado falló; la partida en disco sigue siendo la anterior: {failure}", LogLevel.Error);
                AppendEvent(HostEventTypes.CheckpointFailed, "Checkpoint fallido.", new Dictionary<string, object?>
                {
                    ["manual"] = runningManual,
                    ["error"] = failure.Message
                });
                return;
            }

            activitySinceLast = false;
            bool slow = CheckpointPolicy.IsSlow(longestTickMs, context.Config.CheckpointMaxDurationMs);
            if (slow)
                slowStreak++;

            SaveBackupResult? backup = SaveBackupService.Create(context.Monitor, BackupKind.Checkpoint, context.Config.CheckpointCompress);
            if (backup != null)
            {
                LastCheckpoint = backup;
                LastCheckpointLongestTickMs = longestTickMs;
                SaveBackupService.ApplyRetention(context.Monitor, BackupKind.Checkpoint, context.Config.CheckpointRetention, keepDailyDays: 0);
            }

            TimeSpan next = CheckpointPolicy.EffectiveInterval(context.Config.CheckpointIntervalMinutes, slowStreak);
            context.Monitor.Log(
                $"[Checkpoint] Partida guardada en {total.Elapsed.TotalMilliseconds:F0} ms (tick más largo {longestTickMs:F0} ms){(slow ? ", LENTO" : string.Empty)}. " +
                $"Copia: {backup?.FolderName ?? "NO VERIFICADA"}. Próximo en {(next == TimeSpan.Zero ? "—" : $"{next.TotalMinutes:F0} min")}.",
                backup == null || slow ? LogLevel.Warn : LogLevel.Info);
            AppendEvent(HostEventTypes.CheckpointCreated, $"Checkpoint {(backup != null ? "guardado y copiado" : "guardado sin copia verificada")}.", new Dictionary<string, object?>
            {
                ["manual"] = runningManual,
                ["backupName"] = backup?.FolderName,
                ["bytes"] = backup?.Bytes,
                ["totalMs"] = Math.Round(total.Elapsed.TotalMilliseconds),
                ["longestTickMs"] = Math.Round(longestTickMs),
                ["slow"] = slow,
                ["nextIntervalMinutes"] = next.TotalMinutes
            });
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

        private int CloneLiveFarmhandsIntoSaveData()
        {
            int cloned = 0;
            foreach (KeyValuePair<long, NetRoot<Farmer>> pair in Game1.otherFarmers.Roots.ToList())
            {
                if (pair.Value is not NetFarmerRoot root)
                    continue;
                if (!Game1.netWorldState.Value.farmhandData.FieldDict.TryGetValue(pair.Key, out NetRef<Farmer>? stored))
                {
                    context.Monitor.Log($"[Checkpoint] {root.Value?.Name} ({pair.Key}) no tiene entrada en farmhandData; se guardará como estaba.", LogLevel.Warn);
                    continue;
                }
                root.CloneInto(stored);
                cloned++;
            }
            return cloned;
        }

        private void LogSkip(CheckpointDecision decision, bool manual)
        {
            context.Monitor.Log($"[Checkpoint] {(manual ? "Manual" : "Programado")} no se hace: {Describe(decision.Reason)}.", manual ? LogLevel.Warn : LogLevel.Info);
            AppendEvent(HostEventTypes.CheckpointSkipped, $"Checkpoint omitido: {Describe(decision.Reason)}.", new Dictionary<string, object?>
            {
                ["manual"] = manual,
                ["reason"] = decision.Reason.ToString()
            });
        }

        private static void AppendEvent(string type, string summary, Dictionary<string, object?> data) =>
            AdminApiModule.EventLog.Append(type, Game1.Date.TotalDays, Game1.timeOfDay, summary, data);

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
