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
        private static readonly System.Reflection.FieldInfo? FishSplashPointTime = AccessTools.Field(typeof(GameLocation), "fishSplashPointTime");

        public string Name => "FarmType";

        public ModuleSide Side => ModuleSide.Host;

        public void Register(ModContext ctx)
        {
            context = ctx;
            ApplyHarmonyPatches(ctx.Harmony);
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

            // Farm cachea ambas propiedades en el primer uso
            MountainOreRectCache?.SetValue(farm, null);
            ForestForageCache?.SetValue(farm, null);
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
            if (!string.IsNullOrWhiteSpace(template.FishLocationOverride))
                properties["FarmFishLocationOverride"] = template.FishLocationOverride;

            MountainOreRectCache?.SetValue(annexFarm, null);
            ForestForageCache?.SetValue(annexFarm, null);
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
                building.FinishConstruction(onGameStart: true);
                building.LoadFromBuildingData(building.GetData(), forUpgrade: false, forConstruction: true);
                building.load();

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

                farm.buildings.Add(building);
            }
        }

        private void ApplyHarmonyPatches(Harmony harmony)
        {
            try
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(Farm), nameof(Farm.spawnGroundMonsterOffScreen)),
                    prefix: new HarmonyMethod(typeof(FarmTypeModule), nameof(Prefix_SpawnMonsters))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(Farm), nameof(Farm.spawnFlyingMonstersOffScreen)),
                    prefix: new HarmonyMethod(typeof(FarmTypeModule), nameof(Prefix_SpawnMonsters))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(Farm), nameof(Farm.performTenMinuteUpdate)),
                    postfix: new HarmonyMethod(typeof(FarmTypeModule), nameof(Postfix_FarmTenMinuteUpdate))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(GameLocation), nameof(GameLocation.performTenMinuteUpdate)),
                    postfix: new HarmonyMethod(typeof(FarmTypeModule), nameof(Postfix_LocationTenMinuteUpdate))
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
            return BehaviorOf(__instance) is not { NightMonsters: false };
        }

        /// <summary>Monstruos nocturnos en residencias de combate aunque el equipo no los tenga activados (copia de Farm.performTenMinuteUpdate).</summary>
        public static void Postfix_FarmTenMinuteUpdate(Farm __instance)
        {
            if (!Game1.IsMasterGame || Game1.spawnMonstersAtNight || BehaviorOf(__instance) is not { NightMonsters: true })
                return;

            if (Game1.farmEvent != null || Game1.timeOfDay < 1900 || !(Game1.random.NextDouble() < 0.25 - Game1.player.team.AverageDailyLuck() / 2.0))
                return;

            if (Game1.random.NextDouble() < 0.25)
            {
                if (__instance.Equals(Game1.currentLocation))
                    __instance.spawnFlyingMonstersOffScreen();
            }
            else
            {
                __instance.spawnGroundMonsterOffScreen();
            }
        }

        /// <summary>Burbujas de peces en residencias fluviales; vanilla solo las permite en una granja si la partida es fluvial.</summary>
        public static void Postfix_LocationTenMinuteUpdate(GameLocation __instance, int timeOfDay)
        {
            if (!Game1.IsMasterGame || Game1.whichFarm == 1 || __instance is not Farm || !__instance.IsOutdoors
                || !__instance.fishSplashPoint.Value.Equals(Point.Zero) || BehaviorOf(__instance) is not { RiverFishSplashes: true })
                return;

            var back = __instance.map.RequireLayer("Back");
            Random random = Utility.CreateDaySaveRandom(timeOfDay, back.LayerWidth, 1);
            if (!random.NextBool())
                return;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                Point point = new(random.Next(0, back.LayerWidth), random.Next(0, back.LayerHeight));
                if (!__instance.isOpenWater(point.X, point.Y) || __instance.doesTileHaveProperty(point.X, point.Y, "NoFishing", "Back") != null)
                    continue;

                int distance = FishingRod.distanceToLand(point.X, point.Y, __instance);
                if (distance <= 1 || distance >= 5)
                    continue;

                if (Game1.player.currentLocation?.Equals(__instance) == true)
                    __instance.playSound("waterSlosh");

                FishSplashPointTime?.SetValue(__instance, Game1.timeOfDay);
                __instance.fishSplashPoint.Value = point;
                break;
            }
        }

        /// <summary>Tocones forestales, hierba diaria y extras de Cuatro Esquinas, que Farm.DayUpdate solo hace si la partida es de ese tipo.</summary>
        public static void Postfix_FarmDayUpdate(Farm __instance)
        {
            if (!Game1.IsMasterGame || BehaviorOf(__instance) is not { } behavior)
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
