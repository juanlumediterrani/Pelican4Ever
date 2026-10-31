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
        // Posiciones mejoradas, espaciosas y alineadas con la Farmhouse vanilla
        public static readonly FurnitureTargetCoordinate BedPlacement = new("Bed", 9, 8, 0);
        public static readonly FurnitureTargetCoordinate DiningTablePlacement = new("Table", 5, 6, 0);
        public static readonly FurnitureTargetCoordinate DiningChairPlacement = new("Chair", 4, 6, 1);
        public static readonly FurnitureTargetCoordinate TvPlacement = new("TV", 1, 4, 0);
        public static readonly FurnitureTargetCoordinate FireplacePlacement = new("Fireplace", 8, 4, 0);
        public static readonly FurnitureTargetCoordinate RugPlacement = new("Rug", 6, 8, 0);
        public static readonly FurnitureTargetCoordinate WindowPlacement = new("Window", 3, 1, 0);
        public static readonly FurnitureTargetCoordinate TreePaintingPlacement = new("Tree", 5, 1, 0);
        public static readonly FurnitureTargetCoordinate PlantPlacement = new("Plant", 1, 10, 0);
        public static readonly FurnitureTargetCoordinate StarterChestPlacement = new("StarterChest", 2, 9, 0);

        /// <summary>
        /// Comprueba si un mueble está en una posición inicial comprimida, bugueada o errónea
        /// (como la TV girada lateralmente, la mesa apretada al norte o la cama/planta encajadas en la esquina superior izquierda).
        /// </summary>
        public static bool IsMisplacedStarterFurniture(string? itemName, int tileX, int tileY, int rotation = 0)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return false;

            // Mesa de comedor comprimida en (5, 4) o contra pared norte (Y <= 4)
            if (itemName.Contains("Table", StringComparison.OrdinalIgnoreCase) && tileX == 5 && tileY <= 4)
                return true;

            // Silla de comedor comprimida en (4, 4) o contra pared norte (Y <= 4)
            if (itemName.Contains("Chair", StringComparison.OrdinalIgnoreCase) && tileX == 4 && tileY <= 4)
                return true;

            // TV rotado lateralmente (rotation != 0) o ubicado en (1, 5) del glitch anterior o metido en la pared (Y <= 3)
            if (itemName.Contains("TV", StringComparison.OrdinalIgnoreCase) && (rotation != 0 || (tileX == 1 && tileY == 5) || tileY <= 3))
                return true;

            // Cama encajada en el rincón superior izquierdo / vacío (X <= 2, Y <= 3)
            if (itemName.Contains("Bed", StringComparison.OrdinalIgnoreCase) && tileX <= 2 && tileY <= 3)
                return true;

            // Planta encajada sobre el cabecero de la cama (X <= 2, Y <= 3)
            if (itemName.Contains("Plant", StringComparison.OrdinalIgnoreCase) && tileX <= 2 && tileY <= 3)
                return true;

            return false;
        }

        /// <summary>
        /// Comprueba si un mueble está en la posición por defecto apretada contra la pared norte (Y &lt;= 4).
        /// </summary>
        public static bool IsCrampedNorthWallPlacement(string? itemName, int tileX, int tileY)
        {
            return IsMisplacedStarterFurniture(itemName, tileX, tileY, 0);
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
