using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Pathfinding;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Gestiona el micro-deambular (Micro-Wandering) de los NPCs para dar vida continua al pueblo
    /// y la reprogramación/adaptación horaria de festivales a prime-time (TASK-401..403).
    /// </summary>
    public sealed class WorldLifeModule : IModule
    {
        private static ModContext? context;

        // Anclas de destino de los aldeanos: key = Nombre del NPC, value = (Tile base, timestamp del próximo paso)
        private static readonly Dictionary<string, (Vector2 Anchor, DateTime NextStep)> NpcAnchors = new();
        private static readonly Random Rnd = new();

        // Control de rayos en tormentas (1 a 5 al día, GAP-16)
        private static int dailyLightningStrikes = 0;
        private static int maxDailyLightningStrikes = 3;

        // Seguimiento de oleadas de forrajeo (GAP-13)
        private static int lastForageWaveHour = -1;

        public string Name => "WorldLife";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            // Eventos SMAPI
            ctx.Helper.Events.Content.AssetRequested += OnAssetRequested;

            if (Context.IsMainPlayer)
            {
                ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
                ctx.Helper.Events.GameLoop.DayStarted += OnDayStarted;

                // Comandos de administración
                ctx.Helper.ConsoleCommands.Add("p4e_festival_move", "Reprograma un festival: p4e_festival_move <season> <oldDay> <newDay>", OnFestivalMoveCommand);
                ctx.Helper.ConsoleCommands.Add("p4e_forage_wave", "Fuerza una oleada de forrajeo en exteriores: p4e_forage_wave", OnForageWaveCommand);
            }

            ApplyHarmonyPatches(ctx.Harmony);
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            NpcAnchors.Clear();
            dailyLightningStrikes = 0;
            maxDailyLightningStrikes = Game1.random.Next(1, 6); // 1 a 5 rayos en un día de tormenta (GAP-16)
            lastForageWaveHour = -1;
        }

        private void ApplyHarmonyPatches(Harmony harmony)
        {
            try
            {
                // 1. Parche en Utility.performLightningUpdate para limitar los rayos a 1-5 por día de tormenta (GAP-16)
                harmony.Patch(
                    original: AccessTools.Method(typeof(Utility), nameof(Utility.performLightningUpdate), new[] { typeof(int) }),
                    prefix: new HarmonyMethod(typeof(WorldLifeModule), nameof(Prefix_PerformLightningUpdate))
                );

                // 2. Parches en Forest para ampliar el horario del Carro Ambulante los viernes y domingos (GAP-04)
                var cartTranspiler = new HarmonyMethod(typeof(WorldLifeModule), nameof(Transpiler_ExtendCartHours));
                harmony.Patch(
                    original: AccessTools.Method(typeof(Forest), nameof(Forest.checkAction)),
                    transpiler: cartTranspiler
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(Forest), nameof(Forest.draw), new[] { typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) }),
                    transpiler: cartTranspiler
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(Forest), nameof(Forest.UpdateWhenCurrentLocation), new[] { typeof(GameTime) }),
                    transpiler: cartTranspiler
                );

                // 3. Parche en NPC.updateConstructionAnimation para evitar que Robin abandone su tienda durante obras (GAP-10)
                var updateConstructionMethod = AccessTools.Method(typeof(NPC), "updateConstructionAnimation");
                if (updateConstructionMethod != null)
                {
                    harmony.Patch(
                        original: updateConstructionMethod,
                        prefix: new HarmonyMethod(typeof(WorldLifeModule), nameof(Prefix_UpdateConstructionAnimation))
                    );
                }

                // 4. Parche en Game1.IsThereABuildingUnderConstruction para que Robin atienda su mostrador sin bloqueos (GAP-10)
                harmony.Patch(
                    original: AccessTools.Method(typeof(Game1), nameof(Game1.IsThereABuildingUnderConstruction), new[] { typeof(string) }),
                    prefix: new HarmonyMethod(typeof(WorldLifeModule), nameof(Prefix_IsThereABuildingUnderConstruction))
                );

                // 5. Parche en NPC.checkSchedule para cancelar cualquier micro-deambular casual
                // y permitir que el NPC inicie su horario programado sin bloqueos ni retrasos.
                harmony.Patch(
                    original: AccessTools.Method(typeof(NPC), nameof(NPC.checkSchedule), new[] { typeof(int) }),
                    prefix: new HarmonyMethod(typeof(WorldLifeModule), nameof(Prefix_CheckSchedule))
                );

                context?.Monitor.Log("[WorldLifeModule] Parches Harmony de mundo (tormentas, carro ambulante, carpintería y rutas) aplicados.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[WorldLifeModule] Error aplicando parches Harmony: {ex}", LogLevel.Error);
            }
        }

        public static IEnumerable<CodeInstruction> Transpiler_ExtendCartHours(IEnumerable<CodeInstruction> instructions)
        {
            int closingTime = context?.Config.TravelingCartClosingTime ?? 2600;
            var timeOfDayField = AccessTools.Field(typeof(Game1), nameof(Game1.timeOfDay));

            CodeInstruction? prev = null;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_I4 && (int)instruction.operand == 2000 &&
                    prev != null && prev.LoadsField(timeOfDayField))
                {
                    instruction.operand = closingTime;
                }
                yield return instruction;
                prev = instruction;
            }
        }

        public static bool Prefix_UpdateConstructionAnimation(NPC __instance)
        {
            if (context?.Config.KeepRobinInCarpentryShop != true)
                return true;

            if (__instance.Name == "Robin")
            {
                // Cancelar el traslado de Robin a las obras para que permanezca en su carpintería
                // y atienda a los jugadores en su horario habitual (GAP-10).
                __instance.shouldPlayRobinHammerAnimation.Value = false;
                return false;
            }
            return true;
        }

        public static bool Prefix_IsThereABuildingUnderConstruction(string builder, ref bool __result)
        {
            if (context?.Config.KeepRobinInCarpentryShop == true
                && ResidenceUpgradeLogic.ShouldKeepRobinAvailable(true, builder))
            {
                __result = false;
                return false;
            }
            return true;
        }

        public static void Prefix_CheckSchedule(NPC __instance, int timeOfDay)
        {
            // Si el NPC tiene un controlador casual de micro-deambular (no una ruta oficial de horario),
            // cancelarlo de inmediato para que checkSchedule no posponga su ruta al mostrador / destino
            if (__instance.controller != null && !__instance.controller.NPCSchedule)
            {
                __instance.controller = null;
                __instance.Halt();
                __instance.speed = 2;
            }
        }

        public static bool Prefix_PerformLightningUpdate(int time_of_day)
        {
            // Si ya se alcanzó el tope de 1 a 5 rayos para hoy, bloquear impactos adicionales
            if (dailyLightningStrikes >= maxDailyLightningStrikes)
            {
                return false;
            }

            // Distribuir los 1 a 5 rayos a lo largo de los 108 ticks de 10 min de la jornada (probabilidad ~3.5% por tick)
            if (Game1.random.NextDouble() >= 0.035)
            {
                return false;
            }

            dailyLightningStrikes++;
            context?.Monitor.Log($"[WorldLifeModule] Rayo de tormenta permitido ({dailyLightningStrikes}/{maxDailyLightningStrikes} hoy).", LogLevel.Trace);
            return true;
        }

        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            // 1. Modificar fechas de festivales si fueron reprogramadas en ServerWorldState
            if (e.NameWithoutLocale.IsEquivalentTo("Data/Festivals/FestivalDates"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, string>().Data;
                    if (context != null)
                    {
                        foreach (var kvp in context.WorldState.RescheduledFestivals)
                        {
                            string oldKey = kvp.Key;   // ej: "spring13"
                            string newKey = kvp.Value; // ej: "spring16"
                            if (data.TryGetValue(oldKey, out string? festivalName))
                            {
                                data.Remove(oldKey);
                                data[newKey] = festivalName;
                            }
                        }
                    }
                });
            }

            // 2. Adaptar la ventana horaria de los festivales a prime-time de tarde/noche (18:00 - 23:50)
            if (e.NameWithoutLocale.StartsWith("Data/Festivals/"))
            {
                e.Edit(asset =>
                {
                    var dict = asset.AsDictionary<string, string>().Data;
                    if (dict.TryGetValue("conditions", out string? conditions))
                    {
                        string[] parts = conditions.Split('/');
                        if (parts.Length >= 2)
                        {
                            string[] timeParts = parts[1].Split(' ');
                            if (timeParts.Length >= 2)
                            {
                                int winStart = context?.Config.FestivalWindowStart ?? 1800;
                                int winEnd = context?.Config.FestivalWindowEnd ?? 2350;

                                parts[1] = $"{winStart} {winEnd}";
                                dict["conditions"] = string.Join("/", parts);
                            }
                        }
                    }
                });
            }
        }

        private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || !Context.IsMainPlayer || context == null)
                return;

            DateTime now = DateTime.UtcNow;

            // Iterar por todos los NPCs del mundo para aplicar Micro-Wandering
            Utility.ForEachCharacter(character =>
            {
                if (character is NPC npc && !npc.IsMonster && !npc.IsInvisible)
                {
                    string locName = npc.currentLocation?.NameOrUniqueName ?? "";
                    bool isOutdoors = npc.currentLocation?.IsOutdoors ?? false;
                    bool isDoingRouteAnim = npc.doingEndOfRouteAnimation.Value 
                        || npc.goingToDoEndOfRouteAnimation.Value 
                        || !string.IsNullOrEmpty(npc.endOfRouteBehaviorName.Value);
                    bool isSleeping = npc.isSleeping.Value;
                    bool isSitting = !string.IsNullOrEmpty(npc.endOfRouteBehaviorName.Value) && npc.endOfRouteBehaviorName.Value.Contains("sit");
                    bool hasActivity = npc.currentScheduleDelay > 0 
                        || npc.shouldPlayRobinHammerAnimation.Value 
                        || npc.shouldPlaySpousePatioAnimation.Value 
                        || npc.Sprite?.CurrentAnimation != null;
                    bool isEmoting = npc.isEmoting;

                    if (!NpcShopkeeperLogic.CanMicroWander(
                        npc.Name,
                        locName,
                        Game1.timeOfDay,
                        isOutdoors,
                        isDoingRouteAnim,
                        isSleeping,
                        isSitting,
                        hasActivity,
                        isEmoting))
                    {
                        // Si es un tendero de servicio o el NPC no es apto para deambular,
                        // retirar ancla y restaurar velocidad normal si estaba alterada
                        NpcAnchors.Remove(npc.Name);
                        if (npc.speed == 1 && (npc.controller == null || npc.controller.NPCSchedule))
                        {
                            npc.speed = 2;
                        }
                        return true;
                    }

                    // Solo si no está en medio de un diálogo, evento o controlador activo de horario
                    if (npc.controller == null && !npc.isMoving() && npc.currentLocation != null)
                    {
                        string name = npc.Name;

                        if (!NpcAnchors.TryGetValue(name, out var anchorData))
                        {
                            // Fijar ancla en la posición actual
                            NpcAnchors[name] = (npc.Tile, now.AddSeconds(Rnd.Next(30, 60)));
                        }
                        else
                        {
                            // Si llegó el momento del siguiente micro-paso
                            if (now >= anchorData.NextStep)
                            {
                                Vector2 baseTile = anchorData.Anchor;

                                // Elegir una casilla libre dentro de un radio de +/- 2 casillas
                                int offsetX = Rnd.Next(-2, 3);
                                int offsetY = Rnd.Next(-2, 3);
                                Point targetPoint = new Point((int)baseTile.X + offsetX, (int)baseTile.Y + offsetY);
                                Vector2 targetVec = new Vector2(targetPoint.X, targetPoint.Y);

                                GameLocation loc = npc.currentLocation;

                                // Validar que la casilla sea transitable y no contenga un Warp
                                if (loc.isTilePassable(targetVec) && !loc.isObjectAtTile(targetPoint.X, targetPoint.Y) && loc.isCollidingWithWarpOrDoor(new Rectangle(targetPoint.X * 64, targetPoint.Y * 64, 64, 64), npc) == null)
                                {
                                    // Asignar controlador de camino corto con paso de paseo
                                    // y restaurar velocidad estándar (2) al completar el paseo
                                    npc.speed = 1;
                                    npc.controller = new PathFindController(npc, loc, targetPoint, Rnd.Next(0, 4), (c, l) =>
                                    {
                                        c.speed = 2;
                                    });
                                }

                                // Programar el próximo paso entre 45 y 90 segundos
                                NpcAnchors[name] = (baseTile, now.AddSeconds(Rnd.Next(45, 90)));
                            }
                        }
                    }
                    else if (npc.controller != null && npc.isMoving())
                    {
                        // Si está siguiendo un horario programado por el juego, retirar el ancla para que se reubique al llegar
                        NpcAnchors.Remove(npc.Name);
                    }
                }
                return true;
            });

            // Oleadas programadas de forrajeo en horario local (14:00 y 20:00) (GAP-13)
            DateTime localNow = Core.Time.ServerTime.LocalNow(context.Config.TimeZoneId);
            int currentHour = localNow.Hour;
            if ((currentHour == 14 || currentHour == 20) && localNow.Minute == 0 && lastForageWaveHour != currentHour)
            {
                lastForageWaveHour = currentHour;
                TriggerForageWave();
            }
            else if (currentHour != 14 && currentHour != 20)
            {
                lastForageWaveHour = -1;
            }
        }

        /// <summary>
        /// Genera una nueva oleada de forrajeo y artefactos en mapas exteriores para jugadores de tarde y noche (GAP-13).
        /// </summary>
        public static int TriggerForageWave()
        {
            if (context == null || !Context.IsMainPlayer)
                return 0;

            int locationsUpdated = 0;
            foreach (GameLocation location in Game1.locations)
            {
                if (location.IsOutdoors && location is not Farm && !location.NameOrUniqueName.StartsWith("Pelican4Ever_Farm"))
                {
                    location.spawnObjects();
                    locationsUpdated++;
                }
            }

            context.Monitor.Log($"[WorldLifeModule] Oleada de forrajeo y artefactos generada en {locationsUpdated} mapas exteriores.", LogLevel.Info);
            return locationsUpdated;
        }

        private void OnForageWaveCommand(string command, string[] args)
        {
            int count = TriggerForageWave();
            context?.Monitor.Log($"[WorldLifeModule] Oleada manual de forrajeo ejecutada en {count} mapas.", LogLevel.Info);
        }

        private void OnFestivalMoveCommand(string command, string[] args)
        {
            if (context == null) return;

            if (args.Length < 3)
            {
                context.Monitor.Log("Uso: p4e_festival_move <season> <oldDay> <newDay> (ej: p4e_festival_move spring 13 16)", LogLevel.Info);
                return;
            }

            string season = args[0].ToLowerInvariant();
            string oldKey = $"{season}{args[1]}";
            string newKey = $"{season}{args[2]}";

            context.WorldState.RescheduledFestivals[oldKey] = newKey;
            context.Helper.GameContent.InvalidateCache("Data/Festivals/FestivalDates");

            context.Monitor.Log($"[WorldLifeModule] Festival reprogramado: {oldKey} -> {newKey}. Caché invalidada con éxito.", LogLevel.Info);
        }
    }
}
