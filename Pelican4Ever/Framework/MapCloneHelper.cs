using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Pelican4Ever.Core.World;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.ObjectModel;
using xTile.Tiles;

namespace Pelican4Ever.Framework
{
    public static class MapCloneHelper
    {
        private static readonly FieldInfo? TileIndexPropertiesCacheField = typeof(TileIndexPropertyAccessor)
            .GetField("m_cache", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly HashSet<int> BridgeBuildingsTileIndices = new()
        {
            183, 184, 185, 211, 212, 283, 284, 809
        };

        private static readonly HashSet<int> WoodPlankBackTileIndices = new()
        {
            1244, 1243, 833, 848, 859, 860, 864, 866, 867, 880, 882, 883, 885
        };

        private static readonly HashSet<int> OpenPathBackTileIndices = new()
        {
            325, 326, 327, 350, 351, 352, 401, 736, 737, 738, 768, 769, 770, 838, 1964, 1965, 1966
        };
        private static readonly HashSet<string> ExcludedAnnexMapProperties = new(StringComparer.OrdinalIgnoreCase)
        {
            "Warp",
            "SpouseAreaLocation",
            "GrandpaShrineLocation",
            "FarmHouseEntry",
            "ShippingBinLocation",
            "PetBowlLocation",
            "GreenhouseLocation",
            "WarpTotemEntry",
            "SpawnMountainFarmOreRect",
            "SpawnForestFarmForage",
            "FarmFishLocationOverride"
        };

        public static TileSheet? GetOutdoorsTileSheet(Map? map)
        {
            if (map == null) return null;
            return map.GetTileSheet("untitled tile sheet")
                ?? map.GetTileSheet("outdoors")
                ?? map.TileSheets.FirstOrDefault(ts => ts.ImageSource.Contains("outdoors", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Crea una copia profunda e independiente de un mapa xTile, asegurando que ninguna modificación
        /// posterior afecte al asset original almacenado en la caché de SMAPI o del juego.
        /// </summary>
        public static Map CloneMap(Map source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            Map target = new Map(source.Id)
            {
                Description = source.Description
            };

            foreach (var prop in source.Properties)
            {
                target.Properties[prop.Key] = prop.Value;
            }

            CopyTileSheets(source, target);

            foreach (var layer in source.Layers)
            {
                var newLayer = new Layer(layer.Id, target, layer.LayerSize, layer.TileSize)
                {
                    Description = layer.Description,
                    Visible = layer.Visible
                };

                foreach (var prop in layer.Properties)
                {
                    newLayer.Properties[prop.Key] = prop.Value;
                }

                for (int x = 0; x < layer.LayerWidth; x++)
                {
                    for (int y = 0; y < layer.LayerHeight; y++)
                    {
                        var t = layer.Tiles[x, y];
                        if (t != null)
                        {
                            newLayer.Tiles[x, y] = CloneTile(t, newLayer, target);
                        }
                    }
                }
                target.AddLayer(newLayer);
            }

            return target;
        }

        /// <summary>
        /// Genera el mapa determinista para un mapa puente (35×30) recortando el viewport visible (10, 0, 35, 30)
        /// de Maps/BusStop, limpiando el autobús averiado y arbustos centrales, sellando el perímetro y tallando
        /// los pasos activos este y oeste con sus warps declarativos (TASK-1202).
        /// </summary>
        public static Map CreateBridgeMap(Map busStopSource, PlazaLocation bridgeLoc)
        {
            if (busStopSource == null)
                throw new ArgumentNullException(nameof(busStopSource));
            if (bridgeLoc == null)
                throw new ArgumentNullException(nameof(bridgeLoc));

            int width = bridgeLoc.Width;   // 35
            int height = bridgeLoc.Height; // 30
            int srcX = 10;
            int srcY = 0;

            Map target = new Map(bridgeLoc.Name)
            {
                Description = $"Plaza Bridge ({bridgeLoc.Name})"
            };

            // 1. Conservar Outdoors = "T" y eliminar propiedades que no aplican a la plaza
            foreach (var prop in busStopSource.Properties)
            {
                if (!string.Equals(prop.Key, "Warp", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(prop.Key, "Doors", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(prop.Key, "NPCWarp", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(prop.Key, "ViewportClamp", StringComparison.OrdinalIgnoreCase)
                    && !prop.Key.EndsWith("_Objects", StringComparison.OrdinalIgnoreCase))
                {
                    target.Properties[prop.Key] = prop.Value;
                }
            }

            target.Properties["Outdoors"] = "T";

            // 2. Copiar TileSheets
            CopyTileSheets(busStopSource, target);
            TileSheet? outdoorsSheet = GetOutdoorsTileSheet(target) ?? target.TileSheets.FirstOrDefault();

            // 3. Recortar 35×30 desde (10, 0) y limpiar Action/TouchAction de todos los tiles
            Size tileSize = busStopSource.Layers.Count > 0 ? busStopSource.Layers[0].TileSize : new Size(64, 64);
            foreach (var layer in busStopSource.Layers)
            {
                var newLayer = new Layer(layer.Id, target, new Size(width, height), layer.TileSize)
                {
                    Description = layer.Description,
                    Visible = layer.Visible
                };

                foreach (var prop in layer.Properties)
                {
                    newLayer.Properties[prop.Key] = prop.Value;
                }

                for (int x = 0; x < width; x++)
                {
                    int sx = srcX + x;
                    if (sx < 0 || sx >= layer.LayerWidth)
                        continue;

                    for (int y = 0; y < height; y++)
                    {
                        int sy = srcY + y;
                        if (sy < 0 || sy >= layer.LayerHeight)
                            continue;

                        var t = layer.Tiles[sx, sy];
                        if (t != null)
                        {
                            Tile cloned = CloneTile(t, newLayer, target);
                            cloned.Properties.Remove("Action");
                            cloned.Properties.Remove("TouchAction");
                            newLayer.Tiles[x, y] = cloned;
                        }
                    }
                }

                target.AddLayer(newLayer);
            }

            Layer backLayer = EnsureLayer(target, "Back", width, height, tileSize);
            Layer bldLayer = EnsureLayer(target, "Buildings", width, height, tileSize);
            Layer frontLayer = EnsureLayer(target, "Front", width, height, tileSize);
            Layer alwaysFrontLayer = EnsureLayer(target, "AlwaysFront", width, height, tileSize);
            EnsureLayer(target, "Paths", width, height, tileSize);

            Layer? bld2Layer = target.GetLayer("Buildings2");
            Layer? alwaysFront2Layer = target.GetLayer("AlwaysFront2");
            Layer? alwaysFront3Layer = target.GetLayer("AlwaysFront3");

            // 4. Despejar únicamente la franja del autobús averiado y la máquina de billetes (x = 7..19, y = 6..11)
            // en las capas elevadas, dejando 100% enteros y sin cortar los cerezos laterales (x = 0..7 y x = 28..34).
            for (int x = 7; x <= 19; x++)
            {
                for (int y = 6; y <= 11; y++)
                {
                    bldLayer.Tiles[x, y] = null;
                    if (bld2Layer != null) bld2Layer.Tiles[x, y] = null;
                    frontLayer.Tiles[x, y] = null;
                    alwaysFrontLayer.Tiles[x, y] = null;
                    if (alwaysFront2Layer != null) alwaysFront2Layer.Tiles[x, y] = null;
                    if (alwaysFront3Layer != null) alwaysFront3Layer.Tiles[x, y] = null;
                }
            }

            // 5. Sustituir la carretera pavimentada de asfalto (y = 6..10) y la acera gris (x = 4..19, y = 11)
            // por terreno salvaje soleado vanilla, manteniendo intacto el camino de tierra inferior (y = 22..25).
            if (outdoorsSheet != null)
            {
                // Borde norte de la antigua carretera (y = 5): si hubiera algún tile de carretera/acera, reemplazarlo
                for (int x = 0; x < width; x++)
                {
                    int? idx5 = backLayer.Tiles[x, 5]?.TileIndex;
                    if (idx5 is >= 1025 and <= 1085)
                    {
                        var (wildIdx, wildType) = TerrainTileClassifier.GetWildTerrainTile(x, 5);
                        var wildTile = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, wildIdx);
                        wildTile.Properties["Type"] = wildType;
                        backLayer.Tiles[x, 5] = wildTile;
                    }
                }

                // Carretera pavimentada (y = 6..10, x = 0..34)
                for (int x = 0; x < width; x++)
                {
                    for (int y = 6; y <= 10; y++)
                    {
                        var (wildIdx, wildType) = TerrainTileClassifier.GetWildTerrainTile(x, y);
                        var wildTile = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, wildIdx);
                        wildTile.Properties["Type"] = wildType;
                        backLayer.Tiles[x, y] = wildTile;

                        int? bldIdx = bldLayer.Tiles[x, y]?.TileIndex;
                        if (bldIdx is >= 1025 and <= 1085)
                        {
                            bldLayer.Tiles[x, y] = null;
                        }
                    }
                }

                // Acera de losas grises de hormigón (y = 11, x = 4..19)
                for (int x = 4; x <= 19; x++)
                {
                    var (wildIdx, wildType) = TerrainTileClassifier.GetWildTerrainTile(x, 11);
                    var wildTile = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, wildIdx);
                    wildTile.Properties["Type"] = wildType;
                    backLayer.Tiles[x, 11] = wildTile;
                }
            }

            // 6. Sellar los bordes perimetrales salvo las aperturas de portales y pasos activos
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    bool isBorder = x == 0 || x == width - 1 || y == 0 || y == height - 1;
                    if (!isBorder)
                        continue;

                    bool isOpening =
                        // Portales Norte (x = 8..9 y x = 25..26 en y = 0)
                        ((x == 8 || x == 9 || x == 25 || x == 26) && y == 0)
                        // Portales Sur (x = 8..9 y x = 25..26 en y = height - 1)
                        || ((x == 8 || x == 9 || x == 25 || x == 26) && y == height - 1)
                        // Paso Oeste activo (x = 0, y = 22..25)
                        || (x == 0 && bridgeLoc.West != null && y >= bridgeLoc.West.FirstRow && y < bridgeLoc.West.FirstRow + bridgeLoc.West.RowCount)
                        // Paso Este activo (x = width - 1, y = 22..25)
                        || (x == width - 1 && bridgeLoc.East != null && y >= bridgeLoc.East.FirstRow && y < bridgeLoc.East.FirstRow + bridgeLoc.East.RowCount);

                    if (isOpening)
                        continue;

                    if (bldLayer.Tiles[x, y] != null)
                        bldLayer.Tiles[x, y].Properties["Passable"] = "F";

                    if (backLayer.Tiles[x, y] != null)
                        backLayer.Tiles[x, y].Properties["Passable"] = "F";
                }
            }

            // Remate con vallas verticales de madera en los extremos este/oeste (y = 22..25) solo en los bordes donde NO hay paso activo,
            // y saneado de objetos multi-casilla verticales (vallas, arbustos y árboles en los bordes del recorte 35x30)
            if (outdoorsSheet != null)
            {
                if (bridgeLoc.West == null)
                {
                    SetTileIfValid(frontLayer, outdoorsSheet, 0, 21, VerticalMultiTileClassifier.FenceVerticalTopTileIndex);
                    for (int y = 22; y <= 25; y++)
                    {
                        int vTile = VerticalMultiTileClassifier.GetVerticalFenceBodyTile(y, 22, 25);
                        SetTileIfValid(bldLayer, outdoorsSheet, 0, y, vTile);
                        SetPassableIfValid(bldLayer, 0, y, false);
                    }
                }

                if (bridgeLoc.East == null)
                {
                    SetTileIfValid(frontLayer, outdoorsSheet, width - 1, 21, VerticalMultiTileClassifier.FenceVerticalTopTileIndex);
                    for (int y = 22; y <= 25; y++)
                    {
                        int vTile = VerticalMultiTileClassifier.GetVerticalFenceBodyTile(y, 22, 25);
                        SetTileIfValid(bldLayer, outdoorsSheet, width - 1, y, vTile);
                        SetPassableIfValid(bldLayer, width - 1, y, false);
                    }
                }

                SanitizeBridgeMultiTileObjects(backLayer, bldLayer, frontLayer, alwaysFrontLayer, alwaysFront2Layer, alwaysFront3Layer, outdoorsSheet);
            }

            // 7. Asegurar transitabilidad de los pasos activos Oeste y Este en el camino de tierra vanilla (y = 22..25)
            // manteniendo intactas las copas de árboles y remates de vallas en Front y AlwaysFront.
            if (bridgeLoc.West is { } west)
            {
                for (int x = 0; x <= 5; x++)
                {
                    for (int y = west.FirstRow; y < west.FirstRow + west.RowCount; y++)
                    {
                        bldLayer.Tiles[x, y] = null;

                        if (backLayer.Tiles[x, y] != null)
                        {
                            ClearBlockingAndWaterProperties(backLayer.Tiles[x, y]);
                            if (!backLayer.Tiles[x, y].Properties.ContainsKey("Type"))
                            {
                                backLayer.Tiles[x, y].Properties["Type"] = "Dirt";
                            }
                        }
                    }
                }
            }

            if (bridgeLoc.East is { } east)
            {
                for (int x = width - 6; x < width; x++)
                {
                    for (int y = east.FirstRow; y < east.FirstRow + east.RowCount; y++)
                    {
                        bldLayer.Tiles[x, y] = null;

                        if (backLayer.Tiles[x, y] != null)
                        {
                            ClearBlockingAndWaterProperties(backLayer.Tiles[x, y]);
                            if (!backLayer.Tiles[x, y].Properties.ContainsKey("Type"))
                            {
                                backLayer.Tiles[x, y].Properties["Type"] = "Dirt";
                            }
                        }
                    }
                }
            }

            // 7c. Limpiar remates Front/AlwaysFront huérfanos sobre las columnas de borde de los
            // pasos activos (copas recortadas por el Crop de BusStop ensucian el corredor).
            if (outdoorsSheet != null)
            {
                if (bridgeLoc.West is { } westPassage)
                    ClearPassageEdgeCanopies(width, bridgeLoc, westPassage.FirstRow, westPassage.RowCount, leftMost: true, frontLayer, alwaysFrontLayer, alwaysFront2Layer, alwaysFront3Layer);
                if (bridgeLoc.East is { } eastPassage)
                    ClearPassageEdgeCanopies(width, bridgeLoc, eastPassage.FirstRow, eastPassage.RowCount, leftMost: false, frontLayer, alwaysFrontLayer, alwaysFront2Layer, alwaysFront3Layer);
            }

            // 8. Escribir map.Properties["Warp"] con los pasos declarativos (TASK-1202 / TASK-1401)
            string passageWarps = PlazaTopologyVerifier.BuildPassageWarpString(bridgeLoc);
            if (!string.IsNullOrEmpty(passageWarps))
            {
                target.Properties["Warp"] = passageWarps;
            }

            return target;
        }

        /// <summary>
        /// Limpia las copas/remates de Front y AlwaysFront sin base en las columnas de borde de
        /// un paso activo del puente (objetos multi-casilla recortados por el Crop de BusStop).
        /// </summary>
        private static void ClearPassageEdgeCanopies(
            int width,
            PlazaLocation bridgeLoc,
            int firstRow,
            int rowCount,
            bool leftMost,
            Layer frontLayer,
            Layer alwaysFrontLayer,
            Layer? alwaysFront2Layer,
            Layer? alwaysFront3Layer)
        {
            int col0 = leftMost ? 0 : width - 2;
            int col1 = leftMost ? 1 : width - 1;

            for (int x = col0; x <= col1; x++)
            {
                for (int y = firstRow - 1; y < firstRow + rowCount + 1; y++)
                {
                    if (y < 0 || y >= bridgeLoc.Height)
                        continue;

                    frontLayer.Tiles[x, y] = null;
                    alwaysFrontLayer.Tiles[x, y] = null;
                    if (alwaysFront2Layer != null) alwaysFront2Layer.Tiles[x, y] = null;
                    if (alwaysFront3Layer != null) alwaysFront3Layer.Tiles[x, y] = null;
                }
            }
        }

        /// <summary>
        /// Sanea los objetos multi-casilla verticales (vallas, arbustos y árboles) en los bordes del recorte 35×30
        /// de <c>Maps/BusStop</c> para que ninguna base en <c>y = 0</c> quede decapitada sin <c>y - 1</c> y ningún
        /// remate en <c>Front</c> quede huérfano o incompleto.
        /// </summary>
        private static void SanitizeBridgeMultiTileObjects(
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer alwaysFrontLayer,
            Layer? alwaysFront2Layer,
            Layer? alwaysFront3Layer,
            TileSheet outdoorsSheet)
        {
            // 1. Esquina superior izquierda (x = 0..2, y = 0..2): sustituir los troncos truncados cuya copa caía en y < 0
            // por un arbusto 3×3 completo (Front y=0: 13..15, Front y=1: 38..40, Buildings y=2: 63..65),
            // eliminando el árbol cortado en x = 3 (y = 0..2) para dejar hierba limpia y sin cortes.
            for (int x = 0; x <= 2; x++)
            {
                SetTileIfValid(bldLayer, outdoorsSheet, x, 0, 351);
                SetPassableIfValid(bldLayer, x, 0, false);
                SetTileIfValid(bldLayer, outdoorsSheet, x, 1, 351);
                SetPassableIfValid(bldLayer, x, 1, false);
                SetTileIfValid(bldLayer, outdoorsSheet, x, 2, 63 + x);
                SetPassableIfValid(bldLayer, x, 2, false);

                for (int y = 0; y <= 2; y++)
                {
                    SetPassableIfValid(backLayer, x, y, false);
                }

                SetTileIfValid(frontLayer, outdoorsSheet, x, 0, 13 + x);
                SetTileIfValid(frontLayer, outdoorsSheet, x, 1, 38 + x);
                frontLayer.Tiles[x, 2] = null;

                alwaysFrontLayer.Tiles[x, 0] = null;
            }

            // Quitar el árbol cortado en x = 3 (y = 0..2)
            SetTileIfValid(bldLayer, outdoorsSheet, 3, 0, 351);
            SetPassableIfValid(bldLayer, 3, 0, false);
            bldLayer.Tiles[3, 1] = null;
            bldLayer.Tiles[3, 2] = null;
            frontLayer.Tiles[3, 0] = null;
            frontLayer.Tiles[3, 1] = null;
            alwaysFrontLayer.Tiles[3, 0] = null;
            if (alwaysFront2Layer != null) alwaysFront2Layer.Tiles[3, 0] = null;
            if (alwaysFront3Layer != null) alwaysFront3Layer.Tiles[3, 0] = null;

            frontLayer.Tiles[4, 0] = null;
            alwaysFrontLayer.Tiles[4, 0] = null;

            // 2. Borde izquierdo (x = 0..3, y = 6..15): limpiar los parches y taludes de la antigua carretera
            // (AlwaysFront[0..2, 9..10], Buildings[0..2, 6..15] y Back[0..3, 11]) y el roble partido verticalmente
            // en srcX = 10 (AlwaysFront3[0..1, 9..12], Front[0..1, 13]) para que el cerezo rosa (x = 0..4, y = 5..11)
            // descanse limpiamente sobre la pradera.
            for (int x = 0; x <= 5; x++)
            {
                for (int y = 11; y <= 16; y++)
                {
                    var (wildIdx, wildType) = TerrainTileClassifier.GetWildTerrainTile(x, y);
                    var wildTile = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, wildIdx);
                    wildTile.Properties["Type"] = wildType;
                    if (x == 0) wildTile.Properties["Passable"] = "F";
                    backLayer.Tiles[x, y] = wildTile;
                }
            }
            for (int x = 0; x <= 2; x++)
            {
                for (int y = 6; y <= 15; y++)
                {
                    bldLayer.Tiles[x, y] = null;
                }
                alwaysFrontLayer.Tiles[x, 9] = null;
                alwaysFrontLayer.Tiles[x, 10] = null;
            }
            SetTileIfValid(backLayer, outdoorsSheet, 0, 16, 376);
            SetPassableIfValid(backLayer, 0, 16, false);
            SetTileIfValid(backLayer, outdoorsSheet, 1, 16, 376);
            SetTileIfValid(bldLayer, outdoorsSheet, 2, 16, 444);
            if (alwaysFront3Layer != null)
            {
                for (int x = 0; x <= 1; x++)
                {
                    for (int y = 9; y <= 12; y++)
                    {
                        alwaysFront3Layer.Tiles[x, y] = null;
                    }
                }
            }
            frontLayer.Tiles[0, 13] = null;
            frontLayer.Tiles[1, 13] = null;

            SetTileIfValid(frontLayer, outdoorsSheet, 1, 9, 118);
            SetTileIfValid(frontLayer, outdoorsSheet, 2, 9, 119);

            // 3. Valla norte (x = 10..17, y = 0..1): unificar en y = 1 (base en Buildings y=1, remate en Front y=0)
            // para eliminar los postes decapitados en y = 0 (donde no existe y = -1), conservando AlwaysFront[14..17, 0] (copa del árbol).
            for (int x = 10; x <= 17; x++)
            {
                SetTileIfValid(bldLayer, outdoorsSheet, x, 0, 351);
                SetPassableIfValid(bldLayer, x, 0, false);
                if (x <= 13)
                {
                    alwaysFrontLayer.Tiles[x, 0] = null;
                }

                var (baseIdx, topIdx) = VerticalMultiTileClassifier.GetHorizontalFencePair(x, 10, 17);
                SetTileIfValid(bldLayer, outdoorsSheet, x, 1, baseIdx);
                SetTileIfValid(frontLayer, outdoorsSheet, x, 0, topIdx);
            }

            // 4. Esquinas derechas (34, 0) y (34, 29): eliminar base de árbol decapitada en y = 0 y cuarto de arbusto huérfano en y = 29
            SetTileIfValid(bldLayer, outdoorsSheet, 34, 0, 353);
            SetPassableIfValid(bldLayer, 34, 0, false);
            frontLayer.Tiles[34, 29] = null;
            alwaysFrontLayer.Tiles[34, 29] = null;

            // 5. Valla horizontal inferior (y = 26, remates en Front y = 25): completar los remates 359 omitidos en BusStop
            // en x = 1 y x = 32 y normalizar la continuidad en x = 2.
            SetTileIfValid(frontLayer, outdoorsSheet, 1, 25, VerticalMultiTileClassifier.FenceMidTopTileIndex);
            SetTileIfValid(bldLayer, outdoorsSheet, 2, 26, VerticalMultiTileClassifier.FenceMidBaseTileIndex);
            SetTileIfValid(frontLayer, outdoorsSheet, 2, 25, VerticalMultiTileClassifier.FenceMidTopTileIndex);
            SetTileIfValid(frontLayer, outdoorsSheet, 32, 25, VerticalMultiTileClassifier.FenceMidTopTileIndex);
        }

        /// <summary>
        /// Genera un mapa de anexo temático de 40×32 recortando el cuadrante representativo del mapa vanilla
        /// (<paramref name="source"/>), eliminando warps vanilla, sellando los cuatro bordes salvo la única
        /// entrada (<paramref name="side"/>) y configurando las zonas de bonificación recalculadas (TASK-902).
        /// </summary>
        public static Map CreateAnnexMap(Map source, AnnexTemplate template, AnnexSide side)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (template == null)
                throw new ArgumentNullException(nameof(template));

            int width = template.Width;
            int height = template.Height;

            Map target = new Map($"Pelican4Ever_Annex_{template.Id}_{side}")
            {
                Description = $"Annex {template.DisplayName} ({side})"
            };

            // 1. Copiar propiedades globales seguras (sin Warp ni coordenadas fuera de 40×32)
            foreach (var prop in source.Properties)
            {
                if (!ExcludedAnnexMapProperties.Contains(prop.Key))
                {
                    target.Properties[prop.Key] = prop.Value;
                }
            }

            target.Properties["Outdoors"] = "T";
            target.Properties["SpouseAreaLocation"] = "2 2";
            target.Properties["GrandpaShrineLocation"] = "2 2";
            target.Properties["FarmHouseEntry"] = "20 15";
            target.Properties["ShippingBinLocation"] = "2 2";
            target.Properties["PetBowlLocation"] = "4 2";
            target.Properties["GreenhouseLocation"] = "10 10";

            if (template.Behavior.MountainOreArea is { } oreArea)
                target.Properties["SpawnMountainFarmOreRect"] = oreArea.ToMapProperty();
            if (template.Behavior.ForestForage)
                target.Properties["SpawnForestFarmForage"] = "T";
            if (!string.IsNullOrWhiteSpace(template.FishLocationOverride))
                target.Properties["FarmFishLocationOverride"] = template.FishLocationOverride;

            // 2. Copiar TileSheets
            CopyTileSheets(source, target);

            int srcMaxW = source.Layers.Count > 0 ? source.Layers[0].LayerWidth : width;
            int srcMaxH = source.Layers.Count > 0 ? source.Layers[0].LayerHeight : height;
            int srcX = Math.Clamp(template.SourceCropX, 0, Math.Max(0, srcMaxW - width));
            int srcY = Math.Clamp(template.SourceCropY, 0, Math.Max(0, srcMaxH - height));
            Size tileSize = source.Layers.Count > 0 ? source.Layers[0].TileSize : new Size(64, 64);

            // 3. Recortar cada capa a 40×32 y limpiar propiedades TouchAction/Action de warp vanilla
            foreach (var layer in source.Layers)
            {
                var newLayer = new Layer(layer.Id, target, new Size(width, height), layer.TileSize)
                {
                    Description = layer.Description,
                    Visible = layer.Visible
                };

                foreach (var prop in layer.Properties)
                {
                    newLayer.Properties[prop.Key] = prop.Value;
                }

                for (int x = 0; x < width; x++)
                {
                    int sx = srcX + x;
                    if (sx < 0 || sx >= layer.LayerWidth)
                        continue;

                    for (int y = 0; y < height; y++)
                    {
                        int sy = srcY + y;
                        if (sy < 0 || sy >= layer.LayerHeight)
                            continue;

                        var t = layer.Tiles[sx, sy];
                        if (t != null)
                        {
                            Tile cloned = CloneTile(t, newLayer, target);
                            StripVanillaWarpTileProperties(cloned);
                            newLayer.Tiles[x, y] = cloned;
                        }
                    }
                }

                target.AddLayer(newLayer);
            }

            Layer backLayer = EnsureLayer(target, "Back", width, height, tileSize);
            Layer bldLayer = EnsureLayer(target, "Buildings", width, height, tileSize);
            Layer frontLayer = EnsureLayer(target, "Front", width, height, tileSize);
            Layer alwaysFrontLayer = EnsureLayer(target, "AlwaysFront", width, height, tileSize);
            Layer pathsLayer = EnsureLayer(target, "Paths", width, height, tileSize);

            TileSheet? mainSheet = target.GetTileSheet("untitled tile sheet")
                                ?? target.TileSheets.FirstOrDefault();

            // 4. Rellenar posibles huecos nulos en Back con suelo firme transitable
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (backLayer.Tiles[x, y] == null && mainSheet != null)
                    {
                        var ground = new StaticTile(backLayer, mainSheet, BlendMode.Alpha, 587);
                        ground.Properties["Type"] = "Dirt";
                        ground.Properties["Diggable"] = "T";
                        backLayer.Tiles[x, y] = ground;
                    }
                }
            }

