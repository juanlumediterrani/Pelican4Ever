using System;
using System.Collections.Generic;
using Pelican4Ever.Core.World;
using xTile;
using xTile.Layers;
using xTile.Tiles;

namespace Pelican4Ever.Framework
{
    /// <summary>
    /// Paleta de índices de tiles vanilla (<c>spring_outdoorsTileSheet</c>) del pintor de
    /// márgenes de minimapas, calibrada contra el spritesheet real (auditoría visual TASK-902).
    /// </summary>
    public static class AnnexRimPalette
    {
        public const int WaterTile = 1230;
        public const int SandTile = 1269;
        public const int WoodPlank = 1244;
        public const int BridgeBeamEven = 184;
        public const int BridgeBeamOdd = 211;

        /// <summary>Arbusto bloqueante clásico del borde de granja (Buildings).</summary>
        public const int Bush = 351;

        /// <summary>Seto 2×2 completo: base 113..114 (Buildings) + copa 88..89 (Front).</summary>
        public const int HedgeBaseLeft = 113;
        public const int HedgeBaseRight = 114;
        public const int HedgeCopaLeft = 88;
        public const int HedgeCopaRight = 89;

        /// <summary>Valla vertical completa: base 436 (Buildings) y remate 361 (Front).</summary>
        public const int FenceVerticalBase = VerticalMultiTileClassifier.FenceVerticalBaseTileIndex;
        public const int FenceVerticalTop = VerticalMultiTileClassifier.FenceVerticalTopTileIndex;

        /// <summary>Valla horizontal completa: bases 383/384/385 + remates 358/359/360.</summary>
        public const int FenceLeftBase = VerticalMultiTileClassifier.FenceLeftBaseTileIndex;
        public const int FenceLeftTop = VerticalMultiTileClassifier.FenceLeftTopTileIndex;
        public const int FenceMidBase = VerticalMultiTileClassifier.FenceMidBaseTileIndex;
        public const int FenceMidTop = VerticalMultiTileClassifier.FenceMidTopTileIndex;
        public const int FenceRightBase = VerticalMultiTileClassifier.FenceRightBaseTileIndex;
        public const int FenceRightTop = VerticalMultiTileClassifier.FenceRightTopTileIndex;

        /// <summary>Tiles de masa rocosa del sheet (desfiladero de colinas), mezclados para el muro.</summary>
        public static readonly int[] RockWallTiles = { 450, 451, 475, 476, 500, 501, 525, 526, 550, 551, 575, 576 };

        /// <summary>Remates del dosel arbóreo (AlwaysFront, bosque).</summary>
        public const int CanopyLow = 968;
        public const int CanopyHigh = 1043;
    }

    /// <summary>
    /// Pintor de márgenes decorativos inalcanzables y entradas por tipo de granja para los
    /// minimapas temáticos 40×32 (TASK-902). Cada borde presenta una franja propia del tipo,
    /// con composiciones vanilla completas y sin cortes de mapa. La abertura de entrada
    /// interrumpe el margen, con estilo de entrada coherente con el tipo de granja.
    /// </summary>
    public static class AnnexBorderArtist
    {
        private const int RimDepth = 2;

