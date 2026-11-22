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

        /// <summary>
        /// Evalúa si un edificio dentro de una residencia privada o anexo puede moverse en CarpenterMenu (TASK-607).
        /// Vanilla comprobaba <c>Game1.getFarm().greenhouseUnlocked</c> (solo el host) y exigía permisos globales de cabaña.
        /// </summary>
        public static bool CanMoveResidenceBuilding(
            bool isGreenhouse,
            bool residenceGreenhouseUnlocked,
            bool isMasterGame,
            bool canManageLocation)
        {
            if (isGreenhouse && !residenceGreenhouseUnlocked)
                return false;

            return isMasterGame || canManageLocation;
        }

        /// <summary>
        /// Determina si al mover un edificio en una residencia hay que limpiar los valores cacheados
        /// (<c>mainFarmhouseEntry</c> y <c>mapMainMailboxPosition</c>) de la instancia <see cref="Farm"/> (TASK-607).
        /// Vanilla solo llama a <c>UnsetFarmhouseValues()</c> si el interior se llama literalmente <c>"FarmHouse"</c>.
        /// </summary>
        public static bool NeedsResidenceFarmhouseCacheReset(string? locationName, string? buildingType)
        {
            return !string.IsNullOrWhiteSpace(locationName)
                && locationName.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase)
                && string.Equals(buildingType, "Farmhouse", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Extrae el nivel de mapa de una ruta de <c>FarmHouse</c> (<c>Maps\FarmHouse</c> → 0,
        /// <c>FarmHouse1</c> → 1, <c>FarmHouse2</c> → 2), ignorando el sufijo <c>_marriage</c>.
        /// </summary>
        public static int? ParseFarmHouseMapUpgradeLevel(string? mapPath)
        {
            if (string.IsNullOrWhiteSpace(mapPath))
                return null;

            string normalized = mapPath.Replace('/', '\\').Trim();
            int slash = normalized.LastIndexOf('\\');
            string leaf = slash >= 0 ? normalized.Substring(slash + 1) : normalized;
            if (leaf.EndsWith("_marriage", StringComparison.OrdinalIgnoreCase))
                leaf = leaf.Substring(0, leaf.Length - "_marriage".Length);

            if (string.Equals(leaf, "FarmHouse", StringComparison.OrdinalIgnoreCase))
                return 0;
            if (string.Equals(leaf, "FarmHouse1", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (string.Equals(leaf, "FarmHouse2", StringComparison.OrdinalIgnoreCase))
                return 2;

            return null;
        }

        /// <summary>
        /// Determina si Robin debe mostrar su menú completo de carpintería (tienda, construir, renovar)
        /// aunque el jugador tenga una ampliación de vivienda en curso o pendiente por buzón, evitando que
        /// vanilla le encierre únicamente en la tienda de materiales.
        /// </summary>
        public static bool ShouldAllowCarpenterMenuDuringHouseUpgrade(
            bool keepRobinInCarpentryShop,
            int daysUntilHouseUpgrade,
            bool hasPendingMailHouseUpgrade,
            bool isBuildingUnderConstruction)
        {
            if (isBuildingUnderConstruction)
                return false;

            if (!keepRobinInCarpentryShop)
                return false;

            return daysUntilHouseUpgrade >= 0 || hasPendingMailHouseUpgrade;
        }

        /// <summary>
        /// Plan de reconciliación nocturna (rollover) para la vivienda de un jugador.
        /// </summary>
        public readonly record struct HouseUpgradeReconciliationPlan(
            int NextDaysUntilHouseUpgrade,
            int TargetFarmerUpgradeLevel,
            bool ShouldCompleteUpgrade,
            bool NeedsOwnerRebind,
            bool NeedsCabinUpgradeSync,
            bool NeedsObjectShift,
            int ShiftFromLevel);

        /// <summary>
        /// Evalúa durante el rollover si la ampliación de casa del jugador avanzó, quedó atascada en 0
        /// o quedó desincronizada respecto al interior (<c>Cabin</c>) de su residencia.
        /// </summary>
        public static HouseUpgradeReconciliationPlan EvaluateHouseUpgradeReconciliation(
            int farmerHouseUpgradeLevel,
            int daysUntilHouseUpgrade,
            bool hasCabin,
            int cabinUpgradeLevel,
            int cabinDisplayedLevel,
            int? cabinMapLevel,
            bool cabinHasOwner,
            bool cabinOwnerMatchesFarmer)
        {
            int clampedFarmerLevel = Math.Clamp(farmerHouseUpgradeLevel, 0, 3);
            int nextDays = daysUntilHouseUpgrade;
            int targetLevel = clampedFarmerLevel;
            bool shouldComplete = false;

            if (daysUntilHouseUpgrade > 0)
            {
                nextDays = daysUntilHouseUpgrade - 1;
                if (nextDays <= 0)
                {
                    nextDays = -1;
                    if (clampedFarmerLevel < 3)
                    {
                        targetLevel = clampedFarmerLevel + 1;
                        shouldComplete = true;
                    }
                }
            }
            else if (daysUntilHouseUpgrade == 0)
            {
                // Recuperar ampliaciones atascadas en 0 días pendientes (ni avanzaban ni liberaban a Robin)
                nextDays = -1;
                if (clampedFarmerLevel < 3)
                {
                    targetLevel = clampedFarmerLevel + 1;
                    shouldComplete = true;
                }
            }
            else if (daysUntilHouseUpgrade < -1)
            {
                nextDays = -1;
            }

            bool needsOwnerRebind = hasCabin && (!cabinHasOwner || !cabinOwnerMatchesFarmer);

            if (!hasCabin)
            {
                return new HouseUpgradeReconciliationPlan(
                    NextDaysUntilHouseUpgrade: nextDays,
                    TargetFarmerUpgradeLevel: targetLevel,
                    ShouldCompleteUpgrade: shouldComplete,
                    NeedsOwnerRebind: false,
                    NeedsCabinUpgradeSync: false,
                    NeedsObjectShift: false,
                    ShiftFromLevel: 0);
            }

            int expectedMapTier = Math.Min(2, targetLevel);
            int actualMapTier = cabinMapLevel ?? Math.Clamp(cabinDisplayedLevel, 0, 2);
            bool mapBehind = actualMapTier < expectedMapTier;
            bool displayedBehind = cabinDisplayedLevel < targetLevel;
            bool cabinLevelBehind = cabinUpgradeLevel < targetLevel;

            bool needsCabinSync = targetLevel > 0 && (shouldComplete || mapBehind || displayedBehind || cabinLevelBehind);
            bool needsObjectShift = targetLevel > 0 && (mapBehind || cabinDisplayedLevel < targetLevel) && actualMapTier < expectedMapTier;

            return new HouseUpgradeReconciliationPlan(
                NextDaysUntilHouseUpgrade: nextDays,
                TargetFarmerUpgradeLevel: targetLevel,
                ShouldCompleteUpgrade: shouldComplete,
                NeedsOwnerRebind: needsOwnerRebind,
                NeedsCabinUpgradeSync: needsCabinSync,
                NeedsObjectShift: needsObjectShift,
                ShiftFromLevel: Math.Clamp(actualMapTier, 0, 2));
        }
    }
}
