using System;

namespace Pelican4Ever.Core.Economy
{
    /// <summary>
    /// Lógica de descanso y recuperación pasiva de estamina sentado (GAP-07). Solo funciona con el buff "Bien comido".
    /// </summary>
    public static class SittingRegenLogic
    {
        /// <summary>
        /// Evalúa un tick de un segundo sentado y determina si se otorga energía y el nuevo acumulador.
        /// </summary>
        public static bool Tick(
            bool isSitting,
            bool isInBed,
            bool isWellFed,
            float currentStamina,
            int maxStamina,
            int currentAccumulator,
            int regenSeconds,
            out float staminaGain,
            out int newAccumulator)
        {
            staminaGain = 0f;
            newAccumulator = 0;

            if (!isSitting || isInBed || !isWellFed || regenSeconds <= 0)
                return false;

            int acc = currentAccumulator + 1;
            if (acc >= regenSeconds)
            {
                newAccumulator = 0;
                if (currentStamina < maxStamina)
                {
                    staminaGain = Math.Min(1f, maxStamina - currentStamina);
                    return true;
                }
                return false;
            }

            newAccumulator = acc;
            return false;
        }
    }
}
