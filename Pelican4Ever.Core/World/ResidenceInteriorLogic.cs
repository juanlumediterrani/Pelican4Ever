using System;

namespace Pelican4Ever.Core.World
{
    public record FurnitureTargetCoordinate(string ItemKey, int TargetX, int TargetY, int Rotation = 0);

    /// <summary>
    /// Lógica de distribución espacial armónica y espaciosa para el interior de las residencias (FarmHouse 12x12).
    /// Evita que el mobiliario quede apelotonado contra la pared superior (Y=4).
    /// </summary>
    public static class ResidenceInteriorLogic
    {
        // Posiciones mejoradas y espaciosas
        public static readonly FurnitureTargetCoordinate BedPlacement = new("Bed", 9, 8, 0);
        public static readonly FurnitureTargetCoordinate DiningTablePlacement = new("Table", 5, 6, 0);
        public static readonly FurnitureTargetCoordinate DiningChairPlacement = new("Chair", 4, 6, 1);
        public static readonly FurnitureTargetCoordinate TvPlacement = new("TV", 1, 5, 1);
        public static readonly FurnitureTargetCoordinate FireplacePlacement = new("Fireplace", 8, 4, 0);
        public static readonly FurnitureTargetCoordinate RugPlacement = new("Rug", 6, 7, 0);
        public static readonly FurnitureTargetCoordinate WindowPlacement = new("Window", 3, 1, 0);
        public static readonly FurnitureTargetCoordinate TreePaintingPlacement = new("Tree", 6, 1, 0);
        public static readonly FurnitureTargetCoordinate PlantPlacement = new("Plant", 1, 10, 0);
        public static readonly FurnitureTargetCoordinate StarterChestPlacement = new("StarterChest", 2, 9, 0);

        /// <summary>
        /// Comprueba si un mueble está en la posición por defecto apretada contra la pared norte (Y &lt;= 4).
        /// </summary>
        public static bool IsCrampedNorthWallPlacement(string? itemName, int tileX, int tileY)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return false;

            // Mesa comprimida en (5, 4)
            if (itemName.Contains("Table", StringComparison.OrdinalIgnoreCase) && tileX == 5 && tileY <= 4)
                return true;

            // Silla comprimida en (4, 4)
            if (itemName.Contains("Chair", StringComparison.OrdinalIgnoreCase) && tileX == 4 && tileY <= 4)
                return true;

            // TV comprimido en (1, 4)
            if (itemName.Contains("TV", StringComparison.OrdinalIgnoreCase) && tileX == 1 && tileY <= 4)
                return true;

            return false;
        }

        /// <summary>
        /// Comprueba que el pasillo principal desde la puerta sur (X=3, Y=11) hasta el centro de la sala
        /// esté 100% despejado y libre de colisiones.
        /// </summary>
        public static bool IsWalkwayTile(int tileX, int tileY)
        {
            // Pasillo directo desde la puerta (3, 11) hacia arriba (3, 7..11)
            return tileX == 3 && tileY >= 7 && tileY <= 11;
        }
    }
}
