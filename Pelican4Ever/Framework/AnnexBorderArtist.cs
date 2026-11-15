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

        /// <summary>Seto 2×2 completo: base 113+114 (Buildings) con copa 88+89 (Front) encima.</summary>
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

            // 1. Limpieza de TODAS las bandas perimetrales y de TODAS las capas del recorte
            //    (el vanilla puede llevar Front2/Buildings2/AlwaysFront2..3/Back2 con restos truncados).
            foreach ((int x, int y) in AnnexMapDesignLogic.EnumerateRimTiles(width, height, RimDepth))
            {
                if (skipTiles.Contains((x, y)))
                    continue;

                foreach (var layer in backLayer.Map.Layers)
                {
                    if (string.Equals(layer.Id, "Back", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(layer.Id, "Paths", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    layer.Tiles[x, y] = null;
                }

                backLayer.Tiles[x, y] = null;
            }

            // 2. Bordes horizontales (N y S): franja de anchura completa por columna.
            for (int x = 0; x < width; x++)
            {
                PaintHorizontalStrip(styles.North, topMost: true, skipTiles, x, backLayer, bldLayer, frontLayer, mainSheet);
                PaintHorizontalStrip(styles.South, topMost: false, skipTiles, x, backLayer, bldLayer, frontLayer, mainSheet);
            }

            // 3. Bordes verticales (O y E) entre las franjas ya pintadas.
            for (int y = RimDepth; y < height - RimDepth; y++)
            {
                PaintVerticalStrip(styles.West, width, leftMost: true, skipTiles, y, backLayer, bldLayer, frontLayer, mainSheet);
                PaintVerticalStrip(styles.East, width, leftMost: false, skipTiles, y, backLayer, bldLayer, frontLayer, mainSheet);
            }

            // 4. Sellado explícito del margen: cada casilla reservada no transitable,
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
        /// Franja de un borde horizontal para la columna <paramref name="x"/>: banda exterior
        /// (fila del borde) + banda interior (fila contigua del margen).
        /// </summary>
        private static void PaintHorizontalStrip(
            AnnexRimStyle style,
            bool topMost,
            HashSet<(int X, int Y)> skipTiles,
            int x,
            Layer back,
            Layer bld,
            Layer front,
            TileSheet sheet)
        {
            int outerRow = topMost ? 0 : 31;
            int innerRow = topMost ? 1 : 30;

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
                    // Banda exterior: muro de roca continuo con suelo bajo él; banda interior: hierba salvaje.
                    if (!skipTiles.Contains((x, outerRow)) && !skipTiles.Contains((x, innerRow)))
                    {
                        PlaceWildBack(back, sheet, x, innerRow);
                        PlaceWildBack(back, sheet, x, outerRow);
                        PlaceRockWall(bld, sheet, x, outerRow);
                    }
                    break;

                case AnnexRimStyle.RanchFence:
                    // Banda interior: valla horizontal continua con pasto opaco detrás
                    // (175, sin flores translúcidas); banda exterior: pasto con arbustos sueltos.
                    if (!skipTiles.Contains((x, outerRow)) && !skipTiles.Contains((x, innerRow)))
                    {
                        PlaceOpaqueGrass(back, sheet, x, outerRow);
                        PlaceOpaqueGrass(back, sheet, x, innerRow);
                        if (x % 4 == 1)
                            SetTile(bld, sheet, AnnexRimPalette.Bush, x, outerRow);
                        PlaceFencePair(bld, front, sheet, x, innerRow);
                    }
                    break;

                default:
                    // Setos/bosque/espinos: masa continua en la banda exterior (setos 2×2 con
                    // pares adyacentes y arbustos entre restos) + hierba salvaje en la interior.
                    // En el borde norte la base se asienta en la fila 1 y su copa (Front) en la
                    // MISMA fila: el sprite 16×32 cubre las filas 0..1 sin cortarse en y=0.
                    // La fila 0 se rellena igualmente con Back salvaje (suelo bajo el seto,
                    // nunca transparente).
                    if (!skipTiles.Contains((x, outerRow)) && !skipTiles.Contains((x, innerRow)))
                    {
                        if (topMost)
                        {
                            PlaceWildBack(back, sheet, x, outerRow);
                            PaintHedgeColumn(1, 1, x, back, bld, front, sheet, style);
                        }
                        else
                        {
                            PlaceWildBack(back, sheet, x, innerRow);
                            PaintHedgeColumn(outerRow, innerRow, x, back, bld, front, sheet, style);
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// Franja de un borde vertical para la fila <paramref name="y"/>: banda exterior
        /// (columna del borde) + banda interior (columna contigua del margen).
        /// </summary>
        private static void PaintVerticalStrip(
            AnnexRimStyle style,
            int width,
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
                        PlaceWildBack(back, sheet, outerCol, y);
                        PlaceRockWall(bld, sheet, outerCol, y);
                    }
                    break;

                case AnnexRimStyle.RanchFence:
                    if (!skipTiles.Contains((outerCol, y)) && !skipTiles.Contains((innerCol, y)))
                    {
                        PlaceOpaqueGrass(back, sheet, innerCol, y);
                        PlaceOpaqueGrass(back, sheet, outerCol, y);
                        PlaceVerticalFencePair(bld, front, sheet, outerCol, y);
                    }
                    break;

                default:
                    // Bordes verticales: masa continua de arbustos sin calvas, con seto 2×2
                    // (dos columnas: interior+exterior) cada 4 filas.
                    if (!skipTiles.Contains((outerCol, y)) && !skipTiles.Contains((innerCol, y)))
                    {
                        PlaceWildBack(back, sheet, innerCol, y);
                        PlaceWildBack(back, sheet, outerCol, y);
                        if (y % 4 == 0)
                        {
                            // Seto 2×2 completo de dos columnas (interior y exterior) en esta fila.
                            SetTile(bld, sheet, AnnexRimPalette.HedgeBaseLeft, innerCol, y);
                            SetTile(bld, sheet, AnnexRimPalette.HedgeBaseRight, outerCol, y);
                            SetTile(front, sheet, AnnexRimPalette.HedgeCopaLeft, innerCol, y - 1);
                            SetTile(front, sheet, AnnexRimPalette.HedgeCopaRight, outerCol, y - 1);
                        }
                        else
                        {
                            SetTile(bld, sheet, AnnexRimPalette.Bush, outerCol, y);
                            SetTile(bld, sheet, AnnexRimPalette.Bush, innerCol, y);
                        }
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Composiciones (todas completas: ningún sprite cruza el límite del mapa)
        // ------------------------------------------------------------------

        /// <summary>
        /// Columna del seto en un borde horizontal: seto 2×2 completo (par de bases y par de
        /// copas en columnas adyacentes) en múltiplos de 4, y arbusto clásico el resto.
        /// </summary>
        private static void PaintHedgeColumn(int baseRow, int copaRow, int x, Layer back, Layer bld, Layer front, TileSheet sheet, AnnexRimStyle style)
        {
            bool thorns = style == AnnexRimStyle.WildThorns;
            int mod = thorns ? 3 : 4;

            PlaceWildBack(back, sheet, x, baseRow);
            if (x % mod == 0)
            {
                // Mitad IZQUIERDA del seto 2×2 (la fila múltiplo abre el par).
                SetTile(bld, sheet, AnnexRimPalette.HedgeBaseLeft, x, baseRow);
                SetTile(front, sheet, AnnexRimPalette.HedgeCopaLeft, x, copaRow);
            }
            else if (x % mod == 1)
            {
                // Mitad DERECHA del seto: cierra el par iniciado en x - 1.
                SetTile(bld, sheet, AnnexRimPalette.HedgeBaseRight, x, baseRow);
                SetTile(front, sheet, AnnexRimPalette.HedgeCopaRight, x, copaRow);
            }
            else
            {
                SetTile(bld, sheet, AnnexRimPalette.Bush, x, baseRow);
            }
        }

        /// <summary>Muro de roca: mezcla determinista de la masa de piedras del sheet.</summary>
        private static void PlaceRockWall(Layer bld, TileSheet sheet, int x, int y)
        {
            int idx = AnnexRimPalette.RockWallTiles[((x * 7 + y * 13) & 0x7FFFFFFF) % AnnexRimPalette.RockWallTiles.Length];
            SetTile(bld, sheet, idx, x, y);
        }

        /// <summary>Valla horizontal: base 383/384/385 en la fila indicada + remate 358/359/360 en Front (fila − 1).</summary>
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

        /// <summary>Valla vertical: base 436 en la fila indicada + remate 361 en Front (fila − 1).</summary>
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
        // Útiles
        // ------------------------------------------------------------------

        private static void PutWater(Layer back, TileSheet sheet, int x, int y)
        {
            var tile = new StaticTile(back, sheet, BlendMode.Alpha, AnnexRimPalette.WaterTile);
            tile.Properties["Type"] = "Water";
            back.Tiles[x, y] = tile;
        }

        /// <summary>Pasto opaco estándar (175) para bandas bajo vallas: sin flores translúcidas.</summary>
        private static void PlaceOpaqueGrass(Layer back, TileSheet sheet, int x, int y)
        {
            var tile = new StaticTile(back, sheet, BlendMode.Alpha, 175);
            tile.Properties["Type"] = "Grass";
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
        // Decoraciones de interior propias de cada tipo (TASK-902): muelle de pesca
        // y cerezo de Cuatro Esquinas.
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
                // Cerezo en flor del cuadrante mixto: copas flanqueando el corredor (18/21) para
                // no ensuciar el pasaje de entrada.
                if (alwaysFrontLayer != null)
                {
                    SetTileIfValid(alwaysFrontLayer, mainSheet, 18, 25, 45);
                    SetTileIfValid(alwaysFrontLayer, mainSheet, 21, 25, 46);
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
        /// corredor cruza agua; túnel arbóreo para las forestales; desfiladero de roca para las
        /// de colina; etc. (TASK-902).
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
            bool rockTemplate = style == AnnexEntranceStyle.RockCanyon;

            // Regla universal: si el pasaje pisa agua, la entrada es de puente de madera.
            if (corridorHasWater)
                style = AnnexEntranceStyle.WoodBridge;

            switch (style)
            {
                case AnnexEntranceStyle.ForestTunnel:
                    PaintForestTunnelEntrance(side, corridorTiles, backLayer, alwaysFrontLayer, mainSheet);
                    break;

                // WoodBridge / DirtGate / WildGrove / RanchGate / SandShorePath: el corredor ya
                // dispone de tablones+vigas sobre agua o del umbral del
                // MarkAndDecorateEntranceDoorway; el resto lo aporta el margen temático.
                default:
                    break;
            }

            // Desfiladero de colinas: muros rocosos flanqueantes aunque el corredor cruce
            // agua (pasarela de madera sobre la charca con orillas de cantera).
            if (rockTemplate)
                PaintRockCanyonEntrance(side, corridorTiles, backLayer, bldLayer, mainSheet);
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
        /// Túnel de bosque: copas (AlwaysFront) flanqueando el corredor por su exterior
        /// (columnas o filas contiguas, NUNCA dentro del pasaje) y fondo de hierba dentro.
        /// </summary>
        private static void PaintForestTunnelEntrance(
            AnnexSide side,
            HashSet<(int X, int Y)> corridorTiles,
            Layer backLayer,
            Layer? alwaysFrontLayer,
            TileSheet mainSheet)
        {
            if (alwaysFrontLayer == null || corridorTiles.Count == 0)
                return;

            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            foreach ((int x, int y) in corridorTiles)
            {
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }

            foreach ((int x, int y) in corridorTiles)
            {
                // Hierba del mapa forestal bajo el pasaje.
                if (backLayer.Tiles[x, y] != null && !backLayer.Tiles[x, y].Properties.ContainsKey("Water"))
                    backLayer.Tiles[x, y].Properties["Type"] = "Grass";
            }

            void Canopy(int cx, int cy)
            {
                if (cx < 0 || cx >= alwaysFrontLayer.LayerWidth || cy < 0 || cy >= alwaysFrontLayer.LayerHeight || corridorTiles.Contains((cx, cy)))
                    return;

                if (alwaysFrontLayer.Tiles[cx, cy] == null)
                    alwaysFrontLayer.Tiles[cx, cy] = new StaticTile(alwaysFrontLayer, mainSheet, BlendMode.Alpha, AnnexRimPalette.CanopyLow);
            }

            if (side == AnnexSide.East)
            {
                // Corredor horizontal: copas en las filas contiguas por fuera (N y S del pasaje).
                for (int x = minX; x <= maxX; x++)
                {
                    Canopy(x, minY - 1);
                    Canopy(x, maxY + 1);
                }
            }
            else
            {
                // Corredor vertical: copas en las columnas contiguas por fuera (W y E del pasaje).
                for (int y = minY; y <= maxY; y++)
                {
                    Canopy(minX - 1, y);
                    Canopy(maxX + 1, y);
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
        /// Desfiladero de roca en la entrada: muros rocosos flanqueando el pasaje (Buildings,
        /// no transitables) en las columnas/filas contiguas al corredor, sin pisarlo.
        /// </summary>
        private static void PaintRockCanyonEntrance(
            AnnexSide side,
            HashSet<(int X, int Y)> corridorTiles,
            Layer backLayer,
            Layer bldLayer,
            TileSheet mainSheet)
        {
            if (corridorTiles.Count == 0)
                return;

            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            foreach ((int x, int y) in corridorTiles)
            {
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }

            void Wall(int wx, int wy)
            {
                if (wx < 0 || wx >= bldLayer.LayerWidth || wy < 0 || wy >= bldLayer.LayerHeight)
                    return;

                bool inCorridor = corridorTiles.Contains((wx, wy));
                if (inCorridor)
                    return;

                PlaceRockWall(bldLayer, mainSheet, wx, wy);
            }

            if (side == AnnexSide.East)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Wall(x, minY - 1);
                    Wall(x, maxY + 1);
                }
            }
            else
            {
                for (int y = minY; y <= maxY; y++)
                {
                    Wall(minX - 1, y);
                    Wall(maxX + 1, y);
                }
            }

            foreach ((int x, int y) in corridorTiles)
            {
                if (backLayer.Tiles[x, y] != null && !backLayer.Tiles[x, y].Properties.ContainsKey("Water"))
                    backLayer.Tiles[x, y].Properties["Type"] = "Dirt";
            }
        }
    }
}
