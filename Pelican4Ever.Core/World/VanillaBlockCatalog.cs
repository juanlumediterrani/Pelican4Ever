using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.World
{
    public enum BlockKind
    {
        EdgeStrip,
        Corner,
        Doorway
    }

    public enum BlockOrientation
    {
        North,
        South,
        East,
        West,
        NorthWest,
        NorthEast,
        SouthWest,
        SouthEast
    }

    /// <summary>
    /// Definición de un bloque vanilla completo que puede copiarse y pegarse en un anexo (TASK-909).
    /// Regla: se define estrictamente por mapa de origen y rectángulo, nunca por índices de tile.
    /// </summary>
    public sealed record VanillaBlockDefinition(
        string Id,
        string FarmTypeId,
        string SourceMapAsset,
        TileArea SourceRectangle,
        BlockKind Kind,
        BlockOrientation Orientation,
        string Description,
        TilePoint? DoorwayStepTile = null
    );

    /// <summary>
    /// Catálogo de bloques vanilla para cerrar bordes cortados y bocas de entrada (TASK-909).
    /// </summary>
    public static class VanillaBlockCatalog
    {
        public static readonly IReadOnlyDictionary<string, VanillaBlockDefinition> Blocks = new Dictionary<string, VanillaBlockDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            // ================= Standard (Maps/Farm, 80×65) =================
            ["Standard_North_Strip"] = new(
                "Standard_North_Strip", "Standard", "Maps/Farm",
                new TileArea(0, 0, 40, 3), BlockKind.EdgeStrip, BlockOrientation.North,
                "Franja norte vanilla de árboles y acantilado"),
            ["Standard_South_Strip"] = new(
                "Standard_South_Strip", "Standard", "Maps/Farm",
                new TileArea(0, 61, 40, 4), BlockKind.EdgeStrip, BlockOrientation.South,
                "Franja sur vanilla de linde de bosque"),
            ["Standard_West_Strip"] = new(
                "Standard_West_Strip", "Standard", "Maps/Farm",
                new TileArea(0, 20, 3, 32), BlockKind.EdgeStrip, BlockOrientation.West,
                "Franja oeste vanilla de acantilados y arboleda"),
            ["Standard_East_Strip"] = new(
                "Standard_East_Strip", "Standard", "Maps/Farm",
                new TileArea(77, 20, 3, 32), BlockKind.EdgeStrip, BlockOrientation.East,
                "Franja este vanilla de muro y vallas"),
            ["Standard_NW_Corner"] = new(
                "Standard_NW_Corner", "Standard", "Maps/Farm",
                new TileArea(0, 0, 4, 4), BlockKind.Corner, BlockOrientation.NorthWest,
                "Esquina noroeste con acantilado y remate de árboles"),
            ["Standard_NE_Corner"] = new(
                "Standard_NE_Corner", "Standard", "Maps/Farm",
                new TileArea(76, 0, 4, 4), BlockKind.Corner, BlockOrientation.NorthEast,
                "Esquina noreste con linde natural"),
            ["Standard_SW_Corner"] = new(
                "Standard_SW_Corner", "Standard", "Maps/Farm",
                new TileArea(0, 61, 4, 4), BlockKind.Corner, BlockOrientation.SouthWest,
                "Esquina suroeste con linde de bosque"),
            ["Standard_SE_Corner"] = new(
                "Standard_SE_Corner", "Standard", "Maps/Farm",
                new TileArea(76, 61, 4, 4), BlockKind.Corner, BlockOrientation.SouthEast,
                "Esquina sureste con linde de bosque"),
            ["Standard_North_Doorway"] = new(
                "Standard_North_Doorway", "Standard", "Maps/Farm",
                new TileArea(38, 0, 6, 3), BlockKind.Doorway, BlockOrientation.North,
                "Boca de entrada norte con paso despejado hacia Backwoods",
                new TilePoint(2, 1)),
            ["Standard_South_Doorway"] = new(
                "Standard_South_Doorway", "Standard", "Maps/Farm",
                new TileArea(38, 61, 6, 4), BlockKind.Doorway, BlockOrientation.South,
                "Boca de entrada sur con paso despejado hacia Forest",
                new TilePoint(2, 2)),

            // ================= Fishing (Maps/Farm_Fishing, 80×65) =================
            ["Fishing_North_Strip"] = new(
                "Fishing_North_Strip", "Fishing", "Maps/Farm_Fishing",
                new TileArea(0, 0, 40, 3), BlockKind.EdgeStrip, BlockOrientation.North,
                "Franja norte fluvial con acantilado y orilla"),
            ["Fishing_South_Strip"] = new(
                "Fishing_South_Strip", "Fishing", "Maps/Farm_Fishing",
                new TileArea(0, 61, 40, 4), BlockKind.EdgeStrip, BlockOrientation.South,
                "Franja sur fluvial con orilla y bosque"),
            ["Fishing_West_Strip"] = new(
                "Fishing_West_Strip", "Fishing", "Maps/Farm_Fishing",
                new TileArea(0, 20, 4, 32), BlockKind.EdgeStrip, BlockOrientation.West,
                "Franja oeste fluvial de acantilado y agua"),
            ["Fishing_East_Strip"] = new(
                "Fishing_East_Strip", "Fishing", "Maps/Farm_Fishing",
                new TileArea(76, 20, 4, 32), BlockKind.EdgeStrip, BlockOrientation.East,
                "Franja este fluvial con orilla este"),
            ["Fishing_NW_Corner"] = new(
                "Fishing_NW_Corner", "Fishing", "Maps/Farm_Fishing",
                new TileArea(0, 0, 4, 4), BlockKind.Corner, BlockOrientation.NorthWest,
                "Esquina noroeste fluvial"),
            ["Fishing_NE_Corner"] = new(
                "Fishing_NE_Corner", "Fishing", "Maps/Farm_Fishing",
                new TileArea(76, 0, 4, 4), BlockKind.Corner, BlockOrientation.NorthEast,
                "Esquina noreste fluvial"),
            ["Fishing_SW_Corner"] = new(
                "Fishing_SW_Corner", "Fishing", "Maps/Farm_Fishing",
                new TileArea(0, 61, 4, 4), BlockKind.Corner, BlockOrientation.SouthWest,
                "Esquina suroeste fluvial"),
            ["Fishing_SE_Corner"] = new(
                "Fishing_SE_Corner", "Fishing", "Maps/Farm_Fishing",
                new TileArea(76, 61, 4, 4), BlockKind.Corner, BlockOrientation.SouthEast,
                "Esquina sureste fluvial"),
            ["Fishing_South_Bridge_Doorway"] = new(
                "Fishing_South_Bridge_Doorway", "Fishing", "Maps/Farm_Fishing",
                new TileArea(38, 61, 6, 4), BlockKind.Doorway, BlockOrientation.South,
                "Boca de entrada sur sobre el puente de madera oficial (tiles 809/859)",
                new TilePoint(2, 2)),

            // ================= Foraging (Maps/Farm_Foraging, 80×65) =================
            ["Foraging_North_Strip"] = new(
                "Foraging_North_Strip", "Foraging", "Maps/Farm_Foraging",
                new TileArea(0, 0, 40, 3), BlockKind.EdgeStrip, BlockOrientation.North,
                "Franja norte forestal con arboleda espesa"),
            ["Foraging_South_Strip"] = new(
                "Foraging_South_Strip", "Foraging", "Maps/Farm_Foraging",
                new TileArea(0, 61, 40, 4), BlockKind.EdgeStrip, BlockOrientation.South,
                "Franja sur forestal de sotobosque"),
            ["Foraging_West_Strip"] = new(
                "Foraging_West_Strip", "Foraging", "Maps/Farm_Foraging",
                new TileArea(0, 20, 4, 32), BlockKind.EdgeStrip, BlockOrientation.West,
                "Franja oeste forestal con hierba y tocones"),
            ["Foraging_East_Strip"] = new(
                "Foraging_East_Strip", "Foraging", "Maps/Farm_Foraging",
                new TileArea(76, 20, 4, 32), BlockKind.EdgeStrip, BlockOrientation.East,
                "Franja este forestal"),
            ["Foraging_NW_Corner"] = new(
                "Foraging_NW_Corner", "Foraging", "Maps/Farm_Foraging",
                new TileArea(0, 0, 4, 4), BlockKind.Corner, BlockOrientation.NorthWest,
                "Esquina noroeste forestal"),
            ["Foraging_NE_Corner"] = new(
                "Foraging_NE_Corner", "Foraging", "Maps/Farm_Foraging",
                new TileArea(76, 0, 4, 4), BlockKind.Corner, BlockOrientation.NorthEast,
                "Esquina noreste forestal"),
            ["Foraging_SW_Corner"] = new(
                "Foraging_SW_Corner", "Foraging", "Maps/Farm_Foraging",
                new TileArea(0, 61, 4, 4), BlockKind.Corner, BlockOrientation.SouthWest,
                "Esquina suroeste forestal"),
            ["Foraging_SE_Corner"] = new(
                "Foraging_SE_Corner", "Foraging", "Maps/Farm_Foraging",
                new TileArea(76, 61, 4, 4), BlockKind.Corner, BlockOrientation.SouthEast,
                "Esquina sureste forestal"),

            // ================= Mining (Maps/Farm_Mining, 80×65) =================
            ["Mining_North_Strip"] = new(
                "Mining_North_Strip", "Mining", "Maps/Farm_Mining",
                new TileArea(0, 0, 40, 3), BlockKind.EdgeStrip, BlockOrientation.North,
                "Franja norte minera de acantilados escarpados"),
            ["Mining_South_Strip"] = new(
                "Mining_South_Strip", "Mining", "Maps/Farm_Mining",
                new TileArea(0, 61, 40, 4), BlockKind.EdgeStrip, BlockOrientation.South,
                "Franja sur minera"),
            ["Mining_West_Strip"] = new(
                "Mining_West_Strip", "Mining", "Maps/Farm_Mining",
                new TileArea(0, 20, 4, 32), BlockKind.EdgeStrip, BlockOrientation.West,
                "Franja oeste minera de paredes rocosas"),
            ["Mining_East_Strip"] = new(
                "Mining_East_Strip", "Mining", "Maps/Farm_Mining",
                new TileArea(76, 20, 4, 32), BlockKind.EdgeStrip, BlockOrientation.East,
                "Franja este minera"),
            ["Mining_NW_Corner"] = new(
                "Mining_NW_Corner", "Mining", "Maps/Farm_Mining",
                new TileArea(0, 0, 4, 4), BlockKind.Corner, BlockOrientation.NorthWest,
                "Esquina noroeste rocosa"),
            ["Mining_NE_Corner"] = new(
                "Mining_NE_Corner", "Mining", "Maps/Farm_Mining",
                new TileArea(76, 0, 4, 4), BlockKind.Corner, BlockOrientation.NorthEast,
                "Esquina noreste rocosa"),
            ["Mining_SW_Corner"] = new(
                "Mining_SW_Corner", "Mining", "Maps/Farm_Mining",
                new TileArea(0, 61, 4, 4), BlockKind.Corner, BlockOrientation.SouthWest,
                "Esquina suroeste rocosa"),
            ["Mining_SE_Corner"] = new(
                "Mining_SE_Corner", "Mining", "Maps/Farm_Mining",
                new TileArea(76, 61, 4, 4), BlockKind.Corner, BlockOrientation.SouthEast,
                "Esquina sureste rocosa"),

            // ================= Combat (Maps/Farm_Combat, 80×65) =================
            ["Combat_North_Strip"] = new(
                "Combat_North_Strip", "Combat", "Maps/Farm_Combat",
                new TileArea(0, 0, 40, 3), BlockKind.EdgeStrip, BlockOrientation.North,
                "Franja norte salvaje con acantilados"),
            ["Combat_South_Strip"] = new(
                "Combat_South_Strip", "Combat", "Maps/Farm_Combat",
                new TileArea(0, 61, 40, 4), BlockKind.EdgeStrip, BlockOrientation.South,
                "Franja sur salvaje"),
            ["Combat_West_Strip"] = new(
                "Combat_West_Strip", "Combat", "Maps/Farm_Combat",
                new TileArea(0, 20, 4, 32), BlockKind.EdgeStrip, BlockOrientation.West,
                "Franja oeste salvaje"),
            ["Combat_East_Strip"] = new(
                "Combat_East_Strip", "Combat", "Maps/Farm_Combat",
                new TileArea(76, 20, 4, 32), BlockKind.EdgeStrip, BlockOrientation.East,
                "Franja este salvaje"),
            ["Combat_NW_Corner"] = new(
                "Combat_NW_Corner", "Combat", "Maps/Farm_Combat",
                new TileArea(0, 0, 4, 4), BlockKind.Corner, BlockOrientation.NorthWest,
                "Esquina noroeste salvaje"),
            ["Combat_NE_Corner"] = new(
                "Combat_NE_Corner", "Combat", "Maps/Farm_Combat",
                new TileArea(76, 0, 4, 4), BlockKind.Corner, BlockOrientation.NorthEast,
                "Esquina noreste salvaje"),
            ["Combat_SW_Corner"] = new(
                "Combat_SW_Corner", "Combat", "Maps/Farm_Combat",
                new TileArea(0, 61, 4, 4), BlockKind.Corner, BlockOrientation.SouthWest,
                "Esquina suroeste salvaje"),
            ["Combat_SE_Corner"] = new(
                "Combat_SE_Corner", "Combat", "Maps/Farm_Combat",
                new TileArea(76, 61, 4, 4), BlockKind.Corner, BlockOrientation.SouthEast,
                "Esquina sureste salvaje"),

            // ================= FourCorners (Maps/Farm_FourCorners, 80×80) =================
            ["FourCorners_North_Strip"] = new(
                "FourCorners_North_Strip", "FourCorners", "Maps/Farm_FourCorners",
                new TileArea(0, 0, 40, 3), BlockKind.EdgeStrip, BlockOrientation.North,
                "Franja norte cuatro esquinas"),
            ["FourCorners_South_Strip"] = new(
                "FourCorners_South_Strip", "FourCorners", "Maps/Farm_FourCorners",
                new TileArea(0, 76, 40, 4), BlockKind.EdgeStrip, BlockOrientation.South,
                "Franja sur cuatro esquinas"),
            ["FourCorners_West_Strip"] = new(
                "FourCorners_West_Strip", "FourCorners", "Maps/Farm_FourCorners",
                new TileArea(0, 20, 4, 32), BlockKind.EdgeStrip, BlockOrientation.West,
                "Franja oeste cuatro esquinas"),
            ["FourCorners_East_Strip"] = new(
                "FourCorners_East_Strip", "FourCorners", "Maps/Farm_FourCorners",
                new TileArea(76, 20, 4, 32), BlockKind.EdgeStrip, BlockOrientation.East,
                "Franja este cuatro esquinas"),
            ["FourCorners_NW_Corner"] = new(
                "FourCorners_NW_Corner", "FourCorners", "Maps/Farm_FourCorners",
                new TileArea(0, 0, 4, 4), BlockKind.Corner, BlockOrientation.NorthWest,
                "Esquina noroeste cuatro esquinas"),
            ["FourCorners_NE_Corner"] = new(
                "FourCorners_NE_Corner", "FourCorners", "Maps/Farm_FourCorners",
                new TileArea(76, 0, 4, 4), BlockKind.Corner, BlockOrientation.NorthEast,
                "Esquina noreste cuatro esquinas"),
            ["FourCorners_SW_Corner"] = new(
                "FourCorners_SW_Corner", "FourCorners", "Maps/Farm_FourCorners",
                new TileArea(0, 76, 4, 4), BlockKind.Corner, BlockOrientation.SouthWest,
                "Esquina suroeste cuatro esquinas"),
            ["FourCorners_SE_Corner"] = new(
                "FourCorners_SE_Corner", "FourCorners", "Maps/Farm_FourCorners",
                new TileArea(76, 76, 4, 4), BlockKind.Corner, BlockOrientation.SouthEast,
                "Esquina sureste cuatro esquinas"),

            // ================= Ranching (Maps/Farm_Ranching, 100×75) =================
            ["Ranching_North_Strip"] = new(
                "Ranching_North_Strip", "Ranching", "Maps/Farm_Ranching",
                new TileArea(0, 0, 40, 3), BlockKind.EdgeStrip, BlockOrientation.North,
                "Franja norte pradera con valla y arboleda"),
            ["Ranching_South_Strip"] = new(
                "Ranching_South_Strip", "Ranching", "Maps/Farm_Ranching",
                new TileArea(0, 71, 40, 4), BlockKind.EdgeStrip, BlockOrientation.South,
                "Franja sur pradera"),
            ["Ranching_West_Strip"] = new(
                "Ranching_West_Strip", "Ranching", "Maps/Farm_Ranching",
                new TileArea(0, 20, 4, 32), BlockKind.EdgeStrip, BlockOrientation.West,
                "Franja oeste pradera"),
            ["Ranching_East_Strip"] = new(
                "Ranching_East_Strip", "Ranching", "Maps/Farm_Ranching",
                new TileArea(96, 20, 4, 32), BlockKind.EdgeStrip, BlockOrientation.East,
                "Franja este pradera"),
            ["Ranching_NW_Corner"] = new(
                "Ranching_NW_Corner", "Ranching", "Maps/Farm_Ranching",
                new TileArea(0, 0, 4, 4), BlockKind.Corner, BlockOrientation.NorthWest,
                "Esquina noroeste pradera"),
            ["Ranching_NE_Corner"] = new(
                "Ranching_NE_Corner", "Ranching", "Maps/Farm_Ranching",
                new TileArea(96, 0, 4, 4), BlockKind.Corner, BlockOrientation.NorthEast,
                "Esquina noreste pradera"),
            ["Ranching_SW_Corner"] = new(
                "Ranching_SW_Corner", "Ranching", "Maps/Farm_Ranching",
                new TileArea(0, 71, 4, 4), BlockKind.Corner, BlockOrientation.SouthWest,
                "Esquina suroeste pradera"),
            ["Ranching_SE_Corner"] = new(
                "Ranching_SE_Corner", "Ranching", "Maps/Farm_Ranching",
                new TileArea(96, 71, 4, 4), BlockKind.Corner, BlockOrientation.SouthEast,
                "Esquina sureste pradera"),

            // ================= Beach (Maps/Farm_Island, 110×110) =================
            ["Beach_North_Strip"] = new(
                "Beach_North_Strip", "Beach", "Maps/Farm_Island",
                new TileArea(0, 0, 40, 3), BlockKind.EdgeStrip, BlockOrientation.North,
                "Franja norte playa con acantilados"),
            ["Beach_South_Strip"] = new(
                "Beach_South_Strip", "Beach", "Maps/Farm_Island",
                new TileArea(0, 106, 40, 4), BlockKind.EdgeStrip, BlockOrientation.South,
                "Franja sur playa con mar"),
            ["Beach_West_Strip"] = new(
                "Beach_West_Strip", "Beach", "Maps/Farm_Island",
                new TileArea(0, 20, 4, 32), BlockKind.EdgeStrip, BlockOrientation.West,
                "Franja oeste playa con mar y arena"),
            ["Beach_East_Strip"] = new(
                "Beach_East_Strip", "Beach", "Maps/Farm_Island",
                new TileArea(106, 20, 4, 32), BlockKind.EdgeStrip, BlockOrientation.East,
                "Franja este playa con acantilados"),
            ["Beach_NW_Corner"] = new(
                "Beach_NW_Corner", "Beach", "Maps/Farm_Island",
                new TileArea(0, 0, 4, 4), BlockKind.Corner, BlockOrientation.NorthWest,
                "Esquina noroeste playa"),
            ["Beach_NE_Corner"] = new(
                "Beach_NE_Corner", "Beach", "Maps/Farm_Island",
                new TileArea(106, 0, 4, 4), BlockKind.Corner, BlockOrientation.NorthEast,
                "Esquina noreste playa"),
            ["Beach_SW_Corner"] = new(
                "Beach_SW_Corner", "Beach", "Maps/Farm_Island",
                new TileArea(0, 106, 4, 4), BlockKind.Corner, BlockOrientation.SouthWest,
                "Esquina suroeste playa"),
            ["Beach_SE_Corner"] = new(
                "Beach_SE_Corner", "Beach", "Maps/Farm_Island",
                new TileArea(106, 106, 4, 4), BlockKind.Corner, BlockOrientation.SouthEast,
                "Esquina sureste playa"),
        };

        public static VanillaBlockDefinition? FindBlock(string farmType, BlockKind kind, BlockOrientation orientation)
        {
            return Blocks.Values.FirstOrDefault(b =>
                string.Equals(b.FarmTypeId, farmType, StringComparison.OrdinalIgnoreCase) &&
                b.Kind == kind &&
                b.Orientation == orientation);
        }

        public static IReadOnlyList<VanillaBlockDefinition> GetBlocksForType(string farmType)
        {
            return Blocks.Values.Where(b => string.Equals(b.FarmTypeId, farmType, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}
