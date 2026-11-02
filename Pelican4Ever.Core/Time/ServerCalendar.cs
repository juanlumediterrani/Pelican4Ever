using System;

namespace Pelican4Ever.Core.Time
{
    public enum Season
    {
        Spring = 0,
        Summer = 1,
        Fall = 2,
        Winter = 3
    }

    /// <summary>Fecha de juego. <see cref="TotalDays"/> es 0 para el Día 1 de Primavera del Año 1.</summary>
    public readonly record struct GameDate(int Year, Season Season, int Day)
    {
        public const int DaysPerSeason = 28;
        public const int DaysPerYear = DaysPerSeason * 4;

        public int TotalDays => (Year - 1) * DaysPerYear + (int)Season * DaysPerSeason + (Day - 1);

        /// <summary>Clave de temporada tal y como la usa el motor ("spring", "summer"…).</summary>
        public string SeasonKey => Season.ToString().ToLowerInvariant();

        public static GameDate FromTotalDays(int totalDays)
        {
            if (totalDays < 0)
                throw new ArgumentOutOfRangeException(nameof(totalDays), "La fecha es anterior a la época del calendario.");

            return new GameDate(
                Year: totalDays / DaysPerYear + 1,
                Season: (Season)(totalDays % DaysPerYear / DaysPerSeason),
                Day: totalDays % DaysPerSeason + 1);
        }

        public override string ToString() => $"Año {Year}, {Season} {Day}";
    }

    /// <summary>
    /// Calendario perpetuo: la fecha de juego se DERIVA de la fecha real y de una época (un lunes),
    /// en lugar de incrementarse en cada Rollover. Así, si el servidor está caído durante un Rollover,
    /// el calendario no se desfasa y el Día 1 de cada estación siempre cae en lunes.
    /// </summary>
    public sealed class ServerCalendar
    {
        private readonly DateTime epoch;
        private readonly TimeSpan calendarDayChange;

        /// <param name="epoch">Fecha real (lunes) que corresponde al Día 1 de Primavera del Año 1.</param>
        /// <param name="calendarDayChange">Hora real a la que avanza la fecha de calendario (02:00).</param>
        public ServerCalendar(DateTime epoch, TimeSpan calendarDayChange)
        {
            if (epoch.DayOfWeek != DayOfWeek.Monday)
                throw new ArgumentException("La época del calendario debe ser un lunes.", nameof(epoch));

            this.epoch = epoch.Date;
            this.calendarDayChange = calendarDayChange;
        }

        /// <summary>
        /// Fecha real del día de calendario en curso. Antes del corte seguimos en la fecha anterior.
        /// </summary>
        public DateTime GetGameDayRealDate(DateTime localNow) => (localNow - calendarDayChange).Date;

        /// <summary>Días de juego transcurridos desde la época para una hora local dada.</summary>
        public int GetTotalDays(DateTime localNow) => (GetGameDayRealDate(localNow) - epoch).Days;

        /// <summary>Fecha de juego para una hora local dada.</summary>
        public GameDate GetDate(DateTime localNow) => GameDate.FromTotalDays(GetTotalDays(localNow));

        /// <summary>Devuelve false si la hora real aún precede a la época configurada.</summary>
        public bool TryGetDate(DateTime localNow, out GameDate date)
        {
            int totalDays = GetTotalDays(localNow);
            if (totalDays < 0)
            {
                date = default;
                return false;
            }

            date = GameDate.FromTotalDays(totalDays);
            return true;
        }

        /// <summary>
        /// Decide cuántos Rollovers ejecutar para alinear la partida con el calendario real.
        /// </summary>
        /// <param name="savedTotalDays">Fecha de la partida guardada (<c>Game1.Date.TotalDays</c>).</param>
        /// <param name="localNow">Hora local actual.</param>
        /// <param name="maxCatchUp">Máximo de Rollovers seguidos permitidos.</param>
        public CatchUpPlan PlanCatchUp(int savedTotalDays, DateTime localNow, int maxCatchUp)
        {
            int expected = GetTotalDays(localNow);
            int behind = expected - savedTotalDays;

            if (behind <= 0)
                return new CatchUpPlan(0, SkipToTotalDays: null, IsAhead: behind < 0);

            if (behind <= maxCatchUp)
                return new CatchUpPlan(behind, SkipToTotalDays: null, IsAhead: false);

            // Demasiado desfase: ejecutar un único Rollover y fijar la fecha directamente.
            return new CatchUpPlan(1, SkipToTotalDays: expected, IsAhead: false);
        }
    }

    /// <param name="RolloversToRun">Rollovers nativos a ejecutar en cadena.</param>
    /// <param name="SkipToTotalDays">Si no es null, tras el último Rollover se fija la fecha a este valor.</param>
    /// <param name="IsAhead">La partida va por delante del calendario real (época mal configurada o reloj atrasado).</param>
    public readonly record struct CatchUpPlan(int RolloversToRun, int? SkipToTotalDays, bool IsAhead);
}
