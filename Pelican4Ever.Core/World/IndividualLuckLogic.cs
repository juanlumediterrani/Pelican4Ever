using System;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Lógica pura para calcular la suerte diaria (DailyLuck) individual por jugador,
    /// evitando que todo el servidor comparta la misma tirada de RNG (GAP-19).
    /// </summary>
    public static class IndividualLuckLogic
    {
        public const double MinLuck = -0.2;
        public const double MaxLuck = 0.2;
        public const double SpecialCharmBonus = 0.025;

        /// <summary>
        /// Calcula la suerte diaria determinista de un jugador para una partida y día concretos.
        /// </summary>
        public static double Calculate(ulong uniqueGameId, int totalDays, long playerId, bool hasSpecialCharm)
        {
            // Combinación determinista de semilla de partida, día de juego y UID del jugador
            unchecked
            {
                int seed = (int)(uniqueGameId ^ (ulong)(playerId * 397) ^ (ulong)(totalDays * 71));
                Random rnd = new Random(seed);

                // Tirada estándar vanilla: random entre -100 y 100 dividido entre 1000, tope en 0.1
                double rawLuck = Math.Min(0.1, (double)rnd.Next(-100, 101) / 1000.0);
                double charmBonus = hasSpecialCharm ? SpecialCharmBonus : 0.0;

                return Math.Min(Math.Max(rawLuck + charmBonus, MinLuck), MaxLuck);
            }
        }
    }
}
