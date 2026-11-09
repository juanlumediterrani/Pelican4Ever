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

            // Fusionar AlwaysFront2 en AlwaysFront tras el recorte inicial para no truncar copas multi-capa (ej. cerezo SO)
            if (alwaysFront2Layer != null)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        Tile? af2Tile = alwaysFront2Layer.Tiles[x, y];
                        if (af2Tile != null && af2Tile.TileIndex >= 0)
                        {
                            alwaysFrontLayer.Tiles[x, y] = CloneTile(af2Tile, alwaysFrontLayer, target);
                            alwaysFront2Layer.Tiles[x, y] = null;
                        }
                    }
                }
            }

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
                || template.Id.Equals("Fishing", StringComparison.OrdinalIgnoreCase)
                || template.Id.Equals("Foraging", StringComparison.OrdinalIgnoreCase)
                || template.Id.Equals("Mining", StringComparison.OrdinalIgnoreCase)
                || template.Id.Equals("Combat", StringComparison.OrdinalIgnoreCase)
                || template.Id.Equals("FourCorners", StringComparison.OrdinalIgnoreCase)
                || template.Id.Equals("Ranching", StringComparison.OrdinalIgnoreCase)
                || template.Id.Equals("Beach", StringComparison.OrdinalIgnoreCase);

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
                else if (template.Id.Equals("Foraging", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyForagingFarmEnclosure(source, target, backLayer, bldLayer, frontLayer, alwaysFrontLayer, outdoorsSheet, side, width, height);
                }
                else if (template.Id.Equals("Mining", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyMiningFarmEnclosure(source, target, backLayer, bldLayer, frontLayer, alwaysFrontLayer, outdoorsSheet, side, width, height);
                }
                else if (template.Id.Equals("Combat", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyCombatFarmEnclosure(source, target, backLayer, bldLayer, frontLayer, alwaysFrontLayer, outdoorsSheet, side, width, height);
                }
                else if (template.Id.Equals("FourCorners", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyFourCornersFarmEnclosure(source, target, backLayer, bldLayer, frontLayer, alwaysFrontLayer, outdoorsSheet, side, width, height);
                }
                else if (template.Id.Equals("Ranching", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyRanchingFarmEnclosure(source, target, backLayer, bldLayer, frontLayer, alwaysFrontLayer, outdoorsSheet, side, width, height);
                }
                else if (template.Id.Equals("Beach", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyBeachFarmEnclosure(source, target, backLayer, bldLayer, frontLayer, alwaysFrontLayer, outdoorsSheet, side, width, height);
                }
                else
                {
                    FlankOpeningWithFencePosts(side, openingBorderTiles, bldLayer, frontLayer, outdoorsSheet, width, height);
                }

                SanitizeAnnexMultiTileObjects(backLayer, bldLayer, frontLayer, alwaysFrontLayer, alwaysFront2Layer, outdoorsSheet, width, height);
                SanitizeAnnexSpawnsAndPaths(target, backLayer, bldLayer, frontLayer, alwaysFrontLayer, template, side, openingBorderTiles, width, height);
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

        /// <summary>
        /// Compone la miniatura canónica 40×32 de Beach / Island Farm (V10 aprobado) centrada en (29, 0),
        /// capturando el desfiladero norte de montaña con camino festoneado (lx=11..12, ly=0..3),
        /// la ensenada oceánica turquesa al este (lx=38..39, ly=0..8) y rompiente de espuma (ly=9),
        /// el seto forestal este y sur sobre arena y pasto isleño, y el vallado rústico oeste con dunas diagonales preservadas.
        /// </summary>
        private static void ApplyBeachFarmEnclosure(
            Map source,
            Map target,
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer alwaysFrontLayer,
            TileSheet outdoorsSheet,
            AnnexSide side,
            int width,
            int height)
        {
            const int cx = 29;
            const int cy = 0;

            // 1. Mapeo base 40×32 en cx=29, cy=0 (captura la cueva de la granja, ensenada oceánica, dunas y riscos norte):
            for (int lx = 0; lx < width; lx++)
            {
                int sx = cx + lx;
                for (int ly = 0; ly < height; ly++)
                {
                    int sy = cy + ly;
                    CopySourceCellToTarget(source, target, lx, ly, sx, sy);
                }
            }

            TileSheet islandSheet = target.GetTileSheet("untitled tile sheet2") ?? outdoorsSheet;

            // -------------------------------------------------------------
            // Saneado de Dunas Este: Retirar blob marrón huérfano en (lx=35..37, ly=16..18)
            // -------------------------------------------------------------
            for (int lx = 35; lx <= 37; lx++)
            {
                for (int ly = 16; ly <= 18; ly++)
                {
                    SetTileIfValid(backLayer, islandSheet, lx, ly, 674);
                }
            }

            // -------------------------------------------------------------
            // Paso Norte de Montaña (lx=11..12, ly=0..3): Camino de tierra con festón
            // -------------------------------------------------------------
            if (side == AnnexSide.South) // Entrada activa Norte (el jugador camina hacia el Sur)
            {
                for (int ly = 0; ly <= 3; ly++)
                {
                    SetTileIfValid(backLayer, outdoorsSheet, 11, ly, 225);
                    backLayer.Tiles[11, ly].Properties["Type"] = "Dirt";
                    SetTileIfValid(backLayer, outdoorsSheet, 12, ly, 228);
                    backLayer.Tiles[12, ly].Properties["Type"] = "Dirt";

                    bldLayer.Tiles[11, ly] = null;
                    bldLayer.Tiles[12, ly] = null;
                    frontLayer.Tiles[11, ly] = null;
                    frontLayer.Tiles[12, ly] = null;
                    alwaysFrontLayer.Tiles[11, ly] = null;
                    alwaysFrontLayer.Tiles[12, ly] = null;
                    backLayer.Tiles[11, ly]?.Properties.Remove("Passable");
                    backLayer.Tiles[12, ly]?.Properties.Remove("Passable");
                }
            }
            else // Borde Norte cerrado: portón de empalizada rústica entre los riscos
            {
                SetTileIfValid(frontLayer, outdoorsSheet, 11, 0, 358);
                SetTileIfValid(bldLayer, outdoorsSheet, 11, 1, 383);
                SetPassableIfValid(bldLayer, 11, 1, false);

                SetTileIfValid(frontLayer, outdoorsSheet, 12, 0, 360);
                SetTileIfValid(bldLayer, outdoorsSheet, 12, 1, 385);
                SetPassableIfValid(bldLayer, 12, 1, false);

                SetTileIfValid(bldLayer, outdoorsSheet, 11, 0, 16);
                SetPassableIfValid(bldLayer, 11, 0, false);
                SetTileIfValid(bldLayer, outdoorsSheet, 12, 0, 16);
                SetPassableIfValid(bldLayer, 12, 0, false);
            }

            // Colisión sólida a lo largo de toda la fila 0 fuera de la apertura activa:
            for (int lx = 0; lx < width; lx++)
            {
                if (side == AnnexSide.South && (lx == 11 || lx == 12))
                    continue;

                if (bldLayer.Tiles[lx, 0] == null)
                    SetTileIfValid(bldLayer, outdoorsSheet, lx, 0, 16);
                SetPassableIfValid(bldLayer, lx, 0, false);
                if (backLayer.Tiles[lx, 0] != null)
                    backLayer.Tiles[lx, 0].Properties["Passable"] = "F";
            }

            // -------------------------------------------------------------
            // Borde Oeste: Vallado rústico, franja herbosa canónica y dunas preservadas
            // -------------------------------------------------------------
            // ly=0..12: Risco de montaña norte (impenetrable)
            for (int ly = 0; ly <= 12; ly++)
            {
                SetPassableIfValid(bldLayer, 0, ly, false);
                if (backLayer.Tiles[0, ly] != null)
                    backLayer.Tiles[0, ly].Properties["Passable"] = "F";
            }

            // Columna 0 con pasto verde isleño bajo postes, columna 1 con festón vertical derecho:
            for (int ly = 13; ly <= 27; ly++)
            {
                SetTileIfValid(backLayer, islandSheet, 0, ly, 608);
                backLayer.Tiles[0, ly].Properties["Type"] = "Grass";
                SetTileIfValid(backLayer, islandSheet, 1, ly, 672);
            }

            for (int ly = 28; ly <= 31; ly++)
            {
                SetTileIfValid(backLayer, islandSheet, 0, ly, 608);
                backLayer.Tiles[0, ly].Properties["Type"] = "Grass";
            }

            // Anclaje superior de valla al pie del risco (ly=12):
            SetTileIfValid(bldLayer, outdoorsSheet, 0, 13, 386);
            SetPassableIfValid(bldLayer, 0, 13, false);
            SetTileIfValid(frontLayer, outdoorsSheet, 0, 12, 361);

            for (int ly = 14; ly <= 18; ly++)
            {
                SetTileIfValid(bldLayer, outdoorsSheet, 0, ly, (ly % 2 == 0) ? 411 : 436);
                SetPassableIfValid(bldLayer, 0, ly, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 0, ly - 1, 361);
            }

            if (side == AnnexSide.East) // Entrada activa Oeste (el jugador camina hacia el Este)
            {
                SetTileIfValid(bldLayer, outdoorsSheet, 0, 19, 436);
                SetPassableIfValid(bldLayer, 0, 19, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 0, 18, 361);

                bldLayer.Tiles[0, 20] = null;
                bldLayer.Tiles[0, 21] = null;
                frontLayer.Tiles[0, 19] = null;
                frontLayer.Tiles[0, 20] = null;
                backLayer.Tiles[0, 20]?.Properties.Remove("Passable");
                backLayer.Tiles[0, 21]?.Properties.Remove("Passable");

                // Despejar colisiones en lx=1..3 at ly=20..21 preservando las texturas y sombras diagonales de duna en Back:
                for (int clx = 1; clx <= 3; clx++)
                {
                    for (int cly = 20; cly <= 21; cly++)
                    {
                        bldLayer.Tiles[clx, cly] = null;
                        frontLayer.Tiles[clx, cly] = null;
                        alwaysFrontLayer.Tiles[clx, cly] = null;
                        backLayer.Tiles[clx, cly]?.Properties.Remove("Passable");
                    }
                }

                SetTileIfValid(bldLayer, outdoorsSheet, 0, 22, 386);
                SetPassableIfValid(bldLayer, 0, 22, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 0, 21, 361);
            }
            else // Borde Oeste cerrado
            {
                for (int ly = 19; ly <= 22; ly++)
                {
                    SetTileIfValid(bldLayer, outdoorsSheet, 0, ly, (ly % 2 == 0) ? 411 : 436);
                    SetPassableIfValid(bldLayer, 0, ly, false);
                    SetTileIfValid(frontLayer, outdoorsSheet, 0, ly - 1, 361);
                }
            }

            for (int ly = 23; ly <= 28; ly++)
            {
                SetTileIfValid(bldLayer, outdoorsSheet, 0, ly, (ly % 2 == 0) ? 411 : 436);
                SetPassableIfValid(bldLayer, 0, ly, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 0, ly - 1, 361);
            }

            // -------------------------------------------------------------
            // Borde Este (Opción A Aprobada): Ensenada marina (ly=0..8), espuma rompiente (ly=9) y bosque continuo (ly=10..31)
            // -------------------------------------------------------------
            // Filas 0..8: Agua marina turquesa abierta
            for (int ly = 0; ly <= 8; ly++)
            {
                bldLayer.Tiles[38, ly] = null;
                bldLayer.Tiles[39, ly] = null;
                frontLayer.Tiles[38, ly] = null;
                frontLayer.Tiles[39, ly] = null;
                alwaysFrontLayer.Tiles[38, ly] = null;
                alwaysFrontLayer.Tiles[39, ly] = null;
            }

            // Fila 9: Espuma rompiente continua alineada con columnas 30..37:
            bldLayer.Tiles[38, 9] = null;
            bldLayer.Tiles[39, 9] = null;
            frontLayer.Tiles[38, 9] = null;
            frontLayer.Tiles[39, 9] = null;
            alwaysFrontLayer.Tiles[38, 9] = null;
            alwaysFrontLayer.Tiles[39, 9] = null;
            SetTileIfValid(backLayer, islandSheet, 38, 9, 268);
            SetTileIfValid(backLayer, islandSheet, 39, 9, 197);

            // Filas 10..31: Franja boscosa continua en tierra firme hasta el límite sur:
            for (int ly = 10; ly < height; ly++)
            {
                SetTileIfValid(bldLayer, outdoorsSheet, 38, ly, (ly % 2 == 0) ? 294 : 319);
                SetPassableIfValid(bldLayer, 38, ly, false);
                frontLayer.Tiles[38, ly] = null;
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 38, ly, (ly % 2 == 0) ? 994 : 1019);

                SetTileIfValid(bldLayer, outdoorsSheet, 39, ly, (ly % 2 == 0) ? 295 : 320);
                SetPassableIfValid(bldLayer, 39, ly, false);
                frontLayer.Tiles[39, ly] = null;
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 39, ly, (ly % 2 == 0) ? 1019 : 1020);
            }

            // -------------------------------------------------------------
            // Borde Sur y Portón de Entrada: Seto boscoso sobre pasto isleño
            // -------------------------------------------------------------
            // Fila 28: Festón arena-a-pasto en columnas lx=0..37:
            for (int lx = 0; lx <= 37; lx++)
            {
                SetTileIfValid(backLayer, islandSheet, lx, 28, (lx % 2 == 0) ? 36 : 37);
            }

            // Filas 29..31: Seto denso de robles sobre pasto isleño en lx=0..37:
            for (int lx = 0; lx <= 37; lx++)
            {
                SetTileIfValid(backLayer, islandSheet, lx, 29, 608);
                backLayer.Tiles[lx, 29].Properties["Type"] = "Grass";
                SetTileIfValid(backLayer, islandSheet, lx, 30, 608);
                backLayer.Tiles[lx, 30].Properties["Type"] = "Grass";
                SetTileIfValid(backLayer, islandSheet, lx, 31, 608);
                backLayer.Tiles[lx, 31].Properties["Type"] = "Grass";

                SetTileIfValid(bldLayer, outdoorsSheet, lx, 29, (lx % 2 == 0) ? 294 : 295);
                SetPassableIfValid(bldLayer, lx, 29, false);
                SetTileIfValid(bldLayer, outdoorsSheet, lx, 30, (lx % 2 == 0) ? 319 : 320);
                SetPassableIfValid(bldLayer, lx, 30, false);
                SetTileIfValid(bldLayer, outdoorsSheet, lx, 31, (lx % 2 == 0) ? 344 : 345);
                SetPassableIfValid(bldLayer, lx, 31, false);

                frontLayer.Tiles[lx, 29] = null;
                frontLayer.Tiles[lx, 30] = null;
                frontLayer.Tiles[lx, 31] = null;

                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 29, (lx % 2 == 0) ? 994 : 1019);
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 30, (lx % 2 == 0) ? 1019 : 1020);
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 31, (lx % 2 == 0) ? 1044 : 1045);
            }

            // Entrada Sur en lx=11..12 (alineada con el paso norte y la granja):
            if (side == AnnexSide.North) // Entrada activa Sur (el jugador camina hacia el Norte)
            {
                for (int lx = 11; lx <= 12; lx++)
                {
                    for (int ly = 28; ly <= 31; ly++)
                    {
                        bldLayer.Tiles[lx, ly] = null;
                        frontLayer.Tiles[lx, ly] = null;
                        alwaysFrontLayer.Tiles[lx, ly] = null;
                        SetTileIfValid(backLayer, islandSheet, lx, ly, 674);
                        backLayer.Tiles[lx, ly]?.Properties.Remove("Passable");
                    }
                }

                SetTileIfValid(backLayer, islandSheet, 10, 28, 130);
                SetTileIfValid(backLayer, islandSheet, 13, 28, 129);

                for (int ly = 29; ly <= 31; ly++)
                {
                    SetTileIfValid(backLayer, islandSheet, 10, ly, 672);
                    SetTileIfValid(backLayer, islandSheet, 13, ly, 675);

                    if (ly > 29)
                    {
                        bldLayer.Tiles[10, ly] = null;
                        bldLayer.Tiles[13, ly] = null;
                        alwaysFrontLayer.Tiles[10, ly] = null;
                        alwaysFrontLayer.Tiles[13, ly] = null;
                    }
                }

                SetTileIfValid(bldLayer, outdoorsSheet, 10, 29, 386);
                SetPassableIfValid(bldLayer, 10, 29, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 10, 28, 361);
                alwaysFrontLayer.Tiles[10, 29] = null;

                SetTileIfValid(bldLayer, outdoorsSheet, 13, 29, 386);
                SetPassableIfValid(bldLayer, 13, 29, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 13, 28, 361);
                alwaysFrontLayer.Tiles[13, 29] = null;
            }
        }

        /// <summary>
        /// Compone la miniatura canónica 40×32 de Ranching / Meadowlands Farm (V4 aprobado) centrada en (40, 0),
        /// capturando el paso canónico a Backwoods (lx=23..24, ly=0), la icónica doble cascada y desfiladero (lx=4..9, ly=0..13),
        /// la cancela oeste de pastizales (ly=20..21), el borde este boscoso continuo (lx=38..39) y el seto sur de robles (ly=29..31).
        /// </summary>
        private static void ApplyRanchingFarmEnclosure(
            Map source,
            Map target,
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer alwaysFrontLayer,
            TileSheet outdoorsSheet,
            AnnexSide side,
            int width,
            int height)
        {
            const int cx = 40;
            const int cy = 0;

            // 1. Mapeo base 40x32 en cx=40, cy=0 (captura Backwoods pass lx=23..24, doble cascada lx=4..9, pastos Meadowlands):
            for (int lx = 0; lx < width; lx++)
            {
                int sx = cx + lx;
                for (int ly = 0; ly < height; ly++)
                {
                    int sy = cy + ly;
                    CopySourceCellToTarget(source, target, lx, ly, sx, sy);
                }
            }

            // 2. BORDE Y ENTRADA NORTE (lx=23..24, ly=0..1):
            // En Farm_Ranching.xnb, el paso canónico a Backwoods está en sx=63..64 (local lx=23..24),
            // flanqueado naturalmente por riscos de montaña a la izquierda (lx=22) y derecha (lx=25).
            if (side == AnnexSide.South) // Entrar desde el Norte hacia el Sur
            {
                // Paso canónico abierto:
                for (int lx = 23; lx <= 24; lx++)
                {
                    bldLayer.Tiles[lx, 0] = null;
                    bldLayer.Tiles[lx, 1] = null;
                    frontLayer.Tiles[lx, 0] = null;
                    alwaysFrontLayer.Tiles[lx, 0] = null;
                    if (backLayer.Tiles[lx, 0] != null)
                    {
                        backLayer.Tiles[lx, 0].Properties.Remove("Passable");
                    }
                }
            }
            else // Borde Norte cerrado: portón rústico entre los dos riscos
            {
                // Postes en ly=1 con remates en ly=0:
                SetTileIfValid(frontLayer, outdoorsSheet, 23, 0, 358);
                SetTileIfValid(bldLayer, outdoorsSheet, 23, 1, 383);
                SetPassableIfValid(bldLayer, 23, 1, false);

                SetTileIfValid(frontLayer, outdoorsSheet, 24, 0, 360);
                SetTileIfValid(bldLayer, outdoorsSheet, 24, 1, 385);
                SetPassableIfValid(bldLayer, 24, 1, false);

                // Barrera sólida en fila 0:
                SetTileIfValid(bldLayer, outdoorsSheet, 23, 0, 16);
                SetPassableIfValid(bldLayer, 23, 0, false);
                SetTileIfValid(bldLayer, outdoorsSheet, 24, 0, 16);
                SetPassableIfValid(bldLayer, 24, 0, false);
            }

            // Garantizar colisión sólida en fila 0 fuera de la apertura activa:
            for (int lx = 0; lx < width; lx++)
            {
                if (side == AnnexSide.South && (lx == 23 || lx == 24))
                    continue;

                if (bldLayer.Tiles[lx, 0] == null)
                {
                    SetTileIfValid(bldLayer, outdoorsSheet, lx, 0, 16);
                }
                SetPassableIfValid(bldLayer, lx, 0, false);
                SetPassableIfValid(backLayer, lx, 0, false);
            }

            // 3. BORDE Y ENTRADA OESTE (lx=0..1):
            // ly=0..13: Risco de montaña (100% auténtico vanilla).
            // ly=14..17: Desembocadura del río (agua). Garantizar bloqueo perimetral:
            for (int ly = 14; ly <= 17; ly++)
            {
                SetPassableIfValid(bldLayer, 0, ly, false);
                SetPassableIfValid(backLayer, 0, ly, false);
            }

            // ly=18..27: Valla vertical de postes y rieles a lo largo del pastizal:
            // Poste terminal anclado a la orilla en (0, 18):
            SetTileIfValid(bldLayer, outdoorsSheet, 0, 18, 386);
            SetPassableIfValid(bldLayer, 0, 18, false);
            SetTileIfValid(frontLayer, outdoorsSheet, 0, 17, 361);

            // Poste superior del portón en (0, 19):
            SetTileIfValid(bldLayer, outdoorsSheet, 0, 19, 436);
            SetPassableIfValid(bldLayer, 0, 19, false);
            SetTileIfValid(frontLayer, outdoorsSheet, 0, 18, 361);

            if (side == AnnexSide.East) // Entrar desde el Oeste hacia el Este
            {
                // Pad de tierra canónico 3x2 en lx=0..2, ly=20..21:
                for (int lx = 0; lx <= 2; lx++)
                {
                    for (int ly = 20; ly <= 21; ly++)
                    {
                        bldLayer.Tiles[lx, ly] = null;
                        frontLayer.Tiles[lx, ly] = null;
                        alwaysFrontLayer.Tiles[lx, ly] = null;
                        var dirtTile = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, 587);
                        dirtTile.Properties["Type"] = "Dirt";
                        backLayer.Tiles[lx, ly] = dirtTile;
                    }
                }

                // Ribetes de transición:
                SetTileIfValid(backLayer, outdoorsSheet, 1, 19, 201);
                SetTileIfValid(backLayer, outdoorsSheet, 2, 19, 203);
                SetTileIfValid(backLayer, outdoorsSheet, 3, 20, 228);
                SetTileIfValid(backLayer, outdoorsSheet, 3, 21, 228);
                SetTileIfValid(backLayer, outdoorsSheet, 1, 22, 251);
                SetTileIfValid(backLayer, outdoorsSheet, 2, 22, 253);

                // Poste inferior del portón en (0, 22):
                SetTileIfValid(bldLayer, outdoorsSheet, 0, 22, 383);
                SetPassableIfValid(bldLayer, 0, 22, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 0, 21, 358);
                SetGrassTile(backLayer, outdoorsSheet, 0, 22, 175);
                SetPassableIfValid(backLayer, 0, 22, false);
            }
            else // Portón cerrado
            {
                for (int ly = 20; ly <= 21; ly++)
                {
                    SetTileIfValid(bldLayer, outdoorsSheet, 0, ly, 386);
                    SetPassableIfValid(bldLayer, 0, ly, false);
                    SetTileIfValid(frontLayer, outdoorsSheet, 0, ly - 1, 361);
                    frontLayer.Tiles[0, ly] = null;
                    SetGrassTile(backLayer, outdoorsSheet, 0, ly, 175);
                    SetPassableIfValid(backLayer, 0, ly, false);
                }
                SetTileIfValid(bldLayer, outdoorsSheet, 0, 22, 386);
                SetPassableIfValid(bldLayer, 0, 22, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 0, 21, 361);
                SetGrassTile(backLayer, outdoorsSheet, 0, 22, 175);
                SetPassableIfValid(backLayer, 0, 22, false);
            }

            // Continuación de valla vertical hasta fila 27:
            for (int ly = 23; ly <= 26; ly++)
            {
                SetTileIfValid(bldLayer, outdoorsSheet, 0, ly, 386);
                SetPassableIfValid(bldLayer, 0, ly, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 0, ly - 1, 361);
                frontLayer.Tiles[0, ly] = null;
                SetGrassTile(backLayer, outdoorsSheet, 0, ly, 175);
                SetPassableIfValid(backLayer, 0, ly, false);
            }

            // Poste terminal anclado al bosque sur en (0, 27):
            SetTileIfValid(bldLayer, outdoorsSheet, 0, 27, 436);
            SetPassableIfValid(bldLayer, 0, 27, false);
            SetTileIfValid(frontLayer, outdoorsSheet, 0, 26, 361);
            SetGrassTile(backLayer, outdoorsSheet, 0, 27, 175);
            SetPassableIfValid(backLayer, 0, 27, false);

            // 4. BORDE ESTE (lx=37..39):
            // ly=0..11: Risco de montaña natural (100% auténtico vanilla).
            // ly=12..31: Límite boscoso denso de robles de 2 casillas extendiéndose desde el pie del risco hasta el bosque sur:
            for (int ly = 12; ly < height; ly++)
            {
                // Buffer de vereda de hierba en lx=37 si hay tierra de cultivo:
                int? bkIdx = backLayer.Tiles[37, ly]?.TileIndex;
                if (bkIdx is 176 or 178 or 179 or 180 or 201 or 226 or 488 or 513 or 560 or 614)
                {
                    SetTileIfValid(backLayer, outdoorsSheet, 36, ly, 228);
                    SetGrassTile(backLayer, outdoorsSheet, 37, ly, 175);
                }

                // Columna 38: Línea interior del bosque
                SetGrassTile(backLayer, outdoorsSheet, 38, ly, (ly % 2 == 0) ? 175 : 150);
                SetTileIfValid(bldLayer, outdoorsSheet, 38, ly, (ly % 2 == 0) ? 294 : 319);
                SetPassableIfValid(bldLayer, 38, ly, false);
                frontLayer.Tiles[38, ly] = null;
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 38, ly, (ly % 2 == 0) ? 994 : 1019);

                // Columna 39: Línea exterior del bosque
                SetGrassTile(backLayer, outdoorsSheet, 39, ly, 150);
                SetTileIfValid(bldLayer, outdoorsSheet, 39, ly, (ly % 2 == 0) ? 295 : 320);
                SetPassableIfValid(bldLayer, 39, ly, false);
                frontLayer.Tiles[39, ly] = null;
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 39, ly, (ly % 2 == 0) ? 1019 : 1020);
            }

            // 5. BORDE Y ENTRADA SUR (ly=28..31):
            // Fila 28: Vereda de hierba natural.
            // Filas 29..31: Seto denso de robles canónico de 3 filas en lx=0..37:
            for (int lx = 0; lx < 38; lx++)
            {
                SetGrassTile(backLayer, outdoorsSheet, lx, 29, 175);
                SetGrassTile(backLayer, outdoorsSheet, lx, 30, 175);
                SetGrassTile(backLayer, outdoorsSheet, lx, 31, 175);

                SetTileIfValid(bldLayer, outdoorsSheet, lx, 29, (lx % 2 == 0) ? 294 : 295);
                SetPassableIfValid(bldLayer, lx, 29, false);
                SetTileIfValid(bldLayer, outdoorsSheet, lx, 30, (lx % 2 == 0) ? 319 : 320);
                SetPassableIfValid(bldLayer, lx, 30, false);
                SetTileIfValid(bldLayer, outdoorsSheet, lx, 31, (lx % 2 == 0) ? 344 : 345);
                SetPassableIfValid(bldLayer, lx, 31, false);

                frontLayer.Tiles[lx, 29] = null;
                frontLayer.Tiles[lx, 30] = null;
                frontLayer.Tiles[lx, 31] = null;

                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 29, (lx % 2 == 0) ? 994 : 1019);
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 30, (lx % 2 == 0) ? 1019 : 1020);
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 31, (lx % 2 == 0) ? 1044 : 1045);
            }

            // Mecánica de entrada Sur en lx=23..24 (alineada con el paso a Backwoods):
            if (side == AnnexSide.North) // Entrar desde el Sur hacia el Norte
            {
                // Abrir camino de tierra a través del bosque (filas 28..31):
                for (int lx = 23; lx <= 24; lx++)
                {
                    for (int ly = 28; ly < height; ly++)
                    {
                        bldLayer.Tiles[lx, ly] = null;
                        frontLayer.Tiles[lx, ly] = null;
                        alwaysFrontLayer.Tiles[lx, ly] = null;
                        var dirtTile = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, 587);
                        dirtTile.Properties["Type"] = "Dirt";
                        backLayer.Tiles[lx, ly] = dirtTile;
                    }
                }

                // Transición al prado en fila 27:
                SetTileIfValid(backLayer, outdoorsSheet, 23, 27, 201);
                SetTileIfValid(backLayer, outdoorsSheet, 24, 27, 201);
                SetTileIfValid(backLayer, outdoorsSheet, 22, 27, 200);
                SetTileIfValid(backLayer, outdoorsSheet, 25, 27, 203);

                // Ribetes verticales de tierra a los lados del camino:
                for (int ly = 28; ly < height; ly++)
                {
                    SetTileIfValid(backLayer, outdoorsSheet, 22, ly, 225);
                    SetTileIfValid(backLayer, outdoorsSheet, 25, ly, 228);
                }

                // Postes flanqueando la entrada:
                SetTileIfValid(bldLayer, outdoorsSheet, 22, 29, 385);
                SetPassableIfValid(bldLayer, 22, 29, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 22, 28, 360);

                SetTileIfValid(bldLayer, outdoorsSheet, 25, 29, 383);
                SetPassableIfValid(bldLayer, 25, 29, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 25, 28, 358);
            }
        }

        /// <summary>
        /// Compone la miniatura canónica 40×32 de Four Corners Farm (V5 aprobado) centrada en la encrucijada circular en (20, 24),
        /// con los cuatro cuadrantes perfectamente transitables, el corral superior con cancela y valla de postes y maderos,
        /// la cancela oeste simétrica, el seto sur de 3 filas con autotiling canónico (251) y el pasto auténtico del cuadrante SE.
        /// </summary>
        private static void ApplyFourCornersFarmEnclosure(
            Map source,
            Map target,
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer alwaysFrontLayer,
            TileSheet outdoorsSheet,
            AnnexSide side,
            int width,
            int height)
        {
            const int cx = 20;
            const int cy = 24;

            // 1. Mapeo base 40x32 centrado en la encrucijada icónica en cx=20, cy=24:
            for (int lx = 0; lx < width; lx++)
            {
                int sx = cx + lx;
                for (int ly = 0; ly < height; ly++)
                {
                    int sy = cy + ly;
                    CopySourceCellToTarget(source, target, lx, ly, sx, sy);
                }
            }

            // 1b. Saneado de tiles de hierba superpuestos en Buildings (Buildings: 175):
            for (int lx = 0; lx < width; lx++)
            {
                for (int ly = 0; ly < height; ly++)
                {
                    if (bldLayer.Tiles[lx, ly]?.TileIndex == 175)
                    {
                        int? bkIdx = backLayer.Tiles[lx, ly]?.TileIndex;
                        if (bkIdx is 562 or 587 or 589)
                        {
                            SetGrassTile(backLayer, outdoorsSheet, lx, ly, TerrainTileClassifier.SunnyGrassTileIndex);
                        }
                        bldLayer.Tiles[lx, ly] = null;
                    }
                }
            }

            // 1c. Limpiar agujero en terraza NO (7, 14) y completar copa de roble en (2, 6):
            SetGrassTile(backLayer, outdoorsSheet, 7, 14, TerrainTileClassifier.SunnyGrassTileIndex);
            bldLayer.Tiles[7, 14] = null;
            frontLayer.Tiles[7, 14] = null;
            alwaysFrontLayer.Tiles[7, 14] = null;

            SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 2, 6, 12);
            frontLayer.Tiles[2, 6] = null;

            // 2. BORDE Y ENTRADA NORTE:
            // Paddock fence NO (lx=0..15):
            for (int lx = 0; lx < 16; lx++)
            {
                SetGrassTile(backLayer, outdoorsSheet, lx, 0, (lx % 2 == 0) ? TerrainTileClassifier.SunnyGrassTileIndex : TerrainTileClassifier.SunnyGrassSoftTileIndex);
                SetGrassTile(backLayer, outdoorsSheet, lx, 1, (lx % 2 == 0) ? TerrainTileClassifier.SunnyGrassTileIndex : TerrainTileClassifier.SunnyGrassSoftTileIndex);
                SetTileIfValid(frontLayer, outdoorsSheet, lx, 0, 359);
                SetTileIfValid(bldLayer, outdoorsSheet, lx, 1, 384);
                SetPassableIfValid(bldLayer, lx, 1, false);
                frontLayer.Tiles[lx, 1] = null;
            }

            // Esquina NO en (0, 0..1):
            SetTileIfValid(frontLayer, outdoorsSheet, 0, 0, 358);
            SetTileIfValid(bldLayer, outdoorsSheet, 0, 1, 383);
            SetPassableIfValid(bldLayer, 0, 1, false);

            if (side == AnnexSide.South)
            {
                // Entrada Norte abierta en lx=13..14:
                for (int lx = 13; lx <= 14; lx++)
                {
                    frontLayer.Tiles[lx, 0] = null;
                    bldLayer.Tiles[lx, 1] = null;
                    frontLayer.Tiles[lx, 1] = null;
                    SetDirtTile(backLayer, outdoorsSheet, lx, 0, TerrainTileClassifier.DirtPlainTileIndex);
                    SetDirtTile(backLayer, outdoorsSheet, lx, 1, TerrainTileClassifier.DirtPlainTileIndex);
                    backLayer.Tiles[lx, 0]?.Properties.Remove("Passable");
                    backLayer.Tiles[lx, 1]?.Properties.Remove("Passable");
                }

                // Postes de cancela:
                SetTileIfValid(frontLayer, outdoorsSheet, 12, 0, 360);
                SetTileIfValid(bldLayer, outdoorsSheet, 12, 1, 385);
                SetPassableIfValid(bldLayer, 12, 1, false);
                SetDirtTile(backLayer, outdoorsSheet, 12, 1, 225);

                SetTileIfValid(frontLayer, outdoorsSheet, 15, 0, 358);
                SetTileIfValid(bldLayer, outdoorsSheet, 15, 1, 383);
                SetPassableIfValid(bldLayer, 15, 1, false);
                SetDirtTile(backLayer, outdoorsSheet, 15, 1, 228);

                // Borde superior de tierra en fila 2:
                for (int lx = 1; lx < 12; lx++)
                    SetDirtTile(backLayer, outdoorsSheet, lx, 2, 201);
                SetDirtTile(backLayer, outdoorsSheet, 12, 2, 253);
                SetDirtTile(backLayer, outdoorsSheet, 13, 2, TerrainTileClassifier.DirtPlainTileIndex);
                SetDirtTile(backLayer, outdoorsSheet, 14, 2, TerrainTileClassifier.DirtPlainTileIndex);
                SetDirtTile(backLayer, outdoorsSheet, 15, 2, 200);
            }
            else
            {
                // Cancelas cerradas:
                for (int lx = 13; lx <= 14; lx++)
                {
                    SetTileIfValid(frontLayer, outdoorsSheet, lx, 0, 359);
                    SetTileIfValid(bldLayer, outdoorsSheet, lx, 1, 384);
                    SetPassableIfValid(bldLayer, lx, 1, false);
                }
                SetTileIfValid(frontLayer, outdoorsSheet, 15, 0, 360);
                SetTileIfValid(bldLayer, outdoorsSheet, 15, 1, 385);
                SetPassableIfValid(bldLayer, 15, 1, false);

                for (int lx = 1; lx <= 15; lx++)
                    SetDirtTile(backLayer, outdoorsSheet, lx, 2, 201);
            }

            // Paddock fence NE (lx=23..39):
            for (int lx = 23; lx < 40; lx++)
            {
                SetGrassTile(backLayer, outdoorsSheet, lx, 0, (lx % 2 == 0) ? TerrainTileClassifier.SunnyGrassTileIndex : TerrainTileClassifier.SunnyGrassSoftTileIndex);
                SetGrassTile(backLayer, outdoorsSheet, lx, 1, (lx % 2 == 0) ? TerrainTileClassifier.SunnyGrassTileIndex : TerrainTileClassifier.SunnyGrassSoftTileIndex);
                SetTileIfValid(frontLayer, outdoorsSheet, lx, 0, 359);
                SetTileIfValid(bldLayer, outdoorsSheet, lx, 1, 384);
                SetPassableIfValid(bldLayer, lx, 1, false);
                frontLayer.Tiles[lx, 1] = null;
            }

            SetTileIfValid(frontLayer, outdoorsSheet, 23, 0, 358);
            SetTileIfValid(bldLayer, outdoorsSheet, 23, 1, 383);
            SetPassableIfValid(bldLayer, 23, 1, false);

            SetTileIfValid(frontLayer, outdoorsSheet, 39, 0, 360);
            SetTileIfValid(bldLayer, outdoorsSheet, 39, 1, 385);
            SetPassableIfValid(bldLayer, 39, 1, false);

            for (int lx = 23; lx < 39; lx++)
                SetDirtTile(backLayer, outdoorsSheet, lx, 2, 201);

            SetDirtTile(backLayer, outdoorsSheet, 1, 2, 200);

            // 3. BORDE Y ENTRADA OESTE:
            for (int ly = 2; ly <= 4; ly++)
            {
                SetGrassTile(backLayer, outdoorsSheet, 0, ly, TerrainTileClassifier.SunnyGrassTileIndex);
                SetDirtTile(backLayer, outdoorsSheet, 1, ly, 225);
                SetTileIfValid(bldLayer, outdoorsSheet, 0, ly, 386);
                SetPassableIfValid(bldLayer, 0, ly, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 0, ly - 1, 361);
                frontLayer.Tiles[0, ly] = null;
            }

            SetTileIfValid(bldLayer, outdoorsSheet, 0, 4, 436);
            SetPassableIfValid(bldLayer, 0, 4, false);
            SetTileIfValid(frontLayer, outdoorsSheet, 0, 3, 361);
            frontLayer.Tiles[0, 4] = null;

            if (side == AnnexSide.East)
            {
                // Entrada Oeste abierta en ly=5..6:
                for (int lx = 0; lx <= 2; lx++)
                {
                    for (int ly = 5; ly <= 6; ly++)
                    {
                        bldLayer.Tiles[lx, ly] = null;
                        frontLayer.Tiles[lx, ly] = null;
                        alwaysFrontLayer.Tiles[lx, ly] = null;
                        SetDirtTile(backLayer, outdoorsSheet, lx, ly, TerrainTileClassifier.DirtPlainTileIndex);
                        backLayer.Tiles[lx, ly]?.Properties.Remove("Passable");
                    }
                }
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 2, 6, 12);
                SetDirtTile(backLayer, outdoorsSheet, 1, 4, 200);
                SetDirtTile(backLayer, outdoorsSheet, 1, 7, 228);

                SetTileIfValid(bldLayer, outdoorsSheet, 0, 7, 383);
                SetPassableIfValid(bldLayer, 0, 7, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 0, 6, 358);
                SetGrassTile(backLayer, outdoorsSheet, 0, 7, TerrainTileClassifier.SunnyGrassTileIndex);
                SetPassableIfValid(backLayer, 0, 7, false);
            }
            else
            {
                for (int ly = 5; ly <= 6; ly++)
                {
                    SetGrassTile(backLayer, outdoorsSheet, 0, ly, TerrainTileClassifier.SunnyGrassTileIndex);
                    SetDirtTile(backLayer, outdoorsSheet, 1, ly, 225);
                    SetTileIfValid(bldLayer, outdoorsSheet, 0, ly, 386);
                    SetPassableIfValid(bldLayer, 0, ly, false);
                    SetTileIfValid(frontLayer, outdoorsSheet, 0, ly - 1, 361);
                    frontLayer.Tiles[0, ly] = null;
                }
                SetTileIfValid(bldLayer, outdoorsSheet, 0, 6, 436);
                SetPassableIfValid(bldLayer, 0, 6, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 0, 5, 361);
                frontLayer.Tiles[0, 6] = null;
            }

            // Valla SO (ly=19..27):
            SetGrassTile(backLayer, outdoorsSheet, 0, 19, TerrainTileClassifier.SunnyGrassTileIndex);
            SetDirtTile(backLayer, outdoorsSheet, 1, 19, 225);
            SetTileIfValid(bldLayer, outdoorsSheet, 0, 19, 386);
            SetPassableIfValid(bldLayer, 0, 19, false);
            SetTileIfValid(frontLayer, outdoorsSheet, 0, 18, 361);
            frontLayer.Tiles[0, 19] = null;

            for (int ly = 20; ly <= 26; ly++)
            {
                SetGrassTile(backLayer, outdoorsSheet, 0, ly, TerrainTileClassifier.SunnyGrassTileIndex);
                SetDirtTile(backLayer, outdoorsSheet, 1, ly, 225);
                SetTileIfValid(bldLayer, outdoorsSheet, 0, ly, 386);
                SetPassableIfValid(bldLayer, 0, ly, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 0, ly - 1, 361);
                frontLayer.Tiles[0, ly] = null;
            }

            SetGrassTile(backLayer, outdoorsSheet, 0, 27, TerrainTileClassifier.SunnyGrassTileIndex);
            SetDirtTile(backLayer, outdoorsSheet, 1, 27, 225);
            SetTileIfValid(bldLayer, outdoorsSheet, 0, 27, 436);
            SetPassableIfValid(bldLayer, 0, 27, false);
            SetTileIfValid(frontLayer, outdoorsSheet, 0, 26, 361);
            frontLayer.Tiles[0, 27] = null;

            // 4. BORDE ESTE:
            for (int ly = 2; ly <= 9; ly++)
            {
                SetDirtTile(backLayer, outdoorsSheet, 36, ly, 228);
                SetGrassTile(backLayer, outdoorsSheet, 37, ly, TerrainTileClassifier.SunnyGrassTileIndex);
                bldLayer.Tiles[37, ly] = null;

                SetGrassTile(backLayer, outdoorsSheet, 38, ly, TerrainTileClassifier.SunnyGrassTileIndex);
                SetTileIfValid(bldLayer, outdoorsSheet, 38, ly, 386);
                SetPassableIfValid(bldLayer, 38, ly, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 38, ly - 1, 361);
                frontLayer.Tiles[38, ly] = null;
                alwaysFrontLayer.Tiles[38, ly] = null;

                SetGrassTile(backLayer, outdoorsSheet, 39, ly, TerrainTileClassifier.SunnyGrassSoftTileIndex);
                SetTileIfValid(bldLayer, outdoorsSheet, 39, ly, 16);
                SetPassableIfValid(bldLayer, 39, ly, false);
                frontLayer.Tiles[39, ly] = null;
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 39, ly, 1020);
            }

            SetGrassTile(backLayer, outdoorsSheet, 38, 10, TerrainTileClassifier.SunnyGrassTileIndex);
            SetTileIfValid(bldLayer, outdoorsSheet, 38, 10, 436);
            SetPassableIfValid(bldLayer, 38, 10, false);
            SetTileIfValid(frontLayer, outdoorsSheet, 38, 9, 361);
            frontLayer.Tiles[38, 10] = null;
            alwaysFrontLayer.Tiles[38, 10] = null;

            // Valla SE (ly=19..27):
            SetGrassTile(backLayer, outdoorsSheet, 38, 19, TerrainTileClassifier.SunnyGrassTileIndex);
            SetTileIfValid(bldLayer, outdoorsSheet, 38, 19, 386);
            SetPassableIfValid(bldLayer, 38, 19, false);
            SetTileIfValid(frontLayer, outdoorsSheet, 38, 18, 361);
            frontLayer.Tiles[38, 19] = null;
            alwaysFrontLayer.Tiles[38, 19] = null;

            SetDirtTile(backLayer, outdoorsSheet, 36, 19, 228);
            SetDirtTile(backLayer, outdoorsSheet, 37, 19, 251);

            for (int ly = 20; ly <= 26; ly++)
            {
                SetDirtTile(backLayer, outdoorsSheet, 36, ly, 228);
                SetGrassTile(backLayer, outdoorsSheet, 37, ly, TerrainTileClassifier.SunnyGrassTileIndex);
                bldLayer.Tiles[37, ly] = null;

                SetGrassTile(backLayer, outdoorsSheet, 38, ly, TerrainTileClassifier.SunnyGrassTileIndex);
                SetTileIfValid(bldLayer, outdoorsSheet, 38, ly, 386);
                SetPassableIfValid(bldLayer, 38, ly, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 38, ly - 1, 361);
                frontLayer.Tiles[38, ly] = null;
                alwaysFrontLayer.Tiles[38, ly] = null;

                SetGrassTile(backLayer, outdoorsSheet, 39, ly, TerrainTileClassifier.SunnyGrassSoftTileIndex);
                SetTileIfValid(bldLayer, outdoorsSheet, 39, ly, 16);
                SetPassableIfValid(bldLayer, 39, ly, false);
                frontLayer.Tiles[39, ly] = null;
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 39, ly, 1020);
            }

            SetGrassTile(backLayer, outdoorsSheet, 38, 27, TerrainTileClassifier.SunnyGrassTileIndex);
            SetTileIfValid(bldLayer, outdoorsSheet, 38, 27, 436);
            SetPassableIfValid(bldLayer, 38, 27, false);
            SetTileIfValid(frontLayer, outdoorsSheet, 38, 26, 361);
            frontLayer.Tiles[38, 27] = null;
            alwaysFrontLayer.Tiles[38, 27] = null;

            SetGrassTile(backLayer, outdoorsSheet, 39, 27, TerrainTileClassifier.SunnyGrassSoftTileIndex);
            SetTileIfValid(bldLayer, outdoorsSheet, 39, 27, 16);
            SetPassableIfValid(bldLayer, 39, 27, false);
            SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 39, 27, 1020);

            SetGrassTile(backLayer, outdoorsSheet, 39, 28, TerrainTileClassifier.SunnyGrassSoftTileIndex);
            SetTileIfValid(bldLayer, outdoorsSheet, 39, 28, 16);
            SetPassableIfValid(bldLayer, 39, 28, false);
            SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 39, 28, 1020);

            SetDirtTile(backLayer, outdoorsSheet, 36, 27, 253);
            SetGrassTile(backLayer, outdoorsSheet, 36, 28, TerrainTileClassifier.SunnyGrassTileIndex);
            SetGrassTile(backLayer, outdoorsSheet, 37, 27, TerrainTileClassifier.SunnyGrassTileIndex);
            SetGrassTile(backLayer, outdoorsSheet, 37, 28, TerrainTileClassifier.SunnyGrassTileIndex);

            // 5. BORDE Y ENTRADA SUR:
            SetTileIfValid(bldLayer, outdoorsSheet, 16, 28, 542);
            SetPassableIfValid(bldLayer, 16, 28, false);
            SetGrassTile(backLayer, outdoorsSheet, 16, 28, 537);

            for (int lx = 0; lx < width; lx++)
            {
                if (lx == 25) continue; // Acantilado natural

                SetGrassTile(backLayer, outdoorsSheet, lx, 28, (lx % 2 == 0) ? TerrainTileClassifier.SunnyGrassTileIndex : TerrainTileClassifier.SunnyGrassSoftTileIndex);
                if (lx != 16) bldLayer.Tiles[lx, 28] = null;
                frontLayer.Tiles[lx, 28] = null;
                alwaysFrontLayer.Tiles[lx, 28] = null;

                SetGrassTile(backLayer, outdoorsSheet, lx, 29, TerrainTileClassifier.SunnyGrassTileIndex);
                SetGrassTile(backLayer, outdoorsSheet, lx, 30, TerrainTileClassifier.SunnyGrassTileIndex);
                SetGrassTile(backLayer, outdoorsSheet, lx, 31, TerrainTileClassifier.SunnyGrassTileIndex);

                SetTileIfValid(bldLayer, outdoorsSheet, lx, 29, (lx % 2 == 0) ? 294 : 295);
                SetPassableIfValid(bldLayer, lx, 29, false);
                SetTileIfValid(bldLayer, outdoorsSheet, lx, 30, (lx % 2 == 0) ? 319 : 320);
                SetPassableIfValid(bldLayer, lx, 30, false);
                SetTileIfValid(bldLayer, outdoorsSheet, lx, 31, (lx % 2 == 0) ? 344 : 345);
                SetPassableIfValid(bldLayer, lx, 31, false);

                frontLayer.Tiles[lx, 29] = null;
                frontLayer.Tiles[lx, 30] = null;
                frontLayer.Tiles[lx, 31] = null;

                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 29, (lx % 2 == 0) ? 994 : 1019);
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 30, (lx % 2 == 0) ? 1019 : 1020);
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 31, (lx % 2 == 0) ? 1044 : 1045);
            }

            if (side == AnnexSide.North)
            {
                // Entrada Sur abierta en lx=13..14:
                for (int lx = 13; lx <= 14; lx++)
                {
                    for (int ly = 27; ly < height; ly++)
                    {
                        bldLayer.Tiles[lx, ly] = null;
                        frontLayer.Tiles[lx, ly] = null;
                        alwaysFrontLayer.Tiles[lx, ly] = null;
                        SetDirtTile(backLayer, outdoorsSheet, lx, ly, TerrainTileClassifier.DirtPlainTileIndex);
                        backLayer.Tiles[lx, ly]?.Properties.Remove("Passable");
                    }
                }

                SetDirtTile(backLayer, outdoorsSheet, 11, 27, 251);
                SetDirtTile(backLayer, outdoorsSheet, 12, 27, 253);
                SetDirtTile(backLayer, outdoorsSheet, 15, 27, 250);
                SetDirtTile(backLayer, outdoorsSheet, 16, 27, 251);

                SetDirtTile(backLayer, outdoorsSheet, 12, 28, 225);
                SetDirtTile(backLayer, outdoorsSheet, 15, 28, 228);

                SetTileIfValid(bldLayer, outdoorsSheet, 12, 29, 385);
                SetPassableIfValid(bldLayer, 12, 29, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 12, 28, 360);

                SetTileIfValid(bldLayer, outdoorsSheet, 15, 29, 383);
                SetPassableIfValid(bldLayer, 15, 29, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 15, 28, 358);
            }
            else
            {
                // Borde Sur cerrado con autotiling canónico (Back: 251):
                for (int lx = 10; lx <= 15; lx++)
                    SetDirtTile(backLayer, outdoorsSheet, lx, 27, 251);

                for (int lx = 13; lx <= 14; lx++)
                {
                    SetTileIfValid(bldLayer, outdoorsSheet, lx, 29, (lx % 2 == 0) ? 294 : 295);
                    SetPassableIfValid(bldLayer, lx, 29, false);
                    SetTileIfValid(bldLayer, outdoorsSheet, lx, 30, (lx % 2 == 0) ? 319 : 320);
                    SetPassableIfValid(bldLayer, lx, 30, false);
                    SetTileIfValid(bldLayer, outdoorsSheet, lx, 31, (lx % 2 == 0) ? 344 : 345);
                    SetPassableIfValid(bldLayer, lx, 31, false);

                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 29, (lx % 2 == 0) ? 994 : 1019);
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 30, (lx % 2 == 0) ? 1019 : 1020);
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 31, (lx % 2 == 0) ? 1044 : 1045);
                }
            }

            // 6. SECTOR SURESTE (PASTO AUTÉNTICO):
            for (int ly = 20; ly <= 26; ly++)
            {
                for (int lx = 28; lx <= 35; lx++)
                {
                    int? bIdx = bldLayer.Tiles[lx, ly]?.TileIndex;
                    if (bIdx is 708 or 710 or 695 or 712 or 768)
                        bldLayer.Tiles[lx, ly] = null;
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
            foreach (Layer dstLayer in target.Layers)
            {
                if (lx < 0 || lx >= dstLayer.LayerWidth || ly < 0 || ly >= dstLayer.LayerHeight)
                    continue;

                Layer? srcLayer = source.GetLayer(dstLayer.Id);
                if (srcLayer == null || sx < 0 || sx >= srcLayer.LayerWidth || sy < 0 || sy >= srcLayer.LayerHeight)
                {
                    dstLayer.Tiles[lx, ly] = null;
                    continue;
                }

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

            // Fusionar AlwaysFront2 sobre AlwaysFront para preservar copas multi-capa (ej. cerezos en esquinas SO y sur)
            Layer? srcAf2 = source.GetLayer("AlwaysFront2");
            Layer? dstAf = target.GetLayer("AlwaysFront");
            Layer? dstAf2 = target.GetLayer("AlwaysFront2");
            if (srcAf2 != null && dstAf != null &&
                lx >= 0 && lx < dstAf.LayerWidth && ly >= 0 && ly < dstAf.LayerHeight &&
                sx >= 0 && sx < srcAf2.LayerWidth && sy >= 0 && sy < srcAf2.LayerHeight)
            {
                Tile? af2Tile = srcAf2.Tiles[sx, sy];
                if (af2Tile != null && af2Tile.TileIndex >= 0)
                {
                    Tile clonedAf2 = CloneTile(af2Tile, dstAf, target);
                    StripVanillaWarpTileProperties(clonedAf2);
                    dstAf.Tiles[lx, ly] = clonedAf2;
                }
                if (dstAf2 != null && lx < dstAf2.LayerWidth && ly < dstAf2.LayerHeight)
                {
                    dstAf2.Tiles[lx, ly] = null;
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
        /// Compone la miniatura auténtica 40×32 de la granja Estándar (V10 aprobado) injertando los 4 bordes reales
        /// de Maps/Farm (acantilado y terraza forestal norte continua sin cortes de casa/patio, talud herboso oeste
        /// con cerezos completos incluyendo AlwaysFront2, valla y bosque este desde el pie del acantilado,
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
            // 1. BORDE NORTE (ly = 0..8): Injertar el acantilado de roca marrón y terraza forestal superior continua desde sy = 0..8
            int[] northSxMap = (side == AnnexSide.South)
                ? new int[]
                {
                    // Con el paso norte auténtico entre acantilados en lx = 22..23 (sx = 40..41) y terraza continua en lx = 28..39
                    0, 1, 2, 3, 4,
                    13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25,
                    36, 37, 38, 39,
                    40, 41,
                    42, 43, 44, 45,
                    14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25
                }
                : new int[]
                {
                    // Borde norte cerrado continuamente sin corte de paso de montaña ni huecos de casa/patio
                    0, 1, 2, 3, 4,
                    13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25,
                    13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25,
                    14, 15, 16, 17, 18, 19, 19, 19, 19
                };

            for (int lx = 0; lx < width && lx < northSxMap.Length; lx++)
            {
                int sx = northSxMap[lx];
                for (int ly = 0; ly <= 8; ly++)
                {
                    CopySourceCellToTarget(source, target, lx, ly, sx, ly);
                }
            }

            // Remate derecho canónico del acantilado norte en lx = 36, ly = 4..7 (469, 494, 519, 544)
            SetTileIfValid(bldLayer, outdoorsSheet, 36, 4, 469);
            SetPassableIfValid(bldLayer, 36, 4, false);
            SetTileIfValid(bldLayer, outdoorsSheet, 36, 5, 494);
            SetPassableIfValid(bldLayer, 36, 5, false);
            SetTileIfValid(bldLayer, outdoorsSheet, 36, 6, 519);
            SetPassableIfValid(bldLayer, 36, 6, false);
            SetTileIfValid(bldLayer, outdoorsSheet, 36, 7, 544);
            SetPassableIfValid(bldLayer, 36, 7, false);

            SetTileIfValid(backLayer, outdoorsSheet, 36, 8, 612);
            bldLayer.Tiles[36, 8] = null;
            frontLayer.Tiles[36, 8] = null;

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

            for (int lx = 3; lx <= 35; lx++)
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

            // Esquina interior SO en lx = 2, ly = 28..31 preservando la mitad derecha de la copa del cerezo SO (AlwaysFront en lx=2..3, ly=25..28)
            SetDirtTile(backLayer, outdoorsSheet, 2, 28);
            SetTileIfValid(bldLayer, outdoorsSheet, 2, 28, 412);
            SetPassableIfValid(bldLayer, 2, 28, false);
            SetTileIfValid(frontLayer, outdoorsSheet, 2, 28, 412);

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

            for (int ly = 29; ly < height; ly++)
            {
                CopySourceCellToTarget(source, target, 3, ly, srcX + 3, srcY + ly);
            }
            SetDirtTile(backLayer, outdoorsSheet, 3, 28);
            bldLayer.Tiles[3, 28] = null;
            SetTileIfValid(frontLayer, outdoorsSheet, 3, 28, 413);

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

            // 3. BORDE ESTE (lx = 34..39, ly = 0..31): Injertar franja boscosa este continua (ly = 0..31) + valla vertical desde el pie del acantilado (ly = 7..27)
            for (int ly = 0; ly < height; ly++)
            {
                int sy = ly + 27;
                for (int lx = 37; lx < width; lx++)
                {
                    CopySourceCellToTarget(source, target, lx, ly, lx + 40, sy);
                }
                if (ly >= 7)
                {
                    CopySourceCellToTarget(source, target, 36, ly, 76, sy);
                }
                if (ly >= 26)
                {
                    for (int lx = 34; lx <= 35; lx++)
                    {
                        CopySourceCellToTarget(source, target, lx, ly, lx + 40, sy);
                    }
                }
            }

            // En la terraza forestal noreste (ly = 0..6), sustituir la valla de lx = 37 por pasto forestal oscuro 351 y anclar el remate superior de la valla en Front(37, 6)
            for (int ly = 0; ly <= 6; ly++)
            {
                SetTileIfValid(backLayer, outdoorsSheet, 37, ly, 351);
                SetTileIfValid(bldLayer, outdoorsSheet, 37, ly, 16);
                SetPassableIfValid(bldLayer, 37, ly, false);
                if (frontLayer.Tiles[37, ly]?.TileIndex == 361)
                {
                    frontLayer.Tiles[37, ly] = null;
                }
            }
            SetTileIfValid(frontLayer, outdoorsSheet, 37, 6, 361);
            SetTileIfValid(bldLayer, outdoorsSheet, 37, 7, 386);
            SetPassableIfValid(bldLayer, 37, 7, false);

            SetTileIfValid(backLayer, outdoorsSheet, 36, 7, 612);
            bldLayer.Tiles[36, 7] = null;
            frontLayer.Tiles[36, 7] = null;

            SetTileIfValid(backLayer, outdoorsSheet, 36, 8, 612);
            bldLayer.Tiles[36, 8] = null;
            frontLayer.Tiles[36, 8] = null;

            // Costura continua del seto sureste en (34, 29): 325 -> 326
            SetTileIfValid(backLayer, outdoorsSheet, 34, 29, 326);
            SetTileIfValid(bldLayer, outdoorsSheet, 34, 29, 326);
            SetPassableIfValid(bldLayer, 34, 29, false);
            SetTileIfValid(frontLayer, outdoorsSheet, 34, 29, 326);

            for (int lx = 34; lx < width; lx++)
            {
                SetTileIfValid(backLayer, outdoorsSheet, lx, 30, 351);
                SetTileIfValid(backLayer, outdoorsSheet, lx, 31, 355);
                SetPassableIfValid(backLayer, lx, 31, false);
            }

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

        /// <summary>
        /// Sanea la capa "Paths" y las propiedades de generación ("NoSpawn", "Diggable", "Passable") en todos los bordes,
        /// acantilados decorativos, vallas, copas de cierre y agua para impedir que GameLocation.loadObjects() o la generación
        /// diaria coloquen árboles, maleza, piedras o ramas encima de elementos decorativos o de profundidad.
        /// </summary>
        private static void SanitizeAnnexSpawnsAndPaths(
            Map target,
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer alwaysFrontLayer,
            AnnexTemplate template,
            AnnexSide side,
            HashSet<(int X, int Y)> openingBorderTiles,
            int width,
            int height)
        {
            Layer? pathsLayer = target.GetLayer("Paths");
            bool isStandard = template.Id.Equals("Standard", StringComparison.OrdinalIgnoreCase);
            bool isForaging = template.Id.Equals("Foraging", StringComparison.OrdinalIgnoreCase);
            bool isMining = template.Id.Equals("Mining", StringComparison.OrdinalIgnoreCase);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Tile? backTile = backLayer.Tiles[x, y];
                    Tile? bldTile = bldLayer.Tiles[x, y];
                    Tile? frontTile = frontLayer.Tiles[x, y];

                    bool isBorderZone = x <= 2 || x >= width - 3 || y <= 2 || y >= height - 3;
                    bool isWater = backTile != null && IsWaterTile(backTile);
                    bool isBridge = bldTile != null && TerrainTileClassifier.IsWoodenBridgeTile(bldTile.TileIndex);
                    bool hasBlockingBuilding = bldTile != null && !isBridge;
                    bool isImpassableBack = backTile != null &&
                        backTile.Properties.TryGetValue("Passable", out var passVal) &&
                        string.Equals(passVal?.ToString(), "F", StringComparison.OrdinalIgnoreCase);

                    bool isCliffOrFenceFront = frontTile != null && frontTile.TileIndex is
                        325 or 326 or 327 or 358 or 359 or 360 or 361 or
                        391 or 394 or 412 or 413 or 414 or 415 or 416 or 419 or 438 or 439;

                    bool isDecorativeDepthZone = false;
                    if (isStandard)
                    {
                        bool inNorthPass = side == AnnexSide.South && (x == 22 || x == 23);
                        bool inWestPass = side == AnnexSide.East && (y == 18 || y == 19) && x <= 3;
                        bool inSouthPass = side == AnnexSide.North && (x >= 24 && x <= 26) && y >= 27;
                        if ((y <= 7 && !inNorthPass) || (x <= 2 && !inWestPass) || x >= 37 || (x >= 34 && y >= 26) || (y >= 28 && !inSouthPass))
                        {
                            isDecorativeDepthZone = true;
                        }
                    }
                    else if (isForaging)
                    {
                        bool inNorthPass = side == AnnexSide.South && (x == 26 || x == 27) && y <= 3;
                        bool inWestPass = side == AnnexSide.East && (y == 14 || y == 15) && x <= 3;
                        bool inSouthPass = side == AnnexSide.North && (x == 26 || x == 27) && y >= 27;
                        if ((y <= 3 && !inNorthPass) || (x <= 2 && !inWestPass) || x >= 36 || (y >= 27 && !inSouthPass))
                        {
                            isDecorativeDepthZone = true;
                        }
                    }
                    else if (isMining)
                    {
                        bool inNorthPass = side == AnnexSide.South && (x == 15 || x == 16) && y <= 3;
                        bool inWestPass = side == AnnexSide.East && (y == 19 || y == 20) && x <= 2;
                        bool inSouthPass = side == AnnexSide.North && (x == 24 || x == 25) && y >= 27;
                        bool inQuarrySouthCliff = y >= 13 && y <= 17 && x <= 28 && !(x >= 14 && x <= 17);
                        if ((y <= 3 && !inNorthPass) || (x <= 3 && !inWestPass) || x >= 37 || (y >= 28 && !inSouthPass) || inQuarrySouthCliff)
                        {
                            isDecorativeDepthZone = true;
                        }
                    }

                    if (isBorderZone || isWater || hasBlockingBuilding || isImpassableBack || isCliffOrFenceFront || isDecorativeDepthZone)
                    {
                        if (pathsLayer != null)
                        {
                            // En Foraging preservar únicamente el marcador de tocón renovable (TileIndex 21) dentro del claro interior
                            int? pathIdx = pathsLayer.Tiles[x, y]?.TileIndex;
                            bool keepRenewableStumpMarker = isForaging && pathIdx == 21 && !isDecorativeDepthZone && !isWater && !hasBlockingBuilding;
                            if (!keepRenewableStumpMarker)
                            {
                                pathsLayer.Tiles[x, y] = null;
                            }
                        }

                        if (backTile != null)
                        {
                            backTile.Properties["NoSpawn"] = "All";
                            backTile.Properties.Remove("Diggable");

                            if (isDecorativeDepthZone && !openingBorderTiles.Contains((x, y)))
                            {
                                backTile.Properties["Passable"] = "F";
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Compone la miniatura canónica 40×32 de la Franja Forestal (Foraging / Farm_Forest V28 aprobado)
        /// usando un recorte 100% contiguo en (0, 14) que preserva intacto el Claro Forestal Oeste (lx=0..22, ly=0..31)
        /// con sus 4 tocones nobles renovables y la pradera cultivable soleada (lx=18..35, ly=4..26),
        /// enmarcando los 4 bordes con el dosel forestal profundo original de Farm_Foraging sin costuras ni lago central.
        /// </summary>
        private static void ApplyForagingFarmEnclosure(
            Map source,
            Map target,
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer alwaysFrontLayer,
            TileSheet outdoorsSheet,
            AnnexSide side,
            int width,
            int height)
        {
            const int cx = 0;
            const int cy = 14;

            // 1. Recorte base 100% contiguo en (0, 14) -> sx = 0..39, sy = 14..45
            for (int lx = 0; lx < width; lx++)
            {
                for (int ly = 0; ly < height; ly++)
                {
                    CopySourceCellToTarget(source, target, lx, ly, cx + lx, cy + ly);
                }
            }

            // 2. Limpiar sombras de árboles de casa/granja (1094/1119) y arbustos cortados en el extremo derecho del campo
            for (int lx = 18; lx < width; lx++)
            {
                for (int ly = 0; ly < height; ly++)
                {
                    int? backIdx = backLayer.Tiles[lx, ly]?.TileIndex;
                    if (backIdx == 1094 || backIdx == 1119)
                    {
                        SetGrassTile(backLayer, outdoorsSheet, lx, ly, TerrainTileClassifier.SunnyGrassTileIndex);
                    }
                    if (lx >= 34 || ly <= 3 || ly >= 27)
                    {
                        bldLayer.Tiles[lx, ly] = null;
                        frontLayer.Tiles[lx, ly] = null;
                        alwaysFrontLayer.Tiles[lx, ly] = null;
                    }
                }
            }

            // Suelo limpio de hierba soleada en el margen norte, este y sur del campo derecho (lx = 18..39):
            for (int lx = 18; lx < width; lx++)
            {
                for (int ly = 0; ly <= 4; ly++)
                {
                    if (lx >= 22 || ly <= 3)
                    {
                        SetGrassTile(backLayer, outdoorsSheet, lx, ly, TerrainTileClassifier.SunnyGrassTileIndex);
                        bldLayer.Tiles[lx, ly] = null;
                        frontLayer.Tiles[lx, ly] = null;
                    }
                }
                for (int ly = 26; ly < height; ly++)
                {
                    if (lx >= 19 || ly >= 27)
                    {
                        SetGrassTile(backLayer, outdoorsSheet, lx, ly, TerrainTileClassifier.SunnyGrassTileIndex);
                        bldLayer.Tiles[lx, ly] = null;
                        frontLayer.Tiles[lx, ly] = null;
                    }
                }
            }
            for (int lx = 35; lx < width; lx++)
            {
                for (int ly = 0; ly < height; ly++)
                {
                    SetGrassTile(backLayer, outdoorsSheet, lx, ly, TerrainTileClassifier.SunnyGrassTileIndex);
                    bldLayer.Tiles[lx, ly] = null;
                    frontLayer.Tiles[lx, ly] = null;
                }
            }

            // 3. Autotile limpio de los bordes norte (ly=4), este (lx=34) y sur (ly=25) del bancal cultivable derecho:
            for (int lx = 18; lx < 36; lx++)
            {
                for (int ly = 4; ly < 26; ly++)
                {
                    if (backLayer.Tiles[lx, ly]?.TileIndex == 587)
                    {
                        if (ly == 4)
                            SetGrassTile(backLayer, outdoorsSheet, lx, ly, 201);
                        else if (ly == 25)
                            SetGrassTile(backLayer, outdoorsSheet, lx, ly, 251);
                        else if (lx == 34)
                            SetGrassTile(backLayer, outdoorsSheet, lx, ly, 228);
                    }
                }
            }
            if (backLayer.Tiles[34, 4]?.TileIndex is 201 or 228 or 587)
                SetGrassTile(backLayer, outdoorsSheet, 34, 4, 203);
            if (backLayer.Tiles[34, 25]?.TileIndex is 251 or 228 or 587)
                SetGrassTile(backLayer, outdoorsSheet, 34, 25, 253);

            // 4. BORDE NORTE (ly = 0..3): Dosel forestal profundo continuo (946 + festón inferior 942/943, 967/968)
            for (int lx = 0; lx < width; lx++)
            {
                for (int ly = 0; ly <= 1; ly++)
                {
                    if (lx >= 18)
                        SetTileIfValid(backLayer, outdoorsSheet, lx, ly, 351);
                    SetPassableIfValid(backLayer, lx, ly, false);
                    SetTileIfValid(bldLayer, outdoorsSheet, lx, ly, 16);
                    SetPassableIfValid(bldLayer, lx, ly, false);
                    frontLayer.Tiles[lx, ly] = null;
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, ly, 946);
                }

                bool isEven = (lx % 2 == 0);
                if (lx >= 18)
                {
                    SetTileIfValid(backLayer, outdoorsSheet, lx, 2, 376);
                    SetPassableIfValid(backLayer, lx, 2, false);
                    SetGrassTile(backLayer, outdoorsSheet, lx, 3, TerrainTileClassifier.SunnyGrassTileIndex);
                }
                SetTileIfValid(bldLayer, outdoorsSheet, lx, 2, 16);
                SetPassableIfValid(bldLayer, lx, 2, false);
                frontLayer.Tiles[lx, 2] = null;
                frontLayer.Tiles[lx, 3] = null;
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 2, isEven ? 942 : 943);
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 3, isEven ? 967 : 968);
            }

            // Preservar las 3 columnas izquierdas del dosel oeste (lx = 0..2, ly = 2..3)
            for (int lx = 0; lx <= 2; lx++)
            {
                for (int ly = 2; ly <= 3; ly++)
                {
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, ly, 946);
                }
            }
            SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 3, 2, 992);
            SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 3, 3, 994);

            // 5. BORDE ESTE (lx = 36..39, ly = 0..31): Valla vertical rústica + dosel forestal profundo
            for (int ly = 4; ly <= 26; ly++)
            {
                bool isEven = (ly % 2 == 0);
                SetGrassTile(backLayer, outdoorsSheet, 36, ly, TerrainTileClassifier.SunnyGrassTileIndex);
                bldLayer.Tiles[36, ly] = null;
                frontLayer.Tiles[36, ly] = null;
                alwaysFrontLayer.Tiles[36, ly] = null;

                SetGrassTile(backLayer, outdoorsSheet, 37, ly, TerrainTileClassifier.SunnyGrassTileIndex);
                SetPassableIfValid(backLayer, 37, ly, false);
                SetTileIfValid(bldLayer, outdoorsSheet, 37, ly, 386);
                SetPassableIfValid(bldLayer, 37, ly, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 37, ly, 361);
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 37, ly, isEven ? 994 : 1019);

                SetTileIfValid(backLayer, outdoorsSheet, 38, ly, 351);
                SetPassableIfValid(backLayer, 38, ly, false);
                SetTileIfValid(bldLayer, outdoorsSheet, 38, ly, 16);
                SetPassableIfValid(bldLayer, 38, ly, false);
                frontLayer.Tiles[38, ly] = null;
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 38, ly, isEven ? 995 : 1020);

                SetTileIfValid(backLayer, outdoorsSheet, 39, ly, 351);
                SetPassableIfValid(backLayer, 39, ly, false);
                SetTileIfValid(bldLayer, outdoorsSheet, 39, ly, 16);
                SetPassableIfValid(bldLayer, 39, ly, false);
                frontLayer.Tiles[39, ly] = null;
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 39, ly, 946);
            }

            for (int ly = 0; ly <= 3; ly++)
            {
                for (int lx = 37; lx < width; lx++)
                {
                    SetTileIfValid(backLayer, outdoorsSheet, lx, ly, 351);
                    SetPassableIfValid(backLayer, lx, ly, false);
                    SetTileIfValid(bldLayer, outdoorsSheet, lx, ly, 16);
                    SetPassableIfValid(bldLayer, lx, ly, false);
                    frontLayer.Tiles[lx, ly] = null;
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, ly, 946);
                }
            }

            // 6. BORDE SUR (ly = 27..31): Dosel forestal profundo continuo (festón superior 1042/1043, 1067/1068 + 946)
            for (int lx = 0; lx < width; lx++)
            {
                bool isEven = (lx % 2 == 0);
                if (lx >= 18)
                {
                    SetGrassTile(backLayer, outdoorsSheet, lx, 27, TerrainTileClassifier.SunnyGrassTileIndex);
                    SetTileIfValid(backLayer, outdoorsSheet, lx, 28, 326);
                }
                SetPassableIfValid(backLayer, lx, 28, false);
                bldLayer.Tiles[lx, 27] = null;
                frontLayer.Tiles[lx, 27] = null;
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 27, isEven ? 1042 : 1043);

                SetTileIfValid(bldLayer, outdoorsSheet, lx, 28, 16);
                SetPassableIfValid(bldLayer, lx, 28, false);
                frontLayer.Tiles[lx, 28] = null;
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 28, isEven ? 1067 : 1068);

                for (int ly = 29; ly < height; ly++)
                {
                    if (lx >= 18)
                        SetTileIfValid(backLayer, outdoorsSheet, lx, ly, 351);
                    SetPassableIfValid(backLayer, lx, ly, false);
                    SetTileIfValid(bldLayer, outdoorsSheet, lx, ly, 16);
                    SetPassableIfValid(bldLayer, lx, ly, false);
                    frontLayer.Tiles[lx, ly] = null;
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, ly, 946);
                }
            }

            for (int ly = 27; ly < height; ly++)
            {
                for (int lx = 0; lx <= 2; lx++)
                {
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, ly, 946);
                }
                for (int lx = 37; lx < width; lx++)
                {
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, ly, 946);
                }
            }
            SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 3, 27, 1019);
            SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 3, 28, 1044);

            // 7. Apertura única de entrada según el lado activo (0 salidas muertas):
            if (side == AnnexSide.South)
            {
                // Entrada Norte activa en lx = 26..27, ly = 0..3
                for (int lx = 26; lx <= 27; lx++)
                {
                    for (int ly = 0; ly <= 3; ly++)
                    {
                        SetDirtTile(backLayer, outdoorsSheet, lx, ly, TerrainTileClassifier.DirtPlainTileIndex);
                        bldLayer.Tiles[lx, ly] = null;
                        frontLayer.Tiles[lx, ly] = null;
                        alwaysFrontLayer.Tiles[lx, ly] = null;
                        backLayer.Tiles[lx, ly]?.Properties.Remove("Passable");
                    }
                }
                for (int ly = 0; ly <= 1; ly++)
                {
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 25, ly, (ly % 2 == 0) ? 995 : 1020);
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 28, ly, (ly % 2 == 0) ? 994 : 1019);
                }
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 25, 2, 1045);
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 28, 2, 1044);
                SetTileIfValid(bldLayer, outdoorsSheet, 25, 2, 386);
                SetPassableIfValid(bldLayer, 25, 2, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 25, 1, 361);
                SetTileIfValid(bldLayer, outdoorsSheet, 28, 2, 386);
                SetPassableIfValid(bldLayer, 28, 2, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 28, 1, 361);
            }
            else if (side == AnnexSide.North)
            {
                // Entrada Sur activa en lx = 26..27, ly = 27..31
                for (int lx = 26; lx <= 27; lx++)
                {
                    for (int ly = 26; ly < height; ly++)
                    {
                        SetDirtTile(backLayer, outdoorsSheet, lx, ly, TerrainTileClassifier.DirtPlainTileIndex);
                        bldLayer.Tiles[lx, ly] = null;
                        frontLayer.Tiles[lx, ly] = null;
                        alwaysFrontLayer.Tiles[lx, ly] = null;
                        backLayer.Tiles[lx, ly]?.Properties.Remove("Passable");
                    }
                }
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 25, 28, 995);
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 28, 28, 994);
                for (int ly = 29; ly < height; ly++)
                {
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 25, ly, (ly % 2 == 0) ? 995 : 1020);
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 28, ly, (ly % 2 == 0) ? 994 : 1019);
                }
                SetTileIfValid(bldLayer, outdoorsSheet, 25, 28, 386);
                SetPassableIfValid(bldLayer, 25, 28, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 25, 27, 361);
                SetTileIfValid(bldLayer, outdoorsSheet, 28, 28, 386);
                SetPassableIfValid(bldLayer, 28, 28, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 28, 27, 361);
            }
            else if (side == AnnexSide.East)
            {
                // Entrada Oeste activa en lx = 0..4, ly = 14..15 (sendero forestal de hierba sin recorte rectangular de tierra)
                for (int lx = 0; lx <= 4; lx++)
                {
                    for (int ly = 14; ly <= 15; ly++)
                    {
                        SetTileIfValid(backLayer, outdoorsSheet, lx, ly, (lx <= 1) ? 351 : ((lx == 2) ? 352 : TerrainTileClassifier.SunnyGrassTileIndex));
                        backLayer.Tiles[lx, ly]?.Properties.Remove("Passable");
                        bldLayer.Tiles[lx, ly] = null;
                        frontLayer.Tiles[lx, ly] = null;
                        alwaysFrontLayer.Tiles[lx, ly] = null;
                    }
                }
                for (int lx = 0; lx <= 2; lx++)
                {
                    bool isEven = (lx % 2 == 0);
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 12, isEven ? 942 : 943);
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 13, isEven ? 967 : 968);
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 16, isEven ? 1042 : 1043);
                    SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 17, isEven ? 1067 : 1068);
                }
                SetTileIfValid(bldLayer, outdoorsSheet, 2, 13, 386);
                SetPassableIfValid(bldLayer, 2, 13, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 2, 12, 361);
                SetTileIfValid(bldLayer, outdoorsSheet, 2, 16, 386);
                SetPassableIfValid(bldLayer, 2, 16, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 2, 15, 361);
            }
        }

        /// <summary>
        /// Compone la miniatura canónica 40×32 de la Cantera de Minerales (Mining / Farm_Hilltop V10 aprobado)
        /// usando el recorte contiguo en (0, 33) que centra la meseta de la cantera y su escalinata de piedra (lx=4..31, ly=2..17),
        /// el valle sur con estanque SO (lx=3..36, ly=18..27) y el campo de cultivo este (lx=29..36, ly=3..27),
        /// eliminando la zona norte inalcanzable y utilizando bordes 100% canónicos de Farm_Mining.
        /// </summary>
        private static void ApplyMiningFarmEnclosure(
            Map source,
            Map target,
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer alwaysFrontLayer,
            TileSheet outdoorsSheet,
            AnnexSide side,
            int width,
            int height)
        {
            const int cx = 0;
            const int cy = 33;

            // 1. Recorte base contiguo en (0, 33) -> sx = 0..39, sy = 33..64
            for (int lx = 0; lx < width; lx++)
            {
                for (int ly = 0; ly < height; ly++)
                {
                    CopySourceCellToTarget(source, target, lx, ly, cx + lx, cy + ly);
                }
            }

            // 2. Borde Este (lx = 36..39, ly = 0..31): Copiar borde este auténtico de Farm_Mining (sx = 76..79, sy = 33..64)
            for (int ly = 0; ly < height; ly++)
            {
                int sy = cy + ly;
                for (int dx = 0; dx < 4; dx++)
                {
                    int lx = 36 + dx;
                    CopySourceCellToTarget(source, target, lx, ly, 76 + dx, sy);
                }
            }

            // Completar la casilla 117 en la copa del cerezo este en (38, 8)
            SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 38, 8, 117);

            // Línea continua de valla vertical en lx = 37 (ly = 3..26) y margen limpio en lx = 36, preservando los árboles completos 3xN en lx = 37..39
            for (int ly = 3; ly <= 26; ly++)
            {
                SetDirtTile(backLayer, outdoorsSheet, 36, ly, TerrainTileClassifier.DirtPlainTileIndex);
                bldLayer.Tiles[36, ly] = null;
                frontLayer.Tiles[36, ly] = null;
                alwaysFrontLayer.Tiles[36, ly] = null;

                SetTileIfValid(backLayer, outdoorsSheet, 37, ly, 351);
                SetPassableIfValid(backLayer, 37, ly, false);
                if (ly == 3)
                {
                    SetTileIfValid(bldLayer, outdoorsSheet, 37, ly, 16);
                    SetPassableIfValid(bldLayer, 37, ly, false);
                    SetTileIfValid(frontLayer, outdoorsSheet, 37, ly, 361);
                }
                else if (ly == 4)
                {
                    SetTileIfValid(bldLayer, outdoorsSheet, 37, ly, 386);
                    SetPassableIfValid(bldLayer, 37, ly, false);
                }
                else if (ly == 26)
                {
                    SetTileIfValid(bldLayer, outdoorsSheet, 37, ly, 436);
                    SetPassableIfValid(bldLayer, 37, ly, false);
                }
                else
                {
                    SetTileIfValid(bldLayer, outdoorsSheet, 37, ly, 411);
                    SetPassableIfValid(bldLayer, 37, ly, false);
                }
            }

            // Limpiar restos de borde de risco en Front(37..38, 22..23)
            for (int lx = 37; lx <= 38; lx++)
            {
                for (int ly = 22; ly <= 23; ly++)
                {
                    int? fIdx = frontLayer.Tiles[lx, ly]?.TileIndex;
                    if (fIdx is 415 or 350 or 326)
                    {
                        frontLayer.Tiles[lx, ly] = null;
                    }
                }
                if (bldLayer.Tiles[38, 22]?.TileIndex == 414)
                {
                    SetTileIfValid(bldLayer, outdoorsSheet, 38, 22, 16);
                    SetPassableIfValid(bldLayer, 38, 22, false);
                }
            }

            // Esquina Sureste (lx = 34..39, ly = 27..31) con dos pinos completos 3x4
            for (int ly = 28; ly < height; ly++)
            {
                int sy = cy + ly;
                for (int lx = 34; lx <= 36; lx++)
                {
                    CopySourceCellToTarget(source, target, lx, ly, lx, sy);
                }
                for (int dx = 0; dx < 3; dx++)
                {
                    int lx = 37 + dx;
                    CopySourceCellToTarget(source, target, lx, ly, 75 + dx, sy);
                    SetTileIfValid(backLayer, outdoorsSheet, lx, ly, 351);
                    SetPassableIfValid(backLayer, lx, ly, false);
                    if (ly == 28)
                    {
                        SetTileIfValid(frontLayer, outdoorsSheet, lx, ly, (lx % 2 != 0) ? 413 : 414);
                    }
                    else if (ly == 29)
                    {
                        SetTileIfValid(frontLayer, outdoorsSheet, lx, ly, 326);
                    }
                    else
                    {
                        SetTileIfValid(bldLayer, outdoorsSheet, lx, ly, (ly == 30) ? 355 : 380);
                        SetPassableIfValid(bldLayer, lx, ly, false);
                    }
                }
            }

            SetDirtTile(backLayer, outdoorsSheet, 36, 27, TerrainTileClassifier.DirtPlainTileIndex);
            bldLayer.Tiles[36, 27] = null;
            for (int lx = 37; lx < width; lx++)
            {
                SetTileIfValid(backLayer, outdoorsSheet, lx, 27, 351);
                SetPassableIfValid(backLayer, lx, 27, false);
                SetTileIfValid(bldLayer, outdoorsSheet, lx, 27, 16);
                SetPassableIfValid(bldLayer, lx, 27, false);
            }

            // 3. Borde Norte (lx = 5..39, ly = 0..3): Talud herboso norte con arbustos y valla horizontal continua en ly = 2..3
            for (int lx = 5; lx <= 36; lx++)
            {
                SetTileIfValid(backLayer, outdoorsSheet, lx, 0, (lx > 5) ? 351 : 350);
                SetPassableIfValid(backLayer, lx, 0, false);
                SetTileIfValid(bldLayer, outdoorsSheet, lx, 0, 16);
                SetPassableIfValid(bldLayer, lx, 0, false);
                frontLayer.Tiles[lx, 0] = null;
                alwaysFrontLayer.Tiles[lx, 0] = null;

                int row1Back = (lx == 5) ? 375 : ((lx == 36) ? 377 : 376);
                SetTileIfValid(backLayer, outdoorsSheet, lx, 1, row1Back);
                SetPassableIfValid(backLayer, lx, 1, false);
                SetTileIfValid(bldLayer, outdoorsSheet, lx, 1, 175);
                SetPassableIfValid(bldLayer, lx, 1, false);
                frontLayer.Tiles[lx, 1] = null;
                alwaysFrontLayer.Tiles[lx, 1] = null;
            }

            for (int ly = 0; ly <= 3; ly++)
            {
                CopySourceCellToTarget(source, target, 5, ly, 5, 33 + ly);
            }

            int[] bushXs = { 7, 11, 18, 22, 26, 30, 34 };
            foreach (int bx in bushXs)
            {
                if (bx + 1 <= 36)
                {
                    SetTileIfValid(frontLayer, outdoorsSheet, bx, 0, 13);
                    SetTileIfValid(frontLayer, outdoorsSheet, bx + 1, 0, 14);
                    SetTileIfValid(bldLayer, outdoorsSheet, bx, 1, 38);
                    SetPassableIfValid(bldLayer, bx, 1, false);
                    SetTileIfValid(bldLayer, outdoorsSheet, bx + 1, 1, 39);
                    SetPassableIfValid(bldLayer, bx + 1, 1, false);
                }
            }

            for (int lx = 27; lx <= 36; lx++)
            {
                SetGrassTile(backLayer, outdoorsSheet, lx, 2, TerrainTileClassifier.SunnyGrassTileIndex);
                SetPassableIfValid(backLayer, lx, 2, false);
                bldLayer.Tiles[lx, 2] = null;
                SetTileIfValid(frontLayer, outdoorsSheet, lx, 2, (lx == 36) ? 360 : 359);
                alwaysFrontLayer.Tiles[lx, 2] = null;

                int row3Back = (lx == 27) ? 657 : ((lx <= 30) ? 175 : ((lx == 31) ? 344 : 401));
                SetTileIfValid(backLayer, outdoorsSheet, lx, 3, row3Back);
                SetTileIfValid(bldLayer, outdoorsSheet, lx, 3, (lx == 36) ? 385 : 384);
                SetPassableIfValid(bldLayer, lx, 3, false);
                frontLayer.Tiles[lx, 3] = null;
                alwaysFrontLayer.Tiles[lx, 3] = null;
            }

            // Remate redondeado superior del acantilado este de la cantera en (31, 4)
            SetTileIfValid(bldLayer, outdoorsSheet, 31, 4, 369);
            SetPassableIfValid(bldLayer, 31, 4, false);
            frontLayer.Tiles[31, 4] = null;

            for (int ly = 0; ly <= 3; ly++)
            {
                for (int lx = 37; lx < width; lx++)
                {
                    SetTileIfValid(backLayer, outdoorsSheet, lx, ly, 351);
                    SetPassableIfValid(backLayer, lx, ly, false);
                    if (ly < 3)
                    {
                        SetTileIfValid(bldLayer, outdoorsSheet, lx, ly, 16);
                        SetPassableIfValid(bldLayer, lx, ly, false);
                        frontLayer.Tiles[lx, ly] = null;
                        alwaysFrontLayer.Tiles[lx, ly] = null;
                    }
                }
            }
            SetTileIfValid(frontLayer, outdoorsSheet, 37, 0, 13);
            SetTileIfValid(frontLayer, outdoorsSheet, 38, 0, 14);
            SetTileIfValid(bldLayer, outdoorsSheet, 37, 1, 38);
            SetPassableIfValid(bldLayer, 37, 1, false);
            SetTileIfValid(bldLayer, outdoorsSheet, 38, 1, 39);
            SetPassableIfValid(bldLayer, 38, 1, false);

            // 4. Apertura única de entrada según el lado activo (0 salidas muertas):
            if (side == AnnexSide.North)
            {
                // Entrada Sur activa en lx = 24..25, ly = 28..31
                for (int ly = 28; ly < height; ly++)
                {
                    int sy = cy + ly;
                    for (int dx = 0; dx < 3; dx++)
                    {
                        CopySourceCellToTarget(source, target, 21 + dx, ly, 37 + dx, sy);
                    }
                }
                for (int ly = 26; ly < height; ly++)
                {
                    for (int lx = 24; lx <= 25; lx++)
                    {
                        SetDirtTile(backLayer, outdoorsSheet, lx, ly, TerrainTileClassifier.DirtPlainTileIndex);
                        bldLayer.Tiles[lx, ly] = null;
                        frontLayer.Tiles[lx, ly] = null;
                        alwaysFrontLayer.Tiles[lx, ly] = null;
                        backLayer.Tiles[lx, ly]?.Properties.Remove("Passable");
                    }
                }

                SetDirtTile(backLayer, outdoorsSheet, 26, 28, TerrainTileClassifier.DirtPlainTileIndex);
                SetPassableIfValid(backLayer, 26, 28, false);
                bldLayer.Tiles[26, 28] = null;
                SetTileIfValid(frontLayer, outdoorsSheet, 26, 28, 412);
                alwaysFrontLayer.Tiles[26, 28] = null;

                SetTileIfValid(backLayer, outdoorsSheet, 26, 29, 325);
                SetPassableIfValid(backLayer, 26, 29, false);
                SetTileIfValid(bldLayer, outdoorsSheet, 26, 29, 391);
                SetPassableIfValid(bldLayer, 26, 29, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 26, 29, 391);
                alwaysFrontLayer.Tiles[26, 29] = null;

                SetTileIfValid(backLayer, outdoorsSheet, 26, 30, 350);
                SetPassableIfValid(backLayer, 26, 30, false);
                SetTileIfValid(bldLayer, outdoorsSheet, 26, 30, 416);
                SetPassableIfValid(bldLayer, 26, 30, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 26, 30, 416);
                alwaysFrontLayer.Tiles[26, 30] = null;

                SetTileIfValid(backLayer, outdoorsSheet, 26, 31, 350);
                SetPassableIfValid(backLayer, 26, 31, false);
                SetTileIfValid(bldLayer, outdoorsSheet, 26, 31, 391);
                SetPassableIfValid(bldLayer, 26, 31, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 26, 31, 391);
                alwaysFrontLayer.Tiles[26, 31] = null;
            }
            else if (side == AnnexSide.South)
            {
                // Entrada Norte activa en lx = 15..16, ly = 0..3 (alineada con la escalinata de piedra de la cantera)
                SetTileIfValid(backLayer, outdoorsSheet, 14, 0, 352);
                SetPassableIfValid(backLayer, 14, 0, false);
                SetTileIfValid(backLayer, outdoorsSheet, 14, 1, 377);
                SetPassableIfValid(backLayer, 14, 1, false);
                SetTileIfValid(backLayer, outdoorsSheet, 17, 0, 350);
                SetPassableIfValid(backLayer, 17, 0, false);
                SetTileIfValid(backLayer, outdoorsSheet, 17, 1, 375);
                SetPassableIfValid(backLayer, 17, 1, false);

                SetTileIfValid(frontLayer, outdoorsSheet, 14, 2, 360);
                SetTileIfValid(bldLayer, outdoorsSheet, 14, 3, 385);
                SetPassableIfValid(bldLayer, 14, 3, false);

                SetTileIfValid(frontLayer, outdoorsSheet, 17, 2, 358);
                SetTileIfValid(bldLayer, outdoorsSheet, 17, 3, 383);
                SetPassableIfValid(bldLayer, 17, 3, false);

                for (int lx = 15; lx <= 16; lx++)
                {
                    for (int ly = 0; ly <= 3; ly++)
                    {
                        SetTileIfValid(backLayer, outdoorsSheet, lx, ly, (ly == 3) ? 655 : TerrainTileClassifier.SunnyGrassTileIndex);
                        backLayer.Tiles[lx, ly]?.Properties.Remove("Passable");
                        bldLayer.Tiles[lx, ly] = null;
                        frontLayer.Tiles[lx, ly] = null;
                        alwaysFrontLayer.Tiles[lx, ly] = null;
                    }
                }
            }
            else if (side == AnnexSide.East)
            {
                // Entrada Oeste activa en lx = 0..2, ly = 19..20 (hacia el valle sur)
                alwaysFrontLayer.Tiles[0, 13] = null;
                for (int ly = 14; ly <= 21; ly++)
                {
                    for (int lx = 0; lx <= 2; lx++)
                    {
                        frontLayer.Tiles[lx, ly] = null;
                        alwaysFrontLayer.Tiles[lx, ly] = null;
                    }
                    SetTileIfValid(bldLayer, outdoorsSheet, 0, ly, 16);
                    SetPassableIfValid(bldLayer, 0, ly, false);
                    SetTileIfValid(bldLayer, outdoorsSheet, 1, ly, 16);
                    SetPassableIfValid(bldLayer, 1, ly, false);
                }

                SetTileIfValid(frontLayer, outdoorsSheet, 0, 15, 13);
                SetTileIfValid(frontLayer, outdoorsSheet, 1, 15, 14);
                SetTileIfValid(bldLayer, outdoorsSheet, 0, 16, 38);
                SetPassableIfValid(bldLayer, 0, 16, false);
                SetTileIfValid(bldLayer, outdoorsSheet, 1, 16, 39);
                SetPassableIfValid(bldLayer, 1, 16, false);

                SetTileIfValid(bldLayer, outdoorsSheet, 2, 18, 444);
                SetPassableIfValid(bldLayer, 2, 18, false);

                for (int ly = 19; ly <= 20; ly++)
                {
                    for (int lx = 0; lx <= 2; lx++)
                    {
                        SetDirtTile(backLayer, outdoorsSheet, lx, ly, TerrainTileClassifier.DirtPlainTileIndex);
                        backLayer.Tiles[lx, ly]?.Properties.Remove("Passable");
                        bldLayer.Tiles[lx, ly] = null;
                        frontLayer.Tiles[lx, ly] = null;
                        alwaysFrontLayer.Tiles[lx, ly] = null;
                    }
                }

                SetTileIfValid(bldLayer, outdoorsSheet, 2, 21, 369);
                SetPassableIfValid(bldLayer, 2, 21, false);
            }
        }

        private static void ApplyCombatFarmEnclosure(
            Map source,
            Map target,
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer alwaysFrontLayer,
            TileSheet outdoorsSheet,
            AnnexSide side,
            int width,
            int height)
        {
            const int cx = 24;

            // 1. Mapeo base 40x32 desde Maps/Farm_Combat.xnb:
            // ly=0..7: sy=0..7 (Acantilado norte canónico con paso de montaña en sx=40..41)
            // ly=8..27: sy=23..42 (Lago central silvestre 100% canónico y pradera cultivable)
            // ly=28..31: Bosque y seto sur canónico
            for (int lx = 0; lx < width; lx++)
            {
                int sx = cx + lx;
                for (int ly = 0; ly < 28; ly++)
                {
                    int sy = (ly <= 7) ? ly : (23 + (ly - 8));
                    CopySourceCellToTarget(source, target, lx, ly, sx, sy);
                }
            }

            // 2. Mecánica del Paso de Montaña Norte en lx=16..17 (sx=40..41):
            if (side == AnnexSide.South)
            {
                // Paso completamente abierto para entrar caminando hacia el sur
                for (int ly = 0; ly < 8; ly++)
                {
                    bldLayer.Tiles[16, ly] = null;
                    bldLayer.Tiles[17, ly] = null;
                    frontLayer.Tiles[16, ly] = null;
                    frontLayer.Tiles[17, ly] = null;
                    alwaysFrontLayer.Tiles[16, ly] = null;
                    alwaysFrontLayer.Tiles[17, ly] = null;
                    backLayer.Tiles[16, ly]?.Properties.Remove("Passable");
                    backLayer.Tiles[17, ly]?.Properties.Remove("Passable");
                }
                SetDirtTile(backLayer, outdoorsSheet, 16, 0, TerrainTileClassifier.DirtPlainTileIndex);
                SetDirtTile(backLayer, outdoorsSheet, 17, 0, TerrainTileClassifier.DirtPlainTileIndex);
                backLayer.Tiles[16, 0]?.Properties.Remove("Passable");
                backLayer.Tiles[17, 0]?.Properties.Remove("Passable");
            }
            else
            {
                // Variantes Norte y Este: Cerrar el paso de montaña con acantilado de roca marrón sólida
                for (int ly = 0; ly <= 3; ly++)
                {
                    SetTileIfValid(bldLayer, outdoorsSheet, 16, ly, 16);
                    SetPassableIfValid(bldLayer, 16, ly, false);
                    SetTileIfValid(bldLayer, outdoorsSheet, 17, ly, 16);
                    SetPassableIfValid(bldLayer, 17, ly, false);
                }
                SetTileIfValid(bldLayer, outdoorsSheet, 16, 4, 468);
                SetPassableIfValid(bldLayer, 16, 4, false);
                SetTileIfValid(bldLayer, outdoorsSheet, 17, 4, 467);
                SetPassableIfValid(bldLayer, 17, 4, false);

                SetTileIfValid(bldLayer, outdoorsSheet, 16, 5, 493);
                SetPassableIfValid(bldLayer, 16, 5, false);
                SetTileIfValid(bldLayer, outdoorsSheet, 17, 5, 492);
                SetPassableIfValid(bldLayer, 17, 5, false);

                SetTileIfValid(bldLayer, outdoorsSheet, 16, 6, 518);
                SetPassableIfValid(bldLayer, 16, 6, false);
                SetTileIfValid(bldLayer, outdoorsSheet, 17, 6, 517);
                SetPassableIfValid(bldLayer, 17, 6, false);

                SetTileIfValid(bldLayer, outdoorsSheet, 16, 7, 543);
                SetPassableIfValid(bldLayer, 16, 7, false);
                SetTileIfValid(bldLayer, outdoorsSheet, 17, 7, 542);
                SetPassableIfValid(bldLayer, 17, 7, false);

                SetDirtTile(backLayer, outdoorsSheet, 16, 7, 562);
                SetDirtTile(backLayer, outdoorsSheet, 17, 7, 512);

                SetTileIfValid(bldLayer, outdoorsSheet, 15, 4, 468);
                SetPassableIfValid(bldLayer, 15, 4, false);
                SetTileIfValid(bldLayer, outdoorsSheet, 15, 5, 493);
                SetPassableIfValid(bldLayer, 15, 5, false);
                SetTileIfValid(bldLayer, outdoorsSheet, 15, 6, 518);
                SetPassableIfValid(bldLayer, 15, 6, false);
                SetTileIfValid(bldLayer, outdoorsSheet, 15, 7, 543);
                SetPassableIfValid(bldLayer, 15, 7, false);

                SetTileIfValid(bldLayer, outdoorsSheet, 18, 4, 467);
                SetPassableIfValid(bldLayer, 18, 4, false);
                SetTileIfValid(bldLayer, outdoorsSheet, 18, 5, 492);
                SetPassableIfValid(bldLayer, 18, 5, false);
                SetTileIfValid(bldLayer, outdoorsSheet, 18, 6, 517);
                SetPassableIfValid(bldLayer, 18, 6, false);
                SetTileIfValid(bldLayer, outdoorsSheet, 18, 7, 542);
                SetPassableIfValid(bldLayer, 18, 7, false);
            }

            // 3. Límite y entrada oeste (lx=0..3):
            // ly=8..12: Valla vertical de madera en lx=0 con ribete canónico de tierra en lx=1
            for (int ly = 8; ly <= 12; ly++)
            {
                SetGrassTile(backLayer, outdoorsSheet, 0, ly, TerrainTileClassifier.SunnyGrassTileIndex);
                SetDirtTile(backLayer, outdoorsSheet, 1, ly, 201);
                SetTileIfValid(bldLayer, outdoorsSheet, 0, ly, 386);
                SetPassableIfValid(bldLayer, 0, ly, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 0, ly, 361);
            }

            // ly=13..15: Entrada Oeste (side == AnnexSide.East)
            if (side == AnnexSide.East)
            {
                SetGrassTile(backLayer, outdoorsSheet, 0, 13, TerrainTileClassifier.SunnyGrassTileIndex);
                SetDirtTile(backLayer, outdoorsSheet, 1, 13, 228);
                SetTileIfValid(bldLayer, outdoorsSheet, 0, 13, 383);
                SetPassableIfValid(bldLayer, 0, 13, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 0, 12, 358);
                frontLayer.Tiles[0, 13] = null;

                for (int lx = 0; lx <= 2; lx++)
                {
                    for (int ly = 14; ly <= 15; ly++)
                    {
                        bldLayer.Tiles[lx, ly] = null;
                        frontLayer.Tiles[lx, ly] = null;
                        alwaysFrontLayer.Tiles[lx, ly] = null;
                        SetDirtTile(backLayer, outdoorsSheet, lx, ly, TerrainTileClassifier.DirtPlainTileIndex);
                        backLayer.Tiles[lx, ly]?.Properties.Remove("Passable");
                    }
                }
            }
            else
            {
                for (int ly = 13; ly <= 15; ly++)
                {
                    SetGrassTile(backLayer, outdoorsSheet, 0, ly, TerrainTileClassifier.SunnyGrassTileIndex);
                    SetDirtTile(backLayer, outdoorsSheet, 1, ly, 201);
                    SetTileIfValid(bldLayer, outdoorsSheet, 0, ly, 386);
                    SetPassableIfValid(bldLayer, 0, ly, false);
                    SetTileIfValid(frontLayer, outdoorsSheet, 0, ly, 361);
                }
            }

            // ly=16..26: Acantilado rocoso oeste (lx=0..3) desde Farm_Combat sx=20..23, sy=24..34:
            for (int lyDest = 16; lyDest <= 26; lyDest++)
            {
                int sySrc = 24 + (lyDest - 16);
                for (int lxDest = 0; lxDest <= 3; lxDest++)
                {
                    int sxSrc = 20 + lxDest;
                    CopySourceCellToTarget(source, target, lxDest, lyDest, sxSrc, sySrc);
                    SetPassableIfValid(bldLayer, lxDest, lyDest, false);
                }
            }

            // ly=27: Transición de tierra limpia y sombras al pie del acantilado
            SetDirtTile(backLayer, outdoorsSheet, 0, 27, 565);
            SetDirtTile(backLayer, outdoorsSheet, 1, 27, 226);
            SetDirtTile(backLayer, outdoorsSheet, 2, 27, 587);
            SetDirtTile(backLayer, outdoorsSheet, 3, 27, 153);
            SetDirtTile(backLayer, outdoorsSheet, 4, 27, 587);

            // Arbusto 2x2 integrado al pie del acantilado (lx=0..1, ly=27..28):
            SetTileIfValid(frontLayer, outdoorsSheet, 0, 27, 88);
            SetTileIfValid(frontLayer, outdoorsSheet, 1, 27, 89);
            SetTileIfValid(bldLayer, outdoorsSheet, 0, 28, 113);
            SetPassableIfValid(bldLayer, 0, 28, false);
            SetTileIfValid(bldLayer, outdoorsSheet, 1, 28, 114);
            SetPassableIfValid(bldLayer, 1, 28, false);
            SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 0, 27, 1017);
            SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 1, 27, 1042);

            for (int lx = 0; lx <= 3; lx++)
            {
                SetGrassTile(backLayer, outdoorsSheet, lx, 28, TerrainTileClassifier.SunnyGrassTileIndex);
            }

            // 4. Seto y franja sur (ly=28..31):
            for (int lx = 0; lx < width; lx++)
            {
                if (lx >= 5 && lx <= 36)
                {
                    for (int ly = 26; ly <= 27; ly++)
                    {
                        bldLayer.Tiles[lx, ly] = null;
                        frontLayer.Tiles[lx, ly] = null;
                        alwaysFrontLayer.Tiles[lx, ly] = null;
                        int? bIdx = backLayer.Tiles[lx, ly]?.TileIndex;
                        if (!bIdx.HasValue || bIdx.Value >= 1200 || backLayer.Tiles[lx, ly]?.TileSheet != outdoorsSheet)
                        {
                            SetDirtTile(backLayer, outdoorsSheet, lx, ly, TerrainTileClassifier.DirtPlainTileIndex);
                        }
                    }
                }

                SetGrassTile(backLayer, outdoorsSheet, lx, 28, (lx % 2 == 0) ? TerrainTileClassifier.SunnyGrassTileIndex : TerrainTileClassifier.SunnyGrassSoftTileIndex);
                if (lx >= 2)
                {
                    bldLayer.Tiles[lx, 28] = null;
                    frontLayer.Tiles[lx, 28] = null;
                    alwaysFrontLayer.Tiles[lx, 28] = null;
                }

                SetGrassTile(backLayer, outdoorsSheet, lx, 29, TerrainTileClassifier.SunnyGrassTileIndex);
                SetGrassTile(backLayer, outdoorsSheet, lx, 30, TerrainTileClassifier.SunnyGrassTileIndex);
                SetGrassTile(backLayer, outdoorsSheet, lx, 31, TerrainTileClassifier.SunnyGrassTileIndex);

                SetTileIfValid(bldLayer, outdoorsSheet, lx, 29, (lx % 2 == 0) ? 294 : 295);
                SetPassableIfValid(bldLayer, lx, 29, false);
                SetTileIfValid(bldLayer, outdoorsSheet, lx, 30, (lx % 2 == 0) ? 319 : 320);
                SetPassableIfValid(bldLayer, lx, 30, false);
                SetTileIfValid(bldLayer, outdoorsSheet, lx, 31, (lx % 2 == 0) ? 344 : 345);
                SetPassableIfValid(bldLayer, lx, 31, false);

                frontLayer.Tiles[lx, 29] = null;
                frontLayer.Tiles[lx, 30] = null;
                frontLayer.Tiles[lx, 31] = null;

                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 29, (lx % 2 == 0) ? 994 : 1019);
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 30, (lx % 2 == 0) ? 1019 : 1020);
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, lx, 31, (lx % 2 == 0) ? 1044 : 1045);
            }

            // Entrada Sur en lx=16..17 (side == AnnexSide.North):
            if (side == AnnexSide.North)
            {
                for (int lx = 16; lx <= 17; lx++)
                {
                    for (int ly = 28; ly <= 31; ly++)
                    {
                        bldLayer.Tiles[lx, ly] = null;
                        frontLayer.Tiles[lx, ly] = null;
                        alwaysFrontLayer.Tiles[lx, ly] = null;
                        SetDirtTile(backLayer, outdoorsSheet, lx, ly, (lx == 16) ? TerrainTileClassifier.DirtPlainTileIndex : 228);
                        backLayer.Tiles[lx, ly]?.Properties.Remove("Passable");
                    }
                }

                SetTileIfValid(bldLayer, outdoorsSheet, 15, 29, 385);
                SetPassableIfValid(bldLayer, 15, 29, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 15, 28, 360);

                SetTileIfValid(bldLayer, outdoorsSheet, 18, 29, 383);
                SetPassableIfValid(bldLayer, 18, 29, false);
                SetTileIfValid(frontLayer, outdoorsSheet, 18, 28, 358);
            }
            else
            {
                for (int lx = 16; lx <= 17; lx++)
                {
                    SetTileIfValid(bldLayer, outdoorsSheet, lx, 29, (lx == 16) ? 294 : 295);
                    SetPassableIfValid(bldLayer, lx, 29, false);
                    SetTileIfValid(bldLayer, outdoorsSheet, lx, 30, (lx == 16) ? 319 : 320);
                    SetPassableIfValid(bldLayer, lx, 30, false);
                    SetTileIfValid(bldLayer, outdoorsSheet, lx, 31, (lx == 16) ? 344 : 345);
                    SetPassableIfValid(bldLayer, lx, 31, false);
                }
            }

            // 5. Límite Este y esquinas NE / SE (lx=36..39):
            for (int ly = 8; ly <= 31; ly++)
            {
                frontLayer.Tiles[37, ly] = null;
                frontLayer.Tiles[38, ly] = null;
                frontLayer.Tiles[39, ly] = null;
            }

            // Esquina NE: unión entre raíl horizontal y valla vertical
            SetTileIfValid(bldLayer, outdoorsSheet, 37, 8, 384);
            SetPassableIfValid(bldLayer, 37, 8, false);
            SetTileIfValid(frontLayer, outdoorsSheet, 37, 7, 359);

            SetTileIfValid(bldLayer, outdoorsSheet, 38, 8, 385);
            SetPassableIfValid(bldLayer, 38, 8, false);
            SetTileIfValid(frontLayer, outdoorsSheet, 38, 7, 360);

            SetTileIfValid(bldLayer, outdoorsSheet, 39, 7, 16);
            SetPassableIfValid(bldLayer, 39, 7, false);
            SetTileIfValid(bldLayer, outdoorsSheet, 39, 8, 16);
            SetPassableIfValid(bldLayer, 39, 8, false);
            SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 38, 7, 994);
            SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 39, 7, 995);

            // Valla vertical en columna 38 (ly=9..27):
            for (int ly = 9; ly <= 26; ly++)
            {
                SetDirtTile(backLayer, outdoorsSheet, 36, ly, (ly % 2 == 0) ? 153 : 226);
                SetGrassTile(backLayer, outdoorsSheet, 37, ly, TerrainTileClassifier.SunnyGrassTileIndex);
                bldLayer.Tiles[37, ly] = null;

                SetGrassTile(backLayer, outdoorsSheet, 38, ly, TerrainTileClassifier.SunnyGrassTileIndex);
                SetTileIfValid(bldLayer, outdoorsSheet, 38, ly, 386);
                SetPassableIfValid(bldLayer, 38, ly, false);
                frontLayer.Tiles[38, ly] = null;
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 38, ly, (ly % 2 == 0) ? 994 : 1019);

                SetGrassTile(backLayer, outdoorsSheet, 39, ly, TerrainTileClassifier.SunnyGrassSoftTileIndex);
                SetTileIfValid(bldLayer, outdoorsSheet, 39, ly, 16);
                SetPassableIfValid(bldLayer, 39, ly, false);
                SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 39, ly, (ly % 2 == 0) ? 995 : 1020);
            }

            // Poste terminal en (38, 27):
            SetGrassTile(backLayer, outdoorsSheet, 38, 27, TerrainTileClassifier.SunnyGrassTileIndex);
            SetTileIfValid(bldLayer, outdoorsSheet, 38, 27, 383);
            SetPassableIfValid(bldLayer, 38, 27, false);
            SetTileIfValid(frontLayer, outdoorsSheet, 38, 26, 358);

            // Seto exterior continuo en columna 39 hasta conectar con el seto sur:
            SetGrassTile(backLayer, outdoorsSheet, 39, 27, TerrainTileClassifier.SunnyGrassSoftTileIndex);
            SetTileIfValid(bldLayer, outdoorsSheet, 39, 27, 16);
            SetPassableIfValid(bldLayer, 39, 27, false);
            SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 39, 27, 1020);

            SetGrassTile(backLayer, outdoorsSheet, 39, 28, TerrainTileClassifier.SunnyGrassSoftTileIndex);
            SetTileIfValid(bldLayer, outdoorsSheet, 39, 28, 16);
            SetPassableIfValid(bldLayer, 39, 28, false);
            SetTileIfValid(alwaysFrontLayer, outdoorsSheet, 39, 28, 1020);

            // Ribete canónico de esquina redondeada en SE:
            SetDirtTile(backLayer, outdoorsSheet, 36, 27, 228);
            SetGrassTile(backLayer, outdoorsSheet, 36, 28, TerrainTileClassifier.SunnyGrassTileIndex);
            SetGrassTile(backLayer, outdoorsSheet, 37, 27, TerrainTileClassifier.SunnyGrassTileIndex);
            SetGrassTile(backLayer, outdoorsSheet, 37, 28, TerrainTileClassifier.SunnyGrassTileIndex);
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

        private static readonly HashSet<int> WaterTileIndices = new HashSet<int>
        {
            1246, 854, 870, 866, 882, 1249, 1274, 1267, 1299, 1322, 1323, 1248, 1273, 1298, 1324
        };

        private static bool IsWaterTile(Tile? tile)
        {
            if (tile == null) return false;
            if (HasWaterProperty(tile)) return true;
            return WaterTileIndices.Contains(tile.TileIndex);
        }

        private static void SetGrassTile(Layer backLayer, TileSheet outdoorsSheet, int x, int y, int tileIndex = TerrainTileClassifier.SunnyGrassTileIndex)
        {
            if (x >= 0 && x < backLayer.LayerWidth && y >= 0 && y < backLayer.LayerHeight)
            {
                var gt = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, tileIndex);
                gt.Properties["Type"] = "Grass";
                backLayer.Tiles[x, y] = gt;
            }
        }
    }
}
