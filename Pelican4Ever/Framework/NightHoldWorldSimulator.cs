using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Extensions;
using StardewValley.Locations;
using StardewValley.Tools;
using Pelican4Ever.Core.World;
using Pelican4Ever.Modules;

namespace Pelican4Ever.Framework
{
    /// <summary>
    /// Simula el entorno del mundo (burbujas de pesca, bateo de minerales, monstruos nocturnos,
    /// fauna ambiental y mantenimiento de mazmorras) durante la Madrugada (NightHold, 01:50 a 06:00),
    /// manteniendo el reloj del motor congelado en 2550 para evitar desmayos.
    /// </summary>
    public static class NightHoldWorldSimulator
    {
        /// <summary>
        /// Registro de cuándo apareció cada burbuja de pesca por ubicación (tickIndex relativo a la noche en curso).
        /// </summary>
        private static readonly Dictionary<string, int> BubbleSpawnTicks = new(StringComparer.OrdinalIgnoreCase);

        private static readonly Action<GameLocation, bool>? TryToAddCrittersMethod =
            AccessTools.Method(typeof(GameLocation), "tryToAddCritters", new[] { typeof(bool) })
                ?.CreateDelegate<Action<GameLocation, bool>>();

        /// <summary>
        /// Reinicia el estado de seguimiento al comenzar un nuevo día (Rollover).
        /// </summary>
        public static void ResetForNewDay()
        {
            BubbleSpawnTicks.Clear();
        }

