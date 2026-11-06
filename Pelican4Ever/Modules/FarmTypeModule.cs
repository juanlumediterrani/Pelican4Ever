using System;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Extensions;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Replica en cada residencia los comportamientos de su tipo de granja que el juego decide con el tipo global
    /// de la partida (<c>Game1.whichFarm</c>). Nivel 1: propiedades de mapa que el propio juego ya lee por granja.
    /// Nivel 2: parches para lo que solo mira el tipo global. Funciona mejor si la partida del servidor es Estándar:
    /// con otro tipo global, los comportamientos de ese tipo se siguen aplicando también a todas las residencias.
    /// </summary>
    public sealed class FarmTypeModule : IModule
    {
        private static ModContext? context;

        private static readonly System.Reflection.FieldInfo? MountainOreRectCache = AccessTools.Field(typeof(Farm), "_mountainForageRectangle");
        private static readonly System.Reflection.FieldInfo? ForestForageCache = AccessTools.Field(typeof(Farm), "_shouldSpawnForestFarmForage");
        private static readonly System.Reflection.FieldInfo? BeachForageCache = AccessTools.Field(typeof(Farm), "_shouldSpawnBeachFarmForage");
        private static readonly System.Reflection.FieldInfo? FishSplashPointTime = AccessTools.Field(typeof(GameLocation), "fishSplashPointTime");

        public string Name => "FarmType";

        public ModuleSide Side => ModuleSide.Host;

        private static bool patchesApplied;

        public void Register(ModContext ctx)
        {
            context = ctx;
            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        }

        private void OnSaveLoaded(object? sender, StardewModdingAPI.Events.SaveLoadedEventArgs e)
        {
            if (!Context.IsMainPlayer || patchesApplied)
                return;

            ApplyHarmonyPatches(context!.Harmony);
            patchesApplied = true;
        }

        /// <summary>Comportamientos de la residencia o anexo, o null si la ubicación no es ninguno. Un hueco libre cuenta como estándar.</summary>
        private static FarmTypeBehavior? BehaviorOf(GameLocation location)
        {
            if (context == null)
                return null;

            string name = location.NameOrUniqueName;
            if (name.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string? farmType = context.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == name)?.FarmType;
                return FarmTypeBehaviors.For(farmType);
            }

            if (name.StartsWith(LocationCullingLogic.AnnexPrefix, StringComparison.OrdinalIgnoreCase))
            {
                AnnexAssignment? annex = context.WorldState.FindAnnexById(name);
                return AnnexTemplateCatalog.GetTemplate(annex?.TemplateId).Behavior;
            }

            return null;
        }

        /// <summary>
        /// Nivel 1: añade al mapa de la residencia las propiedades con las que Farm activa minerales y forraje
        /// forestal sin mirar el tipo global. Hay que repetirlo cada vez que se carga el mapa.
        /// </summary>
        public static void ApplyMapProperties(Farm farm, string? farmType)
        {
            var properties = farm.Map?.Properties;
            if (properties == null)
                return;

            FarmTypeBehavior behavior = FarmTypeBehaviors.For(farmType);
            if (behavior.MountainOreArea is { } area)
                properties["SpawnMountainFarmOreRect"] = area.ToMapProperty();
            if (behavior.ForestForage)
                properties["SpawnForestFarmForage"] = "T";
            if (behavior.BeachForage)
                properties["SpawnBeachFarmForage"] = "T";

            // Farm cachea estas propiedades en el primer uso
            MountainOreRectCache?.SetValue(farm, null);
            ForestForageCache?.SetValue(farm, null);
            BeachForageCache?.SetValue(farm, null);
        }

        /// <summary>
        /// Nivel 1 para anexos (TASK-902 / TASK-903): aplica las propiedades recalculadas de la plantilla 40×32.
        /// </summary>
        public static void ApplyAnnexMapProperties(Farm annexFarm, string? templateId)
        {
            var properties = annexFarm.Map?.Properties;
            if (properties == null)
                return;

            AnnexTemplate template = AnnexTemplateCatalog.GetTemplate(templateId);
            FarmTypeBehavior behavior = template.Behavior;
            if (behavior.MountainOreArea is { } area)
                properties["SpawnMountainFarmOreRect"] = area.ToMapProperty();
            if (behavior.ForestForage)
                properties["SpawnForestFarmForage"] = "T";
            if (behavior.BeachForage)
                properties["SpawnBeachFarmForage"] = "T";
            if (!string.IsNullOrWhiteSpace(template.FishLocationOverride))
                properties["FarmFishLocationOverride"] = template.FishLocationOverride;

            MountainOreRectCache?.SetValue(annexFarm, null);
            ForestForageCache?.SetValue(annexFarm, null);
            BeachForageCache?.SetValue(annexFarm, null);
        }

        /// <summary>Simula los días de minerales con los que vanilla empieza una granja o anexo de Colinas o Cuatro Esquinas.</summary>
        public static void SpawnInitialOres(Farm farm, string? farmType)
        {
            bool isAnnex = farm.NameOrUniqueName.StartsWith(LocationCullingLogic.AnnexPrefix, StringComparison.OrdinalIgnoreCase);
            FarmTypeBehavior behavior = isAnnex
                ? AnnexTemplateCatalog.GetTemplate(farmType).Behavior
                : FarmTypeBehaviors.For(farmType);

            if (behavior.MountainOreArea == null || !farm.ShouldSpawnMountainOres())
                return;

            for (int i = 0; i < behavior.InitialMountainOreDays; i++)
                farm.doDailyMountainFarmUpdate();
        }

        /// <summary>
        /// Configura el gallinero de inicio, dos gallinas y vallas para granjas de Pradera (TASK-605),
        /// replicando lo que Farm.onNewGame hace para MeadowlandsFarm en vanilla.
        /// </summary>
        public static void SpawnInitialMeadowlandsSetup(Farm farm, string? farmType, long ownerId = 0)
        {
            if (!FarmGrassLogic.IsMeadowlands(farmType))
                return;

            // 1. Vallas iniciales (Fences)
            for (int k = 47; k < 63; k++)
            {
                farm.objects[new Vector2(k, 20f)] = new Fence(new Vector2(k, 20f), "322", isGate: false);
            }
            for (int l = 16; l < 20; l++)
            {
                farm.objects[new Vector2(47f, l)] = new Fence(new Vector2(47f, l), "322", isGate: false);
            }
            for (int m = 7; m < 20; m++)
            {
                farm.objects[new Vector2(62f, m)] = new Fence(new Vector2(62f, m), "322", m == 13);
            }

            // 2. Gallinero de inicio (Coop) con 2 gallinas
            if (!farm.buildings.Any(b => b.buildingType.Value == "Coop"))
            {
                Building building = new Building("Coop", new Vector2(54f, 9f));
                building.parentLocationName.Value = farm.NameOrUniqueName;
                farm.buildings.Add(building);
                building.FinishConstruction(onGameStart: true);
                building.LoadFromBuildingData(building.GetData(), forUpgrade: false, forConstruction: true);
                building.load();
                building.updateInteriorWarps();

                long animalOwner = ownerId != 0 ? ownerId : (farm.GetFarmOwnerId() ?? Game1.player.UniqueMultiplayerID);
                FarmAnimal farmAnimal = new FarmAnimal("White Chicken", Game1.Multiplayer.getNewID(), animalOwner);
                FarmAnimal farmAnimal2 = new FarmAnimal("Brown Chicken", Game1.Multiplayer.getNewID(), animalOwner);

                string[] names = new[] { "Cluck", "Peck" };
                try
                {
                    string raw = Game1.content.LoadString("Strings\\1_6_Strings:StarterChicken_Names");
                    if (!string.IsNullOrEmpty(raw))
                    {
                        string[] pairs = raw.Split('|');
                        string pair = pairs[Game1.random.Next(pairs.Length)];
                        names = pair.Split(',');
                    }
                }
                catch { }

                farmAnimal.Name = names[0].Trim();
                farmAnimal2.Name = (names.Length > 1 ? names[1] : "Chicky").Trim();

                if (building.GetIndoors() is AnimalHouse animalHouse)
                {
                    animalHouse.adoptAnimal(farmAnimal);
                    animalHouse.adoptAnimal(farmAnimal2);
                }
            }

        }

        /// <summary>
        /// Rellena el regalo de bienvenida de la casa de la residencia con la propiedad <c>FarmHouseStarterGift</c> de su
        /// propio mapa (p. ej. 15 de heno en Pradera); FarmHouse lo genera leyendo el mapa de la granja principal. En la
        /// fluvial añade el Ahumador de pescado que vanilla coloca en la casa: va en el regalo para no chocar con la
        /// disposición de muebles de la residencia.
        /// </summary>
        public static void ApplyStarterGift(Farm farm, string? farmType)
        {
            if (farm.GetMainFarmHouse()?.GetIndoors() is not GameLocation house)
                return;

            StardewValley.Objects.Chest? chest = house.objects.Values
                .OfType<StardewValley.Objects.Chest>()
                .FirstOrDefault(c => c.giftbox.Value && c.giftboxIsStarterGift.Value);
            if (chest == null)
                return;

            var items = new System.Collections.Generic.List<Item>();
            string[] gift = farm.GetMapPropertySplitBySpaces("FarmHouseStarterGift");
            for (int i = 0; i < gift.Length; i += 2)
            {
                if (ArgUtility.TryGet(gift, i, out string giftId, out _, allowBlank: false, "string giftId")
                    && ArgUtility.TryGetOptionalInt(gift, i + 1, out int count, out _, 0, "int count"))
                {
                    items.Add(ItemRegistry.Create(giftId, count));
                }
            }
            if (items.Count > 0)
            {
                chest.Items.Clear();
                foreach (Item item in items)
                    chest.Items.Add(item);
            }

            if (FarmTypeVanillaLogic.StartsWithFishSmoker(farmType) && !chest.Items.Any(i => i?.QualifiedItemId == "(BC)FishSmoker"))
                chest.Items.Add(ItemRegistry.Create("(BC)FishSmoker"));
        }

        /// <summary>
        /// Papel de pared y suelo de inicio de la casa según el tipo de la residencia. FarmHouse los toma del mapa de la
        /// granja principal o de <c>Game1.whichFarm</c>; aquí se usan el mapa y el tipo de la propia residencia.
        /// Los muebles no se tocan: la residencia los coloca con <c>CleanAndArrangeResidenceInterior</c>.
        /// </summary>
        public static void ApplyStarterHouseStyle(Farm farm, string? farmType)
        {
            if (farm.GetMainFarmHouse()?.GetIndoors() is not StardewValley.Locations.DecoratableLocation house)
                return;

            FarmHouseStarterStyle style = FarmTypeVanillaLogic.StarterStyleFor(
                farmType, farm.getMapProperty("FarmHouseWallpaper"), farm.getMapProperty("FarmHouseFlooring"));
            if (style.Wallpaper != null)
                house.SetWallpaper(style.Wallpaper, null);
            if (style.Flooring != null)
                house.SetFloor(style.Flooring, null);
        }

        private void ApplyHarmonyPatches(Harmony harmony)
        {
            try
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(Farm), nameof(Farm.spawnGroundMonsterOffScreen)),
                    prefix: new HarmonyMethod(typeof(FarmTypeModule), nameof(Prefix_SpawnGroundMonster))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(Farm), nameof(Farm.spawnFlyingMonstersOffScreen)),
                    prefix: new HarmonyMethod(typeof(FarmTypeModule), nameof(Prefix_SpawnMonsters))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(GameLocation), nameof(GameLocation.performTenMinuteUpdate)),
                    postfix: new HarmonyMethod(typeof(FarmTypeModule), nameof(Postfix_TenMinuteUpdate))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(Farm), nameof(Farm.doDailyMountainFarmUpdate)),
                    prefix: new HarmonyMethod(typeof(FarmTypeModule), nameof(Prefix_DailyMountainFarmUpdate))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(Farm), nameof(Farm.DayUpdate)),
                    postfix: new HarmonyMethod(typeof(FarmTypeModule), nameof(Postfix_FarmDayUpdate))
                );

                context?.Monitor.Log("[FarmTypeModule] Parches de comportamiento por tipo de residencia aplicados.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[FarmTypeModule] Error al aplicar parches Harmony: {ex}", LogLevel.Error);
            }
        }

        /// <summary>Con los monstruos nocturnos activados para el equipo, no aparecen en residencias que no sean de combate.</summary>
        public static bool Prefix_SpawnMonsters(Farm __instance)
        {
            if (!Game1.IsMasterGame || !Context.IsMainPlayer)
                return true;

            return BehaviorOf(__instance) is not { NightMonsters: false };
        }

        /// <summary>
        /// Monstruos de suelo en residencias: en las de combate se generan con el nivel de Combate del dueño
        /// (vanilla usa el del jugador local, que en el servidor es el anfitrión); en las demás no aparecen.
        /// </summary>
        public static bool Prefix_SpawnGroundMonster(Farm __instance)
        {
            if (!Game1.IsMasterGame || !Context.IsMainPlayer)
                return true;

            FarmTypeBehavior? behavior = BehaviorOf(__instance);
            if (behavior == null)
                return true;
            if (!behavior.NightMonsters)
                return false;

            SpawnGroundMonsterForOwner(__instance);
            return false;
        }

        /// <summary>Dueño de la residencia o anexo (conectado o no); si no tiene dueño, el anfitrión.</summary>
        private static Farmer OwnerOf(Farm farm)
        {
            long? ownerId = farm.GetFarmOwnerId();
            return (ownerId.HasValue ? Game1.GetPlayer(ownerId.Value) : null) ?? Game1.player;
        }

        /// <summary>
        /// Minerales diarios de la cantera de una residencia o anexo con el nivel de Minería del dueño. Réplica de
        /// Farm.doDailyMountainFarmUpdate, que usa el del jugador local (en el servidor, el anfitrión).
        /// </summary>
        public static bool Prefix_DailyMountainFarmUpdate(Farm __instance)
        {
            if (!Context.IsMainPlayer || BehaviorOf(__instance)?.MountainOreArea is not { } area)
                return true;

            int miningLevel = OwnerOf(__instance).MiningLevel;
            var rect = new Rectangle(area.X, area.Y, area.Width, area.Height);
            double chance = 1.0;
            while (Game1.random.NextDouble() < chance)
            {
                Vector2 tile = Utility.getRandomPositionInThisRectangle(rect, Game1.random);
                if (__instance.doesTileHavePropertyNoNull((int)tile.X, (int)tile.Y, "Type", "Back").Equals("Dirt")
                    && __instance.CanItemBePlacedHere(tile, itemIsPassable: false, CollisionMask.All, CollisionMask.None))
                {
                    if (Game1.random.NextDouble() < 0.15)
                    {
                        __instance.objects.Add(tile, ItemRegistry.Create<StardewValley.Object>("(O)590"));
                        continue;
                    }

                    (string itemId, int minutesUntilReady) = (Game1.random.NextBool() ? "670" : "668", 2);
                    if (Game1.random.NextDouble() < 0.1)
                    {
                        if (miningLevel >= 8 && Game1.random.NextDouble() < 0.33)
                            (itemId, minutesUntilReady) = ("77", 7);
                        else if (miningLevel >= 5 && Game1.random.NextBool())
                            (itemId, minutesUntilReady) = ("76", 5);
                        else
                            (itemId, minutesUntilReady) = ("75", 3);
                    }
                    if (Game1.random.NextDouble() < 0.21)
                        (itemId, minutesUntilReady) = ("751", 3);
                    if (miningLevel >= 4 && Game1.random.NextDouble() < 0.15)
                        (itemId, minutesUntilReady) = ("290", 4);
                    if (miningLevel >= 7 && Game1.random.NextDouble() < 0.1)
                        (itemId, minutesUntilReady) = ("764", 8);
                    if (miningLevel >= 10 && Game1.random.NextDouble() < 0.01)
                        (itemId, minutesUntilReady) = ("765", 16);

                    __instance.objects.Add(tile, new StardewValley.Object(itemId, 10) { MinutesUntilReady = minutesUntilReady });
                }
                chance *= 0.75;
            }
            return false;
        }

        /// <summary>
        /// Réplica de Farm.spawnGroundMonsterOffScreen para una granja salvaje usando el nivel del dueño:
        /// Bruto sombrío (nivel 8+), gólem salvaje (66%, de iridio al 50% con nivel 9+) o slime escalado.
        /// </summary>
        private static void SpawnGroundMonsterForOwner(Farm farm)
        {
            int combatLevel = OwnerOf(farm).CombatLevel;
            for (int i = 0; i < 15; i++)
            {
                Vector2 tile = farm.getRandomTile();
                if (Utility.isOnScreen(Utility.Vector2ToPoint(tile), 64, farm))
                    tile.X -= Game1.viewport.Width / 64;
                if (!farm.CanItemBePlacedHere(tile))
                    continue;

                StardewValley.Monsters.Monster monster;
                if (combatLevel >= 8 && Game1.random.NextDouble() < 0.15)
                {
                    monster = new StardewValley.Monsters.ShadowBrute(tile * 64f) { focusedOnFarmers = true };
                }
                else if (Game1.random.NextDouble() < 0.66)
                {
                    // RockGolem solo elige "Iridium Golem" si la partida es Salvaje (whichFarm 4)
                    int previousFarm = Game1.whichFarm;
                    Game1.whichFarm = 4;
                    try
                    {
                        monster = new StardewValley.Monsters.RockGolem(tile * 64f, combatLevel);
                    }
                    finally
                    {
                        Game1.whichFarm = previousFarm;
                    }
                }
                else
                {
                    int mineLevel = combatLevel >= 10 ? 140 : combatLevel >= 8 ? 100 : combatLevel >= 4 ? 41 : 1;
                    monster = new StardewValley.Monsters.GreenSlime(tile * 64f, mineLevel);
                }

                monster.wildernessFarmMonster = true;
                farm.characters.Add(monster);
                return;
            }
        }

        /// <summary>
        /// Aplica comportamientos cada 10 minutos (monstruos nocturnos y burbujas de peces)
        /// solo en ubicaciones de tipo Farm y solo en el servidor anfitrión.
        /// </summary>
        public static void Postfix_TenMinuteUpdate(GameLocation __instance, int timeOfDay)
        {
            if (!Game1.IsMasterGame || !Context.IsMainPlayer || __instance is not Farm farm)
                return;

            FarmTypeBehavior? behavior = BehaviorOf(farm);
            if (behavior == null)
                return;

            // 1. Monstruos nocturnos en residencias de combate aunque el equipo no los tenga activados
            if (!Game1.spawnMonstersAtNight && behavior.NightMonsters)
            {
                if (Game1.farmEvent == null && Game1.timeOfDay >= 1900 && Game1.random.NextDouble() < 0.25 - Game1.player.team.AverageDailyLuck() / 2.0)
                {
                    if (Game1.random.NextDouble() < 0.25)
                    {
                        if (farm.Equals(Game1.currentLocation))
                            farm.spawnFlyingMonstersOffScreen();
                    }
                    else
                    {
                        // Pasa por Prefix_SpawnGroundMonster, que usa el nivel del dueño
                        farm.spawnGroundMonsterOffScreen();
                    }
                }
            }

            // 2. Burbujas de peces en residencias fluviales (si la partida global no es fluvial)
            if (behavior.RiverFishSplashes && Game1.whichFarm != 1 && farm.IsOutdoors && farm.fishSplashPoint.Value.Equals(Point.Zero))
            {
                var back = farm.map?.RequireLayer("Back");
                if (back != null)
                {
                    Random random = Utility.CreateDaySaveRandom(timeOfDay, back.LayerWidth, 1);
                    if (random.NextBool())
                    {
                        for (int attempt = 0; attempt < 2; attempt++)
                        {
                            Point point = new(random.Next(0, back.LayerWidth), random.Next(0, back.LayerHeight));
                            if (!farm.isOpenWater(point.X, point.Y) || farm.doesTileHaveProperty(point.X, point.Y, "NoFishing", "Back") != null)
                                continue;

                            int distance = FishingRod.distanceToLand(point.X, point.Y, farm);
                            if (distance <= 1 || distance >= 5)
                                continue;

                            if (Game1.player.currentLocation?.Equals(farm) == true)
                                farm.playSound("waterSlosh");

                            FishSplashPointTime?.SetValue(farm, Game1.timeOfDay);
                            farm.fishSplashPoint.Value = point;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>Tocones forestales, hierba diaria y extras de Cuatro Esquinas, que Farm.DayUpdate solo hace si la partida es de ese tipo.</summary>
        public static void Postfix_FarmDayUpdate(Farm __instance)
        {
            if (!Game1.IsMasterGame || !Context.IsMainPlayer || BehaviorOf(__instance) is not { } behavior)
                return;

            // TASK-606: Siembra diaria de hierba en residencias
            SpawnDailyGrass(__instance);

            if (behavior.ForestStumps && Game1.whichFarm != 2)
            {
                int height = __instance.map.Layers[0].LayerHeight;
                for (int x = 0; x < 20; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        if (__instance.getTileIndexAt(x, y, "Paths") == 21 && CanPlaceClump(__instance, x, y))
                            __instance.resourceClumps.Add(new ResourceClump(600, 2, 2, new Vector2(x, y)));
                    }
                }
            }

            if (behavior.FourCornersExtras && Game1.whichFarm != 5)
            {
                bool isAnnex = __instance.NameOrUniqueName.StartsWith(LocationCullingLogic.AnnexPrefix, StringComparison.OrdinalIgnoreCase);
                int stumpX = isAnnex ? 6 : 5;
                int stumpY = isAnnex ? 6 : 32;
                float maxWeedX = isAnnex ? 20f : 36f;
                float maxWeedY = isAnnex ? 16f : 34f;

                if (CanPlaceClump(__instance, stumpX, stumpY))
                    __instance.resourceClumps.Add(new ResourceClump(600, 2, 2, new Vector2(stumpX, stumpY)));

                if (__instance.objects.Length > 0)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        if (Utility.TryGetRandom(__instance.objects, out _, out StardewValley.Object weeds)
                            && weeds.IsWeeds() && weeds.TileLocation.X < maxWeedX && weeds.TileLocation.Y < maxWeedY)
                        {
                            // Mismo cambio que hace Farm.DayUpdate; SetIdAndSprite está marcado obsoleto solo para código nuevo
#pragma warning disable CS0618
                            weeds.SetIdAndSprite(792 + Game1.seasonIndex);
#pragma warning restore CS0618
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Siembra diaria de hierba en residencias y anexos (TASK-606 / TASK-903). Vanilla solo añade hierba en spawnWeeds si name.Value == "Farm";
        /// aquí replicamos esa lógica para que cada hueco o anexo gane matas silvestres (y hierba azul en Pradera).
        /// </summary>
        public static void SpawnDailyGrass(Farm farm)
        {
            if (farm.GetSeason() == Season.Winter || farm.map == null)
                return;

            string? farmType = context?.WorldState.Doors.Values
                .FirstOrDefault(d => d.FarmSlotId == farm.NameOrUniqueName)?.FarmType
                ?? context?.WorldState.FindAnnexById(farm.NameOrUniqueName)?.TemplateId;
            bool isMeadowlands = FarmGrassLogic.IsMeadowlands(farmType);

            var data = farm.GetData();
            int minWeeds = data?.MinDailyWeeds ?? 1;
            int maxWeeds = data?.MaxDailyWeeds ?? 5;
            int firstDayMult = data?.FirstDayWeedMultiplier ?? 15;

            int attempts = FarmGrassLogic.CalculateDailyAttempts(
                minWeeds,
                maxWeeds,
                Game1.dayOfMonth,
                Game1.IsSpring,
                firstDayMult,
                Game1.random.NextDouble()
            );

            int mapWidth = farm.map.DisplayWidth / 64;
            int mapHeight = farm.map.DisplayHeight / 64;
            if (mapWidth <= 0 || mapHeight <= 0)
                return;

            for (int i = 0; i < attempts; i++)
            {
                for (int retry = 0; retry < 3; retry++)
                {
                    if (Game1.random.NextDouble() >= FarmGrassLogic.DailyGrassChance)
                        continue;

                    int x = Game1.random.Next(mapWidth);
                    int y = Game1.random.Next(mapHeight);
                    Vector2 tile = new Vector2(x, y);

                    bool hasDiggable = farm.doesTileHaveProperty(x, y, "Diggable", "Back") != null;
                    bool isTileOpen = farm.isTileLocationOpen(new xTile.Dimensions.Location(x, y));
                    bool isOccupied = farm.IsTileOccupiedBy(tile) || farm.objects.ContainsKey(tile) || farm.terrainFeatures.ContainsKey(tile);
                    bool isWater = farm.isWaterTile(x, y);
                    bool isNoSpawn = farm.IsNoSpawnTile(tile, "Grass");

                    if (FarmGrassLogic.CanSpawnGrassOnTile(false, hasDiggable, isTileOpen, isOccupied, isWater, isNoSpawn))
                    {
                        int grassType = FarmGrassLogic.DetermineGrassType(isMeadowlands, Game1.random.NextDouble());
                        int numberOfWeeds = Game1.random.Next(1, 3);
                        farm.terrainFeatures[tile] = new Grass(grassType, numberOfWeeds);
                        break;
                    }
                }
            }
        }

        private static bool CanPlaceClump(Farm farm, int x, int y)
        {
            return farm.CanItemBePlacedHere(new Vector2(x, y), itemIsPassable: false, CollisionMask.All, CollisionMask.None)
                && farm.CanItemBePlacedHere(new Vector2(x + 1, y), itemIsPassable: false, CollisionMask.All, CollisionMask.None)
                && farm.CanItemBePlacedHere(new Vector2(x + 1, y + 1), itemIsPassable: false, CollisionMask.All, CollisionMask.None)
                && farm.CanItemBePlacedHere(new Vector2(x, y + 1), itemIsPassable: false, CollisionMask.All, CollisionMask.None);
        }
    }
}
