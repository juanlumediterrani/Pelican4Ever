using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Extensions;
using StardewValley.GameData.Locations;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Gestiona los huecos de granja (Pelican4Ever_Farm_01..16), selección de plantillas de mapa (TASK-602),
    /// culling dinámico de red para granjas inactivas (TASK-603) y las puertas de acceso residenciales (TASK-601).
    /// </summary>
    public sealed class FarmSlotModule : IModule
    {
        private static ModContext? context;
        private static bool welcomeNotified;
        private static bool chatCommandsRegistered;

        /// <summary>Slot cuya residencia no estaba cargada en el cliente al despertar; se viaja a ella en cuanto se pueda.</summary>
        private static string? pendingResidenceWakeSlotId;

        /// <summary>Huecos y tipos con los que se construyó la última versión de Data/Locations.</summary>
        private static string slotDataSignature = string.Empty;
        public static List<DoorModel> RegisteredDoors { get; private set; } = new();

        public string Name => "FarmSlot";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            // Cargar catálogo de puertas desde assets/doors.json
            LoadDoors();

            // Eventos SMAPI generales
            ctx.Helper.Events.Content.AssetRequested += OnAssetRequested;
            ctx.Helper.Events.Input.ButtonPressed += OnButtonPressed;
            ctx.Helper.Events.Display.RenderedWorld += OnRenderedWorld;
            ctx.Helper.Events.Player.Warped += OnPlayerWarped;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.GameLoop.DayStarted += OnDayStarted;
            ctx.Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            ctx.Helper.Events.GameLoop.UpdateTicked += OnPendingResidenceWakeTicked;

            // Comandos de consola y chat
            ctx.Helper.ConsoleCommands.Add("p4e_doors", "Muestra el estado y ubicación de las 16 residencias.", OnConsoleDoors);
            ctx.Helper.Events.GameLoop.GameLaunched += (s, e) =>
            {
                RegisterChatCommands();
                ApplyNitvPatches(ctx.Harmony);
            };

            // Mensajes y sincronización multijugador (en host y peones)
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            ctx.Helper.Events.Multiplayer.PeerContextReceived += OnPeerContextReceived;

            ApplyHarmonyPatches(ctx.Harmony);
        }

        private void LoadDoors()
        {
            if (context == null) return;

            string doorPath = Path.Combine(context.Helper.DirectoryPath, "assets", "doors.json");
            if (File.Exists(doorPath))
            {
                try
                {
                    var rawDoors = context.Helper.Data.ReadJsonFile<List<DoorModel>>("assets/doors.json") ?? new();
                    var validDoors = new List<DoorModel>();
                    var seenDoorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var seenSlotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var door in rawDoors)
                    {
                        var spec = door.ToDoorSpec();
                        var errors = new List<string>(DoorCatalogValidator.ValidateOne(spec, 16));

                        if (!string.IsNullOrWhiteSpace(door.DoorId) && !seenDoorIds.Add(door.DoorId))
                            errors.Add($"DoorId duplicado: '{door.DoorId}'.");

                        if (!string.IsNullOrWhiteSpace(door.DefaultSlotId) && !seenSlotIds.Add(door.DefaultSlotId))
                            errors.Add($"DefaultSlotId duplicado: '{door.DefaultSlotId}'.");

                        if (errors.Count > 0)
                        {
                            foreach (string err in errors)
                            {
                                context.Monitor.Log($"[FarmSlotModule] Error en puerta '{door.DoorId}': {err}", LogLevel.Error);
                            }
                        }
                        else
                        {
                            validDoors.Add(door);
                        }
                    }

                    RegisteredDoors = validDoors;
                    context.Monitor.Log($"[FarmSlotModule] {RegisteredDoors.Count} residencias/puertas válidas cargadas desde assets/doors.json.", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    context.Monitor.Log($"[FarmSlotModule] Error leyendo assets/doors.json: {ex}", LogLevel.Error);
                }
            }
            else
            {
                context.Monitor.Log($"[FarmSlotModule] No se encontró {doorPath}.", LogLevel.Warn);
            }
        }

        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            // Inyectar huecos de granja en Data/Locations con su plantilla asignada
            if (e.NameWithoutLocale.IsEquivalentTo("Data/Locations"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, LocationData>().Data;

                    // La Granja Host (Plaza Central) utiliza un mapa propio para aislar sus aperturas perimetrales
                    if (data.TryGetValue("Farm", out var hostFarmData))
                    {
                        if (hostFarmData.CreateOnLoad != null)
                        {
                            hostFarmData.CreateOnLoad.MapPath = "Maps/Pelican4Ever_HostFarm";
                        }
                        else
                        {
                            hostFarmData.CreateOnLoad = new CreateLocationData
                            {
                                MapPath = "Maps/Pelican4Ever_HostFarm",
                                Type = "StardewValley.Farm, Stardew Valley"
                            };
                        }
                        hostFarmData.CustomFields ??= new Dictionary<string, string>();
                        FarmTemplateCatalog.ApplyNitvCustomFields(hostFarmData.CustomFields, "Standard");
                    }

                    // TASK-1201: Registrar declarativamente los puentes activos en Data/Locations
                    int slotCount = context?.Config.FarmSlotCount ?? 16;
                    int bridges = HostPlazaLayout.BridgeCount(slotCount);
                    for (int n = 1; n <= bridges; n++)
                    {
                        string name = HostPlazaLayout.BridgeName(n);
                        if (!data.TryGetValue(name, out var bridgeData) || bridgeData == null)
                        {
                            bridgeData = new LocationData();
                            data[name] = bridgeData;
                        }
                        bridgeData.DisplayName = $"Puente {n}";
                        bridgeData.CanPlantHere = false;
                        bridgeData.CreateOnLoad ??= new CreateLocationData();
                        bridgeData.CreateOnLoad.MapPath = $"Maps/{name}";
                        bridgeData.CreateOnLoad.Type = "StardewValley.GameLocation, Stardew Valley";
                        bridgeData.CreateOnLoad.AlwaysActive = true;
                        bridgeData.CustomFields ??= new Dictionary<string, string>();
                        FarmTemplateCatalog.ApplyNitvCustomFields(bridgeData.CustomFields, "Standard");
                    }
                    for (int n = bridges + 1; n <= HostPlazaLayout.MaxBridges; n++)
                    {
                        string name = HostPlazaLayout.BridgeName(n);
                        data.Remove(name);
                    }

                    int count = slotCount;

                    for (int i = 1; i <= count; i++)
                    {
                        string slotId = $"Pelican4Ever_Farm_{i:D2}";
                        string farmType = "Standard";
                        if (context != null && context.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == slotId) is { } assignment && !string.IsNullOrEmpty(assignment.FarmType))
                        {
                            farmType = assignment.FarmType;
                        }
                        var template = FarmTemplateCatalog.GetTemplate(farmType);

                        if (!data.TryGetValue(slotId, out LocationData? slotData) || slotData == null)
                        {
                            slotData = new LocationData();
                            data[slotId] = slotData;
                        }

                        slotData.DisplayName ??= $"Granja {i} ({template.DisplayName})";
                        slotData.CanPlantHere = true;
                        if (slotData.CreateOnLoad == null)
                        {
                            slotData.CreateOnLoad = new CreateLocationData
                            {
                                MapPath = template.MapPath,
                                Type = "StardewValley.Farm, Stardew Valley",
                                AlwaysActive = true // Gestionado dinámicamente mediante LocationCullingLogic
                            };
                        }
                        else
                        {
                            slotData.CreateOnLoad.MapPath = template.MapPath;
                            slotData.CreateOnLoad.Type = "StardewValley.Farm, Stardew Valley";
                            slotData.CreateOnLoad.AlwaysActive = true;
                        }

                        // Peces, forraje y excavaciones del tipo elegido. El juego solo aplica "Farm_<tipo>" a la
                        // granja "Farm"; sin copiarlo, una residencia solo pesca lo de "Default" (basura).
                        if (data.TryGetValue(template.LocationDataKey, out LocationData? typeData)
                            || data.TryGetValue("Farm_Standard", out typeData))
                        {
                            slotData.Fish ??= typeData.Fish;
                            slotData.FishAreas ??= typeData.FishAreas;
                            slotData.Forage ??= typeData.Forage;
                            slotData.ArtifactSpots ??= typeData.ArtifactSpots;
                            slotData.MinDailyWeeds = typeData.MinDailyWeeds;
                            slotData.MaxDailyWeeds = typeData.MaxDailyWeeds;
                            slotData.FirstDayWeedMultiplier = typeData.FirstDayWeedMultiplier;
                            slotData.MinDailyForageSpawn = typeData.MinDailyForageSpawn;
                            slotData.MaxDailyForageSpawn = typeData.MaxDailyForageSpawn;
                            slotData.MaxSpawnedForageAtOnce = typeData.MaxSpawnedForageAtOnce;
                            slotData.ChanceForClay = typeData.ChanceForClay;
                            if (typeData.CustomFields != null)
                            {
                                slotData.CustomFields ??= new Dictionary<string, string>();
                                foreach (var kvp in typeData.CustomFields)
                                {
                                    slotData.CustomFields.TryAdd(kvp.Key, kvp.Value);
                                }
                            }
                            slotData.CustomFields ??= new Dictionary<string, string>();
                            FarmTemplateCatalog.ApplyNitvCustomFields(slotData.CustomFields, template.Id);
                        }
                        else
                        {
                            context?.Monitor.Log($"[FarmSlotModule] Data/Locations no tiene '{template.LocationDataKey}' ni 'Farm_Standard'; {slotId} usará solo los peces por defecto.", LogLevel.Warn);
                            slotData.CustomFields ??= new Dictionary<string, string>();
                            FarmTemplateCatalog.ApplyNitvCustomFields(slotData.CustomFields, template.Id);
                        }

                        // TASK-Cave: Registrar la cueva privada instanciada para la residencia (Pelican4Ever_Farm_XX_Cave)
                        string caveId = $"{slotId}_Cave";
                        if (!data.TryGetValue(caveId, out LocationData? caveData) || caveData == null)
                        {
                            caveData = new LocationData();
                            data[caveId] = caveData;
                        }
                        caveData.DisplayName ??= $"Cueva Parcela {i:D2}";
                        caveData.CustomFields ??= new Dictionary<string, string>();
                        caveData.CustomFields["Nature.NIV.Underground"] = "true";
                        if (caveData.CreateOnLoad == null)
                        {
                            caveData.CreateOnLoad = new CreateLocationData
                            {
                                MapPath = "Maps/FarmCave",
                                Type = "StardewValley.Locations.FarmCave, Stardew Valley",
                                AlwaysActive = true
                            };
                        }
                        else
                        {
                            caveData.CreateOnLoad.MapPath = "Maps/FarmCave";
                            caveData.CreateOnLoad.Type = "StardewValley.Locations.FarmCave, Stardew Valley";
                            caveData.CreateOnLoad.AlwaysActive = true;
                        }

                        // TASK-Cellar: Registrar el sótano privado instanciado para la residencia (Pelican4Ever_Farm_XX_Cellar)
                        string cellarId = ResidenceUpgradeLogic.GetSlotCellarName(slotId);
                        if (!data.TryGetValue(cellarId, out LocationData? cellarData) || cellarData == null)
                        {
                            cellarData = new LocationData();
                            data[cellarId] = cellarData;
                        }
                        cellarData.DisplayName ??= $"Bodega Parcela {i:D2}";
                        if (cellarData.CreateOnLoad == null)
                        {
                            cellarData.CreateOnLoad = new CreateLocationData
                            {
                                MapPath = "Maps/Cellar",
                                Type = "StardewValley.Locations.Cellar, Stardew Valley",
                                AlwaysActive = true
                            };
                        }
                        else
                        {
                            cellarData.CreateOnLoad.MapPath = "Maps/Cellar";
                            cellarData.CreateOnLoad.Type = "StardewValley.Locations.Cellar, Stardew Valley";
                            cellarData.CreateOnLoad.AlwaysActive = true;
                        }

                        // TASK-Greenhouse: Registrar el invernadero privado instanciado para la residencia (Pelican4Ever_Farm_XX_Greenhouse)
                        string greenhouseId = ResidenceUpgradeLogic.GetSlotGreenhouseName(slotId);
                        if (!data.TryGetValue(greenhouseId, out LocationData? ghData) || ghData == null)
                        {
                            ghData = new LocationData();
                            data[greenhouseId] = ghData;
                        }
                        ghData.DisplayName ??= $"Invernadero Parcela {i:D2}";
                        ghData.CanPlantHere = true;
                        if (ghData.CreateOnLoad == null)
                        {
                            ghData.CreateOnLoad = new CreateLocationData
                            {
                                MapPath = "Maps/Greenhouse",
                                AlwaysActive = true
                            };
                        }
                        else
                        {
                            ghData.CreateOnLoad.MapPath = "Maps/Greenhouse";
                            ghData.CreateOnLoad.AlwaysActive = true;
                        }
                    }

                    // TASK-903: Inyectar ubicaciones de anexos adquiridos (Pelican4Ever_Annex_<NN>_<Side>) en Data/Locations
                    if (context != null && context.WorldState.Annexes != null)
                    {
                        foreach (AnnexAssignment annex in context.WorldState.Annexes.Values)
                        {
                            if (string.IsNullOrWhiteSpace(annex.AnnexId))
                                continue;

                            AnnexTemplate annexTemplate = AnnexTemplateCatalog.GetTemplate(annex.TemplateId);
                            string annexMapPath = annexTemplate.GetMapAssetForSide(annex.Side);

                            if (!data.TryGetValue(annex.AnnexId, out LocationData? annexData) || annexData == null)
                            {
                                annexData = new LocationData();
                                data[annex.AnnexId] = annexData;
                            }

                            annexData.DisplayName ??= $"Anexo {annex.Side} ({annexTemplate.DisplayName})";
                            annexData.CanPlantHere = true;
                            if (annexData.CreateOnLoad == null)
                            {
                                annexData.CreateOnLoad = new CreateLocationData
                                {
                                    MapPath = annexMapPath,
                                    Type = "StardewValley.Farm, Stardew Valley",
                                    AlwaysActive = true
                                };
                            }
                            else
                            {
                                annexData.CreateOnLoad.MapPath = annexMapPath;
                                annexData.CreateOnLoad.Type = "StardewValley.Farm, Stardew Valley";
                                annexData.CreateOnLoad.AlwaysActive = true;
                            }

                            if (data.TryGetValue(annexTemplate.LocationDataKey, out LocationData? typeData)
                                || data.TryGetValue("Farm_Standard", out typeData))
                            {
                                annexData.Fish = CloneAnnexFishData(typeData.Fish);
                                annexData.FishAreas = CloneAnnexFishAreaData(typeData.FishAreas);
                                annexData.Forage = typeData.Forage;
                                annexData.ArtifactSpots = typeData.ArtifactSpots;
                                annexData.MinDailyWeeds = typeData.MinDailyWeeds;
                                annexData.MaxDailyWeeds = typeData.MaxDailyWeeds;
                                annexData.FirstDayWeedMultiplier = typeData.FirstDayWeedMultiplier;
                                annexData.MinDailyForageSpawn = typeData.MinDailyForageSpawn;
                                annexData.MaxDailyForageSpawn = typeData.MaxDailyForageSpawn;
                                annexData.MaxSpawnedForageAtOnce = typeData.MaxSpawnedForageAtOnce;
                                annexData.ChanceForClay = typeData.ChanceForClay;
                                if (typeData.CustomFields != null)
                                {
                                    annexData.CustomFields = new Dictionary<string, string>(typeData.CustomFields);
                                }
                                annexData.CustomFields ??= new Dictionary<string, string>();
                                FarmTemplateCatalog.ApplyNitvCustomFields(annexData.CustomFields, annexTemplate.Id);
                            }
                            else
                            {
                                annexData.CustomFields ??= new Dictionary<string, string>();
                                FarmTemplateCatalog.ApplyNitvCustomFields(annexData.CustomFields, annexTemplate.Id);
                            }

                            data[annex.AnnexId] = annexData;
                        }
                    }
                });
            }
            else if (AnnexTemplateCatalog.TryParseAnnexMapAsset(e.NameWithoutLocale.BaseName, out AnnexTemplate annexTemplate, out AnnexSide annexSide))
            {
                // TASK-902: Genera el mapa temático de un cuarto de tamaño (40×32) para el anexo y su lado de entrada
                e.LoadFrom(() =>
                {
                    var baseMap = context?.Helper.GameContent.Load<xTile.Map>(annexTemplate.SourceVanillaMap)
                               ?? context?.Helper.GameContent.Load<xTile.Map>("Maps/Farm");
                    if (baseMap == null)
                    {
                        return new xTile.Map($"Pelican4Ever_Annex_{annexTemplate.Id}_{annexSide}");
                    }

                    return MapCloneHelper.CreateAnnexMap(baseMap, annexTemplate, annexSide);
                }, AssetLoadPriority.Medium);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo("Maps/Pelican4Ever_HostFarm"))
            {
                // Genera el mapa exclusivo para la Granja Host (Plaza Central), clonando Maps/Farm y abriendo sendas de 2 tiles
                e.LoadFrom(() =>
                {
                    var baseMap = context?.Helper.GameContent.Load<xTile.Map>("Maps/Farm");
                    if (baseMap == null)
                    {
                        return new xTile.Map("Pelican4Ever_HostFarm");
                    }

                    var hostMap = MapCloneHelper.CloneMap(baseMap);
                    var bldLayer = hostMap.GetLayer("Buildings");
                    var frontLayer = hostMap.GetLayer("Front");
                    var alwaysFrontLayer = hostMap.GetLayer("AlwaysFront");
                    var backLayer = hostMap.GetLayer("Back");

                    // Localizar una baldosa de suelo válida en el interior de la granja para rellenar el suelo
                    xTile.Tiles.Tile? sampleTile = null;
                    if (backLayer != null)
                    {
                        sampleTile = backLayer.Tiles[40, 30] ?? backLayer.Tiles[64, 15] ?? backLayer.Tiles[20, 20];
                        if (sampleTile == null)
                        {
                            for (int sy = 5; sy < backLayer.LayerHeight - 5 && sampleTile == null; sy++)
                            {
                                for (int sx = 5; sx < backLayer.LayerWidth - 5; sx++)
                                {
                                    if (backLayer.Tiles[sx, sy] != null)
                                    {
                                        sampleTile = backLayer.Tiles[sx, sy];
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    var townMap = context?.Helper.GameContent.Load<xTile.Map>("Maps/Town");
                    if (townMap != null)
                        AddMailboxAssets(hostMap, townMap);
                    AddPathAssets(hostMap);

                    foreach (var door in RegisteredDoors)
                    {
                        if (string.Equals(door.LocationName, "Farm", StringComparison.OrdinalIgnoreCase))
                        {
                            ClearDoorAreaOnMap(bldLayer, frontLayer, alwaysFrontLayer, backLayer, door, sampleTile);
                        }
                    }

                    // TASK-1203: Tallar el paso oeste en Maps/Pelican4Ever_HostFarm si hay puentes activos
                    int slotCount = context?.Config.FarmSlotCount ?? 16;
                    var farmPlaza = HostPlazaLayout.Find(HostPlazaLayout.HostFarm, slotCount);
                    if (farmPlaza?.West is { } westPassage)
                    {
                        var sheetOutdoors = MapCloneHelper.GetOutdoorsTileSheet(hostMap) ?? sampleTile?.TileSheet;
                        ApplyFarmWestPassageArtisanFinishes(hostMap, westPassage, sheetOutdoors);

                        // Añadir warp hacia Pelican4Ever_Bridge_1 conservando warps vanilla
                        string existingWarps = hostMap.Properties.TryGetValue("Warp", out var wVal) ? wVal.ToString() : string.Empty;
                        var westWarpsList = new List<string>();
                        for (int y = westPassage.FirstRow; y < westPassage.FirstRow + westPassage.RowCount; y++)
                        {
                            westWarpsList.Add($"0 {y} {westPassage.TargetLocation} {westPassage.ArrivalX} {westPassage.ArrivalY}");
                        }
                        string westWarps = string.Join(" ", westWarpsList);
                        hostMap.Properties["Warp"] = string.IsNullOrWhiteSpace(existingWarps) ? westWarps : $"{existingWarps} {westWarps}";
                    }

                    return hostMap;
                }, AssetLoadPriority.Medium);
            }
            else if (e.NameWithoutLocale.StartsWith("Maps/Pelican4Ever_Bridge_"))
            {
                // TASK-1202: Genera el mapa Maps/Pelican4Ever_Bridge_N determinista a partir de Maps/BusStop
                string bridgeName = Path.GetFileName(e.NameWithoutLocale.BaseName);
                e.LoadFrom(() =>
                {
                    int slotCount = context?.Config.FarmSlotCount ?? 16;
                    var bridgeLoc = HostPlazaLayout.Find(bridgeName, slotCount)
                        ?? HostPlazaLayout.Chain(16).FirstOrDefault(l => string.Equals(l.Name, bridgeName, StringComparison.OrdinalIgnoreCase));
                    if (bridgeLoc == null)
                        return new xTile.Map(bridgeName);

                    var busStopMap = context?.Helper.GameContent.Load<xTile.Map>("Maps/BusStop");
                    if (busStopMap == null)
                        return new xTile.Map(bridgeName);

                    var bridgeMap = MapCloneHelper.CreateBridgeMap(busStopMap, bridgeLoc);

                    var townMap = context?.Helper.GameContent.Load<xTile.Map>("Maps/Town");
                    if (townMap != null)
                        AddMailboxAssets(bridgeMap, townMap);
                    AddPathAssets(bridgeMap);

                    var bldLayer = bridgeMap.GetLayer("Buildings");
                    var frontLayer = bridgeMap.GetLayer("Front");
                    var alwaysFrontLayer = bridgeMap.GetLayer("AlwaysFront");
                    var backLayer = bridgeMap.GetLayer("Back");

                    var sampleTile = backLayer?.Tiles[15, 15] ?? backLayer?.Tiles[8, 6];

                    foreach (var door in RegisteredDoors)
                    {
                        if (string.Equals(door.LocationName, bridgeLoc.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            ClearDoorAreaOnMap(bldLayer, frontLayer, alwaysFrontLayer, backLayer, door, sampleTile);
                        }
                    }

                    return bridgeMap;
                }, AssetLoadPriority.Medium);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo(SteppingStonesAssetKey) || e.NameWithoutLocale.IsEquivalentTo(TerrainTileClassifier.SteppingStonesTileSheetId))
            {
                e.LoadFromModFile<Microsoft.Xna.Framework.Graphics.Texture2D>("assets/stepping_stones.png", AssetLoadPriority.Medium);
            }
        }

        public static (Point tile1, Point tile2) GetOpeningTiles(DoorModel door)
        {
            if (door.Edge is PortalEdge.West or PortalEdge.East)
            {
                return (new Point(door.TownTileX, door.TownTileY), new Point(door.TownTileX, door.TownTileY + 1));
            }
            else
            {
                return (new Point(door.TownTileX, door.TownTileY), new Point(door.TownTileX + 1, door.TownTileY));
            }
        }

        public static void GetResidenceArrivalCoordinates(DoorModel door, out int targetX, out int targetY, out int facingDirection)
        {
            SpatialWarpRouter.GetResidenceArrivalCoordinates(door.Edge, out targetX, out targetY, out facingDirection);
        }

        public static void GetResidenceArrivalCoordinates(DoorModel door, string? farmType, out int targetX, out int targetY, out int facingDirection)
        {
            SpatialWarpRouter.GetResidenceArrivalCoordinates(door.Edge, farmType, out targetX, out targetY, out facingDirection);
        }

        public static bool IsInOpening(DoorModel door, int tileX, int tileY)
        {
            var (t1, t2) = GetOpeningTiles(door);
            return (tileX == t1.X && tileY == t1.Y) || (tileX == t2.X && tileY == t2.Y);
        }

        public static bool IsInPlazaPassage(PlazaLocation loc, int tileX, int tileY)
        {
            if (loc.West is { } west)
            {
                int maxWestX = string.Equals(loc.Name, HostPlazaLayout.HostFarm, StringComparison.OrdinalIgnoreCase) ? 7 : 5;
                if (tileX >= 0 && tileX <= maxWestX && tileY >= west.FirstRow && tileY < west.FirstRow + west.RowCount)
                    return true;
            }

            if (loc.East is { } east)
            {
                if (tileX >= loc.Width - 6 && tileX < loc.Width && tileY >= east.FirstRow && tileY < east.FirstRow + east.RowCount)
                    return true;
            }

            return false;
        }

        public static bool IsDoorActiveForCurrentConfig(DoorModel door)
        {
            if (door == null)
                return false;

            if (context?.WorldState?.Doors != null && context.WorldState.Doors.ContainsKey(door.DoorId))
                return true;

            int slotCount = context?.Config.FarmSlotCount ?? 16;
            return PlazaCatalogFormatter.IsDoorActive(door.DefaultSlotId, door.LocationName, slotCount);
        }

        public static bool IsInOpeningOrBorder(DoorModel door, int tileX, int tileY)
        {
            var loc = HostPlazaLayout.Find(door.LocationName, 16);
            return IsInOpeningOrBorder(door, loc, tileX, tileY);
        }

        public static bool IsInOpeningOrBorder(DoorModel door, PlazaLocation? loc, int tileX, int tileY)
        {
            loc ??= HostPlazaLayout.Find(door.LocationName, 16);
            if (loc == null)
            {
                var (t1, t2) = GetOpeningTiles(door);
                return (tileX == t1.X && tileY == t1.Y) || (tileX == t2.X && tileY == t2.Y);
            }

            var path = SpatialWarpRouter.GetResidencePathTiles(door.Edge, door.TownTileX, door.TownTileY, loc);
            foreach (var p in path)
            {
                if (p.X == tileX && p.Y == tileY)
                    return true;
            }

            return false;
        }

        private static void SetTileIfValid(xTile.Layers.Layer? layer, xTile.Tiles.TileSheet sheet, int x, int y, int tileIndex)
        {
            if (layer != null && x >= 0 && x < layer.LayerWidth && y >= 0 && y < layer.LayerHeight)
            {
                layer.Tiles[x, y] = new xTile.Tiles.StaticTile(layer, sheet, xTile.Tiles.BlendMode.Alpha, tileIndex);
            }
        }

        public static List<Point> ClearDoorAreaOnMap(
            xTile.Layers.Layer? bld,
            xTile.Layers.Layer? front,
            xTile.Layers.Layer? alwaysFront,
            xTile.Layers.Layer? back,
            DoorModel door,
            xTile.Tiles.Tile? sampleTile)
        {
            var loc = HostPlazaLayout.Find(door.LocationName, 16)
                   ?? new PlazaLocation(door.LocationName, bld?.LayerWidth ?? 80, bld?.LayerHeight ?? 65, null, null);
            var passTiles = SpatialWarpRouter.GetResidencePathTiles(door.Edge, door.TownTileX, door.TownTileY, loc)
                .Select(t => new Point(t.X, t.Y))
                .ToList();
            var passTilesSet = new HashSet<Point>(passTiles);

            var map = bld?.Map ?? front?.Map ?? back?.Map;
            var bld2 = map?.GetLayer("Buildings2");
            var alwaysFront2 = map?.GetLayer("AlwaysFront2");
            var alwaysFront3 = map?.GetLayer("AlwaysFront3");

            var sheetStepping = map?.GetTileSheet(TerrainTileClassifier.SteppingStonesTileSheetId);
            var sheetOutdoors = MapCloneHelper.GetOutdoorsTileSheet(map)
                             ?? sampleTile?.TileSheet;

            var entranceStyle = TerrainTileClassifier.ResolveEntranceStyle(door.LocationName, door.DoorId, door.Edge, door.TownTileX, door.TownTileY);

            foreach (var pt in passTiles)
            {
                // 1. Limpiar colisión en Buildings para que el paso sea completamente transitable sin bloqueos
                if (bld != null && pt.X >= 0 && pt.X < bld.LayerWidth && pt.Y >= 0 && pt.Y < bld.LayerHeight)
                    bld.Tiles[pt.X, pt.Y] = null;
                if (bld2 != null && pt.X >= 0 && pt.X < bld2.LayerWidth && pt.Y >= 0 && pt.Y < bld2.LayerHeight)
                    bld2.Tiles[pt.X, pt.Y] = null;

                // 2. Limpiar Front para eliminar solapamientos de vallas, rocas y follaje cortado
                if (front != null && pt.X >= 0 && pt.X < front.LayerWidth && pt.Y >= 0 && pt.Y < front.LayerHeight)
                    front.Tiles[pt.X, pt.Y] = null;

                // 3. Limpiar AlwaysFront a lo largo del camino para evitar fragmentos de copa flotando en el aire sobre el paso
                if (alwaysFront != null && pt.X >= 0 && pt.X < alwaysFront.LayerWidth && pt.Y >= 0 && pt.Y < alwaysFront.LayerHeight)
                    alwaysFront.Tiles[pt.X, pt.Y] = null;
                if (alwaysFront2 != null && pt.X >= 0 && pt.X < alwaysFront2.LayerWidth && pt.Y >= 0 && pt.Y < alwaysFront2.LayerHeight)
                    alwaysFront2.Tiles[pt.X, pt.Y] = null;
                if (alwaysFront3 != null && pt.X >= 0 && pt.X < alwaysFront3.LayerWidth && pt.Y >= 0 && pt.Y < alwaysFront3.LayerHeight)
                    alwaysFront3.Tiles[pt.X, pt.Y] = null;

                if (back != null && pt.X >= 0 && pt.X < back.LayerWidth && pt.Y >= 0 && pt.Y < back.LayerHeight)
                {
                    var existingBack = back.Tiles[pt.X, pt.Y];
                    string? typeProp = null;
                    existingBack?.Properties.TryGetValue("Type", out typeProp);

                    int mask = TerrainTileClassifier.CalculateAutotileMask(pt.X, pt.Y, (x, y) => passTilesSet.Contains(new Point(x, y)));

                    var (tileIndex, isStepping) = TerrainTileClassifier.GetPathTileInfo(
                        door.Edge,
                        loc,
                        door.TownTileX,
                        door.TownTileY,
                        pt.X,
                        pt.Y,
                        existingBack?.TileIndex,
                        typeProp,
                        existingBack?.TileSheet?.Id,
                        mask,
                        entranceStyle);

                    var targetSheet = (isStepping ? sheetStepping : null) ?? sheetOutdoors;
                    if (targetSheet != null)
                    {
                        back.Tiles[pt.X, pt.Y] = new xTile.Tiles.StaticTile(back, targetSheet, xTile.Tiles.BlendMode.Alpha, tileIndex);
                        back.Tiles[pt.X, pt.Y].Properties.Remove("Passable");
                        if (!isStepping)
                        {
                            back.Tiles[pt.X, pt.Y].Properties["Type"] = (entranceStyle == EntranceStyle.WoodPlanks) ? "Wood" : "Stone";
                        }
                    }
                    else if (back.Tiles[pt.X, pt.Y] == null && sampleTile != null)
                    {
                        back.Tiles[pt.X, pt.Y] = sampleTile.Clone(back);
                        back.Tiles[pt.X, pt.Y].Properties.Remove("Passable");
                    }
                }
            }

            // 4. Remates y acabados artesanales de perímetros, desfiladeros y eliminación de copas cortadas
            ApplyPortalArtisanFinishes(bld, front, alwaysFront, back, door, sheetOutdoors, map);

            PlaceMailboxTiles(bld, front, back, door);

            return passTiles;
        }

        private static void ApplyPortalArtisanFinishes(
            xTile.Layers.Layer? bld,
            xTile.Layers.Layer? front,
            xTile.Layers.Layer? alwaysFront,
            xTile.Layers.Layer? back,
            DoorModel door,
            xTile.Tiles.TileSheet? sheetOutdoors,
            xTile.Map? map)
        {
            if (sheetOutdoors == null) return;

            var bld2 = map?.GetLayer("Buildings2");
            var alwaysFront2 = map?.GetLayer("AlwaysFront2");
            var alwaysFront3 = map?.GetLayer("AlwaysFront3");

            bool isFarm = string.Equals(door.LocationName, HostPlazaLayout.HostFarm, StringComparison.OrdinalIgnoreCase);

            if (isFarm)
            {
                if (door.Edge == PortalEdge.North)
                {
                    if (door.TownTileX == 18) // Farm_Portal_01 (NW: desfiladero rocoso, x=18..19, y=0..10)
                    {
                        int lx = 17, rx = 20;
                        SetTileIfValid(bld, sheetOutdoors, lx, 3, 444);
                        SetTileIfValid(bld, sheetOutdoors, lx, 4, 469);
                        SetTileIfValid(bld, sheetOutdoors, lx, 5, 494);
                        SetTileIfValid(bld, sheetOutdoors, lx, 6, 519);
                        SetTileIfValid(bld, sheetOutdoors, lx, 7, 544);
                        if (back != null) SetTileIfValid(back, sheetOutdoors, lx, 8, 618);

                        SetTileIfValid(bld, sheetOutdoors, rx, 3, 441);
                        SetTileIfValid(bld, sheetOutdoors, rx, 4, 466);
                        SetTileIfValid(bld, sheetOutdoors, rx, 5, 491);
                        SetTileIfValid(bld, sheetOutdoors, rx, 6, 516);
                        SetTileIfValid(bld, sheetOutdoors, rx, 7, 541);
                        if (back != null) SetTileIfValid(back, sheetOutdoors, rx, 8, 645);

                        // Limpiar copas, ramas y hojas que asomen sobre el desfiladero
                        for (int x = 16; x <= 21; x++)
                        {
                            for (int y = 0; y <= 10; y++)
                            {
                                ClearTile(front, x, y);
                                ClearTile(alwaysFront, x, y);
                                ClearTile(alwaysFront2, x, y);
                                ClearTile(alwaysFront3, x, y);
                            }
                        }

                        // Detalle decorativo: piedra rocosa en la ladera derecha
                        SetTileIfValid(bld, sheetOutdoors, rx, 2, 307);
                    }
                    else if (door.TownTileX == 62) // Farm_Portal_02 (NE: bosque campestre, x=62..63, y=0..10)
                    {
                        // Valla de madera en y = 9 con postes canónicos
                        SetTileIfValid(bld, sheetOutdoors, 61, 9, 385);
                        SetTileIfValid(front, sheetOutdoors, 61, 8, 360);
                        SetTileIfValid(bld, sheetOutdoors, 64, 9, 383);
                        SetTileIfValid(front, sheetOutdoors, 64, 8, 358);

                        // Eliminar completamente el árbol cortado y copas en x = 60..65, y = 0..9
                        for (int x = 60; x <= 65; x++)
                        {
                            for (int y = 0; y <= 9; y++)
                            {
                                ClearTile(front, x, y);
                                ClearTile(alwaysFront, x, y);
                                ClearTile(alwaysFront2, x, y);
                                ClearTile(alwaysFront3, x, y);
                            }
                        }

                        // Seto verde delimitador a ambos lados del paso
                        for (int y = 3; y <= 7; y++)
                        {
                            SetTileIfValid(bld, sheetOutdoors, 61, y, 406);
                            SetTileIfValid(bld, sheetOutdoors, 64, y, 406);
                        }

                        // Detalle decorativo: barril rústico junto a la entrada norte
                        SetTileIfValid(bld, sheetOutdoors, 64, 2, 809);
                    }
                }
                else if (door.Edge == PortalEdge.South)
                {
                    if (door.TownTileX == 18) // Farm_Portal_03 (SW: pastizal sur, x=18..19, y=60..64)
                    {
                        // Remate de valla de madera en y = 63
                        SetTileIfValid(bld, sheetOutdoors, 17, 63, 385);
                        SetTileIfValid(front, sheetOutdoors, 17, 62, 360);
                        SetTileIfValid(bld, sheetOutdoors, 20, 63, 383);
                        SetTileIfValid(front, sheetOutdoors, 20, 62, 358);

                        // Limpiar corte en Front y reborde de hierba
                        for (int x = 16; x <= 21; x++)
                        {
                            ClearTile(front, x, 60);
                            ClearTile(front, x, 61);
                            ClearTile(front, x, 62);
                        }

                        // Limpiar completamente el árbol cortado en AlwaysFront
                        for (int x = 15; x <= 22; x++)
                        {
                            for (int y = 58; y <= 64; y++)
                            {
                                ClearTile(alwaysFront, x, y);
                                ClearTile(alwaysFront2, x, y);
                                ClearTile(alwaysFront3, x, y);
                            }
                        }

                        // Detalle decorativo: farola rústica de poste junto al camino
                        SetTileIfValid(bld, sheetOutdoors, 20, 61, 378);
                    }
                    else if (door.TownTileX == 62) // Farm_Portal_04 (SE: huerto sudeste, x=62..63, y=60..64)
                    {
                        // Remate de valla de madera en y = 63
                        SetTileIfValid(bld, sheetOutdoors, 61, 63, 385);
                        SetTileIfValid(front, sheetOutdoors, 61, 62, 360);
                        SetTileIfValid(bld, sheetOutdoors, 64, 63, 383);
                        SetTileIfValid(front, sheetOutdoors, 64, 62, 358);

                        // Limpiar corte en Front y reborde de hierba
                        for (int x = 60; x <= 65; x++)
                        {
                            ClearTile(front, x, 60);
                            ClearTile(front, x, 61);
                            ClearTile(front, x, 62);
                        }

                        // Limpiar completamente el cerezo de 6x5 en AlwaysFront
                        for (int x = 59; x <= 66; x++)
                        {
                            for (int y = 58; y <= 65; y++)
                            {
                                ClearTile(alwaysFront, x, y);
                                ClearTile(alwaysFront2, x, y);
                                ClearTile(alwaysFront3, x, y);
                            }
                        }

                        // Detalle decorativo: flor silvestre en la pradera
                        SetTileIfValid(bld, sheetOutdoors, 64, 61, 256);
                    }
                }
            }
            else // Puentes (Pelican4Ever_Bridge_1..3)
            {
                if (door.Edge == PortalEdge.North)
                {
                    if (door.TownTileX <= 15) // Norte Izquierda (junto a la vagoneta)
                    {
                        int lx = door.TownTileX - 1;
                        int rx = door.TownTileX + 2;

                        SetTileIfValid(bld, sheetOutdoors, lx, 0, 385);
                        SetTileIfValid(front, sheetOutdoors, lx, 0, 360);
                        SetTileIfValid(bld, sheetOutdoors, rx, 0, 383);
                        SetTileIfValid(front, sheetOutdoors, rx, 0, 358);

                        // Limpiar medio arbusto y copas que solapan el paso
                        ClearTile(bld, lx, 2);
                        ClearTile(bld, lx, 3);
                        for (int x = lx - 1; x <= rx + 1; x++)
                        {
                            for (int y = 0; y <= 4; y++)
                            {
                                ClearTile(front, x, y);
                                ClearTile(alwaysFront, x, y);
                                ClearTile(alwaysFront2, x, y);
                            }
                        }

                        // Detalle decorativo estación: barril de madera junto al andén
                        SetTileIfValid(bld, sheetOutdoors, rx, 1, 809);
                    }
                    else // Norte Derecha (desfiladero de roca en el acantilado)
                    {
                        int lx = door.TownTileX - 1;
                        int rx = door.TownTileX + 2;

                        // Desfiladero canónico de roca
                        SetTileIfValid(bld, sheetOutdoors, lx, 1, 469);
                        SetTileIfValid(bld, sheetOutdoors, lx, 2, 494);
                        SetTileIfValid(bld, sheetOutdoors, lx, 3, 519);
                        SetTileIfValid(bld, sheetOutdoors, lx, 4, 544);
                        SetTileIfValid(bld, sheetOutdoors, lx, 5, 544);
                        if (back != null) SetTileIfValid(back, sheetOutdoors, lx, 6, 618);

                        SetTileIfValid(bld, sheetOutdoors, rx, 1, 466);
                        SetTileIfValid(bld, sheetOutdoors, rx, 2, 491);
                        SetTileIfValid(bld, sheetOutdoors, rx, 3, 516);
                        SetTileIfValid(bld, sheetOutdoors, rx, 4, 541);
                        SetTileIfValid(bld, sheetOutdoors, rx, 5, 541);
                        if (back != null) SetTileIfValid(back, sheetOutdoors, rx, 6, 645);

                        for (int y = 0; y <= 6; y++)
                        {
                            ClearTile(front, lx, y);
                            ClearTile(front, rx, y);
                            ClearTile(bld2, lx, y);
                            ClearTile(bld2, rx, y);
                        }

                        // Limpiar árbol y copa cortada sobre la pared de roca
                        for (int x = lx - 1; x <= rx + 2; x++)
                        {
                            for (int y = 0; y <= 7; y++)
                            {
                                ClearTile(alwaysFront, x, y);
                                ClearTile(alwaysFront2, x, y);
                                ClearTile(alwaysFront3, x, y);
                            }
                        }

                        // Detalle decorativo: piedra de montaña
                        SetTileIfValid(bld, sheetOutdoors, rx, 6, 307);
                    }
                }
                else if (door.Edge == PortalEdge.South)
                {
                    int lx = door.TownTileX - 1;
                    int rx = door.TownTileX + 2;

                    // Remate de valla inferior debajo de la acera
                    SetTileIfValid(bld, sheetOutdoors, lx, 26, 385);
                    SetTileIfValid(front, sheetOutdoors, lx, 25, 360);
                    SetTileIfValid(bld, sheetOutdoors, rx, 26, 383);
                    SetTileIfValid(front, sheetOutdoors, rx, 25, 358);

                    // Limpiar reborde de la acera y valla en el hueco
                    ClearTile(front, door.TownTileX, 25);
                    ClearTile(front, door.TownTileX + 1, 25);

                    // Limpiar follaje y ramas cortadas en AlwaysFront
                    for (int x = lx - 1; x <= rx + 2; x++)
                    {
                        for (int y = 23; y <= 29; y++)
                        {
                            ClearTile(alwaysFront, x, y);
                            ClearTile(alwaysFront2, x, y);
                            ClearTile(alwaysFront3, x, y);
                        }
                    }

                    // Detalle decorativo: farola de madera o arbusto en el lado opuesto al buzón
                    if (door.TownTileX <= 15)
                    {
                        SetTileIfValid(bld, sheetOutdoors, rx, 25, 378); // Farola de madera junto a la acera
                    }
                    else
                    {
                        SetTileIfValid(bld, sheetOutdoors, rx, 25, 307); // Roca decorativa
                    }
                }
            }
        }

        private static void ClearTile(xTile.Layers.Layer? layer, int x, int y)
        {
            if (layer != null && x >= 0 && x < layer.LayerWidth && y >= 0 && y < layer.LayerHeight)
            {
                layer.Tiles[x, y] = null;
            }
        }

        /// <summary>
        /// Aplica acabados artesanales y canónicos vanilla al paso oeste de la Granja Host (Maps/Farm).
        /// Elimina copas y ramas cortadas en AlwaysFront y Front (árboles 1 y 2), coloca entarimado
        /// de tablones rústicos de madera auténtica en Back y remata con vallas y acantilado.
        /// </summary>
        public static void ApplyFarmWestPassageArtisanFinishes(
            xTile.Map? hostMap,
            PlazaPassage westPassage,
            xTile.Tiles.TileSheet? sheetOutdoors)
        {
            if (hostMap == null) return;
            sheetOutdoors ??= MapCloneHelper.GetOutdoorsTileSheet(hostMap);
            if (sheetOutdoors == null) return;

            var bld = hostMap.GetLayer("Buildings");
            var bld2 = hostMap.GetLayer("Buildings2");
            var front = hostMap.GetLayer("Front");
            var alwaysFront = hostMap.GetLayer("AlwaysFront");
            var alwaysFront2 = hostMap.GetLayer("AlwaysFront2");
            var alwaysFront3 = hostMap.GetLayer("AlwaysFront3");
            var back = hostMap.GetLayer("Back");

            // 1. Limpieza integral de follaje, copas cortadas y ramas en x = 0..7, y = 22..30
            // Elimina Tree 1 (x=1..6, y=24..26) y Tree 2 (x=0..2, y=26..29) de raíz
            for (int x = 0; x <= 7; x++)
            {
                for (int y = 22; y <= 30; y++)
                {
                    ClearTile(alwaysFront, x, y);
                    ClearTile(alwaysFront2, x, y);
                    ClearTile(alwaysFront3, x, y);
                    ClearTile(front, x, y);
                }
            }

            // 2. Tallar corredor transitable en x = 0..7, y = 26..27 con entarimado rústico de madera
            for (int x = 0; x <= 7; x++)
            {
                for (int y = westPassage.FirstRow; y < westPassage.FirstRow + westPassage.RowCount; y++)
                {
                    ClearTile(bld, x, y);
                    ClearTile(bld2, x, y);

                    if (back != null && x >= 0 && x < back.LayerWidth && y >= 0 && y < back.LayerHeight)
                    {
                        // Tablones rústicos de madera auténtica vanilla (1244 / 1269)
                        int plankTileIndex = ((x + y) % 2 == 0) ? 1244 : 1269;
                        back.Tiles[x, y] = new xTile.Tiles.StaticTile(back, sheetOutdoors, xTile.Tiles.BlendMode.Alpha, plankTileIndex);
                        back.Tiles[x, y].Properties.Remove("Passable");
                        back.Tiles[x, y].Properties["Type"] = "Wood";
                    }
                }
            }

            // 3. Remates de valla y acantilado en los bordes del corredor (y = 25 y y = 28)
            // Lado Norte (y = 25): poste izquierdo, barandilla de madera y esquina de acantilado
            SetTileIfValid(bld, sheetOutdoors, 0, 25, 385);
            SetTileIfValid(front, sheetOutdoors, 0, 24, 360);
            for (int x = 1; x <= 5; x++)
            {
                SetTileIfValid(bld, sheetOutdoors, x, 25, 384);
                SetTileIfValid(front, sheetOutdoors, x, 24, 359);
            }
            SetTileIfValid(bld, sheetOutdoors, 6, 25, 544); // Acantilado canónico superior

            // Lado Sur (y = 28): poste izquierdo, barandilla y poste derecho de remate
            SetTileIfValid(bld, sheetOutdoors, 0, 28, 385);
            for (int x = 1; x <= 5; x++)
            {
                SetTileIfValid(bld, sheetOutdoors, x, 28, 384);
            }
            SetTileIfValid(bld, sheetOutdoors, 6, 28, 383);
        }

        /// <summary>Asset de tilesheet para Steppingstone Path en Maps/z_Pelican4Ever_SteppingStones.</summary>
        private const string SteppingStonesAssetKey = "Maps/z_Pelican4Ever_SteppingStones";

        /// <summary>
        /// Añade la hoja de tiles de Stepping Stones a la Granja Host.
        /// </summary>
        private static void AddPathAssets(xTile.Map map)
        {
            if (map.GetTileSheet(TerrainTileClassifier.SteppingStonesTileSheetId) == null)
            {
                map.AddTileSheet(new xTile.Tiles.TileSheet(
                    TerrainTileClassifier.SteppingStonesTileSheetId,
                    map,
                    SteppingStonesAssetKey,
                    new xTile.Dimensions.Size(4, 8),
                    new xTile.Dimensions.Size(16, 16)));
            }
        }

        /// <summary>Hoja de tiles del pueblo añadida a la Granja Host para dibujar los buzones de las parcelas.</summary>
        private const string MailboxTileSheetId = "z_Pelican4Ever_Town";

        /// <summary>Buzón de las casas de Pelican Town (<c>Maps/spring_town</c>): poste en Buildings y caja una fila encima.</summary>
        private const int MailboxPostTileIndex = 41;
        private const int MailboxTopTileIndex = 9;

        /// <summary>Capa que se dibuja sobre Front: la caja del buzón no sustituye copas ni rebordes de hierba.</summary>
        private const string MailboxTopLayerId = "Front2";

        /// <summary>
        /// Añade al mapa de la Granja Host la hoja de tiles del pueblo y la capa <see cref="MailboxTopLayerId"/>.
        /// La hoja se llama <c>spring_town</c> y el juego la cambia de estación como las demás.
        /// </summary>
        private static void AddMailboxAssets(xTile.Map map, xTile.Map townMap)
        {
            if (map.GetTileSheet(MailboxTileSheetId) == null)
            {
                xTile.Tiles.TileSheet? source = townMap.GetTileSheet("Town")
                    ?? townMap.TileSheets.FirstOrDefault(ts => ts.ImageSource.Replace('\\', '/').EndsWith("spring_town", StringComparison.OrdinalIgnoreCase));
                if (source == null)
                {
                    context?.Monitor.Log("[FarmSlotModule] Maps/Town no tiene la hoja spring_town; las parcelas quedan sin buzón.", LogLevel.Warn);
                    return;
                }

                map.AddTileSheet(new xTile.Tiles.TileSheet(MailboxTileSheetId, map, source.ImageSource, source.SheetSize, source.TileSize));
            }

            if (map.GetLayer(MailboxTopLayerId) == null && map.GetLayer("Front") is xTile.Layers.Layer front)
                map.AddLayer(new xTile.Layers.Layer(MailboxTopLayerId, map, front.LayerSize, front.TileSize));
        }

        /// <summary>
        /// Dibuja el buzón de la parcela con los tiles del buzón del pueblo: el poste sustituye el tile de Buildings
        /// (colisión de un poste real) y la caja va en <see cref="MailboxTopLayerId"/>, una fila encima.
        /// El buzón se planta sobre una base limpia de hierba (175) o tierra (587) según el terreno circundante.
        /// </summary>
        private static void PlaceMailboxTiles(xTile.Layers.Layer? bld, xTile.Layers.Layer? front, xTile.Layers.Layer? back, DoorModel door)
        {
            xTile.Map? map = bld?.Map;
            xTile.Tiles.TileSheet? sheetTown = map?.GetTileSheet(MailboxTileSheetId);
            xTile.Tiles.TileSheet? sheetOutdoors = MapCloneHelper.GetOutdoorsTileSheet(map);
            xTile.Layers.Layer? top = map?.GetLayer(MailboxTopLayerId);
            xTile.Layers.Layer? alwaysFront = map?.GetLayer("AlwaysFront");
            if (bld == null || sheetTown == null || top == null)
                return;

            var (mx, my) = TownMailboxLogic.GetMailboxCoords(door.TownTileX, door.TownTileY, door.MailboxTileX, door.MailboxTileY);
            if (mx < 0 || mx >= bld.LayerWidth || my < 1 || my >= bld.LayerHeight)
                return;

            bld.Tiles[mx, my] = new xTile.Tiles.StaticTile(bld, sheetTown, xTile.Tiles.BlendMode.Alpha, MailboxPostTileIndex);
            top.Tiles[mx, my - 1] = new xTile.Tiles.StaticTile(top, sheetTown, xTile.Tiles.BlendMode.Alpha, MailboxTopTileIndex);

            // Asegurar que ni Front ni AlwaysFront tapen el poste ni la caja del buzón
            if (front != null)
            {
                if (mx >= 0 && mx < front.LayerWidth && my >= 0 && my < front.LayerHeight)
                    front.Tiles[mx, my] = null;
                if (mx >= 0 && mx < front.LayerWidth && my - 1 >= 0 && my - 1 < front.LayerHeight)
                    front.Tiles[mx, my - 1] = null;
            }
            if (alwaysFront != null)
            {
                if (mx >= 0 && mx < alwaysFront.LayerWidth && my >= 0 && my < alwaysFront.LayerHeight)
                    alwaysFront.Tiles[mx, my] = null;
                if (mx >= 0 && mx < alwaysFront.LayerWidth && my - 1 >= 0 && my - 1 < alwaysFront.LayerHeight)
                    alwaysFront.Tiles[mx, my - 1] = null;
            }

            // El buzón se planta limpiamente sobre hierba (175) o tierra (587) según si el entorno circundante es de hierba o tierra
            if (back != null && sheetOutdoors != null && mx >= 0 && mx < back.LayerWidth && my >= 0 && my < back.LayerHeight)
            {
                var existingBack = back.Tiles[mx, my];
                string? typeProp = null;
                existingBack?.Properties.TryGetValue("Type", out typeProp);

                var loc = HostPlazaLayout.Find(door.LocationName, 16)
                       ?? new PlazaLocation(door.LocationName, bld.LayerWidth, bld.LayerHeight, null, null);
                bool isGrass = TerrainTileClassifier.IsMailboxOnGrass(door.Edge, loc, door.TownTileX, door.TownTileY, mx, my);
                if (!isGrass && existingBack != null && TerrainTileClassifier.IsGrassTileIndex(existingBack.TileIndex, typeProp, existingBack.TileSheet?.Id))
                {
                    isGrass = true;
                }

                int baseTile = isGrass ? 175 : 587;
                back.Tiles[mx, my] = new xTile.Tiles.StaticTile(back, sheetOutdoors, xTile.Tiles.BlendMode.Alpha, baseTile);
            }
        }

        /// <summary>
        /// Casillas donde versiones anteriores ponían un cartel de madera (Sign 37) en lugar del buzón; se retira
        /// de las partidas existentes. Las demás parcelas ya tenían el cartel en la casilla del buzón actual.
        /// </summary>
        private static readonly Point[] LegacySignTiles =
        {
            new(2, 25), new(7, 25),
            new(17, 2), new(17, 9),
            new(27, 2), new(27, 9),
            new(51, 2), new(51, 9),
            new(61, 2), new(61, 11),
            new(7, 2), new(10, 9),
            new(71, 2), new(71, 9),
            new(61, 62), new(7, 62)
        };

        private static void RemoveLegacySign(Farm farm, Vector2 tile)
        {
            if (farm.objects.TryGetValue(tile, out StardewValley.Object? obj) && obj is Sign && obj.ItemId == "37")
                farm.objects.Remove(tile);
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            RegisterChatCommands();

            if (Context.IsMainPlayer)
            {
                if (context == null) return;

                // Asegurarse de que useSeparateWallets esté activo para carteras individuales
                Game1.player.team.useSeparateWallets.Value = true;

                Farm hostFarm = Game1.getFarm();
                if (hostFarm != null)
                {
                    if (!string.Equals(hostFarm.mapPath.Value, "Maps\\Pelican4Ever_HostFarm", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(hostFarm.mapPath.Value, "Maps/Pelican4Ever_HostFarm", StringComparison.OrdinalIgnoreCase))
                    {
                        hostFarm.mapPath.Value = "Maps\\Pelican4Ever_HostFarm";
                        hostFarm.reloadMap();
                    }

                    // Eliminar cabañas redundantes de la granja host (Plaza Central)
                    RemoveHostCabins(hostFarm);

                    // Aprovisionar portales físicos continuos de 2 tiles y aceras en la granja del anfitrión
                    EnsureHostFarmPortals(hostFarm);
                }

                // Reparar asignaciones heredadas de reclamos múltiples o de un catálogo de puertas anterior
                if (RegisteredDoors.Count > 0)
                {
                    var catalogSlots = new Dictionary<string, string>();
                    foreach (DoorModel d in RegisteredDoors)
                        catalogSlots.TryAdd(d.DoorId, d.DefaultSlotId);

                    List<string> released = context.WorldState.NormalizeDoorAssignments(catalogSlots);
                    if (released.Count > 0)
                    {
                        context.Monitor.Log($"[FarmSlotModule] Liberadas {released.Count} asignaciones inválidas o duplicadas: {string.Join(", ", released)}.", LogLevel.Warn);
                    }
                }

                // Data/Locations se construyó al lanzar el juego, sin el estado de la partida
                RefreshSlotLocationData();

                // Asegurar edificios limpios y Farmhouse operativa en cada granja instanciada.
                // Primero las reclamadas: así el hogar del dueño queda fijado antes de soltar cabañas libres.
                int slotCount = context.Config.FarmSlotCount;
                foreach (Farmer farmer in Game1.getAllFarmers())
                {
                    SanitizePlayerOrphanedBridgeLocation(farmer, slotCount);
                }

                var slots = Enumerable.Range(1, slotCount)
                    .Select(i => $"Pelican4Ever_Farm_{i:D2}")
                    .Select(slotId => (slotId, assignment: context.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == slotId)))
                    .OrderBy(s => s.assignment == null);
                foreach (var (slotId, assignment) in slots)
                {
                    Farm? farm = EnsureFarmSlotExists(slotId);
                    if (farm != null)
                    {
                        if (assignment != null)
                        {
                            ApplyFarmTemplate(farm, assignment.FarmType, regenerate: false);
                            // Las propiedades de mapa no se guardan con la partida: reponerlas aunque el mapa no cambie
                            FarmTypeModule.ApplyMapProperties(farm, assignment.FarmType);
                        }
                        EnsureFarmSlotBuildings(farm, assignment);
                        if (assignment == null)
                        {
                            farm.ClearOwnership();
                        }
                    }
                }

                // Antes de que entre ningún cliente: ningún peón puede quedar sin cabaña
                RehomeOrphanFarmhands();

                // Publicar modData de propiedad en las ubicaciones cargadas
                foreach (var kvp in context.WorldState.Doors)
                {
                    DoorAssignment assignment = kvp.Value;
                    GameLocation? loc = Game1.getLocationFromName(assignment.FarmSlotId);
                    if (loc != null)
                    {
                        loc.PublishOwnership(assignment.OwnerId, assignment.Policy);
                    }
                    GameLocation? caveLoc = Game1.getLocationFromName($"{assignment.FarmSlotId}_Cave");
                    if (caveLoc != null)
                    {
                        caveLoc.PublishOwnership(assignment.OwnerId, assignment.Policy);
                    }
                    GameLocation? cellarLoc = Game1.getLocationFromName(ResidenceUpgradeLogic.GetSlotCellarName(assignment.FarmSlotId));
                    if (cellarLoc != null)
                    {
                        cellarLoc.PublishOwnership(assignment.OwnerId, assignment.Policy);
                    }
                    GameLocation? ghLoc = Game1.getLocationFromName(ResidenceUpgradeLogic.GetSlotGreenhouseName(assignment.FarmSlotId));
                    if (ghLoc != null)
                    {
                        ghLoc.PublishOwnership(assignment.OwnerId, assignment.Policy);
                    }
                }

                // TASK-903: Asegurar ubicaciones de anexos adquiridos, sus mapas 40×32 y su propiedad/política heredada
                EnsureAllAnnexLocations();

                // Sincronizar catálogo inicial de residencias y anexos con todos los peones
                BroadcastDoorsSync();
            }
            else
            {
                // Cliente: pedir al host el estado más actualizado de residencias
                context?.Helper.Multiplayer.SendMessage(
                    string.Empty,
                    ModMessages.RequestDoorsSync,
                    new[] { context.Manifest.UniqueID },
                    new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );
            }
        }

        private void OnPeerContextReceived(object? sender, PeerContextReceivedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null) return;

            int slotCount = context.Config.FarmSlotCount;
            Farmer? farmer = Game1.GetPlayer(e.Peer.PlayerID);
            SanitizePlayerOrphanedBridgeLocation(farmer, slotCount);

            SendDoorsSyncToPlayer(e.Peer.PlayerID);
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null) return;

            EnsureAllPlazaPortals();
        }

        public static void SanitizePlayerOrphanedBridgeLocation(Farmer? who, int slotCount)
        {
            if (who == null) return;
            var chain = HostPlazaLayout.Chain(slotCount);
            bool CheckBridge(string? locName)
            {
                if (string.IsNullOrEmpty(locName) || !locName.StartsWith(HostPlazaLayout.BridgePrefix, StringComparison.OrdinalIgnoreCase))
                    return false;
                return !chain.Any(l => string.Equals(l.Name, locName, StringComparison.OrdinalIgnoreCase));
            }

            if (CheckBridge(who.disconnectLocation.Value))
            {
                context?.Monitor.Log($"[FarmSlotModule] El jugador {who.Name} tenía disconnectLocation en puente '{who.disconnectLocation.Value}' no activo. Redirigiendo a 'Farm'.", LogLevel.Warn);
                who.disconnectLocation.Value = HostPlazaLayout.HostFarm;
                who.disconnectPosition.Value = new Vector2(64f * 64f, 15f * 64f);
            }
            if (CheckBridge(who.lastSleepLocation.Value))
            {
                context?.Monitor.Log($"[FarmSlotModule] El jugador {who.Name} tenía lastSleepLocation en puente '{who.lastSleepLocation.Value}' no activo. Redirigiendo a 'Farm'.", LogLevel.Warn);
                who.lastSleepLocation.Value = HostPlazaLayout.HostFarm;
            }
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            welcomeNotified = false;
            pendingResidenceWakeSlotId = null;
            RefreshSlotLocationData();
        }

        /// <summary>
        /// Reconstruye Data/Locations si cambió el tipo de alguna residencia o se adquirió un anexo:
        /// cada hueco y anexo toma peces, forraje y mapa del estado vigente al pedirse el asset.
        /// </summary>
        public static void RefreshSlotLocationData()
        {
            if (context == null) return;

            int slotCount = context.Config.FarmSlotCount;
            int bridges = HostPlazaLayout.BridgeCount(slotCount);
            string doorSig = string.Join(";", context.WorldState.Doors.Values
                .Select(d => $"{d.FarmSlotId}={d.FarmType}")
                .OrderBy(entry => entry, StringComparer.Ordinal));
            string annexSig = string.Join(";", (context.WorldState.Annexes?.Values ?? Enumerable.Empty<AnnexAssignment>())
                .Select(a => $"{a.AnnexId}={a.TemplateId}:{a.Side}")
                .OrderBy(entry => entry, StringComparer.Ordinal));
            string signature = $"{slotCount}:{bridges}|{doorSig}|{annexSig}";
            if (signature == slotDataSignature) return;

            slotDataSignature = signature;
            context.Helper.GameContent.InvalidateCache("Data/Locations");
            context.Helper.GameContent.InvalidateCache("Maps/Pelican4Ever_HostFarm");
            for (int i = 1; i <= HostPlazaLayout.MaxBridges; i++)
            {
                context.Helper.GameContent.InvalidateCache($"Maps/{HostPlazaLayout.BridgeName(i)}");
            }
        }

        /// <summary>
        /// Clona la lista de peces para un anexo 40×32 eliminando restricciones de coordenadas del mapa 80×65.
        /// </summary>
        private static List<SpawnFishData>? CloneAnnexFishData(List<SpawnFishData>? source)
        {
            if (source == null)
                return null;

            var list = new List<SpawnFishData>(source.Count);
            foreach (SpawnFishData item in source)
            {
                list.Add(new SpawnFishData
                {
                    Id = item.Id,
                    ItemId = item.ItemId,
                    RandomItemId = item.RandomItemId,
                    MaxItems = item.MaxItems,
                    MinStack = item.MinStack,
                    MaxStack = item.MaxStack,
                    Quality = item.Quality,
                    ToolUpgradeLevel = item.ToolUpgradeLevel,
                    IsRecipe = item.IsRecipe,
                    StackModifiers = item.StackModifiers,
                    StackModifierMode = item.StackModifierMode,
                    QualityModifiers = item.QualityModifiers,
                    QualityModifierMode = item.QualityModifierMode,
                    ModData = item.ModData,
                    PerItemCondition = item.PerItemCondition,
                    Condition = item.Condition,
                    Chance = item.Chance,
                    Season = item.Season,
                    FishAreaId = item.FishAreaId,
                    BobberPosition = null,
                    PlayerPosition = null,
                    MinFishingLevel = item.MinFishingLevel,
                    MinDistanceFromShore = item.MinDistanceFromShore,
                    MaxDistanceFromShore = item.MaxDistanceFromShore,
                    ApplyDailyLuck = item.ApplyDailyLuck,
                    CuriosityLureBuff = item.CuriosityLureBuff,
                    SpecificBaitBuff = item.SpecificBaitBuff,
                    SpecificBaitMultiplier = item.SpecificBaitMultiplier,
                    CatchLimit = item.CatchLimit,
                    CanUseTrainingRod = item.CanUseTrainingRod,
                    IsBossFish = item.IsBossFish,
                    SetFlagOnCatch = item.SetFlagOnCatch,
                    RequireMagicBait = item.RequireMagicBait,
                    Precedence = item.Precedence,
                    IgnoreFishDataRequirements = item.IgnoreFishDataRequirements,
                    CanBeInherited = item.CanBeInherited,
                    ChanceModifiers = item.ChanceModifiers,
                    ChanceModifierMode = item.ChanceModifierMode,
                    ChanceBoostPerLuckLevel = item.ChanceBoostPerLuckLevel,
                    UseFishCaughtSeededRandom = item.UseFishCaughtSeededRandom
                });
            }
            return list;
        }

        private static Dictionary<string, FishAreaData>? CloneAnnexFishAreaData(Dictionary<string, FishAreaData>? source)
        {
            if (source == null)
                return null;

            var dict = new Dictionary<string, FishAreaData>(source.Count);
            foreach (var kvp in source)
            {
                dict[kvp.Key] = new FishAreaData
                {
                    DisplayName = kvp.Value.DisplayName,
                    Position = null,
                    CrabPotFishTypes = kvp.Value.CrabPotFishTypes,
                    CrabPotJunkChance = kvp.Value.CrabPotJunkChance
                };
            }
            return dict;
        }

        /// <summary>
        /// Asegura en el host todas las ubicaciones de anexos adquiridos y limpia cualquier anexo liberado (TASK-903).
        /// </summary>
        public static void EnsureAllAnnexLocations()
        {
            if (!Context.IsMainPlayer || context == null || Game1.locations == null)
                return;

            if (context.WorldState.Annexes != null)
            {
                foreach (AnnexAssignment annex in context.WorldState.Annexes.Values)
                {
                    EnsureAnnexLocation(annex, regenerate: false);
                }
            }

            foreach (GameLocation loc in Game1.locations)
            {
                if (loc.NameOrUniqueName.StartsWith(LocationCullingLogic.AnnexPrefix, StringComparison.OrdinalIgnoreCase)
                    && context.WorldState.FindAnnexById(loc.NameOrUniqueName) == null)
                {
                    loc.ClearOwnership();
                }
            }
        }

        /// <summary>
        /// Crea o actualiza en el host la ubicación <see cref="Farm"/> de un anexo privado (TASK-903),
        /// aplicando su mapa 40×32, sus propiedades de bonificación y la política de acceso heredada de la residencia.
        /// </summary>
        public static Farm? EnsureAnnexLocation(AnnexAssignment annex, bool regenerate)
        {
            if (!Context.IsMainPlayer || context == null || annex == null || string.IsNullOrWhiteSpace(annex.AnnexId))
                return null;

            AnnexTemplate template = AnnexTemplateCatalog.GetTemplate(annex.TemplateId);
            string mapAsset = template.GetMapAssetForSide(annex.Side);

            Farm? annexFarm = Game1.getLocationFromName(annex.AnnexId) as Farm;
            bool newlyCreated = false;
            if (annexFarm == null)
            {
                annexFarm = new Farm(mapAsset, annex.AnnexId);
                Game1.locations.Add(annexFarm);
                newlyCreated = true;
            }
            else if (!string.Equals(annexFarm.mapPath.Value?.Replace('\\', '/'), mapAsset.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                annexFarm.mapPath.Value = mapAsset;
                annexFarm.reloadMap();
            }

            // Eliminar cualquier edificio por defecto que nunca deba existir en un anexo 40×32
            for (int i = annexFarm.buildings.Count - 1; i >= 0; i--)
            {
                string bType = annexFarm.buildings[i].buildingType.Value ?? string.Empty;
                if (bType.Equals("Farmhouse", StringComparison.OrdinalIgnoreCase)
                    || bType.Equals("Greenhouse", StringComparison.OrdinalIgnoreCase)
                    || bType.Equals("Shipping Bin", StringComparison.OrdinalIgnoreCase)
                    || bType.Equals("Pet Bowl", StringComparison.OrdinalIgnoreCase)
                    || bType.EndsWith("Cabin", StringComparison.OrdinalIgnoreCase))
                {
                    DeleteUnclaimedFarmhandOf(annexFarm.buildings[i]);
                    annexFarm.buildings.RemoveAt(i);
                }
            }

            FarmTypeModule.ApplyAnnexMapProperties(annexFarm, template.Id);
            FishLocationOverrideField?.SetValue(annexFarm, null);
            FishChanceOverrideField?.SetValue(annexFarm, 0f);

            if (regenerate || newlyCreated)
            {
                if (regenerate)
                {
                    annexFarm.objects.Clear();
                    annexFarm.terrainFeatures.Clear();
                    annexFarm.largeTerrainFeatures.Clear();
                    annexFarm.resourceClumps.Clear();
                    annexFarm.loadObjects();
                }
                FarmTypeModule.SpawnInitialOres(annexFarm, template.Id);
            }

            // Despejar siempre el corredor de la única entrada para que no haya rocas, maleza ni tocones bloqueando el paso
            MapCloneHelper.GetEntranceOpeningAndCorridor(annex.Side, template.Width, template.Height, out var openingBorderTiles, out var corridorTiles);
            foreach (var (cx, cy) in corridorTiles)
            {
                annexFarm.removeEverythingFromThisTile(cx, cy);
                for (int i = annexFarm.resourceClumps.Count - 1; i >= 0; i--)
                {
                    if (IsClumpOverTile(annexFarm.resourceClumps[i], cx, cy))
                        annexFarm.resourceClumps.RemoveAt(i);
                }
            }

            // Sincronizar warps de retorno hacia la residencia madre en la apertura del anexo (TASK-904)
            DoorAssignment? parentResidence = context.WorldState.Doors.Values
                .FirstOrDefault(d => string.Equals(d.FarmSlotId, annex.ResidenceSlotId, StringComparison.OrdinalIgnoreCase));
            var warpPoints = AnnexEntrance.For(annex.Side, template.Width, template.Height, parentResidence?.FarmType);
            foreach (var (bx, by) in openingBorderTiles)
            {
                UpdateOrCreateWarp(annexFarm, bx, by, annex.ResidenceSlotId, warpPoints.ResidenceReturnTile.X, warpPoints.ResidenceReturnTile.Y);
            }

            AccessPolicy policy = parentResidence?.Policy ?? AccessPolicy.Locked;
            long ownerId = parentResidence?.OwnerId ?? annex.OwnerId;
            annex.OwnerId = ownerId;

            annexFarm.PublishOwnership(ownerId, policy);
            return annexFarm;
        }

        private void OnPlayerWarped(object? sender, WarpedEventArgs e)
        {
            if (!e.IsLocalPlayer || context == null)
                return;

            if (e.NewLocation != null && HostPlazaLayout.IsPlazaLocation(e.NewLocation.NameOrUniqueName))
            {
                if (e.NewLocation is Farm hostFarm && string.Equals(hostFarm.NameOrUniqueName, "Farm", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(hostFarm.mapPath.Value, "Maps\\Pelican4Ever_HostFarm", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(hostFarm.mapPath.Value, "Maps/Pelican4Ever_HostFarm", StringComparison.OrdinalIgnoreCase))
                    {
                        hostFarm.mapPath.Value = "Maps\\Pelican4Ever_HostFarm";
                        hostFarm.reloadMap();
                    }

                    RemoveHostCabins(hostFarm);

                    if (!welcomeNotified)
                    {
                        bool hasResidence = context.WorldState.Doors.Values.Any(d => d.OwnerId == Game1.player.UniqueMultiplayerID);
                        if (!hasResidence)
                        {
                            welcomeNotified = true;
                            Game1.addHUDMessage(new HUDMessage("¡Bienvenido a la Plaza Central! Camina hacia un portal libre en la plaza o cruza el paso del oeste hacia los puentes (o escribe /residencias) para fundar tu granja.", HUDMessage.newQuest_type));
                        }
                    }
                }

                EnsureLocationPortals(e.NewLocation);
            }

            if (e.NewLocation is Farm farm && farm.NameOrUniqueName.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var assignment = context.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == farm.NameOrUniqueName);
                EnsureFarmSlotBuildings(farm, assignment);
            }

            if (e.NewLocation is Cabin cabin && cabin.NameOrUniqueName.EndsWith("_Home", StringComparison.OrdinalIgnoreCase))
            {
                CleanAndArrangeResidenceInterior(cabin);
            }
        }

        public static void RemoveHostCabins(Farm farm)
        {
            if (!Context.IsMainPlayer || farm == null) return;

            if (Game1.netWorldState?.Value != null)
            {
                int targetCount = context?.Config.FarmSlotCount ?? 16;
                Game1.netWorldState.Value.highestPlayerLimit.Value = Math.Max(Game1.netWorldState.Value.highestPlayerLimit.Value, targetCount);
                Game1.netWorldState.Value.currentPlayerLimit.Value = Math.Max(Game1.netWorldState.Value.currentPlayerLimit.Value, targetCount);
            }

            int removed = 0;
            for (int i = farm.buildings.Count - 1; i >= 0; i--)
            {
                Building b = farm.buildings[i];
                if (b.GetIndoors() is Cabin || b.buildingType.Value.EndsWith("Cabin", StringComparison.OrdinalIgnoreCase))
                {
                    DeleteUnclaimedFarmhandOf(b);
                    farm.buildings.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0)
            {
                context?.Monitor.Log($"[FarmSlotModule] Se eliminaron {removed} cabañas redundantes de la granja anfitriona (Plaza Central).", LogLevel.Info);
                RehomeOrphanFarmhands();
            }
        }

        /// <summary>Borra el peón sin reclamar de una cabaña que se va a quitar, para que no quede huérfano en farmhandData.</summary>
        private static void DeleteUnclaimedFarmhandOf(Building building)
        {
            if (Context.IsMainPlayer && building.GetIndoors() is Cabin cabin && cabin.HasOwner && cabin.owner.isUnclaimedFarmhand)
            {
                cabin.DeleteFarmhand();
            }
        }

        /// <summary>
        /// Resuelve en el host los peones cuyo hogar ya no es una cabaña: borra los no reclamados y reubica los reales.
        /// Si quedan huérfanos, GameServer.IsFarmhandAvailable los reubica mientras construye la lista de peones,
        /// borrando el peón de una cabaña que ya iba en esa lista; al elegirlo, checkFarmhandRequest lanza
        /// KeyNotFoundException y el cliente se queda en negro.
        /// </summary>
        public static void RehomeOrphanFarmhands()
        {
            if (!Context.IsMainPlayer || Game1.netWorldState?.Value == null) return;

            foreach (Farmer farmhand in Game1.netWorldState.Value.farmhandData.Values.ToList())
            {
                if (Game1.getLocationFromName(farmhand.homeLocation.Value) is Cabin)
                    continue;

                if (farmhand.isUnclaimedFarmhand)
                {
                    Game1.player.team.DeleteFarmhand(farmhand);
                    context?.Monitor.Log($"[FarmSlotModule] Peón sin reclamar {farmhand.UniqueMultiplayerID} sin cabaña: eliminado.", LogLevel.Trace);
                }
                else if (!Game1.netWorldState.Value.TryAssignFarmhandHome(farmhand))
                {
                    context?.Monitor.Log($"[FarmSlotModule] El granjero {farmhand.Name} ({farmhand.UniqueMultiplayerID}) no tiene cabaña libre a la que mudarse.", LogLevel.Warn);
                }
            }
        }

        private static int unclaimedCheckCooldown;
        private static readonly PortalPromptGate portalPromptGate = new();

        private void OnPendingResidenceWakeTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (pendingResidenceWakeSlotId == null || !Context.IsWorldReady || Game1.player == null)
                return;

            // Esperar a que acabe el cambio de día (menús de envío, fundidos, sincronización)
            if (Game1.activeClickableMenu != null || Game1.isWarping || Game1.eventUp || Game1.globalFade
                || Game1.fadeToBlack || Game1.newDaySync.hasInstance())
                return;

            string slotId = pendingResidenceWakeSlotId;
            pendingResidenceWakeSlotId = null;

            // Verificación estricta de propiedad: nunca viajar si el jugador no es dueño de la residencia
            var assignment = context?.WorldState?.Doors?.Values.FirstOrDefault(d => d.OwnerId == Game1.player.UniqueMultiplayerID);
            if (assignment == null || !string.Equals(assignment.FarmSlotId, slotId, StringComparison.OrdinalIgnoreCase))
            {
                context?.Monitor.Log($"[FarmSlotModule] Cancelado viaje diferido: el jugador {Game1.player.Name} ({Game1.player.UniqueMultiplayerID}) no posee la residencia {slotId}.", LogLevel.Warn);
                return;
            }

            string home = $"{slotId}_Home";
            if (string.Equals(Game1.currentLocation?.NameOrUniqueName, home, StringComparison.OrdinalIgnoreCase))
                return;

            context?.Monitor.Log($"[FarmSlotModule] Residencia {home} no cargada al despertar; viajando a la cama.", LogLevel.Debug);
            Game1.warpFarmer(home, ResidenceInteriorLogic.BedPlacement.TargetX, ResidenceInteriorLogic.BedPlacement.TargetY, false);
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!e.IsMultipleOf(4) || !Context.IsWorldReady || context == null)
                return;

            if (unclaimedCheckCooldown > 0)
            {
                unclaimedCheckCooldown--;
                return;
            }

            var curLoc = Game1.currentLocation;
            if (curLoc == null || !HostPlazaLayout.IsPlazaLocation(curLoc.NameOrUniqueName) || Game1.player == null || Game1.isFestival())
            {
                if (curLoc == null || !HostPlazaLayout.IsPlazaLocation(curLoc.NameOrUniqueName))
                    portalPromptGate.Reset();
                return;
            }

            // Con un menú abierto (p. ej. el propio diálogo de fundación) no se evalúa nada: la puerta lógica conserva
            // el estado hasta que el jugador salga del tile.
            if (Game1.activeClickableMenu != null)
                return;

            Vector2 playerTile = Game1.player.Tile;
            DoorModel? door = RegisteredDoors.FirstOrDefault(d =>
            {
                if (!string.Equals(d.LocationName, curLoc.NameOrUniqueName, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!IsDoorActiveForCurrentConfig(d))
                    return false;
                var loc = HostPlazaLayout.Find(d.LocationName, context.Config.FarmSlotCount);
                return loc != null && SpatialWarpRouter.IsPortalTriggerTile(d.Edge, d.TownTileX, d.TownTileY, loc, (int)playerTile.X, (int)playerTile.Y);
            });

            string? unclaimedDoorId = door != null && !context.WorldState.Doors.ContainsKey(door.DoorId) ? door.DoorId : null;
            // Se evalúa SIEMPRE (también con null) para rearmar la puerta lógica al salir del tile.
            bool shouldPrompt = portalPromptGate.ShouldPrompt(unclaimedDoorId);
            if (door != null && shouldPrompt)
            {
                unclaimedCheckCooldown = 40; // Evitar disparos repetidos
                Game1.player.Halt();
                Game1.player.completelyStopAnimatingOrDoingAction();
                InteractWithResidentialDoor(door);
            }
        }

        /// <summary>
        /// Aprovisiona los portales físicos de 2 tiles y los senderos en el perímetro de toda la plaza central.
        /// </summary>
        public static void EnsureHostFarmPortals(Farm farm)
        {
            if (farm == null || context == null || farm.NameOrUniqueName != "Farm") return;

            EnsureAllPlazaPortals();
        }

        public static void EnsureAllPlazaPortals()
        {
            if (context == null) return;
            int slotCount = context.Config.FarmSlotCount;
            var chain = HostPlazaLayout.Chain(slotCount);
            foreach (var locDef in chain)
            {
                GameLocation? loc = Game1.getLocationFromName(locDef.Name);
                if (loc != null)
                {
                    EnsureLocationPortals(loc);
                }
            }
        }

        private static void CleanUpHostFarmLegacy(Farm farmLoc)
        {
            foreach (Point legacy in LegacySignTiles)
                RemoveLegacySign(farmLoc, legacy.ToVector2());

            var petBowl = farmLoc.buildings.FirstOrDefault(b =>
                (b.buildingType.Value == "Pet Bowl" || b.GetType().Name == "PetBowl")
                && b.tileX.Value >= 52 && b.tileX.Value <= 55 && b.tileY.Value <= 10);
            if (petBowl != null)
            {
                petBowl.tileX.Value = 56;
                petBowl.tileY.Value = 8;
            }
        }

        public static void EnsureLocationPortals(GameLocation location)
        {
            var ctx = context;
            if (location == null || ctx == null) return;

            string locName = location.NameOrUniqueName;
            if (!HostPlazaLayout.IsPlazaLocation(locName)) return;

            var hostMap = location.Map;
            if (hostMap != null)
            {
                if (hostMap.GetTileSheet(MailboxTileSheetId) == null)
                {
                    var townMap = ctx.Helper.GameContent.Load<xTile.Map>("Maps/Town");
                    if (townMap != null)
                        AddMailboxAssets(hostMap, townMap);
                }
                AddPathAssets(hostMap);
            }

            var bldLayer = hostMap?.GetLayer("Buildings");
            var frontLayer = hostMap?.GetLayer("Front");
            var alwaysFrontLayer = hostMap?.GetLayer("AlwaysFront");
            var backLayer = hostMap?.GetLayer("Back");

            xTile.Tiles.Tile? sampleTile = backLayer?.Tiles[20, 20] ?? backLayer?.Tiles[5, 5];

            // 1. Limpieza de elementos heredados exclusiva de la Granja Host
            if (location is Farm farmLoc && string.Equals(locName, HostPlazaLayout.HostFarm, StringComparison.OrdinalIgnoreCase))
            {
                CleanUpHostFarmLegacy(farmLoc);
            }

            int slotCount = ctx.Config.FarmSlotCount;
            var plazaLoc = HostPlazaLayout.Find(locName, slotCount);

            // 2. Pasos Oeste y Este (limpieza de escombros y warps continuos)
            if (plazaLoc?.West is { } westPassage)
            {
                if (string.Equals(locName, HostPlazaLayout.HostFarm, StringComparison.OrdinalIgnoreCase))
                {
                    var sheetOutdoors = MapCloneHelper.GetOutdoorsTileSheet(hostMap) ?? sampleTile?.TileSheet;
                    ApplyFarmWestPassageArtisanFinishes(hostMap, westPassage, sheetOutdoors);
                }

                int maxWestX = string.Equals(locName, HostPlazaLayout.HostFarm, StringComparison.OrdinalIgnoreCase) ? 7 : 5;
                for (int x = 0; x <= maxWestX; x++)
                {
                    for (int y = westPassage.FirstRow; y < westPassage.FirstRow + westPassage.RowCount; y++)
                    {
                        location.removeEverythingFromThisTile(x, y);
                        location.removeTileProperty(x, y, "Back", "Passable");
                        if (location is Farm f)
                        {
                            for (int i = f.resourceClumps.Count - 1; i >= 0; i--)
                            {
                                if (IsClumpOverTile(f.resourceClumps[i], x, y))
                                    f.resourceClumps.RemoveAt(i);
                            }
                        }
                    }
                }

                for (int y = westPassage.FirstRow; y < westPassage.FirstRow + westPassage.RowCount; y++)
                {
                    UpdateOrCreateWarp(location, 0, y, westPassage.TargetLocation, westPassage.ArrivalX, westPassage.ArrivalY);
                }

                // Despejar casilla de llegada en la ubicación destino si está cargada
                GameLocation? targetLoc = Game1.getLocationFromName(westPassage.TargetLocation);
                targetLoc?.removeEverythingFromThisTile(westPassage.ArrivalX, westPassage.ArrivalY);
                targetLoc?.removeTileProperty(westPassage.ArrivalX, westPassage.ArrivalY, "Back", "Passable");
            }
            else if (string.Equals(locName, HostPlazaLayout.HostFarm, StringComparison.OrdinalIgnoreCase))
            {
                // Si la Granja Host ya no tiene paso oeste (slotCount <= 4), retirar warps de (0, 26..27)
                RemoveWarpAt(location, 0, 26);
                RemoveWarpAt(location, 0, 27);
            }

            if (plazaLoc?.East is { } eastPassage)
            {
                int mapWidth = hostMap?.Layers.Count > 0 ? hostMap.Layers[0].LayerWidth : plazaLoc.Width;
                for (int x = mapWidth - 6; x < mapWidth; x++)
                {
                    for (int y = eastPassage.FirstRow; y < eastPassage.FirstRow + eastPassage.RowCount; y++)
                    {
                        location.removeEverythingFromThisTile(x, y);
                        location.removeTileProperty(x, y, "Back", "Passable");
                        if (location is Farm f)
                        {
                            for (int i = f.resourceClumps.Count - 1; i >= 0; i--)
                            {
                                if (IsClumpOverTile(f.resourceClumps[i], x, y))
                                    f.resourceClumps.RemoveAt(i);
                            }
                        }
                    }
                }

                for (int y = eastPassage.FirstRow; y < eastPassage.FirstRow + eastPassage.RowCount; y++)
                {
                    UpdateOrCreateWarp(location, mapWidth - 1, y, eastPassage.TargetLocation, eastPassage.ArrivalX, eastPassage.ArrivalY);
                }

                // Despejar casilla de llegada en la ubicación destino si está cargada
                GameLocation? targetLoc = Game1.getLocationFromName(eastPassage.TargetLocation);
                targetLoc?.removeEverythingFromThisTile(eastPassage.ArrivalX, eastPassage.ArrivalY);
                targetLoc?.removeTileProperty(eastPassage.ArrivalX, eastPassage.ArrivalY, "Back", "Passable");
            }

            // 3. Portales residenciales en esta ubicación
            foreach (DoorModel door in RegisteredDoors)
            {
                if (!string.Equals(door.LocationName, locName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // A. Limpiar copas de árboles, ramas, troncos y suelo en el mapa del runtime
                var passTiles = ClearDoorAreaOnMap(bldLayer, frontLayer, alwaysFrontLayer, backLayer, door, sampleTile);

                var (t1, t2) = GetOpeningTiles(door);

                // B. Limpiar objetos, escombros, tocones, piedras y propiedades Passable a lo largo de todo el paso
                foreach (var pt in passTiles)
                {
                    location.removeEverythingFromThisTile(pt.X, pt.Y);
                    location.removeTileProperty(pt.X, pt.Y, "Back", "Passable");

                    if (location is Farm farmRef)
                    {
                        for (int i = farmRef.resourceClumps.Count - 1; i >= 0; i--)
                        {
                            var clump = farmRef.resourceClumps[i];
                            if (IsClumpOverTile(clump, pt.X, pt.Y))
                            {
                                farmRef.resourceClumps.RemoveAt(i);
                            }
                        }
                    }
                }

                // C. Margen despejado de 3×4 casillas al final interior del camino (TASK-1302 / TASK-1401)
                if (plazaLoc != null)
                {
                    var clearanceTiles = PlazaTopologyVerifier.GetInteriorClearanceTiles(door.Edge, door.TownTileX, door.TownTileY, plazaLoc);
                    foreach (var (ix, iy) in clearanceTiles)
                    {
                        location.removeEverythingFromThisTile(ix, iy);
                        location.removeTileProperty(ix, iy, "Back", "Passable");
                        if (location is Farm farmRef)
                        {
                            for (int i = farmRef.resourceClumps.Count - 1; i >= 0; i--)
                            {
                                if (IsClumpOverTile(farmRef.resourceClumps[i], ix, iy))
                                    farmRef.resourceClumps.RemoveAt(i);
                            }
                        }
                    }
                }

                // D. El buzón es de tiles del mapa (PlaceMailboxTiles): despejar su casilla
                var (mailboxX, mailboxY) = TownMailboxLogic.GetMailboxCoords(door.TownTileX, door.TownTileY, door.MailboxTileX, door.MailboxTileY);
                location.removeEverythingFromThisTile(mailboxX, mailboxY);
                location.removeEverythingFromThisTile(mailboxX, mailboxY - 1);

                // E. Sincronizar Warps continuos en los 4 tiles de disparo (apertura y borde exterior)
                bool isClaimed = ctx.WorldState.Doors.TryGetValue(door.DoorId, out DoorAssignment? assignment);
                int mapH = hostMap?.Layers.Count > 0 ? hostMap.Layers[0].LayerHeight : (plazaLoc?.Height ?? 65);
                int outerBorderY = door.Edge == PortalEdge.North ? 0 : mapH - 1;

                if (isClaimed && assignment != null)
                {
                    GetResidenceArrivalCoordinates(door, assignment.FarmType, out int targetX, out int targetY, out _);

                    UpdateOrCreateWarp(location, t1.X, t1.Y, assignment.FarmSlotId, targetX, targetY);
                    UpdateOrCreateWarp(location, t2.X, t2.Y, assignment.FarmSlotId, targetX, targetY);
                    UpdateOrCreateWarp(location, door.TownTileX, outerBorderY, assignment.FarmSlotId, targetX, targetY);
                    UpdateOrCreateWarp(location, door.TownTileX + 1, outerBorderY, assignment.FarmSlotId, targetX, targetY);
                }
                else
                {
                    RemoveWarpAt(location, t1.X, t1.Y);
                    RemoveWarpAt(location, t2.X, t2.Y);
                    RemoveWarpAt(location, door.TownTileX, outerBorderY);
                    RemoveWarpAt(location, door.TownTileX + 1, outerBorderY);
                }
            }
        }

        private static bool IsClumpOverTile(StardewValley.TerrainFeatures.ResourceClump clump, int x, int y)
        {
            return x >= (int)clump.Tile.X && x < (int)clump.Tile.X + clump.width.Value
                && y >= (int)clump.Tile.Y && y < (int)clump.Tile.Y + clump.height.Value;
        }

        private static void UpdateOrCreateWarp(GameLocation farm, int x, int y, string targetLocation, int targetX, int targetY)
        {
            Warp? existingWarp = farm.warps.FirstOrDefault(w => w.X == x && w.Y == y);
            if (existingWarp == null)
            {
                farm.warps.Add(new Warp(x, y, targetLocation, targetX, targetY, false));
            }
            else
            {
                existingWarp.TargetName = targetLocation;
                existingWarp.TargetX = targetX;
                existingWarp.TargetY = targetY;
            }
        }

        private static void RemoveWarpAt(GameLocation farm, int x, int y)
        {
            for (int i = farm.warps.Count - 1; i >= 0; i--)
            {
                if (farm.warps[i].X == x && farm.warps[i].Y == y)
                {
                    farm.warps.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Aplica al hueco el mapa de la plantilla elegida. Data/Locations se edita al lanzar el juego, antes de
        /// cargar el estado, así que todos los huecos nacen con el mapa estándar y hay que corregirlo en el host
        /// (los clientes recargan el mapa al cambiar <c>mapPath</c>). Con <paramref name="regenerate"/> (hueco recién
        /// reclamado) recoloca los edificios de inicio y regenera la vegetación según el mapa nuevo.
        /// Devuelve true si cambió el mapa.
        /// </summary>
        public static bool ApplyFarmTemplate(Farm farm, string? farmType, bool regenerate, long ownerId = 0)
        {
            if (!Context.IsMainPlayer) return false;

            FarmTemplate template = FarmTemplateCatalog.GetTemplate(farmType);
            if (string.Equals(farm.mapPath.Value?.Replace('/', '\\'), template.MapPath.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase))
                return false;

            farm.mapPath.Value = template.MapPath;
            farm.reloadMap();
            FarmTypeModule.ApplyMapProperties(farm, template.Id);
            farm.mapShippingBinPosition = null;
            farm.mainFarmhouseEntry = null;
            // Farm.getFish cachea la propiedad FarmFishLocationOverride del mapa en el primer lance
            FishLocationOverrideField?.SetValue(farm, null);
            FishChanceOverrideField?.SetValue(farm, 0f);

            if (regenerate)
            {
                // GetMainFarmHouseEntry toma la posición del edificio existente; la del mapa nuevo sale de su propiedad
                Point entry = farm.TryGetMapPropertyAs("FarmHouseEntry", out Point parsed) ? parsed : new Point(64, 15);
                MoveBuilding(farm, "Farmhouse", new Vector2(entry.X - 5, entry.Y - 3));
                MoveBuilding(farm, "Greenhouse", farm.GetGreenhouseStartLocation());
                MoveBuilding(farm, "Shipping Bin", farm.GetStarterShippingBinLocation());
                MoveBuilding(farm, "Pet Bowl", farm.GetStarterPetBowlLocation());
                farm.mainFarmhouseEntry = null;

                farm.objects.Clear();
                farm.terrainFeatures.Clear();
                farm.largeTerrainFeatures.Clear();
                farm.resourceClumps.Clear();
                farm.loadObjects();
                FarmTypeModule.SpawnInitialOres(farm, template.Id);
                FarmTypeModule.SpawnInitialMeadowlandsSetup(farm, template.Id, ownerId);
            }
            else
            {
                // Hueco ya jugado con otro mapa (reclamos anteriores a este arreglo): se conserva lo que haya,
                // salvo lo que el mapa nuevo deja en el agua o dentro de un muro
                bool Blocked(Vector2 tile) => farm.isWaterTile((int)tile.X, (int)tile.Y)
                    || farm.getTileIndexAt((int)tile.X, (int)tile.Y, "Buildings") != -1;

                foreach (Vector2 tile in farm.objects.Keys.Where(Blocked).ToList())
                    farm.objects.Remove(tile);
                foreach (Vector2 tile in farm.terrainFeatures.Keys.Where(Blocked).ToList())
                    farm.terrainFeatures.Remove(tile);
                for (int i = farm.largeTerrainFeatures.Count - 1; i >= 0; i--)
                {
                    if (Blocked(farm.largeTerrainFeatures[i].Tile))
                        farm.largeTerrainFeatures.RemoveAt(i);
                }
                for (int i = farm.resourceClumps.Count - 1; i >= 0; i--)
                {
                    if (Blocked(farm.resourceClumps[i].Tile))
                        farm.resourceClumps.RemoveAt(i);
                }
            }

            farm.updateLayout();
            context?.Monitor.Log($"[FarmSlotModule] {farm.NameOrUniqueName} usa ahora el mapa {template.MapPath} ({template.Id}).", LogLevel.Info);
            return true;
        }

        private static readonly System.Reflection.FieldInfo? FishLocationOverrideField = AccessTools.Field(typeof(Farm), "_fishLocationOverride");
        private static readonly System.Reflection.FieldInfo? FishChanceOverrideField = AccessTools.Field(typeof(Farm), "_fishChanceOverride");

        private static void MoveBuilding(Farm farm, string buildingType, Vector2 tile)
        {
            Building? building = farm.getBuildingByType(buildingType);
            if (building == null) return;

            building.tileX.Value = (int)tile.X;
            building.tileY.Value = (int)tile.Y;
            building.updateInteriorWarps();
        }

        /// <summary>
        /// Asegura que la ubicación de residencia instanciada (Pelican4Ever_Farm_XX) exista en Game1.locations.
        /// Si no está presente (por ej. partidas guardadas previas, creación dinámica o fallos de CreateOnLoad),
        /// la crea de inmediato con su plantilla asignada, propiedades de mapa, cueva, bodega e invernadero.
        /// </summary>
        public static Farm? EnsureFarmSlotExists(string slotId)
        {
            if (string.IsNullOrWhiteSpace(slotId))
                return null;

            if (Game1.getLocationFromName(slotId) is Farm existingFarm)
                return existingFarm;

            if (!Context.IsMainPlayer || Game1.locations == null)
                return null;

            string farmType = "Standard";
            DoorAssignment? assignment = context?.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == slotId);
            if (assignment != null && !string.IsNullOrEmpty(assignment.FarmType))
            {
                farmType = assignment.FarmType;
            }

            var template = FarmTemplateCatalog.GetTemplate(farmType);
            var farm = new Farm(template.MapPath, slotId);
            farm.isAlwaysActive.Value = true;
            Game1.locations.Add(farm);
            FarmTypeModule.ApplyMapProperties(farm, template.Id);

            context?.Monitor.Log($"[FarmSlotModule] Ubicación de residencia {slotId} ({template.MapPath}) instanciada e inyectada dinámicamente en Game1.locations.", LogLevel.Info);

            EnsureFarmSlotBuildings(farm, assignment);
            return farm;
        }

        /// <summary>
        /// Asegura que la granja instanciada tenga eliminadas las cabañas automáticas no deseadas,
        /// su edificio Farmhouse principal con interior tipo Cabin dedicado, y warps de entrada/salida conectados.
        /// </summary>
        public static void EnsureFarmSlotBuildings(Farm farm, DoorAssignment? assignment)
        {
            if (!Context.IsMainPlayer || farm == null) return;

            // 1. Localizar o asegurar el edificio Farmhouse principal
            Building? farmhouse = farm.getBuildingByType("Farmhouse")
                               ?? farm.buildings.FirstOrDefault(b => b.buildingType.Value.Contains("Farmhouse", StringComparison.OrdinalIgnoreCase));

            Point entryDoor = farm.GetMainFarmHouseEntry();
            Vector2 starterTile = farm.GetStarterFarmhouseLocation();

            if (farmhouse == null)
            {
                farmhouse = new Building("Farmhouse", starterTile);
                farmhouse.load();
                farm.buildings.Add(farmhouse);
            }

            // 2. Quitar cualquier cabaña secundaria generada automáticamente por Stardew Valley
            for (int i = farm.buildings.Count - 1; i >= 0; i--)
            {
                Building b = farm.buildings[i];
                if (b != farmhouse && (b.GetIndoors() is Cabin || b.buildingType.Value.EndsWith("Cabin", StringComparison.OrdinalIgnoreCase)))
                {
                    DeleteUnclaimedFarmhandOf(b);
                    farm.buildings.RemoveAt(i);
                }
            }

            // 3. Asegurar interior tipo Cabin para la Farmhouse de este slot
            string cabinName = $"{farm.NameOrUniqueName}_Home";
            Cabin? cabin = farmhouse.GetIndoors() as Cabin;

            if (cabin == null)
            {
                cabin = new Cabin("Maps\\FarmHouse");
                cabin.uniqueName.Value = cabinName;
                cabin.IsFarm = true;
                farmhouse.indoors.Value = cabin;
                farmhouse.nonInstancedIndoorsName.Value = null;
            }

            if (!cabin.HasOwner)
            {
                cabin.CreateFarmhand();
            }

            // No registrar la Cabin en Game1.locations: es interior de un edificio de la granja y el juego ya la
            // resuelve con Game1.getLocationFromName → findStructure. Duplicarla la serializaría dos veces y
            // Utility.ForEachLocation la recorrería dos veces (DayUpdate doble).
            if (Game1.locations != null && Game1.locations.Contains(cabin))
            {
                Game1.locations.Remove(cabin);
            }

            // Distribuir de forma óptima el mobiliario interior para evitar que quede apelotonado contra la pared norte
            CleanAndArrangeResidenceInterior(cabin);

            // Despejar puntos de llegada cardinales para que nunca haya ramas, maleza o escombros bloqueando el warp
            var arrivalTemplate = FarmTemplateCatalog.GetTemplate(assignment?.FarmType);
            farm.removeEverythingFromThisTile(arrivalTemplate.NorthArrivalTileX, arrivalTemplate.NorthArrivalTileY);
            farm.removeEverythingFromThisTile(arrivalTemplate.SouthArrivalTileX, arrivalTemplate.SouthArrivalTileY);
            farm.removeEverythingFromThisTile(arrivalTemplate.EastArrivalTileX, arrivalTemplate.EastArrivalTileY);

            // Rescate de seguridad: si un jugador quedó atrapado en el acantilado inaccesible (40, 1) de Praderas
            if (FarmGrassLogic.IsMeadowlands(assignment?.FarmType))
            {
                foreach (var farmer in farm.farmers)
                {
                    if ((int)farmer.Tile.X == 40 && (int)farmer.Tile.Y == 1)
                    {
                        farmer.Position = new Vector2(arrivalTemplate.NorthArrivalTileX * 64, arrivalTemplate.NorthArrivalTileY * 64);
                    }
                }
            }

            // 4. Configurar warps de entrada y salida
            int doorX = entryDoor.X != 0 ? entryDoor.X : (farmhouse.tileX.Value + farmhouse.humanDoor.X);
            int doorY = entryDoor.Y != 0 ? entryDoor.Y : (farmhouse.tileY.Value + farmhouse.humanDoor.Y);

            if (cabin.warps.Count == 0)
            {
                cabin.warps.Add(new Warp(3, 11, farm.NameOrUniqueName, doorX, doorY + 1, false));
            }
            else
            {
                cabin.warps[0].TargetName = farm.NameOrUniqueName;
                cabin.warps[0].TargetX = doorX;
                cabin.warps[0].TargetY = doorY + 1;
            }
            farmhouse.updateInteriorWarps();

            // 5. Asignar dueño si existe
            if (assignment != null && assignment.OwnerId != 0)
            {
                Farmer? ownerFarmer = Game1.GetPlayer(assignment.OwnerId);
                if (ownerFarmer != null)
                {
                    // AssignFarmhand lanza si la cabaña ya pertenece a otro granjero real
                    if (cabin.HasOwner && cabin.OwnerId != ownerFarmer.UniqueMultiplayerID && !cabin.owner.isUnclaimedFarmhand)
                    {
                        context?.Monitor.Log($"[FarmSlotModule] La cabaña de {farm.NameOrUniqueName} pertenece a {cabin.OwnerId}; no se reasigna a {ownerFarmer.UniqueMultiplayerID}.", LogLevel.Warn);
                    }
                    else
                    {
                        cabin.AssignFarmhand(ownerFarmer);
                    }
                }
            }
            else if (cabin.HasOwner && !cabin.owner.isUnclaimedFarmhand
                && !string.Equals(cabin.owner.homeLocation.Value, cabin.NameOrUniqueName, StringComparison.OrdinalIgnoreCase))
            {
                // Hueco libre atado a un granjero que ya vive en otra residencia (reclamos duplicados antiguos):
                // se suelta la referencia sin borrar al granjero y se crea un peón nuevo para el hueco.
                cabin.farmhandReference.Value = null;
                cabin.CreateFarmhand();
            }

            // 6. Sincronizar warps perimetrales hacia anexos adquiridos (TASK-904)
            if (context != null && context.WorldState.Annexes != null)
            {
                var annexes = context.WorldState.GetAnnexesForResidence(farm.NameOrUniqueName);
                foreach (var annex in annexes)
                {
                    var template = AnnexTemplateCatalog.GetTemplate(annex.TemplateId);
                    var pts = AnnexEntrance.For(annex.Side, template.Width, template.Height, assignment?.FarmType);
                    switch (annex.Side)
                    {
                        case AnnexSide.North:
                            if (FarmGrassLogic.IsMeadowlands(assignment?.FarmType))
                            {
                                UpdateOrCreateWarp(farm, 63, 0, annex.AnnexId, pts.EntryTile.X, pts.EntryTile.Y);
                                UpdateOrCreateWarp(farm, 64, 0, annex.AnnexId, pts.EntryTile.X, pts.EntryTile.Y);
                            }
                            else
                            {
                                UpdateOrCreateWarp(farm, 39, 0, annex.AnnexId, pts.EntryTile.X, pts.EntryTile.Y);
                                UpdateOrCreateWarp(farm, 40, 0, annex.AnnexId, pts.EntryTile.X, pts.EntryTile.Y);
                                UpdateOrCreateWarp(farm, 41, 0, annex.AnnexId, pts.EntryTile.X, pts.EntryTile.Y);
                            }
                            break;
                        case AnnexSide.East:
                            if (FarmGrassLogic.IsMeadowlands(assignment?.FarmType))
                            {
                                UpdateOrCreateWarp(farm, 99, 21, annex.AnnexId, pts.EntryTile.X, pts.EntryTile.Y);
                                UpdateOrCreateWarp(farm, 99, 22, annex.AnnexId, pts.EntryTile.X, pts.EntryTile.Y);
                                UpdateOrCreateWarp(farm, 99, 23, annex.AnnexId, pts.EntryTile.X, pts.EntryTile.Y);
                            }
                            else
                            {
                                UpdateOrCreateWarp(farm, 79, 15, annex.AnnexId, pts.EntryTile.X, pts.EntryTile.Y);
                                UpdateOrCreateWarp(farm, 79, 16, annex.AnnexId, pts.EntryTile.X, pts.EntryTile.Y);
                                UpdateOrCreateWarp(farm, 79, 17, annex.AnnexId, pts.EntryTile.X, pts.EntryTile.Y);
                            }
                            break;
                        case AnnexSide.South:
                            if (FarmGrassLogic.IsMeadowlands(assignment?.FarmType))
                            {
                                UpdateOrCreateWarp(farm, 52, 74, annex.AnnexId, pts.EntryTile.X, pts.EntryTile.Y);
                                UpdateOrCreateWarp(farm, 53, 74, annex.AnnexId, pts.EntryTile.X, pts.EntryTile.Y);
                            }
                            else
                            {
                                UpdateOrCreateWarp(farm, 39, 64, annex.AnnexId, pts.EntryTile.X, pts.EntryTile.Y);
                                UpdateOrCreateWarp(farm, 40, 64, annex.AnnexId, pts.EntryTile.X, pts.EntryTile.Y);
                                UpdateOrCreateWarp(farm, 41, 64, annex.AnnexId, pts.EntryTile.X, pts.EntryTile.Y);
                            }
                            break;
                    }
                }
            }

            // 7. Configurar la Cueva privada de la residencia (FarmCave)
            string caveId = $"{farm.NameOrUniqueName}_Cave";
            var farmTemplate = FarmTemplateCatalog.GetTemplate(assignment?.FarmType);

            FarmCave? cave = Game1.locations?.FirstOrDefault(l => string.Equals(l.NameOrUniqueName, caveId, StringComparison.OrdinalIgnoreCase)) as FarmCave
                          ?? Game1.getLocationFromName(caveId) as FarmCave;
            if (cave == null && Context.IsMainPlayer)
            {
                cave = new FarmCave("Maps/FarmCave", caveId);
                cave.isAlwaysActive.Value = true;
                Game1.locations?.Add(cave);
            }

            if (cave != null)
            {
                if (assignment != null && assignment.OwnerId != 0)
                {
                    cave.PublishOwnership(assignment.OwnerId, assignment.Policy);
                }

                UpdateOrCreateWarp(farm, farmTemplate.CaveEntranceX, farmTemplate.CaveEntranceY, caveId, 8, 11);
                UpdateOrCreateWarp(cave, 8, 12, farm.NameOrUniqueName, farmTemplate.CaveExitX, farmTemplate.CaveExitY);

                int caveChoice = Game1.player.caveChoice.Value != 0 ? Game1.player.caveChoice.Value : Game1.MasterPlayer.caveChoice.Value;
                if (caveChoice == 2 && cave.objects.Count() == 0)
                {
                    cave.setUpMushroomHouse();
                }
            }

            // 8. Configurar el Sótano privado de la residencia (Cellar) y warps de escaleras (GAP-10 / TASK-905)
            EnsureSlotCellar(farm.NameOrUniqueName, cabin, assignment);

            // 9. Configurar el Invernadero privado de la residencia (Greenhouse) (GAP-20)
            EnsureSlotGreenhouse(farm, assignment);
        }

        /// <summary>
        /// Asegura que la residencia tenga un sótano privado instanciado en Game1.locations
        /// y que las escaleras de la cabaña estén conectadas a dicho sótano (GAP-10 / TASK-905).
        /// </summary>
        public static void EnsureSlotCellar(string farmSlotId, Cabin cabin, DoorAssignment? assignment = null)
        {
            if (!Context.IsMainPlayer) return;

            string cellarName = ResidenceUpgradeLogic.GetSlotCellarName(farmSlotId);
            Cellar? cellar = Game1.locations?.FirstOrDefault(l => string.Equals(l.NameOrUniqueName, cellarName, StringComparison.OrdinalIgnoreCase)) as Cellar
                          ?? Game1.getLocationFromName(cellarName) as Cellar;
            if (cellar == null && Game1.locations != null)
            {
                cellar = new Cellar("Maps\\Cellar", cellarName);
                cellar.isAlwaysActive.Value = true;
                Game1.locations.Add(cellar);
                context?.Monitor.Log($"[FarmSlotModule] Sótano privado aprovisionado para {farmSlotId}: {cellarName}", LogLevel.Trace);
            }

            if (cellar != null && assignment != null && assignment.OwnerId != 0)
            {
                cellar.PublishOwnership(assignment.OwnerId, assignment.Policy);
            }

            if (cabin.upgradeLevel >= 3)
            {
                cabin.AddCellarTiles();
                cabin.updateCellarWarps();
            }
        }

        /// <summary>
        /// Asegura que la residencia tenga su edificio Greenhouse con interior instanciado exclusivo en Game1.locations
        /// y warps de entrada/salida conectados a la parcela privada (GAP-20).
        /// </summary>
        public static void EnsureSlotGreenhouse(Farm farm, DoorAssignment? assignment = null)
        {
            if (!Context.IsMainPlayer) return;

            Building? greenhouseBuilding = farm.getBuildingByType("Greenhouse")
                ?? farm.buildings.FirstOrDefault(b => b.buildingType.Value.Equals("Greenhouse", StringComparison.OrdinalIgnoreCase));

            Vector2 ghStartTile = farm.GetGreenhouseStartLocation();
            if (greenhouseBuilding == null)
            {
                greenhouseBuilding = Building.CreateInstanceFromId("Greenhouse", ghStartTile);
                greenhouseBuilding.parentLocationName.Value = farm.NameOrUniqueName;
                greenhouseBuilding.load();
                farm.buildings.Add(greenhouseBuilding);
            }

            string greenhouseName = ResidenceUpgradeLogic.GetSlotGreenhouseName(farm.NameOrUniqueName);
            GameLocation? ghLoc = Game1.locations?.FirstOrDefault(l => string.Equals(l.NameOrUniqueName, greenhouseName, StringComparison.OrdinalIgnoreCase));

            if (ghLoc == null && Game1.locations != null)
            {
                ghLoc = new GameLocation("Maps\\Greenhouse", greenhouseName);
                ghLoc.isGreenhouse.Value = true;
                ghLoc.IsFarm = true;
                ghLoc.isAlwaysActive.Value = true;
                Game1.locations.Add(ghLoc);
                context?.Monitor.Log($"[FarmSlotModule] Invernadero privado aprovisionado para {farm.NameOrUniqueName}: {greenhouseName}", LogLevel.Trace);
            }

            if (ghLoc != null)
            {
                ghLoc.isGreenhouse.Value = true;
                ghLoc.IsFarm = true;
                greenhouseBuilding.parentLocationName.Value = farm.NameOrUniqueName;
                greenhouseBuilding.indoors.Value = null;
                greenhouseBuilding.nonInstancedIndoorsName.Value = greenhouseName;
                greenhouseBuilding.UpdateIndoorParent();

                if (assignment != null && assignment.OwnerId != 0)
                {
                    ghLoc.PublishOwnership(assignment.OwnerId, assignment.Policy);

                    // Sincronizar desbloqueo si el propietario ya tiene ccPantry o jojaPantry
                    Farmer? owner = Game1.GetPlayer(assignment.OwnerId);
                    if (owner != null && (owner.mailReceived.Contains("ccPantry") || owner.mailReceived.Contains("jojaPantry") || owner.hasCompletedCommunityCenter()))
                    {
                        farm.greenhouseUnlocked.Value = true;
                    }
                }

                // Configurar warp de retorno hacia la puerta del invernadero en la granja del jugador
                int doorX = greenhouseBuilding.tileX.Value + greenhouseBuilding.humanDoor.X;
                int doorY = greenhouseBuilding.tileY.Value + greenhouseBuilding.humanDoor.Y;

                greenhouseBuilding.updateInteriorWarps(ghLoc);
                UpdateOrCreateWarp(ghLoc, 10, 24, farm.NameOrUniqueName, doorX, doorY + 1);
            }
        }

        private static void RegisterChatCommands()
        {
            if (chatCommandsRegistered) return;
            try
            {
                if (!ChatCommands.Exists("doors"))
                {
                    ChatCommands.Register(
                        "doors",
                        HandleDoorsChatCommand,
                        name => "Muestra el catálogo de residencias de la Plaza Central y su estado (/doors o /residencias)",
                        new[] { "residencias" },
                        mainOnly: false,
                        multiplayerOnly: false,
                        cheatsOnly: false
                    );
                }
                chatCommandsRegistered = true;
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[FarmSlotModule] Error registrando comando de chat /doors: {ex}", LogLevel.Warn);
            }
        }

        private static void HandleDoorsChatCommand(string[] command, ChatBox chat)
        {
            if (context == null) return;
            int slotCount = context.Config.FarmSlotCount;

            var specs = RegisteredDoors.Select(d => new DoorSpec(
                d.DoorId,
                d.LocationName,
                d.TownTileX,
                d.TownTileY,
                d.Edge,
                d.DefaultSlotId,
                d.MailboxTileX,
                d.MailboxTileY
            ));

            var entries = PlazaCatalogFormatter.BuildCatalogEntries(specs, slotCount, doorId =>
            {
                bool isClaimed = context.WorldState.Doors.TryGetValue(doorId, out DoorAssignment? assignment);
                string? owner = null;
                string? farmType = assignment?.FarmType;
                if (isClaimed && assignment != null)
                {
                    if (context.WorldState.Players.TryGetValue(assignment.OwnerId, out var rec) && !string.IsNullOrWhiteSpace(rec.PlayerName))
                        owner = rec.PlayerName;
                    else
                        owner = "Vecino";
                }
                var dModel = RegisteredDoors.FirstOrDefault(d => d.DoorId == doorId);
                return (isClaimed, owner, farmType, dModel?.Description);
            });

            chat.addInfoMessage("=== PARCELAS RESIDENCIALES EN LA PLAZA CENTRAL ===");
            int freeCount = 0;
            foreach (var entry in entries)
            {
                if (!entry.IsClaimed) freeCount++;
                chat.addInfoMessage(PlazaCatalogFormatter.FormatChatLine(entry));
            }
            chat.addInfoMessage($"Total: {freeCount} de {entries.Count} parcelas libres. Camina hacia el portal o escribe /residencias para fundar tu granja.");
        }

        private void OnConsoleDoors(string command, string[] args)
        {
            if (context == null) return;
            int slotCount = context.Config.FarmSlotCount;

            var specs = RegisteredDoors.Select(d => new DoorSpec(
                d.DoorId,
                d.LocationName,
                d.TownTileX,
                d.TownTileY,
                d.Edge,
                d.DefaultSlotId,
                d.MailboxTileX,
                d.MailboxTileY
            ));

            var entries = PlazaCatalogFormatter.BuildCatalogEntries(specs, slotCount, doorId =>
            {
                bool isClaimed = context.WorldState.Doors.TryGetValue(doorId, out DoorAssignment? assignment);
                string? owner = null;
                string? farmType = assignment?.FarmType;
                if (isClaimed && assignment != null)
                {
                    if (context.WorldState.Players.TryGetValue(assignment.OwnerId, out var rec) && !string.IsNullOrWhiteSpace(rec.PlayerName))
                        owner = rec.PlayerName;
                    else
                        owner = assignment.OwnerId.ToString();
                }
                var dModel = RegisteredDoors.FirstOrDefault(d => d.DoorId == doorId);
                return (isClaimed, owner, farmType, dModel?.Description);
            });

            context.Monitor.Log($"=== RESIDENCIAS EN LA PLAZA CENTRAL ({entries.Count} activas para FarmSlotCount={slotCount}) ===", LogLevel.Info);
            foreach (var entry in entries)
            {
                context.Monitor.Log(PlazaCatalogFormatter.FormatConsoleLine(entry), LogLevel.Info);
            }
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || e.FromModID != context.Manifest.UniqueID)
                return;

            if (e.Type == ModMessages.ClaimDoorRequest && Context.IsMainPlayer)
            {
                var payload = e.ReadAs<ClaimDoorPayload>();
                if (payload != null)
                {
                    HandleClaimDoor(e.FromPlayerID, payload.DoorId, payload.FarmType);
                }
            }
            else if (e.Type == ModMessages.ChangePolicyRequest && Context.IsMainPlayer)
            {
                var payload = e.ReadAs<ChangePolicyPayload>();
                if (payload != null)
                {
                    HandleChangePolicy(e.FromPlayerID, payload.FarmSlotId, payload.Policy);
                }
            }
            else if (e.Type == ModMessages.BuyAnnexRequest && Context.IsMainPlayer)
            {
                var payload = e.ReadAs<BuyAnnexPayload>();
                if (payload != null && Enum.TryParse(payload.Side, ignoreCase: true, out AnnexSide side))
                {
                    HandleBuyAnnex(e.FromPlayerID, payload.ResidenceSlotId, side, payload.TemplateId);
                }
            }
            else if (e.Type == ModMessages.UnlockGreenhouseRequest && Context.IsMainPlayer)
            {
                var payload = e.ReadAs<UnlockGreenhousePayload>();
                if (payload != null && !string.IsNullOrEmpty(payload.FarmSlotId))
                {
                    if (Game1.getLocationFromName(payload.FarmSlotId) is Farm targetFarm)
                    {
                        targetFarm.greenhouseUnlocked.Value = true;
                        context?.Monitor.Log($"[FarmSlotModule] Host confirmó desbloqueo de invernadero para {payload.FarmSlotId} solicitado por {e.FromPlayerID}.", LogLevel.Info);
                    }
                }
            }
            else if (e.Type == ModMessages.RequestDoorsSync && Context.IsMainPlayer)
            {
                SendDoorsSyncToPlayer(e.FromPlayerID);
            }
            else if (e.Type == ModMessages.SyncDoorsState)
            {
                var payload = e.ReadAs<SyncDoorsPayload>();
                if (payload != null)
                {
                    context.WorldState.Doors.Clear();
                    foreach (var kvp in payload.Doors)
                    {
                        context.WorldState.Doors[kvp.Key] = kvp.Value;
                    }

                    context.WorldState.Annexes.Clear();
                    if (payload.Annexes != null)
                    {
                        foreach (var kvp in payload.Annexes)
                        {
                            context.WorldState.Annexes[kvp.Key] = kvp.Value;
                        }
                    }

                    foreach (var kvp in payload.Players)
                    {
                        context.WorldState.Players[kvp.Key] = kvp.Value;
                    }

                    if (!Context.IsMainPlayer && Game1.player != null)
                    {
                        var myDoor = context.WorldState.Doors.Values.FirstOrDefault(d => d.OwnerId == Game1.player.UniqueMultiplayerID);
                        if (myDoor != null && !string.IsNullOrEmpty(myDoor.FarmSlotId))
                        {
                            Game1.player.homeLocation.Value = $"{myDoor.FarmSlotId}_Home";

                            // Si el jugador despertó en el fallback provisional de la plaza ("Farm" 64, 15) al unirse
                            // antes de recibir la sincronización de puertas, programar el viaje a la cama de su residencia.
                            if (string.Equals(Game1.currentLocation?.NameOrUniqueName, "Farm", StringComparison.OrdinalIgnoreCase)
                                && (int)Game1.player.Tile.X == 64 && (int)Game1.player.Tile.Y == 15)
                            {
                                pendingResidenceWakeSlotId = myDoor.FarmSlotId;
                            }
                        }
                    }

                    context.Monitor.Log($"[FarmSlotModule] Sincronización de residencias recibida del host ({context.WorldState.Doors.Count} ocupadas, {context.WorldState.Annexes.Count} anexos).", LogLevel.Info);
                    RefreshSlotLocationData();

                    if (Game1.currentLocation != null && HostPlazaLayout.IsPlazaLocation(Game1.currentLocation.NameOrUniqueName))
                    {
                        EnsureLocationPortals(Game1.currentLocation);
                    }
                }
            }
        }

        public static void BroadcastDoorsSync()
        {
            if (context == null || !Context.IsMainPlayer) return;

            var payload = new SyncDoorsPayload
            {
                Doors = new Dictionary<string, DoorAssignment>(context.WorldState.Doors),
                Annexes = new Dictionary<string, AnnexAssignment>(context.WorldState.Annexes),
                Players = new Dictionary<long, PlayerRecord>(context.WorldState.Players)
            };

            context.Helper.Multiplayer.SendMessage(
                payload,
                ModMessages.SyncDoorsState,
                new[] { context.Manifest.UniqueID }
            );
        }

        public static void SendDoorsSyncToPlayer(long playerId)
        {
            if (context == null || !Context.IsMainPlayer) return;

            var payload = new SyncDoorsPayload
            {
                Doors = new Dictionary<string, DoorAssignment>(context.WorldState.Doors),
                Annexes = new Dictionary<string, AnnexAssignment>(context.WorldState.Annexes),
                Players = new Dictionary<long, PlayerRecord>(context.WorldState.Players)
            };

            context.Helper.Multiplayer.SendMessage(
                payload,
                ModMessages.SyncDoorsState,
                new[] { context.Manifest.UniqueID },
                new[] { playerId }
            );
        }

        /// <summary>Procesa en el host un reclamo de puerta. Si lo rechaza, reenvía el estado al jugador para deshacer su copia optimista.</summary>
        private static bool HandleClaimDoor(long playerId, string doorId, string farmType)
        {
            if (context == null) return false;
            var worldState = context.WorldState;

            DoorModel? door = RegisteredDoors.FirstOrDefault(d => d.DoorId == doorId);
            bool doorActive = door != null && PlazaCatalogFormatter.IsDoorActive(door.DefaultSlotId, door.LocationName, context.Config.FarmSlotCount);
            DoorClaimRejection rejection = worldState.CheckDoorClaim(playerId, doorId, farmType);
            if (door == null || !doorActive || rejection != DoorClaimRejection.None)
            {
                string reason = door == null ? "puerta fuera del catálogo" : (!doorActive ? "parcela fuera del límite FarmSlotCount" : rejection.ToString());
                context.Monitor.Log($"[FarmSlotModule] Reclamo de {doorId} por {playerId} rechazado ({reason}).", LogLevel.Warn);
                if (playerId != Game1.player.UniqueMultiplayerID)
                {
                    SendDoorsSyncToPlayer(playerId);
                }
                return false;
            }

            // Reclamo repetido de la propia puerta: no reiniciar tipo, política ni amigos
            if (worldState.Doors.ContainsKey(doorId))
            {
                return true;
            }

            string slotId = door.DefaultSlotId;
            var playerRecord = worldState.GetOrCreatePlayer(playerId, Game1.GetPlayer(playerId)?.Name ?? string.Empty);
            playerRecord.FarmSlotId = slotId;

            string resolvedType = string.IsNullOrWhiteSpace(farmType) ? "Standard" : farmType;
            var assignment = new DoorAssignment
            {
                DoorId = doorId,
                FarmSlotId = slotId,
                FarmType = resolvedType,
                OwnerId = playerId,
                Policy = AccessPolicy.Locked
            };
            worldState.Doors[doorId] = assignment;

            // Replicar en GameLocation.modData y asegurar configuración de edificios
            Farm? farmLoc = EnsureFarmSlotExists(slotId);
            if (farmLoc != null)
            {
                farmLoc.PublishOwnership(playerId, AccessPolicy.Locked);
                ApplyFarmTemplate(farmLoc, resolvedType, regenerate: true, playerId);
                FarmTypeModule.ApplyMapProperties(farmLoc, resolvedType);
                EnsureFarmSlotBuildings(farmLoc, assignment);
            }
            GameLocation? caveLoc = Game1.getLocationFromName($"{slotId}_Cave");
            if (caveLoc != null)
            {
                caveLoc.PublishOwnership(playerId, AccessPolicy.Locked);
            }
            GameLocation? cellarLoc = Game1.getLocationFromName(ResidenceUpgradeLogic.GetSlotCellarName(slotId));
            if (cellarLoc != null)
            {
                cellarLoc.PublishOwnership(playerId, AccessPolicy.Locked);
            }
            GameLocation? ghLoc = Game1.getLocationFromName(ResidenceUpgradeLogic.GetSlotGreenhouseName(slotId));
            if (ghLoc != null)
            {
                ghLoc.PublishOwnership(playerId, AccessPolicy.Locked);
            }

            context.Monitor.Log($"[FarmSlotModule] Puerta {doorId} asignada con éxito a {playerId} (Granja: {slotId}, Tipo: {resolvedType}).", LogLevel.Info);
            RefreshSlotLocationData();

            // Actualizar warps físicos continuos en la plaza
            GameLocation? doorLoc = Game1.getLocationFromName(door.LocationName);
            if (doorLoc != null)
            {
                EnsureLocationPortals(doorLoc);
            }
            EnsureAllPlazaPortals();

            // Sincronizar inmediatamente a todos los peones
            BroadcastDoorsSync();
            return true;
        }

        private static void HandleChangePolicy(long playerId, string farmSlotId, string policyStr)
        {
            if (context == null) return;
            var worldState = context.WorldState;

            var doorEntry = worldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == farmSlotId && d.OwnerId == playerId);
            if (doorEntry == null) return;

            if (Enum.TryParse(policyStr, ignoreCase: true, out AccessPolicy newPolicy))
            {
                doorEntry.Policy = newPolicy;
                GameLocation? loc = Game1.getLocationFromName(farmSlotId);
                if (loc != null)
                {
                    loc.PublishOwnership(playerId, newPolicy);
                }

                // TASK-903: Propagar la política de acceso actualizada a todos los anexos de esta residencia
                foreach (AnnexAssignment annex in worldState.GetAnnexesForResidence(farmSlotId))
                {
                    GameLocation? annexLoc = Game1.getLocationFromName(annex.AnnexId);
                    annexLoc?.PublishOwnership(playerId, newPolicy);
                }

                // Propagar también a la cueva, sótano e invernadero privados de la residencia
                GameLocation? caveLoc = Game1.getLocationFromName($"{farmSlotId}_Cave");
                caveLoc?.PublishOwnership(playerId, newPolicy);

                GameLocation? cellarLoc = Game1.getLocationFromName(ResidenceUpgradeLogic.GetSlotCellarName(farmSlotId));
                cellarLoc?.PublishOwnership(playerId, newPolicy);

                GameLocation? ghLoc = Game1.getLocationFromName(ResidenceUpgradeLogic.GetSlotGreenhouseName(farmSlotId));
                ghLoc?.PublishOwnership(playerId, newPolicy);

                context.Monitor.Log($"[FarmSlotModule] Política de {farmSlotId} cambiada a {newPolicy} por propietario {playerId}.", LogLevel.Info);

                // Sincronizar inmediatamente a todos los peones
                BroadcastDoorsSync();
            }
        }

        /// <summary>
        /// Procesa en el host la compra de un anexo temático de residencia (TASK-905).
        /// </summary>
        public static bool HandleBuyAnnex(long playerId, string residenceSlotId, AnnexSide side, string templateId)
        {
            if (context == null || !Context.IsMainPlayer) return false;
            var worldState = context.WorldState;

            DoorAssignment? residenceDoor = worldState.Doors.Values
                .FirstOrDefault(d => string.Equals(d.FarmSlotId, residenceSlotId, StringComparison.OrdinalIgnoreCase));
            DoorModel? doorModel = RegisteredDoors.FirstOrDefault(d => d.DefaultSlotId == residenceSlotId);

            var freeSides = doorModel != null
                ? ResidenceExits.FreeSides(doorModel.Edge)
                : (IReadOnlyCollection<AnnexSide>)Array.Empty<AnnexSide>();

            Farmer? farmer = Game1.GetPlayer(playerId);
            int playerMoney = farmer?.Money ?? 0;

            int maxAnnexes = context.Config.MaxAnnexesPerResidence > 0 ? context.Config.MaxAnnexesPerResidence : 2;
            AnnexPurchaseResult result = AnnexPurchaseLogic.ValidateAndPurchase(
                playerId,
                residenceSlotId,
                side,
                templateId,
                playerMoney,
                residenceDoor,
                freeSides,
                worldState,
                maxAnnexes,
                context.Config.FirstAnnexPriceMultiplier,
                context.Config.SecondAnnexPriceMultiplier
            );

            if (!result.Success || result.Assignment == null)
            {
                string reason = result.Rejection switch
                {
                    AnnexPurchaseRejection.InsufficientFunds => "No dispones de suficiente dinero para adquirir este anexo.",
                    AnnexPurchaseRejection.NotResidenceOwner => "Solo el propietario de la residencia puede adquirir anexos.",
                    AnnexPurchaseRejection.AnnexAlreadyExists => "Ya existe un anexo adquirido en este acceso.",
                    AnnexPurchaseRejection.SideNotFree => "Este acceso no admite un anexo.",
                    AnnexPurchaseRejection.MaxAnnexesReached => "Has alcanzado el límite máximo de anexos para esta residencia.",
                    _ => "No se pudo adquirir el anexo."
                };

                context.Monitor.Log($"[FarmSlotModule] Compra de anexo {residenceSlotId}_{side} ({templateId}) rechazada para {playerId} ({result.Rejection}).", LogLevel.Warn);
                if (playerId == Game1.player.UniqueMultiplayerID)
                {
                    Game1.showRedMessage(reason);
                }
                return false;
            }

            if (farmer != null)
            {
                DailyRolloverModule.SetFarmerMoney(farmer, result.RemainingPlayerMoney);
            }

            worldState.Annexes[result.Assignment.AnnexId] = result.Assignment;

            EnsureAnnexLocation(result.Assignment, regenerate: true);
            RefreshSlotLocationData();

            // Reconfigurar warps de la residencia para incluir la nueva salida hacia el anexo (TASK-904)
            if (Game1.getLocationFromName(residenceSlotId) is Farm resFarm)
            {
                EnsureFarmSlotBuildings(resFarm, residenceDoor);
            }

            AnnexTemplate template = AnnexTemplateCatalog.GetTemplate(templateId);
            context.Monitor.Log($"[FarmSlotModule] Anexo {result.Assignment.AnnexId} ({template.DisplayName}) adquirido por {playerId} por {result.PricePaid:N0}g.", LogLevel.Info);

            if (playerId == Game1.player.UniqueMultiplayerID)
            {
                Game1.addHUDMessage(new HUDMessage($"¡Anexo adquirido: {template.DisplayName}! La salida ya está abierta.", HUDMessage.newQuest_type));
            }

            BroadcastDoorsSync();
            return true;
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || context == null)
                return;

            if (!e.Button.IsActionButton())
                return;

            if (Game1.currentLocation == null)
                return;

            string curLoc = Game1.currentLocation.NameOrUniqueName;
            Vector2 targetTile = e.Cursor.GrabTile;

            // Interacción con puerta de la Farmhouse en una granja privada (entrar al interior Cabin)
            if (curLoc.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase) && Game1.currentLocation is Farm farm)
            {
                Point entryDoor = farm.GetMainFarmHouseEntry();
                if (Math.Abs(entryDoor.X - (int)targetTile.X) <= 1 && Math.Abs(entryDoor.Y - (int)targetTile.Y) <= 1)
                {
                    Building? farmhouse = farm.getBuildingByType("Farmhouse")
                                       ?? farm.buildings.FirstOrDefault(b => string.Equals(b.buildingType.Value, "Farmhouse", StringComparison.OrdinalIgnoreCase));
                    if (farmhouse?.GetIndoors() is Cabin cabin)
                    {
                        CleanAndArrangeResidenceInterior(cabin);
                        int targetX = cabin.warps.Count > 0 ? cabin.warps[0].X : 3;
                        int targetY = cabin.warps.Count > 0 ? cabin.warps[0].Y - 1 : 10;
                        Game1.currentLocation.playSound("doorClose");
                        Game1.warpFarmer(cabin.NameOrUniqueName, targetX, targetY, 0);
                        return;
                    }
                }
            }

            // Interacción para salir del interior Cabin hacia el exterior de la granja privada
            if (Game1.currentLocation is Cabin insideCabin && insideCabin.NameOrUniqueName.EndsWith("_Home", StringComparison.OrdinalIgnoreCase))
            {
                if (insideCabin.warps.Count > 0 && Math.Abs(insideCabin.warps[0].X - (int)targetTile.X) <= 1 && Math.Abs(insideCabin.warps[0].Y - (int)targetTile.Y) <= 1)
                {
                    Warp exitWarp = insideCabin.warps[0];
                    Game1.currentLocation.playSound("doorClose");
                    Game1.warpFarmer(exitWarp.TargetName, exitWarp.TargetX, exitWarp.TargetY, 2);
                    return;
                }
            }

            DoorModel? door = RegisteredDoors.FirstOrDefault(d =>
                IsDoorActiveForCurrentConfig(d)
                && string.Equals(string.IsNullOrWhiteSpace(d.LocationName) ? "Farm" : d.LocationName, curLoc, StringComparison.OrdinalIgnoreCase)
                && (IsInOpeningOrBorder(d, (int)targetTile.X, (int)targetTile.Y)
                    || ((d.MailboxTileX ?? (d.TownTileX + 1)) == (int)targetTile.X && (d.MailboxTileY ?? d.TownTileY) == (int)targetTile.Y)));

            if (door != null)
            {
                InteractWithResidentialDoor(door);
                return;
            }

            // TASK-904 / TASK-905: Interacción directa con accesos de anexo en la residencia privada
            if (curLoc.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var residenceDoor = RegisteredDoors.FirstOrDefault(d => d.DefaultSlotId == curLoc);
                if (residenceDoor != null)
                {
                    var freeSides = ResidenceExits.FreeSides(residenceDoor.Edge);
                    string? farmType = context?.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == curLoc)?.FarmType;
                    bool isMeadowlands = FarmGrassLogic.IsMeadowlands(farmType);
                    AnnexSide? clickedSide = null;
                    if (isMeadowlands)
                    {
                        if (targetTile.Y <= 2 && (int)targetTile.X >= 60 && (int)targetTile.X <= 67)
                            clickedSide = AnnexSide.North;
                        else if (targetTile.X >= 97 && (int)targetTile.Y >= 20 && (int)targetTile.Y <= 24)
                            clickedSide = AnnexSide.East;
                        else if (targetTile.Y >= 72 && (int)targetTile.X >= 50 && (int)targetTile.X <= 55)
                            clickedSide = AnnexSide.South;
                    }
                    else
                    {
                        if (targetTile.Y <= 2 && (int)targetTile.X >= 36 && (int)targetTile.X <= 44)
                            clickedSide = AnnexSide.North;
                        else if (targetTile.X >= 77 && (int)targetTile.Y >= 12 && (int)targetTile.Y <= 19)
                            clickedSide = AnnexSide.East;
                        else if (targetTile.Y >= 63 && (int)targetTile.X >= 36 && (int)targetTile.X <= 44)
                            clickedSide = AnnexSide.South;
                    }

                    if (clickedSide.HasValue && freeSides.Contains(clickedSide.Value))
                    {
                        var annex = context?.WorldState.FindAnnex(curLoc, clickedSide.Value);
                        if (annex == null)
                        {
                            ShowBuyAnnexDialog(curLoc, clickedSide.Value);
                            return;
                        }
                        else
                        {
                            var template = AnnexTemplateCatalog.GetTemplate(annex.TemplateId);
                            var pts = AnnexEntrance.For(clickedSide.Value, template.Width, template.Height, farmType);
                            Game1.warpFarmer(annex.AnnexId, pts.EntryTile.X, pts.EntryTile.Y, pts.FacingDirection);
                            return;
                        }
                    }
                }
            }
        }

        public static void InteractWithResidentialDoor(DoorModel door)
        {
            if (context == null || Game1.currentLocation == null) return;
            var worldState = context.WorldState;

            worldState.Doors.TryGetValue(door.DoorId, out DoorAssignment? assignment);
            long myId = Game1.player.UniqueMultiplayerID;

            if (assignment == null)
            {
                // Si el jugador ya posee una residencia, no permitir fundar otra
                var existingAssignment = worldState.FindResidenceOf(myId);
                if (existingAssignment != null)
                {
                    var existingDoor = RegisteredDoors.FirstOrDefault(d => d.DoorId == existingAssignment.DoorId);
                    string desc = existingDoor?.Description ?? existingAssignment.DoorId;
                    Game1.drawObjectDialogue($"Ya eres propietario de una residencia ({desc}). Cada granjero sólo puede poseer una parcela.");
                    return;
                }

                // Puerta libre: submenú de elección de tipo de granja (TASK-602)
                Response[] choices = new[]
                {
                    new Response("Standard", "Granja Estándar (Clásica equilibrada)"),
                    new Response("Foraging", "Granja Forestal (Recolección y madera)"),
                    new Response("Fishing", "Granja Fluvial (Pesca e islas)"),
                    new Response("Mining", "Granja Minera (Colinas y minerales)"),
                    new Response("FourCorners", "Granja Cuatro Esquinas (Parcelas)"),
                    new Response("Ranching", "Granja Pradera (Ganadería y pasto azul)"),
                    new Response("Cancel", "Cancelar")
                };

                QuestionDialog.Ask(
                    $"{door.Description}.\nEsta residencia está libre. ¿Qué tipo de granja deseas fundar?",
                    choices,
                    (who, whichAnswer) =>
                    {
                        if (whichAnswer == "Cancel")
                            return;

                        string resolvedType = string.IsNullOrWhiteSpace(whichAnswer) ? "Standard" : whichAnswer;
                        Response[] confirmChoices = new[]
                        {
                            new Response("Yes", "Sí, fundar y entrar ahora"),
                            new Response("No", "No, cancelar sin construir")
                        };

                        QuestionDialog.Ask(
                            $"Vas a fundar la residencia ({resolvedType}) y entrar.\n¿Confirmas? Si eliges No, no se asignará ni construirá.",
                            confirmChoices,
                            (w, confirmation) =>
                            {
                                if (!ResidenceClaimConfirmation.ShouldCreate(confirmation))
                                {
                                    Game1.addHUDMessage(new HUDMessage("Fundación cancelada; la residencia sigue libre.", HUDMessage.newQuest_type));
                                    return;
                                }

                                if (Context.IsMainPlayer)
                                {
                                    if (!HandleClaimDoor(myId, door.DoorId, resolvedType))
                                    {
                                        Game1.drawObjectDialogue("No se pudo fundar esta residencia.");
                                        return;
                                    }
                                }
                                else
                                {
                                    context.Helper.Multiplayer.SendMessage(
                                        new ClaimDoorPayload { DoorId = door.DoorId, FarmType = resolvedType },
                                        ModMessages.ClaimDoorRequest,
                                        new[] { context.Manifest.UniqueID },
                                        new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                                    );

                                    // Copia optimista: si el host rechaza, su resincronización la deshace.
                                    worldState.Doors[door.DoorId] = new DoorAssignment
                                    {
                                        DoorId = door.DoorId,
                                        FarmSlotId = door.DefaultSlotId,
                                        FarmType = resolvedType,
                                        OwnerId = myId,
                                        Policy = AccessPolicy.Locked
                                    };
                                    var playerRecord = worldState.GetOrCreatePlayer(myId, Game1.player.Name);
                                    playerRecord.FarmSlotId = door.DefaultSlotId;
                                    Game1.player.homeLocation.Value = $"{door.DefaultSlotId}_Home";
                                }

                                Game1.addHUDMessage(new HUDMessage($"¡Residencia reclamada! Plantilla: {resolvedType}.", HUDMessage.newQuest_type));
                                WarpToFarm(door.DefaultSlotId);
                            }
                        );
                    }
                );
            }
            else if (assignment.OwnerId == myId)
            {
                // Es mi casa
                Response[] choices = new[]
                {
                    new Response("Enter", "Entrar a mi granja"),
                    new Response("Policy", $"Cambiar acceso (Actual: {assignment.Policy})"),
                    new Response("Cancel", "Cancelar")
                };

                QuestionDialog.Ask(
                    $"Tu residencia en {PlazaCatalogFormatter.GetLocationDisplayName(door.LocationName)}. (Tipo: {assignment.FarmType})",
                    choices,
                    (who, whichAnswer) =>
                    {
                        if (whichAnswer == "Enter")
                        {
                            WarpToFarm(assignment.FarmSlotId);
                        }
                        else if (whichAnswer == "Policy")
                        {
                            ShowPolicySelectionDialog(assignment.FarmSlotId);
                        }
                    }
                );
            }
            else
            {
                // Casa de otro jugador
                string ownerName = "un vecino";
                if (worldState.Players.TryGetValue(assignment.OwnerId, out var record) && !string.IsNullOrEmpty(record.PlayerName))
                    ownerName = record.PlayerName;

                Response[] choices = new[]
                {
                    new Response("Visit", $"Visitar granja de {ownerName}"),
                    new Response("Cancel", "Cancelar")
                };

                QuestionDialog.Ask(
                    $"Residencia de {ownerName}. Tipo: {assignment.FarmType}. Estado: {assignment.Policy}.",
                    choices,
                    (who, whichAnswer) =>
                    {
                        if (whichAnswer == "Visit")
                        {
                            if (assignment.Policy == AccessPolicy.Locked)
                            {
                                Game1.drawObjectDialogue("La puerta está cerrada con llave.");
                            }
                            else
                            {
                                WarpToFarm(assignment.FarmSlotId);
                            }
                        }
                    }
                );
            }
        }

        public static void ShowPolicySelectionDialog(string farmSlotId)
        {
            if (context == null || Game1.currentLocation == null) return;

            Response[] choices = new[]
            {
                new Response("Locked", "Cerrado con llave (Locked)"),
                new Response("Friends", "Solo Amigos (Friends)"),
                new Response("Public", "Público para visitas (Public)"),
                new Response("Cancel", "Cancelar")
            };

            QuestionDialog.Ask(
                "Selecciona la política de acceso para tu granja:",
                choices,
                (who, whichAnswer) =>
                {
                    if (whichAnswer is "Locked" or "Friends" or "Public")
                    {
                        context.Helper.Multiplayer.SendMessage(
                            new ChangePolicyPayload { FarmSlotId = farmSlotId, Policy = whichAnswer },
                            ModMessages.ChangePolicyRequest,
                            new[] { context.Manifest.UniqueID },
                            new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                        );

                        if (Context.IsMainPlayer)
                        {
                            HandleChangePolicy(Game1.player.UniqueMultiplayerID, farmSlotId, whichAnswer);
                        }

                        Game1.addHUDMessage(new HUDMessage($"Acceso actualizado a: {whichAnswer}", HUDMessage.newQuest_type));
                    }
                }
            );
        }

        /// <summary>
        /// Muestra el diálogo interactivo para seleccionar y adquirir un anexo temático en la salida indicada (TASK-905).
        /// </summary>
        public static void ShowBuyAnnexDialog(string residenceSlotId, AnnexSide side)
        {
            var ctx = context;
            if (ctx == null || Game1.currentLocation == null) return;
            var worldState = ctx.WorldState;

            long myId = Game1.player.UniqueMultiplayerID;
            var doorAssignment = worldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == residenceSlotId);
            if (doorAssignment == null || doorAssignment.OwnerId != myId)
            {
                Game1.drawObjectDialogue("Este acceso está cerrado. Solo el propietario puede adquirir una ampliación de anexo.");
                return;
            }

            if (worldState.FindAnnex(residenceSlotId, side) != null)
            {
                return;
            }

            int existingAnnexesCount = worldState.GetAnnexesForResidence(residenceSlotId).Count;
            float firstMultiplier = ctx.Config.FirstAnnexPriceMultiplier;
            float secondMultiplier = ctx.Config.SecondAnnexPriceMultiplier;

            var choices = new List<Response>();
            foreach (var kvp in AnnexTemplateCatalog.Templates)
            {
                AnnexTemplate t = kvp.Value;
                int price = AnnexPurchaseLogic.CalculatePrice(t, existingAnnexesCount, firstMultiplier, secondMultiplier);
                choices.Add(new Response(t.Id, $"{t.DisplayName} ({price:N0}g)"));
            }
            choices.Add(new Response("Cancel", "Cancelar"));

            string sideName = side switch
            {
                AnnexSide.North => "Norte",
                AnnexSide.East => "Este",
                AnnexSide.South => "Sur",
                _ => side.ToString()
            };

            string tierDesc = existingAnnexesCount == 0 ? "Primer anexo (Tarifa reducida)" : "Segundo anexo";

            QuestionDialog.Ask(
                $"Salida {sideName}: Espacio disponible para ampliación de granja [{tierDesc}].\n¿Deseas adquirir un anexo privado?",
                choices.ToArray(),
                (who, whichAnswer) =>
                {
                    if (whichAnswer != "Cancel" && AnnexTemplateCatalog.Templates.TryGetValue(whichAnswer, out AnnexTemplate? selectedTemplate))
                    {
                        int currentExisting = worldState.GetAnnexesForResidence(residenceSlotId).Count;
                        int price = AnnexPurchaseLogic.CalculatePrice(selectedTemplate, currentExisting, firstMultiplier, secondMultiplier);

                        if (Game1.player.Money < price)
                        {
                            Game1.drawObjectDialogue($"No tienes suficiente oro para adquirir {selectedTemplate.DisplayName}.\nPrecio: {price:N0}g (tienes {Game1.player.Money:N0}g).");
                            return;
                        }

                        Response[] confirmChoices = new[]
                        {
                            new Response("Yes", $"Sí, adquirir ({price:N0}g)"),
                            new Response("No", "No por ahora")
                        };

                        QuestionDialog.Ask(
                            $"¿Confirmas la compra de '{selectedTemplate.DisplayName}' para la salida {sideName} por {price:N0}g?",
                            confirmChoices,
                            (w, a) =>
                            {
                                if (a == "Yes")
                                {
                                    if (Context.IsMainPlayer)
                                    {
                                        HandleBuyAnnex(myId, residenceSlotId, side, selectedTemplate.Id);
                                    }
                                    else
                                    {
                                        ctx.Helper.Multiplayer.SendMessage(
                                            new BuyAnnexPayload
                                            {
                                                ResidenceSlotId = residenceSlotId,
                                                Side = side.ToString(),
                                                TemplateId = selectedTemplate.Id
                                            },
                                            ModMessages.BuyAnnexRequest,
                                            new[] { ctx.Manifest.UniqueID },
                                            new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                                        );
                                    }
                                }
                            }
                        );
                    }
                }
            );
        }

        public static void WarpToFarm(string farmSlotId)
        {
            DoorAssignment? assignment = context?.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == farmSlotId);
            DoorModel? door = RegisteredDoors.FirstOrDefault(d => d.DefaultSlotId == farmSlotId);

            Farm? farm = EnsureFarmSlotExists(farmSlotId);
            if (farm != null)
            {
                EnsureFarmSlotBuildings(farm, assignment);
            }

            int targetX, targetY, facingDir;
            if (door != null)
            {
                GetResidenceArrivalCoordinates(door, assignment?.FarmType, out targetX, out targetY, out facingDir);
            }
            else
            {
                var template = FarmTemplateCatalog.GetTemplate(assignment?.FarmType);
                targetX = template.CabinTileX;
                targetY = template.CabinTileY + 2;
                facingDir = 2;
            }

            Game1.warpFarmer(farmSlotId, targetX, targetY, facingDir);
        }

        private void ApplyHarmonyPatches(Harmony harmony)
        {
            try
            {
                // Parche para redirigir la salida de una granja Pelican4Ever_Farm_XX hacia su puerta residencial
                harmony.Patch(
                    original: AccessTools.Method(typeof(Game1), nameof(Game1.warpFarmer), new[] { typeof(LocationRequest), typeof(int), typeof(int), typeof(int) }),
                    prefix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Prefix_WarpFarmer))
                );

                // Parches anti-griefing en granjas ajenas: bloquear herramientas destructivas y cofres
                harmony.Patch(
                    original: AccessTools.Method(typeof(Farmer), "performBeginUsingTool"),
                    prefix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Prefix_Farmer_PerformBeginUsingTool))
                );

                harmony.Patch(
                    original: AccessTools.Method(typeof(StardewValley.Objects.Chest), nameof(StardewValley.Objects.Chest.checkForAction)),
                    prefix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Prefix_ChestCheckForAction))
                );

                // Parche de Culling dinámico de red para 16 granjas (TASK-603)
                harmony.Patch(
                    original: AccessTools.Method(typeof(Multiplayer), nameof(Multiplayer.isAlwaysActiveLocation)),
                    postfix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Postfix_IsAlwaysActiveLocation))
                );

                // Parche para evitar excepción y congelamiento de clientes por cabañas faltantes o no cargadas
                harmony.Patch(
                    original: AccessTools.Method(typeof(BedFurniture), nameof(BedFurniture.ApplyWakeUpPosition)),
                    prefix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Prefix_ApplyWakeUpPosition))
                );

                // TASK-605: Destinos de teletransporte (Tótem de granja y obelisco de la isla) hacia la residencia
                harmony.Patch(
                    original: AccessTools.Method(typeof(StardewValley.Object), "totemWarpForReal"),
                    prefix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Prefix_TotemWarpForReal))
                );
                harmony.Patch(
                    original: AccessTools.Method(
                        typeof(IslandWest),
                        nameof(IslandWest.performAction),
                        new[] { typeof(string[]), typeof(Farmer), typeof(xTile.Dimensions.Location) }
                    ),
                    prefix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Prefix_IslandWestPerformAction))
                );

                // Parche de paso libre sin colisión en los portales residenciales de la Plaza Central
                harmony.Patch(
                    original: AccessTools.Method(
                        typeof(GameLocation),
                        nameof(GameLocation.isCollidingPosition),
                        new[] {
                            typeof(Microsoft.Xna.Framework.Rectangle),
                            typeof(xTile.Dimensions.Rectangle),
                            typeof(bool),
                            typeof(int),
                            typeof(bool),
                            typeof(Character),
                            typeof(bool),
                            typeof(bool),
                            typeof(bool),
                            typeof(bool)
                        }
                    ),
                    prefix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Prefix_IsCollidingPosition))
                );

                // TASK-903: Asegurar que las ubicaciones de anexo guardadas en el XML de la partida existan en Game1.locations
                // antes de que SaveGame.loadDataToLocations restaure cultivos, objetos y máquinas tras reiniciar el servidor
                harmony.Patch(
                    original: AccessTools.Method(typeof(SaveGame), nameof(SaveGame.loadDataToLocations)),
                    prefix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Prefix_SaveGameLoadDataToLocations))
                );

                // TASK-903: Impedir que Farm añada Farmhouse/Greenhouse/ShippingBin/Cabins/SpousePatio/GrandpaCandles a anexos 40×32
                harmony.Patch(
                    original: AccessTools.Method(typeof(Farm), nameof(Farm.AddDefaultBuildings)),
                    prefix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Prefix_SkipOnAnnexFarm))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(Farm), nameof(Farm.UpdatePatio)),
                    prefix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Prefix_SkipOnAnnexFarm))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(Farm), nameof(Farm.addGrandpaCandles)),
                    prefix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Prefix_SkipOnAnnexFarm))
                );

                // Redirigir el menú de construcción de Robin y del Mago exclusivamente a la residencia y anexos del jugador
                harmony.Patch(
                    original: AccessTools.Method(typeof(GameLocation), nameof(GameLocation.ShowConstructOptions), new[] { typeof(string), typeof(int) }),
                    prefix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Prefix_ShowConstructOptions))
                );

                // GAP-10 / TASK-905: Enrutar sótanos privados de cabañas a su respectiva residencia
                harmony.Patch(
                    original: AccessTools.Method(typeof(FarmHouse), nameof(FarmHouse.GetCellarName)),
                    prefix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Prefix_FarmHouse_GetCellarName))
                );

                // Corrección gráfica: desfasar verticalmente la textura de Farmhouse con interior Cabin al subir de nivel
                harmony.Patch(
                    original: AccessTools.Method(typeof(Building), nameof(Building.getSourceRect)),
                    postfix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Postfix_Building_getSourceRect))
                );

                // GAP-20: Interceptar el acceso a invernaderos en ruinas para ofrecer la pasarela interactiva de restauración
                harmony.Patch(
                    original: AccessTools.Method(typeof(GreenhouseBuilding), nameof(GreenhouseBuilding.OnUseHumanDoor), new[] { typeof(Farmer) }),
                    prefix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Prefix_GreenhouseBuilding_OnUseHumanDoor))
                );

                // Prevención de recursión infinita (StackOverflowException) en Game1.findStructure <-> Game1.getLocationFromName
                // cuando un edificio tiene nonInstancedIndoorsName cuyo destino aún no está en Game1.locations
                harmony.Patch(
                    original: AccessTools.Method(typeof(Game1), nameof(Game1.findStructure), new[] { typeof(GameLocation), typeof(string) }),
                    prefix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Prefix_FindStructure))
                );

                context?.Monitor.Log("[FarmSlotModule] Parches Harmony de enrutado, anti-griefing, culling de red, colisión, anexos, protección de despertar, sótanos privados, invernaderos y construcción en residencia aplicados.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[FarmSlotModule] Error al aplicar parches Harmony: {ex}", LogLevel.Error);
            }
        }

        public static bool Prefix_FindStructure(GameLocation parentLocation, string name, ref GameLocation? __result)
        {
            // Nunca se delega en el original: Building.GetIndoors() de un interior no instanciado llama a
            // Game1.getLocationFromName, que vuelve a findStructure (StackOverflowException).
            __result = null;
            if (parentLocation?.buildings == null || string.IsNullOrEmpty(name))
                return false;

            foreach (Building b in parentLocation.buildings)
            {
                if (b == null) continue;

                if (b.indoors.Value != null && string.Equals(b.indoors.Value.NameOrUniqueName, name, StringComparison.OrdinalIgnoreCase))
                {
                    __result = b.indoors.Value;
                    return false;
                }

                if (b.HasIndoorsName(name))
                {
                    if (b.indoors.Value != null)
                    {
                        __result = b.indoors.Value;
                        return false;
                    }

                    // Interior no instanciado (p. ej. invernadero): buscarlo directamente en la lista sin recursión
                    GameLocation? nonInstanced = Game1.locations?.FirstOrDefault(l => l != null && string.Equals(l.NameOrUniqueName, name, StringComparison.OrdinalIgnoreCase));
                    if (nonInstanced != null)
                    {
                        __result = nonInstanced;
                        return false;
                    }
                }

                // Si el destino es un interior de residencia de este slot (ej. Pelican4Ever_Farm_01_Home)
                // y este es el edificio Farmhouse, asegurar su Cabin si indoors aún no está hidratado
                if (name.EndsWith("_Home", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(parentLocation.NameOrUniqueName, name.Substring(0, name.Length - 5), StringComparison.OrdinalIgnoreCase)
                    && (b.buildingType.Value?.Contains("Farmhouse", StringComparison.OrdinalIgnoreCase) == true || b.indoors.Value is Cabin))
                {
                    if (b.indoors.Value is Cabin existingCabin)
                    {
                        __result = existingCabin;
                        return false;
                    }
                    // Solo el host hidrata la residencia; un cliente esperaría a la versión sincronizada
                    if (!Context.IsMainPlayer)
                        return false;

                    var cabin = new Cabin("Maps\\FarmHouse");
                    cabin.uniqueName.Value = name;
                    cabin.IsFarm = true;
                    b.indoors.Value = cabin;
                    CleanAndArrangeResidenceInterior(cabin);
                    __result = cabin;
                    return false;
                }
            }

            return false;
        }

        public static void Prefix_SaveGameLoadDataToLocations(List<GameLocation> fromLocations)
        {
            if (fromLocations == null || Game1.locations == null)
                return;

            foreach (GameLocation savedLoc in fromLocations)
            {
                string? locName = savedLoc?.NameOrUniqueName;
                if (string.IsNullOrWhiteSpace(locName))
                    continue;

                if (locName.EndsWith(ResidenceUpgradeLogic.CellarSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    if (Game1.getLocationFromName(locName) == null)
                    {
                        // El objeto deserializado del save no tiene mapa cargado: usarlo como ubicación real hace que
                        // TransferDataFromSavedLocation falle (UpdateMapSeats con mapa nulo). Se crea una ubicación nueva
                        // con su mapa y el juego le transfiere el estado guardado.
                        string cellarMap = !string.IsNullOrWhiteSpace(savedLoc!.mapPath.Value) ? savedLoc.mapPath.Value : "Maps\\Cellar";
                        var cellar = new Cellar(cellarMap, locName);
                        Game1.locations.Add(cellar);
                        context?.Monitor.Log($"[FarmSlotModule] Sótano privado restaurado desde guardado: {locName}", LogLevel.Trace);
                    }
                    continue;
                }

                if (locName.EndsWith(ResidenceUpgradeLogic.GreenhouseSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    if (Game1.getLocationFromName(locName) == null)
                    {
                        string ghMap = !string.IsNullOrWhiteSpace(savedLoc!.mapPath.Value) ? savedLoc.mapPath.Value : "Maps\\Greenhouse";
                        var ghLoc = new GameLocation(ghMap, locName);
                        ghLoc.isGreenhouse.Value = true;
                        ghLoc.IsFarm = true;
                        Game1.locations.Add(ghLoc);
                        context?.Monitor.Log($"[FarmSlotModule] Invernadero privado restaurado desde guardado: {locName}", LogLevel.Trace);
                    }
                    continue;
                }

                if (locName.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase)
                    && !locName.EndsWith("_Cave", StringComparison.OrdinalIgnoreCase))
                {
                    if (Game1.getLocationFromName(locName) == null)
                    {
                        string mapPath = !string.IsNullOrWhiteSpace(savedLoc!.mapPath.Value)
                            ? savedLoc.mapPath.Value
                            : "Maps/Farm";
                        // Ubicación nueva con mapa cargado; el objeto deserializado (sin mapa) solo aporta el estado a transferir.
                        var farm = new Farm(mapPath, locName);
                        farm.isAlwaysActive.Value = true;
                        Game1.locations.Add(farm);
                        context?.Monitor.Log($"[FarmSlotModule] Pre-registrada residencia {locName} ({mapPath}) para restaurar cultivos y estructuras.", LogLevel.Trace);
                    }
                    continue;
                }

                if (locName.StartsWith(HostPlazaLayout.BridgePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    int slotCount = context?.Config.FarmSlotCount ?? 16;
                    var chain = HostPlazaLayout.Chain(slotCount);
                    bool inChain = chain.Any(l => string.Equals(l.Name, locName, StringComparison.OrdinalIgnoreCase));
                    if (!inChain)
                    {
                        var existing = Game1.getLocationFromName(locName);
                        if (existing != null)
                        {
                            Game1.locations.Remove(existing);
                        }
                        context?.Monitor.Log($"[FarmSlotModule] Puente guardado {locName} descartado (no está en la cadena activa para slotCount={slotCount}).", LogLevel.Trace);
                        continue;
                    }

                    if (Game1.getLocationFromName(locName) == null)
                    {
                        var bridgeLoc = new GameLocation($"Maps/{locName}", locName)
                        {
                            isOutdoors = { Value = true }
                        };
                        Game1.locations.Add(bridgeLoc);
                        context?.Monitor.Log($"[FarmSlotModule] Pre-registrado puente {locName} para restaurar estado guardado.", LogLevel.Trace);
                    }
                    continue;
                }

                if (!locName.StartsWith(LocationCullingLogic.AnnexPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Game1.getLocationFromName(locName) == null)
                {
                    string mapPath = !string.IsNullOrWhiteSpace(savedLoc!.mapPath.Value)
                        ? savedLoc.mapPath.Value
                        : "Maps/Pelican4Ever_Annex_Standard_North";
                    var annexFarm = new Farm(mapPath, locName);
                    Game1.locations.Add(annexFarm);
                    context?.Monitor.Log($"[FarmSlotModule] Pre-registrada ubicación de anexo {locName} ({mapPath}) para restaurar cultivos y máquinas.", LogLevel.Trace);
                }
            }
        }

        public static bool Prefix_SkipOnAnnexFarm(Farm __instance)
        {
            if (__instance != null && __instance.NameOrUniqueName.StartsWith(LocationCullingLogic.AnnexPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return true;
        }

        public static bool Prefix_ShowConstructOptions(GameLocation __instance, string builder, int page)
        {
            if (context == null)
                return true;

            Farmer who = Game1.player;

            // Obtener el hueco de residencia asignado al jugador actual
            string? residenceSlotId = context.WorldState.Doors.Values
                .FirstOrDefault(d => d.OwnerId == who.UniqueMultiplayerID)?.FarmSlotId;

            if (string.IsNullOrEmpty(residenceSlotId))
            {
                if (context.WorldState.Players.TryGetValue(who.UniqueMultiplayerID, out var playerRec) && !string.IsNullOrEmpty(playerRec.FarmSlotId))
                {
                    residenceSlotId = playerRec.FarmSlotId;
                }
            }

            var annexes = !string.IsNullOrEmpty(residenceSlotId) && context.WorldState.Annexes != null
                ? context.WorldState.GetAnnexesForResidence(residenceSlotId)
                : null;

            var allowedLocations = ConstructionLocationFilter.GetAllowedConstructionLocations(
                who.UniqueMultiplayerID,
                Context.IsMainPlayer,
                residenceSlotId,
                annexes
            );

            if (allowedLocations.Count == 0)
            {
                Game1.drawObjectDialogue("Debes reclamar una residencia en la Plaza Central antes de poder encargar obras de construcción.");
                return false;
            }

            // Si solo tiene su granja principal (sin anexos), abrimos directamente el menú de construcción en su granja
            if (allowedLocations.Count == 1)
            {
                string targetLocName = allowedLocations[0].Key;
                GameLocation? targetLoc = Game1.getLocationFromName(targetLocName);
                if (targetLoc != null)
                {
                    Game1.activeClickableMenu = new StardewValley.Menus.CarpenterMenu(builder, targetLoc);
                }
                else
                {
                    Game1.drawObjectDialogue($"No se pudo acceder a la ubicación '{targetLocName}'. Inténtalo de nuevo.");
                }
                return false;
            }

            // Si tiene anexos, le permitimos elegir entre su granja principal y sus propios anexos (nunca el host ni granjas ajenas)
            __instance.ShowPagedResponses(
                Game1.content.LoadString("Strings\\Buildings:Construction_ChooseLocation"),
                allowedLocations,
                delegate(string chosenLocationName)
                {
                    GameLocation? loc = Game1.getLocationFromName(chosenLocationName);
                    if (loc != null)
                    {
                        Game1.activeClickableMenu = new StardewValley.Menus.CarpenterMenu(builder, loc);
                    }
                },
                auto_select_single_choice: true
            );

            return false;
        }

        public static bool Prefix_FarmHouse_GetCellarName(FarmHouse __instance, ref string? __result)
        {
            if (__instance is Cabin cabin)
            {
                string? slotId = ResidenceUpgradeLogic.ExtractSlotIdFromCabinName(cabin.uniqueName.Value ?? cabin.NameOrUniqueName);
                if (string.IsNullOrEmpty(slotId) && cabin.ParentBuilding?.GetParentLocation() is GameLocation parentLoc)
                {
                    if (parentLoc.NameOrUniqueName.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        slotId = parentLoc.NameOrUniqueName;
                    }
                }

                if (!string.IsNullOrEmpty(slotId))
                {
                    __result = ResidenceUpgradeLogic.GetSlotCellarName(slotId);
                    return false;
                }
            }
            return true;
        }

        public static void Postfix_Building_getSourceRect(Building __instance, ref Microsoft.Xna.Framework.Rectangle __result)
        {
            if (__instance == null)
                return;

            Cabin? cabin = __instance.indoors.Value as Cabin;
            bool isCabin = cabin != null;
            int upgradeLevel = cabin?.upgradeLevel ?? 0;

            var (x, y) = ResidenceUpgradeLogic.AdjustFarmhouseBuildingSourceRect(
                __instance.buildingType.Value,
                isCabin,
                upgradeLevel,
                __result.X,
                __result.Y,
                __result.Width,
                __result.Height
            );

            __result.X = x;
            __result.Y = y;
        }

        public static bool Prefix_GreenhouseBuilding_OnUseHumanDoor(GreenhouseBuilding __instance, Farmer who, ref bool __result)
        {
            Farm? farm = __instance.GetParentLocation() as Farm ?? __instance.GetFarm();
            if (farm == null) return true;

            // Comprobar si ya está desbloqueado para la granja o el jugador
            bool isUnlocked = farm.greenhouseUnlocked.Value
                || who.mailReceived.Contains("ccPantry")
                || who.mailReceived.Contains("jojaPantry")
                || who.hasCompletedCommunityCenter()
                || Game1.MasterPlayer.mailReceived.Contains("ccPantry");

            if (isUnlocked)
            {
                if (!farm.greenhouseUnlocked.Value)
                {
                    farm.greenhouseUnlocked.Value = true;
                }
                __result = true;
                return false; // Permitir el acceso por la puerta
            }

            // Si está en ruinas, interceptar para ofrecer la pasarela interactiva de restauración
            if (who.IsLocalPlayer)
            {
                OfferGreenhouseRestorationDialogue(farm, __instance, who);
            }

            __result = false;
            return false;
        }

        public static void OfferGreenhouseRestorationDialogue(Farm farm, GreenhouseBuilding building, Farmer who)
        {
            string farmSlotId = farm.NameOrUniqueName;
            bool isOwner = false;
            if (context?.WorldState != null)
            {
                var assignment = context.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == farmSlotId);
                if (assignment != null && assignment.OwnerId == who.UniqueMultiplayerID)
                {
                    isOwner = true;
                }
            }
            if (Context.IsMainPlayer && farmSlotId == "Farm")
            {
                isOwner = true;
            }

            if (!isOwner)
            {
                Game1.drawObjectDialogue("El invernadero de esta residencia está en ruinas. Solo su propietario puede restaurarlo.");
                return;
            }

            var choices = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("materials", $"Restaurar con materiales ({ResidenceUpgradeLogic.GreenhouseRepairCostMaterials:N0}g, {ResidenceUpgradeLogic.GreenhouseRepairMaterialWood} Madera, {ResidenceUpgradeLogic.GreenhouseRepairMaterialStone} Piedra, {ResidenceUpgradeLogic.GreenhouseRepairMaterialGoldBars} Lingotes de Oro)"),
                new KeyValuePair<string, string>("joja", $"Contratar cuadrilla de obras ({ResidenceUpgradeLogic.GreenhouseRepairCostJoja:N0}g)"),
                new KeyValuePair<string, string>("leave", "Dejarlo de momento (o completar la Despensa del Centro Cívico)")
            };

            farm.ShowPagedResponses(
                "El invernadero de tu residencia está en ruinas. ¿Deseas repararlo?",
                choices,
                delegate (string choice)
                {
                    if (choice == "materials")
                    {
                        TryRepairGreenhouseWithMaterials(farm, who);
                    }
                    else if (choice == "joja")
                    {
                        TryRepairGreenhouseWithJoja(farm, who);
                    }
                }
            );
        }

        public static void TryRepairGreenhouseWithMaterials(Farm farm, Farmer who)
        {
            int woodCount = who.Items.CountId("(O)388");
            int stoneCount = who.Items.CountId("(O)390");
            int goldBarCount = who.Items.CountId("(O)336");

            if (who.Money < ResidenceUpgradeLogic.GreenhouseRepairCostMaterials
                || woodCount < ResidenceUpgradeLogic.GreenhouseRepairMaterialWood
                || stoneCount < ResidenceUpgradeLogic.GreenhouseRepairMaterialStone
                || goldBarCount < ResidenceUpgradeLogic.GreenhouseRepairMaterialGoldBars)
            {
                Game1.drawObjectDialogue($"No tienes suficientes recursos. Necesitas {ResidenceUpgradeLogic.GreenhouseRepairCostMaterials:N0}g, {ResidenceUpgradeLogic.GreenhouseRepairMaterialWood} Madera, {ResidenceUpgradeLogic.GreenhouseRepairMaterialStone} Piedra y {ResidenceUpgradeLogic.GreenhouseRepairMaterialGoldBars} Lingotes de Oro.");
                return;
            }

            who.Money -= ResidenceUpgradeLogic.GreenhouseRepairCostMaterials;
            who.Items.ReduceId("(O)388", ResidenceUpgradeLogic.GreenhouseRepairMaterialWood);
            who.Items.ReduceId("(O)390", ResidenceUpgradeLogic.GreenhouseRepairMaterialStone);
            who.Items.ReduceId("(O)336", ResidenceUpgradeLogic.GreenhouseRepairMaterialGoldBars);

            FinalizeGreenhouseRepair(farm, who, "¡Has reunido los materiales y reparado el invernadero de tu residencia! Ya está listo para cultivar en cualquier estación.");
        }

        public static void TryRepairGreenhouseWithJoja(Farm farm, Farmer who)
        {
            if (who.Money < ResidenceUpgradeLogic.GreenhouseRepairCostJoja)
            {
                Game1.drawObjectDialogue(Game1.content.LoadString("Strings\\UI:NotEnoughMoney3"));
                return;
            }

            who.Money -= ResidenceUpgradeLogic.GreenhouseRepairCostJoja;
            who.mailReceived.Add("jojaPantry");

            FinalizeGreenhouseRepair(farm, who, "¡La cuadrilla de obras ha restaurado por completo el invernadero de tu residencia! Ya está disponible para tus cultivos.");
        }

        public static void FinalizeGreenhouseRepair(Farm farm, Farmer who, string message)
        {
            who.mailReceived.Add("ccPantry");
            farm.greenhouseUnlocked.Value = true;

            if (Context.IsMainPlayer)
            {
                context?.Monitor.Log($"[FarmSlotModule] Invernadero reparado y desbloqueado en {farm.NameOrUniqueName} por {who.Name} ({who.UniqueMultiplayerID}).", LogLevel.Info);
            }
            else
            {
                context?.Helper.Multiplayer.SendMessage(
                    new UnlockGreenhousePayload { FarmSlotId = farm.NameOrUniqueName },
                    ModMessages.UnlockGreenhouseRequest,
                    new[] { context.Manifest.UniqueID },
                    new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );
            }

            farm.playSound("axchop");
            farm.playSound("money");
            Game1.drawObjectDialogue(message);
        }

        public static bool Prefix_IsCollidingPosition(GameLocation __instance, Microsoft.Xna.Framework.Rectangle position, ref bool __result)
        {
            if (__instance != null && HostPlazaLayout.IsPlazaLocation(__instance.NameOrUniqueName))
            {
                int leftTile = position.Left / 64;
                int rightTile = position.Right / 64;
                int centerTile = position.Center.X / 64;
                int bottomTile = position.Bottom / 64;
                int topTile = position.Top / 64;

                string locName = __instance.NameOrUniqueName;
                int slotCount = context?.Config.FarmSlotCount ?? 16;
                var plazaLoc = HostPlazaLayout.Find(locName, slotCount);

                if (plazaLoc != null)
                {
                    if (IsInPlazaPassage(plazaLoc, centerTile, bottomTile)
                        || IsInPlazaPassage(plazaLoc, leftTile, bottomTile)
                        || IsInPlazaPassage(plazaLoc, rightTile, bottomTile)
                        || IsInPlazaPassage(plazaLoc, centerTile, topTile))
                    {
                        __result = false;
                        return false;
                    }
                }

                foreach (var door in RegisteredDoors)
                {
                    if (!string.Equals(door.LocationName, locName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (IsInOpeningOrBorder(door, plazaLoc, centerTile, bottomTile)
                        || IsInOpeningOrBorder(door, plazaLoc, leftTile, bottomTile)
                        || IsInOpeningOrBorder(door, plazaLoc, rightTile, bottomTile)
                        || IsInOpeningOrBorder(door, plazaLoc, centerTile, topTile))
                    {
                        __result = false;
                        return false;
                    }
                }
            }
            return true;
        }

        public static bool Prefix_WarpFarmer(ref LocationRequest locationRequest, ref int tileX, ref int tileY, ref int facingDirectionAfterWarp)
        {
            if (locationRequest == null) return true;

            string targetLoc = locationRequest.Name;

            // Control de acceso continuo a granjas privadas instanciadas (Opción B: Bloqueo si está Locked)
            if (targetLoc.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string baseSlot = targetLoc;
                if (baseSlot.EndsWith("_Cave", StringComparison.OrdinalIgnoreCase))
                    baseSlot = baseSlot.Substring(0, baseSlot.Length - "_Cave".Length);
                else if (baseSlot.EndsWith(ResidenceUpgradeLogic.CellarSuffix, StringComparison.OrdinalIgnoreCase))
                    baseSlot = baseSlot.Substring(0, baseSlot.Length - ResidenceUpgradeLogic.CellarSuffix.Length);
                else if (baseSlot.EndsWith(ResidenceUpgradeLogic.GreenhouseSuffix, StringComparison.OrdinalIgnoreCase))
                    baseSlot = baseSlot.Substring(0, baseSlot.Length - ResidenceUpgradeLogic.GreenhouseSuffix.Length);
                // Interior de la residencia: aplicar la política de la parcela y no crear una granja "…_Home"
                else if (baseSlot.EndsWith(ResidenceUpgradeLogic.CabinHomeSuffix, StringComparison.OrdinalIgnoreCase))
                    baseSlot = baseSlot.Substring(0, baseSlot.Length - ResidenceUpgradeLogic.CabinHomeSuffix.Length);

                var assignment = context?.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == baseSlot);

                if (Context.IsMainPlayer && locationRequest.Location == null)
                {
                    if (assignment != null)
                    {
                        EnsureFarmSlotExists(baseSlot);
                        locationRequest = Game1.getLocationRequest(targetLoc);
                    }
                }
                if (assignment != null)
                {
                    long myId = Game1.player.UniqueMultiplayerID;
                    if (assignment.OwnerId != myId && assignment.Policy == AccessPolicy.Locked)
                    {
                        Game1.showRedMessage("Esta parcela es privada y está cerrada con llave.");
                        // Detener al jugador tanto en la Granja Host como en los mapas puente de la plaza
                        if (Game1.currentLocation != null && (Game1.currentLocation is Farm || HostPlazaLayout.IsPlazaLocation(Game1.currentLocation.NameOrUniqueName)))
                        {
                            Game1.player.Halt();
                            Game1.player.completelyStopAnimatingOrDoingAction();
                        }
                        return false;
                    }
                }

                // Ajustar orientación adecuada únicamente al entrar a la residencia desde el portal exterior del host
                if (string.Equals(targetLoc, baseSlot, StringComparison.OrdinalIgnoreCase))
                {
                    var doorTarget = RegisteredDoors.FirstOrDefault(d => d.DefaultSlotId == targetLoc);
                    if (doorTarget != null)
                    {
                        string portalOrigin = string.IsNullOrWhiteSpace(doorTarget.LocationName) ? "Farm" : doorTarget.LocationName;
                        string currentLocName = Game1.currentLocation?.NameOrUniqueName ?? string.Empty;
                        if (string.Equals(currentLocName, portalOrigin, StringComparison.OrdinalIgnoreCase))
                        {
                            string? targetFarmType = context?.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == targetLoc)?.FarmType;
                            GetResidenceArrivalCoordinates(doorTarget, targetFarmType, out _, out _, out int facingDir);
                            facingDirectionAfterWarp = facingDir;
                        }
                    }
                }
            }
            else if (targetLoc.StartsWith(LocationCullingLogic.AnnexPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (Context.IsMainPlayer && locationRequest.Location == null)
                {
                    AnnexAssignment? annexForLoc = context?.WorldState.FindAnnexById(targetLoc);
                    if (annexForLoc != null)
                    {
                        EnsureAnnexLocation(annexForLoc, regenerate: false);
                        locationRequest = Game1.getLocationRequest(targetLoc);
                    }
                }

                // TASK-903: Control de acceso heredado de la residencia para ubicaciones de anexo
                AnnexAssignment? annex = context?.WorldState.FindAnnexById(targetLoc);
                DoorAssignment? parentDoor = annex != null
                    ? context?.WorldState.Doors.Values.FirstOrDefault(d => string.Equals(d.FarmSlotId, annex.ResidenceSlotId, StringComparison.OrdinalIgnoreCase))
                    : null;

                long? ownerId = parentDoor?.OwnerId ?? annex?.OwnerId;
                AccessPolicy policy = parentDoor?.Policy ?? AccessPolicy.Locked;
                long myId = Game1.player.UniqueMultiplayerID;

                if (ownerId.HasValue && ownerId.Value != myId && policy == AccessPolicy.Locked)
                {
                    Game1.showRedMessage("Este anexo es privado y está cerrado con llave.");
                    Game1.player.Halt();
                    Game1.player.completelyStopAnimatingOrDoingAction();
                    return false;
                }
            }

            // Si el jugador está saliendo de una cabaña hacia la granja principal
            if (Game1.currentLocation is Cabin && targetLoc == "Farm")
            {
                var assignment = context?.WorldState.Doors.Values.FirstOrDefault(d => d.OwnerId == Game1.player.UniqueMultiplayerID);
                if (assignment != null && RegisteredDoors.FirstOrDefault(d => d.DoorId == assignment.DoorId) is { } door)
                {
                    ResolvePlazaReturn(door, ref locationRequest, ref tileX, ref tileY, ref facingDirectionAfterWarp);
                }
                else
                {
                    // Si aún no tiene parcela reclamada, ubicarlo en la plaza central de la Granja Host
                    locationRequest = Game1.getLocationRequest("Farm");
                    tileX = 64;
                    tileY = 16;
                    facingDirectionAfterWarp = 2;
                }
                return true;
            }

            // Si el jugador está en una granja privada o en su cueva
            string curLoc = Game1.currentLocation?.NameOrUniqueName ?? string.Empty;

            // Interceptar salida desde la cueva de una residencia privada
            if (curLoc.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase)
                && curLoc.EndsWith("_Cave", StringComparison.OrdinalIgnoreCase))
            {
                string parentFarmId = curLoc.Substring(0, curLoc.Length - "_Cave".Length);
                string? farmType = context?.WorldState.Doors.Values
                    .FirstOrDefault(d => string.Equals(d.FarmSlotId, parentFarmId, StringComparison.OrdinalIgnoreCase))?.FarmType;
                var template = FarmTemplateCatalog.GetTemplate(farmType);

                locationRequest = Game1.getLocationRequest(parentFarmId);
                tileX = template.CaveExitX;
                tileY = template.CaveExitY;
                facingDirectionAfterWarp = 2; // Sur
                return true;
            }

            // Interceptar entrada a la cueva desde una residencia privada
            if (curLoc.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase)
                && !curLoc.EndsWith("_Cave", StringComparison.OrdinalIgnoreCase)
                && string.Equals(targetLoc, "FarmCave", StringComparison.OrdinalIgnoreCase))
            {
                string caveId = $"{curLoc}_Cave";
                locationRequest = Game1.getLocationRequest(caveId);
                tileX = 8;
                tileY = 11;
                facingDirectionAfterWarp = 0; // Norte
                return true;
            }

            // Interceptar salida desde el invernadero de una residencia privada si el warp aún apuntara a "Farm"
            if (curLoc.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase)
                && curLoc.EndsWith(ResidenceUpgradeLogic.GreenhouseSuffix, StringComparison.OrdinalIgnoreCase)
                && string.Equals(targetLoc, "Farm", StringComparison.OrdinalIgnoreCase))
            {
                string? parentFarmId = ResidenceUpgradeLogic.ExtractSlotIdFromGreenhouseName(curLoc);
                if (!string.IsNullOrEmpty(parentFarmId))
                {
                    locationRequest = Game1.getLocationRequest(parentFarmId);
                    if (Game1.getLocationFromName(parentFarmId) is Farm parentFarm)
                    {
                        Building? gh = parentFarm.getBuildingByType("Greenhouse");
                        if (gh != null)
                        {
                            tileX = gh.tileX.Value + gh.humanDoor.X;
                            tileY = gh.tileY.Value + gh.humanDoor.Y + 1;
                        }
                    }
                    facingDirectionAfterWarp = 2; // Sur
                    return true;
                }
            }

            // Interceptar entrada al invernadero desde una residencia privada si el warp apuntara al "Greenhouse" global
            if (curLoc.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase)
                && !curLoc.EndsWith(ResidenceUpgradeLogic.GreenhouseSuffix, StringComparison.OrdinalIgnoreCase)
                && string.Equals(targetLoc, "Greenhouse", StringComparison.OrdinalIgnoreCase))
            {
                string ghId = ResidenceUpgradeLogic.GetSlotGreenhouseName(curLoc);
                locationRequest = Game1.getLocationRequest(ghId);
                tileX = 10;
                tileY = 23;
                facingDirectionAfterWarp = 0; // Norte
                return true;
            }

            // Si el jugador está saliendo de una granja instanciada privada hacia el exterior
            if (curLoc.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var door = RegisteredDoors.FirstOrDefault(d => d.DefaultSlotId == curLoc);
                if (door != null)
                {
                    if (SpatialWarpRouter.ShouldReturnToPlaza(door.Edge, targetLoc))
                    {
                        ResolvePlazaReturn(door, ref locationRequest, ref tileX, ref tileY, ref facingDirectionAfterWarp);
                        return true;
                    }

                    // TASK-904: Sustituir salidas vanilla de las residencias (Backwoods, BusStop, Forest)
                    AnnexSide? side = ResidenceExits.SideForVanillaTarget(targetLoc);
                    if (side.HasValue)
                    {
                        var annex = context?.WorldState.FindAnnex(curLoc, side.Value);
                        if (annex != null)
                        {
                            var template = AnnexTemplateCatalog.GetTemplate(annex.TemplateId);
                            var pts = AnnexEntrance.For(side.Value, template.Width, template.Height);
                            locationRequest = Game1.getLocationRequest(annex.AnnexId);
                            tileX = pts.EntryTile.X;
                            tileY = pts.EntryTile.Y;
                            facingDirectionAfterWarp = pts.FacingDirection;
                            return true;
                        }
                        else
                        {
                            Game1.player.Halt();
                            Game1.player.completelyStopAnimatingOrDoingAction();

                            var assignment = context?.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == curLoc);
                            if (assignment != null && assignment.OwnerId == Game1.player.UniqueMultiplayerID)
                            {
                                ShowBuyAnnexDialog(curLoc, side.Value);
                            }
                            else
                            {
                                Game1.drawObjectDialogue("Este paso está cerrado. Solo el propietario de la residencia puede adquirir una ampliación aquí.");
                            }
                            return false;
                        }
                    }
                }
            }

            // TASK-904: Si el jugador está saliendo de un anexo de residencia hacia la residencia madre
            if (curLoc.StartsWith(LocationCullingLogic.AnnexPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var annex = context?.WorldState.FindAnnexById(curLoc);
                if (annex != null)
                {
                    string? residenceFarmType = context?.WorldState.Doors.Values
                        .FirstOrDefault(d => string.Equals(d.FarmSlotId, annex.ResidenceSlotId, StringComparison.OrdinalIgnoreCase))?.FarmType;
                    var template = AnnexTemplateCatalog.GetTemplate(annex.TemplateId);
                    var pts = AnnexEntrance.For(annex.Side, template.Width, template.Height, residenceFarmType);
                    locationRequest = Game1.getLocationRequest(annex.ResidenceSlotId);
                    tileX = pts.ResidenceReturnTile.X;
                    tileY = pts.ResidenceReturnTile.Y;
                    facingDirectionAfterWarp = pts.ResidenceReturnFacingDirection;
                    return true;
                }
            }
            return true;
        }

        private static void ResolvePlazaReturn(DoorModel door, ref LocationRequest locationRequest, ref int tileX, ref int tileY, ref int facingDirectionAfterWarp)
        {
            int slotCount = context?.Config.FarmSlotCount ?? 16;
            var returnTarget = PlazaTopologyVerifier.ResolvePlazaReturnTarget(
                door.LocationName,
                door.Edge,
                door.TownTileX,
                door.TownTileY,
                slotCount,
                name => Game1.getLocationFromName(name) != null);

            if (returnTarget.IsFallback)
            {
                context?.Monitor.Log($"[FarmSlotModule] Destino de retorno a plaza '{door.LocationName}' para puerta {door.DoorId} no está activo o no existe (slotCount={slotCount}). Redirigiendo a Granja Host central.", LogLevel.Warn);
            }

            locationRequest = Game1.getLocationRequest(returnTarget.TargetLocation);
            tileX = returnTarget.TileX;
            tileY = returnTarget.TileY;
            facingDirectionAfterWarp = returnTarget.FacingDirection;
        }

        /// <summary>
        /// TASK-605: Redirige el uso del Tótem de Teletransporte a la Granja ((O)688) hacia la residencia privada del jugador.
        /// </summary>
        public static bool Prefix_TotemWarpForReal(StardewValley.Object __instance)
        {
            if (__instance.QualifiedItemId != "(O)688")
                return true;

            var assignment = context?.WorldState.Doors.Values
                .FirstOrDefault(d => d.OwnerId == Game1.player.UniqueMultiplayerID);

            if (assignment != null && !string.IsNullOrEmpty(assignment.FarmSlotId))
            {
                string slotId = assignment.FarmSlotId;
                Point warpPoint = GetResidenceTotemPoint(slotId, assignment.FarmType);
                Game1.warpFarmer(slotId, warpPoint.X, warpPoint.Y, flip: false);

                // Mismo cierre que Object.totemWarpForReal: la animación del tótem dejó al jugador oculto e invulnerable
                Game1.fadeToBlackAlpha = 0.99f;
                Game1.screenGlow = false;
                Game1.player.temporarilyInvincible = false;
                Game1.player.temporaryInvincibilityTimer = 0;
                Game1.displayFarmer = true;

                context?.Monitor.Log($"[FarmSlotModule] Tótem de granja utilizado por {Game1.player.Name}: teletransportado a residencia {slotId} ({warpPoint.X}, {warpPoint.Y}).", LogLevel.Info);
                return false;
            }

            return true;
        }

        /// <summary>
        /// TASK-605: Redirige la interacción con el Obelisco de la Granja en Isla Jengibre hacia la residencia privada del jugador.
        /// </summary>
        public static bool Prefix_IslandWestPerformAction(IslandWest __instance, string[] action, Farmer who, ref bool __result)
        {
            if (ArgUtility.Get(action, 0) != "FarmObelisk")
                return true;

            var assignment = context?.WorldState.Doors.Values
                .FirstOrDefault(d => d.OwnerId == who.UniqueMultiplayerID);

            if (assignment == null || string.IsNullOrEmpty(assignment.FarmSlotId))
                return true;

            for (int i = 0; i < 12; i++)
            {
                who.currentLocation.temporarySprites.Add(new TemporaryAnimatedSprite(354, Game1.random.Next(25, 75), 6, 1, new Vector2(Game1.random.Next((int)who.Position.X - 256, (int)who.Position.X + 192), Game1.random.Next((int)who.Position.Y - 256, (int)who.Position.Y + 192)), flicker: false, Game1.random.NextBool()));
            }
            who.currentLocation.playSound("wand");
            Game1.displayFarmer = false;
            who.temporarilyInvincible = true;
            who.temporaryInvincibilityTimer = -2000;
            who.freezePause = 1000;
            Game1.flashAlpha = 1f;

            string slotId = assignment.FarmSlotId;
            string farmType = assignment.FarmType;

            DelayedAction.fadeAfterDelay(delegate
            {
                Point warpPoint = GetResidenceTotemPoint(slotId, farmType);
                Game1.warpFarmer(slotId, warpPoint.X, warpPoint.Y, flip: false);
                Game1.fadeToBlackAlpha = 0.99f;
                Game1.screenGlow = false;
                who.temporarilyInvincible = false;
                who.temporaryInvincibilityTimer = 0;
                Game1.displayFarmer = true;
            }, 1000);

            __result = true;
            return false;
        }

        private static Point GetResidenceTotemPoint(string slotId, string? farmType)
        {
            GameLocation? residenceLoc = Game1.getLocationFromName(slotId);
            if (residenceLoc != null && residenceLoc.TryGetMapPropertyAs("WarpTotemEntry", out Point parsed, required: false))
            {
                return parsed;
            }

            FarmTemplate template = FarmTemplateCatalog.GetTemplate(farmType);
            return new Point(template.MailboxTileX + 1, template.MailboxTileY + 2);
        }

        /// <summary>
        /// Resuelve y asegura la instancia de la cabaña (Cabin) asociada a un hueco de residencia o nombre de ubicación.
        /// Funciona de forma segura tanto en el Host como en el Cliente sin duplicar ubicaciones en Game1.locations.
        /// </summary>
        public static Cabin? ResolveResidenceCabin(string locationOrSlotName, DoorAssignment? assignment = null)
        {
            if (string.IsNullOrWhiteSpace(locationOrSlotName))
                return null;

            string slotId = locationOrSlotName;
            if (slotId.EndsWith("_Home", StringComparison.OrdinalIgnoreCase))
            {
                slotId = slotId.Substring(0, slotId.Length - 5);
            }
            else if (assignment != null && !string.IsNullOrEmpty(assignment.FarmSlotId))
            {
                slotId = assignment.FarmSlotId;
            }

            assignment ??= context?.WorldState?.Doors?.Values.FirstOrDefault(d => string.Equals(d.FarmSlotId, slotId, StringComparison.OrdinalIgnoreCase));

            Farm? farm = Game1.getLocationFromName(slotId) as Farm;
            if (farm == null && Context.IsMainPlayer)
            {
                if (assignment != null)
                {
                    farm = EnsureFarmSlotExists(slotId);
                }
            }

            if (farm == null)
                return null;

            Building? farmhouse = farm.getBuildingByType("Farmhouse")
                               ?? farm.buildings.FirstOrDefault(b => b.buildingType.Value.Contains("Farmhouse", StringComparison.OrdinalIgnoreCase));

            Point entryDoor = farm.GetMainFarmHouseEntry();
            Vector2 starterTile = farm.GetStarterFarmhouseLocation();

            if (farmhouse == null)
            {
                farmhouse = new Building("Farmhouse", starterTile);
                farmhouse.load();
                farm.buildings.Add(farmhouse);
            }

            string cabinName = $"{slotId}_Home";
            Cabin? cabin = farmhouse.GetIndoors() as Cabin;

            if (cabin == null)
            {
                cabin = new Cabin("Maps\\FarmHouse");
                cabin.uniqueName.Value = cabinName;
                cabin.IsFarm = true;
                farmhouse.indoors.Value = cabin;
                farmhouse.nonInstancedIndoorsName.Value = null;
            }

            if (Game1.locations != null && Game1.locations.Contains(cabin))
            {
                Game1.locations.Remove(cabin);
            }

            CleanAndArrangeResidenceInterior(cabin);

            int doorX = entryDoor.X != 0 ? entryDoor.X : (farmhouse.tileX.Value + farmhouse.humanDoor.X);
            int doorY = entryDoor.Y != 0 ? entryDoor.Y : (farmhouse.tileY.Value + farmhouse.humanDoor.Y);

            if (cabin.warps.Count == 0)
            {
                cabin.warps.Add(new Warp(3, 11, farm.NameOrUniqueName, doorX, doorY + 1, false));
            }
            else
            {
                cabin.warps[0].TargetName = farm.NameOrUniqueName;
                cabin.warps[0].TargetX = doorX;
                cabin.warps[0].TargetY = doorY + 1;
            }
            farmhouse.updateInteriorWarps();

            return cabin;
        }

        public static bool Prefix_ApplyWakeUpPosition(Farmer who)
        {
            try
            {
                SanitizePlayerOrphanedBridgeLocation(who, context?.Config.FarmSlotCount ?? 16);

                // 1. Identificar si el jugador posee una residencia asignada en WorldState
                DoorAssignment? assignment = context?.WorldState?.Doors?.Values
                    .FirstOrDefault(d => d.OwnerId == who.UniqueMultiplayerID);

                string? assignedSlotId = assignment?.FarmSlotId;
                if (string.IsNullOrEmpty(assignedSlotId) && context?.WorldState?.Players != null
                    && context.WorldState.Players.TryGetValue(who.UniqueMultiplayerID, out var playerRec) && !string.IsNullOrEmpty(playerRec.FarmSlotId))
                {
                    assignedSlotId = playerRec.FarmSlotId;
                }

                if (!string.IsNullOrEmpty(assignedSlotId))
                {
                    who.homeLocation.Value = $"{assignedSlotId}_Home";
                }

                // 2. Intentar despertar en la ubicación de última desconexión si es el mismo día
                string? discName = who.disconnectLocation.Value;
                GameLocation? disconnectLoc = !string.IsNullOrEmpty(discName)
                    ? (Game1.getLocationFromName(discName) ?? (discName.EndsWith("_Home", StringComparison.OrdinalIgnoreCase) && assignment != null ? ResolveResidenceCabin(discName, assignment) : null))
                    : null;

                if (disconnectLoc != null && who.disconnectDay.Value == Game1.MasterPlayer.stats.DaysPlayed && !Game1.newDaySync.hasInstance())
                {
                    who.currentLocation = disconnectLoc;
                    who.Position = who.disconnectPosition.Value;
                }
                else
                {
                    // 3. Intentar despertar en la cama donde durmió
                    string? lastSleep = who.lastSleepLocation.Value;
                    GameLocation? lastSleepLoc = !string.IsNullOrEmpty(lastSleep)
                        ? (Game1.getLocationFromName(lastSleep) ?? (lastSleep.EndsWith("_Home", StringComparison.OrdinalIgnoreCase) && assignment != null ? ResolveResidenceCabin(lastSleep, assignment) : null))
                        : null;

                    if (lastSleepLoc == null && !string.IsNullOrEmpty(assignedSlotId))
                    {
                        lastSleepLoc = ResolveResidenceCabin($"{assignedSlotId}_Home", assignment);
                    }

                    if (lastSleepLoc != null && (lastSleepLoc.CanWakeUpHere(who) || lastSleepLoc is FarmHouse))
                    {
                        who.currentLocation = lastSleepLoc;
                        Point bedSpot = who.lastSleepPoint.Value;
                        if (bedSpot == Point.Zero)
                        {
                            if (lastSleepLoc is FarmHouse fh)
                            {
                                bedSpot = fh.GetPlayerBedSpot();
                            }
                            if (bedSpot == Point.Zero)
                            {
                                bedSpot = new Point(ResidenceInteriorLogic.BedPlacement.TargetX, ResidenceInteriorLogic.BedPlacement.TargetY);
                            }
                        }
                        who.Position = Utility.PointToVector2(bedSpot) * 64f;
                        try
                        {
                            BedFurniture.ShiftPositionForBed(who);
                        }
                        catch
                        {
                            // Ignorar excepciones secundarias al ajustar animación de cama
                        }
                    }
                    else
                    {
                        // 4. Intentar despertar en su FarmHouse / Cabin de residencia
                        Cabin? homeCabin = !string.IsNullOrEmpty(assignedSlotId)
                            ? ResolveResidenceCabin($"{assignedSlotId}_Home", assignment)
                            : null;

                        FarmHouse? farmHouse = homeCabin;

                        if (farmHouse != null)
                        {
                            who.currentLocation = farmHouse;
                            Point bedSpot = farmHouse.GetPlayerBedSpot();
                            if (bedSpot == Point.Zero)
                            {
                                bedSpot = new Point(ResidenceInteriorLogic.BedPlacement.TargetX, ResidenceInteriorLogic.BedPlacement.TargetY);
                            }
                            who.Position = Utility.PointToVector2(bedSpot) * 64f;
                            try
                            {
                                BedFurniture.ShiftPositionForBed(who);
                            }
                            catch
                            {
                            }
                        }
                        else if (!string.IsNullOrEmpty(assignedSlotId) && Game1.getLocationFromName(assignedSlotId) is Farm assignedFarm)
                        {
                            // 5. Fallback a su propia granja privada (porche / buzón), ¡nunca al anfitrión ni a Pierre!
                            Point mailbox = GetResidenceTotemPoint(assignedSlotId, assignment?.FarmType);
                            who.currentLocation = assignedFarm;
                            who.Position = new Vector2(mailbox.X * 64f, mailbox.Y * 64f);
                        }
                        else
                        {
                            // 6. Fallback final: sin residencia cargada (en un cliente la granja privada puede estar fuera
                            // del culling de red) o sin slot conocido. Posición provisional; si hay slot, se viaja a él después.
                            if (!string.IsNullOrEmpty(assignedSlotId) && who == Game1.player)
                                pendingResidenceWakeSlotId = assignedSlotId;

                            GameLocation fallback = Game1.getLocationFromName("Farm")
                                                 ?? Game1.getLocationFromName("Town")
                                                 ?? Game1.locations.FirstOrDefault()
                                                 ?? new GameLocation("Maps\\Farm", "Farm");

                            who.currentLocation = fallback;
                            who.Position = fallback.NameOrUniqueName == "Farm"
                                ? new Vector2(64f * 64f, 15f * 64f)
                                : new Vector2(42f * 64f, 56f * 64f);
                        }
                    }
                }

                if (who == Game1.player)
                {
                    Game1.currentLocation = who.currentLocation;
                }

                return false;
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[FarmSlotModule] Advertencia en Prefix_ApplyWakeUpPosition: {ex.Message}. Aplicando recuperación segura.", LogLevel.Warn);
                DoorAssignment? assignment = context?.WorldState?.Doors?.Values
                    .FirstOrDefault(d => d.OwnerId == who.UniqueMultiplayerID);
                string? assignedSlotId = assignment?.FarmSlotId;
                if (string.IsNullOrEmpty(assignedSlotId) && context?.WorldState?.Players != null
                    && context.WorldState.Players.TryGetValue(who.UniqueMultiplayerID, out var pRec) && !string.IsNullOrEmpty(pRec.FarmSlotId))
                {
                    assignedSlotId = pRec.FarmSlotId;
                }
                if (!string.IsNullOrEmpty(assignedSlotId))
                {
                    Cabin? cabin = ResolveResidenceCabin($"{assignedSlotId}_Home", assignment);
                    if (cabin != null)
                    {
                        who.currentLocation = cabin;
                        who.Position = new Vector2(ResidenceInteriorLogic.BedPlacement.TargetX * 64f, ResidenceInteriorLogic.BedPlacement.TargetY * 64f);
                        if (who == Game1.player) Game1.currentLocation = cabin;
                        return false;
                    }
                }

                if (!string.IsNullOrEmpty(assignedSlotId) && who == Game1.player)
                    pendingResidenceWakeSlotId = assignedSlotId;

                GameLocation fallback = Game1.getLocationFromName("Farm") ?? Game1.currentLocation;
                who.currentLocation = fallback;
                who.Position = new Vector2(64f * 64f, 15f * 64f);
                if (who == Game1.player)
                    Game1.currentLocation = who.currentLocation;
                return false;
            }
        }

        private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
        {
            if (!Context.IsWorldReady || context == null || Game1.currentLocation == null)
                return;

            string curLoc = Game1.currentLocation.NameOrUniqueName;
            long myId = Game1.player.UniqueMultiplayerID;
            Vector2 playerTile = Game1.player.Tile;

            foreach (DoorModel door in RegisteredDoors)
            {
                string doorLoc = string.IsNullOrWhiteSpace(door.LocationName) ? "Farm" : door.LocationName;
                if (!string.Equals(curLoc, doorLoc, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!IsDoorActiveForCurrentConfig(door))
                    continue;

                var (t1, t2) = GetOpeningTiles(door);
                float d1 = Vector2.Distance(playerTile, new Vector2(t1.X, t1.Y));
                float d2 = Vector2.Distance(playerTile, new Vector2(t2.X, t2.Y));
                float dist = Math.Min(d1, d2);
                if (dist > 2.8f)
                    continue;

                bool isClaimed = context.WorldState.Doors.TryGetValue(door.DoorId, out DoorAssignment? assignment);
                bool isMine = isClaimed && assignment?.OwnerId == myId;

                string badgeText;
                Color badgeColor;

                if (!isClaimed)
                {
                    badgeText = "[E / Clic o Caminar] Fundar Parcela";
                    badgeColor = Color.LightGreen;
                }
                else if (isMine)
                {
                    badgeText = "🏠 Mi Parcela (Caminar para entrar)";
                    badgeColor = Color.Cyan;
                }
                else
                {
                    string owner = "Vecino";
                    if (context.WorldState.Players.TryGetValue(assignment!.OwnerId, out var rec) && !string.IsNullOrWhiteSpace(rec.PlayerName))
                        owner = rec.PlayerName;
                    badgeText = $"🏠 Parcela de {owner}";
                    badgeColor = Color.LightGoldenrodYellow;
                }

                float bounce = (float)Math.Sin(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 200.0) * 3f;
                float centerX = (t1.X + t2.X) / 2f * 64f + 32f;
                float centerY = (t1.Y + t2.Y) / 2f * 64f - 24f + bounce;
                Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, new Vector2(centerX, centerY));
                DrawDoorBadge(e.SpriteBatch, badgeText, screenPos, badgeColor);
            }

            // TASK-904 / TASK-905: Indicadores sobre salidas libres y anexos en la residencia privada
            if (curLoc.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var door = RegisteredDoors.FirstOrDefault(d => d.DefaultSlotId == curLoc);
                if (door != null)
                {
                    var freeSides = ResidenceExits.FreeSides(door.Edge);
                    var doorAssignment = context.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == curLoc);
                    bool isMine = doorAssignment != null && doorAssignment.OwnerId == myId;

                    var resTemplate = FarmTemplateCatalog.GetTemplate(doorAssignment?.FarmType);
                    foreach (AnnexSide side in freeSides)
                    {
                        Point sideTile = side switch
                        {
                            AnnexSide.North => new Point(resTemplate.NorthArrivalTileX, resTemplate.NorthArrivalTileY),
                            AnnexSide.East => new Point(resTemplate.EastArrivalTileX, resTemplate.EastArrivalTileY),
                            AnnexSide.South => new Point(resTemplate.SouthArrivalTileX, resTemplate.SouthArrivalTileY),
                            _ => Point.Zero
                        };

                        float dist = Vector2.Distance(playerTile, new Vector2(sideTile.X, sideTile.Y));
                        if (dist > 3.5f) continue;

                        var annex = context.WorldState.FindAnnex(curLoc, side);
                        string badgeText;
                        Color badgeColor;

                        if (annex != null)
                        {
                            var template = AnnexTemplateCatalog.GetTemplate(annex.TemplateId);
                            badgeText = $"🌲 {template.DisplayName} (Caminar para entrar)";
                            badgeColor = Color.Cyan;
                        }
                        else if (isMine)
                        {
                            badgeText = "[E / Clic o Caminar] Adquirir Anexo";
                            badgeColor = Color.LightGreen;
                        }
                        else
                        {
                            badgeText = "🚫 Ampliación no disponible";
                            badgeColor = Color.LightGray;
                        }

                        float bounce = (float)Math.Sin(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 200.0) * 3f;
                        float centerX = sideTile.X * 64f + 32f;
                        float centerY = sideTile.Y * 64f - 24f + bounce;
                        Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, new Vector2(centerX, centerY));
                        DrawDoorBadge(e.SpriteBatch, badgeText, screenPos, badgeColor);
                    }
                }
            }
        }

        private static void DrawDoorBadge(SpriteBatch b, string text, Vector2 screenPos, Color textColor)
        {
            if (Game1.smallFont == null || Game1.fadeToBlackRect == null)
                return;

            Vector2 textSize = Game1.smallFont.MeasureString(text);
            Rectangle bgRect = new Rectangle(
                (int)(screenPos.X - (textSize.X / 2f) - 8),
                (int)(screenPos.Y - textSize.Y - 6),
                (int)textSize.X + 16,
                (int)textSize.Y + 8
            );

            b.Draw(Game1.fadeToBlackRect, bgRect, new Color(0, 0, 0, 180));
            Utility.drawTextWithShadow(
                b,
                text,
                Game1.smallFont,
                new Vector2(screenPos.X - (textSize.X / 2f), screenPos.Y - textSize.Y - 2),
                textColor
            );
        }

        public static bool Prefix_Farmer_PerformBeginUsingTool(Farmer __instance)
        {
            if (__instance != Game1.player)
                return true;

            GameLocation? location = __instance.currentLocation;
            Tool? tool = __instance.CurrentTool;

            if (location != null && tool != null && LocationCullingLogic.IsPrivateFarmOrAnnex(location.NameOrUniqueName))
            {
                long? ownerId = location.GetFarmOwnerId();
                if (ownerId.HasValue && ownerId.Value != __instance.UniqueMultiplayerID)
                {
                    if (tool is Pickaxe or Axe or Hoe)
                    {
                        Game1.showRedMessage("No tienes permiso para modificar esta granja.");
                        __instance.completelyStopAnimatingOrDoingAction();
                        return false;
                    }
                }
            }
            return true;
        }

        public static bool Prefix_ToolBeginUsing(Tool __instance, GameLocation location, Farmer who)
        {
            if (who != Game1.player)
                return true;

            if (location != null && LocationCullingLogic.IsPrivateFarmOrAnnex(location.NameOrUniqueName))
            {
                long? ownerId = location.GetFarmOwnerId();
                if (ownerId.HasValue && ownerId.Value != who.UniqueMultiplayerID)
                {
                    if (__instance is Pickaxe or Axe or Hoe)
                    {
                        Game1.showRedMessage("No tienes permiso para modificar esta granja.");
                        who.completelyStopAnimatingOrDoingAction();
                        return false;
                    }
                }
            }
            return true;
        }

        public static bool Prefix_ChestCheckForAction(StardewValley.Objects.Chest __instance, Farmer who, bool justCheckingForActivity, ref bool __result)
        {
            if (justCheckingForActivity || who != Game1.player)
                return true;

            GameLocation? loc = __instance.Location ?? Game1.currentLocation;
            if (loc != null && LocationCullingLogic.IsPrivateFarmOrAnnex(loc.NameOrUniqueName))
            {
                long? ownerId = loc.GetFarmOwnerId();
                if (ownerId.HasValue && ownerId.Value != who.UniqueMultiplayerID)
                {
                    Game1.showRedMessage("Este cofre es privado.");
                    __result = false;
                    return false;
                }
            }
            return true;
        }

        public static void Postfix_IsAlwaysActiveLocation(GameLocation location, ref bool __result)
        {
            if (location == null || string.IsNullOrEmpty(location.NameOrUniqueName))
                return;

            if (!LocationCullingLogic.IsPrivateFarmOrAnnex(location.NameOrUniqueName))
                return;

            if (context == null) return;

            string locName = location.NameOrUniqueName;
            string baseSlotId = locName;
            if (locName.EndsWith("_Cave", StringComparison.OrdinalIgnoreCase))
            {
                baseSlotId = locName.Substring(0, locName.Length - "_Cave".Length);
            }
            else if (locName.EndsWith(ResidenceUpgradeLogic.CellarSuffix, StringComparison.OrdinalIgnoreCase))
            {
                baseSlotId = locName.Substring(0, locName.Length - ResidenceUpgradeLogic.CellarSuffix.Length);
            }
            else if (locName.EndsWith(ResidenceUpgradeLogic.GreenhouseSuffix, StringComparison.OrdinalIgnoreCase))
            {
                baseSlotId = locName.Substring(0, locName.Length - ResidenceUpgradeLogic.GreenhouseSuffix.Length);
            }

            var doorAssignment = context.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == baseSlotId);
            long? ownerId = doorAssignment?.OwnerId
                         ?? context.WorldState.FindAnnexById(location.NameOrUniqueName)?.OwnerId
                         ?? location.GetFarmOwnerId();

            var onlineFarmerIds = Game1.getOnlineFarmers().Select(f => f.UniqueMultiplayerID).ToHashSet();

            __result = LocationCullingLogic.ShouldKeepActive(
                location.NameOrUniqueName,
                location.farmers.Count,
                ownerId,
                onlineFarmerIds
            );
        }

        /// <summary>
        /// Reordena el mobiliario y objetos de inicio del interior de una residencia (Cabin)
        /// para garantizar un espacio acogedor, transitable y sin apelotonamiento en la pared norte,
        /// restaurando además la chimenea, ventana, cuadro y alfombra si la cabaña carecía de ellos.
        /// </summary>
        public static void CleanAndArrangeResidenceInterior(Cabin cabin)
        {
            if (cabin == null) return;

            RepositionResidenceFurniture(cabin);
            EnsureStarterDecorations(cabin);
            RepositionStarterChests(cabin);
        }

        private static void RepositionResidenceFurniture(Cabin cabin)
        {
            if (cabin == null || cabin.furniture == null) return;

            foreach (var furn in cabin.furniture)
            {
                string name = furn.Name ?? string.Empty;
                int curX = (int)furn.TileLocation.X;
                int curY = (int)furn.TileLocation.Y;
                int curRot = furn.currentRotation.Value;

                if (ResidenceInteriorLogic.IsMisplacedStarterFurniture(name, curX, curY, curRot))
                {
                    if (name.Contains("Table", StringComparison.OrdinalIgnoreCase))
                    {
                        furn.TileLocation = new Vector2(ResidenceInteriorLogic.DiningTablePlacement.TargetX, ResidenceInteriorLogic.DiningTablePlacement.TargetY);
                        furn.currentRotation.Value = ResidenceInteriorLogic.DiningTablePlacement.Rotation;
                        furn.updateRotation();
                        furn.RecalculateBoundingBox();
                        furn.updateDrawPosition();
                    }
                    else if (name.Contains("Chair", StringComparison.OrdinalIgnoreCase))
                    {
                        furn.TileLocation = new Vector2(ResidenceInteriorLogic.DiningChairPlacement.TargetX, ResidenceInteriorLogic.DiningChairPlacement.TargetY);
                        furn.currentRotation.Value = ResidenceInteriorLogic.DiningChairPlacement.Rotation;
                        furn.updateRotation();
                        furn.RecalculateBoundingBox();
                        furn.updateDrawPosition();
                    }
                    else if (name.Contains("TV", StringComparison.OrdinalIgnoreCase))
                    {
                        furn.TileLocation = new Vector2(ResidenceInteriorLogic.TvPlacement.TargetX, ResidenceInteriorLogic.TvPlacement.TargetY);
                        furn.currentRotation.Value = ResidenceInteriorLogic.TvPlacement.Rotation;
                        furn.updateRotation();
                        furn.RecalculateBoundingBox();
                        furn.updateDrawPosition();
                    }
                    else if (furn is BedFurniture || name.Contains("Bed", StringComparison.OrdinalIgnoreCase))
                    {
                        furn.TileLocation = new Vector2(ResidenceInteriorLogic.BedPlacement.TargetX, ResidenceInteriorLogic.BedPlacement.TargetY);
                        furn.currentRotation.Value = ResidenceInteriorLogic.BedPlacement.Rotation;
                        furn.updateRotation();
                        furn.RecalculateBoundingBox();
                        furn.updateDrawPosition();
                    }
                    else if (name.Contains("Plant", StringComparison.OrdinalIgnoreCase))
                    {
                        furn.TileLocation = new Vector2(ResidenceInteriorLogic.PlantPlacement.TargetX, ResidenceInteriorLogic.PlantPlacement.TargetY);
                        furn.currentRotation.Value = ResidenceInteriorLogic.PlantPlacement.Rotation;
                        furn.updateRotation();
                        furn.RecalculateBoundingBox();
                        furn.updateDrawPosition();
                    }
                }
            }
        }

        private static void EnsureStarterDecorations(Cabin cabin)
        {
            if (cabin == null || cabin.furniture == null) return;

            // 1. Cama individual
            bool hasBed = cabin.furniture.Any(f => f is BedFurniture || (f.Name != null && f.Name.Contains("Bed", StringComparison.OrdinalIgnoreCase)));
            if (!hasBed)
            {
                var bed = new BedFurniture(BedFurniture.DEFAULT_BED_INDEX, new Vector2(ResidenceInteriorLogic.BedPlacement.TargetX, ResidenceInteriorLogic.BedPlacement.TargetY));
                cabin.furniture.Add(bed);
            }

            // 2. Planta de interior en la esquina inferior izquierda
            bool hasPlant = cabin.furniture.Any(f => f.ItemId == "1376" || (f.Name != null && f.Name.Contains("Plant", StringComparison.OrdinalIgnoreCase)));
            if (!hasPlant)
            {
                var plant = ItemRegistry.Create<Furniture>("(F)1376");
                if (plant != null)
                {
                    plant.TileLocation = new Vector2(ResidenceInteriorLogic.PlantPlacement.TargetX, ResidenceInteriorLogic.PlantPlacement.TargetY);
                    plant.currentRotation.Value = ResidenceInteriorLogic.PlantPlacement.Rotation;
                    plant.updateRotation();
                    plant.RecalculateBoundingBox();
                    plant.updateDrawPosition();
                    cabin.furniture.Add(plant);
                }
            }

            // 3. Chimenea de ladrillo
            bool hasFireplace = cabin.furniture.Any(f => f.ItemId == "1792" || (f.Name != null && f.Name.Contains("Fireplace", StringComparison.OrdinalIgnoreCase)));
            if (!hasFireplace)
            {
                var fireplace = ItemRegistry.Create<Furniture>("(F)1792");
                if (fireplace != null)
                {
                    fireplace.TileLocation = new Vector2(ResidenceInteriorLogic.FireplacePlacement.TargetX, ResidenceInteriorLogic.FireplacePlacement.TargetY);
                    fireplace.currentRotation.Value = ResidenceInteriorLogic.FireplacePlacement.Rotation;
                    fireplace.updateRotation();
                    fireplace.RecalculateBoundingBox();
                    fireplace.updateDrawPosition();
                    cabin.furniture.Add(fireplace);
                }
            }

            // 4. Alfombra roja rústica
            bool hasRug = cabin.furniture.Any(f => f.ItemId == "1618" || (f.Name != null && f.Name.Contains("Rug", StringComparison.OrdinalIgnoreCase)));
            if (!hasRug)
            {
                var rug = ItemRegistry.Create<Furniture>("(F)1618");
                if (rug != null)
                {
                    rug.TileLocation = new Vector2(ResidenceInteriorLogic.RugPlacement.TargetX, ResidenceInteriorLogic.RugPlacement.TargetY);
                    rug.currentRotation.Value = ResidenceInteriorLogic.RugPlacement.Rotation;
                    rug.updateRotation();
                    rug.RecalculateBoundingBox();
                    rug.updateDrawPosition();
                    cabin.furniture.Add(rug);
                }
            }

            // 5. Ventana básica
            bool hasWindow = cabin.furniture.Any(f => f.ItemId == "1614" || (f.Name != null && f.Name.Contains("Window", StringComparison.OrdinalIgnoreCase)));
            if (!hasWindow)
            {
                var window = ItemRegistry.Create<Furniture>("(F)1614");
                if (window != null)
                {
                    window.TileLocation = new Vector2(ResidenceInteriorLogic.WindowPlacement.TargetX, ResidenceInteriorLogic.WindowPlacement.TargetY);
                    window.currentRotation.Value = ResidenceInteriorLogic.WindowPlacement.Rotation;
                    window.updateRotation();
                    window.RecalculateBoundingBox();
                    window.updateDrawPosition();
                    cabin.furniture.Add(window);
                }
            }

            // 6. Cuadro de arbolito
            bool hasPainting = cabin.furniture.Any(f => f.ItemId == "1602" || (f.Name != null && (f.Name.Contains("Tree", StringComparison.OrdinalIgnoreCase) || f.Name.Contains("Painting", StringComparison.OrdinalIgnoreCase))));
            if (!hasPainting)
            {
                var painting = ItemRegistry.Create<Furniture>("(F)1602");
                if (painting != null)
                {
                    painting.TileLocation = new Vector2(ResidenceInteriorLogic.TreePaintingPlacement.TargetX, ResidenceInteriorLogic.TreePaintingPlacement.TargetY);
                    painting.currentRotation.Value = ResidenceInteriorLogic.TreePaintingPlacement.Rotation;
                    painting.updateRotation();
                    painting.RecalculateBoundingBox();
                    painting.updateDrawPosition();
                    cabin.furniture.Add(painting);
                }
            }

            // 7. TV básica
            bool hasTv = cabin.furniture.Any(f => f.ItemId == "1466" || (f.Name != null && f.Name.Contains("TV", StringComparison.OrdinalIgnoreCase)));
            if (!hasTv)
            {
                var tv = ItemRegistry.Create<Furniture>("(F)1466");
                if (tv != null)
                {
                    tv.TileLocation = new Vector2(ResidenceInteriorLogic.TvPlacement.TargetX, ResidenceInteriorLogic.TvPlacement.TargetY);
                    tv.currentRotation.Value = ResidenceInteriorLogic.TvPlacement.Rotation;
                    tv.updateRotation();
                    tv.RecalculateBoundingBox();
                    tv.updateDrawPosition();
                    cabin.furniture.Add(tv);
                }
            }

            // 8. Mesa de comedor
            bool hasTable = cabin.furniture.Any(f => f.ItemId == "1120" || (f.Name != null && f.Name.Contains("Table", StringComparison.OrdinalIgnoreCase)));
            if (!hasTable)
            {
                var table = ItemRegistry.Create<Furniture>("(F)1120");
                if (table != null)
                {
                    table.TileLocation = new Vector2(ResidenceInteriorLogic.DiningTablePlacement.TargetX, ResidenceInteriorLogic.DiningTablePlacement.TargetY);
                    table.currentRotation.Value = ResidenceInteriorLogic.DiningTablePlacement.Rotation;
                    table.updateRotation();
                    var held = ItemRegistry.Create<Furniture>("(F)1364");
                    if (held != null) table.heldObject.Value = held;
                    table.RecalculateBoundingBox();
                    table.updateDrawPosition();
                    cabin.furniture.Add(table);
                }
            }

            // 9. Silla de comedor
            bool hasChair = cabin.furniture.Any(f => f.ItemId == "0" || (f.Name != null && f.Name.Contains("Chair", StringComparison.OrdinalIgnoreCase)));
            if (!hasChair)
            {
                var chair = ItemRegistry.Create<Furniture>("(F)0");
                if (chair != null)
                {
                    chair.TileLocation = new Vector2(ResidenceInteriorLogic.DiningChairPlacement.TargetX, ResidenceInteriorLogic.DiningChairPlacement.TargetY);
                    chair.currentRotation.Value = ResidenceInteriorLogic.DiningChairPlacement.Rotation;
                    chair.updateRotation();
                    chair.RecalculateBoundingBox();
                    chair.updateDrawPosition();
                    cabin.furniture.Add(chair);
                }
            }
        }

        private static void RepositionStarterChests(Cabin cabin)
        {
            if (cabin == null || cabin.objects == null) return;

            var toMove = new List<KeyValuePair<Vector2, StardewValley.Object>>();
            foreach (var kvp in cabin.objects.Pairs)
            {
                if (ResidenceInteriorLogic.IsWalkwayTile((int)kvp.Key.X, (int)kvp.Key.Y))
                {
                    toMove.Add(kvp);
                }
            }

            foreach (var kvp in toMove)
            {
                cabin.objects.Remove(kvp.Key);
                Vector2 target = new Vector2(ResidenceInteriorLogic.StarterChestPlacement.TargetX, ResidenceInteriorLogic.StarterChestPlacement.TargetY);
                if (cabin.objects.ContainsKey(target))
                {
                    target = new Vector2(1, 9);
                }
                kvp.Value.TileLocation = target;
                cabin.objects.Add(target, kvp.Value);
            }
        }

        private static void ApplyNitvPatches(Harmony harmony)
        {
            try
            {
                Type? nitvEntryType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(asm.GetName().Name, "NatureInTheValley", StringComparison.OrdinalIgnoreCase))
                    {
                        nitvEntryType = asm.GetType("NatureInTheValley.NatureInTheValleyEntry");
                        break;
                    }
                }

                if (nitvEntryType == null)
                    return;

                var onWarpMethod = AccessTools.Method(nitvEntryType, "OnWarp", new[] { typeof(object), typeof(WarpedEventArgs) });
                if (onWarpMethod != null)
                {
                    harmony.Patch(
                        original: onWarpMethod,
                        prefix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Prefix_NiTV_OnWarp))
                    );
                    context?.Monitor.Log("[FarmSlotModule] Parche de concurrencia multijugador aplicado a Nature In The Valley (OnWarp).", LogLevel.Info);
                }

                var validLocationMethod = AccessTools.Method(nitvEntryType, "validLocation", new[] { typeof(GameLocation), typeof(string) });
                if (validLocationMethod != null)
                {
                    harmony.Patch(
                        original: validLocationMethod,
                        postfix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Postfix_NiTV_ValidLocation))
                    );
                    context?.Monitor.Log("[FarmSlotModule] Parche de compatibilidad de hábitats aplicado a Nature In The Valley (validLocation).", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[FarmSlotModule] Advertencia al parchear Nature In The Valley: {ex}", LogLevel.Warn);
            }
        }

        public static bool Prefix_NiTV_OnWarp(WarpedEventArgs eventArgs)
        {
            // Solo procesar OnWarp si el evento pertenece al jugador local.
            // En multijugador SMAPI dispara Warped para todos los clientes;
            // si no filtramos, el cambio de mapa de cualquier otro granjero
            // borra la fauna y corrompe los spawns locales.
            if (eventArgs != null && !eventArgs.IsLocalPlayer)
            {
                return false;
            }
            return true;
        }

        public static void Postfix_NiTV_ValidLocation(GameLocation location, string code, ref bool __result)
        {
            if (__result) return;
            if (location == null || string.IsNullOrEmpty(code)) return;

            var customFields = location.GetData()?.CustomFields;
            if (customFields == null) return;

            // Criaturas de SH's Wild Animals o paquetes de fauna con nombres literales de bioma
            if ((string.Equals(code, "Forest", StringComparison.OrdinalIgnoreCase) || string.Equals(code, "Woods", StringComparison.OrdinalIgnoreCase))
                && customFields.ContainsKey("Nature.NIV.Forest"))
            {
                __result = true;
                return;
            }

            if (string.Equals(code, "Beach", StringComparison.OrdinalIgnoreCase) && customFields.ContainsKey("Nature.NIV.Beach"))
            {
                __result = true;
                return;
            }
        }
    }
}
