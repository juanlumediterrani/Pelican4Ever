using System;
using Pelican4Ever.Core.Localization;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.Time;
using Pelican4Ever.Core.World;

namespace Pelican4Ever.Core.Admin
{
    /// <summary>
    /// Resultado de aplicar el descanso del anfitrión en función del modo <c>AgentHost</c> (TASK-804).
    /// </summary>
    public sealed record HostRestResult(
        bool RestRecorded,
        int GameDay,
        bool ShouldDisconnectSession,
        string Message);

    /// <summary>
    /// Evaluación del progreso en riesgo si el servidor se reinicia antes del próximo guardado/rollover (TASK-804 / TASK-1608).
    /// </summary>
    public sealed class ProgressAtRiskDto
    {
        public int CurrentGameDay { get; set; }
        public int CurrentGameTime { get; set; }
        public int GameMinutesSinceMorning { get; set; }
        public int OnlinePlayersCount { get; set; }
        public int UnrestedActivePlayersCount { get; set; }
        public bool WouldLoseUnsavedDayProgress { get; set; }
        public string? LastCheckpointAtUtc { get; set; }
        public int? LastCheckpointGameTime { get; set; }
        public string Summary { get; set; } = string.Empty;
    }

    /// <summary>
    /// Estado actual del coordinador de reinicio controlado (TASK-804 / TASK-1608).
    /// </summary>
    public sealed class SafeRestartStatusDto
    {
        public bool IsPending { get; set; }
        public string Mode { get; set; } = "none";
        public string Reason { get; set; } = string.Empty;
        public bool Immediate { get; set; }
        public bool WaitingForNextSave { get; set; }
        public bool RestartAfterCheckpoint { get; set; }
        public int WarningMinutes { get; set; }
        public string RequestedAtUtc { get; set; } = string.Empty;
        public string? ExecuteNotBeforeUtc { get; set; }
        public string WarningAnnouncement { get; set; } = string.Empty;
        public ProgressAtRiskDto ProgressAtRisk { get; set; } = new();
        /// <summary>Id del suceso <c>host.restart.scheduled</c>/<c>host.restart.cancelled</c> que acredita la petición; null si no se registró.</summary>
        public long? EventId { get; set; }
    }

    /// <summary>
    /// Lógica pura de dominio para el descanso sin desconexión del anfitrión agente y el reinicio controlado (TASK-804).
    /// </summary>
    public static class AgentHostLogic
    {
        /// <summary>
        /// Registra el descanso en el <see cref="PlayerRecord"/> y determina si debe cerrarse la sesión (<c>ExitToTitle</c>).
        /// Con <paramref name="agentHostEnabled"/> = true y <paramref name="isHostPlayer"/> = true, el host registra
        /// <c>RestedOnGameDay</c> y <c>LastActiveGameDay</c> y permanece conectado.
        /// </summary>
        public static HostRestResult ApplyRest(
            PlayerRecord record,
            int currentGameDay,
            bool isHostPlayer,
            bool agentHostEnabled)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            record.RestedOnGameDay = currentGameDay;
            record.LastActiveGameDay = currentGameDay;

            if (isHostPlayer && agentHostEnabled)
            {
                return new HostRestResult(
                    RestRecorded: true,
                    GameDay: currentGameDay,
                    ShouldDisconnectSession: false,
                    Message: CoreI18n.Get("bed_logout.host_agent_active"));
            }

            return new HostRestResult(
                RestRecorded: true,
                GameDay: currentGameDay,
                ShouldDisconnectSession: true,
                Message: CoreI18n.Get("bed_logout.disconnecting"));
        }

