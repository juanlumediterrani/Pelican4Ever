using System;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;

namespace Pelican4Ever.Framework
{
    public static class MapCloneHelper
    {
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
                            var targetSheet = target.GetTileSheet(t.TileSheet.Id);
                            if (t is StaticTile st)
                            {
                                var newTile = new StaticTile(newLayer, targetSheet, st.BlendMode, st.TileIndex);
                                foreach (var p in st.Properties)
                                {
                                    newTile.Properties[p.Key] = p.Value;
                                }
                                newLayer.Tiles[x, y] = newTile;
                            }
                            else if (t is AnimatedTile at)
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
                                newLayer.Tiles[x, y] = newTile;
                            }
                        }
                    }
                }
                target.AddLayer(newLayer);
            }

            return target;
        }
    }
}
