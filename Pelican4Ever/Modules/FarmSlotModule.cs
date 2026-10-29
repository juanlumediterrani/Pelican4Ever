using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Locations;
using StardewValley.Tools;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Gestiona los huecos de granja (Pelican4Ever_Farm_01..04), su inyección en Data/Locations,
    /// las puertas interactivas en Pelican Town y la protección anti-griefing en visitas (TASK-201..204 / ADR-0003).
    /// </summary>
    public sealed class FarmSlotModule : IModule
    {
        private static ModContext? context;
        public static List<DoorModel> RegisteredDoors { get; private set; } = new();

        public string Name => "FarmSlot";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            // Cargar catálogo de puertas desde assets/doors.json
            LoadDoors();

            // Eventos SMAPI
            ctx.Helper.Events.Content.AssetRequested += OnAssetRequested;
            ctx.Helper.Events.Input.ButtonPressed += OnButtonPressed;

            if (Context.IsMainPlayer)
            {
                ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
                ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            }

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
                    context.Monitor.Log($"[FarmSlotModule] {RegisteredDoors.Count} puertas cargadas desde assets/doors.json.", LogLevel.Info);
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
            // Inyectar huecos de granja en Data/Locations
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
                            data[slotId] = new LocationData
                            {
                                DisplayName = $"Granja {i}",
                                CanPlantHere = true,
                                CreateOnLoad = new CreateLocationData
                                {
                                    MapPath = "Maps\\Farm",
                                    Type = "StardewValley.Farm, Stardew Valley",
                                    AlwaysActive = true // Permite construcción y sincro continua para el MVP
                                }
                            };
                        }
                    }
                });
            }
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null)
                return;

            // Asegurarse de que useSeparateWallets esté activo para carteras individuales
            Game1.player.team.useSeparateWallets.Value = true;

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
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null)
                return;

            if (e.FromModID != context.Manifest.UniqueID)
                return;

            if (e.Type == ModMessages.ClaimDoorRequest)
            {
                var payload = e.ReadAs<ClaimDoorPayload>();
                if (payload != null)
                {
                    HandleClaimDoor(e.FromPlayerID, payload.DoorId);
                }
            }
            else if (e.Type == ModMessages.ChangePolicyRequest)
            {
                var payload = e.ReadAs<ChangePolicyPayload>();
                if (payload != null)
                {
                    HandleChangePolicy(e.FromPlayerID, payload.FarmSlotId, payload.Policy);
                }
            }
        }

        private void HandleClaimDoor(long playerId, string doorId)
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

            var assignment = new DoorAssignment
            {
                DoorId = doorId,
                FarmSlotId = slotId,
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

            context.Monitor.Log($"[FarmSlotModule] Puerta {doorId} asignada con éxito a {playerId} (Granja: {slotId}).", LogLevel.Info);
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
            }
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || context == null)
                return;

            if (!e.Button.IsActionButton())
                return;

            // Comprobar si el jugador está interactuando frente a una puerta en Town
            if (Game1.currentLocation != null && Game1.currentLocation.Name == "Town")
            {
                Vector2 targetTile = e.Cursor.GrabTile;
                DoorModel? door = RegisteredDoors.FirstOrDefault(d => Math.Abs(d.TownTileX - (int)targetTile.X) <= 1 && Math.Abs(d.TownTileY - (int)targetTile.Y) <= 1);

                if (door != null)
                {
                    InteractWithTownDoor(door);
                }
            }
        }

        private void InteractWithTownDoor(DoorModel door)
        {
            if (context == null) return;
            var worldState = context.WorldState;

            worldState.Doors.TryGetValue(door.DoorId, out DoorAssignment? assignment);
            long myId = Game1.player.UniqueMultiplayerID;

            if (assignment == null)
            {
                // Puerta libre: ofrecer reclamación
                Response[] choices = new[]
                {
                    new Response("Claim", "Reclamar esta casa para mi granja"),
                    new Response("Cancel", "Cancelar")
                };

                Game1.currentLocation.createQuestionDialogue(
                    $"Esta residencia está desocupada. ¿Deseas reclamarla para vincularla a tu granja?",
                    choices,
                    (who, whichAnswer) =>
                    {
                        if (whichAnswer == "Claim")
                        {
                            context.Helper.Multiplayer.SendMessage(
                                new ClaimDoorPayload { DoorId = door.DoorId },
                                ModMessages.ClaimDoorRequest,
                                new[] { context.Manifest.UniqueID },
                                new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                            );

                            if (Context.IsMainPlayer)
                            {
                                HandleClaimDoor(myId, door.DoorId);
                            }

                            Game1.addHUDMessage(new HUDMessage("Solicitud de reclamación enviada.", HUDMessage.newQuest_type));
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
                    "Tu residencia en Pelican Town.",
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
                    $"Residencia de {ownerName}. Estado: {assignment.Policy}.",
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
            if (context == null) return;

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
            // Warp hacia la granja instanciada (coordenadas de llegada estándar 64, 15)
            Game1.warpFarmer(farmSlotId, 64, 15, 2);
        }

        private void ApplyHarmonyPatches(Harmony harmony)
        {
            try
            {
                // Parche para redirigir la salida de una granja Pelican4Ever_Farm_XX hacia su puerta en Town
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

                context?.Monitor.Log("[FarmSlotModule] Parches Harmony de enrutado y anti-griefing aplicados.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[FarmSlotModule] Error al aplicar parches Harmony: {ex}", LogLevel.Error);
            }
        }

        public static bool Prefix_WarpFarmer(ref string locationName, ref int tileX, ref int tileY, ref int facingDirectionAfterWarp)
        {
            // Si el jugador está saliendo de una granja instanciada hacia BusStop o Backwoods, redirigir a su puerta en Town
            if (Game1.currentLocation != null && Game1.currentLocation.Name.StartsWith("Pelican4Ever_Farm_"))
            {
                if (locationName is "BusStop" or "Backwoods")
                {
                    string currentFarm = Game1.currentLocation.Name;
                    var door = RegisteredDoors.FirstOrDefault(d => d.DefaultSlotId == currentFarm);
                    if (door != null)
                    {
                        locationName = "Town";
                        tileX = door.TownTileX;
                        tileY = door.TownTileY + 1; // Salir justo un tile enfrente de la puerta
                        facingDirectionAfterWarp = 2; // Mirando hacia abajo
                    }
                }
            }
            return true;
        }

        public static bool Prefix_ToolBeginUsing(Tool __instance, GameLocation location, Farmer who)
        {
            // Si no es el jugador local, no intervenir
            if (who != Game1.player)
                return true;

            // Verificar si estamos en una granja ajena
            if (location != null && location.Name.StartsWith("Pelican4Ever_Farm_"))
            {
                long? ownerId = location.GetFarmOwnerId();
                if (ownerId.HasValue && ownerId.Value != who.UniqueMultiplayerID)
                {
                    // Bloquear hachas, picos, azadas sobre cultivos o bombas en granjas ajenas
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
            if (loc != null && loc.Name.StartsWith("Pelican4Ever_Farm_"))
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
    }
}