            // 5. Calcular apertura de entrada y corredor interior según el lado del anexo
            GetEntranceOpeningAndCorridor(side, width, height, out HashSet<(int X, int Y)> openingBorderTiles, out HashSet<(int X, int Y)> corridorTiles);

            // 6. Despejar y asegurar paso transitable en la entrada y su corredor de acceso hacia el interior
            foreach (var (cx, cy) in corridorTiles)
            {
                if (cx < 0 || cx >= width || cy < 0 || cy >= height)
                    continue;

                bldLayer.Tiles[cx, cy] = null;
                frontLayer.Tiles[cx, cy] = null;
                alwaysFrontLayer.Tiles[cx, cy] = null;

                // Barrido por TODAS las capas del recorte: el vanilla puede llevar Content2/Front2/
                // AlwaysFront2..3 y restos sobre el corredor quedarían como pegotes flotantes.
                foreach (var layer in target.Layers)
                {
                    if (string.Equals(layer.Id, "Back", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(layer.Id, "Paths", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    layer.Tiles[cx, cy] = null;
                }

                bool wasWater = backLayer.Tiles[cx, cy] != null && HasWaterProperty(backLayer.Tiles[cx, cy]);

                if (wasWater && mainSheet != null)
                {
                    // Si el corredor o entrada cruza agua, construir un puente de madera canónico de Stardew Valley
                    var bridgePlank = new StaticTile(backLayer, mainSheet, BlendMode.Alpha, 1244);
                    backLayer.Tiles[cx, cy] = bridgePlank;
                    ClearBlockingAndWaterProperties(bridgePlank);
                    bridgePlank.Properties["Type"] = "Wood";

                    int bIndex = (cx % 2 == 0) ? 184 : 211;
                    var bridgeBeam = new StaticTile(bldLayer, mainSheet, BlendMode.Alpha, bIndex);
                    bridgeBeam.Properties["Passable"] = "T";
                    bldLayer.Tiles[cx, cy] = bridgeBeam;
                }
                else if (backLayer.Tiles[cx, cy] != null)
                {
                    ClearBlockingAndWaterProperties(backLayer.Tiles[cx, cy]);
                    if (!backLayer.Tiles[cx, cy].Properties.ContainsKey("Type"))
                    {
                        backLayer.Tiles[cx, cy].Properties["Type"] = "Dirt";
                    }
                }
                else if (mainSheet != null)
                {
                    var passGround = new StaticTile(backLayer, mainSheet, BlendMode.Alpha, 587);
                    passGround.Properties["Type"] = "Dirt";
                    backLayer.Tiles[cx, cy] = passGround;
                }
            }

            // Marcar e identificar de forma claramente visible la puerta/umbral de entrada y flanquearla con postes
            if (mainSheet != null)
            {
                bool corridorHasWater = false;
                foreach (var (cx, cy) in corridorTiles)
                {
                    if (backLayer.Tiles[cx, cy] != null && HasWaterProperty(backLayer.Tiles[cx, cy]))
                    {
                        corridorHasWater = true;
                        break;
                    }
                }

                MarkAndDecorateEntranceDoorway(side, width, height, backLayer, bldLayer, frontLayer, mainSheet, openingBorderTiles, corridorHasWater);
            }

            // 8. Zonas de bonificación válidas y diseño meticuloso de cada minimapa (TASK-902)
            EnsureAnnexBonusZones(template, width, height, backLayer, bldLayer, frontLayer, pathsLayer, mainSheet, corridorTiles);

            // 9. Margen decorativo perimetral inalcanzable estilo vanilla + entrada por tipo de
            //    granja (TASK-902). Sustituye al sellado invisible: cada borde del minimapa queda
            //    delimitado por una franja temática completa del propio tipo (setos, acantilados,
            //    agua, bosque, cerca, arena) y la entrada se remata según su tipo de granja.
            var rimSkipTiles = new HashSet<(int X, int Y)>(openingBorderTiles);
            rimSkipTiles.UnionWith(corridorTiles);
            if (mainSheet != null)
            {
                Layer? bld2Layer = target.GetLayer("Buildings2");
                Layer? alwaysFront2Layer = target.GetLayer("AlwaysFront2");
                Layer? alwaysFront3Layer = target.GetLayer("AlwaysFront3");
                AnnexBorderArtist.BuildRim(
                    template, side, width, height, backLayer, bldLayer, frontLayer, alwaysFrontLayer, bld2Layer, alwaysFront2Layer, alwaysFront3Layer, mainSheet, rimSkipTiles);
                AnnexBorderArtist.PaintTemplateInteriors(template, side, backLayer, bldLayer, frontLayer, alwaysFrontLayer, mainSheet);
                AnnexBorderArtist.ApplyEntranceStyle(template, side, width, height, corridorTiles, backLayer, bldLayer, frontLayer, alwaysFrontLayer, mainSheet);
            }

            // 10. Remediación exhaustiva de glitches gráficos y alineación de capas en todo el minimapa
            if (mainSheet != null)
            {
                SanitizeMapGraphicGlitches(target, mainSheet);
            }

            return target;
        }

        private static void MarkAndDecorateEntranceDoorway(
            AnnexSide side,
            int width,
            int height,
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            TileSheet mainSheet,
            HashSet<(int X, int Y)> openingBorderTiles,
            bool corridorHasWater)
        {
            AnnexWarpPoints pts = AnnexEntrance.For(side, width, height);
            int ex = pts.ReturnExitTile.X;
            int ey = pts.ReturnExitTile.Y;

            if (corridorHasWater)
            {
                // Entrada sobre agua: el umbral completo se convierte en puente de madera
                // (tablones + vigas transitables) y NO se colocan vallas, que quedarían flotando
                // sobre el agua. Barandilla visible solo en la viga exterior del borde.
                foreach (var (ox, oy) in openingBorderTiles)
                {
                    var plank = new StaticTile(backLayer, mainSheet, BlendMode.Alpha, 1244);
                    plank.Properties["Type"] = "Wood";
                    backLayer.Tiles[ox, oy] = plank;

                    int bIndex = (ox % 2 == 0) ? 184 : 211;
                    var beam = new StaticTile(bldLayer, mainSheet, BlendMode.Alpha, bIndex);
                    beam.Properties["Passable"] = "T";
                    bldLayer.Tiles[ox, oy] = beam;

                    frontLayer.Tiles[ox, oy] = null;
                }

                return;
            }

            switch (side)
            {
                case AnnexSide.North:
                    int southY = height - 1;
                    for (int x = ex - 1; x <= ex; x++)
                    {
                        if (x >= 0 && x < width && backLayer.Tiles[x, southY] != null && !HasWaterProperty(backLayer.Tiles[x, southY]))
                        {
                            if (IsRoadConvertableBack(backLayer.Tiles[x, southY]))
                            {
                                SetTileIfValid(backLayer, mainSheet, x, southY, 736);
                                backLayer.Tiles[x, southY].Properties["Type"] = "Stone";
                            }
                        }
                    }
                    if (ex - 2 >= 0)
                    {
                        SetTileIfValid(bldLayer, mainSheet, ex - 2, southY, VerticalMultiTileClassifier.FenceLeftBaseTileIndex);
                        SetPassableIfValid(bldLayer, ex - 2, southY, false);
                        if (southY > 0 && frontLayer != null)
                        {
                            SetTileIfValid(frontLayer, mainSheet, ex - 2, southY - 1, VerticalMultiTileClassifier.FenceLeftTopTileIndex);
                        }
                    }
                    if (ex + 1 < width)
                    {
                        SetTileIfValid(bldLayer, mainSheet, ex + 1, southY, VerticalMultiTileClassifier.FenceRightBaseTileIndex);
                        SetPassableIfValid(bldLayer, ex + 1, southY, false);
                        if (southY > 0 && frontLayer != null)
                        {
                            SetTileIfValid(frontLayer, mainSheet, ex + 1, southY - 1, VerticalMultiTileClassifier.FenceRightTopTileIndex);
                        }
                    }
                    break;

                case AnnexSide.South:
                    for (int x = ex - 1; x <= ex; x++)
                    {
                        if (x >= 0 && x < width && backLayer.Tiles[x, 0] != null && !HasWaterProperty(backLayer.Tiles[x, 0]))
                        {
                            if (IsRoadConvertableBack(backLayer.Tiles[x, 0]))
                            {
                                SetTileIfValid(backLayer, mainSheet, x, 0, 736);
                                backLayer.Tiles[x, 0].Properties["Type"] = "Stone";
                            }
                        }
                    }
                    if (ex - 2 >= 0)
                    {
                        SetTileIfValid(bldLayer, mainSheet, ex - 2, 0, VerticalMultiTileClassifier.FenceLeftBaseTileIndex);
                        SetPassableIfValid(bldLayer, ex - 2, 0, false);
                    }
                    if (ex + 1 < width)
                    {
                        SetTileIfValid(bldLayer, mainSheet, ex + 1, 0, VerticalMultiTileClassifier.FenceRightBaseTileIndex);
                        SetPassableIfValid(bldLayer, ex + 1, 0, false);
                    }
                    break;

                case AnnexSide.East:
                    for (int y = ey; y <= ey + 1; y++)
                    {
                        if (y >= 0 && y < height && backLayer.Tiles[0, y] != null && !HasWaterProperty(backLayer.Tiles[0, y]))
                        {
                            if (IsRoadConvertableBack(backLayer.Tiles[0, y]))
                            {
                                SetTileIfValid(backLayer, mainSheet, 0, y, 736);
                                backLayer.Tiles[0, y].Properties["Type"] = "Stone";
                            }
                        }
                    }
                    if (ey - 1 >= 0)
                    {
                        SetTileIfValid(bldLayer, mainSheet, 0, ey - 1, VerticalMultiTileClassifier.FenceVerticalUpperMidTileIndex);
                        SetPassableIfValid(bldLayer, 0, ey - 1, false);
                        if (ey - 2 >= 0 && frontLayer != null)
                        {
                            SetTileIfValid(frontLayer, mainSheet, 0, ey - 2, VerticalMultiTileClassifier.FenceVerticalTopTileIndex);
                        }
                    }
                    if (ey + 2 < height)
                    {
                        SetTileIfValid(bldLayer, mainSheet, 0, ey + 2, VerticalMultiTileClassifier.FenceVerticalBaseTileIndex);
                        SetPassableIfValid(bldLayer, 0, ey + 2, false);
                        if (frontLayer != null)
                        {
                            SetTileIfValid(frontLayer, mainSheet, 0, ey + 1, VerticalMultiTileClassifier.FenceVerticalUpperMidTileIndex);
                        }
                    }
                    break;
            }
        }

        public static void GetEntranceOpeningAndCorridor(
            AnnexSide side,
            int width,
            int height,
            out HashSet<(int X, int Y)> openingBorderTiles,
            out HashSet<(int X, int Y)> corridorTiles)
        {
            openingBorderTiles = new HashSet<(int X, int Y)>();
            corridorTiles = new HashSet<(int X, int Y)>();

            AnnexWarpPoints pts = AnnexEntrance.For(side, width, height);
            int ex = pts.ReturnExitTile.X;
            int ey = pts.ReturnExitTile.Y;

            switch (side)
            {
                case AnnexSide.North:
                    // Entrada por el borde Sur del anexo (y = height - 1)
                    openingBorderTiles.Add((ex - 1, height - 1));
                    openingBorderTiles.Add((ex, height - 1));
                    for (int y = Math.Max(1, height - 7); y < height; y++)
                    {
                        corridorTiles.Add((ex - 1, y));
                        corridorTiles.Add((ex, y));
                    }
                    break;

                case AnnexSide.South:
                    // Entrada por el borde Norte del anexo (y = 0)
                    openingBorderTiles.Add((ex - 1, 0));
                    openingBorderTiles.Add((ex, 0));
                    for (int y = 0; y <= Math.Min(height - 2, 6); y++)
                    {
                        corridorTiles.Add((ex - 1, y));
                        corridorTiles.Add((ex, y));
                    }
                    break;

                case AnnexSide.East:
                    // Entrada por el borde Oeste del anexo (x = 0)
                    openingBorderTiles.Add((0, ey));
                    openingBorderTiles.Add((0, ey + 1));
                    for (int x = 0; x <= Math.Min(width - 2, 6); x++)
                    {
                        corridorTiles.Add((x, ey));
                        corridorTiles.Add((x, ey + 1));
                    }
                    break;
            }
        }

        private static void EnsureAnnexBonusZones(
            AnnexTemplate template,
            int width,
            int height,
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer pathsLayer,
            TileSheet? mainSheet,
            HashSet<(int X, int Y)> corridorTiles)
        {
            // A) Zona de minerales (Colinas y Cuatro Esquinas): requiere tiles libres con Type="Dirt" en Back
            if (template.Behavior.MountainOreArea is { } oreArea && mainSheet != null)
            {
                for (int x = oreArea.X; x < oreArea.X + oreArea.Width; x++)
                {
                    for (int y = oreArea.Y; y < oreArea.Y + oreArea.Height; y++)
                    {
                        if (x <= 1 || x >= width - 2 || y <= 1 || y >= height - 2 || corridorTiles.Contains((x, y)))
                            continue;

                        bldLayer.Tiles[x, y] = null;
                        frontLayer.Tiles[x, y] = null;

                        if (backLayer.Tiles[x, y] == null || HasWaterProperty(backLayer.Tiles[x, y]))
                        {
                            backLayer.Tiles[x, y] = new StaticTile(backLayer, mainSheet, BlendMode.Alpha, 587);
                        }
                        else
                        {
                            ClearBlockingAndWaterProperties(backLayer.Tiles[x, y]);
                        }

                        backLayer.Tiles[x, y].Properties["Type"] = "Dirt";
                    }
                }
            }

            // B) Zona forestal (Forraje en x < 18 con Type="Grass" y tocones con Paths índice 21)
            if ((template.Behavior.ForestForage || template.Behavior.ForestStumps) && mainSheet != null)
            {
                for (int x = 3; x < Math.Min(18, width - 2); x++)
                {
                    for (int y = 4; y < height - 4; y++)
                    {
                        if (corridorTiles.Contains((x, y)))
                            continue;

                        if (bldLayer.Tiles[x, y] == null && backLayer.Tiles[x, y] != null && !HasWaterProperty(backLayer.Tiles[x, y]))
                        {
                            ClearBlockingAndWaterProperties(backLayer.Tiles[x, y]);
                            backLayer.Tiles[x, y].Properties["Type"] = "Grass";
                        }
                    }
                }

                if (template.Behavior.ForestStumps)
                {
                    TileSheet pathsSheet = targetSheetForPaths(pathsLayer.Map, mainSheet);
                    (int sx, int sy)[] stumpSpots = { (6, 8), (6, 20) };
                    foreach (var (sx, sy) in stumpSpots)
                    {
                        if (sx + 1 >= width - 1 || sy + 1 >= height - 1)
                            continue;

                        for (int dx = 0; dx < 2; dx++)
                        {
                            for (int dy = 0; dy < 2; dy++)
                            {
                                bldLayer.Tiles[sx + dx, sy + dy] = null;
                                frontLayer.Tiles[sx + dx, sy + dy] = null;
                                if (backLayer.Tiles[sx + dx, sy + dy] == null || HasWaterProperty(backLayer.Tiles[sx + dx, sy + dy]))
                                {
                                    backLayer.Tiles[sx + dx, sy + dy] = new StaticTile(backLayer, mainSheet, BlendMode.Alpha, 587);
                                }
                                ClearBlockingAndWaterProperties(backLayer.Tiles[sx + dx, sy + dy]);
                                backLayer.Tiles[sx + dx, sy + dy].Properties["Type"] = "Grass";
                            }
                        }

                        pathsLayer.Tiles[sx, sy] = new StaticTile(pathsLayer, pathsSheet, BlendMode.Alpha, 21);
                    }
                }
            }

            // C) Tocón de Cuatro Esquinas en (6, 6)
            if (template.Behavior.FourCornersExtras && mainSheet != null)
            {
                for (int dx = 0; dx < 2; dx++)
                {
                    for (int dy = 0; dy < 2; dy++)
                    {
                        int tx = 6 + dx;
                        int ty = 6 + dy;
                        bldLayer.Tiles[tx, ty] = null;
                        frontLayer.Tiles[tx, ty] = null;
                        if (backLayer.Tiles[tx, ty] == null || HasWaterProperty(backLayer.Tiles[tx, ty]))
                        {
                            backLayer.Tiles[tx, ty] = new StaticTile(backLayer, mainSheet, BlendMode.Alpha, 587);
                        }
                        ClearBlockingAndWaterProperties(backLayer.Tiles[tx, ty]);
                        backLayer.Tiles[tx, ty].Properties["Type"] = "Grass";
                    }
                }
            }
        }


        /// <summary>
        /// Escanea exhaustivamente todas las capas del mapa recortado para reparar cualquier glitch gráfico
        /// de multi-casilla (copas huérfanas sin tronco, troncos decapitados sin copa en y - 1, riscos cortados
        /// sin remate superior, vallas horizontales/verticales sin remate top en Front o sin vecinos válidos).
        /// </summary>
        private static void SanitizeMapGraphicGlitches(Map map, TileSheet outdoorsSheet)
        {
            Layer? backLayer = map.GetLayer("Back");
            Layer? bldLayer = map.GetLayer("Buildings");
            Layer? frontLayer = map.GetLayer("Front");
            Layer? alwaysFrontLayer = map.GetLayer("AlwaysFront");

            if (backLayer == null || bldLayer == null)
                return;

            int width = backLayer.LayerWidth;
            int height = backLayer.LayerHeight;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    // 1. Descapitados en y = 0: Si Buildings[x, 0] tiene base de árbol u objeto multi-casilla que requiere y - 1
                    if (y == 0 && bldLayer.Tiles[x, 0] != null)
                    {
                        int bIndex = bldLayer.Tiles[x, 0].TileIndex;
                        if (VerticalMultiTileClassifier.ClassifyOutdoorsBaseTile(bIndex) == VerticalMultiTileKind.TreeTrunk)
                        {
                            bldLayer.Tiles[x, 0] = new StaticTile(bldLayer, outdoorsSheet, BlendMode.Alpha, 351);
                            bldLayer.Tiles[x, 0].Properties["Passable"] = "F";
                        }
                    }

                    // 2. Remate de vallas horizontales en Buildings[x, y]
                    if (bldLayer.Tiles[x, y] != null)
                    {
                        int bIndex = bldLayer.Tiles[x, y].TileIndex;
                        if (bIndex is 383 or 384 or 385 or 434)
                        {
                            int expectedTop = bIndex switch
                            {
                                383 => 358,
                                384 => 359,
                                434 => 409,
                                385 => 360,
                                _ => 359
                            };
                            if (y > 0 && frontLayer != null)
                            {
                                if (frontLayer.Tiles[x, y - 1] == null)
                                {
                                    frontLayer.Tiles[x, y - 1] = new StaticTile(frontLayer, outdoorsSheet, BlendMode.Alpha, expectedTop);
                                }
                            }
                            else if (y == 0)
                            {
                                bldLayer.Tiles[x, 0] = new StaticTile(bldLayer, outdoorsSheet, BlendMode.Alpha, 194);
                                bldLayer.Tiles[x, 0].Properties["Passable"] = "F";
                            }
                        }
                    }

                    // 3. Remate de desniveles/acantilados en Buildings[x, y] (441..444, 164..166)
                    if (bldLayer.Tiles[x, y] != null)
                    {
                        int bIndex = bldLayer.Tiles[x, y].TileIndex;
                        if (bIndex is >= 441 and <= 444 or >= 164 and <= 166)
                        {
                            if (y > 0 && frontLayer != null && frontLayer.Tiles[x, y - 1] == null)
                            {
                                int topLedge = bIndex switch
                                {
                                    441 => 416,
                                    444 => 419,
                                    164 => 139,
                                    166 => 141,
                                    _ => 417
                                };
                                frontLayer.Tiles[x, y - 1] = new StaticTile(frontLayer, outdoorsSheet, BlendMode.Alpha, topLedge);
                            }
                        }
                    }

                    // 4. Copas de árbol u objetos superiores huérfanos en Front / AlwaysFront
                    if (frontLayer != null && frontLayer.Tiles[x, y] != null)
                    {
                        int fIndex = frontLayer.Tiles[x, y].TileIndex;
                        if (fIndex is 143 or 144 or 118 or 119 or 104 or 107 or 108 or 111)
                        {
                            bool hasBaseBelow = (y + 1 < height) && bldLayer.Tiles[x, y + 1] != null;
                            bool isBorder = (y == 0 || y == height - 1 || x == 0 || x == width - 1);
                            if (!hasBaseBelow && !isBorder)
                            {
                                frontLayer.Tiles[x, y] = null;
                            }
                        }
                    }
                }
            }
        }

