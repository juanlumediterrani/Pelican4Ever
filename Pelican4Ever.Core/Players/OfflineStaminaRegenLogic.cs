using System;

namespace Pelican4Ever.Core.Players
{
    /// <summary>
    /// Lógica de recuperación gradual de energía y salud mientras el jugador está desconectado.
    /// Por defecto, la barra completa (100% MaxStamina y 100% maxHealth) se recarga en 4 horas reales de desconexión.
    /// </summary>
    public static class OfflineStaminaRegenLogic
    {
        public readonly record struct RecoveryGains(int Stamina, int Health);
        public readonly record struct HostIdleRecoveryResult(int StaminaGained, int HealthGained, DateTimeOffset NextAnchorUtc);

        /// <summary>
        /// Calcula recuperación incremental para el anfitrión agente mientras reposa quieto dentro de su casa
        /// (sin desconectarse ni usar la cama), aplicando la misma tasa por hora que la recuperación offline.
        /// </summary>
        public static HostIdleRecoveryResult CalculateHostIdleRecovery(
            DateTimeOffset anchorUtc,
            DateTimeOffset nowUtc,
            int maxStamina,
            int maxHealth,
            float fullBarHours)
        {
            if (fullBarHours <= 0f || anchorUtc == DateTimeOffset.MinValue || nowUtc <= anchorUtc)
                return new HostIdleRecoveryResult(0, 0, nowUtc);

            TimeSpan elapsed = nowUtc - anchorUtc;
            if (elapsed < TimeSpan.FromMinutes(1))
                return new HostIdleRecoveryResult(0, 0, anchorUtc);

            int stamina = Math.Max(0, (int)Math.Floor(CalculateLinearGain(maxStamina, elapsed, fullBarHours)));
            int health = Math.Max(0, (int)Math.Floor(CalculateLinearGain(maxHealth, elapsed, fullBarHours)));
            if (stamina <= 0 && health <= 0)
                return new HostIdleRecoveryResult(0, 0, anchorUtc);

            return new HostIdleRecoveryResult(stamina, health, nowUtc);
        }

        /// <summary>Calcula cuánto puede recuperar el jugador al volver. Cada barra limita la ganancia al aplicarla.</summary>
        public static RecoveryGains CalculateRecoveryGains(
            PlayerRecord record,
            DateTimeOffset nowUtc,
            int maxStamina,
            int maxHealth,
            float fullBarHours)
        {
            DateTimeOffset staminaAnchor = record.OfflineStaminaAnchorUtc != DateTimeOffset.MinValue
                ? record.OfflineStaminaAnchorUtc
                : record.LastSeenUtc;
            DateTimeOffset healthAnchor = record.OfflineHealthAnchorUtc != DateTimeOffset.MinValue
                ? record.OfflineHealthAnchorUtc
                : staminaAnchor;

            TimeSpan staminaDuration = GetOfflineDuration(staminaAnchor, nowUtc);
            TimeSpan healthDuration = GetOfflineDuration(healthAnchor, nowUtc);
            return new RecoveryGains(
                (int)Math.Round(CalculateStaminaGain(maxStamina, staminaDuration, fullBarHours)),
                CalculateHealthGain(maxHealth, healthDuration, fullBarHours));
        }

        private static TimeSpan GetOfflineDuration(DateTimeOffset anchor, DateTimeOffset nowUtc)
        {
            TimeSpan duration = nowUtc - anchor;
            return anchor != DateTimeOffset.MinValue && duration > TimeSpan.FromSeconds(30)
                ? duration
                : TimeSpan.Zero;
        }

        private static float CalculateLinearGain(
            int maxValue,
            TimeSpan offlineDuration,
            float fullBarHours)
        {
            if (maxValue <= 0 || offlineDuration <= TimeSpan.Zero || fullBarHours <= 0f)
                return 0f;

            double fullBarSeconds = fullBarHours * 3600.0;
            double fraction = offlineDuration.TotalSeconds / fullBarSeconds;
            fraction = Math.Clamp(fraction, 0.0, 1.0);

            return (float)(maxValue * fraction);
        }

        /// <summary>
        /// Calcula la ganancia de estamina en función del tiempo transcurrido offline.
        /// </summary>
        public static float CalculateStaminaGain(
            int maxStamina,
            TimeSpan offlineDuration,
            float fullBarHours) => CalculateLinearGain(maxStamina, offlineDuration, fullBarHours);

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

        /// <summary>
        /// Calcula la ganancia entera de salud en función del tiempo transcurrido offline.
        /// </summary>
        public static int CalculateHealthGain(
            int maxHealth,
            TimeSpan offlineDuration,
            float fullBarHours)
        {
            float gain = CalculateLinearGain(maxHealth, offlineDuration, fullBarHours);
            return gain <= 0f ? 0 : (int)Math.Round(gain, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Calcula la nueva salud tras aplicar la ganancia offline, sin superar <paramref name="maxHealth"/>.
        /// </summary>
        public static int CalculateNewHealth(
            int currentHealth,
            int maxHealth,
            TimeSpan offlineDuration,
            float fullBarHours)
        {
            int gain = CalculateHealthGain(maxHealth, offlineDuration, fullBarHours);
            return ApplyHealthGain(currentHealth, maxHealth, gain);
        }

        /// <summary>
        /// Aplica una ganancia de salud a la salud actual sin superar <paramref name="maxHealth"/>.
        /// </summary>
        public static int ApplyHealthGain(int currentHealth, int maxHealth, int gained)
        {
            if (gained <= 0 || maxHealth <= 0 || currentHealth >= maxHealth)
                return currentHealth;

            return Math.Min(maxHealth, Math.Max(0, currentHealth) + gained);
        }
    }
}