        /// <summary>
        /// Ejecuta un tick de simulación ambiental de 10 minutos reales durante NightHold.
        /// </summary>
        /// <param name="context">Contexto del mod con logger y configuración.</param>
        /// <param name="tickIndex">Índice del tick de madrugada transcurrido (0, 1, 2...).</param>
        public static void SimulateTick(ModContext context, int tickIndex)
        {
            if (!Context.IsMainPlayer || !Context.IsWorldReady)
                return;

            Random random = new((int)(Game1.uniqueIDForThisGame ^ (ulong)Game1.Date.TotalDays ^ (ulong)(tickIndex * 7919)));

            try
            {
                SimulateFishBubbles(random, tickIndex, context.Monitor);
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[NightHoldWorldSimulator] Error simulando burbujas de pesca: {ex}", LogLevel.Error);
            }

            try
            {
                SimulateOrePanning(random, context.Monitor);
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[NightHoldWorldSimulator] Error simulando bateo de minerales: {ex}", LogLevel.Error);
            }

            try
            {
                SimulateNightMonsters(random, context.Monitor);
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[NightHoldWorldSimulator] Error simulando monstruos nocturnos: {ex}", LogLevel.Error);
            }

            try
            {
                SimulateDungeons(tickIndex, context.Monitor);
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[NightHoldWorldSimulator] Error simulando minas y volcán: {ex}", LogLevel.Error);
            }

            try
            {
                SimulateCritters(context.Monitor);
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[NightHoldWorldSimulator] Error simulando criaturas ambientales: {ex}", LogLevel.Error);
            }

            try
            {
                SimulateBugLand(random, context.Monitor);
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[NightHoldWorldSimulator] Error simulando BugLand: {ex}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Genera y expira burbujas de pesca en todas las ubicaciones exteriores elegibles.
        /// </summary>
        private static void SimulateFishBubbles(Random random, int tickIndex, IMonitor monitor)
        {
            foreach (GameLocation location in Game1.locations)
            {
                if (location?.map?.RequireLayer("Back") == null || !location.IsOutdoors)
                    continue;

                // Validación de elegibilidad en granjas
                if (location is Farm farm)
                {
                    bool isEligible = (Game1.whichFarm == 1) || (FarmTypeModule.BehaviorOf(farm)?.RiverFishSplashes == true);
                    if (!isEligible)
                        continue;
                }

                string locKey = location.NameOrUniqueName;

                if (!location.fishSplashPoint.Value.Equals(Point.Zero))
                {
                    // Burbuja activa: comprobar expiración
                    if (!BubbleSpawnTicks.TryGetValue(locKey, out int spawnTick))
                    {
                        // Existía antes de entrar a NightHold: asignamos antigüedad de al menos 60 min
                        spawnTick = Math.Max(0, tickIndex - 6);
                        BubbleSpawnTicks[locKey] = spawnTick;
                    }

                    int elapsedMinutes = Math.Max(0, (tickIndex - spawnTick) * 10);
                    if (NightHoldWorldLogic.ShouldBubbleExpire(elapsedMinutes, random.NextDouble()))
                    {
                        location.fishSplashPoint.Value = Point.Zero;
                        BubbleSpawnTicks.Remove(locKey);
                        monitor.Log($"[NightHoldWorldSimulator] Burbuja de pesca expiró en {locKey} tras {elapsedMinutes} min.", LogLevel.Trace);
                    }
                }
                else
                {
                    // Sin burbuja: tirar probabilidad de spawn (50 %)
                    BubbleSpawnTicks.Remove(locKey);
                    if (NightHoldWorldLogic.ShouldSpawnBubble(random.NextDouble()))
                    {
                        Point? point = TryFindWaterPoint(location, random);
                        if (point.HasValue)
                        {
                            location.fishSplashPoint.Value = point.Value;
                            BubbleSpawnTicks[locKey] = tickIndex;
                            if (location.farmers.Any())
                            {
                                location.playSound("waterSlosh");
                            }
                            monitor.Log($"[NightHoldWorldSimulator] Nueva burbuja de pesca en {locKey} en {point.Value}.", LogLevel.Trace);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Busca una casilla de agua válida a distancia de caña (1 a 4 casillas de la orilla) con 2 intentos, réplica exacta de vanilla.
        /// </summary>
        public static Point? TryFindWaterPoint(GameLocation location, Random random)
        {
            var back = location.map?.RequireLayer("Back");
            if (back == null)
                return null;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                Point point = new(random.Next(0, back.LayerWidth), random.Next(0, back.LayerHeight));
                if (!location.isOpenWater(point.X, point.Y) || location.doesTileHaveProperty(point.X, point.Y, "NoFishing", "Back") != null)
                    continue;

                int distance = FishingRod.distanceToLand(point.X, point.Y, location);
                if (distance <= 1 || distance >= 5)
                    continue;

                return point;
            }

            return null;
        }

        /// <summary>
        /// Genera y expira puntos de bateo de cobre mediante la lógica nativa en ríos y lagos.
        /// </summary>
        private static void SimulateOrePanning(Random random, IMonitor monitor)
        {
            foreach (GameLocation location in Game1.locations)
            {
                if (location == null || !location.IsOutdoors || location is Beach || location.map?.RequireLayer("Back") == null)
                    continue;

                bool spawned = location.performOrePanTenMinuteUpdate(random);
                if (spawned && location.farmers.Any() && !location.Equals(Game1.currentLocation))
                {
                    location.playSound("slosh");
                }
            }
        }

        /// <summary>
        /// Genera monstruos nocturnos en granja principal (si tiene spawn activo) y en residencias de combate.
        /// </summary>
        private static void SimulateNightMonsters(Random random, IMonitor monitor)
        {
            if (Game1.farmEvent != null)
                return;

            double luck = Game1.player?.team?.AverageDailyLuck() ?? 0.0;

            // 1. Granja principal
            Farm mainFarm = Game1.getFarm();
            if (mainFarm != null && Game1.spawnMonstersAtNight)
            {
                if (NightHoldWorldLogic.ShouldSpawnNightMonster(luck, random.NextDouble()))
                {
                    if (NightHoldWorldLogic.IsFlyingMonster(random.NextDouble()))
                    {
                        if (mainFarm.farmers.Any() || mainFarm.Equals(Game1.currentLocation))
                            mainFarm.spawnFlyingMonstersOffScreen();
                    }
                    else
                    {
                        mainFarm.spawnGroundMonsterOffScreen();
                    }
                }
            }

            // 2. Residencias de combate (NightMonsters)
            foreach (GameLocation location in Game1.locations)
            {
                if (location is Farm farm && !farm.Equals(mainFarm))
                {
                    var behavior = FarmTypeModule.BehaviorOf(farm);
                    if (behavior != null && behavior.NightMonsters && !Game1.spawnMonstersAtNight)
                    {
                        if (NightHoldWorldLogic.ShouldSpawnNightMonster(luck, random.NextDouble()))
                        {
                            if (NightHoldWorldLogic.IsFlyingMonster(random.NextDouble()))
                            {
                                if (farm.farmers.Any() || farm.Equals(Game1.currentLocation))
                                    farm.spawnFlyingMonstersOffScreen();
                            }
                            else
                            {
                                farm.spawnGroundMonsterOffScreen();
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Mantiene limpias las minas inactivas y ejecuta ticks en niveles activos de minas y volcán.
        /// </summary>
        private static void SimulateDungeons(int tickIndex, IMonitor monitor)
        {
            MineShaft.UpdateMines10Minutes(2550);
            VolcanoDungeon.UpdateLevels10Minutes(2550);
        }

        /// <summary>
        /// Añade fauna decorativa nocturna (búhos, murciélagos, ranas) a la pantalla de jugadores activos.
        /// </summary>
        private static void SimulateCritters(IMonitor monitor)
        {
            if (TryToAddCrittersMethod == null)
                return;

            foreach (GameLocation location in Game1.locations)
            {
                if (location != null && location.IsOutdoors && location.farmers.Any())
                {
                    TryToAddCrittersMethod(location, false);
                }
            }
        }

        /// <summary>
        /// Genera moscas en BugLand periódicamente si hay jugadores presentes.
        /// </summary>
        private static void SimulateBugLand(Random random, IMonitor monitor)
        {
            GameLocation bugLand = Game1.getLocationFromName("BugLand");
            if (bugLand != null && bugLand.farmers.Any() && random.NextDouble() <= 0.2)
            {
                bugLand.characters.Add(new StardewValley.Monsters.Fly(bugLand.getRandomTile() * 64f, hard: true));
            }
        }
    }
}
