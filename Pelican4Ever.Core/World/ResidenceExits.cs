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
        /// Devuelve los dos lados libres de la residencia privada que no se utilizan para volver a la Plaza Central (Granja Host o Mapa Puente),
        /// según el borde donde se ubica el portal en la plaza.
        /// </summary>
        public static IReadOnlyList<AnnexSide> FreeSides(PortalEdge edge)
        {
            return edge switch
            {
                PortalEdge.South => new[] { AnnexSide.East, AnnexSide.South },
                PortalEdge.North => new[] { AnnexSide.North, AnnexSide.East },
                PortalEdge.West or PortalEdge.East => new[] { AnnexSide.North, AnnexSide.South },
                _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, null)
            };
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
