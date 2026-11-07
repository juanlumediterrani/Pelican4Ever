using System;
using System.Collections.Generic;
using Pelican4Ever.Core.World;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.ObjectModel;
using xTile.Tiles;

namespace Pelican4Ever.Framework
{
    /// <summary>
    /// Paleta de índices de tiles vanilla usada por el pintor de márgenes de minimapas.
    /// Los índices de terreno (agua, arena) se aprenden del mapa vanilla de origen en tiempo
    /// de ejecución para garantizar que el margen usa exactamente el arte del propio tipo de
    /// granja; el resto son índices canónicos de <c>spring_outdoorsTileSheet</c> validados
    /// contra el spritesheet real durante las rondas de auditoría visual (TASK-902).
    /// </summary>
    public static class AnnexRimPalette
    {
        /// <summary>Suelo firme transitable (tierra labrada limpia).</summary>
        public const int DirtGround = 587;

        /// <summary>Agua del propio mapa fluvial si el origen no aporta índice mejor.</summary>
        public const int WaterFallback = 1230;

        /// <summary>Arena del propio mapa costero si el origen no aporta índice mejor.</summary>
        public const int SandFallback = 1269;

        public const int WoodPlank = 1244;
        public const int BridgeBeamEven = 184;
        public const int BridgeBeamOdd = 211;
        public const int RailNorthSouth = 194;
        public const int RailWest = 195;
        public const int RailEast = 193;

        public const int FenceLeftBase = VerticalMultiTileClassifier.FenceLeftBaseTileIndex;
        public const int FenceLeftTop = VerticalMultiTileClassifier.FenceLeftTopTileIndex;
        public const int FenceMidBase = VerticalMultiTileClassifier.FenceMidBaseTileIndex;
        public const int FenceMidTop = VerticalMultiTileClassifier.FenceMidTopTileIndex;
        public const int FenceRightBase = VerticalMultiTileClassifier.FenceRightBaseTileIndex;
        public const int FenceRightTop = VerticalMultiTileClassifier.FenceRightTopTileIndex;

        /// <summary>Arbusto bloqueante clásico del borde de granja (Buildings, con remate 326 en Front).</summary>
        public const int Bush = 351;
        public const int BushTop = 326;

        /// <summary>Seto 2×2 completo: base 113..114 (Buildings) + copa 88..89 (Front).</summary>
        public const int HedgeBaseLeft = 113;
        public const int HedgeBaseRight = 114;
        public const int HedgeCopaLeft = 88;
        public const int HedgeCopaRight = 89;

        /// <summary>Arbolillo 2×2 completo: base 168..169 + copa 143..144 (Front).</summary>
        public const int HedgeTreeBaseLeft = 168;
        public const int HedgeTreeBaseRight = 169;
        public const int HedgeTreeCopaLeft = 143;
        public const int HedgeTreeCopaRight = 144;

        /// <summary>Talud de colina: base 441/444 (Buildings) + remate 416/419 (Front).</summary>
        public const int CliffBaseLeft = 441;
        public const int CliffBaseRight = 444;
        public const int CliffTopLeft = 416;
        public const int CliffTopRight = 419;
    }

    /// <summary>
    /// Pintor de márgenes decorativos inalcanzables y entradas por tipo de granja para los
    /// minimapas temáticos 40×32 (TASK-902). Reemplaza el sellado perimetral invisible: cada
    /// borde presenta una franja propia del tipo (setos, acantilado, agua, bosque, cerca,
    /// arena) apoyada en decoración vanilla completa y visible, sin cortes de mapa. La abertura
    /// de entrada interrumpe el margen, con un estilo de entrada coherente con el tipo.
    /// </summary>
    public static class AnnexBorderArtist
    {
        /// <summary>
        /// Descubre la paleta de terreno (agua, arena) escaneando el Back del mapa vanilla de
        /// origen: primer tile con propiedad de hoja "Water"="T" y primer tile con
        /// "NoSprinklers"="T" sin agua. Aplica fallbacks canónicos si no aparecen.
        /// </summary>
        public static (int WaterIndex, int SandIndex) DiscoverTerrainPalette(Map? source)
        {
            int? water = null;
            int? sand = null;

            if (source?.GetLayer("Back") is { } back)
            {
                for (int y = 0; y < back.LayerHeight && (water == null || sand == null); y++)
                {
                    for (int x = 0; x < back.LayerWidth && (water == null || sand == null); x++)
                    {
                        Tile? t = back.Tiles[x, y];
                        if (t == null)
                            continue;

                        if (water == null && SheetFlag(t, "Water"))
                            water = t.TileIndex;
                        if (sand == null && SheetFlag(t, "NoSprinklers") && !SheetFlag(t, "Water"))
                            sand = t.TileIndex;
                    }
                }
            }

            return (water ?? AnnexRimPalette.WaterFallback, sand ?? AnnexRimPalette.SandFallback);
        }

