using System;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Lógica de dominio pura para la siembra diaria de hierba silvestre y azul en residencias (TASK-606).
    /// </summary>
    public static class FarmGrassLogic
    {
        public const int StandardGrassType = 1;
        public const int BlueGrassType = 7;
        public const double DailyGrassChance = 0.15;
        public const double MeadowlandsBlueGrassChance = 0.10;

        /// <summary>
        /// Comprueba si el tipo de granja corresponde a Pradera (Meadowlands).
        /// </summary>
        public static bool IsMeadowlands(string? farmType)
        {
            if (string.IsNullOrWhiteSpace(farmType))
                return false;

            return string.Equals(farmType, "Meadowlands", StringComparison.OrdinalIgnoreCase)
                || string.Equals(farmType, "MeadowlandsFarm", StringComparison.OrdinalIgnoreCase)
                || string.Equals(farmType, "Ranching", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determina el tipo de hierba a sembrar: azul (7) para Pradera con 10% de probabilidad, o estándar (1).
        /// </summary>
        public static int DetermineGrassType(bool isMeadowlands, double roll)
        {
            if (isMeadowlands && roll < MeadowlandsBlueGrassChance)
                return BlueGrassType;

            return StandardGrassType;
        }

        /// <summary>
        /// Calcula el número de intentos de siembra diaria de hierba a partir de los datos de ubicación.
        /// </summary>
        public static int CalculateDailyAttempts(int minDailyWeeds, int maxDailyWeeds, int dayOfMonth, bool isSpring, int firstDayMultiplier, double roll)
        {
            int min = Math.Max(1, minDailyWeeds);
            int max = Math.Max(min, maxDailyWeeds);
            int count = (int)Math.Floor(min + roll * (max - min + 1));
            count = Math.Clamp(count, min, max);

            if (dayOfMonth == 1 && isSpring)
            {
                count *= Math.Max(1, firstDayMultiplier);
            }

            return count;
        }

        /// <summary>
        /// Valida si una casilla reúne los requisitos básicos para sembrar hierba.
        /// </summary>
        public static bool CanSpawnGrassOnTile(bool isWinter, bool hasDiggableProperty, bool isTileOpen, bool isOccupied, bool isWater, bool isNoSpawn)
        {
            if (isWinter)
                return false;

            return hasDiggableProperty && isTileOpen && !isOccupied && !isWater && !isNoSpawn;
        }
    }
}
