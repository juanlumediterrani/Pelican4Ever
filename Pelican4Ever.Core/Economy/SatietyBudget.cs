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

        /// <summary>
        /// Determina el umbral de Saciedad necesario para activar el efecto "Bien comido" (por defecto 1/4 = 25% de la capacidad).
        /// </summary>
        public static float WellFedThreshold(int maxSatiety, float thresholdFraction = 0.25f)
        {
            return Math.Max(0f, maxSatiety * thresholdFraction);
        }

        /// <summary>
        /// Evalúa si el nivel de Saciedad actual otorga el efecto "Bien comido" (al menos 1/4 de la barra de saciedad).
        /// </summary>
        public static bool IsWellFed(float currentSatiety, int maxSatiety, float thresholdFraction = 0.25f)
        {
            if (maxSatiety <= 0)
                return false;

            return currentSatiety >= WellFedThreshold(maxSatiety, thresholdFraction);
        }

        /// <summary>
        /// Calcula los minutos restantes que le quedan al efecto "Bien comido" antes de que la Saciedad
        /// caiga por debajo del umbral de 1/4 por el vaciado continuo.
        /// </summary>
        public static int RemainingWellFedMinutes(float currentSatiety, int maxSatiety, float drainHours, float thresholdFraction = 0.25f)
        {
            if (maxSatiety <= 0 || drainHours <= 0)
                return 0;

            float threshold = WellFedThreshold(maxSatiety, thresholdFraction);
            if (currentSatiety < threshold)
                return 0;

            double minutes = ((currentSatiety - threshold) / maxSatiety) * (drainHours * 60.0);
            return Math.Max(1, (int)Math.Ceiling(minutes));
        }
    }
}
