using System;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Reglas de presencia y servicio de tenderos en sus mostradores (GAP-10 y TASK-401).
    /// </summary>
    public static class NpcShopkeeperLogic
    {
        /// <summary>
        /// Comprueba si un aldeano está actualmente de servicio en su mostrador comercial.
        /// Durante el horario laboral, los tenderos no deben desplazarse aleatoriamente.
        /// </summary>
        public static bool IsShopkeeperOnDuty(string npcName, string locationName, int timeOfDay)
        {
            if (string.IsNullOrEmpty(npcName) || string.IsNullOrEmpty(locationName))
                return false;

            return npcName switch
            {
                "Robin" => locationName == "ScienceHouse" && timeOfDay >= 900 && timeOfDay < 1700,
                "Pierre" => locationName == "SeedShop" && timeOfDay >= 900 && timeOfDay < 1700,
                "Marnie" => locationName == "AnimalShop" && timeOfDay >= 900 && timeOfDay < 1600,
                "Clint" => locationName == "Blacksmith" && timeOfDay >= 900 && timeOfDay < 1600,
                "Gus" => locationName == "Saloon" && timeOfDay >= 1200 && timeOfDay < 2400,
                "Willy" => locationName == "FishShop" && timeOfDay >= 900 && timeOfDay < 1700,
                "Harvey" => locationName == "Hospital" && timeOfDay >= 900 && timeOfDay < 1500,
                "Marlon" or "Gil" => locationName == "AdventureGuild" && timeOfDay >= 1400 && timeOfDay < 2200,
                "Sandy" => locationName == "SandyHouse" && timeOfDay >= 900 && timeOfDay < 2400,
                "Morris" => locationName == "JojaMart" && timeOfDay >= 900 && timeOfDay < 2300,
                "Krobus" => locationName == "Sewer",
                "Dwarf" => locationName is "Mine" or "VolcanoDungeon",
                _ => false
            };
        }

        /// <summary>
        /// Determina si un aldeano es apto para micro-deambular (Micro-Wandering).
        /// </summary>
        public static bool CanMicroWander(
            string npcName,
            string locationName,
            int timeOfDay,
            bool isOutdoors,
            bool isDoingRouteAnimation,
            bool isSleeping,
            bool isSitting,
            bool hasActivity,
            bool isEmoting)
        {
            // Si está durmiendo, sentado, gesticulando, con actividad o realizando animación de fin de ruta
            if (isDoingRouteAnimation || isSleeping || isSitting || hasActivity || isEmoting)
                return false;

            // Si es un tendero de servicio en su comercio
            if (IsShopkeeperOnDuty(npcName, locationName, timeOfDay))
                return false;

            // Solo permitir en exteriores o en áreas públicas amplias de ocio (como el Saloon fuera del mostrador o el Club)
            // para evitar descolocar interiores con mostradores o pasillos estrechos
            if (!isOutdoors && locationName != "Saloon" && locationName != "Club")
                return false;

            return true;
        }
    }
}
