using System;

namespace Pelican4Ever.Core.Economy
{
    /// <summary>
    /// Presupuesto diario de energía procedente de la comida ("Saciedad").
    ///
    /// La comida sigue devolviendo energía instantánea como en vanilla, pero el total diario está limitado
    /// a una fracción de la energía máxima. Una vez agotado, la comida solo cura vida y aplica buffs.
    /// Los platos cocinados conceden además "Bien comido" (regeneración pasiva) hasta N veces al día.
    /// </summary>
    public static class SatietyBudget
    {
        /// <summary>Saciedad total del día para un jugador.</summary>
        public static int DailyBudget(int maxStamina, float dailyFraction)
        {
            return Math.Max(0, (int)Math.Floor(maxStamina * dailyFraction));
        }

        /// <summary>Energía instantánea que se concede al comer, dado la Saciedad acumulada actual y la capacidad máxima.</summary>
        /// <param name="requestedGain">Energía que el motor habría sumado (ya recortada al máximo de la barra).</param>
        /// <param name="currentSatiety">Nivel de Saciedad actual acumulado.</param>
        /// <param name="maxSatiety">Capacidad máxima de Saciedad.</param>
        public static int AllowedGain(int requestedGain, float currentSatiety, int maxSatiety)
        {
            if (requestedGain <= 0)
                return 0;

            int remaining = Math.Max(0, maxSatiety - (int)Math.Floor(currentSatiety));
            return Math.Min(requestedGain, remaining);
        }

        public static int AllowedGain(int requestedGain, int currentSatiety, int maxSatiety)
            => AllowedGain(requestedGain, (float)currentSatiety, maxSatiety);
    }
}
