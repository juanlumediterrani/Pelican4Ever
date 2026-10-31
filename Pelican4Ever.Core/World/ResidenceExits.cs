using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Lados de la residencia privada donde pueden emplazarse anexos temáticos (TASK-901).
    /// </summary>
    public enum AnnexSide
    {
        North,
        East,
        South
    }

    /// <summary>
    /// Lógica pura para determinar las salidas y lados libres de las residencias (TASK-901).
    /// </summary>
    public static class ResidenceExits
    {
        /// <summary>
        /// Devuelve los dos lados libres de la residencia privada que no se utilizan para volver a la Plaza Central (Granja Host),
        /// según la ubicación espacial del portal en la plaza.
        /// </summary>
        public static IReadOnlyList<AnnexSide> FreeSides(int townTileX, int townTileY)
        {
            if (SpatialWarpRouter.ShouldReturnToHostFarm(townTileX, townTileY, "Backwoods"))
            {
                // El norte vuelve al host -> quedan libres Este y Sur
                return new[] { AnnexSide.East, AnnexSide.South };
            }

            if (SpatialWarpRouter.ShouldReturnToHostFarm(townTileX, townTileY, "Forest"))
            {
                // El sur vuelve al host -> quedan libres Norte y Este
                return new[] { AnnexSide.North, AnnexSide.East };
            }

            // El este (BusStop) vuelve al host -> quedan libres Norte y Sur
            return new[] { AnnexSide.North, AnnexSide.South };
        }

        /// <summary>
        /// Determina el lado cardinal asociado a una ubicación de salida vanilla de la granja.
        /// </summary>
        public static AnnexSide? SideForVanillaTarget(string? targetLocation)
        {
            if (string.IsNullOrWhiteSpace(targetLocation))
                return null;

            if (targetLocation.Equals("Backwoods", StringComparison.OrdinalIgnoreCase)
                || targetLocation.Equals("Mountain", StringComparison.OrdinalIgnoreCase))
                return AnnexSide.North;

            if (targetLocation.Equals("BusStop", StringComparison.OrdinalIgnoreCase))
                return AnnexSide.East;

            if (targetLocation.Equals("Forest", StringComparison.OrdinalIgnoreCase))
                return AnnexSide.South;

            return null;
        }
    }
}
