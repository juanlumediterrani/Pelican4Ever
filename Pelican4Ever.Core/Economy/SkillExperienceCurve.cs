using System;

namespace Pelican4Ever.Core.Economy
{
    /// <summary>Curva diaria sin estado. El contador acumula XP tras el multiplicador fijo y antes del recorte.</summary>
    public static class SkillExperienceCurve
    {
        public readonly record struct Result(int GrantedXp, decimal EarnedToday, decimal Remainder);

        /// <param name="earnedToday">XP acumulado hoy después del multiplicador fijo, antes de aplicar la curva.</param>
        /// <param name="remainder">Fracción de XP concedible pendiente del evento anterior, entre 0 y 1.</param>
        public static Result Apply(decimal earnedToday, int incomingXp, decimal fixedMultiplier,
            int dailyFullXp, decimal overflowMultiplier, decimal remainder)
        {
            if (earnedToday < 0) throw new ArgumentOutOfRangeException(nameof(earnedToday));
            if (incomingXp < 0) throw new ArgumentOutOfRangeException(nameof(incomingXp));
            if (fixedMultiplier < 0) throw new ArgumentOutOfRangeException(nameof(fixedMultiplier));
            if (dailyFullXp < 0) throw new ArgumentOutOfRangeException(nameof(dailyFullXp));
            if (overflowMultiplier < 0) throw new ArgumentOutOfRangeException(nameof(overflowMultiplier));
            if (remainder < 0 || remainder >= 1) throw new ArgumentOutOfRangeException(nameof(remainder));

            decimal scaled = incomingXp * fixedMultiplier;
            decimal full = dailyFullXp == 0 ? scaled : Math.Min(scaled, Math.Max(0, dailyFullXp - earnedToday));
            decimal total = full + (scaled - full) * overflowMultiplier + remainder;
            int granted = checked((int)decimal.Floor(total));
            return new Result(granted, earnedToday + scaled, total - granted);
        }
    }
}
