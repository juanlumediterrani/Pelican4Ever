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
                    int count = context?.Config.FarmSlotCount ?? 4;

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
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            RegisterChatCommands();

            if (Context.IsMainPlayer)
            {
                if (context == null) return;

                // Asegurarse de que useSeparateWallets esté activo para carteras individuales
                Game1.player.team.useSeparateWallets.Value = true;

                // Aprovisionar cabañas y huecos de farmhands suficientes en la granja del host
                EnsureHostCabins(Game1.getFarm(), context.Config.FarmSlotCount);

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

            if (e.NewLocation.Name is "Town" && !welcomeNotified)
            {
                bool hasResidence = context.WorldState.Doors.Values.Any(d => d.OwnerId == Game1.player.UniqueMultiplayerID);
                if (!hasResidence)
                {
                    welcomeNotified = true;
                    Game1.addHUDMessage(new HUDMessage("¡Bienvenido a Pelican Town! Busca una puerta con cartel verde o escribe /residencias para reclamar tu casa.", HUDMessage.newQuest_type));
                }
            }
        }

        public static void EnsureHostCabins(Farm farm, int targetCount)
        {
            if (!Context.IsMainPlayer || farm == null) return;

            if (Game1.netWorldState?.Value != null)
            {
                Game1.netWorldState.Value.highestPlayerLimit.Value = Math.Max(Game1.netWorldState.Value.highestPlayerLimit.Value, targetCount);
                Game1.netWorldState.Value.currentPlayerLimit.Value = Math.Max(Game1.netWorldState.Value.currentPlayerLimit.Value, targetCount);
            }

            int existingCount = farm.buildings.Count(b => b.GetIndoors() is Cabin);
            if (existingCount >= targetCount)
            {
                return;
            }

            int needed = targetCount - existingCount;
            context?.Monitor.Log($"[FarmSlotModule] Aprovisionando {needed} cabañas adicionales en la granja del anfitrión para soportar hasta {targetCount} jugadores...", LogLevel.Info);

            string[] cabinTypes = { "Log Cabin", "Stone Cabin", "Plank Cabin" };

            for (int i = existingCount; i < targetCount; i++)
            {
                string skin = cabinTypes[i % cabinTypes.Length];
                Vector2 tile = new Vector2(70 + (i % 3) * 6, 25 + (i / 3) * 4);

                try
                {
                    Building building = new Building("Cabin", tile);
                    building.magical.Value = true;
                    building.skinId.Value = skin;
                    building.daysOfConstructionLeft.Value = 0;
                    building.load();
                    farm.buildStructure(building, tile, Game1.player, skipSafetyChecks: true);

                    if (building.GetIndoors() is Cabin cabin)
                    {
                        cabin.CreateFarmhand();
                    }
                }
                catch (Exception ex)
                {
                    context?.Monitor.Log($"[FarmSlotModule] Error aprovisionando cabaña {i + 1}: {ex.Message}", LogLevel.Warn);
                }
            }

            context?.Monitor.Log($"[FarmSlotModule] Total de cabañas en la granja: {farm.buildings.Count(b => b.GetIndoors() is Cabin)}.", LogLevel.Info);
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
            chat.addInfoMessage("=== RESIDENCIAS DISPONIBLES EN PELICAN TOWN ===");
            int freeCount = 0;
            foreach (var door in RegisteredDoors)
            {
                DoorAssignment? assignment = null;
                bool isClaimed = context != null && context.WorldState.Doors.TryGetValue(door.DoorId, out assignment);
                string loc = string.IsNullOrWhiteSpace(door.LocationName) ? "Town" : door.LocationName;
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
            chat.addInfoMessage($"Total: {freeCount} residencias libres. Camina hasta la puerta y pulsa [E] para reclamarla.");
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

        private void HandleClaimDoor(long playerId, string doorId, string farmType)
        {
            if (context == null) return;
            var worldState = context.WorldState;

            // Si la puerta ya está asignada
            if (worldState.Doors.ContainsKey(doorId))
            {
                context.Monitor.Log($"[FarmSlotModule] Jugador {playerId} intentó reclamar puerta ocupada {doorId}.", LogLevel.Warn);
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

            // Replicar en GameLocation.modData
            GameLocation? loc = Game1.getLocationFromName(slotId);
            if (loc != null)
            {
                loc.PublishOwnership(playerId, AccessPolicy.Locked);
            }

            context.Monitor.Log($"[FarmSlotModule] Puerta {doorId} asignada con éxito a {playerId} (Granja: {slotId}, Tipo: {resolvedType}).", LogLevel.Info);

            // Sincronizar inmediatamente a todos los peones
            BroadcastDoorsSync();
        }

        private void HandleChangePolicy(long playerId, string farmSlotId, string policyStr)
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
            DoorModel? door = RegisteredDoors.FirstOrDefault(d =>
                string.Equals(string.IsNullOrWhiteSpace(d.LocationName) ? "Town" : d.LocationName, curLoc, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(d.TownTileX - (int)targetTile.X) <= 1
                && Math.Abs(d.TownTileY - (int)targetTile.Y) <= 1);

            if (door != null)
            {
                InteractWithResidentialDoor(door);
            }
        }

        private void InteractWithResidentialDoor(DoorModel door)
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

        private void ShowPolicySelectionDialog(string farmSlotId)
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

        private void WarpToFarm(string farmSlotId)
        {
            // Ubicar coordenadas de llegada de la cabaña según la plantilla elegida
            DoorAssignment? assignment = context?.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == farmSlotId);
            var template = FarmTemplateCatalog.GetTemplate(assignment?.FarmType);

            Game1.warpFarmer(farmSlotId, template.CabinTileX, template.CabinTileY + 2, 2);
        }

        private void ApplyHarmonyPatches(Harmony harmony)
        {
            try
            {
                // Parche para redirigir la salida de una granja Pelican4Ever_Farm_XX hacia su puerta residencial
                harmony.Patch(
                    original: AccessTools.Method(typeof(Game1), nameof(Game1.warpFarmer), new[] { typeof(string), typeof(int), typeof(int), typeof(int) }),
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

                context?.Monitor.Log("[FarmSlotModule] Parches Harmony de enrutado, anti-griefing, culling de red y protección de despertar aplicados.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[FarmSlotModule] Error al aplicar parches Harmony: {ex}", LogLevel.Error);
            }
        }

        public static bool Prefix_WarpFarmer(ref string locationName, ref int tileX, ref int tileY, ref int facingDirectionAfterWarp)
        {
            // Si el jugador está saliendo de una cabaña hacia la granja principal
            if (Game1.currentLocation is Cabin && locationName == "Farm")
            {
                var assignment = context?.WorldState.Doors.Values.FirstOrDefault(d => d.OwnerId == Game1.player.UniqueMultiplayerID);
                if (assignment != null && RegisteredDoors.FirstOrDefault(d => d.DoorId == assignment.DoorId) is { } door)
                {
                    locationName = string.IsNullOrWhiteSpace(door.LocationName) ? "Town" : door.LocationName;
                    tileX = door.TownTileX;
                    tileY = door.TownTileY + 1;
                    facingDirectionAfterWarp = 2;
                }
                else
                {
                    // Si aún no tiene residencia reclamada, ubicarlo en la plaza central de Pelican Town
                    locationName = "Town";
                    tileX = 42;
                    tileY = 56;
                    facingDirectionAfterWarp = 2;
                }
                return true;
            }

            // Si el jugador está saliendo de una granja instanciada hacia el exterior, redirigir a su puerta residencial
            if (Game1.currentLocation != null && Game1.currentLocation.Name.StartsWith(LocationCullingLogic.FarmPrefix))
            {
                if (locationName is "BusStop" or "Backwoods" or "Town")
                {
                    string currentFarm = Game1.currentLocation.Name;
                    var door = RegisteredDoors.FirstOrDefault(d => d.DefaultSlotId == currentFarm);
                    if (door != null)
                    {
                        locationName = string.IsNullOrWhiteSpace(door.LocationName) ? "Town" : door.LocationName;
                        tileX = door.TownTileX;
                        tileY = door.TownTileY + 1; // Salir justo enfrente de la puerta
                        facingDirectionAfterWarp = 2; // Mirando hacia abajo
                    }
                }
            }
            return true;
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
                string doorLoc = string.IsNullOrWhiteSpace(door.LocationName) ? "Town" : door.LocationName;
                if (!string.Equals(curLoc, doorLoc, StringComparison.OrdinalIgnoreCase))
                    continue;

                float dist = Vector2.Distance(playerTile, new Vector2(door.TownTileX, door.TownTileY));
                if (dist > 14f)
                    continue;

                bool isClaimed = context.WorldState.Doors.TryGetValue(door.DoorId, out DoorAssignment? assignment);
                bool isMine = isClaimed && assignment?.OwnerId == myId;

                string badgeText;
                Color badgeColor;

                if (!isClaimed)
                {
                    badgeText = dist <= 2.5f ? "[E / Clic] Reclamar Residencia" : "🏠 Residencia Libre";
                    badgeColor = Color.LightGreen;
                }
                else if (isMine)
                {
                    badgeText = dist <= 2.5f ? "[E / Clic] Entrar a Mi Granja" : "🏠 Mi Residencia";
                    badgeColor = Color.Cyan;
                }
                else
                {
                    string owner = "Vecino";
                    if (context.WorldState.Players.TryGetValue(assignment!.OwnerId, out var rec) && !string.IsNullOrWhiteSpace(rec.PlayerName))
                        owner = rec.PlayerName;
                    badgeText = dist <= 2.5f ? $"[E / Clic] Casa de {owner}" : $"🏠 Casa de {owner}";
                    badgeColor = Color.LightGoldenrodYellow;
                }

                float bounce = (dist <= 2.5f) ? (float)Math.Sin(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 200.0) * 3f : 0f;
                Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, new Vector2(door.TownTileX * 64f + 32f, (door.TownTileY - 1) * 64f + bounce));

                // Dibujar marcador visual en el suelo (poste con cartel de entrada)
                Vector2 worldTilePos = new Vector2(door.TownTileX * 64f, door.TownTileY * 64f);
                Vector2 localTilePos = Game1.GlobalToLocal(Game1.viewport, worldTilePos);

                if (Game1.objectSpriteSheet != null)
                {
                    // Poste de madera (Wood Sign)
                    e.SpriteBatch.Draw(
                        Game1.objectSpriteSheet,
                        new Vector2(localTilePos.X + 16f, localTilePos.Y + 8f),
                        GameLocation.getSourceRectForObject(37),
                        Color.White,
                        0f,
                        Vector2.Zero,
                        2.5f,
                        SpriteEffects.None,
                        (float)(door.TownTileY * 64 + 32) / 10000f
                    );

                    // Icono de casita coloreado
                    Color iconTint = isClaimed ? (isMine ? Color.Cyan : Color.LightGoldenrodYellow) : Color.LightGreen;
                    e.SpriteBatch.Draw(
                        Game1.mouseCursors,
                        new Vector2(localTilePos.X + 22f, localTilePos.Y + 12f),
                        new Rectangle(64, 448, 16, 16),
                        iconTint,
                        0f,
                        Vector2.Zero,
                        1.2f,
                        SpriteEffects.None,
                        (float)(door.TownTileY * 64 + 33) / 10000f
                    );
                }

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
    }
}
