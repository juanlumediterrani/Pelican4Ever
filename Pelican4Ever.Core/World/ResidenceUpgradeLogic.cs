using System;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Lógica pura de dominio para la gestión y aislamiento de ampliaciones de vivienda,
    /// sótanos privados y renovaciones interiores (cocina, cuna, ático, comedor, rincón, etc.).
    /// </summary>
    public static class ResidenceUpgradeLogic
    {
        public const string CabinHomeSuffix = "_Home";
        public const string CellarSuffix = "_Cellar";
        public const string GreenhouseSuffix = "_Greenhouse";

        // Costes de la pasarela de restauración del invernadero
        public const int GreenhouseRepairCostMaterials = 25000;
        public const int GreenhouseRepairMaterialWood = 200;
        public const int GreenhouseRepairMaterialStone = 100;
        public const int GreenhouseRepairMaterialGoldBars = 5;

        public const int GreenhouseRepairCostJoja = 35000;

        /// <summary>
        /// Obtiene el nombre único del invernadero privado correspondiente a un hueco de residencia.
        /// </summary>
        public static string GetSlotGreenhouseName(string? slotId)
        {
            if (string.IsNullOrWhiteSpace(slotId))
                return "Greenhouse";

            return $"{slotId}{GreenhouseSuffix}";
        }

        /// <summary>
        /// Extrae el identificador del hueco de granja a partir del nombre único del invernadero (ej: "Pelican4Ever_Farm_01_Greenhouse").
        /// </summary>
        public static string? ExtractSlotIdFromGreenhouseName(string? greenhouseName)
        {
            if (string.IsNullOrWhiteSpace(greenhouseName))
                return null;

            if (greenhouseName.EndsWith(GreenhouseSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return greenhouseName.Substring(0, greenhouseName.Length - GreenhouseSuffix.Length);
            }

            return null;
        }

        /// <summary>
        /// Determina si un invernadero está o debe considerarse desbloqueado para un jugador/granja.
        /// </summary>
        public static bool CanUnlockGreenhouse(
            bool hasCcPantry,
            bool hasJojaPantry,
            bool hasCompletedCC,
            bool masterHasCcPantry,
            bool isFarmUnlocked)
        {
            return isFarmUnlocked || hasCcPantry || hasJojaPantry || hasCompletedCC || masterHasCcPantry;
        }

        /// <summary>
        /// Determina si el jugador tiene suficiente oro y materiales para la restauración manual.
        /// </summary>
        public static bool CanAffordMaterialRepair(int playerMoney, int playerWood, int playerStone, int playerGoldBars)
        {
            return playerMoney >= GreenhouseRepairCostMaterials
                && playerWood >= GreenhouseRepairMaterialWood
                && playerStone >= GreenhouseRepairMaterialStone
                && playerGoldBars >= GreenhouseRepairMaterialGoldBars;
        }

        /// <summary>
        /// Determina si el jugador tiene suficiente oro para la cuadrilla de obras (vía Joja / comercial).
        /// </summary>
        public static bool CanAffordJojaRepair(int playerMoney)
        {
            return playerMoney >= GreenhouseRepairCostJoja;
        }

        /// <summary>
        /// Obtiene el nombre único del sótano privado correspondiente a un hueco de residencia.
        /// </summary>
        public static string GetSlotCellarName(string? slotId)
        {
            if (string.IsNullOrWhiteSpace(slotId))
                return "Cellar";

            return $"{slotId}{CellarSuffix}";
        }

        /// <summary>
        /// Extrae el identificador del hueco de granja a partir del nombre único de la cabaña (ej: "Pelican4Ever_Farm_01_Home").
        /// </summary>
        public static string? ExtractSlotIdFromCabinName(string? cabinName)
        {
            if (string.IsNullOrWhiteSpace(cabinName))
                return null;

            if (cabinName.EndsWith(CabinHomeSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return cabinName.Substring(0, cabinName.Length - CabinHomeSuffix.Length);
            }

            return null;
        }

        /// <summary>
        /// Corrige el desfase de textura exterior para edificios de tipo Farmhouse con interior Cabin.
        /// En Stardew Valley vanilla, Building.getSourceRect aplica un desplazamiento horizontal (X) para Cabins
        /// porque asume texturas horizontales de cabañas (cabins.png). Sin embargo, en Pelican 4Ever la residencia
        /// usa buildingType "Farmhouse" (houses.png), donde las ampliaciones están apiladas verticalmente (Y).
        /// </summary>
        public static (int x, int y) AdjustFarmhouseBuildingSourceRect(
            string? buildingType,
            bool isCabinIndoors,
            int upgradeLevel,
            int currentX,
            int currentY,
            int width,
            int height)
        {
            if (string.Equals(buildingType, "Farmhouse", StringComparison.OrdinalIgnoreCase) && isCabinIndoors)
            {
                int upgradeTier = Math.Clamp(upgradeLevel, 0, 2);
                if (upgradeTier > 0)
                {
                    // Revertir el desplazamiento horizontal erróneo aplicado por vanilla
                    int fixedX = currentX - (width * upgradeTier);
                    // Aplicar el desplazamiento vertical correcto correspondiente a houses.png
                    int fixedY = currentY + (height * upgradeTier);
                    return (fixedX, fixedY);
                }
            }
            return (currentX, currentY);
        }

        /// <summary>
        /// Determina si Robin debe mantenerse disponible para otros jugadores aunque haya
        /// obras en construcción en marcha (GAP-10).
        /// </summary>
        public static bool ShouldKeepRobinAvailable(bool keepRobinInShopConfig, string builder)
        {
            return keepRobinInShopConfig && string.Equals(builder, "Robin", StringComparison.OrdinalIgnoreCase);
        }
    }
}
