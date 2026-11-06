using System;

namespace Pelican4Ever.Core.Admin
{
    /// <summary>Por qué no se hace un checkpoint (Épica 11, gap G8).</summary>
    public enum CheckpointSkipReason
    {
        /// <summary>Se hace el checkpoint.</summary>
        None,

        /// <summary><c>CheckpointIntervalMinutes</c> es 0 y no es una petición manual.</summary>
        Disabled,

        /// <summary>Aún no ha pasado el intervalo desde el último checkpoint.</summary>
        NotDue,

        /// <summary>No ha habido jugadores ni actividad desde el último checkpoint.</summary>
        NoActivity,

        /// <summary>Hay otro guardado en curso (nativo, rollover o spike).</summary>
        SaveInProgress,

        /// <summary>La secuencia del rollover diario no está en reposo.</summary>
        RolloverActive,

        /// <summary>Falta menos que el aviso del rollover: el propio rollover guardará.</summary>
        NearRollover,

        /// <summary>Festival o evento en curso: la ubicación es temporal y el juego nunca guarda ahí.</summary>
        FestivalOrEvent,

        /// <summary>Hay farmhands conectados y no se clona su estado: se guardarían retrasados (gap G4).</summary>
        FarmhandsWithoutClone,

        /// <summary>El guardado nativo eliminaría tiendas, Junimos o personajes temporales de WitchHut.</summary>
        DestructiveSaveCleanup
    }

    /// <summary>Datos de un instante para decidir si toca checkpoint.</summary>
    public sealed record CheckpointInputs
    {
        /// <summary>Petición manual (<c>p4e_checkpoint_now</c> o API): ignora intervalo, desactivación y actividad.</summary>
        public bool Manual { get; init; }

        /// <summary>Intervalo configurado en minutos; 0 desactiva los checkpoints automáticos.</summary>
        public int IntervalMinutes { get; init; }

        /// <summary>Checkpoints lentos seguidos en el día de juego actual; cada uno duplica el intervalo (gap G2).</summary>
        public int SlowStreak { get; init; }

        /// <summary>Tiempo desde el último checkpoint o, si no hubo ninguno hoy, desde el inicio del día.</summary>
        public TimeSpan SinceLastCheckpoint { get; init; }

        /// <summary>Tiempo real hasta el próximo cambio de fecha (02:00).</summary>
        public TimeSpan UntilRollover { get; init; }

        /// <summary>Minutos de aviso del rollover; dentro de esa ventana no se hacen checkpoints.</summary>
        public int RolloverWarningMinutes { get; init; } = 10;

        public bool SaveInProgress { get; init; }

        public bool RolloverActive { get; init; }

        public bool FestivalOrEvent { get; init; }

        public bool DestructiveSaveCleanup { get; init; }

        /// <summary>Hubo algún jugador conectado o actividad del host desde el último checkpoint.</summary>
        public bool ActivitySinceLast { get; init; }

        /// <summary>Farmhands conectados en este momento.</summary>
        public int OnlineFarmhands { get; init; }

        /// <summary>Se copia el estado vivo de los farmhands a <c>farmhandData</c> antes de guardar.</summary>
        public bool CloneFarmhands { get; init; }
    }

    /// <summary>Resultado de <see cref="CheckpointPolicy.Decide"/>.</summary>
    /// <param name="Run">Hay que hacer el checkpoint ahora.</param>
    /// <param name="Reason">Motivo de no hacerlo (<see cref="CheckpointSkipReason.None"/> si se hace).</param>
    /// <param name="Due">Tocaba por tiempo (o era manual): un salto con <c>Due</c> merece quedar registrado.</param>
    /// <param name="EffectiveInterval">Intervalo aplicado tras el retroceso por lentitud.</param>
    public sealed record CheckpointDecision(bool Run, CheckpointSkipReason Reason, bool Due, TimeSpan EffectiveInterval);

    /// <summary>Lógica pura de cuándo hacer un checkpoint intradía (TASK-1603, gaps G2 y G8).</summary>
    public static class CheckpointPolicy
    {
        /// <summary>Máximo de duplicaciones del intervalo por lentitud (8 veces el configurado).</summary>
        public const int MaxBackoffDoublings = 3;

        /// <summary>Intervalo tras aplicar el retroceso: se duplica por cada checkpoint lento del día, hasta 8 veces.</summary>
        public static TimeSpan EffectiveInterval(int intervalMinutes, int slowStreak)
        {
            if (intervalMinutes <= 0)
                return TimeSpan.Zero;
            int doublings = Math.Clamp(slowStreak, 0, MaxBackoffDoublings);
            return TimeSpan.FromMinutes(intervalMinutes * (1 << doublings));
        }

        /// <summary>Un checkpoint es lento si bloqueó el juego más de <paramref name="maxDurationMs"/> (0 = sin límite).</summary>
        public static bool IsSlow(double longestTickMs, int maxDurationMs) =>
            maxDurationMs > 0 && longestTickMs > maxDurationMs;

        public static CheckpointDecision Decide(CheckpointInputs inputs)
        {
            if (inputs == null)
                throw new ArgumentNullException(nameof(inputs));

            TimeSpan interval = EffectiveInterval(inputs.IntervalMinutes, inputs.SlowStreak);
            if (!inputs.Manual && inputs.IntervalMinutes <= 0)
                return Skip(CheckpointSkipReason.Disabled, due: false, interval);

            bool due = inputs.Manual || inputs.SinceLastCheckpoint >= interval;

            // Seguridad: aplica también a las peticiones manuales.
            if (inputs.SaveInProgress)
                return Skip(CheckpointSkipReason.SaveInProgress, due, interval);
            if (inputs.RolloverActive)
                return Skip(CheckpointSkipReason.RolloverActive, due, interval);
            if (inputs.UntilRollover <= TimeSpan.FromMinutes(Math.Max(0, inputs.RolloverWarningMinutes)))
                return Skip(CheckpointSkipReason.NearRollover, due, interval);
            if (inputs.FestivalOrEvent)
                return Skip(CheckpointSkipReason.FestivalOrEvent, due, interval);
            if (inputs.DestructiveSaveCleanup)
                return Skip(CheckpointSkipReason.DestructiveSaveCleanup, due, interval);
            if (inputs.OnlineFarmhands > 0 && !inputs.CloneFarmhands)
                return Skip(CheckpointSkipReason.FarmhandsWithoutClone, due, interval);

            if (!inputs.Manual)
            {
                if (!due)
                    return Skip(CheckpointSkipReason.NotDue, due: false, interval);
                if (!inputs.ActivitySinceLast)
                    return Skip(CheckpointSkipReason.NoActivity, due: false, interval);
            }

            return new CheckpointDecision(true, CheckpointSkipReason.None, true, interval);
        }

        private static CheckpointDecision Skip(CheckpointSkipReason reason, bool due, TimeSpan interval) =>
            new(false, reason, due, interval);
    }
}
