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
        /// <summary>
        /// Genera el minimapa temático (40×32) para un anexo mediante composición vanilla pura estilo «Mapas Puente» (TASK-911):
        /// 1. Recorta el rectángulo canónico 40×32 del tipo (<see cref="AnnexTemplate.SourceCropX"/>, <see cref="AnnexTemplate.SourceCropY"/>).
        /// 2. Limpia propiedades de warp y acciones de mapa vanilla residuales.
        /// 3. Sella el perímetro (4 bordes) con Passable = "F" salvo la apertura de 2 casillas activa.
        /// 4. Despeja la apertura, corredor y área de seguridad interior de 3×4 casillas:
        ///    - En Back: elimina 'Passable', 'Water' y 'NoSpawn'. NUNCA asigna Passable="T" en Back (regla GameLocation.isTilePassable).
        ///    - Si el suelo era sombra oscura (351..401) o agua sin puente, restaura terreno canónico limpio de TerrainTileClassifier.
        ///    - En Buildings/Front/AlwaysFront: despeja obstáculos y sanea árboles cortados con VerticalMultiTileClassifier.
        ///    - Flanquea la apertura con cancelas/postes de valla canónicos.
        /// 5. Sanea los bordes del recorte para eliminar troncos decapitados en y=0 y copas huérfanas.
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
            Layer? alwaysFront2Layer = target.GetLayer("AlwaysFront2");
            Layer? alwaysFront3Layer = target.GetLayer("AlwaysFront3");
            EnsureLayer(target, "Paths", width, height, tileSize);
            TileSheet? outdoorsSheet = GetOutdoorsTileSheet(target);

            // 4. Apertura de entrada y área interior de 3×4 casillas (TASK-907 / TASK-911)
            GetEntranceOpeningAndCorridor(side, width, height, out HashSet<(int X, int Y)> openingBorderTiles, out HashSet<(int X, int Y)> corridorTiles, layout.EntranceTile);
            TileArea clearanceArea = AnnexValidationLogic.GetInteriorClearanceArea(side, layout.EntranceTile, width, height);

            // 5. Sellar todo el perímetro (4 bordes) con Passable = "F" salvo la apertura de entrada
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

            // 6. Conjunto total de casillas a despejar: apertura + corredor + área de seguridad interior 3×4
            var clearTiles = new HashSet<(int X, int Y)>(openingBorderTiles);
            foreach (var ct in corridorTiles) clearTiles.Add(ct);
            for (int cx = clearanceArea.X; cx < clearanceArea.X + clearanceArea.Width; cx++)
            {
                for (int cy = clearanceArea.Y; cy < clearanceArea.Y + clearanceArea.Height; cy++)
                {
                    clearTiles.Add((cx, cy));
                }
            }

            // Saneado previo de árboles que intersecten el área de entrada por su caja completa (VerticalMultiTileClassifier)
            bool isCustomComposed = template.Id.Equals("Standard", StringComparison.OrdinalIgnoreCase)
                || template.Id.Equals("Fishing", StringComparison.OrdinalIgnoreCase);

            if (!isCustomComposed)
            {
                foreach (var (cx, cy) in clearTiles)
                {
                    if (cx < 0 || cx >= width || cy < 0 || cy >= height)
                        continue;

                    int? bldIdx = bldLayer.Tiles[cx, cy]?.TileIndex;
                    if (bldIdx.HasValue && VerticalMultiTileClassifier.IsTreeTrunkBase(bldIdx.Value))
                    {
                        TileArea treeBox = VerticalMultiTileClassifier.GetStaticTreeBoundingBox(cx, cy, bldIdx.Value);
                        ClearMapBox(treeBox, bldLayer, frontLayer, alwaysFrontLayer, alwaysFront2Layer, alwaysFront3Layer, width, height);
                    }
                }
            }

            // Despejar capas superiores y normalizar suelo en Back (regla de oro: NUNCA asignar Passable="T" en Back)

            foreach (var (cx, cy) in clearTiles)
            {
                if (cx < 0 || cx >= width || cy < 0 || cy >= height)
                    continue;

                int? currentBldIdx = bldLayer.Tiles[cx, cy]?.TileIndex;
                bool isBridge = currentBldIdx.HasValue && TerrainTileClassifier.IsWoodenBridgeTile(currentBldIdx.Value);

                if (isBridge && bldLayer.Tiles[cx, cy] != null)
                {
                    // Preservar la plataforma de madera del puente y marcarla explícitamente transitable
                    bldLayer.Tiles[cx, cy].Properties["Passable"] = "T";
                }
                else if (!isCustomComposed)
                {
                    bldLayer.Tiles[cx, cy] = null;
                }

                if (!isCustomComposed)
                {
                    frontLayer.Tiles[cx, cy] = null;
                }
                // NOTA CRÍTICA: NUNCA borrar AlwaysFront en clearTiles. En SDV las copas no tienen colisión
                // y borrarlas crea agujeros rectangulares antiestéticos en el cielo (TASK-911/V4).

                if (backLayer.Tiles[cx, cy] != null)
                {
                    int backIdx = backLayer.Tiles[cx, cy].TileIndex;
                    bool isWater = HasWaterProperty(backLayer.Tiles[cx, cy]) || backIdx >= 1200;
                    bool isShade = TerrainTileClassifier.IsForestShadeGrass(backIdx);

                    if (isBridge && isWater)
                    {
                        // Si hay puente de madera sobre agua: mantener el agua animada en Back y retirar Passable de Back
                        backLayer.Tiles[cx, cy].Properties.Remove("Passable");
                    }
                    else if (!isBridge && !isCustomComposed)
                    {
                        ClearBlockingAndWaterProperties(backLayer.Tiles[cx, cy]);
                        if (isWater || isShade || (outdoorsSheet != null && backLayer.Tiles[cx, cy].TileSheet == outdoorsSheet && backIdx == 0))
                        {
                            if (outdoorsSheet != null)
                            {
                                int groundIdx = (cy == height - 1 || cy == 0 || cx == 0 || cx == width - 1)
                                    ? TerrainTileClassifier.DirtPlainTileIndex
                                    : TerrainTileClassifier.SunnyGrassTileIndex;
                                var newTile = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, groundIdx);
                                newTile.Properties["Type"] = (groundIdx == TerrainTileClassifier.DirtPlainTileIndex) ? "Dirt" : "Grass";
                                backLayer.Tiles[cx, cy] = newTile;
                            }
                        }
                    }
                }
            }

            // 7. Composición específica por variante (sin salidas muertas) y saneado del perímetro 40×32
            if (outdoorsSheet != null)
            {
                if (template.Id.Equals("Standard", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyStandardFarmEnclosure(source, target, backLayer, bldLayer, frontLayer, alwaysFrontLayer, outdoorsSheet, side, srcX, srcY, width, height);
                }
                else if (template.Id.Equals("Fishing", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyRiverlandFarmEnclosure(source, target, backLayer, bldLayer, frontLayer, alwaysFrontLayer, outdoorsSheet, side, srcY, width, height);
                }
                else
                {
                    FlankOpeningWithFencePosts(side, openingBorderTiles, bldLayer, frontLayer, outdoorsSheet, width, height);
                }

                SanitizeAnnexMultiTileObjects(backLayer, bldLayer, frontLayer, alwaysFrontLayer, alwaysFront2Layer, outdoorsSheet, width, height);
            }

            // 8. Re-asegurar el sellado perimetral en los 4 bordes salvo únicamente las 2 casillas de la apertura activa
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    bool isBorder = x == 0 || x == width - 1 || y == 0 || y == height - 1;
                    if (!isBorder)
                        continue;

                    if (openingBorderTiles.Contains((x, y)))
                    {
                        backLayer.Tiles[x, y]?.Properties.Remove("Passable");
                    }
                    else
                    {
                        if (bldLayer.Tiles[x, y] != null)
                            bldLayer.Tiles[x, y].Properties["Passable"] = "F";
                        if (backLayer.Tiles[x, y] != null)
                            backLayer.Tiles[x, y].Properties["Passable"] = "F";
                    }
                }
            }

            return target;
        }

        private static void ClearMapBox(
            TileArea box,
            Layer bld,
            Layer front,
            Layer alwaysFront,
            Layer? alwaysFront2,
            Layer? alwaysFront3,
            int mapWidth,
            int mapHeight)
        {
            for (int x = box.X; x < box.X + box.Width; x++)
            {
                if (x < 0 || x >= mapWidth) continue;
                for (int y = box.Y; y < box.Y + box.Height; y++)
                {
                    if (y < 0 || y >= mapHeight) continue;
                    bld.Tiles[x, y] = null;
                    front.Tiles[x, y] = null;
                    alwaysFront.Tiles[x, y] = null;
                    if (alwaysFront2 != null) alwaysFront2.Tiles[x, y] = null;
                    if (alwaysFront3 != null) alwaysFront3.Tiles[x, y] = null;
                }
            }
        }

        private static void FlankOpeningWithFencePosts(
            AnnexSide side,
            HashSet<(int X, int Y)> openingBorderTiles,
            Layer bldLayer,
            Layer frontLayer,
            TileSheet outdoorsSheet,
            int width,
            int height)
        {
            if (openingBorderTiles.Count == 0) return;

            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;
            foreach (var (x, y) in openingBorderTiles)
            {
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            switch (side)
            {
                case AnnexSide.North:
                    // Entrada por el sur (y = height - 1)
                    if (minX - 1 >= 0)
                    {
                        SetTileIfValid(bldLayer, outdoorsSheet, minX - 1, height - 1, VerticalMultiTileClassifier.FenceRightBaseTileIndex);
                        SetTileIfValid(frontLayer, outdoorsSheet, minX - 1, height - 2, VerticalMultiTileClassifier.FenceRightTopTileIndex);
                        SetPassableIfValid(bldLayer, minX - 1, height - 1, false);
                    }
                    if (maxX + 1 < width)
                    {
                        SetTileIfValid(bldLayer, outdoorsSheet, maxX + 1, height - 1, VerticalMultiTileClassifier.FenceLeftBaseTileIndex);
                        SetTileIfValid(frontLayer, outdoorsSheet, maxX + 1, height - 2, VerticalMultiTileClassifier.FenceLeftTopTileIndex);
                        SetPassableIfValid(bldLayer, maxX + 1, height - 1, false);
                    }
                    break;

                case AnnexSide.South:
                    // Entrada por el norte (y = 0)
                    if (minX - 1 >= 0)
                    {
                        SetTileIfValid(bldLayer, outdoorsSheet, minX - 1, 0, VerticalMultiTileClassifier.FenceVerticalBaseTileIndex);
                        SetPassableIfValid(bldLayer, minX - 1, 0, false);
                    }
                    if (maxX + 1 < width)
                    {
                        SetTileIfValid(bldLayer, outdoorsSheet, maxX + 1, 0, VerticalMultiTileClassifier.FenceVerticalBaseTileIndex);
                        SetPassableIfValid(bldLayer, maxX + 1, 0, false);
                    }
                    break;

                case AnnexSide.East:
                    // Entrada por el oeste (x = 0)
                    if (minY - 1 >= 0)
                    {
                        SetTileIfValid(bldLayer, outdoorsSheet, 0, minY - 1, VerticalMultiTileClassifier.FenceVerticalUpperMidTileIndex);
                        SetTileIfValid(frontLayer, outdoorsSheet, 0, minY - 2, VerticalMultiTileClassifier.FenceVerticalTopTileIndex);
                        SetPassableIfValid(bldLayer, 0, minY - 1, false);
                    }
                    if (maxY + 1 < height)
                    {
                        SetTileIfValid(bldLayer, outdoorsSheet, 0, maxY + 1, VerticalMultiTileClassifier.FenceVerticalBaseTileIndex);
                        SetPassableIfValid(bldLayer, 0, maxY + 1, false);
                    }
                    break;
            }
        }

        private static void CopySourceCellToTarget(Map source, Map target, int lx, int ly, int sx, int sy)
        {
            string[] layerNames = { "Back", "Buildings", "Front", "AlwaysFront" };
            foreach (string lname in layerNames)
            {
                Layer? srcLayer = source.GetLayer(lname);
                Layer? dstLayer = target.GetLayer(lname);
                if (srcLayer == null || dstLayer == null)
                    continue;

                if (lx >= 0 && lx < dstLayer.LayerWidth && ly >= 0 && ly < dstLayer.LayerHeight &&
                    sx >= 0 && sx < srcLayer.LayerWidth && sy >= 0 && sy < srcLayer.LayerHeight)
                {
                    Tile? srcTile = srcLayer.Tiles[sx, sy];
                    if (srcTile != null)
                    {
                        Tile cloned = CloneTile(srcTile, dstLayer, target);
                        StripVanillaWarpTileProperties(cloned);
                        dstLayer.Tiles[lx, ly] = cloned;
                    }
                    else
                    {
                        dstLayer.Tiles[lx, ly] = null;
                    }
                }
            }
        }

        private static void SetDirtTile(Layer backLayer, TileSheet outdoorsSheet, int x, int y, int tileIndex = TerrainTileClassifier.DirtPlainTileIndex)
        {
            if (x >= 0 && x < backLayer.LayerWidth && y >= 0 && y < backLayer.LayerHeight)
            {
                var dt = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, tileIndex);
                dt.Properties["Type"] = "Dirt";
                backLayer.Tiles[x, y] = dt;
            }
        }

        /// <summary>
        /// Compone la miniatura auténtica 40×32 de la granja Estándar (V8 aprobado) injertando los 4 bordes reales
        /// de Maps/Farm (acantilado y terraza forestal norte, talud herboso oeste con cerezos, valla y bosque este,
        /// y arboleda sur) y habilitando únicamente la entrada/salida activa (0 salidas muertas).
        /// </summary>
        private static void ApplyStandardFarmEnclosure(
            Map source,
            Map target,
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer alwaysFrontLayer,
            TileSheet outdoorsSheet,
            AnnexSide side,
            int srcX,
            int srcY,
            int width,
            int height)
        {
            // 1. BORDE NORTE (ly = 0..8): Injertar el acantilado de roca marrón y terraza forestal superior desde sy = 0..8
            int[] northSxMap = (side == AnnexSide.South)
                ? new int[]
                {
                    // Con el paso norte auténtico entre acantilados en lx = 22..23 (sx = 40..41)
                    0, 1, 2, 3, 4,
                    13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25,
                    36, 37, 38, 39,
                    40, 41,
                    42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57
                }
                : new int[]
                {
                    // Borde norte cerrado continuamente sin corte de paso de montaña
                    0, 1, 2, 3, 4,
                    13, 14, 15, 16, 17, 18, 19, 20, 21, 22,
                    26,
                    14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25,
                    46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57
                };

            for (int lx = 0; lx < width && lx < northSxMap.Length; lx++)
            {
                int sx = northSxMap[lx];
                for (int ly = 0; ly <= 8; ly++)
                {
                    CopySourceCellToTarget(source, target, lx, ly, sx, ly);
                }
            }

            if (side == AnnexSide.South)
            {
                frontLayer.Tiles[18, 0] = null;
                frontLayer.Tiles[19, 0] = null;
                frontLayer.Tiles[18, 1] = null;
                frontLayer.Tiles[19, 1] = null;
                SetDirtTile(backLayer, outdoorsSheet, 22, 8);
                SetDirtTile(backLayer, outdoorsSheet, 23, 8);
                for (int ly = 0; ly <= 7; ly++)
                {
                    backLayer.Tiles[22, ly]?.Properties.Remove("Passable");
                    backLayer.Tiles[23, ly]?.Properties.Remove("Passable");
                    bldLayer.Tiles[22, ly] = null;
                    bldLayer.Tiles[23, ly] = null;
                }
            }

            for (int lx = 3; lx <= 34; lx++)
            {
                if (side == AnnexSide.South && (lx == 22 || lx == 23))
                    continue;

                int? idx8 = backLayer.Tiles[lx, 8]?.TileIndex;
                if (idx8.HasValue && (idx8.Value == 512 || idx8.Value == 537 || idx8.Value == 562 || idx8.Value == 565 || idx8.Value == 566 || idx8.Value == 618 || idx8.Value == 645))
                {
                    SetDirtTile(backLayer, outdoorsSheet, lx, 8);
                }
            }
            SetDirtTile(backLayer, outdoorsSheet, 4, 7);

            // 2. BORDE OESTE (lx = 0..4): Injertar el talud herboso oeste con los 3 cerezos rosas y la esquina SO
            for (int lx = 0; lx <= 4; lx++)
            {
                for (int ly = 0; ly <= 12; ly++)
                {
                    CopySourceCellToTarget(source, target, lx, ly, lx, ly);
                }
            }

            for (int ly = 9; ly <= 12; ly++)
            {
                SetTileIfValid(backLayer, outdoorsSheet, 0, ly, 351);
                SetTileIfValid(bldLayer, outdoorsSheet, 0, ly, 16);
                SetPassableIfValid(bldLayer, 0, ly, false);

                SetTileIfValid(backLayer, outdoorsSheet, 1, ly, 352);
                SetTileIfValid(bldLayer, outdoorsSheet, 1, ly, 16);
                SetPassableIfValid(bldLayer, 1, ly, false);
            }

            for (int x = 2; x <= 4; x++)
            {
                for (int y = 7; y <= 12; y++)
                {
                    int? bIdx = backLayer.Tiles[x, y]?.TileIndex;
                    if (bIdx.HasValue && (bIdx.Value == 537 || bIdx.Value == 611 || bIdx.Value == 589 || bIdx.Value == 560 || bIdx.Value == 614))
                    {
                        SetDirtTile(backLayer, outdoorsSheet, x, y);
                    }
                }
            }

            for (int lx = 0; lx <= 3; lx++)
            {
                for (int ly = 13; ly < height; ly++)
                {
                    CopySourceCellToTarget(source, target, lx, ly, lx, 33 + ly);
                    if (ly <= 27 && lx == 3)
                    {
                        SetDirtTile(backLayer, outdoorsSheet, 3, ly);
                        bldLayer.Tiles[3, ly] = null;
                    }
                }
            }

            // Esquina interior SO en lx = 2, ly = 28..31 y restauración del árbol SO en lx = 3, ly = 24..31
            SetDirtTile(backLayer, outdoorsSheet, 2, 28);
            SetTileIfValid(bldLayer, outdoorsSheet, 2, 28, 412);
            SetPassableIfValid(bldLayer, 2, 28, false);
            SetTileIfValid(frontLayer, outdoorsSheet, 2, 28, 412);
            alwaysFrontLayer.Tiles[2, 28] = null;

            SetTileIfValid(backLayer, outdoorsSheet, 2, 29, 326);
            SetTileIfValid(bldLayer, outdoorsSheet, 2, 29, 326);
            SetPassableIfValid(bldLayer, 2, 29, false);
            SetTileIfValid(frontLayer, outdoorsSheet, 2, 29, 326);
            alwaysFrontLayer.Tiles[2, 29] = null;

            SetTileIfValid(backLayer, outdoorsSheet, 2, 30, 355);
            SetTileIfValid(bldLayer, outdoorsSheet, 2, 30, 355);
            SetPassableIfValid(bldLayer, 2, 30, false);
            frontLayer.Tiles[2, 30] = null;
            alwaysFrontLayer.Tiles[2, 30] = null;

            SetTileIfValid(backLayer, outdoorsSheet, 2, 31, 380);
            SetPassableIfValid(backLayer, 2, 31, false);
            SetTileIfValid(bldLayer, outdoorsSheet, 2, 31, 380);
            SetPassableIfValid(bldLayer, 2, 31, false);
            frontLayer.Tiles[2, 31] = null;
            alwaysFrontLayer.Tiles[2, 31] = null;

            for (int ly = 24; ly < height; ly++)
            {
                CopySourceCellToTarget(source, target, 3, ly, srcX + 3, srcY + ly);
            }

            if (side == AnnexSide.East)
            {
                // Entrada Oeste activa en lx = 0..3, ly = 18..19 enmarcada entre el acantilado de roca superior (ly = 14..17) y el talud herboso inferior (ly = 20..23)
                for (int lx = 0; lx <= 3; lx++)
                {
                    for (int ly = 13; ly <= 23; ly++)
                    {
                        frontLayer.Tiles[lx, ly] = null;
                        alwaysFrontLayer.Tiles[lx, ly] = null;
                    }
                }

                SetTileIfValid(backLayer, outdoorsSheet, 0, 13, 351);
                SetTileIfValid(bldLayer, outdoorsSheet, 0, 13, 16);
                SetPassableIfValid(bldLayer, 0, 13, false);

                SetTileIfValid(backLayer, outdoorsSheet, 1, 13, 352);
                SetTileIfValid(bldLayer, outdoorsSheet, 1, 13, 16);
                SetPassableIfValid(bldLayer, 1, 13, false);

                SetDirtTile(backLayer, outdoorsSheet, 2, 13);
                SetTileIfValid(bldLayer, outdoorsSheet, 2, 13, 444);
                SetPassableIfValid(bldLayer, 2, 13, false);

                (int C0, int C1, int C2)[] westCliffRows =
                {
                    (467, 468, 469),
                    (492, 493, 494),
                    (517, 518, 519),
                    (542, 543, 544)
                };

                for (int idxR = 0; idxR < westCliffRows.Length; idxR++)
                {
                    int ly = 14 + idxR;
                    var (c0, c1, c2) = westCliffRows[idxR];
                    if (idxR == 0)
                    {
                        SetTileIfValid(backLayer, outdoorsSheet, 0, ly, 351);
                        SetTileIfValid(backLayer, outdoorsSheet, 1, ly, 352);
                    }
                    else
                    {
                        SetDirtTile(backLayer, outdoorsSheet, 0, ly);
                        SetDirtTile(backLayer, outdoorsSheet, 1, ly);
                    }
                    SetDirtTile(backLayer, outdoorsSheet, 2, ly);

                    SetTileIfValid(bldLayer, outdoorsSheet, 0, ly, c0);
                    SetPassableIfValid(bldLayer, 0, ly, false);
                    SetTileIfValid(bldLayer, outdoorsSheet, 1, ly, c1);
                    SetPassableIfValid(bldLayer, 1, ly, false);
                    SetTileIfValid(bldLayer, outdoorsSheet, 2, ly, c2);
                    SetPassableIfValid(bldLayer, 2, ly, false);
                }

                for (int lx = 0; lx <= 3; lx++)
                {
                    for (int ly = 18; ly <= 19; ly++)
                    {
                        SetDirtTile(backLayer, outdoorsSheet, lx, ly);
                        bldLayer.Tiles[lx, ly] = null;
                        frontLayer.Tiles[lx, ly] = null;
                        alwaysFrontLayer.Tiles[lx, ly] = null;
                    }
                }
                SetTileIfValid(frontLayer, outdoorsSheet, 0, 19, 413);
                SetTileIfValid(frontLayer, outdoorsSheet, 1, 19, 414);
                SetTileIfValid(frontLayer, outdoorsSheet, 2, 19, 438);

                SetTileIfValid(backLayer, outdoorsSheet, 0, 20, 326);
                SetTileIfValid(bldLayer, outdoorsSheet, 0, 20, 326);
                SetPassableIfValid(bldLayer, 0, 20, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 0, 20, 326);

                SetTileIfValid(backLayer, outdoorsSheet, 1, 20, 326);
                SetTileIfValid(bldLayer, outdoorsSheet, 1, 20, 326);
                SetPassableIfValid(bldLayer, 1, 20, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 1, 20, 326);

                SetDirtTile(backLayer, outdoorsSheet, 2, 20);
                SetTileIfValid(bldLayer, outdoorsSheet, 2, 20, 394);
                SetPassableIfValid(bldLayer, 2, 20, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 2, 20, 394);

                for (int ly = 21; ly <= 23; ly++)
                {
                    SetTileIfValid(backLayer, outdoorsSheet, 0, ly, 351);
                    SetTileIfValid(bldLayer, outdoorsSheet, 0, ly, 16);
                    SetPassableIfValid(bldLayer, 0, ly, false);

                    SetTileIfValid(backLayer, outdoorsSheet, 1, ly, 352);
                    SetTileIfValid(bldLayer, outdoorsSheet, 1, ly, 16);
                    SetPassableIfValid(bldLayer, 1, ly, false);

                    SetDirtTile(backLayer, outdoorsSheet, 2, ly);
                    SetTileIfValid(bldLayer, outdoorsSheet, 2, ly, (ly % 2 != 0) ? 419 : 394);
                    SetPassableIfValid(bldLayer, 2, ly, false);
                }
            }

            // 3. BORDE ESTE (lx = 34..39, ly = 7..31): Injertar valla vertical este + franja boscosa con cerezos y robles
            for (int ly = 7; ly < height; ly++)
            {
                int sy = ly + 27;
                for (int lx = 36; lx < width; lx++)
                {
                    CopySourceCellToTarget(source, target, lx, ly, lx + 40, sy);
                }
                if (ly >= 26)
                {
                    for (int lx = 34; lx <= 35; lx++)
                    {
                        CopySourceCellToTarget(source, target, lx, ly, lx + 40, sy);
                    }
                }
            }

            for (int lx = 34; lx < width; lx++)
            {
                SetTileIfValid(backLayer, outdoorsSheet, lx, 30, 351);
                SetTileIfValid(backLayer, outdoorsSheet, lx, 31, 355);
                SetPassableIfValid(backLayer, lx, 31, false);
            }

            CopySourceCellToTarget(source, target, 37, 7, 55, 7);
            CopySourceCellToTarget(source, target, 38, 7, 56, 7);
            CopySourceCellToTarget(source, target, 39, 7, 57, 7);
            SetTileIfValid(bldLayer, outdoorsSheet, 37, 7, 386);
            SetPassableIfValid(bldLayer, 37, 7, false);

            SetTileIfValid(backLayer, outdoorsSheet, 36, 8, 612);
            bldLayer.Tiles[36, 8] = null;
            frontLayer.Tiles[36, 8] = null;

            SetTileIfValid(backLayer, outdoorsSheet, 37, 8, 594);
            SetTileIfValid(bldLayer, outdoorsSheet, 37, 8, 386);
            SetPassableIfValid(bldLayer, 37, 8, false);
            frontLayer.Tiles[37, 8] = null;

            SetTileIfValid(backLayer, outdoorsSheet, 38, 8, 350);
            SetTileIfValid(bldLayer, outdoorsSheet, 38, 8, 129);
            SetPassableIfValid(bldLayer, 38, 8, false);
            frontLayer.Tiles[38, 8] = null;

            SetTileIfValid(backLayer, outdoorsSheet, 39, 8, 351);
            SetTileIfValid(bldLayer, outdoorsSheet, 39, 8, 16);
            SetPassableIfValid(bldLayer, 39, 8, false);
            frontLayer.Tiles[39, 8] = null;

            // 4. BORDE SUR (lx = 24..27): Abierto si side == North, cerrado orgánicamente si side != North
            if (side == AnnexSide.North)
            {
                for (int y = 29; y < height; y++)
                {
                    bldLayer.Tiles[24, y] = null;
                    SetDirtTile(backLayer, outdoorsSheet, 24, y);
                }
                frontLayer.Tiles[24, 28] = null;
                frontLayer.Tiles[24, 29] = null;

                for (int x = 25; x <= 26; x++)
                {
                    for (int y = 27; y < height; y++)
                    {
                        bldLayer.Tiles[x, y] = null;
                        frontLayer.Tiles[x, y] = null;
                        SetDirtTile(backLayer, outdoorsSheet, x, y);
                    }
                }
            }
            else
            {
                for (int x = 24; x <= 27; x++)
                {
                    bool isEven = (x % 2 == 0);

                    SetDirtTile(backLayer, outdoorsSheet, x, 28);
                    bldLayer.Tiles[x, 28] = null;
                    SetTileIfValid(frontLayer, outdoorsSheet, x, 28, isEven ? 413 : 414);
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, x, 28, isEven ? 18 : 19);

                    SetTileIfValid(backLayer, outdoorsSheet, x, 29, 326);
                    SetTileIfValid(bldLayer, outdoorsSheet, x, 29, 326);
                    SetPassableIfValid(bldLayer, x, 29, false);
                    SetTileIfValid(frontLayer, outdoorsSheet, x, 29, 326);
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, x, 29, isEven ? 43 : 44);

                    SetTileIfValid(backLayer, outdoorsSheet, x, 30, 351);
                    SetTileIfValid(bldLayer, outdoorsSheet, x, 30, 355);
                    SetPassableIfValid(bldLayer, x, 30, false);
                    frontLayer.Tiles[x, 30] = null;
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, x, 30, isEven ? 68 : 69);

                    SetTileIfValid(backLayer, outdoorsSheet, x, 31, 355);
                    SetPassableIfValid(backLayer, x, 31, false);
                    SetTileIfValid(bldLayer, outdoorsSheet, x, 31, 380);
                    SetPassableIfValid(bldLayer, x, 31, false);
                    frontLayer.Tiles[x, 31] = null;
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, x, 31, isEven ? 93 : 94);
                }
            }
        }

        /// <summary>
        /// Compone el minimapa de la granja Fluvial (V4 aprobado) eliminando puentes muertos y habilitando únicamente
        /// la entrada/salida correspondiente al lado activo (Sur, Oeste o Norte).
        /// </summary>
        private static void ApplyRiverlandFarmEnclosure(
            Map source,
            Map target,
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer alwaysFrontLayer,
            TileSheet outdoorsSheet,
            AnnexSide side,
            int srcY,
            int width,
            int height)
        {
            TileSheet? v16Sheet = target.GetTileSheet("v16_Outdoors2") ?? outdoorsSheet;

            static void SetWaterTile(Layer back, TileSheet sheet, int x, int y, int tileIndex)
            {
                if (x >= 0 && x < back.LayerWidth && y >= 0 && y < back.LayerHeight)
                {
                    var wt = new StaticTile(back, sheet, BlendMode.Alpha, tileIndex);
                    wt.Properties["Water"] = "T";
                    back.Tiles[x, y] = wt;
                }
            }

            // 1. Eliminar el puente horizontal muerto del Este (x = 35..38, y = 15..17) y cerrar los acantilados
            SetWaterTile(backLayer, outdoorsSheet, 35, 16, 1246);
            SetTileIfValid(bldLayer, outdoorsSheet, 35, 16, 237);
            SetPassableIfValid(bldLayer, 35, 16, false);
            for (int x = 36; x <= 37; x++)
            {
                for (int y = 15; y <= 17; y++)
                {
                    SetWaterTile(backLayer, outdoorsSheet, x, y, 1246);
                    bldLayer.Tiles[x, y] = null;
                }
            }
            SetWaterTile(backLayer, outdoorsSheet, 38, 16, 1246);
            SetTileIfValid(bldLayer, outdoorsSheet, 38, 16, 235);
            SetPassableIfValid(bldLayer, 38, 16, false);

            // 2. Eliminar el puente de tablón inferior del Oeste (x = 0..2, y = 17..19) y cerrar la orilla en x = 2
            SetWaterTile(backLayer, outdoorsSheet, 2, 18, 1246);
            SetTileIfValid(bldLayer, outdoorsSheet, 2, 18, 235);
            SetPassableIfValid(bldLayer, 2, 18, false);
            for (int x = 0; x <= 1; x++)
            {
                for (int y = 17; y <= 19; y++)
                {
                    SetWaterTile(backLayer, outdoorsSheet, x, y, 1246);
                    bldLayer.Tiles[x, y] = null;
                }
            }

            // 3. Limpiar el puente norte original en lx = 0..5, ly = 0..1
            for (int x = 0; x <= 5; x++)
            {
                for (int y = 0; y <= 1; y++)
                {
                    SetWaterTile(backLayer, outdoorsSheet, x, y, 1246);
                    bldLayer.Tiles[x, y] = null;
                }
            }

            // 4. Gestión de la península noroeste (x = 0..3, y = 1..13) según si la entrada Oeste está activa
            if (side != AnnexSide.East)
            {
                // Redondear la isla noroeste copiando sx = 16..19 en lx = 0..3 para ly = 1..13 y sellar su borde izquierdo
                string[] layerNames = { "Back", "Buildings", "Front", "AlwaysFront" };
                for (int lx = 0; lx <= 3; lx++)
                {
                    int sx = 16 + lx;
                    for (int ly = 1; ly <= 13; ly++)
                    {
                        int sy = srcY + ly;
                        foreach (string lname in layerNames)
                        {
                            Layer? srcLayer = source.GetLayer(lname);
                            Layer? dstLayer = target.GetLayer(lname);
                            if (srcLayer == null || dstLayer == null)
                                continue;

                            if (sx >= 0 && sx < srcLayer.LayerWidth && sy >= 0 && sy < srcLayer.LayerHeight)
                            {
                                Tile? srcTile = srcLayer.Tiles[sx, sy];
                                if (srcTile != null)
                                {
                                    Tile cloned = CloneTile(srcTile, dstLayer, target);
                                    StripVanillaWarpTileProperties(cloned);
                                    dstLayer.Tiles[lx, ly] = cloned;
                                }
                                else
                                {
                                    dstLayer.Tiles[lx, ly] = null;
                                }
                            }
                        }
                    }
                }

                SetWaterTile(backLayer, v16Sheet, 0, 7, 866);
                SetTileIfValid(bldLayer, v16Sheet, 0, 7, 978);
                SetPassableIfValid(bldLayer, 0, 7, false);

                SetWaterTile(backLayer, v16Sheet, 0, 8, 882);
                SetTileIfValid(bldLayer, outdoorsSheet, 0, 8, 235);
                SetPassableIfValid(bldLayer, 0, 8, false);

                SetWaterTile(backLayer, v16Sheet, 0, 9, 882);
                SetTileIfValid(bldLayer, outdoorsSheet, 0, 9, 235);
                SetPassableIfValid(bldLayer, 0, 9, false);
            }
            else
            {
                // side == AnnexSide.East (entrada Oeste activa en (1,8)): conservar la península oeste y cerrar su orilla norte en y = 2
                for (int x = 0; x <= 4; x++)
                {
                    SetWaterTile(backLayer, outdoorsSheet, x, 2, 1246);
                    SetTileIfValid(bldLayer, v16Sheet, x, 2, 1025);
                    SetPassableIfValid(bldLayer, x, 2, false);
                }
            }

            // 5. Si side == AnnexSide.South (entrada Norte activa en (4,1)): tender el puente norte de 2 casillas en x = 4..5, y = 0..2
            if (side == AnnexSide.South)
            {
                for (int y = 0; y <= 2; y++)
                {
                    SetWaterTile(backLayer, outdoorsSheet, 4, y, 1274);
                    SetWaterTile(backLayer, outdoorsSheet, 5, y, 1274);
                    backLayer.Tiles[4, y]?.Properties.Remove("Passable");
                    backLayer.Tiles[5, y]?.Properties.Remove("Passable");
                }

                SetTileIfValid(bldLayer, outdoorsSheet, 4, 0, 910);
                SetPassableIfValid(bldLayer, 4, 0, true);
                SetTileIfValid(bldLayer, outdoorsSheet, 5, 0, 885);
                SetPassableIfValid(bldLayer, 5, 0, true);

                SetTileIfValid(bldLayer, outdoorsSheet, 4, 1, 859);
                SetPassableIfValid(bldLayer, 4, 1, true);
                SetTileIfValid(bldLayer, outdoorsSheet, 5, 1, 860);
                SetPassableIfValid(bldLayer, 5, 1, true);

                SetTileIfValid(bldLayer, outdoorsSheet, 4, 2, 884);
                SetPassableIfValid(bldLayer, 4, 2, true);
                SetTileIfValid(bldLayer, outdoorsSheet, 5, 2, 884);
                SetPassableIfValid(bldLayer, 5, 2, true);
            }

            // 6. Gestión del puente Sur (x = 25..26, y = 27..30)
            if (side == AnnexSide.North)
            {
                // Asegurar que las plataformas del puente sur (y = 27..30) sean explícitamente transitables
                for (int x = 25; x <= 26; x++)
                {
                    for (int y = 27; y <= 30; y++)
                    {
                        backLayer.Tiles[x, y]?.Properties.Remove("Passable");
                        SetPassableIfValid(bldLayer, x, y, true);
                    }
                    backLayer.Tiles[x, 31]?.Properties.Remove("Passable");
                    bldLayer.Tiles[x, 31] = null;
                }
            }
            else
            {
                // Cerrar el puente sur y sellar las orillas superior (y = 27) e inferior (y = 30)
                SetWaterTile(backLayer, outdoorsSheet, 25, 27, 1244);
                SetTileIfValid(bldLayer, outdoorsSheet, 25, 27, 184);
                SetPassableIfValid(bldLayer, 25, 27, false);

                SetWaterTile(backLayer, outdoorsSheet, 26, 27, 1244);
                SetTileIfValid(bldLayer, outdoorsSheet, 26, 27, 211);
                SetPassableIfValid(bldLayer, 26, 27, false);

                SetWaterTile(backLayer, outdoorsSheet, 25, 28, 1269);
                bldLayer.Tiles[25, 28] = null;
                SetWaterTile(backLayer, outdoorsSheet, 26, 28, 1269);
                bldLayer.Tiles[26, 28] = null;
                bldLayer.Tiles[24, 28] = null;
                bldLayer.Tiles[27, 28] = null;

                for (int x = 24; x <= 27; x++)
                {
                    SetWaterTile(backLayer, outdoorsSheet, x, 29, 1246);
                    bldLayer.Tiles[x, 29] = null;
                }

                for (int x = 25; x <= 26; x++)
                {
                    SetWaterTile(backLayer, outdoorsSheet, x, 30, 1274);
                    SetTileIfValid(bldLayer, v16Sheet, x, 30, 1025);
                    SetPassableIfValid(bldLayer, x, 30, false);
                }
            }
        }

        private static void SanitizeAnnexMultiTileObjects(
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer alwaysFrontLayer,
            Layer? alwaysFront2Layer,
            TileSheet outdoorsSheet,
            int width,
            int height)
        {
            // 1. Fila y = 0: limpiar troncos decapitados cuya copa caía en y = -1
            for (int x = 0; x < width; x++)
            {
                int? bldIdx = bldLayer.Tiles[x, 0]?.TileIndex;
                if (bldIdx.HasValue && (VerticalMultiTileClassifier.IsTreeTrunkBase(bldIdx.Value) || VerticalMultiTileClassifier.IsBushTile(bldIdx.Value)))
                {
                    bldLayer.Tiles[x, 0] = null;
                }

                // Copas huérfanas en Front y=0 cuyos troncos estaban en y=1 cortados
                int? frontIdx = frontLayer.Tiles[x, 0]?.TileIndex;
                if (frontIdx.HasValue && bldLayer.Tiles[x, 1] == null && VerticalMultiTileClassifier.IsBushTile(frontIdx.Value))
                {
                    frontLayer.Tiles[x, 0] = null;
                }
            }

            // 2. Fila y = height - 1: limpiar copas huérfanas
            for (int x = 0; x < width; x++)
            {
                int? frontIdx = frontLayer.Tiles[x, height - 1]?.TileIndex;
                if (frontIdx.HasValue && VerticalMultiTileClassifier.IsBushTile(frontIdx.Value))
                {
                    frontLayer.Tiles[x, height - 1] = null;
                }
            }
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
                    int o1 = ex;
                    int o2 = (ex < width - 1) ? ex + 1 : ex - 1;
                    openingBorderTiles.Add((o1, height - 1));
                    openingBorderTiles.Add((o2, height - 1));
                    for (int y = Math.Max(1, height - 6); y < height; y++)
                    {
                        corridorTiles.Add((o1, y));
                        corridorTiles.Add((o2, y));
                    }
                    break;

                case AnnexSide.South:
                    // Entrada por el borde Norte del anexo (y = 0)
                    int so1 = ex;
                    int so2 = (ex < width - 1) ? ex + 1 : ex - 1;
                    openingBorderTiles.Add((so1, 0));
                    openingBorderTiles.Add((so2, 0));
                    for (int y = 0; y <= Math.Min(height - 2, 5); y++)
                    {
                        corridorTiles.Add((so1, y));
                        corridorTiles.Add((so2, y));
                    }
                    break;

                case AnnexSide.East:
                    // Entrada por el borde Oeste del anexo (x = 0)
                    int eo1 = ey;
                    int eo2 = (ey < height - 1) ? ey + 1 : ey - 1;
                    openingBorderTiles.Add((0, eo1));
                    openingBorderTiles.Add((0, eo2));
                    for (int x = 0; x <= Math.Min(width - 2, 5); x++)
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