        private static void SetTileIfValid(Layer? layer, TileSheet? sheet, int x, int y, int tileIndex)
        {
            if (layer != null && sheet != null && x >= 0 && x < layer.LayerWidth && y >= 0 && y < layer.LayerHeight)
            {
                layer.Tiles[x, y] = new StaticTile(layer, sheet, BlendMode.Alpha, tileIndex);
            }
        }

        private static void SetPassableIfValid(Layer? layer, int x, int y, bool passable)
        {
            if (layer != null && x >= 0 && x < layer.LayerWidth && y >= 0 && y < layer.LayerHeight && layer.Tiles[x, y] != null)
            {
                layer.Tiles[x, y].Properties["Passable"] = passable ? "T" : "F";
            }
        }

        private static TileSheet targetSheetForPaths(Map map, TileSheet fallback)
        {
            return map.GetTileSheet("Paths")
                ?? map.GetTileSheet("paths")
                ?? fallback;
        }

        private static Layer EnsureLayer(Map map, string layerId, int width, int height, Size tileSize)
        {
            Layer? existing = map.GetLayer(layerId);
            if (existing != null)
                return existing;

            var created = new Layer(layerId, map, new Size(width, height), tileSize);
            map.AddLayer(created);
            return created;
        }

        private static void CopyTileSheets(Map source, Map target)
        {
            foreach (var sheet in source.TileSheets)
            {
                var newSheet = new TileSheet(sheet.Id, target, sheet.ImageSource, sheet.SheetSize, sheet.TileSize)
                {
                    Margin = sheet.Margin,
                    Spacing = sheet.Spacing,
                    Description = sheet.Description
                };
                foreach (var prop in sheet.Properties)
                {
                    newSheet.Properties[prop.Key] = prop.Value;
                }

                if (TileIndexPropertiesCacheField?.GetValue(sheet.TileIndexProperties) is System.Collections.IDictionary sourceCache)
                {
                    foreach (System.Collections.DictionaryEntry entry in sourceCache)
                    {
                        int tileIndex = (int)entry.Key;
                        if (entry.Value is IPropertyCollection propCol)
                        {
                            foreach (var p in propCol)
                            {
                                newSheet.TileIndexProperties[tileIndex][p.Key] = p.Value;
                            }
                        }
                    }
                }

                target.AddTileSheet(newSheet);
            }
        }

