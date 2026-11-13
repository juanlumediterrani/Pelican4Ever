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
    /// Conecta la compra del cuenco en la carpintería con la visita de Marnie a la puerta de la residencia
    /// en el siguiente despertar del jugador (sin carta de por medio) y la vinculación autoritativa de la mascota.
    /// </summary>
    public sealed class ClientPetAdoptionModule : IModule
    {
        private static ModContext? context;
        private static PetAdoptionRecord localRecord = new();
        private static bool isAdoptionPromptActive = false;
        private static bool wakeTriggerPending = false;

        /// <summary>Último motivo registrado por el que la visita de Marnie no puede empezar (evita spam en el log).</summary>
        private static string lastAdoptionGate = string.Empty;

        /// <summary>Actores temporales de la visita en curso, para poder retirarlos si se interrumpe.</summary>
        private static Farm? activeAdoptionFarm;
        private static NPC? activeMarnie;
        private static Pet? activePetPreview;

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
            ctx.Helper.Events.GameLoop.DayStarted += OnDayStarted;
            ctx.Helper.Events.Player.Warped += OnWarped;
            ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;

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

        /// <summary>
        /// Mantiene la definición histórica de la carta de Marnie en <c>Data/Mail</c> para que las partidas
        /// que ya la tuvieran en el buzón puedan seguir leyéndola. El flujo actual no vuelve a entregarla.
        /// </summary>
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
            wakeTriggerPending = false;
            lastAdoptionGate = string.Empty;
            localRecord = new PetAdoptionRecord();

            if (context == null) return;

            if (Context.IsMainPlayer)
            {
                if (context.WorldState.Players.TryGetValue(Game1.player.UniqueMultiplayerID, out var rec))
                {
                    localRecord = rec.PetAdoption;
                }

                UpdateWakeTriggerPending();
                HealStuckPets("carga de partida");
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
            if (context == null)
                return;

            wakeTriggerPending = false;
            lastAdoptionGate = string.Empty;

            if (isAdoptionPromptActive)
                CancelAdoptionEvent();

            if (Context.IsMainPlayer)
            {
                if (context.WorldState.Players.TryGetValue(Game1.player.UniqueMultiplayerID, out var rec))
                {
                    localRecord = rec.PetAdoption;
                }
            }
            else
            {
                // El host es la autoridad; pedimos el estado actualizado al empezar el día.
                context.Helper.Multiplayer.SendMessage(
                    Game1.player.UniqueMultiplayerID,
                    MsgQueryPetAdoption,
                    new[] { context.Manifest.UniqueID },
                    new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );
            }

            UpdateWakeTriggerPending();

            if (Context.IsMainPlayer)
            {
                HealStuckPets("inicio del día");
            }
        }

        /// <summary>
        /// La IA de <see cref="Pet"/> solo se mueve si <c>CurrentBehavior</c> referencia un comportamiento
        /// existente en <c>Data/Pets</c>. Las mascotas creadas por este módulo no pasan por
        /// <c>PetLicense.warpToFarmHouse</c>, que es quien inicializa ese campo en el juego vanilla, así que
        /// se quedaban inmóviles junto al cuenco. Esta rutina las repara también en partidas ya guardadas.
        /// </summary>
        private static void HealStuckPets(string origin)
        {
            if (context == null || !Context.IsMainPlayer)
                return;

            foreach (Pet pet in Utility.getAllPets())
            {
                EnsurePetBehavior(pet, origin);
            }
        }

        /// <summary>Garantiza que la mascota tenga un comportamiento válido de <c>Data/Pets</c>.</summary>
        private static void EnsurePetBehavior(Pet pet, string origin)
        {
            if (string.IsNullOrEmpty(pet.CurrentBehavior) || pet.GetCurrentPetBehavior() == null)
            {
                pet.CurrentBehavior = "Walk";
                context?.Monitor.Log(
                    $"[ClientPetAdoption] Comportamiento de la mascota '{pet.displayName}' inicializado a 'Walk' ({origin}).",
                    LogLevel.Trace);
            }

            // Las primeras versiones la creaban sobre la casilla del cuenco, que es un edificio
            // con colisión. Se recoloca delante para que pueda empezar a caminar sin esperar al próximo día.
            PetBowl? bowl = pet.GetPetBowl();
            if (bowl != null
                && (int)pet.TilePoint.X == bowl.tileX.Value
                && (int)pet.TilePoint.Y == bowl.tileY.Value)
            {
                pet.WarpToPetBowl();
                context?.Monitor.Log(
                    $"[ClientPetAdoption] Mascota '{pet.displayName}' recolocada delante del cuenco ({origin}).",
                    LogLevel.Trace);
            }
        }

        /// <summary>
        /// Marca que en este despertar debe producirse la visita de Marnie si el cuenco se compró antes de dormir.
        /// </summary>
        private static void UpdateWakeTriggerPending()
        {
            bool pending = PetAdoptionLogic.HasWokenSincePurchase(localRecord, Game1.Date.TotalDays);
            if (pending && !wakeTriggerPending)
            {
                context?.Monitor.Log(
                    $"[ClientPetAdoption] Visita de Marnie pendiente para {Game1.player?.Name ?? "<jugador>"} en '{localRecord.FarmSlotId}' (estado={localRecord.State}, compra día {localRecord.PurchaseGameDay}, hoy {Game1.Date.TotalDays}).",
                    LogLevel.Info);
            }

            wakeTriggerPending = pending;
        }

        private void OnWarped(object? sender, WarpedEventArgs e)
        {
            if (!e.IsLocalPlayer)
                return;

            // Si el jugador abandona la granja a mitad de la visita, retirar los actores temporales
            // para no dejarlos sueltos ni bloquear futuros intentos.
            if (isAdoptionPromptActive && !ReferenceEquals(e.NewLocation, activeAdoptionFarm))
                CancelAdoptionEvent();

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

            if (!PetAdoptionLogic.IsAwaitingAdoption(localRecord))
                return;

            if (Game1.currentLocation is not Farm farm)
            {
                NoteAdoptionGate(
                    $"la ubicación actual '{Game1.currentLocation?.NameOrUniqueName ?? "<null>"}' " +
                    $"({Game1.currentLocation?.GetType().Name ?? "?"}) no es una granja");
                return;
            }

            int today = Game1.Date.TotalDays;
            bool wokenSincePurchase = PetAdoptionLogic.HasWokenSincePurchase(localRecord, today);
            if (!wakeTriggerPending && !wokenSincePurchase)
            {
                NoteAdoptionGate($"aún no ha dormido desde la compra (compra día {localRecord.PurchaseGameDay}, hoy {today})");
                return;
            }

            string currentMap = farm.NameOrUniqueName;
            long playerId = Game1.player.UniqueMultiplayerID;

            // Determinar si el jugador local es propietario de esta ranura
            long ownerId = 0;
            var assignment = context.WorldState?.Doors.Values.FirstOrDefault(d => string.Equals(d.FarmSlotId, currentMap, StringComparison.OrdinalIgnoreCase));
            AnnexAssignment? annex = context.WorldState?.FindAnnexById(currentMap);
            if (assignment != null)
            {
                ownerId = assignment.OwnerId;
            }
            else if (annex != null)
            {
                ownerId = annex.OwnerId;
            }
            else if (farm.modData.TryGetValue("Pelican4Ever.OwnerId", out string? ownerStr) && long.TryParse(ownerStr, out long parsedOwner))
            {
                ownerId = parsedOwner;
            }
            else if (Context.IsMainPlayer
                && string.Equals(farm.NameOrUniqueName, "Farm", StringComparison.OrdinalIgnoreCase))
            {
                // La granja principal pertenece siempre al anfitrión.
                ownerId = playerId;
            }

            if (!PetAdoptionLogic.CanTriggerAdoptionEvent(localRecord, playerId, ownerId, currentMap, Game1.timeOfDay))
            {
                NoteAdoptionGate(
                    $"condiciones fuera de rango: jugador={playerId}, propietario={ownerId}, " +
                    $"mapa='{currentMap}', esperado='{localRecord.FarmSlotId}', hora={Game1.timeOfDay}");
                return;
            }

            lastAdoptionGate = string.Empty;
            wakeTriggerPending = false;
            StartAdoptionEvent(farm);
        }

        /// <summary>Registra una sola vez el motivo por el que la visita de Marnie aún no puede empezar.</summary>
        private static void NoteAdoptionGate(string gate)
        {
            if (string.Equals(lastAdoptionGate, gate, StringComparison.Ordinal))
                return;

            lastAdoptionGate = gate;
            context?.Monitor.Log($"[ClientPetAdoption] Visita de Marnie pendiente; bloqueada por: {gate}.", LogLevel.Trace);
        }

        /// <summary>Retira a Marnie y a la mascota de muestra si la visita se interrumpe o queda obsoleta.</summary>
        private static void CancelAdoptionEvent()
        {
            if (activeAdoptionFarm != null)
            {
                if (activeMarnie != null)
                    activeAdoptionFarm.characters.Remove(activeMarnie);
                if (activePetPreview != null)
                    activeAdoptionFarm.characters.Remove(activePetPreview);
            }

            activeAdoptionFarm = null;
            activeMarnie = null;
            activePetPreview = null;
            isAdoptionPromptActive = false;
        }

        private void StartAdoptionEvent(Farm farm)
        {
            isAdoptionPromptActive = true;
            lastAdoptionGate = string.Empty;

            context?.Monitor.Log(
                $"[ClientPetAdoption] Marnie se presenta en la puerta de '{farm.NameOrUniqueName}' para {Game1.player.Name}.",
                LogLevel.Info);

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
                Game1.content.Load<Texture2D>("Portraits\\Marnie"),
                true
            );

            Pet petPreview = new Pet((int)petTile.X, (int)petTile.Y, petBreed, petType);

            farm.characters.Add(marnie);
            farm.characters.Add(petPreview);

            activeAdoptionFarm = farm;
            activeMarnie = marnie;
            activePetPreview = petPreview;

            Game1.player.faceGeneralDirection(marnieTile * 64f);
            Game1.player.freezePause = 1000;

            string petDisplay = petType == "Cat"
                ? (LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.es ? "gato" : "cat")
                : (LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.es ? "perro" : "dog");

            // Diálogo de introducción de Marnie
            marnie.CurrentDialogue.Push(new Dialogue(marnie, null, I18n.Get("marnie.pet_event.intro", new { player = Game1.player.Name })));
            Game1.drawDialogue(marnie);

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
            CancelAdoptionEvent();

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
                localRecord.State = PetAdoptionState.Adopted;
                context.Helper.Multiplayer.SendMessage(
                    payload,
                    MsgPetAdopted,
                    new[] { context.Manifest.UniqueID },
                    new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );
            }

            if (localRecord.State != PetAdoptionState.Adopted)
                localRecord.State = PetAdoptionState.Adopted;

            Game1.drawObjectDialogue(I18n.Get("marnie.pet_event.accepted", new { petName = finalName }));
            isAdoptionPromptActive = false;
        }

        private void DeclineAdoption(Farm farm, NPC marnie, Pet petPreview)
        {
            CancelAdoptionEvent();

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

            localRecord.State = PetAdoptionState.Declined;

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

            // Localizar el cuenco antes de crear la mascota: su casilla canónica es la de delante
            // (PetBowl.GetPetSpot), no la del edificio, que bloquea el movimiento.
            var petBowl = loc.buildings.OfType<PetBowl>().FirstOrDefault(b => b.tileX.Value == payload.BowlTileX && b.tileY.Value == payload.BowlTileY)
                          ?? loc.buildings.OfType<PetBowl>().FirstOrDefault();
            Point petSpot = petBowl?.GetPetSpot() ?? new Point(payload.BowlTileX, payload.BowlTileY);

            // Instanciar mascota autoritativa
            Pet pet = new Pet(petSpot.X, petSpot.Y, payload.PetBreed, payload.PetType);
            pet.Name = payload.PetName;
            pet.displayName = payload.PetName;
            pet.currentLocation = loc;

            // Vincular al cuenco en el mapa. AssignPet copia el nombre del mapa desde el cuenco,
            // así que la asignación autoritativa se hace después para no quedar sobrescrita.
            if (petBowl != null)
            {
                petBowl.AssignPet(pet);
            }

            pet.homeLocationName.Value = loc.NameOrUniqueName;
            EnsurePetBehavior(pet, "adopción");

            if (petBowl == null)
            {
                context.Monitor.Log($"[ClientPetAdoption] La mascota '{payload.PetName}' se creó sin cuenco asociado en {payload.SlotName} ({payload.BowlTileX}, {payload.BowlTileY}).", LogLevel.Warn);
            }
            else
            {
                context.Monitor.Log($"[ClientPetAdoption] Cuenco {payload.BowlTileX},{payload.BowlTileY} vinculado a petId {pet.petId.Value}.", LogLevel.Trace);
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
                    UpdateWakeTriggerPending();
                    context.Monitor.Log(
                        $"[ClientPetAdoption] Estado de adopción sincronizado desde el host: estado={record.State}, cuenco en '{record.FarmSlotId}' (compra día {record.PurchaseGameDay}).",
                        LogLevel.Trace);
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
            string petType = Game1.player.whichPetType;
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
