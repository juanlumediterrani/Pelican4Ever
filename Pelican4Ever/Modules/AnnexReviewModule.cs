using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using xTile;
using xTile.Layers;
using xTile.Tiles;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Módulo de herramientas para revisar y validar los minimapas de anexo (TASK-907 / TASK-913):
    /// - <c>p4e_annex_preview</c>: vista previa in situ sin alterar el estado del mundo ni el guardado.
    /// - <c>p4e_annex_screenshot</c>: captura completa de los 24 casos para comparar antes/después.
    /// - <c>p4e_annex_validate</c>: validación automática de tamaño, bordes, transitabilidad y bonificaciones.
    /// Solo activo en el Host cuando <c>EnableTestCommands</c> es true.
    /// </summary>
    public sealed class AnnexReviewModule : IModule
    {
        public const string PreviewLocationName = "Pelican4Ever_AnnexPreview";

        private ModContext context = null!;
        private ChatBox? replyChat;

        public string Name => "AnnexReview";

        public ModuleSide Side => ModuleSide.Host;

        private static readonly (string Name, string Help, Action<AnnexReviewModule, string[]> Handler)[] Commands =
        {
            ("p4e_annex_preview", "Lleva al host a una ubicación temporal para ver un anexo: p4e_annex_preview <tipo> <north|east|south>. Tipos: Standard, Fishing, Foraging, Mining, Combat, FourCorners, Ranching, Beach.", (m, a) => m.OnPreview(a)),
            ("p4e_annex_screenshot", "Exporta captura de mapa completo de anexos: p4e_annex_screenshot [tipo|all]", (m, a) => m.OnScreenshot(a)),
            ("p4e_annex_validate", "Valida los 24 casos de anexos y pasos de residencias: p4e_annex_validate [tipo|all]", (m, a) => m.OnValidate(a)),
            ("p4e_annex_windows", "Analiza y propone las 3 mejores ventanas 40×32 de recorte vanilla: p4e_annex_windows [tipo|all]", (m, a) => m.OnWindows(a)),
        };

        public void Register(ModContext ctx)
        {
            context = ctx;
            foreach (var command in Commands)
                ctx.Helper.ConsoleCommands.Add(command.Name, command.Help, (_, args) => Run(command.Name, args, command.Handler));

            ctx.Helper.Events.GameLoop.SaveLoaded += (_, _) => RegisterChatCommands();
            ctx.Helper.Events.GameLoop.Saving += OnSaving;
        }

        private void RegisterChatCommands()
        {
            try
            {
                foreach (var command in Commands)
                {
                    if (!ChatCommands.Exists(command.Name))
                    {
                        var c = command;
                        ChatCommands.Register(c.Name, (cmd, chat) => RunFromChat(chat, c.Name, cmd.Skip(1).ToArray(), c.Handler), _ => c.Help, null, mainOnly: true, multiplayerOnly: false, cheatsOnly: false);
                    }
                }
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[AnnexReview] Error registrando comandos de chat: {ex.Message}", LogLevel.Warn);
            }
        }

        private void RunFromChat(ChatBox chat, string name, string[] args, Action<AnnexReviewModule, string[]> handler)
        {
            replyChat = chat;
            try { Run(name, args, handler); }
            finally { replyChat = null; }
        }

        private void Run(string name, string[] args, Action<AnnexReviewModule, string[]> handler)
        {
            if (!Context.IsWorldReady)
            {
                Say("Partida no cargada.", LogLevel.Warn);
                return;
            }
            if (!Context.IsMainPlayer)
            {
                Say("Este comando solo puede ejecutarse en el Host.", LogLevel.Warn);
                return;
            }
            if (!context.Config.EnableTestCommands)
            {
                Say($"{name} está desactivado. Pon \"EnableTestCommands\": true en config.json y reinicia.", LogLevel.Warn);
                return;
            }

            try
            {
                handler(this, args);
            }
            catch (Exception ex)
            {
                Say($"[AnnexReview] Error en {name}: {ex.Message}", LogLevel.Error);
                context.Monitor.Log(ex.ToString(), LogLevel.Trace);
            }
        }

        private void Say(string message, LogLevel level)
        {
            context.Monitor.Log(message, level);
            replyChat?.addInfoMessage(message);
        }

        private void OnSaving(object? sender, SavingEventArgs e)
        {
            // Criterio de aceptación TASK-907: Ver un caso no compra nada ni deja ubicaciones en el guardado.
            CleanupPreviewLocation();
        }

        private void CleanupPreviewLocation()
        {
            Farm? previewLoc = Game1.locations?.OfType<Farm>()
                .FirstOrDefault(l => string.Equals(l.NameOrUniqueName, PreviewLocationName, StringComparison.OrdinalIgnoreCase));
            if (previewLoc != null)
            {
                if (Game1.currentLocation == previewLoc)
                {
                    Game1.warpFarmer("Farm", 64, 16, 2);
                }
                Game1.locations?.Remove(previewLoc);
            }
        }

        // ==========================================
        // COMANDOS
        // ==========================================

        private void OnPreview(string[] args)
        {
            if (args.Length < 2)
            {
                Say("Uso: p4e_annex_preview <tipo> <north|east|south>  (ej. p4e_annex_preview Fishing North)", LogLevel.Warn);
                return;
            }

            if (!TryParseTemplateAndSide(args[0], args[1], out AnnexTemplate template, out AnnexSide side))
                return;

            string mapAsset = template.GetMapAssetForSide(side);

            Farm? previewLoc = Game1.locations?.OfType<Farm>()
                .FirstOrDefault(l => string.Equals(l.NameOrUniqueName, PreviewLocationName, StringComparison.OrdinalIgnoreCase));

            if (previewLoc == null)
            {
                previewLoc = new Farm(mapAsset, PreviewLocationName)
                {
                    isAlwaysActive = { Value = true }
                };
                Game1.locations?.Add(previewLoc);
            }
            else
            {
                previewLoc.mapPath.Value = mapAsset;
                previewLoc.reloadMap();
            }

            AnnexWarpPoints pts = AnnexEntrance.For(template, side);
            Game1.warpFarmer(PreviewLocationName, pts.EntryTile.X, pts.EntryTile.Y, pts.FacingDirection);

            Say($"[AnnexPreview] Vista previa de anexo '{template.DisplayName}' ({side}) en {PreviewLocationName}. Entrada en ({pts.EntryTile.X}, {pts.EntryTile.Y}).", LogLevel.Info);
        }

        private void OnScreenshot(string[] args)
        {
            string target = args.Length > 0 ? args[0] : "all";
            List<(AnnexTemplate Template, AnnexSide Side)> cases = new();

            if (string.Equals(target, "all", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var t in AnnexTemplateCatalog.Templates.Values)
                    foreach (AnnexSide side in new[] { AnnexSide.North, AnnexSide.East, AnnexSide.South })
                        cases.Add((t, side));
            }
            else
            {
                if (!AnnexTemplateCatalog.Templates.TryGetValue(target, out AnnexTemplate? template))
                {
                    Say($"Tipo de anexo desconocido '{target}'. Tipos: {string.Join(", ", AnnexTemplateCatalog.Templates.Keys)}", LogLevel.Warn);
                    return;
                }
                foreach (AnnexSide side in new[] { AnnexSide.North, AnnexSide.East, AnnexSide.South })
                    cases.Add((template, side));
            }

            Say($"[AnnexScreenshot] Tomando capturas de {cases.Count} caso(s)...", LogLevel.Info);

            MethodInfo? privateTake = HarmonyLib.AccessTools.Method(typeof(Game1), "takeMapScreenshot", new[] { typeof(GameLocation), typeof(float), typeof(string), typeof(Action) });

            int count = 0;
            foreach (var (t, side) in cases)
            {
                string mapAsset = t.GetMapAssetForSide(side);
                string locName = $"Pelican4Ever_Screenshot_{t.Id}_{side}";

                Farm loc = new Farm(mapAsset, locName);
                string shotName = $"annex_{t.Id.ToLowerInvariant()}_{side.ToString().ToLowerInvariant()}";

                try
                {
                    string? savedPath = null;
                    if (privateTake != null)
                    {
                        savedPath = (string?)privateTake.Invoke(Game1.game1, new object?[] { loc, 1f, shotName, null });
                    }
                    else
                    {
                        GameLocation prevLoc = Game1.currentLocation;
                        Game1.currentLocation = loc;
                        try
                        {
                            savedPath = Game1.game1.takeMapScreenshot(1f, shotName, null);
                        }
                        finally
                        {
                            Game1.currentLocation = prevLoc;
                        }
                    }

                    count++;
                    context.Monitor.Log($"  Captura guardada: {shotName} -> {savedPath ?? "(carpeta de capturas)"}", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    context.Monitor.Log($"  Error en captura de {t.Id}_{side}: {ex.Message}", LogLevel.Error);
                }
            }

            Say($"[AnnexScreenshot] Completadas {count} capturas en la carpeta de capturas de Stardew Valley.", LogLevel.Info);
        }

        private void OnValidate(string[] args)
        {
            string target = args.Length > 0 ? args[0] : "all";
            List<AnnexTemplate> templatesToTest = new();

            if (string.Equals(target, "all", StringComparison.OrdinalIgnoreCase))
            {
                templatesToTest.AddRange(AnnexTemplateCatalog.Templates.Values);
            }
            else
            {
                if (!AnnexTemplateCatalog.Templates.TryGetValue(target, out AnnexTemplate? template))
                {
                    Say($"Tipo de anexo desconocido '{target}'. Tipos: {string.Join(", ", AnnexTemplateCatalog.Templates.Keys)}", LogLevel.Warn);
                    return;
                }
                templatesToTest.Add(template);
            }

            Say($"[AnnexValidate] Validando anexos y residencias para {templatesToTest.Count} tipo(s)...", LogLevel.Info);

            int totalAnnexChecked = 0;
            int passedAnnexCount = 0;
            int totalResidenceChecked = 0;
            int passedResidenceCount = 0;

            // 1. Validar Anexos
            foreach (var t in templatesToTest)
            {
                foreach (AnnexSide side in new[] { AnnexSide.North, AnnexSide.East, AnnexSide.South })
                {
                    totalAnnexChecked++;
                    string asset = t.GetMapAssetForSide(side);
                    Map map;
                    try
                    {
                        map = context.Helper.GameContent.Load<Map>(asset);
                    }
                    catch (Exception ex)
                    {
                        Say($"  [ERROR] No se pudo cargar el asset {asset}: {ex.Message}", LogLevel.Error);
                        continue;
                    }

                    AnnexMapInspectionData inspection = BuildAnnexInspectionData(map, t, side);
                    AnnexValidationReport report = AnnexValidationLogic.ValidateAnnex(inspection);

                    if (report.IsValid)
                    {
                        passedAnnexCount++;
                        context.Monitor.Log($"  [OK] Anexo {t.Id}_{side}: dimensiones 40x32, entrada sellada, {report.ReachableTiles}/{report.TotalTransitableTiles} transitables.", LogLevel.Trace);
                    }
                    else
                    {
                        Say($"  [FALLO] Anexo {t.Id}_{side}:", LogLevel.Warn);
                        foreach (var issue in report.Issues)
                            Say($"    - [{issue.Severity}] {issue.Code}: {issue.Message}", issue.Severity == ValidationSeverity.Error ? LogLevel.Error : LogLevel.Warn);
                    }
                }
            }

            // 2. Validar Residencias (TASK-913)
            foreach (var t in templatesToTest)
            {
                if (!FarmTemplateCatalog.Templates.TryGetValue(t.BaseFarmType, out FarmTemplate? farmTemplate) &&
                    !FarmTemplateCatalog.Templates.TryGetValue(t.Id, out farmTemplate))
                {
                    continue;
                }

                Map resMap;
                try
                {
                    string path = farmTemplate.MapPath.Replace('\\', '/');
                    resMap = context.Helper.GameContent.Load<Map>(path);
                }
                catch (Exception ex)
                {
                    Say($"  [ERROR] No se pudo cargar el mapa de residencia {farmTemplate.MapPath}: {ex.Message}", LogLevel.Error);
                    continue;
                }

                int mw = resMap.Layers[0].LayerWidth;
                int mh = resMap.Layers[0].LayerHeight;
                bool[,] pGrid = BuildPassableGrid(resMap);

                foreach (AnnexSide side in new[] { AnnexSide.North, AnnexSide.East, AnnexSide.South })
                {
                    totalResidenceChecked++;
                    TilePoint arrival = side switch
                    {
                        AnnexSide.North => new TilePoint(farmTemplate.NorthArrivalTileX, farmTemplate.NorthArrivalTileY),
                        AnnexSide.East => new TilePoint(farmTemplate.EastArrivalTileX, farmTemplate.EastArrivalTileY),
                        _ => new TilePoint(farmTemplate.SouthArrivalTileX, farmTemplate.SouthArrivalTileY)
                    };
                    var warps = ResidenceAnnexAccessLogic.WarpTiles(farmTemplate.Id, side);

                    var resInspection = new ResidenceSideInspectionData(farmTemplate.Id, side, mw, mh, arrival, warps, pGrid);
                    var resReport = AnnexValidationLogic.ValidateResidence(resInspection);

                    if (resReport.IsValid)
                    {
                        passedResidenceCount++;
                        context.Monitor.Log($"  [OK] Residencia {farmTemplate.Id}_{side}: llegada {arrival} y {warps.Count} warps transitables con camino libre.", LogLevel.Trace);
                    }
                    else
                    {
                        Say($"  [FALLO] Residencia {farmTemplate.Id}_{side}:", LogLevel.Warn);
                        foreach (var issue in resReport.Issues)
                            Say($"    - [{issue.Severity}] {issue.Code}: {issue.Message}", LogLevel.Error);
                    }
                }
            }

            Say($"[AnnexValidate] Resumen: Anexos {passedAnnexCount}/{totalAnnexChecked} OK. Residencias {passedResidenceCount}/{totalResidenceChecked} OK.",
                (passedAnnexCount == totalAnnexChecked && passedResidenceCount == totalResidenceChecked) ? LogLevel.Info : LogLevel.Warn);
        }

        private void OnWindows(string[] args)
        {
            string target = args.Length > 0 ? args[0] : "all";
            List<AnnexTemplate> templatesToAnalyze = new();

            if (string.Equals(target, "all", StringComparison.OrdinalIgnoreCase))
            {
                templatesToAnalyze.AddRange(AnnexTemplateCatalog.Templates.Values);
            }
            else
            {
                if (!AnnexTemplateCatalog.Templates.TryGetValue(target, out AnnexTemplate? template))
                {
                    Say($"Tipo de anexo desconocido '{target}'. Tipos: {string.Join(", ", AnnexTemplateCatalog.Templates.Keys)}", LogLevel.Warn);
                    return;
                }
                templatesToAnalyze.Add(template);
            }

            Say($"[AnnexWindows] Analizando ventanas de recorte 40×32 para {templatesToAnalyze.Count} tipo(s)...", LogLevel.Info);

            foreach (var t in templatesToAnalyze)
            {
                Map map;
                try
                {
                    string path = t.SourceVanillaMap.Replace('\\', '/');
                    map = context.Helper.GameContent.Load<Map>(path);
                }
                catch (Exception ex)
                {
                    Say($"  [ERROR] No se pudo cargar el mapa vanilla {t.SourceVanillaMap}: {ex.Message}", LogLevel.Error);
                    continue;
                }

                int mw = map.Layers[0].LayerWidth;
                int mh = map.Layers[0].LayerHeight;
                bool[,] passable = new bool[mw, mh];
                bool[,] isWater = new bool[mw, mh];
                bool[,] hasBld = new bool[mw, mh];

                Layer? back = map.GetLayer("Back");
                Layer? bld = map.GetLayer("Buildings");

                for (int x = 0; x < mw; x++)
                {
                    for (int y = 0; y < mh; y++)
                    {
                        passable[x, y] = IsTilePassableSDV(map, x, y);
                        Tile? bt = back?.Tiles[x, y];
                        isWater[x, y] = bt?.TileIndexProperties.ContainsKey("Water") == true
                                     || (bt != null && bt.Properties.ContainsKey("Water"));
                        hasBld[x, y] = bld?.Tiles[x, y] != null;
                    }
                }

                var farmTemplate = FarmTemplateCatalog.GetTemplate(t.Id);
                TilePoint houseTile = new TilePoint(farmTemplate.CabinTileX, farmTemplate.CabinTileY);
                VanillaExitPassage? vanillaExit = VanillaFarmExitTable.Table.TryGetValue(t.Id, out var pe) ? pe : null;

                foreach (AnnexSide side in new[] { AnnexSide.North, AnnexSide.East, AnnexSide.South })
                {
                    var scoringInput = new MapScoringInput(
                        TemplateId: t.Id,
                        Side: side,
                        MapWidth: mw,
                        MapHeight: mh,
                        IsPassable: passable,
                        IsWater: isWater,
                        HasBuildingLayerTile: hasBld,
                        OreArea: t.Behavior.MountainOreArea,
                        HasForestForage: t.Behavior.ForestForage,
                        VanillaHouseTile: houseTile,
                        VanillaExit: vanillaExit
                    );

                    var top3 = AnnexWindowScoringLogic.FindTopWindows(scoringInput, topN: 3);
                    if (top3.Count == 0) continue;

                    var best = top3[0];
                    Say($"[Windows] {t.Id}_{side} -> #1 Ventana ({best.WindowX:D2}, {best.WindowY:D2}), Entrada ({best.EntranceTile.X:D2}, {best.EntranceTile.Y:D2}), Pts={best.Score} | {best.Details}", LogLevel.Info);
                    for (int i = 1; i < top3.Count; i++)
                    {
                        var alt = top3[i];
                        context.Monitor.Log($"    #{i + 1} ({alt.WindowX:D2}, {alt.WindowY:D2}) Pts={alt.Score} | {alt.Details}", LogLevel.Trace);
                    }
                }
            }

            Say("[AnnexWindows] Análisis completado. Consulta el registro de SMAPI para ver candidatos alternativos.", LogLevel.Info);
        }

        // ==========================================
        // HELPERS DE INSPECCIÓN DE MAPAS
        // ==========================================

        private static bool TryParseTemplateAndSide(string typeArg, string sideArg, out AnnexTemplate template, out AnnexSide side)
        {
            template = AnnexTemplateCatalog.Templates["Standard"];
            side = AnnexSide.North;

            if (!AnnexTemplateCatalog.Templates.TryGetValue(typeArg, out AnnexTemplate? foundTemplate))
            {
                return false;
            }

            if (!Enum.TryParse(sideArg, ignoreCase: true, out AnnexSide foundSide))
            {
                return false;
            }

            template = foundTemplate;
            side = foundSide;
            return true;
        }

        public static bool IsTilePassableSDV(Map map, int x, int y)
        {
            Layer? back = map.GetLayer("Back");
            Layer? bld = map.GetLayer("Buildings");

            Tile? backTile = back?.Tiles[x, y];
            if (backTile == null) return false;
            if (backTile.TileIndexProperties.ContainsKey("Passable")) return false;
            if (backTile.Properties.TryGetValue("Passable", out var bp) && bp == "F") return false;

            Tile? bldTile = bld?.Tiles[x, y];
            if (bldTile != null)
            {
                if (bldTile.Properties.TryGetValue("Passable", out var bp2))
                {
                    if (bp2 == "F") return false;
                    if (bp2 == "T") return true;
                }
                if (bldTile.TileIndexProperties.ContainsKey("Shadow"))
                {
                    // Shadow no colisiona
                }
                else if (!bldTile.TileIndexProperties.ContainsKey("Passable"))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool[,] BuildPassableGrid(Map map)
        {
            int w = map.Layers[0].LayerWidth;
            int h = map.Layers[0].LayerHeight;
            bool[,] grid = new bool[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    grid[x, y] = IsTilePassableSDV(map, x, y);
            return grid;
        }

        public static AnnexMapInspectionData BuildAnnexInspectionData(Map map, AnnexTemplate template, AnnexSide side)
        {
            int w = map.Layers[0].LayerWidth;
            int h = map.Layers[0].LayerHeight;
            bool[,] backPresent = new bool[w, h];
            bool[,] passable = new bool[w, h];
            bool[,] isWater = new bool[w, h];

            Layer? back = map.GetLayer("Back");

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    Tile? bt = back?.Tiles[x, y];
                    backPresent[x, y] = bt != null;
                    passable[x, y] = IsTilePassableSDV(map, x, y);
                    isWater[x, y] = bt?.TileIndexProperties.ContainsKey("Water") == true
                                 || (bt != null && bt.Properties.ContainsKey("Water"));
                }
            }

            TilePoint entry = AnnexEntrance.For(template, side).EntryTile;

            TileArea? oreArea = null;
            if (map.Properties.TryGetValue("SpawnMountainFarmOreRect", out var oreProp)
                && TileArea.TryParse(oreProp?.ToString(), out TileArea parsedOre))
            {
                oreArea = parsedOre;
            }
            else if (template.Behavior.MountainOreArea is { } defOre)
            {
                oreArea = defOre;
            }

            TileArea? forageArea = template.Behavior.ForestForage ? new TileArea(0, 0, 18, h) : null;

            var missingSheets = new List<string>();
            var sheetSet = new HashSet<string>(map.TileSheets.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);
            foreach (Layer layer in map.Layers)
            {
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        Tile? t = layer.Tiles[x, y];
                        if (t != null && !sheetSet.Contains(t.TileSheet.Id))
                        {
                            if (!missingSheets.Contains(t.TileSheet.Id))
                                missingSheets.Add(t.TileSheet.Id);
                        }
                    }
                }
            }

            var vanillaLeftovers = new List<string>();
            if (map.Properties.ContainsKey("Warp"))
                vanillaLeftovers.Add($"Warp en mapa: {map.Properties["Warp"]}");

            foreach (Layer layer in map.Layers)
            {
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        Tile? t = layer.Tiles[x, y];
                        if (t != null)
                        {
                            if (t.Properties.TryGetValue("TouchAction", out var ta) && ta.ToString().StartsWith("Warp", StringComparison.OrdinalIgnoreCase))
                                vanillaLeftovers.Add($"TouchAction en ({x},{y}): {ta}");
                            if (t.Properties.TryGetValue("Action", out var act) && act.ToString().StartsWith("Warp", StringComparison.OrdinalIgnoreCase))
                                vanillaLeftovers.Add($"Action en ({x},{y}): {act}");
                        }
                    }
                }
            }

            return new AnnexMapInspectionData(
                TemplateId: template.Id,
                Side: side,
                Width: w,
                Height: h,
                EntryTile: entry,
                BackTilePresent: backPresent,
                IsPassable: passable,
                IsWater: isWater,
                MountainOreArea: oreArea,
                ForestForageArea: forageArea,
                MissingTileSheets: missingSheets,
                RemainingVanillaWarpsOrActions: vanillaLeftovers
            );
        }
    }
}
