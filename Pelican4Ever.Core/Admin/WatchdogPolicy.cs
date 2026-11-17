namespace Pelican4Ever.Core.Admin
{
    /// <summary>Política pura para decidir cuándo emitir pulsos <c>WATCHDOG=1</c> a systemd (ADR-0010, TASK-2504).</summary>
    public static class WatchdogPolicy
    {
        public const int PulseIntervalSeconds = 15;

        /// <summary>
        /// Devuelve <c>true</c> si el último tick del hilo principal está dentro del umbral de bloqueo configurado
        /// (<paramref name="hangThresholdSeconds"/>). Un umbral &lt;= 0 desactiva el corte por bloqueo.
        /// </summary>
        public static bool ShouldSendPulse(double secondsSinceLastTick, int hangThresholdSeconds)
        {
            if (double.IsNaN(secondsSinceLastTick) || double.IsInfinity(secondsSinceLastTick))
                return false;

            if (hangThresholdSeconds <= 0)
                return true;

            return secondsSinceLastTick <= hangThresholdSeconds;
        }
    }
}
