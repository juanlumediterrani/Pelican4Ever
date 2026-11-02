using System;

namespace Pelican4Ever.Core.Players
{
    /// <summary>
    /// Lógica de recuperación gradual de energía mientras el jugador está desconectado.
    /// Por defecto, la barra completa (100% MaxStamina) se recarga en 4 horas reales de desconexión.
    /// </summary>
    public static class OfflineStaminaRegenLogic
    {
        /// <summary>
        /// Calcula la ganancia de estamina en función del tiempo transcurrido offline.
        /// </summary>
        public static float CalculateStaminaGain(
            int maxStamina,
            TimeSpan offlineDuration,
            float fullBarHours)
        {
            if (maxStamina <= 0 || offlineDuration <= TimeSpan.Zero || fullBarHours <= 0f)
                return 0f;

            double fullBarSeconds = fullBarHours * 3600.0;
            double fraction = offlineDuration.TotalSeconds / fullBarSeconds;
            fraction = Math.Clamp(fraction, 0.0, 1.0);

            return (float)(maxStamina * fraction);
        }

        /// <summary>
        /// Calcula el nuevo valor de estamina tras aplicar la ganancia offline, sin superar MaxStamina.
        /// </summary>
        public static float CalculateNewStamina(
            float currentStamina,
            int maxStamina,
            TimeSpan offlineDuration,
            float fullBarHours)
        {
            float gain = CalculateStaminaGain(maxStamina, offlineDuration, fullBarHours);
            return ApplyGain(currentStamina, maxStamina, gain);
        }

        /// <summary>
        /// Aplica una ganancia de estamina a la estamina actual sin superar MaxStamina.
        /// </summary>
        public static float ApplyGain(float currentStamina, int maxStamina, float gained)
        {
            if (gained <= 0f)
                return currentStamina;

            return Math.Min((float)maxStamina, currentStamina + gained);
        }
    }
}
