using System;

namespace Pelican4Ever.Core.Time
{
    /// <summary>Lectura del reloj de juego derivada de la hora real.</summary>
    /// <param name="TimeOfDay">Hora en formato del motor (0600–2550, saltos de 10 minutos).</param>
    /// <param name="IsNightHold">True si estamos en la "Madrugada" (reloj congelado en 2550 hasta el Rollover).</param>
    /// <param name="MillisecondsIntoSlot">Milisegundos transcurridos dentro del tramo de 10 minutos actual (para interpolar la luz).</param>
    public readonly record struct ClockReading(int TimeOfDay, bool IsNightHold, int MillisecondsIntoSlot);

    /// <summary>
    /// Conversión pura entre hora real y <c>Game1.timeOfDay</c>.
    ///
    /// Modelo (ver ADR-0002):
    /// <list type="bullet">
    /// <item>06:00 real → 0600. El día de juego empieza con el Rollover.</item>
    /// <item>Entre 06:00 y <c>nightHoldStart</c> (01:50 por defecto) el reloj avanza 1:1 (00:30 real → 2430).</item>
    /// <item>De <c>nightHoldStart</c> al siguiente Rollover el reloj queda fijo en la hora tope (2550):
    /// el jugador puede seguir jugando, pero no hay ticks de 10 minutos. Las máquinas recuperan ese tramo
    /// en el Rollover gracias a <c>Utility.CalculateMinutesUntilMorning</c>.</item>
    /// </list>
    /// </summary>
    public static class GameClock
    {
        /// <summary>Primera hora del día de juego en formato motor.</summary>
        public const int DayStartTimeOfDay = 600;

        /// <summary>Última hora alcanzable sin que el motor fuerce el desmayo (≥ 2600).</summary>
        public const int MaxTimeOfDay = 2550;

        private const int SlotMilliseconds = 10 * 60 * 1000;

        /// <summary>Calcula la lectura del reloj para una hora real.</summary>
        /// <param name="realTimeOfDay">Hora real local (0–24h).</param>
        /// <param name="dayStart">Hora real de inicio del día de juego (06:00).</param>
        /// <param name="nightHoldStart">Hora real a la que se congela el reloj (≤ 01:50).</param>
        public static ClockReading Read(TimeSpan realTimeOfDay, TimeSpan dayStart, TimeSpan nightHoldStart)
        {
            if (realTimeOfDay < TimeSpan.Zero || realTimeOfDay >= TimeSpan.FromDays(1))
                throw new ArgumentOutOfRangeException(nameof(realTimeOfDay));

            // Minutos transcurridos desde el inicio del día de juego (0 … 1439).
            TimeSpan sinceStart = realTimeOfDay - dayStart;
            if (sinceStart < TimeSpan.Zero)
                sinceStart += TimeSpan.FromDays(1);

            TimeSpan holdOffset = nightHoldStart - dayStart;
            if (holdOffset <= TimeSpan.Zero)
                holdOffset += TimeSpan.FromDays(1);

            int maxMinutes = ToMinutes(MaxTimeOfDay) - ToMinutes(DayStartTimeOfDay);
            int holdMinutes = Math.Min((int)holdOffset.TotalMinutes, maxMinutes);

            int elapsedMinutes = (int)sinceStart.TotalMinutes;
            if (elapsedMinutes >= holdMinutes)
            {
                int heldAt = FromMinutes(ToMinutes(DayStartTimeOfDay) + holdMinutes - holdMinutes % 10);
                return new ClockReading(heldAt, IsNightHold: true, MillisecondsIntoSlot: 0);
            }

            int slotMinutes = elapsedMinutes - elapsedMinutes % 10;
            int msIntoSlot = (int)(sinceStart - TimeSpan.FromMinutes(slotMinutes)).TotalMilliseconds;
            return new ClockReading(
                FromMinutes(ToMinutes(DayStartTimeOfDay) + slotMinutes),
                IsNightHold: false,
                MillisecondsIntoSlot: Math.Clamp(msIntoSlot, 0, SlotMilliseconds - 1));
        }

        /// <summary>
        /// Número de ticks de 10 minutos necesarios para llevar el reloj de <paramref name="current"/> a <paramref name="target"/>.
        /// Nunca es negativo: el reloj del juego no retrocede (cambio de hora DST, ajuste NTP…).
        /// </summary>
        public static int TicksToReach(int current, int target)
        {
            int diff = ToMinutes(target) - ToMinutes(current);
            return diff <= 0 ? 0 : diff / 10;
        }

        /// <summary>
        /// Calcula cuántos ticks de 10 minutos han transcurrido dentro de la Madrugada (NightHold).
        /// Devuelve 0 si no estamos en NightHold.
        /// </summary>
        public static int NightHoldTicksElapsed(TimeSpan realTimeOfDay, TimeSpan dayStart, TimeSpan nightHoldStart)
        {
            TimeSpan sinceStart = realTimeOfDay - dayStart;
            if (sinceStart < TimeSpan.Zero)
                sinceStart += TimeSpan.FromDays(1);

            TimeSpan holdOffset = nightHoldStart - dayStart;
            if (holdOffset <= TimeSpan.Zero)
                holdOffset += TimeSpan.FromDays(1);

            int maxMinutes = ToMinutes(MaxTimeOfDay) - ToMinutes(DayStartTimeOfDay);
            int holdMinutes = Math.Min((int)holdOffset.TotalMinutes, maxMinutes);

            int elapsedMinutes = (int)sinceStart.TotalMinutes;
            if (elapsedMinutes < holdMinutes)
                return 0;

            int minutesIntoNightHold = elapsedMinutes - holdMinutes;
            return minutesIntoNightHold / 10;
        }

        /// <summary>Convierte una hora en formato motor (ej. 2430) a minutos desde 00:00 (ej. 1470).</summary>
        public static int ToMinutes(int timeOfDay) => timeOfDay / 100 * 60 + timeOfDay % 100;

        /// <summary>Convierte minutos desde 00:00 a formato motor (ej. 1470 → 2430).</summary>
        public static int FromMinutes(int minutes) => minutes / 60 * 100 + minutes % 60;
    }
}
