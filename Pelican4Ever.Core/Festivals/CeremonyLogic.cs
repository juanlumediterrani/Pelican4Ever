using System;

namespace Pelican4Ever.Core.Festivals
{
    /// <summary>
    /// Lógica de dominio pura para ceremonias comunitarias programadas en vivo (TASK-702).
    /// </summary>
    public static class CeremonyLogic
    {
        public const int DefaultCeremonyTime = 2100;
        public const int WarningLeadTimeMinutes = 15;

        /// <summary>
        /// Comprueba si el reloj del juego ha entrado en la ventana de preaviso de la ceremonia comunitaria.
        /// </summary>
        public static bool IsWarningWindow(int currentTimeOfDay, int ceremonyTimeOfDay = DefaultCeremonyTime, int leadMinutes = WarningLeadTimeMinutes)
        {
            // En tiempo Stardew (ej 2045 o 2050 para ceremonia a las 2100)
            int warningTime = ceremonyTimeOfDay - leadMinutes;
            if (ceremonyTimeOfDay % 100 < leadMinutes)
            {
                // Ajuste de base 60 a formato HHmm (ej 2100 - 15 = 2045)
                int hours = (ceremonyTimeOfDay / 100) - 1;
                int mins = 60 + (ceremonyTimeOfDay % 100) - leadMinutes;
                warningTime = (hours * 100) + mins;
            }

            return currentTimeOfDay >= warningTime && currentTimeOfDay < ceremonyTimeOfDay;
        }

        /// <summary>
        /// Comprueba si es el momento exacto o posterior para iniciar la ceremonia.
        /// </summary>
        public static bool IsCeremonyTime(int currentTimeOfDay, int ceremonyTimeOfDay = DefaultCeremonyTime)
        {
            return currentTimeOfDay >= ceremonyTimeOfDay;
        }

        /// <summary>
        /// Comprueba si el jugador se encuentra en la ubicación de la ceremonia para no arrastrar a jugadores ausentes.
        /// </summary>
        public static bool IsPlayerInCeremonyLocation(string? playerLocation, string? ceremonyLocation)
        {
            if (string.IsNullOrWhiteSpace(playerLocation) || string.IsNullOrWhiteSpace(ceremonyLocation))
                return false;

            return string.Equals(playerLocation, ceremonyLocation, StringComparison.OrdinalIgnoreCase);
        }
    }
}
