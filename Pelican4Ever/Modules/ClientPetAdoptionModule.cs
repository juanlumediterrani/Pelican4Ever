using System;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pelican4Ever.Core.Pets;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.Menus;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Gestiona la adopción de mascotas para jugadores clientes al construir un cuenco para mascotas (Épica 20).
    /// Conecta la compra del cuenco en la carpintería con el despacho de la carta de Marnie al día siguiente
    /// y la ejecución del evento de adopción en la residencia privada del cliente.
    /// </summary>
    public sealed class ClientPetAdoptionModule : IModule
    {
        private static ModContext? context;
        private static PetAdoptionRecord localRecord = new();
        private static bool isAdoptionPromptActive = false;

        public const string MsgBowlPurchased = "Pelican4Ever.PetBowlPurchased";
        public const string MsgPetAdoptionSync = "Pelican4Ever.PetAdoptionSync";
        public const string MsgPetAdopted = "Pelican4Ever.PetAdopted";
        public const string MsgPetAdoptionDeclined = "Pelican4Ever.PetAdoptionDeclined";
        public const string MsgQueryPetAdoption = "Pelican4Ever.QueryPetAdoption";

        public string Name => "ClientPetAdoption";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            ctx.Helper.Events.Content.AssetRequested += OnAssetRequested;
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.Player.Warped += OnWarped;
            ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;

            if (Context.IsMainPlayer)
            {
                ctx.Helper.Events.GameLoop.DayStarted += OnDayStarted;
            }

            try
            {
                ctx.Harmony.Patch(
                    original: AccessTools.Method(typeof(CarpenterMenu), "tryToBuild"),
                    postfix: new HarmonyMethod(typeof(ClientPetAdoptionModule), nameof(Postfix_CarpenterMenu_TryToBuild))
                );
            }
            catch (Exception ex)
            {
                ctx.Monitor.Log($"[ClientPetAdoption] Error al parchear CarpenterMenu.tryToBuild: {ex}", LogLevel.Error);
            }
        }

        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo("Data/Mail"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, string>().Data;
                    string title = I18n.Get("marnie.pet_letter.title");
                    string body = I18n.Get("marnie.pet_letter.body", new { player = Game1.player?.Name ?? string.Empty });
                    data[PetAdoptionLogic.MarnieMailKey] = $"{body}[#]{title}";
                });
            }
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            isAdoptionPromptActive = false;
            if (context == null) return;

            if (Context.IsMainPlayer)
            {
                if (context.WorldState.Players.TryGetValue(Game1.player.UniqueMultiplayerID, out var rec))
                {
                    localRecord = rec.PetAdoption;
                }
            }
            else
            {
                // Solicitar estado autoritativo al host
                context.Helper.Multiplayer.SendMessage(
                    Game1.player.UniqueMultiplayerID,
                    MsgQueryPetAdoption,
                    new[] { context.Manifest.UniqueID },
                    new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );
            }
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null)
                return;

            int currentGameDay = Game1.Date.TotalDays;
            bool changed = false;

            foreach (var (playerId, playerRecord) in context.WorldState.Players)
            {
                if (PetAdoptionLogic.ShouldDeliverLetter(playerRecord.PetAdoption, currentGameDay))
                {
                    PetAdoptionLogic.MarkLetterDelivered(playerRecord.PetAdoption);
                    changed = true;

                    Farmer? farmer = Game1.getFarmerMaybeOffline(playerId);
                    if (farmer != null)
                    {
                        if (!farmer.mailbox.Contains(PetAdoptionLogic.MarnieMailKey) && !farmer.mailReceived.Contains(PetAdoptionLogic.MarnieMailKey))
                        {
                            farmer.mailbox.Add(PetAdoptionLogic.MarnieMailKey);
                        }
                    }

                    // Sincronizar al jugador
                    context.Helper.Multiplayer.SendMessage(
                        playerRecord.PetAdoption,
                        MsgPetAdoptionSync,
                        new[] { context.Manifest.UniqueID },
                        new[] { playerId }
                    );

                    context.Monitor.Log($"[ClientPetAdoption] Carta de Marnie despachada al buzón del jugador {playerRecord.PlayerName} ({playerId}).", LogLevel.Info);
                }
            }

            if (changed)
            {
                context.Helper.Data.WriteSaveData(ServerWorldState.SaveDataKey, context.WorldState);
            }
        }

        private void OnWarped(object? sender, WarpedEventArgs e)
        {
            if (!e.IsLocalPlayer)
                return;

            CheckAndTriggerAdoptionEvent();
        }

        private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || !Context.IsPlayerFree)
                return;

            CheckAndTriggerAdoptionEvent();
        }

        private void CheckAndTriggerAdoptionEvent()
        {
            if (context == null || isAdoptionPromptActive || !Context.IsPlayerFree)
                return;

            if (localRecord == null || localRecord.State != PetAdoptionState.LetterDelivered)
                return;

            if (Game1.currentLocation is not Farm farm)
                return;

            string currentMap = farm.NameOrUniqueName;
            long playerId = Game1.player.UniqueMultiplayerID;

            // Determinar si el jugador local es propietario de esta ranura
            long ownerId = 0;
            var assignment = context.WorldState?.Doors.Values.FirstOrDefault(d => string.Equals(d.FarmSlotId, currentMap, StringComparison.OrdinalIgnoreCase));
            if (assignment != null)
            {
                ownerId = assignment.OwnerId;
            }
            else if (farm.modData.TryGetValue("Pelican4Ever.OwnerId", out string? ownerStr) && long.TryParse(ownerStr, out long parsedOwner))
            {
                ownerId = parsedOwner;
            }

            if (!PetAdoptionLogic.CanTriggerAdoptionEvent(localRecord, playerId, ownerId, currentMap, Game1.isRaining, Game1.timeOfDay))
                return;

            // Validar que el jugador haya recibido la carta
            if (!Game1.player.mailReceived.Contains(PetAdoptionLogic.MarnieMailKey) && !Game1.player.mailbox.Contains(PetAdoptionLogic.MarnieMailKey))
                return;

            StartAdoptionEvent(farm);
        }

        private void StartAdoptionEvent(Farm farm)
        {
            isAdoptionPromptActive = true;

            Point entry = farm.GetMainFarmHouseEntry();
            Vector2 marnieTile = new Vector2(entry.X - 1, entry.Y + 1);
            Vector2 petTile = new Vector2(entry.X, entry.Y + 1);

            string petType = string.IsNullOrWhiteSpace(localRecord.PetType)
                ? (Game1.player.catPerson ? "Cat" : "Dog")
                : localRecord.PetType;
            string petBreed = string.IsNullOrWhiteSpace(localRecord.PetBreed)
                ? Game1.player.whichPetBreed.ToString()
                : localRecord.PetBreed;

            // Instanciar actores temporales
            NPC marnie = new NPC(
                new AnimatedSprite("Characters\\Marnie", 0, 16, 32),
                marnieTile * 64f,
                farm.NameOrUniqueName,
                2,
                "Marnie",
                false,
                null,
                Game1.content.Load<Texture2D>("Portraits\\Marnie")
            );

            Pet petPreview = new Pet((int)petTile.X, (int)petTile.Y, petBreed, petType);

            farm.characters.Add(marnie);
            farm.characters.Add(petPreview);

            Game1.player.faceGeneralDirection(marnieTile * 64f);
            Game1.player.freezePause = 1000;

            string petDisplay = petType == "Cat"
                ? (LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.es ? "gato" : "cat")
                : (LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.es ? "perro" : "dog");

            // Diálogo de introducción de Marnie
            Game1.drawDialogue(marnie, I18n.Get("marnie.pet_event.intro", new { player = Game1.player.Name }));

            // Pregunta de adopción al finalizar el diálogo
            QuestionDialog.Ask(
                I18n.Get("marnie.pet_event.question", new { petType = petDisplay }),
                new[]
                {
                    new Response("Yes", I18n.Get("marnie.pet_event.adopt_yes")),
                    new Response("No", I18n.Get("marnie.pet_event.adopt_no"))
                },
                (who, answer) =>
                {
                    if (answer == "Yes")
                    {
                        string defaultPetName = Dialogue.randomName();
                        Game1.activeClickableMenu = new NamingMenu(
                            chosenName =>
                            {
                                string finalName = string.IsNullOrWhiteSpace(chosenName) ? defaultPetName : chosenName.Trim();
                                CompleteAdoption(farm, marnie, petPreview, finalName, petType, petBreed);
                            },
                            I18n.Get("marnie.pet_event.naming_title", new { petType = petDisplay }),
                            defaultPetName
                        );
                    }
                    else
                    {
                        DeclineAdoption(farm, marnie, petPreview);
                    }
                }
            );
        }

        private void CompleteAdoption(Farm farm, NPC marnie, Pet petPreview, string finalName, string petType, string petBreed)
        {
            farm.characters.Remove(marnie);
            farm.characters.Remove(petPreview);

            localRecord.State = PetAdoptionState.Adopted;
            localRecord.PetName = finalName;
            localRecord.PetType = petType;
            localRecord.PetBreed = petBreed;

            var payload = new PetAdoptedPayload
            {
                SlotName = farm.NameOrUniqueName,
                PetName = finalName,
                PetType = petType,
                PetBreed = petBreed,
                BowlTileX = localRecord.BowlTileX,
                BowlTileY = localRecord.BowlTileY
            };

            if (Context.IsMainPlayer)
            {
                ProcessPetAdoption(Game1.player.UniqueMultiplayerID, payload);
            }
            else if (context != null)
            {
                context.Helper.Multiplayer.SendMessage(
                    payload,
                    MsgPetAdopted,
                    new[] { context.Manifest.UniqueID },
                    new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );
            }

            Game1.drawObjectDialogue(I18n.Get("marnie.pet_event.accepted", new { petName = finalName }));
            isAdoptionPromptActive = false;
        }

        private void DeclineAdoption(Farm farm, NPC marnie, Pet petPreview)
        {
            farm.characters.Remove(marnie);
            farm.characters.Remove(petPreview);

            localRecord.State = PetAdoptionState.Declined;

            if (Context.IsMainPlayer)
            {
                if (context?.WorldState.Players.TryGetValue(Game1.player.UniqueMultiplayerID, out var rec) == true)
                {
                    PetAdoptionLogic.RecordDeclined(rec.PetAdoption);
                    context.Helper.Data.WriteSaveData(ServerWorldState.SaveDataKey, context.WorldState);
                }
            }
            else if (context != null)
            {
                context.Helper.Multiplayer.SendMessage(
                    Game1.player.UniqueMultiplayerID,
                    MsgPetAdoptionDeclined,
                    new[] { context.Manifest.UniqueID },
                    new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );
            }

            Game1.drawObjectDialogue(I18n.Get("marnie.pet_event.declined"));
            isAdoptionPromptActive = false;
        }

        private static void ProcessPetAdoption(long playerId, PetAdoptedPayload payload)
        {
            if (context == null) return;

            GameLocation? loc = Game1.getLocationFromName(payload.SlotName);
            if (loc == null)
            {
                context.Monitor.Log($"[ClientPetAdoption] No se encontró el mapa {payload.SlotName} para spawnear la mascota.", LogLevel.Warn);
                return;
            }

            // Instanciar mascota autoritativa
            Pet pet = new Pet(payload.BowlTileX, payload.BowlTileY, payload.PetBreed, payload.PetType);
            pet.Name = payload.PetName;
            pet.displayName = payload.PetName;
            pet.homeLocationName.Value = loc.NameOrUniqueName;

            // Vincular al cuenco en el mapa
            var petBowl = loc.buildings.OfType<PetBowl>().FirstOrDefault(b => b.tileX.Value == payload.BowlTileX && b.tileY.Value == payload.BowlTileY)
                          ?? loc.buildings.OfType<PetBowl>().FirstOrDefault();
            if (petBowl != null)
            {
                petBowl.AssignPet(pet);
            }

            loc.characters.Add(pet);

            // Persistir en el estado autoritativo
            var playerRecord = context.WorldState.GetOrCreatePlayer(playerId, string.Empty);
            PetAdoptionLogic.RecordAdoption(playerRecord.PetAdoption, payload.PetName, payload.PetType, payload.PetBreed);
            context.Helper.Data.WriteSaveData(ServerWorldState.SaveDataKey, context.WorldState);

            context.Monitor.Log($"[ClientPetAdoption] Mascota '{payload.PetName}' ({payload.PetType}) vinculada a cuenco en {payload.SlotName} para el jugador {playerId}.", LogLevel.Info);

            // Notificar a todos los clientes para sincronización
            context.Helper.Multiplayer.SendMessage(
                playerRecord.PetAdoption,
                MsgPetAdoptionSync,
                new[] { context.Manifest.UniqueID },
                new[] { playerId }
            );
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || e.FromModID != context.Manifest.UniqueID)
                return;

            if (Context.IsMainPlayer)
            {
                if (e.Type == MsgBowlPurchased && e.ReadAs<PetBowlPurchasedPayload>() is { } bowlPayload)
                {
                    var playerRecord = context.WorldState.GetOrCreatePlayer(e.FromPlayerID, string.Empty);
                    PetAdoptionLogic.RecordPurchase(
                        playerRecord.PetAdoption,
                        bowlPayload.SlotName,
                        bowlPayload.TileX,
                        bowlPayload.TileY,
                        bowlPayload.GameDay,
                        bowlPayload.PetType,
                        bowlPayload.PetBreed
                    );
                    context.Helper.Data.WriteSaveData(ServerWorldState.SaveDataKey, context.WorldState);
                    context.Monitor.Log($"[ClientPetAdoption] Registrada compra de Pet Bowl para {e.FromPlayerID} en {bowlPayload.SlotName} ({bowlPayload.TileX}, {bowlPayload.TileY}).", LogLevel.Info);
                }
                else if (e.Type == MsgPetAdopted && e.ReadAs<PetAdoptedPayload>() is { } adoptedPayload)
                {
                    ProcessPetAdoption(e.FromPlayerID, adoptedPayload);
                }
                else if (e.Type == MsgPetAdoptionDeclined)
                {
                    if (context.WorldState.Players.TryGetValue(e.FromPlayerID, out var playerRecord))
                    {
                        PetAdoptionLogic.RecordDeclined(playerRecord.PetAdoption);
                        context.Helper.Data.WriteSaveData(ServerWorldState.SaveDataKey, context.WorldState);
                        context.Monitor.Log($"[ClientPetAdoption] Adopción declinada por el jugador {e.FromPlayerID}.", LogLevel.Info);
                    }
                }
                else if (e.Type == MsgQueryPetAdoption)
                {
                    if (context.WorldState.Players.TryGetValue(e.FromPlayerID, out var playerRecord))
                    {
                        context.Helper.Multiplayer.SendMessage(
                            playerRecord.PetAdoption,
                            MsgPetAdoptionSync,
                            new[] { context.Manifest.UniqueID },
                            new[] { e.FromPlayerID }
                        );
                    }
                }
            }
            else
            {
                if (e.Type == MsgPetAdoptionSync && e.ReadAs<PetAdoptionRecord>() is { } record)
                {
                    localRecord = record;
                }
            }
        }

        /// <summary>
        /// Hook invocado al construir exitosamente un edificio en CarpenterMenu.
        /// </summary>
        public static void Postfix_CarpenterMenu_TryToBuild(CarpenterMenu __instance, bool __result, Building ___currentBuilding, GameLocation ___TargetLocation)
        {
            if (!__result || ___currentBuilding == null || ___TargetLocation == null || context == null)
                return;

            string bType = ___currentBuilding.buildingType?.Value ?? string.Empty;
            if (!bType.Equals("Pet Bowl", StringComparison.OrdinalIgnoreCase))
                return;

            int tileX = (Game1.viewport.X + Game1.getOldMouseX()) / 64;
            int tileY = (Game1.viewport.Y + Game1.getOldMouseY()) / 64;
            string slotName = ___TargetLocation.NameOrUniqueName;
            long playerId = Game1.player.UniqueMultiplayerID;
            int gameDay = Game1.Date.TotalDays;
            string petType = Game1.player.whichPetType.Value;
            if (string.IsNullOrWhiteSpace(petType))
                petType = Game1.player.catPerson ? "Cat" : "Dog";
            string petBreed = Game1.player.whichPetBreed.ToString();

            if (Context.IsMainPlayer)
            {
                var playerRecord = context.WorldState.GetOrCreatePlayer(playerId, Game1.player.Name);
                PetAdoptionLogic.RecordPurchase(playerRecord.PetAdoption, slotName, tileX, tileY, gameDay, petType, petBreed);
                localRecord = playerRecord.PetAdoption;
                context.Helper.Data.WriteSaveData(ServerWorldState.SaveDataKey, context.WorldState);
                context.Monitor.Log($"[ClientPetAdoption] Cuenco registrado directamente para host en {slotName} ({tileX}, {tileY}).", LogLevel.Info);
            }
            else
            {
                var payload = new PetBowlPurchasedPayload
                {
                    SlotName = slotName,
                    TileX = tileX,
                    TileY = tileY,
                    GameDay = gameDay,
                    PetType = petType,
                    PetBreed = petBreed
                };

                context.Helper.Multiplayer.SendMessage(
                    payload,
                    MsgBowlPurchased,
                    new[] { context.Manifest.UniqueID },
                    new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );

                localRecord.State = PetAdoptionState.BowlPurchased;
                localRecord.FarmSlotId = slotName;
                localRecord.BowlTileX = tileX;
                localRecord.BowlTileY = tileY;
                localRecord.PurchaseGameDay = gameDay;
                localRecord.PetType = petType;
                localRecord.PetBreed = petBreed;

                context.Monitor.Log($"[ClientPetAdoption] Notificada al host la compra de Pet Bowl en {slotName} ({tileX}, {tileY}).", LogLevel.Info);
            }
        }
    }

    public class PetBowlPurchasedPayload
    {
        public string SlotName { get; set; } = string.Empty;
        public int TileX { get; set; }
        public int TileY { get; set; }
        public int GameDay { get; set; }
        public string PetType { get; set; } = "Dog";
        public string PetBreed { get; set; } = "0";
    }

    public class PetAdoptedPayload
    {
        public string SlotName { get; set; } = string.Empty;
        public string PetName { get; set; } = string.Empty;
        public string PetType { get; set; } = "Dog";
        public string PetBreed { get; set; } = "0";
        public int BowlTileX { get; set; }
        public int BowlTileY { get; set; }
    }
}
