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
        /// <summary>
        /// Genera el minimapa temático (40×32) para un anexo mediante composición estrictamente vanilla (TASK-911):
        /// 1. Recorta la ventana 40×32 seleccionada en <see cref="AnnexWindowLayout"/> conservando bordes y salidas naturales.
        /// 2. Pega bloques vanilla completos del catálogo (<see cref="VanillaBlockCatalog"/>) en los lados cortados.
        /// 3. Sella el perímetro con Passable = "F" salvo la apertura única de entrada.
        /// 4. Despeja el umbral de entrada sin añadir ningún StaticTile con índice sintético (Regla de Oro de Épica 9.1).
        /// </summary>
        public static Map CreateAnnexMap(
            Map source,
            AnnexTemplate template,
            AnnexSide side,
            Func<string, Map?>? mapLoader = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (template == null)
                throw new ArgumentNullException(nameof(template));

            int width = template.Width;
            int height = template.Height;
            AnnexWindowLayout layout = template.GetWindowLayoutForSide(side);

            Map target = new Map($"Pelican4Ever_Annex_{template.Id}_{side}")
            {
                Description = $"Annex {template.DisplayName} ({side})"
            };

            // 1. Copiar propiedades globales seguras (sin Warp ni coordenadas fijas de granja vanilla)
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

            // Bonificaciones determinadas por la ventana o la plantilla
            if (layout.MountainOreArea is { } oreArea)
                target.Properties["SpawnMountainFarmOreRect"] = oreArea.ToMapProperty();
            else if (template.Behavior.MountainOreArea is { } defOreArea)
                target.Properties["SpawnMountainFarmOreRect"] = defOreArea.ToMapProperty();

            if (template.Behavior.ForestForage)
                target.Properties["SpawnForestFarmForage"] = "T";
            if (!string.IsNullOrWhiteSpace(template.FishLocationOverride))
                target.Properties["FarmFishLocationOverride"] = template.FishLocationOverride;

            // 2. Copiar TileSheets
            CopyTileSheets(source, target);

            int srcMaxW = source.Layers.Count > 0 ? source.Layers[0].LayerWidth : width;
            int srcMaxH = source.Layers.Count > 0 ? source.Layers[0].LayerHeight : height;
            int srcX = Math.Clamp(layout.CropX, 0, Math.Max(0, srcMaxW - width));
            int srcY = Math.Clamp(layout.CropY, 0, Math.Max(0, srcMaxH - height));
            Size tileSize = source.Layers.Count > 0 ? source.Layers[0].TileSize : new Size(64, 64);

            // 3. Recortar cada capa a 40×32 desde la ventana y limpiar warps vanilla residuales
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
            EnsureLayer(target, "Paths", width, height, tileSize);

            // 4. Pegar bloques vanilla completos del catálogo en los lados cortados (TASK-909 / TASK-911)
            if (layout.BlockIdsToPaste != null)
            {
                foreach (string blockId in layout.BlockIdsToPaste)
                {
                    if (!VanillaBlockCatalog.Blocks.TryGetValue(blockId, out VanillaBlockDefinition? blockDef))
                        continue;

                    Map? blockSourceMap = mapLoader != null
                        ? (mapLoader(blockDef.SourceMapAsset) ?? source)
                        : source;

                    if (blockSourceMap == null)
                        continue;

                    CopyTileSheets(blockSourceMap, target);

                    int dx = 0;
                    int dy = 0;
                    if (blockDef.Kind == BlockKind.EdgeStrip)
                    {
                        switch (blockDef.Orientation)
                        {
                            case BlockOrientation.North:
                                dx = 0; dy = 0; break;
                            case BlockOrientation.South:
                                dx = 0; dy = height - blockDef.SourceRectangle.Height; break;
                            case BlockOrientation.West:
                                dx = 0; dy = 0; break;
                            case BlockOrientation.East:
                                dx = width - blockDef.SourceRectangle.Width; dy = 0; break;
                        }
                    }
                    else if (blockDef.Kind == BlockKind.Corner)
                    {
                        switch (blockDef.Orientation)
                        {
                            case BlockOrientation.NorthWest:
                                dx = 0; dy = 0; break;
                            case BlockOrientation.NorthEast:
                                dx = width - blockDef.SourceRectangle.Width; dy = 0; break;
                            case BlockOrientation.SouthWest:
                                dx = 0; dy = height - blockDef.SourceRectangle.Height; break;
                            case BlockOrientation.SouthEast:
                                dx = width - blockDef.SourceRectangle.Width; dy = height - blockDef.SourceRectangle.Height; break;
                        }
                    }
                    else if (blockDef.Kind == BlockKind.Doorway)
                    {
                        dx = Math.Clamp(layout.EntranceTile.X - (blockDef.DoorwayStepTile?.X ?? 0), 0, width - blockDef.SourceRectangle.Width);
                        dy = Math.Clamp(layout.EntranceTile.Y - (blockDef.DoorwayStepTile?.Y ?? 0), 0, height - blockDef.SourceRectangle.Height);
                    }

                    foreach (Layer srcBlockLayer in blockSourceMap.Layers)
                    {
                        Layer targetLayer = EnsureLayer(target, srcBlockLayer.Id, width, height, tileSize);

                        for (int bx = 0; bx < blockDef.SourceRectangle.Width; bx++)
                        {
                            int sx = blockDef.SourceRectangle.X + bx;
                            int tx = dx + bx;
                            if (tx < 0 || tx >= width || sx < 0 || sx >= srcBlockLayer.LayerWidth)
                                continue;

                            for (int by = 0; by < blockDef.SourceRectangle.Height; by++)
                            {
                                int sy = blockDef.SourceRectangle.Y + by;
                                int ty = dy + by;
                                if (ty < 0 || ty >= height || sy < 0 || sy >= srcBlockLayer.LayerHeight)
                                    continue;

                                Tile? srcTile = srcBlockLayer.Tiles[sx, sy];
                                if (srcTile != null)
                                {
                                    Tile cloned = CloneTile(srcTile, targetLayer, target);
                                    StripVanillaWarpTileProperties(cloned);
                                    targetLayer.Tiles[tx, ty] = cloned;
                                }
                            }
                        }
                    }
                }
            }

            // 5. Calcular apertura de entrada y corredor según coordenadas de ventana
            GetEntranceOpeningAndCorridor(side, width, height, out HashSet<(int X, int Y)> openingBorderTiles, out HashSet<(int X, int Y)> corridorTiles, layout.EntranceTile);

            // 6. Sellar todo el perímetro (4 bordes) con Passable = "F" salvo la apertura de entrada
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    bool isBorder = x == 0 || x == width - 1 || y == 0 || y == height - 1;
                    if (!isBorder || openingBorderTiles.Contains((x, y)))
                        continue;

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

            // 7. Despejar apertura y corredor de entrada sin añadir ningún tile sintético (Regla de Oro)
            foreach (var (cx, cy) in corridorTiles.Concat(openingBorderTiles))
            {
                if (cx < 0 || cx >= width || cy < 0 || cy >= height)
                    continue;

                bldLayer.Tiles[cx, cy] = null;
                frontLayer.Tiles[cx, cy] = null;
                alwaysFrontLayer.Tiles[cx, cy] = null;

                if (backLayer.Tiles[cx, cy] != null)
                {
                    ClearBlockingAndWaterProperties(backLayer.Tiles[cx, cy]);
                    backLayer.Tiles[cx, cy].Properties["Passable"] = "T";
                }
            }

            return target;
        }

        public static void GetEntranceOpeningAndCorridor(
            AnnexSide side,
            int width,
            int height,
            out HashSet<(int X, int Y)> openingBorderTiles,
            out HashSet<(int X, int Y)> corridorTiles,
            TilePoint? customEntrance = null)
        {
            openingBorderTiles = new HashSet<(int X, int Y)>();
            corridorTiles = new HashSet<(int X, int Y)>();

            AnnexWarpPoints pts = AnnexEntrance.For(side, width, height, null, customEntrance);
            int ex = pts.ReturnExitTile.X;
            int ey = pts.ReturnExitTile.Y;

            switch (side)
            {
                case AnnexSide.North:
                    // Entrada por el borde Sur del anexo (y = height - 1)
                    int o1 = (ex > 0) ? ex - 1 : ex;
                    int o2 = (ex > 0) ? ex : ex + 1;
                    openingBorderTiles.Add((o1, height - 1));
                    openingBorderTiles.Add((o2, height - 1));
                    for (int y = Math.Max(1, height - 7); y < height; y++)
                    {
                        corridorTiles.Add((o1, y));
                        corridorTiles.Add((o2, y));
                    }
                    break;

                case AnnexSide.South:
                    // Entrada por el borde Norte del anexo (y = 0)
                    int so1 = (ex > 0) ? ex - 1 : ex;
                    int so2 = (ex > 0) ? ex : ex + 1;
                    openingBorderTiles.Add((so1, 0));
                    openingBorderTiles.Add((so2, 0));
                    for (int y = 0; y <= Math.Min(height - 2, 6); y++)
                    {
                        corridorTiles.Add((so1, y));
                        corridorTiles.Add((so2, y));
                    }
                    break;

                case AnnexSide.East:
                    // Entrada por el borde Oeste del anexo (x = 0)
                    int eo1 = (ey > 0) ? ey - 1 : ey;
                    int eo2 = (ey > 0) ? ey : ey + 1;
                    openingBorderTiles.Add((0, eo1));
                    openingBorderTiles.Add((0, eo2));
                    for (int x = 0; x <= Math.Min(width - 2, 6); x++)
                    {
                        corridorTiles.Add((x, eo1));
                        corridorTiles.Add((x, eo2));
                    }
                    break;
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
