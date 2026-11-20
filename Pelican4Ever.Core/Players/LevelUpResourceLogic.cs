using System;

namespace Pelican4Ever.Core.Players
{
    /// <summary>
    /// Reglas puras para preservar vida y energía cuando <c>LevelUpMenu</c> se resuelve
    /// fuera del cierre nocturno nativo (por ejemplo durante <c>BedLogout</c> o al iniciar sesión).
    /// </summary>
    public static class LevelUpResourceLogic
    {
        public readonly record struct ResourceSnapshot(int Health, int MaxHealth, float Stamina);

        /// <summary>
        /// Fuera del flujo nocturno nativo (<c>Game1.showingEndOfNightStuff</c>),
        /// subir de nivel o elegir profesión no debe recargar vida y energía al 100%.
        /// </summary>
        public static bool ShouldPreserveResources(bool showingEndOfNightStuff) => !showingEndOfNightStuff;

        /// <summary>Detecta el relleno de nivel en una actualización de granjero recibida por el host.</summary>
        public static bool IsRemoteLevelUpRefill(
            int previousPendingLevels, int pendingLevels, float previousStamina, float stamina,
            int previousMaxStamina, int maxStamina)
        {
            return pendingLevels < previousPendingLevels
                && maxStamina > 0
                && maxStamina == previousMaxStamina
                && stamina > previousStamina
                && stamina >= maxStamina;
        }

        /// <summary>
        /// Calcula la vida resultante tras aplicar el perk de nivel o profesión sin curar vida faltante.
        /// Si aumentó <c>maxHealth</c> (Combate/Luchador/Defensor), suma únicamente ese incremento.
        /// </summary>
        public static int ResolveHealth(ResourceSnapshot before, int currentMaxHealth)
        {
            if (before.Health <= 0)
                return before.Health;

            int maxHealthGain = Math.Max(0, currentMaxHealth - before.MaxHealth);
            int safeMaxHealth = Math.Max(0, currentMaxHealth);
            return Math.Clamp(before.Health + maxHealthGain, 0, safeMaxHealth);
        }

        /// <summary>
        /// Preserva exactamente la energía previa al perk sin superar el máximo vigente.
        /// </summary>
        public static float ResolveStamina(ResourceSnapshot before, int currentMaxStamina)
        {
            if (currentMaxStamina <= 0)
                return before.Stamina;

            return Math.Min((float)currentMaxStamina, before.Stamina);
        }
    }
}