        /// <summary>
        /// Construye el margen perimetral del minimapa según el estilo declarado por tipo de
        /// granja. <paramref name="skipTiles"/> contiene la abertura de entrada y su corredor.
        /// Todos los tiles del margen (salvo la abertura) quedan no transitables.
        /// </summary>
        public static void BuildRim(
            AnnexTemplate template,
            AnnexSide side,
            int width,
            int height,
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer? alwaysFrontLayer,
            Layer? bld2Layer,
            Layer? alwaysFront2Layer,
            Layer? alwaysFront3Layer,
            TileSheet mainSheet,
            HashSet<(int X, int Y)> skipTiles)
        {
            var styles = AnnexMapDesignLogic.RimStyles(template.Behavior, template.Id);

            // 1. Limpieza de TODAS las bandas perimetrales (restos del recorte vanilla truncados).
            foreach ((int x, int y) in AnnexMapDesignLogic.EnumerateRimTiles(width, height, RimDepth))
            {
                if (skipTiles.Contains((x, y)))
                    continue;

                bldLayer.Tiles[x, y] = null;
                frontLayer.Tiles[x, y] = null;
                backLayer.Tiles[x, y] = null;
                if (alwaysFrontLayer != null) alwaysFrontLayer.Tiles[x, y] = null;
                if (bld2Layer != null) bld2Layer.Tiles[x, y] = null;
                if (alwaysFront2Layer != null) alwaysFront2Layer.Tiles[x, y] = null;
                if (alwaysFront3Layer != null) alwaysFront3Layer.Tiles[x, y] = null;
            }

            // 2. Bordes horizontales (N y S): franjas de anchura completa.
            for (int x = 0; x < width; x++)
            {
                PaintHorizontalStrip(styles.North, width, height, topMost: true, skipTiles, x, backLayer, bldLayer, frontLayer, alwaysFrontLayer, mainSheet);
                PaintHorizontalStrip(styles.South, width, height, topMost: false, skipTiles, x, backLayer, bldLayer, frontLayer, alwaysFrontLayer, mainSheet);
            }

            // 3. Bordes verticales (O y E) entre las franjas ya pintadas.
            for (int y = RimDepth; y < height - RimDepth; y++)
            {
                PaintVerticalStrip(styles.West, width, height, leftMost: true, skipTiles, y, backLayer, bldLayer, frontLayer, mainSheet);
                PaintVerticalStrip(styles.East, width, height, leftMost: false, skipTiles, y, backLayer, bldLayer, frontLayer, mainSheet);
            }

            // 4. Sellado explícito del margen: cada casilla reservada queda no transitable,
            //    respaldada por la decoración pintada (sin barreras invisibles).
            foreach ((int x, int y) in AnnexMapDesignLogic.EnumerateRimTiles(width, height, RimDepth))
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
        /// Pinta la franja de un borde horizontal completada con sus dos bandas para la
        /// columna <paramref name="x"/>. outerRow = 0 (Norte) o height-1 (Sur);
        /// innerRow = 1 o height-2. En composiciones híbridas (base+copa), la base se sitúa en
        /// la fila exterior y la copa superpone hacia dentro.
        /// </summary>
        private static void PaintHorizontalStrip(
            AnnexRimStyle style,
            int width,
            int height,
            bool topMost,
            HashSet<(int X, int Y)> skipTiles,
            int x,
            Layer back,
            Layer bld,
            Layer front,
            Layer? alwaysFront,
            TileSheet sheet)
        {
            int outerRow = topMost ? 0 : height - 1;
            int innerRow = topMost ? 1 : height - 2;

            switch (style)
            {
                case AnnexRimStyle.RiverBank or AnnexRimStyle.OceanShore:
                    // Banda exterior: agua total. Banda interior: orilla (hierba o arena).
                    if (!skipTiles.Contains((x, outerRow)))
                        PutWater(back, sheet, x, outerRow);
                    if (!skipTiles.Contains((x, innerRow)))
                        PaintShoreCell(x, innerRow, back, bld, sheet, style == AnnexRimStyle.OceanShore);
                    break;

                case AnnexRimStyle.RockyCanyon:
                    // Banda exterior: masa de taludes completos (base + remate Front).
                    if (!skipTiles.Contains((x, outerRow)) && !skipTiles.Contains((x, innerRow)))
                    {
                        PlaceWildBack(back, sheet, x, innerRow);
                        PlaceCliffPair(bld, front, sheet, x, outerRow);
                    }
                    break;

                case AnnexRimStyle.RanchFence:
                    // Banda interior: valla horizontal continua; banda exterior: pasto con arbustos.
                    if (!skipTiles.Contains((x, outerRow)) && !skipTiles.Contains((x, innerRow)))
                    {
                        PlaceWildBack(back, sheet, x, outerRow);
                        if (x % 4 == 1)
                            SetTile(bld, sheet, AnnexRimPalette.Bush, x, outerRow);
                        PlaceFencePair(bld, front, sheet, x, innerRow);
                    }
                    break;

                default:
                    // Setos/bosque/espinos: masa continua base+copa en la banda exterior, hierba brillante en la interior.
                    if (!skipTiles.Contains((x, outerRow)) && !skipTiles.Contains((x, innerRow)))
                    {
                        PlaceWildBack(back, sheet, x, innerRow);
                        PaintHedgeColumn(baseRow: outerRow, copaRow: innerRow, x, back, bld, front, sheet, style);
                    }
                    break;
            }

            _ = width;
            _ = alwaysFront;
        }

        /// <summary>
        /// Pinta la franja de un borde vertical para la fila <paramref name="y"/>. outerCol =
        /// 0 (Oeste) o width-1 (Este); innerCol = 1 o width-2. Las composiciones verticales
        /// usan base en la columna exterior y copa en Front de la misma columna (fila - 1).
        /// </summary>
        private static void PaintVerticalStrip(
            AnnexRimStyle style,
            int width,
            int height,
            bool leftMost,
            HashSet<(int X, int Y)> skipTiles,
            int y,
            Layer back,
            Layer bld,
            Layer front,
            TileSheet sheet)
        {
            int outerCol = leftMost ? 0 : width - 1;
            int innerCol = leftMost ? 1 : width - 2;

            switch (style)
            {
                case AnnexRimStyle.RiverBank or AnnexRimStyle.OceanShore:
                    if (!skipTiles.Contains((outerCol, y)))
                        PutWater(back, sheet, outerCol, y);
                    if (!skipTiles.Contains((innerCol, y)))
                        PaintShoreCell(innerCol, y, back, bld, sheet, style == AnnexRimStyle.OceanShore);
                    break;

                case AnnexRimStyle.RockyCanyon:
                    if (!skipTiles.Contains((outerCol, y)) && !skipTiles.Contains((innerCol, y)))
                    {
                        PlaceWildBack(back, sheet, innerCol, y);
                        PlaceCliffVerticalPair(bld, front, sheet, outerCol, y);
                    }
                    break;

                case AnnexRimStyle.RanchFence:
                    if (!skipTiles.Contains((outerCol, y)) && !skipTiles.Contains((innerCol, y)))
                    {
                        PlaceWildBack(back, sheet, innerCol, y);
                        PlaceWildBack(back, sheet, outerCol, y);
                        if (y % 3 != 0)
                            PlaceVerticalFencePair(bld, front, sheet, outerCol, y);
                    }
                    break;

                default:
                    if (!skipTiles.Contains((outerCol, y)) && !skipTiles.Contains((innerCol, y)))
                    {
                        PlaceWildBack(back, sheet, innerCol, y);
                        PlaceWildBack(back, sheet, outerCol, y);
                        int mod = style == AnnexRimStyle.WildThorns ? 3 : 4;
                        if (y % mod != 0)
                            SetTile(bld, sheet, AnnexRimPalette.Bush, outerCol, y);
                    }
                    break;
            }

            _ = height;
        }

        // ------------------------------------------------------------------
        // Composiciones (todas completas: ninguna cruzará el límite del mapa)
        // ------------------------------------------------------------------

        /// <summary>Hilera híbrida continuada en un borde horizontal: seto 2×2 en múltiplos de 4
        /// y arbusto de un casillo en el resto; Back salvaje en la fila de base entre remates.</summary>
        private static void PaintHedgeColumn(int baseRow, int copaRow, int x, Layer back, Layer bld, Layer front, TileSheet sheet, AnnexRimStyle style)
        {
            bool forest = style == AnnexRimStyle.ForestEdge;
            bool thorns = style == AnnexRimStyle.WildThorns;
            int mod = thorns ? 3 : 4;

            PlaceWildBack(back, sheet, x, baseRow);
            if (x % mod == 0)
            {
                // Seto 2×2 completo: base (Buildings) en la fila de base, copa (Front) en la copaRow.
                bool left = x % 8 < mod / 2;
                SetTile(bld, sheet, left ? AnnexRimPalette.HedgeBaseLeft : AnnexRimPalette.HedgeBaseRight, x, baseRow);
                SetTile(front, sheet, left ? AnnexRimPalette.HedgeCopaLeft : AnnexRimPalette.HedgeCopaRight, x, copaRow);
            }
            else
            {
                SetTile(bld, sheet, AnnexRimPalette.Bush, x, baseRow);
            }

            // Bosque: tramos de dosel extras proyectados sobre la banda exterior.
            if (forest && x % 5 == 2)
                SetTile(bld, sheet, AnnexRimPalette.Bush, x, copaRow);
        }

        /// <summary>Talud doble horizontal en la fila exterior: base en Buildings y remate en Front.</summary>
        private static void PlaceCliffPair(Layer bld, Layer front, TileSheet sheet, int x, int baseRow)
        {
            bool leftTile = x % 2 == 0;
            SetTile(bld, sheet, leftTile ? AnnexRimPalette.CliffBaseLeft : AnnexRimPalette.CliffBaseRight, x, baseRow);
            SetTile(front, sheet, leftTile ? AnnexRimPalette.CliffTopLeft : AnnexRimPalette.CliffTopRight, x, baseRow - 1);
        }

        /// <summary>Talud vertical en la columna exterior: base en Buildings y remate en Front (y - 1).</summary>
        private static void PlaceCliffVerticalPair(Layer bld, Layer front, TileSheet sheet, int x, int y)
        {
            bool upper = (y / 2) % 2 == 0;
            SetTile(bld, sheet, upper ? AnnexRimPalette.CliffBaseLeft : AnnexRimPalette.CliffBaseRight, x, y);
            SetTile(front, sheet, upper ? AnnexRimPalette.CliffTopLeft : AnnexRimPalette.CliffTopRight, x, y - 1);
        }

        /// <summary>Valla horizontal: base 383/384/385 en la fila indicada + remate en Front fila - 1.</summary>
        private static void PlaceFencePair(Layer bld, Layer front, TileSheet sheet, int x, int baseRow)
        {
            int baseIdx;
            int topIdx;
            if (x % 7 == 0)
            {
                baseIdx = AnnexRimPalette.FenceLeftBase;
                topIdx = AnnexRimPalette.FenceLeftTop;
            }
            else if (x % 7 == 6)
            {
                baseIdx = AnnexRimPalette.FenceRightBase;
                topIdx = AnnexRimPalette.FenceRightTop;
            }
            else
            {
                baseIdx = AnnexRimPalette.FenceMidBase;
                topIdx = AnnexRimPalette.FenceMidTop;
            }

            SetTile(bld, sheet, baseIdx, x, baseRow);
            SetTile(front, sheet, topIdx, x, baseRow - 1);
        }

        /// <summary>Valla vertical: base 436 en la fila indicada + remate 361 en Front (y - 1).</summary>
        private static void PlaceVerticalFencePair(Layer bld, Layer front, TileSheet sheet, int x, int y)
        {
            SetTile(bld, sheet, AnnexRimPalette.FenceVerticalBase, x, y);
            SetTile(front, sheet, AnnexRimPalette.FenceVerticalTop, x, y - 1);
        }

        /// <summary>Orilla de una banda de agua: hierba salvaje (río) o arena con rocas (playa).</summary>
        private static void PaintShoreCell(int x, int y, Layer back, Layer bld, TileSheet sheet, bool ocean)
        {
            var (wildIdx, wildType) = TerrainTileClassifier.GetWildTerrainTile(x, y);
            var tile = new StaticTile(back, sheet, BlendMode.Alpha, ocean ? AnnexRimPalette.SandTile : wildIdx);
            tile.Properties["Type"] = ocean ? "Dirt" : wildType;
            back.Tiles[x, y] = tile;

            if (x % 5 == 4)
                SetTile(bld, sheet, AnnexRimPalette.Bush, x, y);
        }

        // ------------------------------------------------------------------
        // Utiles
        // ------------------------------------------------------------------

        private static void PutWater(Layer back, TileSheet sheet, int x, int y)
        {
            var tile = new StaticTile(back, sheet, BlendMode.Alpha, AnnexRimPalette.WaterTile);
            tile.Properties["Type"] = "Water";
            back.Tiles[x, y] = tile;
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
                // Muelle oeste (y = 19): tablones de madera sobre el agua con la punta rematada
                // por valla vertical completa no transitable.
                SetTileIfValid(backLayer, mainSheet, 1, 19, AnnexRimPalette.WoodPlank);
                SetTileWithPassable(bldLayer, mainSheet, 1, 19, AnnexRimPalette.BridgeBeamOdd, "T");

                SetTileIfValid(backLayer, mainSheet, 0, 19, AnnexRimPalette.WoodPlank);
                SetTileWithPassable(bldLayer, mainSheet, 0, 19, AnnexRimPalette.FenceVerticalBase, "F");
                SetTileIfValid(frontLayer, mainSheet, 0, 18, AnnexRimPalette.FenceVerticalTop);

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
                // Cerezo en flor del cuadrante mixto y rampa noroeste.
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

        private static void SetTileIfValid(Layer layer, TileSheet sheet, int x, int y, int tileIndex)
        {
            if (layer == null || x < 0 || x >= layer.LayerWidth || y < 0 || y >= layer.LayerHeight)
                return;

            layer.Tiles[x, y] = new StaticTile(layer, sheet, BlendMode.Alpha, tileIndex);
        }

        private static void SetTileWithPassable(Layer layer, TileSheet sheet, int x, int y, int tileIndex, string passable)
        {
            if (layer == null || x < 0 || x >= layer.LayerWidth || y < 0 || y >= layer.LayerHeight)
                return;

            var tile = new StaticTile(layer, sheet, BlendMode.Alpha, tileIndex);
            tile.Properties["Passable"] = passable;
            layer.Tiles[x, y] = tile;
        }

        // ----------------------------------------------------------------------
        // Estilos de entrada por tipo de granja
        // ----------------------------------------------------------------------

        /// <summary>
        /// Completa la entrada del minimapa con el detalle de su tipo: puente de madera si el
        /// corredor de acceso cruza agua; túnel arbóreo para las forestales; desfiladero de
        /// roca para las de colina; etc. (TASK-902).
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
                case AnnexEntranceStyle.ForestTunnel:
                    PaintForestTunnelEntrance(side, corridorTiles, backLayer, alwaysFrontLayer, mainSheet);
                    break;

                case AnnexEntranceStyle.RockCanyon:
                    PaintRockCanyonEntrance(side, corridorTiles, backLayer, bldLayer, frontLayer, mainSheet);
                    break;

                // WoodBridge: el corredor ya cruza con tablones+vigas y el margen de agua enmarca
                // la abertura; con vueltas-a-puertas de río no se añade más para evitar ornamentos
                // extraños. DirtGate/WildGrove/RanchGate/SandShorePath quedan delimitados por el
                // propio margen temático y el umbral del MarkAndDecorateEntranceDoorway.
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
        /// Túnel de bosque: copas continuas (AlwaysFront) en las filas/columnas flanqueando el
        /// corredor y fondo de hierba del mapa forestal dentro del pasaje.
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

            foreach ((int x, int y) in corridorTiles)
            {
                // Hierba del mapa forestal bajo el pasaje.
                if (backLayer.Tiles[x, y] != null && !backLayer.Tiles[x, y].Properties.ContainsKey("Water"))
                    backLayer.Tiles[x, y].Properties["Type"] = "Grass";

                if (side == AnnexSide.East)
                {
                    // Corredor horizontal: copas en las filas adyacentes.
                    SetCanopyIfEmpty(alwaysFrontLayer, mainSheet, x, y - 1, AnnexRimPalette.CanopyLow);
                    SetCanopyIfEmpty(alwaysFrontLayer, mainSheet, x, y + 2, AnnexRimPalette.CanopyHigh);
                }
                else
                {
                    // Corredor vertical: copas en las columnas adyacentes.
                    SetCanopyIfEmpty(alwaysFrontLayer, mainSheet, x - 2, y, AnnexRimPalette.CanopyLow);
                    SetCanopyIfEmpty(alwaysFrontLayer, mainSheet, x + 1, y, AnnexRimPalette.CanopyHigh);
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