        private static Tile CloneTile(Tile t, Layer newLayer, Map target)
        {
            var targetSheet = target.GetTileSheet(t.TileSheet.Id);
            if (t is StaticTile st)
            {
                var newTile = new StaticTile(newLayer, targetSheet, st.BlendMode, st.TileIndex);
                foreach (var p in st.Properties)
                {
                    newTile.Properties[p.Key] = p.Value;
                }
                return newTile;
            }
            if (t is AnimatedTile at)
            {
                var frames = new StaticTile[at.TileFrames.Length];
                for (int f = 0; f < at.TileFrames.Length; f++)
                {
                    var frame = at.TileFrames[f];
                    var frameSheet = target.GetTileSheet(frame.TileSheet.Id);
                    frames[f] = new StaticTile(newLayer, frameSheet, frame.BlendMode, frame.TileIndex);
                }
                var newTile = new AnimatedTile(newLayer, frames, at.FrameInterval);
                foreach (var p in at.Properties)
                {
                    newTile.Properties[p.Key] = p.Value;
                }
                return newTile;
            }

            return t.Clone(newLayer);
        }

        private static void StripVanillaWarpTileProperties(Tile tile)
        {
            if (tile.Properties.TryGetValue("TouchAction", out var touchVal))
            {
                string s = touchVal.ToString();
                if (s.StartsWith("Warp", StringComparison.OrdinalIgnoreCase)
                    || s.StartsWith("LoadMap", StringComparison.OrdinalIgnoreCase))
                {
                    tile.Properties.Remove("TouchAction");
                }
            }

            if (tile.Properties.TryGetValue("Action", out var actionVal))
            {
                string s = actionVal.ToString();
                if (s.StartsWith("Warp", StringComparison.OrdinalIgnoreCase)
                    || s.StartsWith("LoadMap", StringComparison.OrdinalIgnoreCase)
                    || s.StartsWith("Enter", StringComparison.OrdinalIgnoreCase))
                {
                    tile.Properties.Remove("Action");
                }
            }
        }

        /// <summary>
        /// ¿El Back es un terreno salvaje convertible a camino de piedra en el umbral de la entrada?
        /// </summary>
        private static bool IsRoadConvertableBack(Tile back)
        {
            if (back == null)
                return false;

            if (back.TileIndex == 587 || back.TileIndex == 175)
                return true;

            return TerrainTileClassifier.IsGrassTileIndex(back.TileIndex);
        }

        private static bool HasWaterProperty(Tile tile)
        {
            return tile.Properties.ContainsKey("Water") || tile.TileIndexProperties.ContainsKey("Water");
        }

        private static void ClearBlockingAndWaterProperties(Tile tile)
        {
            tile.Properties.Remove("Passable");
            tile.Properties.Remove("Water");
            tile.Properties.Remove("NoSpawn");
        }
    }
}
