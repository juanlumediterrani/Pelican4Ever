using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pelican4Ever.Core.Pets;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.GameData.Pets;
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

        /// <summary>Ticks consecutivos en los que la mascota propia no aparece en la copia local del cliente.</summary>
        private static int missingPetTicks;

        /// <summary>Muestras (1 s) en las que cada mascota quería pasear y no avanzaba, por petId.</summary>
        private static readonly Dictionary<Guid, int> petStuckSamples = new();

        /// <summary>
        /// Indica si el cliente ya recibió del anfitrión su registro de adopción. Hasta entonces
        /// <see cref="localRecord"/> está vacío y no dice cuál es la mascota propia.
        /// </summary>
        private static bool localRecordSynced;

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
            localRecordSynced = false;
            petStuckSamples.Clear();

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

        /// <summary>
        /// Garantiza que la mascota tenga un comportamiento que la IA de vanilla sepa ejecutar.
        /// «Sleep» es válido aunque no figure en <c>Data/Pets</c>: lo gestiona <c>Pet.RunState</c> directamente
        /// (es el estado inicial y el nocturno), así que no debe tratarse como inexistente.
        /// </summary>
        private static void EnsurePetBehavior(Pet pet, string origin)
        {
            string current = pet.CurrentBehavior;
            if (string.Equals(current, "Sleep", StringComparison.Ordinal) || pet.GetCurrentPetBehavior() != null)
                return;

            pet.CurrentBehavior = "Sleep";
            context?.Monitor.Log(
                $"[ClientPetAdoption] Comportamiento de la mascota '{pet.displayName}' ajustado a 'Sleep' (era '{current}', {origin}).",
                LogLevel.Trace);
            Diagnostics.Write($"Mascota '{pet.displayName}': comportamiento '{current}' -> 'Sleep' ({origin}).");
        }

        /// <summary>
        /// Reconcilia en el anfitrión la mascota adoptada de un jugador. Las ubicaciones privadas se regeneran
        /// al cargar y sus entidades no siempre sobreviven, así que la mascota se reconstruye a partir del
        /// registro autoritativo (nombre, tipo, raza y GUID). También garantiza que haya una sola copia y
        /// que el cuenco apunte a ella. No la mueve: de la posición se ocupa la IA de vanilla.
        /// </summary>
        private static void EnsureAdoptedPetFor(PetAdoptionRecord? record)
        {
            if (context == null || record == null || record.State != PetAdoptionState.Adopted)
                return;
            if (string.IsNullOrWhiteSpace(record.PetName))
                return;

            Guid petGuid;
            if (Guid.TryParse(record.PetGuid, out Guid parsed))
            {
                petGuid = parsed;
            }
            else
            {
                // Partidas anteriores al GUID persistido: rescatar la mascota existente con el mismo nombre
                // antes de crear una copia nueva.
                Pet? existing = FindExistingPet(record);
                petGuid = existing?.petId.Value ?? Guid.NewGuid();
                record.PetGuid = petGuid.ToString();
            }

            List<GameLocation> candidates = GetCandidateLocations(record).ToList();
            if (candidates.Count == 0)
                return;

            // La mascota puede estar fuera de la residencia (vanilla la lleva a una casa los días de lluvia),
            // así que se busca en todo el mundo antes de dar por hecho que falta.
            List<(GameLocation Location, Pet Pet)> copies = FindPetCopies(petGuid);
            (GameLocation Location, Pet Pet)? kept = copies.Count == 0
                ? null
                : copies.OrderByDescending(c => candidates.Contains(c.Location)).First();

            foreach ((GameLocation location, Pet copy) in copies)
            {
                if (ReferenceEquals(copy, kept!.Value.Pet))
                    continue;

                location.characters.Remove(copy);
                Diagnostics.Write($"Copia duplicada de '{copy.displayName}' retirada de '{location.NameOrUniqueName}'.");
            }

            GameLocation bowlLocation = candidates.FirstOrDefault(c => FindPetBowl(c, record) != null) ?? candidates[0];
            Pet pet;
            if (kept == null)
            {
                PetBowl? bowl = FindPetBowl(bowlLocation, record);
                Point spot = bowl?.GetPetSpot() ?? new Point(Math.Max(0, record.BowlTileX), Math.Max(0, record.BowlTileY));

                pet = new Pet(
                    spot.X,
                    spot.Y,
                    string.IsNullOrWhiteSpace(record.PetBreed) ? "0" : record.PetBreed,
                    string.IsNullOrWhiteSpace(record.PetType) ? "Dog" : record.PetType);
                pet.petId.Value = petGuid;
                pet.Name = record.PetName;
                pet.displayName = record.PetName;
                pet.currentLocation = bowlLocation;
                bowlLocation.characters.Add(pet);

                context.Monitor.Log(
                    $"[ClientPetAdoption] Mascota '{record.PetName}' restaurada en '{bowlLocation.NameOrUniqueName}' (petId {petGuid}).",
                    LogLevel.Info);
                Diagnostics.Write($"Mascota '{record.PetName}' restaurada en '{bowlLocation.NameOrUniqueName}' (petId {petGuid}).");
            }
            else
            {
                pet = kept.Value.Pet;
            }

            EnsurePetBehavior(pet, "reconciliación");
            AssignPetToBowl(bowlLocation, pet, record);
        }

        /// <summary>Todas las copias de una mascota (por <c>petId</c>) en cualquier ubicación cargada.</summary>
        private static List<(GameLocation Location, Pet Pet)> FindPetCopies(Guid petGuid)
        {
            var copies = new List<(GameLocation, Pet)>();
            Utility.ForEachLocation(location =>
            {
                foreach (NPC character in location.characters)
                {
                    if (character is Pet pet && pet.petId.Value == petGuid && !ReferenceEquals(pet, activePetPreview))
                        copies.Add((location, pet));
                }

                return true;
            });

            return copies;
        }

        /// <summary>
        /// Retira de la residencia y anexos del jugador local cualquier mascota que no sea la suya.
        /// Cubre el caso de un cliente sin mascota al que le aparecía la del anfitrión.
        /// </summary>
        private static void RemoveForeignPetsFromOwnLocations()
        {
            // Quitar un personaje en el cliente se replica al anfitrión. Hasta recibir el registro
            // autoritativo no se sabe cuál es la mascota propia y se borraría también esa.
            if (context == null || Game1.player == null || isAdoptionPromptActive || !localRecordSynced)
                return;

            Guid ownGuid = Guid.Empty;
            bool adopted = localRecord.State == PetAdoptionState.Adopted;
            if (adopted && !Guid.TryParse(localRecord.PetGuid, out ownGuid))
                return;

            List<GameLocation> locations = GetOwnLocations().ToList();
            if (locations.Count == 0)
                return;

            bool ownPetSeen = false;
            foreach (GameLocation location in locations)
            {
                if (adopted && location.characters.OfType<Pet>().Any(p => p.petId.Value == ownGuid))
                    ownPetSeen = true;

                foreach (Pet pet in location.characters.OfType<Pet>()
                             .Where(p => p.petId.Value != ownGuid && !ReferenceEquals(p, activePetPreview))
                             .ToList())
                {
                    location.characters.Remove(pet);
                    Diagnostics.Write($"Mascota ajena '{pet.displayName}' retirada de '{location.NameOrUniqueName}' (cliente).");
                }
            }

            // Solo avisar si el jugador está dentro de una ubicación propia: fuera de ella los personajes
            // de esa ubicación aún no se han sincronizado y el aviso sería un falso positivo.
            bool insideOwnLocation = locations.Any(l => ReferenceEquals(l, Game1.currentLocation));
            if (adopted && insideOwnLocation && !ownPetSeen)
            {
                missingPetTicks++;
                if (missingPetTicks % 30 == 1)
                {
                    Diagnostics.Write(
                        $"La mascota '{localRecord.PetName}' (petId {localRecord.PetGuid}) no está en la copia local de " +
                        $"'{Game1.currentLocation?.NameOrUniqueName}' (¿sin sincronizar desde el host?).");
                }
            }
            else
            {
                missingPetTicks = 0;
            }
        }

        /// <summary>Muestras de 1 s seguidas, en comportamiento de paseo, tras las que se actúa sobre una mascota parada.</summary>
        private const int StuckSamplesBeforeAction = 3;

        /// <summary>
        /// Vigila en el anfitrión las mascotas que quieren pasear y no avanzan. Una mascota cuya caja de
        /// colisión ya está dentro de algo sólido choca en las cuatro direcciones y vanilla no la saca nunca
        /// de ahí; solo en ese caso se la lleva a la casilla libre más cercana a su cuenco. En cualquier otro
        /// caso no se toca la IA: se deja constancia de qué la bloquea para poder diagnosticarlo.
        /// </summary>
        private static void WatchStuckPets()
        {
            var seen = new HashSet<Guid>();
            foreach (Pet pet in Utility.getAllPets())
            {
                Guid id = pet.petId.Value;
                seen.Add(id);

                GameLocation? location = pet.currentLocation;
                PetBehavior? behavior = pet.GetCurrentPetBehavior();
                if (location == null || pet.isSleepingOnFarmerBed.Value || pet.isMoving())
                {
                    petStuckSamples.Remove(id);
                    continue;
                }

                // Sentada, lamiéndose o dormida no intenta moverse: la muestra no cuenta, pero tampoco reinicia.
                if (behavior == null || !behavior.WalkInDirection)
                    continue;

                int samples = petStuckSamples.TryGetValue(id, out int previous) ? previous + 1 : 1;
                petStuckSamples[id] = samples;
                if (samples % StuckSamplesBeforeAction != 0)
                    continue;

                bool embedded = IsCollidingAt(pet, location, pet.GetBoundingBox());

                // Una línea al detectarlo y después una por minuto de paseo bloqueado, no una por segundo.
                if (samples == StuckSamplesBeforeAction || samples % 60 == 0)
                    Diagnostics.Write(DescribeStuckPet(pet, location, embedded));

                if (!embedded)
                    continue;

                Point? free = FindFreeTileNearBowl(pet, location);
                if (free == null)
                {
                    Diagnostics.Write($"Mascota '{pet.displayName}' incrustada y sin casilla libre cercana en '{location.NameOrUniqueName}'.");
                    continue;
                }

                Game1.warpCharacter(pet, location, new Vector2(free.Value.X, free.Value.Y));
                petStuckSamples.Remove(id);
                context?.Monitor.Log(
                    $"[ClientPetAdoption] Mascota '{pet.displayName}' incrustada en un obstáculo; movida a ({free.Value.X},{free.Value.Y}) en '{location.NameOrUniqueName}'.",
                    LogLevel.Info);
                Diagnostics.Write($"Mascota '{pet.displayName}' movida a la casilla libre ({free.Value.X},{free.Value.Y}).");
            }

            foreach (Guid stale in petStuckSamples.Keys.Where(k => !seen.Contains(k)).ToList())
                petStuckSamples.Remove(stale);
        }

        /// <summary>
        /// Misma comprobación que usa <c>Character.MovePosition</c> para decidir si la mascota puede ocupar
        /// un rectángulo, sin los efectos secundarios (empujones, hierba) de una colisión real.
        /// </summary>
        private static bool IsCollidingAt(Pet pet, GameLocation location, Rectangle box)
        {
            return location.isCollidingPosition(
                box, Game1.viewport, isFarmer: false, damagesFarmer: 0, glider: false, character: pet,
                pathfinding: false, projectile: false, ignoreCharacterRequirement: false, skipCollisionEffects: true);
        }

        /// <summary>Primera casilla, por anillos alrededor del punto del cuenco, donde la mascota no colisiona.</summary>
        private static Point? FindFreeTileNearBowl(Pet pet, GameLocation location)
        {
            PetBowl? bowl = pet.GetPetBowl();
            Point origin = bowl != null && location.buildings.Contains(bowl) ? bowl.GetPetSpot() : pet.TilePoint;
            Rectangle box = pet.GetBoundingBox();
            int offsetX = box.X - (int)pet.Position.X;
            int offsetY = box.Y - (int)pet.Position.Y;

            for (int radius = 0; radius <= 6; radius++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius)
                            continue;

                        int x = origin.X + dx;
                        int y = origin.Y + dy;

                        // Fuera del mapa isCollidingPosition responde «libre», así que se descarta antes.
                        var candidate = new Rectangle(x * 64 + offsetX, y * 64 + offsetY, box.Width, box.Height);
                        if (!location.isTileOnMap(new Vector2(candidate.Left / 64, candidate.Top / 64))
                            || !location.isTileOnMap(new Vector2(candidate.Right / 64, candidate.Bottom / 64)))
                        {
                            continue;
                        }

                        if (!IsCollidingAt(pet, location, candidate))
                            return new Point(x, y);
                    }
                }
            }

            return null;
        }

        /// <summary>Qué impide avanzar a una mascota: colisión real por dirección y obstáculos que pisa.</summary>
        private static string DescribeStuckPet(Pet pet, GameLocation location, bool embedded)
        {
            Rectangle box = pet.GetBoundingBox();
            string[] names = { "N", "E", "S", "O" };
            var directions = new List<string>();
            for (int direction = 0; direction < 4; direction++)
            {
                bool blocked = IsCollidingAt(pet, location, pet.nextPosition(direction));
                directions.Add($"{names[direction]}:{(blocked ? "bloqueado" : "libre")}");
            }

            var blockers = new List<string>();
            foreach (Building building in location.buildings)
            {
                if (building.intersects(box))
                    blockers.Add($"edificio {building.buildingType.Value}@({building.tileX.Value},{building.tileY.Value})");
            }

            foreach (NPC other in location.characters)
            {
                if (!ReferenceEquals(other, pet) && other.GetBoundingBox().Intersects(box))
                    blockers.Add($"personaje {other.Name}");
            }

            foreach (Farmer farmer in location.farmers)
            {
                if (farmer.GetBoundingBox().Intersects(box))
                    blockers.Add($"jugador {farmer.Name}");
            }

            return
                $"Mascota parada '{pet.displayName}' en '{location.NameOrUniqueName}': comportamiento='{pet.CurrentBehavior}', " +
                $"tile=({pet.TilePoint.X},{pet.TilePoint.Y}), px=({(int)pet.Position.X},{(int)pet.Position.Y}), " +
                $"caja=({box.X},{box.Y},{box.Width}x{box.Height}), speed={pet.speed}, dir={pet.FacingDirection}, " +
                $"incrustada={embedded}, colisión[{string.Join(" ", directions)}], " +
                $"pisa[{(blockers.Count == 0 ? "nada" : string.Join("; ", blockers))}]";
        }

        /// <summary>Ubicaciones del jugador local: su residencia y sus anexos.</summary>
        private static IEnumerable<GameLocation> GetOwnLocations()
        {
            if (context == null || Game1.player == null)
                yield break;

            long me = Game1.player.UniqueMultiplayerID;
            string? residence = context.WorldState.ResolvePlayerResidenceSlot(me);
            if (string.IsNullOrWhiteSpace(residence))
                yield break;

            if (Game1.getLocationFromName(residence) is GameLocation main)
                yield return main;

            foreach (AnnexAssignment annex in context.WorldState.GetAnnexesForResidence(residence))
            {
                if (annex.OwnerId == me
                    && !string.IsNullOrWhiteSpace(annex.AnnexId)
                    && Game1.getLocationFromName(annex.AnnexId) is GameLocation annexLocation)
                {
                    yield return annexLocation;
                }
            }
        }

        /// <summary>Busca una mascota ya creada con el nombre registrado (para no duplicarla).</summary>
        private static Pet? FindExistingPet(PetAdoptionRecord record)
        {
            foreach (GameLocation location in GetCandidateLocations(record))
            {
                Pet? pet = location.characters.OfType<Pet>().FirstOrDefault(p =>
                    string.Equals(p.displayName, record.PetName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.Name, record.PetName, StringComparison.OrdinalIgnoreCase));
                if (pet != null)
                    return pet;
            }

            return null;
        }

        /// <summary>Ubicaciones donde puede estar la mascota del jugador: su residencia y sus anexos.</summary>
        private static IEnumerable<GameLocation> GetCandidateLocations(PetAdoptionRecord record)
        {
            if (string.IsNullOrWhiteSpace(record.FarmSlotId))
                yield break;

            if (Game1.getLocationFromName(record.FarmSlotId) is GameLocation residence)
                yield return residence;

            if (context?.WorldState == null)
                yield break;

            foreach (AnnexAssignment annex in context.WorldState.GetAnnexesForResidence(record.FarmSlotId))
            {
                if (!string.IsNullOrWhiteSpace(annex.AnnexId)
                    && Game1.getLocationFromName(annex.AnnexId) is GameLocation annexLocation)
                {
                    yield return annexLocation;
                }
            }
        }

        /// <summary>Cuenco asignado a la mascota: el de las coordenadas registradas o el primero libre.</summary>
        private static PetBowl? FindPetBowl(GameLocation location, PetAdoptionRecord record)
        {
            List<PetBowl> bowls = location.buildings.OfType<PetBowl>().ToList();
            if (bowls.Count == 0)
                return null;

            if (record.BowlTileX >= 0 && record.BowlTileY >= 0)
            {
                PetBowl? exact = bowls.FirstOrDefault(b =>
                    b.tileX.Value == record.BowlTileX && b.tileY.Value == record.BowlTileY);
                if (exact != null)
                    return exact;
            }

            return bowls.FirstOrDefault(b => !b.HasPet()) ?? bowls[0];
        }

        /// <summary>Vincula la mascota con su cuenco y libera los cuencos que la apuntaban.</summary>
        private static void AssignPetToBowl(GameLocation location, Pet pet, PetAdoptionRecord record)
        {
            List<PetBowl> bowls = location.buildings.OfType<PetBowl>().ToList();
            if (bowls.Count == 0)
                return;

            PetBowl? bowl = null;
            if (record.BowlTileX >= 0 && record.BowlTileY >= 0)
            {
                bowl = bowls.FirstOrDefault(b => b.tileX.Value == record.BowlTileX && b.tileY.Value == record.BowlTileY);
            }

            bowl ??= bowls.FirstOrDefault(b => !b.HasPet()) ?? bowls[0];

            foreach (PetBowl other in bowls.Where(b => b != bowl && b.petId.Value == pet.petId.Value))
                other.petId.Value = Guid.Empty;

            // No reescribir los NetFields cada segundo si ya está todo correcto (evita tráfico de red).
            if (bowl.petId.Value == pet.petId.Value
                && string.Equals(pet.homeLocationName.Value, bowl.parentLocationName.Value, StringComparison.Ordinal))
            {
                return;
            }

            bowl.AssignPet(pet);
        }

        /// <summary>Indica si el jugador tiene algún cuenco sin mascota en su residencia o anexos.</summary>
        private static bool HasFreePetBowl(string residenceSlotId)
        {
            if (context?.WorldState == null)
                return false;

            var record = new PetAdoptionRecord { FarmSlotId = residenceSlotId };
            foreach (GameLocation location in GetCandidateLocations(record))
            {
                if (location.buildings.OfType<PetBowl>().Any(bowl => !bowl.HasPet()))
                    return true;
            }

            return false;
        }

        /// <summary>Reconcilia las mascotas de todos los jugadores (host) o la del jugador local (cliente).</summary>
        private void ReconcilePets()
        {
            if (context == null || !Context.IsWorldReady)
                return;

            if (Context.IsMainPlayer)
            {
                bool changed = false;
                foreach (PlayerRecord player in context.WorldState.Players.Values)
                {
                    PetAdoptionRecord? record = player.PetAdoption;
                    if (record == null || record.State != PetAdoptionState.Adopted)
                        continue;

                    string? previousGuid = record.PetGuid;
                    EnsureAdoptedPetFor(record);
                    if (!string.Equals(previousGuid, record.PetGuid, StringComparison.Ordinal))
                        changed = true;
                }

                if (changed)
                    context.Helper.Data.WriteSaveData(ServerWorldState.SaveDataKey, context.WorldState);

                WatchStuckPets();
            }
            else
            {
                // El cliente NO simula la mascota: su movimiento lo calcula el anfitrión y llega por red.
                // Aquí solo se retiran las mascotas ajenas de la residencia y los anexos.
                RemoveForeignPetsFromOwnLocations();
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

            ReconcilePets();
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

            // Solo tiene sentido la visita si queda un cuenco libre: con todos los cuencos ocupados
            // (o con la mascota ya adoptada) no se vuelve a llamar a Marnie.
            if (!HasFreePetBowl(currentMap))
            {
                NoteAdoptionGate("no queda ningún cuenco libre en la residencia ni en los anexos");
                return;
            }

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
            Diagnostics.Write($"Visita de Marnie bloqueada: {gate}.");
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
            Diagnostics.Write($"Marnie se presenta en '{farm.NameOrUniqueName}' para {Game1.player.Name}.");

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

            var playerRecord = context.WorldState.GetOrCreatePlayer(playerId, string.Empty);

            // El anfitrión es la autoridad: solo se crea la mascota si este jugador tiene una adopción
            // pendiente y en la ubicación donde registró el cuenco. Un mensaje repetido o fuera de flujo
            // no debe generar otra mascota; se le devuelve el estado real.
            if (!PetAdoptionLogic.IsAwaitingAdoption(playerRecord.PetAdoption)
                || !string.Equals(playerRecord.PetAdoption.FarmSlotId, payload.SlotName, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(payload.PetName))
            {
                context.Monitor.Log(
                    $"[ClientPetAdoption] Adopción ignorada para {playerId} en '{payload.SlotName}' " +
                    $"(estado={playerRecord.PetAdoption.State}, cuenco en '{playerRecord.PetAdoption.FarmSlotId}').",
                    LogLevel.Warn);
                context.Helper.Multiplayer.SendMessage(
                    playerRecord.PetAdoption,
                    MsgPetAdoptionSync,
                    new[] { context.Manifest.UniqueID },
                    new[] { playerId }
                );
                return;
            }

            GameLocation? loc = Game1.getLocationFromName(payload.SlotName);
            if (loc == null)
            {
                context.Monitor.Log($"[ClientPetAdoption] No se encontró el mapa {payload.SlotName} para spawnear la mascota.", LogLevel.Warn);
                return;
            }

            // Identificador estable de la mascota: el cuenco y el registro la apuntan por este GUID.
            string petGuid = PetAdoptionLogic.EnsurePetGuid(playerRecord.PetAdoption);

            // Localizar el cuenco antes de crear la mascota: su casilla canónica es la de delante
            // (PetBowl.GetPetSpot), no la del edificio, que bloquea el movimiento.
            var petBowl = loc.buildings.OfType<PetBowl>().FirstOrDefault(b => b.tileX.Value == payload.BowlTileX && b.tileY.Value == payload.BowlTileY)
                          ?? loc.buildings.OfType<PetBowl>().FirstOrDefault(b => !b.HasPet())
                          ?? loc.buildings.OfType<PetBowl>().FirstOrDefault();
            Point petSpot = petBowl?.GetPetSpot() ?? new Point(payload.BowlTileX, payload.BowlTileY);

            // Instanciar mascota autoritativa
            Pet pet = new Pet(petSpot.X, petSpot.Y, payload.PetBreed, payload.PetType);
            pet.petId.Value = Guid.Parse(petGuid);
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
            PetAdoptionLogic.RecordAdoption(playerRecord.PetAdoption, payload.PetName, payload.PetType, payload.PetBreed, petGuid);
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
                    // Se responde siempre, aunque el jugador aún no tenga registro: el cliente necesita
                    // saber que su estado (vacío) es el autoritativo antes de tocar las mascotas que ve.
                    PetAdoptionRecord reply = context.WorldState.Players.TryGetValue(e.FromPlayerID, out var playerRecord)
                        ? playerRecord.PetAdoption ?? new PetAdoptionRecord()
                        : new PetAdoptionRecord();
                    context.Helper.Multiplayer.SendMessage(
                        reply,
                        MsgPetAdoptionSync,
                        new[] { context.Manifest.UniqueID },
                        new[] { e.FromPlayerID }
                    );
                }
            }
            else
            {
                if (e.Type == MsgPetAdoptionSync && e.ReadAs<PetAdoptionRecord>() is { } record)
                {
                    localRecord = record;
                    localRecordSynced = true;
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