        private static bool SheetFlag(Tile tile, string flag)
        {
            if (tile.TileIndexProperties.TryGetValue(flag, out var raw) && raw.ToString()?.Equals("T", StringComparison.OrdinalIgnoreCase) == true)
                return true;
            return tile.Properties.TryGetValue(flag, out var raw2) && raw2.ToString()?.Equals("T", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Construye el margen decorativo perimetral (2 casillas de profundidad) en Back/
        /// Buildings/Front/AlwaysFront según el estilo declarado por tipo de granja. La
        /// decoración de cada casilla pertenece a composiciones completas (arbustos 2×2 con
        /// copa, taludes con remate, agua, cercas con travesaño), nunca a elementos cortados.
        /// <paramref name="skipTiles"/> son las casillas de la abertura de entrada y del corredor.
        /// </summary>
        public static void BuildRim(
            AnnexTemplate template,
            AnnexSide side,
            int width,
            int height,
            Map? sourceMap,
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer? alwaysFrontLayer,
            TileSheet mainSheet,
            HashSet<(int X, int Y)> skipTiles)
        {
            const int depth = 2;
            var styles = AnnexMapDesignLogic.RimStyles(template.Behavior, template.Id);

            // Limpieza previa de toda la banda perimetral (restos del recorte vanilla truncados).
            foreach ((int x, int y) in AnnexMapDesignLogic.EnumerateRimTiles(width, height, depth))
            {
                if (skipTiles.Contains((x, y)))
                    continue;

                bldLayer.Tiles[x, y] = null;
                frontLayer.Tiles[x, y] = null;
                if (alwaysFrontLayer != null) alwaysFrontLayer.Tiles[x, y] = null;
                backLayer.Tiles[x, y] = null;
            }

            // Bordes horizontales: N (y = 0..depth-1) y S (y = height-depth..height-1).
            for (int x = 0; x < width; x++)
            {
                for (int band = 0; band < depth; band++)
                {
                    if (!skipTiles.Contains((x, band)))
                        PaintStyledCell(styles.North, x, band, band, backLayer, bldLayer, frontLayer, mainSheet);

                    int sy = height - 1 - band;
                    if (!skipTiles.Contains((x, sy)))
                        PaintStyledCell(styles.South, x, band, sy, backLayer, bldLayer, frontLayer, mainSheet);
                }
            }

            // Bordes verticales: O (x = 0..depth-1) y E (x = width-depth..width-1); las esquinas
            // ya pintadas por N/S se respetan (coherencia de los remates).
            for (int y = depth; y < height - depth; y++)
            {
                for (int band = 0; band < depth; band++)
                {
                    if (!skipTiles.Contains((band, y)))
                        PaintStyledCell(styles.West, band, band, y, backLayer, bldLayer, frontLayer, mainSheet);

                    int ex = width - 1 - band;
                    if (!skipTiles.Contains((ex, y)))
                        PaintStyledCell(styles.East, band, band, y, backLayer, bldLayer, frontLayer, mainSheet);
                }
            }

            // Asegurar inalcanzabilidad: cada casilla del margen no abierta queda bloqueada,
            // respaldada por la decoración pintada (no barreras invisibles huérfanas).
            foreach ((int x, int y) in AnnexMapDesignLogic.EnumerateRimTiles(width, height, depth))
            {
                if (skipTiles.Contains((x, y)))
                    continue;

                if (backLayer.Tiles[x, y] != null)
                    backLayer.Tiles[x, y].Properties["Passable"] = "F";
                if (bldLayer.Tiles[x, y] != null)
                    bldLayer.Tiles[x, y].Properties["Passable"] = "F";
            }
        }

        /// <summary>
        /// Pinta una casilla del margen con el estilo correspondiente. band = 0 es la
        /// fila/columna del borde (la más externa), band = 1 la más interna del margen;
        /// index = posición a lo largo del borde (para variar regularmente el patrón).
        /// </summary>
        private static void PaintStyledCell(
            AnnexRimStyle style,
            int index,
            int band,
            int y,
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            TileSheet mainSheet)
        {
            switch (style)
            {
                case AnnexRimStyle.RiverBank:
                    PaintRiverCell(index, band, y, backLayer, bldLayer, mainSheet);
                    break;
                case AnnexRimStyle.OceanShore:
                    PaintOceanCell(index, band, y, backLayer, bldLayer, mainSheet);
                    break;
                case AnnexRimStyle.RockyCanyon:
                    PaintCliffCell(index, band, y, backLayer, bldLayer, frontLayer, mainSheet);
                    break;
                case AnnexRimStyle.ForestEdge:
                    PaintForestCell(index, band, y, backLayer, bldLayer, frontLayer, mainSheet);
                    break;
                case AnnexRimStyle.WildThorns:
                    PaintThornCell(index, band, y, backLayer, bldLayer, mainSheet);
                    break;
                case AnnexRimStyle.RanchFence:
                    PaintRanchCell(index, band, y, backLayer, bldLayer, frontLayer, mainSheet);
                    break;
                case AnnexRimStyle.HedgeAndTrees:
                default:
                    PaintHedgeCell(index, band, y, backLayer, bldLayer, frontLayer, mainSheet);
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Pintores por estilo. Cada uno compone una franja coherente y completa:
        // band 0 = borde exterior, band 1 = banda interior del margen.
        // ------------------------------------------------------------------

        private static void PaintHedgeCell(int index, int band, int y, Layer back, Layer bld, Layer front, TileSheet sheet)
        {
            if (band == 0)
            {
                PlaceWildBack(back, sheet, index, y);
                if (index % 4 == 0)
                {
                    SetTile(bld, sheet, index % 8 < 4 ? AnnexRimPalette.HedgeBaseLeft : AnnexRimPalette.HedgeBaseRight, index, y);
                    SetTile(front, sheet, index % 8 < 4 ? AnnexRimPalette.HedgeCopaLeft : AnnexRimPalette.HedgeCopaRight, index, y - 1);
                }
                else if (index % 4 == 2)
                {
                    SetTile(bld, sheet, AnnexRimPalette.Bush, index, y);
                }
            }
            else
            {
                PlaceWildBack(back, sheet, index, y);
            }
        }

        private static void PaintForestCell(int index, int band, int y, Layer back, Layer bld, Layer front, TileSheet sheet)
        {
            if (band == 0)
            {
                PlaceWildBack(back, sheet, index, y);
                if (index % 5 < 3)
                {
                    SetTile(bld, sheet, index % 2 == 0 ? AnnexRimPalette.HedgeTreeBaseLeft : AnnexRimPalette.HedgeTreeBaseRight, index, y);
                    SetTile(front, sheet, index % 2 == 0 ? AnnexRimPalette.HedgeTreeCopaLeft : AnnexRimPalette.HedgeTreeCopaRight, index, y - 1);
                }
                else
                {
                    SetTile(bld, sheet, AnnexRimPalette.Bush, index, y);
                }
            }
            else
            {
                PlaceWildBack(back, sheet, index, y);
            }
        }

        private static void PaintRiverCell(int index, int band, int y, Layer back, Layer bld, TileSheet sheet)
        {
            if (band == 0)
            {
                var tile = new StaticTile(back, sheet, BlendMode.Alpha, AnnexRimPalette.WaterFallback);
                tile.Properties["Type"] = "Water";
                back.Tiles[index, y] = tile;
            }
            else
            {
                PlaceWildBack(back, sheet, index, y);
                if (index % 5 == 4)
                    SetTile(bld, sheet, AnnexRimPalette.Bush, index, y);
            }
        }

        private static void PaintOceanCell(int index, int band, int y, Layer back, Layer bld, TileSheet sheet)
        {
            if (band == 0)
            {
                var tile = new StaticTile(back, sheet, BlendMode.Alpha, AnnexRimPalette.WaterFallback);
                tile.Properties["Type"] = "Water";
                back.Tiles[index, y] = tile;
            }
            else
            {
                var tile = new StaticTile(back, sheet, BlendMode.Alpha, AnnexRimPalette.SandFallback);
                tile.Properties["Type"] = "Dirt";
                back.Tiles[index, y] = tile;
                if (index % 6 == 3)
                    SetTile(bld, sheet, AnnexRimPalette.Bush, index, y);
            }
        }

        private static void PaintCliffCell(int index, int band, int y, Layer back, Layer bld, Layer front, TileSheet sheet)
        {
            PlaceWildBack(back, sheet, index, y);
            if (band == 0)
            {
                SetTile(bld, sheet, index % 2 == 0 ? AnnexRimPalette.CliffBaseLeft : AnnexRimPalette.CliffBaseRight, index, y);
                SetTile(front, sheet, index % 2 == 0 ? AnnexRimPalette.CliffTopLeft : AnnexRimPalette.CliffTopRight, index, y - 1);
            }
        }

        private static void PaintThornCell(int index, int band, int y, Layer back, Layer bld, TileSheet sheet)
        {
            PlaceWildBack(back, sheet, index, y);
            if (band == 0 && index % 3 != 2)
                SetTile(bld, sheet, AnnexRimPalette.Bush, index, y);
        }

        private static void PaintRanchCell(int index, int band, int y, Layer back, Layer bld, Layer front, TileSheet sheet)
        {
            PlaceWildBack(back, sheet, index, y);
            if (band == 1)
            {
                int baseIdx;
                int topIdx;
                if (index % 7 == 0)
                {
                    baseIdx = AnnexRimPalette.FenceLeftBase;
                    topIdx = AnnexRimPalette.FenceLeftTop;
                }
                else if (index % 7 == 6)
                {
                    baseIdx = AnnexRimPalette.FenceRightBase;
                    topIdx = AnnexRimPalette.FenceRightTop;
                }
                else
                {
                    baseIdx = AnnexRimPalette.FenceMidBase;
                    topIdx = AnnexRimPalette.FenceMidTop;
                }

                SetTile(bld, sheet, baseIdx, index, y);
                SetTile(front, sheet, topIdx, index, y - 1);
            }
            else if (index % 4 == 1)
            {
                SetTile(bld, sheet, AnnexRimPalette.Bush, index, y);
            }
        }

        private static void PlaceWildBack(Layer back, TileSheet sheet, int x, int y)
        {
            var (wildIdx, wildType) = TerrainTileClassifier.GetWildTerrainTile(x, y);
            var tile = new StaticTile(back, sheet, BlendMode.Alpha, wildIdx);
            tile.Properties["Type"] = wildType;
            back.Tiles[x, y] = tile;
        }

        private static void SetTile(Layer layer, TileSheet sheet, int tileIndex, int x, int y)
        {
            if (layer == null || x < 0 || x >= layer.LayerWidth || y < 0 || y >= layer.LayerHeight)
                return;

            layer.Tiles[x, y] = new StaticTile(layer, sheet, BlendMode.Alpha, tileIndex);
        }

        // ----------------------------------------------------------------------
        // Decoraciones de interior propias de cada tipo de granja (TASK-902):
        // muelle de pesca y cerezo de Cuatro Esquinas.
        // ----------------------------------------------------------------------

        public static void PaintTemplateInteriors(
            AnnexTemplate template,
            AnnexSide side,
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer? alwaysFrontLayer,
            TileSheet mainSheet)
        {
            if (string.Equals(template.Id, "Fishing", StringComparison.OrdinalIgnoreCase))
            {
                // Muelle oeste (y = 19..20): tablones de madera sobre el agua con la punta
                // rematada por barandilla no transitable.
                SetTileIfValid(backLayer, mainSheet, 1, 19, AnnexRimPalette.WoodPlank);
                SetTileWithPassable(bldLayer, mainSheet, 1, 19, AnnexRimPalette.BridgeBeamOdd, "T");

                SetTileIfValid(backLayer, mainSheet, 0, 19, AnnexRimPalette.WoodPlank);
                SetTileWithPassable(bldLayer, mainSheet, 0, 19, AnnexRimPalette.RailWest, "F");

                SetTileIfValid(backLayer, mainSheet, 0, 20, 1230);
                SetTileIfValid(backLayer, mainSheet, 1, 20, 1230);

                if (side == AnnexSide.East)
                {
                    // Pasarela de madera hacia la entrada oeste (corredor sobre agua).
                    SetTileIfValid(backLayer, mainSheet, 0, 16, AnnexRimPalette.WoodPlank);
                    SetTileWithPassable(bldLayer, mainSheet, 0, 16, AnnexRimPalette.BridgeBeamEven, "T");

                    SetTileIfValid(backLayer, mainSheet, 0, 17, AnnexRimPalette.WoodPlank);
                    SetTileWithPassable(bldLayer, mainSheet, 0, 17, AnnexRimPalette.BridgeBeamOdd, "T");
                }
            }
            else if (string.Equals(template.Id, "FourCorners", StringComparison.OrdinalIgnoreCase))
            {
                // Cerezo en flor del cuadrante y rampa noroeste.
                if (alwaysFrontLayer != null)
                {
                    SetTileIfValid(alwaysFrontLayer, mainSheet, 19, 25, 45);
                    SetTileIfValid(alwaysFrontLayer, mainSheet, 20, 25, 46);
                }

                if (frontLayer != null)
                {
                    if (frontLayer.Tiles[13, 4]?.TileIndex == 416)
                        MoveCliffTopIntoCollision(bldLayer, frontLayer, mainSheet, 13, 4, 416);
                    if (frontLayer.Tiles[13, 5]?.TileIndex == 441)
                        MoveCliffTopIntoCollision(bldLayer, frontLayer, mainSheet, 13, 5, 441);
                    if (frontLayer.Tiles[10, 4]?.TileIndex == 419)
                        MoveCliffTopIntoCollision(bldLayer, frontLayer, mainSheet, 10, 4, 419);
                    if (frontLayer.Tiles[10, 5]?.TileIndex == 444)
                        MoveCliffTopIntoCollision(bldLayer, frontLayer, mainSheet, 10, 5, 444);
                }
            }
        }

        private static void MoveCliffTopIntoCollision(Layer bldLayer, Layer frontLayer, TileSheet mainSheet, int x, int y, int tileIndex)
        {
            frontLayer.Tiles[x, y] = null;
            SetTileWithPassable(bldLayer, mainSheet, x, y, tileIndex, "F");
        }

        private static void SetTileWithPassable(Layer layer, TileSheet sheet, int x, int y, int tileIndex, string passable)
        {
            if (x < 0 || x >= layer.LayerWidth || y < 0 || y >= layer.LayerHeight)
                return;

            var tile = new StaticTile(layer, sheet, BlendMode.Alpha, tileIndex);
            tile.Properties["Passable"] = passable;
            layer.Tiles[x, y] = tile;
        }

        private static void SetTileIfValid(Layer layer, TileSheet sheet, int x, int y, int tileIndex)
        {
            if (x < 0 || x >= layer.LayerWidth || y < 0 || y >= layer.LayerHeight)
                return;

            layer.Tiles[x, y] = new StaticTile(layer, sheet, BlendMode.Alpha, tileIndex);
        }

        // ----------------------------------------------------------------------
        // Estilos de entrada por tipo de granja
        // ----------------------------------------------------------------------

        /// <summary>
        /// Completa la entrada del minimapa con el detalle de su tipo: puente de madera con
        /// barandillas si el corredor de acceso cruza agua; túnel arbóreo para las forestales;
        /// desfiladero de roca para las de colina; etc. (TASK-902).
        /// </summary>
        public static void ApplyEntranceStyle(
            AnnexTemplate template,
            AnnexSide side,
            int width,
            int height,
            HashSet<(int X, int Y)> corridorTiles,
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer? alwaysFrontLayer,
            TileSheet mainSheet)
        {
            AnnexEntranceStyle style = AnnexMapDesignLogic.EntranceStyleFor(template);
            bool corridorHasWater = CorridorHasWater(corridorTiles, backLayer);

            // Regla universal: si el pasaje pisa agua, la entrada es de puente de madera.
            if (corridorHasWater)
                style = AnnexEntranceStyle.WoodBridge;

            switch (style)
            {
                case AnnexEntranceStyle.WoodBridge:
                    PaintWoodBridgeEntrance(side, width, height, corridorTiles, backLayer, bldLayer, mainSheet);
                    break;

                case AnnexEntranceStyle.ForestTunnel:
                    PaintForestTunnelEntrance(side, corridorTiles, backLayer, alwaysFrontLayer, mainSheet);
                    break;

                case AnnexEntranceStyle.RockCanyon:
                    PaintRockCanyonEntrance(side, corridorTiles, backLayer, bldLayer, frontLayer, mainSheet);
                    break;

                // DirtGate / WildGrove / RanchGate / SandShorePath: el umbral y sus flancos
                // (vallas del MarkAndDecorate o el propio margen temático) ya delimitan la
                // entrada con su estilo; no se necesita extra.
                default:
                    break;
            }
        }

        private static bool CorridorHasWater(HashSet<(int X, int Y)> corridorTiles, Layer backLayer)
        {
            foreach ((int x, int y) in corridorTiles)
            {
                Tile? t = backLayer.Tiles[x, y];
                if (t != null && (t.Properties.ContainsKey("Water") || t.TileIndexProperties.ContainsKey("Water")))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Barandillas flanqueando el pasaje de madera sobre agua, estilo puente canónico:
        /// en Norte/Sur a ambos lados del corredor vertical; en Este, a ambos lados del horizontal.
        /// </summary>
        private static void PaintWoodBridgeEntrance(
            AnnexSide side,
            int width,
            int height,
            HashSet<(int X, int Y)> corridorTiles,
            Layer backLayer,
            Layer bldLayer,
            TileSheet mainSheet)
        {
            AnnexWarpPoints pts = AnnexEntrance.For(side, width, height);
            int railIndex = side == AnnexSide.East ? AnnexRimPalette.RailEast : AnnexRimPalette.RailNorthSouth;

            if (side == AnnexSide.East)
            {
                int ey = pts.ReturnExitTile.Y;
                foreach ((int x, int _) in corridorTiles)
                {
                    SetRailIfWater(bldLayer, mainSheet, x, ey - 1, backLayer, railIndex);
                    SetRailIfWater(bldLayer, mainSheet, x, ey + 2, backLayer, railIndex);
                }
            }
            else
            {
                int ex = pts.ReturnExitTile.X;
                foreach ((int _, int y) in corridorTiles)
                {
                    SetRailIfWater(bldLayer, mainSheet, ex - 2, y, backLayer, railIndex);
                    SetRailIfWater(bldLayer, mainSheet, ex + 1, y, backLayer, railIndex);
                }
            }
        }

        private static void SetRailIfWater(Layer bldLayer, TileSheet mainSheet, int x, int y, Layer backLayer, int railIndex)
        {
            if (x < 0 || x >= bldLayer.LayerWidth || y < 0 || y >= bldLayer.LayerHeight)
                return;

            Tile? back = backLayer.Tiles[x, y];
            if (back == null || (!(back.Properties.ContainsKey("Water") || back.TileIndexProperties.ContainsKey("Water")) && back.TileIndex != 1230))
                return;

            var rail = new StaticTile(bldLayer, mainSheet, BlendMode.Alpha, railIndex);
            rail.Properties["Passable"] = "F";
            bldLayer.Tiles[x, y] = rail;
        }

        /// <summary>
        /// Túnel de bosque: copas continuas (AlwaysFront) encima y debajo del corredor y fondo
        /// de hierba del mapa forestal dentro del pasaje.
        /// </summary>
        private static void PaintForestTunnelEntrance(
            AnnexSide side,
            HashSet<(int X, int Y)> corridorTiles,
            Layer backLayer,
            Layer? alwaysFrontLayer,
            TileSheet mainSheet)
        {
            if (alwaysFrontLayer == null)
                return;

            const int CopaIndex = 968;
            const int CopaUpperIndex = 1043;

            foreach ((int x, int y) in corridorTiles)
            {
                // Hierba del mapa forestal bajo el pasaje.
                if (backLayer.Tiles[x, y] != null && !backLayer.Tiles[x, y].Properties.ContainsKey("Water"))
                    backLayer.Tiles[x, y].Properties["Type"] = "Grass";

                if (side == AnnexSide.East)
                {
                    // Corredor horizontal: copas en las filas adyacentes.
                    SetCanopyIfEmpty(alwaysFrontLayer, mainSheet, x, y - 1, CopaIndex);
                    SetCanopyIfEmpty(alwaysFrontLayer, mainSheet, x, y + 2, CopaUpperIndex);
                }
                else
                {
                    // Corredor vertical: copas en las columnas adyacentes.
                    SetCanopyIfEmpty(alwaysFrontLayer, mainSheet, x - 2, y, CopaIndex);
                    SetCanopyIfEmpty(alwaysFrontLayer, mainSheet, x + 1, y, CopaUpperIndex);
                }
            }
        }

        private static void SetCanopyIfEmpty(Layer layer, TileSheet mainSheet, int x, int y, int tileIndex)
        {
            if (x < 0 || x >= layer.LayerWidth || y < 0 || y >= layer.LayerHeight || layer.Tiles[x, y] != null)
                return;

            layer.Tiles[x, y] = new StaticTile(layer, mainSheet, BlendMode.Alpha, tileIndex);
        }

        /// <summary>
        /// Desfiladero de roca en la entrada: muros rocosos flanqueando el pasaje con remate
        /// superior en Front (talud completo).
        /// </summary>
        private static void PaintRockCanyonEntrance(
            AnnexSide side,
            HashSet<(int X, int Y)> corridorTiles,
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            TileSheet mainSheet)
        {
            foreach ((int x, int y) in corridorTiles)
            {
                if (side == AnnexSide.East)
                {
                    PlaceCliffWall(bldLayer, frontLayer, mainSheet, x, y - 1, AnnexRimPalette.CliffBaseLeft);
                    PlaceCliffWall(bldLayer, frontLayer, mainSheet, x, y + 2, AnnexRimPalette.CliffBaseRight);
                }
                else
                {
                    PlaceCliffWall(bldLayer, frontLayer, mainSheet, x - 2, y, AnnexRimPalette.CliffBaseLeft);
                    PlaceCliffWall(bldLayer, frontLayer, mainSheet, x + 1, y, AnnexRimPalette.CliffBaseRight);
                }

                if (backLayer.Tiles[x, y] != null && !backLayer.Tiles[x, y].Properties.ContainsKey("Water"))
                    backLayer.Tiles[x, y].Properties["Type"] = "Dirt";
            }
        }

        private static void PlaceCliffWall(Layer bldLayer, Layer frontLayer, TileSheet mainSheet, int x, int y, int baseIndex)
        {
            if (x < 0 || x >= bldLayer.LayerWidth || y < 0 || y >= bldLayer.LayerHeight)
                return;

            var cliff = new StaticTile(bldLayer, mainSheet, BlendMode.Alpha, baseIndex);
            cliff.Properties["Passable"] = "F";
            bldLayer.Tiles[x, y] = cliff;

            if (y - 1 >= 0 && frontLayer != null)
            {
                int topIndex = baseIndex == AnnexRimPalette.CliffBaseLeft ? AnnexRimPalette.CliffTopLeft : AnnexRimPalette.CliffTopRight;
                frontLayer.Tiles[x, y - 1] = new StaticTile(frontLayer, mainSheet, BlendMode.Alpha, topIndex);
            }
        }
    }
}
