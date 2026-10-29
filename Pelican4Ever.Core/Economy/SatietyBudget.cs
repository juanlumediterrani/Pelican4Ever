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

        /// <summary>Energía instantánea que se concede al comer, dado lo ya consumido hoy.</summary>
        /// <param name="requestedGain">Energía que el motor habría sumado (ya recortada al máximo de la barra).</param>
        /// <param name="alreadyUsed">Saciedad consumida hoy.</param>
        /// <param name="budget">Saciedad total del día.</param>
        public static int AllowedGain(int requestedGain, int alreadyUsed, int budget)
        {
            if (requestedGain <= 0)
                return 0;

            int remaining = Math.Max(0, budget - alreadyUsed);
            return Math.Min(requestedGain, remaining);
        }

        /// <summary>Energía regenerada en el Spa, respetando el tope diario.</summary>
        public static int AllowedSpaGain(int requestedGain, int alreadyUsed, int dailyCap)
        {
            if (requestedGain <= 0)
                return 0;

            return Math.Min(requestedGain, Math.Max(0, dailyCap - alreadyUsed));
        }
    }
}
