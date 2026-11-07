using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using xTile;
using xTile.Layers;
using xTile.ObjectModel;
using xTile.Tiles;

namespace MapRenderer;

public static class Program
{
    private const string UnpackedMapsDir = "/home/dr4co/.local/share/Steam/steamapps/common/Stardew Valley/Content (unpacked)/Maps";
    private const string AuditRoot = "/home/dr4co/src/pelican-minimaps/audit/render";

    private static readonly Dictionary<string, Image<Rgba32>> ImageCache = new(StringComparer.OrdinalIgnoreCase);

    public static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "dump")
        {
            DumpTiles(args[1], args.Length > 2 ? args[2] : "North");
            return;
        }

        Directory.CreateDirectory(AuditRoot);
        Directory.CreateDirectory(Path.Combine(AuditRoot, "Maps"));
        Run();
    }

    private static void DumpTiles(string templateId, string side)
    {
        AnnexTemplate template = AnnexTemplateCatalog.GetTemplate(templateId);
        Map source = LoadTmxMap(template.SourceVanillaMap);
        Map gen = MapCloneHelper.CreateAnnexMap(source, template, Enum.Parse<AnnexSide>(side, true));

        Console.WriteLine($"== dump {templateId} {side} ==");
        foreach (Layer layer in gen.Layers)
        {
            Console.WriteLine($"-- layer {layer.Id} ({layer.LayerWidth}x{layer.LayerHeight})");
            foreach (int y in new[] { 0, 1, 2, layer.LayerHeight - 3, layer.LayerHeight - 2, layer.LayerHeight - 1 })
            {
                if (y < 0 || y >= layer.LayerHeight) continue;
                var row = new List<string>();
                for (int x = 0; x < layer.LayerWidth; x++)
                {
                    Tile? t = layer.Tiles[x, y];
                    row.Add(t == null ? "----" : $"{t.TileIndex:D4}");
                }
                Console.WriteLine($"y={y,2}: {string.Join(' ', row)}");
            }
            foreach (int x in new[] { 0, 1, 2, layer.LayerWidth - 3, layer.LayerWidth - 2, layer.LayerWidth - 1 })
            {
                if (x < 0 || x >= layer.LayerWidth) continue;
                Console.WriteLine($"-- columna x={x} rows 0..7: " +
                    string.Join(' ', Enumerable.Range(0, 8).Select(y => layer.Tiles[x, y] == null ? "----" : layer.Tiles[x, y].TileIndex.ToString("D4"))));
            }
        }
    }

    private static void Run()
    {
        var generatedFiles = new List<string>();

        try
        {
            var farm = LoadTmxMap("Maps/Farm");
            var img = RenderMapRegion(farm, 0, 0, farm.Layers[0].LayerWidth, farm.Layers[0].LayerHeight, null);
            SaveImage(img, Path.Combine(AuditRoot, "Maps", "Vanilla_Farm_full.png"));
            generatedFiles.Add("Maps/Vanilla_Farm_full.png");
            Console.WriteLine("Rendered Maps/Vanilla_Farm_full.png");
        }
        catch (Exception ex) { Console.WriteLine("Farm_full FAILED: " + ex); }

        try
        {
            var bus = LoadTmxMap("Maps/BusStop");
            var img = RenderMapRegion(bus, 0, 0, bus.Layers[0].LayerWidth, bus.Layers[0].LayerHeight, null);
            SaveImage(img, Path.Combine(AuditRoot, "Maps", "Vanilla_BusStop_full.png"));
            generatedFiles.Add("Maps/Vanilla_BusStop_full.png");
            Console.WriteLine("Rendered Maps/Vanilla_BusStop_full.png");
        }
        catch (Exception ex) { Console.WriteLine("BusStop_full FAILED: " + ex); }

        try
        {
            var bus = LoadTmxMap("Maps/BusStop");
            var loc = HostPlazaLayout.Find("Pelican4Ever_Bridge_1", 5);
            if (loc != null)
            {
                var bridge = MapCloneHelper.CreateBridgeMap(bus, loc);
                var img = RenderMapRegion(bridge, 0, 0, bridge.Layers[0].LayerWidth, bridge.Layers[0].LayerHeight, null);
                SaveImage(img, Path.Combine(AuditRoot, "Maps", "Bridge_1_full.png"));
                generatedFiles.Add("Maps/Bridge_1_full.png");
                Console.WriteLine("Rendered Maps/Bridge_1_full.png");
            }
        }
        catch (Exception ex) { Console.WriteLine("Bridge FAILED: " + ex); }

        foreach (var kvp in AnnexTemplateCatalog.Templates)
        {
            var template = kvp.Value;
            Map? source = null;
            try { source = LoadTmxMap(template.SourceVanillaMap); }
            catch (Exception ex) { Console.WriteLine($"Source load FAILED {template.SourceVanillaMap}: {ex.Message}"); }

            if (source != null)
            {
                try
                {
                    var cropImg = RenderMapRegion(source, template.SourceCropX, template.SourceCropY, template.Width, template.Height, null);
                    string cropFile = $"Vanilla_{template.Id}_crop.png";
                    SaveImage(cropImg, Path.Combine(AuditRoot, cropFile));
                    generatedFiles.Add(cropFile);
                    Console.WriteLine($"Rendered {cropFile}");
                }
                catch (Exception ex) { Console.WriteLine($"Crop FAILED {template.Id}: {ex.Message}"); }
            }

            foreach (AnnexSide side in new[] { AnnexSide.North, AnnexSide.South, AnnexSide.East })
            {
                try
                {
                    if (source == null) throw new InvalidOperationException("source map not loaded");
                    var gen = MapCloneHelper.CreateAnnexMap(source, template, side);

                    MapCloneHelper.GetEntranceOpeningAndCorridor(side, template.Width, template.Height, out var opening, out var corridor);

                    string mapFile = $"{template.Id}_{side}_map.png";
                    var mapImg = RenderMapRegion(gen, 0, 0, gen.Layers[0].LayerWidth, gen.Layers[0].LayerHeight, null);
                    SaveImage(mapImg, Path.Combine(AuditRoot, mapFile));
                    generatedFiles.Add(mapFile);

                    string collFile = $"{template.Id}_{side}_coll.png";
                    var collImg = RenderMapRegion(gen, 0, 0, gen.Layers[0].LayerWidth, gen.Layers[0].LayerHeight, corridor);
                    SaveImage(collImg, Path.Combine(AuditRoot, collFile));
                    generatedFiles.Add(collFile);

                    string infoFile = $"{template.Id}_{side}_info.txt";
                    WriteInfo(gen, template, side, opening, corridor, Path.Combine(AuditRoot, infoFile));
                    generatedFiles.Add(infoFile);

                    Console.WriteLine($"Rendered {template.Id}_{side}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FAILED {template.Id}_{side}: {ex}");
                }
            }
        }

        var manifest = new
        {
            generatedAt = DateTime.UtcNow.ToString("o"),
            files = generatedFiles.OrderBy(f => f).ToList()
        };
        File.WriteAllText(Path.Combine(AuditRoot, "render_manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        generatedFiles.Add("render_manifest.json");

        File.WriteAllText(Path.Combine(AuditRoot, "TOOL_NOTES.md"), ToolNotes);

        Console.WriteLine("DONE. Files: " + generatedFiles.Count);
    }

    // ---------- TMX loader ----------

    private static Map LoadTmxMap(string assetName)
    {
        string fileName = assetName.Split('/').Last() + ".tmx";
        string path = Path.Combine(UnpackedMapsDir, fileName);
        if (!File.Exists(path)) throw new FileNotFoundException(path);
        return TmxToMap(path, assetName);
    }

    private static Map TmxToMap(string tmxPath, string mapId)
    {
        var doc = XDocument.Load(tmxPath);
        var mapEl = doc.Root!;
        int width = int.Parse(mapEl.Attribute("width")!.Value);
        int height = int.Parse(mapEl.Attribute("height")!.Value);
        int tileW = int.Parse(mapEl.Attribute("tilewidth")!.Value);
        int tileH = int.Parse(mapEl.Attribute("tileheight")!.Value);

        var map = new Map(mapId) { Description = mapId };

        var propsEl = mapEl.Element("properties");
        if (propsEl != null)
        {
            foreach (var p in propsEl.Elements("property"))
            {
                string name = p.Attribute("name")!.Value;
                string value = p.Attribute("value")?.Value ?? p.Value;
                map.Properties[name] = new PropertyValue(value);
            }
        }

        var tilesets = new List<(int firstgid, TileSheet sheet)>();
        foreach (var tsEl in mapEl.Elements("tileset"))
        {
            int firstgid = int.Parse(tsEl.Attribute("firstgid")!.Value);
            string name = tsEl.Attribute("name")!.Value;
            int tsTileW = int.Parse(tsEl.Attribute("tilewidth")!.Value);
            int tsTileH = int.Parse(tsEl.Attribute("tileheight")!.Value);
            int tilecount = int.Parse(tsEl.Attribute("tilecount")!.Value);
            int columns = int.Parse(tsEl.Attribute("columns")!.Value);
            int margin = tsEl.Attribute("margin") != null ? int.Parse(tsEl.Attribute("margin")!.Value) : 0;
            int spacing = tsEl.Attribute("spacing") != null ? int.Parse(tsEl.Attribute("spacing")!.Value) : 0;

            var imageEl = tsEl.Element("image")!;
            string imageName = imageEl.Attribute("source")!.Value;
            if (imageName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                imageName = imageName.Substring(0, imageName.Length - 4);
            string imagePath = Path.Combine(UnpackedMapsDir, imageName + ".png");

            int rows = (tilecount + columns - 1) / columns;
            var sheet = new TileSheet(name, map, imagePath, new xTile.Dimensions.Size(columns, rows), new xTile.Dimensions.Size(tsTileW, tsTileH));
            sheet.Margin = new xTile.Dimensions.Size(margin, margin);
            sheet.Spacing = new xTile.Dimensions.Size(spacing, spacing);

            foreach (var tileEl in tsEl.Elements("tile"))
            {
                int id = int.Parse(tileEl.Attribute("id")!.Value);
                var tprops = tileEl.Element("properties");
                if (tprops == null) continue;
                foreach (var p in tprops.Elements("property"))
                {
                    string pname = p.Attribute("name")!.Value;
                    string pvalue = p.Attribute("value")?.Value ?? p.Value;
                    sheet.TileIndexProperties[id][pname] = new PropertyValue(pvalue);
                }
            }

            map.AddTileSheet(sheet);
            tilesets.Add((firstgid, sheet));
        }

        Func<int, (int firstgid, TileSheet sheet)?> findSheet = gid =>
        {
            (int firstgid, TileSheet sheet) best = (0, null!);
            bool found = false;
            foreach (var ts in tilesets)
            {
                if (gid >= ts.firstgid && ts.firstgid > best.firstgid)
                {
                    best = ts;
                    found = true;
                }
            }
            return found ? best : null;
        };

        foreach (var layerEl in mapEl.Elements("layer"))
        {
            string lname = layerEl.Attribute("name")!.Value;
            int lw = int.Parse(layerEl.Attribute("width")!.Value);
            int lh = int.Parse(layerEl.Attribute("height")!.Value);
            var layer = new Layer(lname, map, new xTile.Dimensions.Size(lw, lh), new xTile.Dimensions.Size(tileW, tileH));

            var lprops = layerEl.Element("properties");
            if (lprops != null)
            {
                foreach (var p in lprops.Elements("property"))
                {
                    string pname = p.Attribute("name")!.Value;
                    string pvalue = p.Attribute("value")?.Value ?? p.Value;
                    layer.Properties[pname] = new PropertyValue(pvalue);
                }
            }

            var dataEl = layerEl.Element("data")!;
            var tokens = dataEl.Value.Split(new[] { ',', '\n', '\r', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int idx = 0;
            for (int y = 0; y < lh; y++)
            {
                for (int x = 0; x < lw; x++)
                {
                    if (idx >= tokens.Length) break;
                    int gid = int.Parse(tokens[idx++]);
                    if (gid == 0) continue;
                    var ts = findSheet(gid);
                    if (ts == null) continue;
                    int tileIndex = gid - ts.Value.firstgid;
                    var tile = new StaticTile(layer, ts.Value.sheet, BlendMode.Alpha, tileIndex);
                    try
                    {
                        var tip = ts.Value.sheet.TileIndexProperties[tileIndex];
                        if (tip != null)
                        {
                            foreach (var pp in tip)
                                tile.Properties[pp.Key] = pp.Value;
                        }
                    }
                    catch { }
                    layer.Tiles[x, y] = tile;
                }
            }

            map.AddLayer(layer);
        }

        return map;
    }

    // ---------- Rendering ----------

    private static readonly string[] LayerOrder = new[] { "Back", "Buildings", "Buildings2", "Front", "Front2", "AlwaysFront", "AlwaysFront2", "AlwaysFront3" };

    private static Image<Rgba32> RenderMapRegion(Map map, int originX, int originY, int regionW, int regionH, HashSet<(int X, int Y)>? corridor)
    {
        int tilePx = 16;
        int imgW = regionW * tilePx;
        int imgH = regionH * tilePx;
        var image = new Image<Rgba32>(imgW, imgH);

        foreach (string layerName in LayerOrder)
        {
            if (layerName == "Paths") continue;
            var layer = map.GetLayer(layerName);
            if (layer == null) continue;
            for (int rx = 0; rx < regionW; rx++)
            {
                for (int ry = 0; ry < regionH; ry++)
                {
                    int sx = originX + rx;
                    int sy = originY + ry;
                    if (sx < 0 || sy < 0 || sx >= layer.LayerWidth || sy >= layer.LayerHeight) continue;
                    var tile = layer.Tiles[sx, sy];
                    if (tile == null) continue;
                    DrawTile(image, tile, rx * tilePx, ry * tilePx, tilePx);
                }
            }
        }

        if (corridor != null)
        {
            var back = map.GetLayer("Back");
            if (back != null)
            {
                for (int rx = 0; rx < regionW; rx++)
                {
                    for (int ry = 0; ry < regionH; ry++)
                    {
                        int sx = originX + rx;
                        int sy = originY + ry;
                        if (sx < 0 || sy < 0 || sx >= back.LayerWidth || sy >= back.LayerHeight) continue;
                        var t = back.Tiles[sx, sy];
                        bool isCorr = corridor.Contains((sx, sy));
                        bool isBlocked = t != null && (HasProp(t, "Passable", "F") || HasProp(t, "Water", "T"));
                        bool isDiggable = t != null && HasProp(t, "Diggable", "T");
                        if (!isCorr && !isBlocked && !isDiggable) continue;
                        Rgba32 color;
                        if (isCorr) color = new Rgba32(255, 165, 0, 120);
                        else if (isBlocked) color = new Rgba32(255, 0, 0, 120);
                        else color = new Rgba32(0, 200, 0, 80);
                        for (int px = rx * tilePx; px < (rx + 1) * tilePx; px++)
                            for (int py = ry * tilePx; py < (ry + 1) * tilePx; py++)
                            {
                                var existing = image[px, py];
                                image[px, py] = AlphaBlend(existing, color);
                            }
                    }
                }
            }
        }

        image.Mutate(ctx => ctx.Resize(regionW * 32, regionH * 32, KnownResamplers.NearestNeighbor));
        return image;
    }

    private static void DrawTile(Image<Rgba32> image, Tile tile, int baseX, int baseY, int tilePx)
    {
        StaticTile st;
        if (tile is AnimatedTile at)
        {
            st = at.TileFrames.Length > 0 ? at.TileFrames[0] : null!;
            if (st == null) return;
        }
        else if (tile is StaticTile s)
        {
            st = s;
        }
        else return;

        var sheet = st.TileSheet;
        if (sheet == null) return;
        var sprite = GetSprite(sheet, st.TileIndex);
        if (sprite == null) return;
        var (pixels, sw, sh) = sprite.Value;

        int drawX = baseX;
        int drawY = baseY + tilePx - sh;

        for (int sy = 0; sy < sh; sy++)
        {
            for (int sx = 0; sx < sw; sx++)
            {
                var c = pixels[sy * sw + sx];
                if (c.A == 0) continue;
                int px = drawX + sx;
                int py = drawY + sy;
                if (px < 0 || py < 0 || px >= image.Width || py >= image.Height) continue;
                image[px, py] = new Rgba32(c.R, c.G, c.B, c.A);
            }
        }
    }

    private static (Rgba32[] pixels, int w, int h)? GetSprite(TileSheet sheet, int index)
    {
        var img = LoadImage(sheet.ImageSource);
        if (img == null) return null;

        int tileW = sheet.TileSize.Width;
        int tileH = sheet.TileSize.Height;
        int marginW = sheet.Margin.Width;
        int marginH = sheet.Margin.Height;
        int spacingW = sheet.Spacing.Width;
        int spacingH = sheet.Spacing.Height;

        int tilesPerRow = sheet.SheetSize.Width;
        if (tilesPerRow <= 0)
            tilesPerRow = Math.Max(1, (img.Width - marginW + spacingW) / (tileW + spacingW));

        int tx = index % tilesPerRow;
        int ty = index / tilesPerRow;

        int px = marginW + tx * (tileW + spacingW);
        int py = marginH + ty * (tileH + spacingH);

        if (px + tileW > img.Width || py + tileH > img.Height) return null;

        var pixels = new Rgba32[tileW * tileH];
        for (int y = 0; y < tileH; y++)
            for (int x = 0; x < tileW; x++)
                pixels[y * tileW + x] = img[px + x, py + y];

        return (pixels, tileW, tileH);
    }

    private static Image<Rgba32>? LoadImage(string path)
    {
        if (ImageCache.TryGetValue(path, out var cached)) return cached;
        if (!File.Exists(path)) return null;
        var img = Image.Load<Rgba32>(path);
        ImageCache[path] = img;
        return img;
    }

    private static Rgba32 AlphaBlend(Rgba32 dst, Rgba32 src)
    {
        float a = src.A / 255f;
        byte r = (byte)(dst.R * (1 - a) + src.R * a);
        byte g = (byte)(dst.G * (1 - a) + src.G * a);
        byte b = (byte)(dst.B * (1 - a) + src.B * a);
        byte alpha = (byte)Math.Min(255, dst.A + src.A);
        return new Rgba32(r, g, b, alpha);
    }

    private static void SaveImage(Image<Rgba32> image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        image.SaveAsPng(path);
    }

    private static void WriteInfo(Map map, AnnexTemplate template, AnnexSide side, HashSet<(int X, int Y)> opening, HashSet<(int X, int Y)> corridor, string path)
    {
        var back = map.GetLayer("Back");
        int w = map.Layers.Count > 0 ? map.Layers[0].LayerWidth : 0;
        int h = map.Layers.Count > 0 ? map.Layers[0].LayerHeight : 0;
        int borderBlocked = 0;
        if (back != null)
        {
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    if (!(x == 0 || y == 0 || x == w - 1 || y == h - 1)) continue;
                    var t = back.Tiles[x, y];
                    if (t != null && (HasProp(t, "Passable", "F") || HasProp(t, "Water", "T"))) borderBlocked++;
                }
        }
        var layers = string.Join(", ", map.Layers.Select(l => l.Id));
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Template: {template.Id}");
        sb.AppendLine($"Side: {side}");
        sb.AppendLine($"Dimensions: {w}x{h}");
        sb.AppendLine($"Border blocked count: {borderBlocked}");
        sb.AppendLine($"Corridor tiles: {corridor.Count}");
        sb.AppendLine($"Opening border tiles: {string.Join(" ", opening)}");
        AnnexWarpPoints pts = AnnexEntrance.For(side, template.Width, template.Height);
        sb.AppendLine($"Entrance exit tile: {pts.ReturnExitTile.X},{pts.ReturnExitTile.Y}");
        sb.AppendLine($"Entrance style: {EntranceStyle(template.Id)}");
        sb.AppendLine($"Layers: {layers}");
        File.WriteAllText(path, sb.ToString());
    }

    private static string EntranceStyle(string templateId)
    {
        return templateId switch
        {
            "Fishing" => "WoodBridge",
            "Foraging" => "ForestTunnel",
            "Mining" => "RockCanyon",
            "Combat" => "WildGrove",
            "Beach" => "SandShorePath",
            "FourCorners" => "DirtGate",
            "Standard" => "DirtGate",
            "Ranching" => "DirtGate",
            _ => "DirtGate"
        };
    }

    private static bool HasProp(Tile t, string key, string value)
    {
        if (t.Properties.TryGetValue(key, out PropertyValue? v))
        {
            if (string.Equals(v?.ToString(), value, StringComparison.OrdinalIgnoreCase)) return true;
        }
        if (t.TileIndexProperties.TryGetValue(key, out PropertyValue? v2))
        {
            if (string.Equals(v2?.ToString(), value, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static readonly string ToolNotes = @"# MapRenderer tool notes

## How to run
```
cd /home/dr4co/src/pelican-minimaps/tools/MapRenderer
dotnet run
```
No game runtime required. Reads unpacked TMX/PNG from Content (unpacked)/Maps.

## Calibration: sprite anchoring rule
Chosen rule: anchor each sprite by its bottom edge — draw sprite with bottom at (tileY+1)*16 and top at (tileY+1)*16 - spriteHeight (extends upward). Back 16x16 tiles fill their cell; taller sprites extend upward. Calibrated against vanilla Farm/BusStop full renders.

## Layer order
Back, Buildings, Buildings2, Front, Front2, AlwaysFront, AlwaysFront2, AlwaysFront3 (Paths skipped).

## Issues
- AnnexEntranceStyle/AnnexMapDesignLogic not available in committed code; entrance style reported via local mapping matching AnnexMapDesignLogicTests expectations.
";
}
