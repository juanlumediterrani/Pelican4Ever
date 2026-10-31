using System;
using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Core.World;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;

namespace Pelican4Ever.Framework
{
    public static class MapCloneHelper
    {
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

                    if (bldLayer.Tiles[x, y] == null && mainSheet != null)
                    {
                        var barrier = new StaticTile(bldLayer, mainSheet, BlendMode.Alpha, 0);
                        barrier.Properties["Passable"] = "F";
                        bldLayer.Tiles[x, y] = barrier;
                    }
                    else if (bldLayer.Tiles[x, y] != null)
                    {
                        bldLayer.Tiles[x, y].Properties["Passable"] = "F";
                    }

                    if (backLayer.Tiles[x, y] != null)
                    {
                        backLayer.Tiles[x, y].Properties["Passable"] = "F";
                    }
                }
            }

            // 7. Despejar y pavimentar la entrada y su corredor de acceso hacia el interior
            foreach (var (cx, cy) in corridorTiles)
            {
                if (cx < 0 || cx >= width || cy < 0 || cy >= height)
                    continue;

                bldLayer.Tiles[cx, cy] = null;
                frontLayer.Tiles[cx, cy] = null;
                alwaysFrontLayer.Tiles[cx, cy] = null;

                if (mainSheet != null)
                {
                    var passGround = new StaticTile(backLayer, mainSheet, BlendMode.Alpha, 587);
                    passGround.Properties["Type"] = "Dirt";
                    backLayer.Tiles[cx, cy] = passGround;
                }
                else if (backLayer.Tiles[cx, cy] != null)
                {
                    ClearBlockingAndWaterProperties(backLayer.Tiles[cx, cy]);
                    backLayer.Tiles[cx, cy].Properties["Type"] = "Dirt";
                }
            }

            // 8. Garantizar zonas de bonificación válidas dentro del área 40×32 (TASK-902 Criterio 2)
            EnsureAnnexBonusZones(template, width, height, backLayer, bldLayer, frontLayer, pathsLayer, mainSheet, corridorTiles);

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

            // D) Remanso Fluvial: garantizar poza/canal de agua pescable y orilla transitable
            if (string.Equals(template.Id, "Fishing", StringComparison.OrdinalIgnoreCase) && mainSheet != null)
            {
                for (int x = 12; x <= 17; x++)
                {
                    for (int y = 11; y <= 15; y++)
                    {
                        if (corridorTiles.Contains((x, y)))
                            continue;

                        bldLayer.Tiles[x, y] = null;
                        if (backLayer.Tiles[x, y] == null)
                        {
                            backLayer.Tiles[x, y] = new StaticTile(backLayer, mainSheet, BlendMode.Alpha, 587);
                        }
                        backLayer.Tiles[x, y].Properties["Water"] = "T";
                        backLayer.Tiles[x, y].Properties.Remove("NoFishing");
                    }
                }

                // Orilla norte de la poza despejada para pescar
                for (int x = 12; x <= 17; x++)
                {
                    int shoreY = 10;
                    bldLayer.Tiles[x, shoreY] = null;
                    frontLayer.Tiles[x, shoreY] = null;
                    if (backLayer.Tiles[x, shoreY] == null || HasWaterProperty(backLayer.Tiles[x, shoreY]))
                    {
                        backLayer.Tiles[x, shoreY] = new StaticTile(backLayer, mainSheet, BlendMode.Alpha, 587);
                    }
                    ClearBlockingAndWaterProperties(backLayer.Tiles[x, shoreY]);
                    backLayer.Tiles[x, shoreY].Properties["Type"] = "Dirt";
                }
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
            tile.TileIndexProperties.Remove("Passable");
            tile.TileIndexProperties.Remove("Water");
        }
    }
}
