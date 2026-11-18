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

        /// <summary>
        /// Caché diaria de casillas candidatas para burbujas de pesca y bateo de minerales por ubicación.
        /// Evita re-escanear mapas completos cada 10 minutos y garantiza selección uniforme sobre agua válida.
        /// </summary>
        private static readonly Dictionary<string, List<Point>> BubbleCandidateCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<Point>> OrePanCandidateCache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly System.Reflection.FieldInfo? FishSplashPointTimeField =
            AccessTools.Field(typeof(GameLocation), "fishSplashPointTime");

        private static readonly Action<GameLocation, bool>? TryToAddCrittersMethod =
            AccessTools.Method(typeof(GameLocation), "tryToAddCritters", new[] { typeof(bool) })
                ?.CreateDelegate<Action<GameLocation, bool>>();

        /// <summary>
        /// Reinicia el estado de seguimiento al comenzar un nuevo día (Rollover).
        /// </summary>
        public static void ResetForNewDay()
        {
            BubbleSpawnTicks.Clear();
            BubbleCandidateCache.Clear();
            OrePanCandidateCache.Clear();
        }

        /// <summary>
        /// Sincroniza <c>fishSplashPointTime</c> al salir de NightHold (06:00) para que las burbujas
        /// nocturnas activas no parezcan tener 370 minutos de antigüedad en el primer tick diurno.
        /// </summary>
        public static void SyncBubblesOnNightHoldExit()
        {
            if (!Context.IsMainPlayer || !Context.IsWorldReady || FishSplashPointTimeField == null)
                return;

            foreach (GameLocation location in Game1.locations)
            {
                if (location != null && location.IsOutdoors && !location.fishSplashPoint.Value.Equals(Point.Zero))
                {
                    FishSplashPointTimeField.SetValue(location, GameClockFallback(Game1.timeOfDay));
                }
            }

            static int GameClockFallback(int timeOfDay) => timeOfDay >= 600 && timeOfDay < 2550 ? timeOfDay : 600;
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
                if (location == null || !location.IsOutdoors || location.map?.GetLayer("Back") == null)
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
                        location.fishFrenzyFish.Value = "";
                        FishSplashPointTimeField?.SetValue(location, 0);
                        BubbleSpawnTicks.Remove(locKey);
                        monitor.Log($"[NightHoldWorldSimulator] Burbuja de pesca expiró en {locKey} tras {elapsedMinutes} min.", LogLevel.Trace);
                    }
                }
                else
                {
                    // Sin burbuja: tirar probabilidad calibrada a tiempo real 1:1 y elegir casilla de agua válida
                    BubbleSpawnTicks.Remove(locKey);
                    if (NightHoldWorldLogic.ShouldSpawnRealTimeBubble(random.NextDouble()))
                    {
                        Point? point = TryFindWaterPoint(location, random);
                        if (point.HasValue)
                        {
                            location.fishSplashPoint.Value = point.Value;
                            FishSplashPointTimeField?.SetValue(location, 600);
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
        /// Selecciona una casilla de agua válida a distancia de caña (2 a 4 casillas de la orilla)
        /// muestreando sobre las casillas elegibles del mapa para que el ritmo 1:1 real sea efectivo.
        /// </summary>
        public static Point? TryFindWaterPoint(GameLocation location, Random random)
        {
            var candidates = GetOrBuildCandidates(location, BubbleCandidateCache, static (loc, x, y) =>
                loc.isWaterTile(x, y)
                && loc.doesTileHaveProperty(x, y, "NoFishing", "Back") == null
                && FishingRod.distanceToLand(x, y, loc) is > 1 and < 5);

            return PickOpenCandidate(location, candidates, random);
        }

        private static Point? TryFindOrePanPoint(GameLocation location, Random random)
        {
            var candidates = GetOrBuildCandidates(location, OrePanCandidateCache, static (loc, x, y) =>
                loc.isWaterTile(x, y)
                && !loc.hasTileAt(new Point(x, y), "Buildings")
                && FishingRod.distanceToLand(x, y, loc, landMustBeAdjacentToWalkableTile: true) <= 1);

            return PickOpenCandidate(location, candidates, random);
        }

        private static List<Point> GetOrBuildCandidates(
            GameLocation location,
            Dictionary<string, List<Point>> cache,
            Func<GameLocation, int, int, bool> predicate)
        {
            string key = location.NameOrUniqueName;
            if (cache.TryGetValue(key, out List<Point>? existing))
                return existing;

            var list = new List<Point>();
            var back = location.map?.GetLayer("Back");
            if (back != null)
            {
                for (int x = 0; x < back.LayerWidth; x++)
                {
                    for (int y = 0; y < back.LayerHeight; y++)
                    {
                        if (predicate(location, x, y))
                            list.Add(new Point(x, y));
                    }
                }
            }

            cache[key] = list;
            return list;
        }

        private static Point? PickOpenCandidate(GameLocation location, List<Point> candidates, Random random)
        {
            if (candidates.Count == 0)
                return null;

            int directAttempts = Math.Min(16, candidates.Count);
            for (int i = 0; i < directAttempts; i++)
            {
                Point p = candidates[random.Next(candidates.Count)];
                if (location.isOpenWater(p.X, p.Y))
                    return p;
            }

            // Fallback determinista por si hay nasas (crab pots) en varios puntos
            int start = random.Next(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                Point p = candidates[(start + i) % candidates.Count];
                if (location.isOpenWater(p.X, p.Y))
                    return p;
            }

            return null;
        }

        /// <summary>
        /// Genera y expira puntos de bateo de cobre en ríos y lagos a ritmo real 1:1.
        /// </summary>
        private static void SimulateOrePanning(Random random, IMonitor monitor)
        {
            bool unlocked = Game1.MasterPlayer?.mailReceived?.Contains("ccFishTank") == true;

            foreach (GameLocation location in Game1.locations)
            {
                if (location == null || !location.IsOutdoors || location is Beach || location.map?.GetLayer("Back") == null)
                    continue;

                if (!location.orePanPoint.Value.Equals(Point.Zero))
                {
                    if (NightHoldWorldLogic.ShouldOrePanExpire(random.NextDouble()))
                    {
                        location.orePanPoint.Value = Point.Zero;
                    }
                    continue;
                }

                if (!unlocked || !NightHoldWorldLogic.ShouldSpawnRealTimeOrePan(random.NextDouble()))
                    continue;

                Point? point = TryFindOrePanPoint(location, random);
                if (point.HasValue)
                {
                    location.orePanPoint.Value = point.Value;
                    if (location.farmers.Any())
                    {
                        location.playSound("slosh");
                    }
                    monitor.Log($"[NightHoldWorldSimulator] Nuevo punto de bateo en {location.NameOrUniqueName} en {point.Value}.", LogLevel.Trace);
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
                TrySpawnFarmMonster(mainFarm, luck, random);
            }

            // 2. Residencias de combate (NightMonsters), independientemente del ajuste global de la granja principal
            foreach (GameLocation location in Game1.locations)
            {
                if (location is Farm farm && !farm.Equals(mainFarm))
                {
                    var behavior = FarmTypeModule.BehaviorOf(farm);
                    if (behavior != null && behavior.NightMonsters)
                    {
                        TrySpawnFarmMonster(farm, luck, random);
                    }
                }
            }
        }

        private static void TrySpawnFarmMonster(Farm farm, double luck, Random random)
        {
            if (!farm.farmers.Any() && !farm.Equals(Game1.currentLocation))
                return;

            if (!NightHoldWorldLogic.ShouldSpawnNightMonster(luck, random.NextDouble()))
                return;

            if (NightHoldWorldLogic.IsFlyingMonster(random.NextDouble()))
            {
                farm.spawnFlyingMonstersOffScreen();
            }
            else
            {
                farm.spawnGroundMonsterOffScreen();
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
        /// Añade fauna decorativa nocturna (búhos, murciélagos, ranas) si hay jugadores en el host.
        /// Los clientes remotos invocan <see cref="TryAddClientCritters"/> porque <c>critters</c> es local.
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
        /// Invocado localmente en clientes durante NightHold para añadir fauna ambiental en pantalla.
        /// </summary>
        public static void TryAddClientCritters(GameLocation? location)
        {
            if (TryToAddCrittersMethod != null && location != null && location.IsOutdoors)
            {
                TryToAddCrittersMethod(location, true);
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
