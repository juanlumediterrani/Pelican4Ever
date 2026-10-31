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

        /// <summary>
        /// Identifica si la fecha corresponde a un festival con ceremonia comunitaria en vivo (sin minijuego individual).
        /// </summary>
        public static bool IsCeremonyFestival(string? season, int dayOfMonth)
        {
            if (string.IsNullOrWhiteSpace(season))
                return false;

            string s = season.Trim().ToLowerInvariant();
            return (s == "summer" && (dayOfMonth == 11 || dayOfMonth == 28));
        }

        /// <summary>
        /// Ubicación requerida para la ceremonia comunitaria (ej. la playa para el Luau y Danza de las Medusas).
        /// </summary>
        public static string? GetCeremonyLocation(string? season, int dayOfMonth)
        {
            if (IsCeremonyFestival(season, dayOfMonth))
                return "Beach";

            return null;
        }

        /// <summary>
        /// Nombre descriptivo del festival ceremonial.
        /// </summary>
        public static string GetCeremonyFestivalName(string? season, int dayOfMonth)
        {
            if (string.IsNullOrWhiteSpace(season))
                return "Ceremonia";

            string s = season.Trim().ToLowerInvariant();
            if (s == "summer")
            {
                if (dayOfMonth == 11) return "Luau";
                if (dayOfMonth == 28) return "Danza de las Medusas Lunares";
            }

            return "Ceremonia Comunitaria";
        }
    }
}
