using System;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewValley;
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

        /// <summary>Comportamientos de la residencia, o null si la ubicación no es una residencia. Un hueco libre cuenta como estándar.</summary>
        private static FarmTypeBehavior? BehaviorOf(GameLocation location)
        {
            if (context == null)
                return null;

            string name = location.NameOrUniqueName;
            if (!name.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase))
                return null;

            string? farmType = context.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == name)?.FarmType;
            return FarmTypeBehaviors.For(farmType);
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

        /// <summary>Simula los días de minerales con los que vanilla empieza una granja de Colinas o Cuatro Esquinas.</summary>
        public static void SpawnInitialOres(Farm farm, string? farmType)
        {
            FarmTypeBehavior behavior = FarmTypeBehaviors.For(farmType);
            if (behavior.MountainOreArea == null || !farm.ShouldSpawnMountainOres())
                return;

            for (int i = 0; i < behavior.InitialMountainOreDays; i++)
                farm.doDailyMountainFarmUpdate();
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

        /// <summary>Tocones forestales y extras de Cuatro Esquinas, que Farm.DayUpdate solo hace si la partida es de ese tipo.</summary>
        public static void Postfix_FarmDayUpdate(Farm __instance)
        {
            if (!Game1.IsMasterGame || BehaviorOf(__instance) is not { } behavior)
                return;

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
                if (CanPlaceClump(__instance, 5, 32))
                    __instance.resourceClumps.Add(new ResourceClump(600, 2, 2, new Vector2(5f, 32f)));

                if (__instance.objects.Length > 0)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        if (Utility.TryGetRandom(__instance.objects, out _, out StardewValley.Object weeds)
                            && weeds.IsWeeds() && weeds.TileLocation.X < 36f && weeds.TileLocation.Y < 34f)
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

        private static bool CanPlaceClump(Farm farm, int x, int y)
        {
            return farm.CanItemBePlacedHere(new Vector2(x, y), itemIsPassable: false, CollisionMask.All, CollisionMask.None)
                && farm.CanItemBePlacedHere(new Vector2(x + 1, y), itemIsPassable: false, CollisionMask.All, CollisionMask.None)
                && farm.CanItemBePlacedHere(new Vector2(x + 1, y + 1), itemIsPassable: false, CollisionMask.All, CollisionMask.None)
                && farm.CanItemBePlacedHere(new Vector2(x, y + 1), itemIsPassable: false, CollisionMask.All, CollisionMask.None);
        }
    }
}
