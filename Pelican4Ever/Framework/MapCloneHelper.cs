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

            // 8. Escribir map.Properties["Warp"] con los pasos declarativos (TASK-1202 / TASK-1401)
            string passageWarps = PlazaTopologyVerifier.BuildPassageWarpString(bridgeLoc);
            if (!string.IsNullOrEmpty(passageWarps))
            {
                target.Properties["Warp"] = passageWarps;
            }

            return target;
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

            // 6. Sellar todo el perímetro (4 bordes) salvo los 2 tiles de la única entrada
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    bool isBorder = x == 0 || x == width - 1 || y == 0 || y == height - 1;
                    if (!isBorder || openingBorderTiles.Contains((x, y)))
                        continue;

                    // NOTA DE DISEÑO METICULOSO:
                    // En Stardew Valley, la colisión perimetral se sella de forma completamente invisible y limpia
                    // estableciendo Passable="F" en Back y en cualquier elemento existente en Buildings.
                    // NO añadimos un StaticTile con índice 0 en Buildings: en el tilesheet oficial del exterior
                    // (spring_outdoorsTileSheet), el tile 0 NO es transparente (es un fragmento de matorral verde),
                    // lo cual ensuciaba visiblemente ríos, puentes y orillas perimetrales.
                    if (bldLayer.Tiles[x, y] != null)
                    {
                        bldLayer.Tiles[x, y].Properties["Passable"] = "F";
                    }

                    if (backLayer.Tiles[x, y] != null)
                    {
                        backLayer.Tiles[x, y].Properties["Passable"] = "F";
                    }
                }
            }

            // 7. Despejar y asegurar paso transitable en la entrada y su corredor de acceso hacia el interior
            foreach (var (cx, cy) in corridorTiles)
            {
                if (cx < 0 || cx >= width || cy < 0 || cy >= height)
                    continue;

                bldLayer.Tiles[cx, cy] = null;
                frontLayer.Tiles[cx, cy] = null;
                alwaysFrontLayer.Tiles[cx, cy] = null;

                if (backLayer.Tiles[cx, cy] != null)
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

            // 7.b Mantener la entrada y su corredor libres de edificios sin cambiar su transitabilidad.
            var entranceTiles = new HashSet<(int X, int Y)>(openingBorderTiles);
            foreach (var ct in corridorTiles) entranceTiles.Add(ct);
            foreach (var (ex, ey) in entranceTiles)
            {
                if (ex >= 0 && ex < width && ey >= 0 && ey < height && backLayer.Tiles[ex, ey] is Tile entranceTile)
                    entranceTile.Properties["Buildable"] = "F";
            }

            // 8. Garantizar zonas de bonificación válidas y diseño meticuloso de cada minimapa (TASK-902)
            EnsureAnnexBonusZones(template, width, height, backLayer, bldLayer, frontLayer, pathsLayer, mainSheet, corridorTiles);
            SmoothThematicAnnexBordersAndEntrances(template, side, width, height, backLayer, bldLayer, frontLayer, alwaysFrontLayer, mainSheet, openingBorderTiles);

            return target;
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

            // D) Remanso Fluvial: la plantilla fluvial vanilla de Farm_Fishing (recorte 16, 18) ya cuenta
            // de forma natural con 346 casillas de agua de río y 247 casillas de orilla pescable limpia alrededor
            // de la isla. No se recorta ningún hueco artificial en medio de la tierra, preservando íntegros los
            // acantilados, la vegetación y la costa orgánica de Stardew Valley.
        }

        /// <summary>
        /// Aplica un diseño meticuloso y acabado artesanal para los bordes, orillas y corredores de entrada
        /// de cada minimapa temático (40×32), asegurando continuidad visual, eliminación de muñones de puentes
        /// y remates orgánicos según la naturaleza de cada granja.
        /// </summary>
        private static void SmoothThematicAnnexBordersAndEntrances(
            AnnexTemplate template,
            AnnexSide side,
            int width,
            int height,
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer alwaysFrontLayer,
            TileSheet? mainSheet,
            HashSet<(int X, int Y)> openingBorderTiles)
        {
            if (mainSheet == null)
                return;

            string id = template.Id;

            // 1. Remates naturales de colisión en los 4 bordes:
            // Donde un puente o pasarela de madera llega a un borde sin ser la entrada, se remata con una barandilla
            // de madera canónica de Stardew Valley (194 en Norte/Sur, 195 en Oeste, 193 en Este) con colisión visible,
            // convirtiéndolo en un muelle o mirador acabado sin muros invisibles.
            // Donde un camino abierto de tierra/empedrado llega al borde, se remata con una valla o poste rústico.
            for (int x = 0; x < width; x++)
            {
                // Borde Norte (y = 0)
                if (!openingBorderTiles.Contains((x, 0)))
                {
                    int? bldIdx = bldLayer.Tiles[x, 0]?.TileIndex;
                    int? backIdx = backLayer.Tiles[x, 0]?.TileIndex;
                    if ((bldIdx.HasValue && BridgeBuildingsTileIndices.Contains(bldIdx.Value)) || (backIdx.HasValue && WoodPlankBackTileIndices.Contains(backIdx.Value)))
                    {
                        SetTileIfValid(bldLayer, mainSheet, x, 0, 194);
                        SetPassableIfValid(bldLayer, x, 0, false);
                    }
                    else if (bldIdx == null && backIdx.HasValue && OpenPathBackTileIndices.Contains(backIdx.Value))
                    {
                        // En y = 0 no se puede colocar la base de una valla horizontal de 2 tiles (requeriría y = -1 en Front).
                        // Se remata con la barandilla rústica canónica de 1 casilla (194), cerrando limpiamente el camino sin decapitaciones.
                        SetTileIfValid(bldLayer, mainSheet, x, 0, 194);
                        SetPassableIfValid(bldLayer, x, 0, false);
                    }
                }

                // Borde Sur (y = height - 1)
                int southY = height - 1;
                if (!openingBorderTiles.Contains((x, southY)))
                {
                    int? bldIdx = bldLayer.Tiles[x, southY]?.TileIndex;
                    int? backIdx = backLayer.Tiles[x, southY]?.TileIndex;
                    if ((bldIdx.HasValue && BridgeBuildingsTileIndices.Contains(bldIdx.Value)) || (backIdx.HasValue && WoodPlankBackTileIndices.Contains(backIdx.Value)))
                    {
                        SetTileIfValid(bldLayer, mainSheet, x, southY, 194);
                        SetPassableIfValid(bldLayer, x, southY, false);
                    }
                    else if (bldIdx == null && backIdx.HasValue && OpenPathBackTileIndices.Contains(backIdx.Value))
                    {
                        // Rematar con barandilla rústica de madera (194) en el límite sur para un cierre limpio y homogéneo
                        SetTileIfValid(bldLayer, mainSheet, x, southY, 194);
                        SetPassableIfValid(bldLayer, x, southY, false);
                    }
                }
            }

            for (int y = 0; y < height; y++)
            {
                // Borde Oeste (x = 0)
                if (!openingBorderTiles.Contains((0, y)))
                {
                    int? bldIdx = bldLayer.Tiles[0, y]?.TileIndex;
                    int? backIdx = backLayer.Tiles[0, y]?.TileIndex;
                    if ((bldIdx.HasValue && BridgeBuildingsTileIndices.Contains(bldIdx.Value)) || (backIdx.HasValue && WoodPlankBackTileIndices.Contains(backIdx.Value)))
                    {
                        SetTileIfValid(bldLayer, mainSheet, 0, y, 195);
                        SetPassableIfValid(bldLayer, 0, y, false);
                    }
                    else if (bldIdx == null && backIdx.HasValue && OpenPathBackTileIndices.Contains(backIdx.Value))
                    {
                        // En la columna vertical x = 0, usar la barandilla rústica vertical oeste (195) en lugar de fragmentos horizontales
                        SetTileIfValid(bldLayer, mainSheet, 0, y, 195);
                        SetPassableIfValid(bldLayer, 0, y, false);
                    }
                }

                // Borde Este (x = width - 1)
                int eastX = width - 1;
                if (!openingBorderTiles.Contains((eastX, y)))
                {
                    int? bldIdx = bldLayer.Tiles[eastX, y]?.TileIndex;
                    int? backIdx = backLayer.Tiles[eastX, y]?.TileIndex;
                    if ((bldIdx.HasValue && BridgeBuildingsTileIndices.Contains(bldIdx.Value)) || (backIdx.HasValue && WoodPlankBackTileIndices.Contains(backIdx.Value)))
                    {
                        SetTileIfValid(bldLayer, mainSheet, eastX, y, 193);
                        SetPassableIfValid(bldLayer, eastX, y, false);
                    }
                    else if (bldIdx == null && backIdx.HasValue && OpenPathBackTileIndices.Contains(backIdx.Value))
                    {
                        // En la columna vertical x = eastX, usar la barandilla rústica vertical este (193) en lugar de fragmentos horizontales
                        SetTileIfValid(bldLayer, mainSheet, eastX, y, 193);
                        SetPassableIfValid(bldLayer, eastX, y, false);
                    }
                }
            }

            // 2. Acabados específicos por plantilla temática
            if (string.Equals(id, "Fishing", StringComparison.OrdinalIgnoreCase))
            {
                // Prolongar puente oeste hacia el borde en x=1
                SetTileIfValid(backLayer, mainSheet, 1, 19, 1244);
                SetTileIfValid(bldLayer, mainSheet, 1, 19, 211);
                SetPassableIfValid(bldLayer, 1, 19, true);

                // Rematar el muelle oeste con barandilla canónica en el límite del mapa
                SetTileIfValid(backLayer, mainSheet, 0, 19, 1244);
                SetTileIfValid(bldLayer, mainSheet, 0, 19, 195);
                SetPassableIfValid(bldLayer, 0, 19, false);

                SetTileIfValid(backLayer, mainSheet, 0, 20, 1269);
                SetTileIfValid(backLayer, mainSheet, 1, 20, 1269);

                // Entrada Oeste (si el anexo se accede desde el Este de la residencia, x=0..1, y=16..17)
                if (side == AnnexSide.East)
                {
                    SetTileIfValid(backLayer, mainSheet, 0, 16, 1244);
                    SetTileIfValid(bldLayer, mainSheet, 0, 16, 184);
                    SetPassableIfValid(bldLayer, 0, 16, true);

                    SetTileIfValid(backLayer, mainSheet, 0, 17, 1244);
                    SetTileIfValid(bldLayer, mainSheet, 0, 17, 211);
                    SetPassableIfValid(bldLayer, 0, 17, true);
                }

                // Embarcadero Sur (x=20, y=30..31): si es la entrada, asegurar transito
                if (openingBorderTiles.Contains((20, 31)))
                {
                    SetPassableIfValid(bldLayer, 20, 31, true);
                }
            }
            else if (string.Equals(id, "Mining", StringComparison.OrdinalIgnoreCase))
            {
                // Cantera de Minerales: desfiladero oeste (x=0..2, y=16..17)
                // Limpiar tiles frontales residuales para evitar fragmentos de copa o risco flotantes en la embocadura
                if (frontLayer != null)
                {
                    for (int x = 0; x <= 2; x++)
                    {
                        if (x < frontLayer.LayerWidth)
                        {
                            if (16 < frontLayer.LayerHeight) frontLayer.Tiles[x, 16] = null;
                            if (17 < frontLayer.LayerHeight) frontLayer.Tiles[x, 17] = null;
                        }
                    }
                }
            }
            else if (string.Equals(id, "Ranching", StringComparison.OrdinalIgnoreCase))
            {
                // Pasto de Pradera Azul: Si el corredor de entrada cruza agua, dotarlo de pasarela de madera transitable
                if (side == AnnexSide.North)
                {
                    // Entrada Sur (y = 31)
                    if (backLayer.Tiles[19, 31] != null && HasWaterProperty(backLayer.Tiles[19, 31]))
                    {
                        SetTileIfValid(backLayer, mainSheet, 19, 31, 1244);
                        SetTileIfValid(bldLayer, mainSheet, 19, 31, 184);
                        SetPassableIfValid(bldLayer, 19, 31, true);
                    }
                    if (backLayer.Tiles[20, 31] != null && HasWaterProperty(backLayer.Tiles[20, 31]))
                    {
                        SetTileIfValid(backLayer, mainSheet, 20, 31, 1244);
                        SetTileIfValid(bldLayer, mainSheet, 20, 31, 211);
                        SetPassableIfValid(bldLayer, 20, 31, true);
                    }
                }
                else if (side == AnnexSide.South)
                {
                    // Entrada Norte (y = 0)
                    if (backLayer.Tiles[19, 0] != null && HasWaterProperty(backLayer.Tiles[19, 0]))
                    {
                        SetTileIfValid(backLayer, mainSheet, 19, 0, 1244);
                        SetTileIfValid(bldLayer, mainSheet, 19, 0, 184);
                        SetPassableIfValid(bldLayer, 19, 0, true);
                    }
                }
            }
            else if (string.Equals(id, "FourCorners", StringComparison.OrdinalIgnoreCase))
            {
                // Cuadrante Mixto: Paso oeste entre desniveles en y=16..17
                if (frontLayer != null)
                {
                    for (int x = 0; x <= 2; x++)
                    {
                        if (x < frontLayer.LayerWidth)
                        {
                            if (16 < frontLayer.LayerHeight) frontLayer.Tiles[x, 16] = null;
                            if (17 < frontLayer.LayerHeight) frontLayer.Tiles[x, 17] = null;
                        }
                    }
                }

                // 1. Restaurar el dosel del cerezo en flor en AlwaysFront en el puente central (x=19..20, y=25):
                // Los tiles 45 y 46 completan la copa circular sobre el risco, evitando que el risco en Front
                // recorte indebidamente las flores rosadas y permitiendo pasar bajo el follaje orgánicamente.
                SetTileIfValid(alwaysFrontLayer, mainSheet, 19, 25, 45);
                SetTileIfValid(alwaysFrontLayer, mainSheet, 20, 25, 46);

                // 2. Arreglo de superposición de capas en el desfiladero/rampa noroeste (x=10..13, y=4..5):
                // Mover los bordes de risco (416, 441 a la derecha; 419, 444 a la izquierda) de Front a Buildings con Passable="F".
                // Esto previene que cualquier risco en Front recorte indebidamente la copa de árboles colindantes.
                if (frontLayer != null)
                {
                    if (frontLayer.Tiles[13, 4]?.TileIndex == 416)
                    {
                        SetTileIfValid(bldLayer, mainSheet, 13, 4, 416);
                        SetPassableIfValid(bldLayer, 13, 4, false);
                        frontLayer.Tiles[13, 4] = null;
                    }
                    if (frontLayer.Tiles[13, 5]?.TileIndex == 441)
                    {
                        SetTileIfValid(bldLayer, mainSheet, 13, 5, 441);
                        SetPassableIfValid(bldLayer, 13, 5, false);
                        frontLayer.Tiles[13, 5] = null;
                    }
                    if (frontLayer.Tiles[10, 4]?.TileIndex == 419)
                    {
                        SetTileIfValid(bldLayer, mainSheet, 10, 4, 419);
                        SetPassableIfValid(bldLayer, 10, 4, false);
                        frontLayer.Tiles[10, 4] = null;
                    }
                    if (frontLayer.Tiles[10, 5]?.TileIndex == 444)
                    {
                        SetTileIfValid(bldLayer, mainSheet, 10, 5, 444);
                        SetPassableIfValid(bldLayer, 10, 5, false);
                        frontLayer.Tiles[10, 5] = null;
                    }
                }
            }
            else if (string.Equals(id, "Foraging", StringComparison.OrdinalIgnoreCase))
            {
                // Franja Forestal: Limpiar restos en Front sobre el pasillo oeste de entrada
                if (frontLayer != null)
                {
                    for (int x = 0; x <= 2; x++)
                    {
                        if (x < frontLayer.LayerWidth)
                        {
                            if (16 < frontLayer.LayerHeight) frontLayer.Tiles[x, 16] = null;
                            if (17 < frontLayer.LayerHeight) frontLayer.Tiles[x, 17] = null;
                        }
                    }
                }

                // Componer los bordes del camino entre los árboles en la entrada oeste (side == East):
                // En lugar de un corte lineal y abrupto a través del muro vegetal, se esculpen los contornos
                // del dosel arbóreo en AlwaysFront con franjas colgantes naturales (968) en el dintel superior (y=15),
                // copas redondeadas (1043) en el límite inferior (y=18) y acabado suave hacia el interior.
                // Asimismo, en Back se sustituyen los cortes de tierra amarilla artificial en el borde (227, 228)
                // por hierba de bosque frondosa (175) para que el sendero se integre armónicamente.
                if (side == AnnexSide.East)
                {
                    // Dintel superior del túnel arbóreo en y=15 (AlwaysFront): hojas colgantes naturales
                    SetTileIfValid(alwaysFrontLayer, mainSheet, 0, 15, 968);
                    SetTileIfValid(alwaysFrontLayer, mainSheet, 1, 15, 968);
                    if (alwaysFrontLayer != null && (alwaysFrontLayer.Tiles[2, 15]?.TileIndex == 946 || alwaysFrontLayer.Tiles[2, 15] == null))
                    {
                        SetTileIfValid(alwaysFrontLayer, mainSheet, 2, 15, 968);
                    }

                    // Base inferior del túnel arbóreo en y=18 (AlwaysFront): copas redondeadas naturales
                    SetTileIfValid(alwaysFrontLayer, mainSheet, 0, 18, 1043);
                    SetTileIfValid(alwaysFrontLayer, mainSheet, 1, 18, 1043);
                    if (alwaysFrontLayer != null && (alwaysFrontLayer.Tiles[2, 18]?.TileIndex == 946 || alwaysFrontLayer.Tiles[2, 18] == null))
                    {
                        SetTileIfValid(alwaysFrontLayer, mainSheet, 2, 18, 1043);
                    }

                    // Suelo orgánico en Back: reemplazar tierra amarilla en x=0..1 por hierba de bosque auténtica (175)
                    for (int x = 0; x <= 1; x++)
                    {
                        for (int y = 16; y <= 17; y++)
                        {
                            int? curBack = backLayer.Tiles[x, y]?.TileIndex;
                            if (curBack == 227 || curBack == 228 || curBack == 587)
                            {
                                SetTileIfValid(backLayer, mainSheet, x, y, 175);
                                if (backLayer.Tiles[x, y] != null)
                                {
                                    backLayer.Tiles[x, y].Properties["Type"] = "Grass";
                                }
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