        /// <summary>
        /// Calcula qué progreso diario se perdería si se reinicia antes del próximo guardado (rollover o último checkpoint).
        /// </summary>
        public static ProgressAtRiskDto EvaluateProgressAtRisk(
            int currentGameDay,
            int currentGameTime,
            int onlinePlayersCount,
            int unrestedActivePlayersCount,
            CheckpointMetadata? lastCheckpoint = null)
        {
            int clampedTime = Math.Max(GameClock.DayStartTimeOfDay, currentGameTime);
            bool hasTodayCheckpoint = lastCheckpoint != null && lastCheckpoint.GameDay == currentGameDay;
            int referenceTime = hasTodayCheckpoint
                ? Math.Max(GameClock.DayStartTimeOfDay, lastCheckpoint!.GameTime)
                : GameClock.DayStartTimeOfDay;
            int minutesSinceAnchor = Math.Max(0, GameClock.ToMinutes(clampedTime) - GameClock.ToMinutes(referenceTime));
            int hours = minutesSinceAnchor / 60;
            int mins = minutesSinceAnchor % 60;
            bool hasUnsavedProgress = minutesSinceAnchor > 0 || onlinePlayersCount > 0 || unrestedActivePlayersCount > 0;

            string summary;
            if (hasTodayCheckpoint && lastCheckpoint != null)
            {
                string cpTime = CheckpointRestoreLogic.FormatGameTime(lastCheckpoint.GameTime);
                summary = $"Progreso no guardado del día {currentGameDay} ({hours}h {mins:00}m desde el último checkpoint de las {cpTime}, " +
                          $"{onlinePlayersCount} jugador(es) conectado(s), {unrestedActivePlayersCount} sin descansar hoy).";
            }
            else if (lastCheckpoint != null && lastCheckpoint.GameDay > 0)
            {
                string cpTime = CheckpointRestoreLogic.FormatGameTime(lastCheckpoint.GameTime);
                summary = $"Progreso no guardado del día {currentGameDay} ({hours}h {mins:00}m desde las 06:00, " +
                          $"{onlinePlayersCount} jugador(es) conectado(s), {unrestedActivePlayersCount} sin descansar hoy). " +
                          $"Último checkpoint: día {lastCheckpoint.GameDay} a las {cpTime}.";
            }
            else
            {
                summary = $"Progreso no guardado del día {currentGameDay} ({hours}h {mins:00}m desde las 06:00, " +
                          $"{onlinePlayersCount} jugador(es) conectado(s), {unrestedActivePlayersCount} sin descansar hoy). " +
                          "Stardew Valley solo guarda al cambiar de día (Rollover).";
            }

            return new ProgressAtRiskDto
            {
                CurrentGameDay = currentGameDay,
                CurrentGameTime = currentGameTime,
                GameMinutesSinceMorning = minutesSinceAnchor,
                OnlinePlayersCount = onlinePlayersCount,
                UnrestedActivePlayersCount = unrestedActivePlayersCount,
                WouldLoseUnsavedDayProgress = hasUnsavedProgress,
                LastCheckpointAtUtc = lastCheckpoint != null && !string.IsNullOrWhiteSpace(lastCheckpoint.SavedAtUtc) ? lastCheckpoint.SavedAtUtc : null,
                LastCheckpointGameTime = lastCheckpoint != null && lastCheckpoint.GameDay > 0 ? lastCheckpoint.GameTime : null,
                Summary = summary
            };
        }
    }

    /// <summary>
    /// Coordinador de estado para el reinicio controlado del servidor (<c>p4e_safe_restart</c> y <c>POST /api/v1/host/restart</c>) (TASK-804).
    /// </summary>
    public sealed class SafeRestartCoordinator
    {
        private readonly object syncRoot = new();
        private SafeRestartStatusDto current = new();
        private DateTimeOffset? executeNotBefore;

        /// <summary>Obtiene una instantánea del estado actual del reinicio controlado.</summary>
        public SafeRestartStatusDto GetStatus()
        {
            lock (syncRoot)
            {
                return CloneStatus(current);
            }
        }

        /// <summary>
        /// Programa un reinicio controlado.
        /// Por defecto (<paramref name="immediate"/> = false y <paramref name="restartAfterCheckpoint"/> = false)
        /// espera hasta justo después del siguiente guardado (Rollover).
        /// Con <paramref name="restartAfterCheckpoint"/> = true espera al siguiente checkpoint intradía (o rollover si ocurre antes).
        /// Si <paramref name="immediate"/> = true, tras los minutos de preaviso lanza un checkpoint y reinicia al terminarlo.
        /// </summary>
        public SafeRestartStatusDto Schedule(
            string? reason,
            bool immediate,
            int warningMinutes,
            int currentGameDay,
            int currentGameTime,
            int onlinePlayersCount,
            int unrestedActivePlayersCount,
            DateTimeOffset? nowUtc = null,
            bool restartAfterCheckpoint = false,
            CheckpointMetadata? lastCheckpoint = null)
        {
            DateTimeOffset now = nowUtc ?? DateTimeOffset.UtcNow;
            int clampedWarning = Math.Max(0, warningMinutes);
            string cleanReason = string.IsNullOrWhiteSpace(reason) ? "Mantenimiento programado del servidor" : reason.Trim();

            ProgressAtRiskDto risk = AgentHostLogic.EvaluateProgressAtRisk(
                currentGameDay,
                currentGameTime,
                onlinePlayersCount,
                unrestedActivePlayersCount,
                lastCheckpoint);

            DateTimeOffset? notBefore = immediate
                ? now.AddMinutes(clampedWarning)
                : null;

            bool waitAfterCheckpoint = !immediate && restartAfterCheckpoint;
            string mode = immediate
                ? "immediate"
                : waitAfterCheckpoint
                    ? "after_next_checkpoint"
                    : "after_next_save";

            string timingText = immediate
                ? (clampedWarning > 0
                    ? $"en {clampedWarning} minuto(s) tras intentar un checkpoint (reinicio inmediato)"
                    : "ahora mismo tras intentar un checkpoint (reinicio inmediato)")
                : waitAfterCheckpoint
                    ? $"justo después del próximo checkpoint (preaviso de {clampedWarning} min)"
                    : $"justo después del próximo guardado/Rollover (preaviso de {clampedWarning} min)";

            string announcement = $"[Reinicio Controlado] El servidor se reiniciará {timingText}. Motivo: {cleanReason}. {risk.Summary}";

            lock (syncRoot)
            {
                executeNotBefore = notBefore;
                current = new SafeRestartStatusDto
                {
                    IsPending = true,
                    Mode = mode,
                    Reason = cleanReason,
                    Immediate = immediate,
                    WaitingForNextSave = !immediate,
                    RestartAfterCheckpoint = waitAfterCheckpoint,
                    WarningMinutes = clampedWarning,
                    RequestedAtUtc = now.ToUniversalTime().ToString("O"),
                    ExecuteNotBeforeUtc = notBefore?.ToUniversalTime().ToString("O"),
                    WarningAnnouncement = announcement,
                    ProgressAtRisk = risk
                };

                return CloneStatus(current);
            }
        }

        /// <summary>Cancela cualquier reinicio controlado pendiente.</summary>
        public SafeRestartStatusDto Cancel()
        {
            lock (syncRoot)
            {
                executeNotBefore = null;
                current = new SafeRestartStatusDto
                {
                    IsPending = false,
                    Mode = "none",
                    Reason = string.Empty,
                    Immediate = false,
                    WaitingForNextSave = false,
                    RestartAfterCheckpoint = false,
                    WarningMinutes = 0,
                    RequestedAtUtc = string.Empty,
                    ExecuteNotBeforeUtc = null,
                    WarningAnnouncement = "[Reinicio Controlado] El reinicio programado ha sido cancelado.",
                    ProgressAtRisk = new ProgressAtRiskDto()
                };

                return CloneStatus(current);
            }
        }

        /// <summary>
        /// Notifica que un guardado (<paramref name="isCheckpoint"/> = false para rollover, true para checkpoint intradía)
        /// acaba de completarse. Devuelve true y consume el estado cuando el modo programado corresponde a ese guardado.
        /// </summary>
        public bool ConsumeOnSaveCompleted(out SafeRestartStatusDto? triggeredStatus, bool isCheckpoint = false)
        {
            lock (syncRoot)
            {
                if (current.IsPending && current.WaitingForNextSave && (!isCheckpoint || current.RestartAfterCheckpoint))
                {
                    triggeredStatus = CloneStatus(current);
                    current.IsPending = false;
                    current.WaitingForNextSave = false;
                    current.RestartAfterCheckpoint = false;
                    executeNotBefore = null;
                    return true;
                }

                triggeredStatus = null;
                return false;
            }
        }

        /// <summary>
        /// Comprueba si un reinicio inmediato con cuenta atrás ya ha alcanzado su instante de ejecución.
        /// </summary>
        public bool ConsumeIfImmediateTimerElapsed(DateTimeOffset nowUtc, out SafeRestartStatusDto? triggeredStatus)
        {
            lock (syncRoot)
            {
                if (current.IsPending && current.Immediate && executeNotBefore.HasValue && nowUtc >= executeNotBefore.Value)
                {
                    triggeredStatus = CloneStatus(current);
                    current.IsPending = false;
                    executeNotBefore = null;
                    return true;
                }

                triggeredStatus = null;
                return false;
            }
        }

        private static SafeRestartStatusDto CloneStatus(SafeRestartStatusDto src)
        {
            return new SafeRestartStatusDto
            {
                IsPending = src.IsPending,
                Mode = src.Mode,
                Reason = src.Reason,
                Immediate = src.Immediate,
                WaitingForNextSave = src.WaitingForNextSave,
                RestartAfterCheckpoint = src.RestartAfterCheckpoint,
                WarningMinutes = src.WarningMinutes,
                RequestedAtUtc = src.RequestedAtUtc,
                ExecuteNotBeforeUtc = src.ExecuteNotBeforeUtc,
                WarningAnnouncement = src.WarningAnnouncement,
                ProgressAtRisk = new ProgressAtRiskDto
                {
                    CurrentGameDay = src.ProgressAtRisk.CurrentGameDay,
                    CurrentGameTime = src.ProgressAtRisk.CurrentGameTime,
                    GameMinutesSinceMorning = src.ProgressAtRisk.GameMinutesSinceMorning,
                    OnlinePlayersCount = src.ProgressAtRisk.OnlinePlayersCount,
                    UnrestedActivePlayersCount = src.ProgressAtRisk.UnrestedActivePlayersCount,
                    WouldLoseUnsavedDayProgress = src.ProgressAtRisk.WouldLoseUnsavedDayProgress,
                    LastCheckpointAtUtc = src.ProgressAtRisk.LastCheckpointAtUtc,
                    LastCheckpointGameTime = src.ProgressAtRisk.LastCheckpointGameTime,
                    Summary = src.ProgressAtRisk.Summary
                }
            };
        }
    }
}
