using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.Locations;
using StardewValley.Locations;
using StardewValley.Menus;
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
            ctx.Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;

            // Comandos de consola y chat
            ctx.Helper.ConsoleCommands.Add("p4e_doors", "Muestra el estado y ubicación de las 16 residencias.", OnConsoleDoors);
            ctx.Helper.Events.GameLoop.GameLaunched += (s, e) => RegisterChatCommands();

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
                    RegisteredDoors = context.Helper.Data.ReadJsonFile<List<DoorModel>>("assets/doors.json") ?? new();
                    context.Monitor.Log($"[FarmSlotModule] {RegisteredDoors.Count} residencias/puertas cargadas desde assets/doors.json.", LogLevel.Info);
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
                    }

                    int count = context?.Config.FarmSlotCount ?? 16;

                    for (int i = 1; i <= count; i++)
                    {
                        string slotId = $"Pelican4Ever_Farm_{i:D2}";
                        if (!data.ContainsKey(slotId))
                        {
                            string farmType = "Standard";
                            if (context != null && context.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == slotId) is { } assignment && !string.IsNullOrEmpty(assignment.FarmType))
                            {
                                farmType = assignment.FarmType;
                            }
                            var template = FarmTemplateCatalog.GetTemplate(farmType);

                            data[slotId] = new LocationData
                            {
                                DisplayName = $"Granja {i} ({template.DisplayName})",
                                CanPlantHere = true,
                                CreateOnLoad = new CreateLocationData
                                {
                                    MapPath = template.MapPath,
                                    Type = "StardewValley.Farm, Stardew Valley",
                                    AlwaysActive = true // Gestionado dinámicamente mediante LocationCullingLogic
                                }
                            };
                        }
                    }
                });
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

                    foreach (var door in RegisteredDoors)
                    {
                        if (string.Equals(door.LocationName, "Farm", StringComparison.OrdinalIgnoreCase))
                        {
                            ClearDoorAreaOnMap(bldLayer, frontLayer, alwaysFrontLayer, backLayer, door, sampleTile);
                        }
                    }

                    return hostMap;
                }, AssetLoadPriority.Medium);
            }
        }

        public static (Point tile1, Point tile2) GetOpeningTiles(DoorModel door)
        {
            if (door.TownTileX <= 2 || door.TownTileX >= 75)
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
            SpatialWarpRouter.GetResidenceArrivalCoordinates(door.TownTileX, door.TownTileY, out targetX, out targetY, out facingDirection);
        }

        public static bool IsInOpening(DoorModel door, int tileX, int tileY)
        {
            var (t1, t2) = GetOpeningTiles(door);
            return (tileX == t1.X && tileY == t1.Y) || (tileX == t2.X && tileY == t2.Y);
        }

        public static bool IsInOpeningOrBorder(DoorModel door, int tileX, int tileY)
        {
            var (t1, t2) = GetOpeningTiles(door);
            if (tileX == t1.X || tileX == t2.X)
            {
                if (door.TownTileY <= 2 && tileY >= 0 && tileY <= 10)
                    return true;
                if (door.TownTileY >= 60 && tileY >= 60 && tileY <= 64)
                    return true;
            }
            if (tileY == t1.Y || tileY == t2.Y)
            {
                if (door.TownTileX <= 2 && tileX >= 0 && tileX <= 7)
                    return true;
                if (door.TownTileX >= 75 && tileX >= 75 && tileX <= 79)
                    return true;
            }

            return false;
        }

        public static List<Point> ClearDoorAreaOnMap(
            xTile.Layers.Layer? bld,
            xTile.Layers.Layer? front,
            xTile.Layers.Layer? alwaysFront,
            xTile.Layers.Layer? back,
            DoorModel door,
            xTile.Tiles.Tile? sampleTile)
        {
            var (t1, t2) = GetOpeningTiles(door);
            var passTiles = new List<Point> { t1, t2 };

            var sheet = bld?.Map?.GetTileSheet("untitled tile sheet")
                     ?? front?.Map?.GetTileSheet("untitled tile sheet")
                     ?? sampleTile?.TileSheet;

            if (door.TownTileX <= 2)
            {
                int maxDepthX = (door.DoorId == "Farm_Portal_02") ? 6 : 4;
                for (int x = 0; x <= maxDepthX; x++)
                {
                    passTiles.Add(new Point(x, t1.Y));
                    passTiles.Add(new Point(x, t2.Y));
                }
            }
            else if (door.TownTileX >= 75)
            {
                for (int x = 76; x <= 79; x++)
                {
                    passTiles.Add(new Point(x, t1.Y));
                    passTiles.Add(new Point(x, t2.Y));
                }
            }
            else if (door.TownTileY <= 2)
            {
                int maxDepthY = (door.DoorId is "Farm_Portal_08" or "Farm_Portal_14") ? 9 : 8;
                for (int y = 0; y <= maxDepthY; y++)
                {
                    passTiles.Add(new Point(t1.X, y));
                    passTiles.Add(new Point(t2.X, y));
                }
            }
            else
            {
                for (int y = 60; y <= 64; y++)
                {
                    passTiles.Add(new Point(t1.X, y));
                    passTiles.Add(new Point(t2.X, y));
                }
            }

            bool isSouthDoor = SpatialWarpRouter.IsSouthBorderPortal(door.TownTileY);

            foreach (var pt in passTiles)
            {
                // 1. Limpiar colisión en Buildings para que el paso sea completamente transitable sin bloqueos
                if (bld != null && pt.X >= 0 && pt.X < bld.LayerWidth && pt.Y >= 0 && pt.Y < bld.LayerHeight)
                    bld.Tiles[pt.X, pt.Y] = null;

                // 2. En Front, si hay copas de árbol las preservamos en AlwaysFront; se eliminan los rebordes de suelo/acantilado
                if (front != null && pt.X >= 0 && pt.X < front.LayerWidth && pt.Y >= 0 && pt.Y < front.LayerHeight)
                {
                    var existingFront = front.Tiles[pt.X, pt.Y];
                    if (existingFront != null && existingFront.TileIndex < 150 && alwaysFront != null && alwaysFront.Tiles[pt.X, pt.Y] == null)
                    {
                        alwaysFront.Tiles[pt.X, pt.Y] = existingFront.Clone(alwaysFront);
                    }
                    front.Tiles[pt.X, pt.Y] = null;
                }

                // 3. En las salidas del sur hacia las residencias, MANTENER intactas las copas de los árboles en AlwaysFront.
                // Así los árboles se ven completos y continuos por encima del camino, y el jugador camina bajo ellos sin bloqueo.
                if (!isSouthDoor && alwaysFront != null && pt.X >= 0 && pt.X < alwaysFront.LayerWidth && pt.Y >= 0 && pt.Y < alwaysFront.LayerHeight)
                {
                    alwaysFront.Tiles[pt.X, pt.Y] = null;
                }

                if (back != null && pt.X >= 0 && pt.X < back.LayerWidth && pt.Y >= 0 && pt.Y < back.LayerHeight)
                {
                    if (sheet != null)
                    {
                        // Pavimentar con senda de grava/tierra natural (tile 587 vanilla)
                        back.Tiles[pt.X, pt.Y] = new xTile.Tiles.StaticTile(back, sheet, xTile.Tiles.BlendMode.Alpha, 587);
                    }
                    else if (back.Tiles[pt.X, pt.Y] == null && sampleTile != null)
                    {
                        back.Tiles[pt.X, pt.Y] = sampleTile.Clone(back);
                    }

                    if (back.Tiles[pt.X, pt.Y] != null)
                    {
                        back.Tiles[pt.X, pt.Y].Properties.Remove("Passable");
                        back.Tiles[pt.X, pt.Y].TileIndexProperties.Remove("Passable");
                    }
                }
            }

            // Despejar casilla del cartel para que ningún gráfico de mapa colisione con el poste físico
            int signX = door.MailboxTileX ?? (door.TownTileX + 1);
            int signY = door.MailboxTileY ?? door.TownTileY;
            if (bld != null && signX >= 0 && signX < bld.LayerWidth && signY >= 0 && signY < bld.LayerHeight)
                bld.Tiles[signX, signY] = null;
            if (front != null && signX >= 0 && signX < front.LayerWidth && signY >= 0 && signY < front.LayerHeight)
                front.Tiles[signX, signY] = null;

            return passTiles;
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

                // Asegurar edificios limpios y Farmhouse operativa en cada granja instanciada
                int slotCount = context.Config.FarmSlotCount;
                for (int i = 1; i <= slotCount; i++)
                {
                    string slotId = $"Pelican4Ever_Farm_{i:D2}";
                    if (Game1.getLocationFromName(slotId) is Farm farm)
                    {
                        var assignment = context.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == slotId);
                        EnsureFarmSlotBuildings(farm, assignment);
                    }
                }

                // Publicar modData de propiedad en las ubicaciones cargadas
                foreach (var kvp in context.WorldState.Doors)
                {
                    DoorAssignment assignment = kvp.Value;
                    GameLocation? loc = Game1.getLocationFromName(assignment.FarmSlotId);
                    if (loc != null)
                    {
                        loc.PublishOwnership(assignment.OwnerId, assignment.Policy);
                    }
                }

                // Sincronizar catálogo inicial de residencias con todos los peones
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

            SendDoorsSyncToPlayer(e.Peer.PlayerID);
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            welcomeNotified = false;
        }

        private void OnPlayerWarped(object? sender, WarpedEventArgs e)
        {
            if (!e.IsLocalPlayer || context == null)
                return;

            if (e.NewLocation is Farm hostFarm && hostFarm.Name == "Farm")
            {
                if (!string.Equals(hostFarm.mapPath.Value, "Maps\\Pelican4Ever_HostFarm", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(hostFarm.mapPath.Value, "Maps/Pelican4Ever_HostFarm", StringComparison.OrdinalIgnoreCase))
                {
                    hostFarm.mapPath.Value = "Maps\\Pelican4Ever_HostFarm";
                    hostFarm.reloadMap();
                }

                RemoveHostCabins(hostFarm);
                EnsureHostFarmPortals(hostFarm);

                if (!welcomeNotified)
                {
                    bool hasResidence = context.WorldState.Doors.Values.Any(d => d.OwnerId == Game1.player.UniqueMultiplayerID);
                    if (!hasResidence)
                    {
                        welcomeNotified = true;
                        Game1.addHUDMessage(new HUDMessage("¡Bienvenido a la Plaza Central! Camina hacia un portal con cartel verde o escribe /residencias para fundar tu granja.", HUDMessage.newQuest_type));
                    }
                }
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
                    farm.buildings.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0)
            {
                context?.Monitor.Log($"[FarmSlotModule] Se eliminaron {removed} cabañas redundantes de la granja anfitriona (Plaza Central).", LogLevel.Info);
            }
        }

        private static int unclaimedCheckCooldown;

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!e.IsMultipleOf(4) || !Context.IsWorldReady || context == null)
                return;

            if (unclaimedCheckCooldown > 0)
            {
                unclaimedCheckCooldown--;
                return;
            }

            if (Game1.currentLocation is not Farm || Game1.player == null || Game1.isFestival() || Game1.activeClickableMenu != null)
                return;

            Vector2 playerTile = Game1.player.Tile;
            DoorModel? door = RegisteredDoors.FirstOrDefault(d =>
                string.Equals(d.LocationName, "Farm", StringComparison.OrdinalIgnoreCase)
                && IsInOpeningOrBorder(d, (int)playerTile.X, (int)playerTile.Y));

            if (door != null && !context.WorldState.Doors.ContainsKey(door.DoorId))
            {
                unclaimedCheckCooldown = 40; // Evitar disparos repetidos
                Game1.player.Halt();
                Game1.player.completelyStopAnimatingOrDoingAction();
                InteractWithResidentialDoor(door);
            }
        }

        /// <summary>
        /// Aprovisiona los portales físicos de 2 tiles y los senderos en el perímetro de la granja del anfitrión (Plaza Central).
        /// Cada parcela cuenta con un portal transitable en el borde del mapa con warps continuos hacia su granja privada,
        /// acera adoquinada de conexión y poste señalizador de madera físico (Sign con BigCraftable).
        /// </summary>
        public static void EnsureHostFarmPortals(Farm farm)
        {
            if (farm == null || context == null) return;

            var hostMap = farm.Map;
            var bldLayer = hostMap?.GetLayer("Buildings");
            var frontLayer = hostMap?.GetLayer("Front");
            var alwaysFrontLayer = hostMap?.GetLayer("AlwaysFront");
            var backLayer = hostMap?.GetLayer("Back");

            xTile.Tiles.Tile? sampleTile = null;
            if (backLayer != null)
            {
                sampleTile = backLayer.Tiles[40, 30] ?? backLayer.Tiles[64, 15] ?? backLayer.Tiles[20, 20];
            }

            foreach (DoorModel door in RegisteredDoors)
            {
                if (!string.Equals(door.LocationName, "Farm", StringComparison.OrdinalIgnoreCase))
                    continue;

                // 1. Limpiar copas de árboles, ramas, troncos y suelo en el mapa del runtime
                var passTiles = ClearDoorAreaOnMap(bldLayer, frontLayer, alwaysFrontLayer, backLayer, door, sampleTile);

                var (t1, t2) = GetOpeningTiles(door);

                // 2. Limpiar objetos, escombros, tocones, piedras y propiedades Passable a lo largo de todo el paso
                foreach (var pt in passTiles)
                {
                    farm.removeEverythingFromThisTile(pt.X, pt.Y);
                    farm.removeTileProperty(pt.X, pt.Y, "Back", "Passable");

                    for (int i = farm.resourceClumps.Count - 1; i >= 0; i--)
                    {
                        var clump = farm.resourceClumps[i];
                        if (IsClumpOverTile(clump, pt.X, pt.Y))
                        {
                            farm.resourceClumps.RemoveAt(i);
                        }
                    }
                }

                // 3. Colocar poste con cartel de madera físico (Sign ID 37 - BigCraftable) en la casilla designada
                int signX = door.MailboxTileX ?? (door.TownTileX + 1);
                int signY = door.MailboxTileY ?? door.TownTileY;
                Vector2 signTile = new Vector2(signX, signY);
                farm.removeEverythingFromThisTile(signX, signY);

                if (!farm.objects.ContainsKey(signTile))
                {
                    Sign sign = new Sign(signTile, "37");
                    sign.bigCraftable.Value = true;
                    farm.objects[signTile] = sign;
                }
                else if (farm.objects[signTile] is Sign existingSign)
                {
                    existingSign.bigCraftable.Value = true;
                }

                // 4. Sincronizar Warps continuos en ambos tiles de la apertura (solo si la parcela está reclamada)
                bool isClaimed = context.WorldState.Doors.TryGetValue(door.DoorId, out DoorAssignment? assignment);

                if (isClaimed && assignment != null)
                {
                    GetResidenceArrivalCoordinates(door, out int targetX, out int targetY, out _);

                    UpdateOrCreateWarp(farm, t1.X, t1.Y, assignment.FarmSlotId, targetX, targetY);
                    UpdateOrCreateWarp(farm, t2.X, t2.Y, assignment.FarmSlotId, targetX, targetY);

                    // Warps en borde exterior absoluto para disparo garantizado
                    if (door.TownTileX <= 2)
                    {
                        UpdateOrCreateWarp(farm, 0, t1.Y, assignment.FarmSlotId, targetX, targetY);
                        UpdateOrCreateWarp(farm, 0, t2.Y, assignment.FarmSlotId, targetX, targetY);
                    }
                    else if (door.TownTileX >= 75)
                    {
                        UpdateOrCreateWarp(farm, 79, t1.Y, assignment.FarmSlotId, targetX, targetY);
                        UpdateOrCreateWarp(farm, 79, t2.Y, assignment.FarmSlotId, targetX, targetY);
                    }
                    else if (door.TownTileY <= 2)
                    {
                        UpdateOrCreateWarp(farm, t1.X, 0, assignment.FarmSlotId, targetX, targetY);
                        UpdateOrCreateWarp(farm, t2.X, 0, assignment.FarmSlotId, targetX, targetY);
                    }
                    else
                    {
                        UpdateOrCreateWarp(farm, t1.X, 64, assignment.FarmSlotId, targetX, targetY);
                        UpdateOrCreateWarp(farm, t2.X, 64, assignment.FarmSlotId, targetX, targetY);
                    }
                }
                else
                {
                    RemoveWarpAt(farm, t1.X, t1.Y);
                    RemoveWarpAt(farm, t2.X, t2.Y);
                    if (door.TownTileX <= 2)
                    {
                        RemoveWarpAt(farm, 0, t1.Y);
                        RemoveWarpAt(farm, 0, t2.Y);
                    }
                    else if (door.TownTileX >= 75)
                    {
                        RemoveWarpAt(farm, 79, t1.Y);
                        RemoveWarpAt(farm, 79, t2.Y);
                    }
                    else if (door.TownTileY <= 2)
                    {
                        RemoveWarpAt(farm, t1.X, 0);
                        RemoveWarpAt(farm, t2.X, 0);
                    }
                    else
                    {
                        RemoveWarpAt(farm, t1.X, 64);
                        RemoveWarpAt(farm, t2.X, 64);
                    }
                }
            }
        }

        private static bool IsClumpOverTile(StardewValley.TerrainFeatures.ResourceClump clump, int x, int y)
        {
            return x >= (int)clump.Tile.X && x < (int)clump.Tile.X + clump.width.Value
                && y >= (int)clump.Tile.Y && y < (int)clump.Tile.Y + clump.height.Value;
        }

        private static void UpdateOrCreateWarp(Farm farm, int x, int y, string targetLocation, int targetX, int targetY)
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

        private static void RemoveWarpAt(Farm farm, int x, int y)
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
        /// Asegura que la granja instanciada tenga eliminadas las cabañas automáticas no deseadas,
        /// su edificio Farmhouse principal con interior tipo Cabin dedicado, y warps de entrada/salida conectados.
        /// </summary>
        public static void EnsureFarmSlotBuildings(Farm farm, DoorAssignment? assignment)
        {
            if (farm == null) return;

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

            // No registrar la Cabin en Game1.locations para evitar duplicación al serializar la partida.
            // Stardew Valley 1.6 resuelve interiores dinámicamente mediante Building.GetIndoors() y findStructure.
            if (Game1.locations != null && Game1.locations.Contains(cabin))
            {
                Game1.locations.Remove(cabin);
            }

            // Distribuir de forma óptima el mobiliario interior para evitar que quede apelotonado contra la pared norte
            CleanAndArrangeResidenceInterior(cabin);

            // Despejar puntos de llegada cardinales para que nunca haya ramas, maleza o escombros bloqueando el warp
            farm.removeEverythingFromThisTile(40, 1);
            farm.removeEverythingFromThisTile(40, 63);
            farm.removeEverythingFromThisTile(78, 17);

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
                    cabin.AssignFarmhand(ownerFarmer);
                }
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
                        name => "Muestra el catálogo de residencias de Pelican Town y su estado (/doors o /residencias)",
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
            chat.addInfoMessage("=== PARCELAS RESIDENCIALES EN LA PLAZA CENTRAL ===");
            int freeCount = 0;
            foreach (var door in RegisteredDoors)
            {
                DoorAssignment? assignment = null;
                bool isClaimed = context != null && context.WorldState.Doors.TryGetValue(door.DoorId, out assignment);
                string loc = string.IsNullOrWhiteSpace(door.LocationName) ? "Farm" : door.LocationName;
                if (!isClaimed || assignment == null)
                {
                    freeCount++;
                    chat.addInfoMessage($"[Libre] {door.DoorId}: {loc} ({door.TownTileX},{door.TownTileY}) - {door.Description}");
                }
                else
                {
                    string owner = "Vecino";
                    if (context != null && context.WorldState.Players.TryGetValue(assignment.OwnerId, out var rec) && !string.IsNullOrWhiteSpace(rec.PlayerName))
                        owner = rec.PlayerName;
                    chat.addInfoMessage($"[Ocupada] {door.DoorId} ({owner}): {loc} ({door.TownTileX},{door.TownTileY})");
                }
            }
            chat.addInfoMessage($"Total: {freeCount} parcelas libres. Camina hacia el portal o pulsa [E] para fundar tu granja.");
        }

        private void OnConsoleDoors(string command, string[] args)
        {
            if (context == null) return;
            context.Monitor.Log($"=== RESIDENCIAS EN PELICAN TOWN ({RegisteredDoors.Count} en catálogo) ===", LogLevel.Info);
            foreach (var door in RegisteredDoors)
            {
                DoorAssignment? assignment = null;
                bool isClaimed = context.WorldState.Doors.TryGetValue(door.DoorId, out assignment);
                string status = (isClaimed && assignment != null) ? $"Ocupada por {assignment.OwnerId} ({assignment.FarmType})" : "LIBRE";
                context.Monitor.Log($"  {door.DoorId} [{status}] en {door.LocationName ?? "Town"} ({door.TownTileX},{door.TownTileY}) - {door.Description}", LogLevel.Info);
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

                    foreach (var kvp in payload.Players)
                    {
                        context.WorldState.Players[kvp.Key] = kvp.Value;
                    }

                    context.Monitor.Log($"[FarmSlotModule] Sincronización de residencias recibida del host ({context.WorldState.Doors.Count} ocupadas).", LogLevel.Info);

                    if (Game1.currentLocation is Farm curFarm && curFarm.Name == "Farm")
                    {
                        EnsureHostFarmPortals(curFarm);
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
                Players = new Dictionary<long, PlayerRecord>(context.WorldState.Players)
            };

            context.Helper.Multiplayer.SendMessage(
                payload,
                ModMessages.SyncDoorsState,
                new[] { context.Manifest.UniqueID },
                new[] { playerId }
            );
        }

        private static void HandleClaimDoor(long playerId, string doorId, string farmType)
        {
            if (context == null) return;
            var worldState = context.WorldState;

            // Si la puerta ya está asignada a otro jugador
            if (worldState.Doors.TryGetValue(doorId, out var existing) && existing.OwnerId != playerId)
            {
                context.Monitor.Log($"[FarmSlotModule] Jugador {playerId} intentó reclamar puerta ocupada {doorId} (propietario: {existing.OwnerId}).", LogLevel.Warn);
                return;
            }

            // Buscar la puerta en catálogo
            DoorModel? door = RegisteredDoors.FirstOrDefault(d => d.DoorId == doorId);
            if (door == null) return;

            string slotId = door.DefaultSlotId;
            var playerRecord = worldState.GetOrCreatePlayer(playerId, string.Empty);
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
            GameLocation? loc = Game1.getLocationFromName(slotId);
            if (loc is Farm farmLoc)
            {
                farmLoc.PublishOwnership(playerId, AccessPolicy.Locked);
                EnsureFarmSlotBuildings(farmLoc, assignment);
            }
            else if (loc != null)
            {
                loc.PublishOwnership(playerId, AccessPolicy.Locked);
            }

            context.Monitor.Log($"[FarmSlotModule] Puerta {doorId} asignada con éxito a {playerId} (Granja: {slotId}, Tipo: {resolvedType}).", LogLevel.Info);

            // Actualizar warps físicos continuos en la granja del anfitrión
            EnsureHostFarmPortals(Game1.getFarm());

            // Sincronizar inmediatamente a todos los peones
            BroadcastDoorsSync();
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
                context.Monitor.Log($"[FarmSlotModule] Política de {farmSlotId} cambiada a {newPolicy} por propietario {playerId}.", LogLevel.Info);

                // Sincronizar inmediatamente a todos los peones
                BroadcastDoorsSync();
            }
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || context == null)
                return;

            if (!e.Button.IsActionButton())
                return;

            if (Game1.currentLocation == null)
                return;

            string curLoc = Game1.currentLocation.Name;
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
                string.Equals(string.IsNullOrWhiteSpace(d.LocationName) ? "Farm" : d.LocationName, curLoc, StringComparison.OrdinalIgnoreCase)
                && (IsInOpeningOrBorder(d, (int)targetTile.X, (int)targetTile.Y)
                    || ((d.MailboxTileX ?? (d.TownTileX + 1)) == (int)targetTile.X && (d.MailboxTileY ?? d.TownTileY) == (int)targetTile.Y)));

            if (door != null)
            {
                InteractWithResidentialDoor(door);
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

                Game1.currentLocation.createQuestionDialogue(
                    $"{door.Description}.\nEsta residencia está libre. ¿Qué tipo de granja deseas fundar?",
                    choices,
                    (who, whichAnswer) =>
                    {
                        if (whichAnswer != "Cancel")
                        {
                            context.Helper.Multiplayer.SendMessage(
                                new ClaimDoorPayload { DoorId = door.DoorId, FarmType = whichAnswer },
                                ModMessages.ClaimDoorRequest,
                                new[] { context.Manifest.UniqueID },
                                new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                            );

                            string resolvedType = string.IsNullOrWhiteSpace(whichAnswer) ? "Standard" : whichAnswer;
                            var localAssignment = new DoorAssignment
                            {
                                DoorId = door.DoorId,
                                FarmSlotId = door.DefaultSlotId,
                                FarmType = resolvedType,
                                OwnerId = myId,
                                Policy = AccessPolicy.Locked
                            };
                            worldState.Doors[door.DoorId] = localAssignment;
                            var playerRecord = worldState.GetOrCreatePlayer(myId, Game1.player.Name);
                            playerRecord.FarmSlotId = door.DefaultSlotId;

                            if (Context.IsMainPlayer)
                            {
                                HandleClaimDoor(myId, door.DoorId, whichAnswer);
                            }

                            Game1.addHUDMessage(new HUDMessage($"¡Residencia reclamada! Plantilla: {resolvedType}.", HUDMessage.newQuest_type));

                            Response[] travelChoices = new[]
                            {
                                new Response("Yes", "Sí, entrar ahora a mi granja"),
                                new Response("No", "No, quedarme en el pueblo")
                            };

                            Game1.currentLocation.createQuestionDialogue(
                                $"¡Enhorabuena! Has fundado tu residencia ({resolvedType}).\n¿Deseas entrar a tu granja ahora?",
                                travelChoices,
                                (w, a) =>
                                {
                                    if (a == "Yes")
                                    {
                                        WarpToFarm(door.DefaultSlotId);
                                    }
                                }
                            );
                        }
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

                Game1.currentLocation.createQuestionDialogue(
                    $"Tu residencia en {door.LocationName ?? "Town"}. (Tipo: {assignment.FarmType})",
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

                Game1.currentLocation.createQuestionDialogue(
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

            Game1.currentLocation.createQuestionDialogue(
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

        public static void WarpToFarm(string farmSlotId)
        {
            DoorAssignment? assignment = context?.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == farmSlotId);
            DoorModel? door = RegisteredDoors.FirstOrDefault(d => d.DefaultSlotId == farmSlotId);

            if (Game1.getLocationFromName(farmSlotId) is Farm farm)
            {
                EnsureFarmSlotBuildings(farm, assignment);
            }

            int targetX, targetY, facingDir;
            if (door != null)
            {
                GetResidenceArrivalCoordinates(door, out targetX, out targetY, out facingDir);
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
                    original: AccessTools.Method(typeof(Tool), nameof(Tool.beginUsing)),
                    prefix: new HarmonyMethod(typeof(FarmSlotModule), nameof(Prefix_ToolBeginUsing))
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

                context?.Monitor.Log("[FarmSlotModule] Parches Harmony de enrutado, anti-griefing, culling de red, colisión y protección de despertar aplicados.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[FarmSlotModule] Error al aplicar parches Harmony: {ex}", LogLevel.Error);
            }
        }

        public static bool Prefix_IsCollidingPosition(GameLocation __instance, Microsoft.Xna.Framework.Rectangle position, ref bool __result)
        {
            if (__instance is Farm farm && farm.Name == "Farm")
            {
                int leftTile = position.Left / 64;
                int rightTile = position.Right / 64;
                int centerTile = position.Center.X / 64;
                int bottomTile = position.Bottom / 64;
                int topTile = position.Top / 64;

                foreach (var door in RegisteredDoors)
                {
                    if (IsInOpeningOrBorder(door, centerTile, bottomTile)
                        || IsInOpeningOrBorder(door, leftTile, bottomTile)
                        || IsInOpeningOrBorder(door, rightTile, bottomTile)
                        || IsInOpeningOrBorder(door, centerTile, topTile))
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
                var assignment = context?.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == targetLoc);
                if (assignment != null)
                {
                    long myId = Game1.player.UniqueMultiplayerID;
                    if (assignment.OwnerId != myId && assignment.Policy == AccessPolicy.Locked)
                    {
                        Game1.showRedMessage("Esta parcela es privada y está cerrada con llave.");
                        // Si estaba en la Granja Host, detener al jugador
                        if (Game1.currentLocation is Farm)
                        {
                            Game1.player.Halt();
                            Game1.player.completelyStopAnimatingOrDoingAction();
                        }
                        return false;
                    }
                }

                // Ajustar orientación adecuada al entrar a la residencia desde un warp continuo del host
                var doorTarget = RegisteredDoors.FirstOrDefault(d => d.DefaultSlotId == targetLoc);
                if (doorTarget != null)
                {
                    GetResidenceArrivalCoordinates(doorTarget, out _, out _, out int facingDir);
                    facingDirectionAfterWarp = facingDir;
                }
            }

            // Si el jugador está saliendo de una cabaña hacia la granja principal
            if (Game1.currentLocation is Cabin && targetLoc == "Farm")
            {
                var assignment = context?.WorldState.Doors.Values.FirstOrDefault(d => d.OwnerId == Game1.player.UniqueMultiplayerID);
                if (assignment != null && RegisteredDoors.FirstOrDefault(d => d.DoorId == assignment.DoorId) is { } door)
                {
                    string dest = string.IsNullOrWhiteSpace(door.LocationName) ? "Farm" : door.LocationName;
                    locationRequest = Game1.getLocationRequest(dest);
                    RepositionToPortalExit(door, ref tileX, ref tileY, ref facingDirectionAfterWarp);
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

            // Si el jugador está saliendo de una granja instanciada privada hacia el exterior
            string curLoc = Game1.currentLocation?.NameOrUniqueName ?? string.Empty;
            if (curLoc.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var door = RegisteredDoors.FirstOrDefault(d => d.DefaultSlotId == curLoc);
                if (door != null)
                {
                    if (SpatialWarpRouter.ShouldReturnToHostFarm(door.TownTileX, door.TownTileY, targetLoc))
                    {
                        string dest = string.IsNullOrWhiteSpace(door.LocationName) ? "Farm" : door.LocationName;
                        locationRequest = Game1.getLocationRequest(dest);
                        RepositionToPortalExit(door, ref tileX, ref tileY, ref facingDirectionAfterWarp);
                    }
                }
            }
            return true;
        }

        private static void RepositionToPortalExit(DoorModel door, ref int tileX, ref int tileY, ref int facingDirectionAfterWarp)
        {
            SpatialWarpRouter.GetHostFarmReturnCoordinates(door.TownTileX, door.TownTileY, out tileX, out tileY, out facingDirectionAfterWarp);
        }

        public static bool Prefix_ApplyWakeUpPosition(Farmer who)
        {
            try
            {
                string? lastSleep = who.lastSleepLocation.Value;
                GameLocation? lastSleepLoc = (!string.IsNullOrEmpty(lastSleep) && Game1.isLocationAccessible(lastSleep))
                    ? Game1.getLocationFromName(lastSleep)
                    : null;

                GameLocation? disconnectLoc = Game1.getLocationFromName(who.disconnectLocation.Value);

                if (disconnectLoc != null && who.disconnectDay.Value == Game1.MasterPlayer.stats.DaysPlayed && !Game1.newDaySync.hasInstance())
                {
                    who.currentLocation = disconnectLoc;
                    who.Position = who.disconnectPosition.Value;
                }
                else if (lastSleepLoc != null && lastSleepLoc.CanWakeUpHere(who))
                {
                    who.Position = Utility.PointToVector2(who.lastSleepPoint.Value) * 64f;
                    who.currentLocation = lastSleepLoc;
                    BedFurniture.ShiftPositionForBed(who);
                }
                else
                {
                    FarmHouse? farmHouse = null;
                    if (!string.IsNullOrEmpty(who.homeLocation.Value))
                    {
                        farmHouse = Game1.getLocationFromName(who.homeLocation.Value) as FarmHouse;
                    }

                    if (farmHouse != null)
                    {
                        who.currentLocation = farmHouse;
                        who.Position = Utility.PointToVector2(farmHouse.GetPlayerBedSpot()) * 64f;
                        BedFurniture.ShiftPositionForBed(who);
                    }
                    else
                    {
                        // Fallback de seguridad: ubicación accesible por defecto para evitar congelamiento de clientes
                        GameLocation fallback = Game1.getLocationFromName("Farm")
                                             ?? Game1.getLocationFromName("Town")
                                             ?? Game1.locations.FirstOrDefault()
                                             ?? new GameLocation("Maps\\Town", "Town");

                        who.currentLocation = fallback;
                        who.Position = new Vector2(64f * 64f, 15f * 64f);
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
                who.currentLocation = Game1.getLocationFromName("Town") ?? Game1.currentLocation;
                who.Position = new Vector2(42f * 64f, 56f * 64f);
                if (who == Game1.player)
                    Game1.currentLocation = who.currentLocation;
                return false;
            }
        }

        private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
        {
            if (!Context.IsWorldReady || context == null || Game1.currentLocation == null)
                return;

            string curLoc = Game1.currentLocation.Name;
            long myId = Game1.player.UniqueMultiplayerID;
            Vector2 playerTile = Game1.player.Tile;

            foreach (DoorModel door in RegisteredDoors)
            {
                string doorLoc = string.IsNullOrWhiteSpace(door.LocationName) ? "Farm" : door.LocationName;
                if (!string.Equals(curLoc, doorLoc, StringComparison.OrdinalIgnoreCase))
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

        public static bool Prefix_ToolBeginUsing(Tool __instance, GameLocation location, Farmer who)
        {
            if (who != Game1.player)
                return true;

            if (location != null && location.Name.StartsWith(LocationCullingLogic.FarmPrefix))
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
            if (loc != null && loc.Name.StartsWith(LocationCullingLogic.FarmPrefix))
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

            if (!location.NameOrUniqueName.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase))
                return;

            if (context == null) return;

            var assignment = context.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == location.NameOrUniqueName);
            long? ownerId = assignment?.OwnerId;

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
    }
}
