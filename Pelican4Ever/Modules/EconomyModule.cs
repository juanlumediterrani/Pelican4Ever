using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Economy;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buffs;
using StardewValley.Locations;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Gestiona la reforma de comida (presupuesto diario de Saciedad y buff "Bien comido"),
    /// tope de recuperación en el Spa, persistencia de minas por turnos de 12 horas
    /// y multiplicadores de precios agrícolas y artesanales (TASK-301..304 / ADR-0004).
    /// </summary>
    public sealed class EconomyModule : IModule
    {
        private static ModContext? context;
        private static float preEatStamina = 0f;
        private static bool isCleaningMines = false;

        // Seguimiento local de regeneración pasiva de "Bien comido"
        private static int wellFedRemainingSeconds = 0;
        private static int spaSecondAccumulator = 0;
        private static int sittingSecondAccumulator = 0;

        // Seguimiento en el Host de cooldown de desocupación de pisos de minas (GAP-14)
        public static readonly System.Collections.Generic.Dictionary<int, DateTime> MineFloorLastOccupiedUtc = new();
        public static readonly System.Collections.Generic.Dictionary<int, DateTime> VolcanoLevelLastOccupiedUtc = new();

        public string Name => "Economy";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;

            ApplyHarmonyPatches(ctx.Harmony);
        }

        private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || context == null)
                return;

            // 1. Tick de "Bien comido" en el cliente local (+1 Stamina cada 60s si está activo)
            if (wellFedRemainingSeconds > 0)
            {
                wellFedRemainingSeconds--;
                if (wellFedRemainingSeconds % 60 == 0)
                {
                    if (Game1.player.Stamina < Game1.player.MaxStamina)
                    {
                        Game1.player.Stamina = Math.Min(Game1.player.MaxStamina, Game1.player.Stamina + context.Config.WellFedRegenPerMinute);
                    }
                }
            }

            // 2. Control de Spa en el cliente local si está quieto en la piscina
            if (Game1.player.swimming.Value && Game1.player.timerSinceLastMovement > 700)
            {
                spaSecondAccumulator++;
                if (spaSecondAccumulator >= context.Config.SpaRegenSeconds)
                {
                    spaSecondAccumulator = 0;
                    var record = context.WorldState.GetOrCreatePlayer(Game1.player.UniqueMultiplayerID, Game1.player.Name);
                    record.EnsureDailyCounters(Game1.Date.TotalDays);

                    int allowed = SatietyBudget.AllowedSpaGain(1, record.SpaEnergyUsed, context.Config.SpaDailyCap);
                    if (allowed > 0)
                    {
                        record.SpaEnergyUsed += allowed;
                        if (Game1.player.Stamina < Game1.player.MaxStamina)
                        {
                            Game1.player.Stamina = Math.Min(Game1.player.MaxStamina, Game1.player.Stamina + allowed);
                        }
                    }
                    else
                    {
                        // Límite diario alcanzado
                        if (spaSecondAccumulator == 0)
                        {
                            Game1.showRedMessage("Has alcanzado el límite diario de reposo en las termas.");
                        }
                    }
                }
            }
            else
            {
                spaSecondAccumulator = 0;
            }

            // 3. Control de descanso sentado (GAP-07)
            if (SittingRegenLogic.Tick(
                Game1.player.IsSitting(),
                Game1.player.isInBed.Value,
                Game1.player.Stamina,
                Game1.player.MaxStamina,
                sittingSecondAccumulator,
                context.Config.SittingRegenSeconds,
                out float staminaGain,
                out int newAccumulator))
            {
                Game1.player.Stamina = Math.Min(Game1.player.MaxStamina, Game1.player.Stamina + staminaGain);
            }
            sittingSecondAccumulator = newAccumulator;

            // 4. En el Host: Comprobar turnos de 12 horas de minas y cooldown por piso desocupado (GAP-14)
            if (Context.IsMainPlayer)
            {
                CheckMineResetTimes();
                CheckMineFloorCooldowns();
            }
        }

        private void CheckMineResetTimes()
        {
            if (context == null) return;

            DateTime now = Core.Time.ServerTime.LocalNow(context.Config.TimeZoneId);
            TimeSpan timeOfDay = now.TimeOfDay;

            foreach (TimeSpan resetTime in context.Config.ParsedMineResetTimes)
            {
                if (timeOfDay.Hours == resetTime.Hours && timeOfDay.Minutes == resetTime.Minutes && timeOfDay.Seconds <= 2)
                {
                    // Si no se ha limpiado en este minuto exacto
                    if ((now - context.WorldState.LastMineResetUtc).TotalMinutes > 5)
                    {
                        context.WorldState.LastMineResetUtc = now;
                        context.Monitor.Log($"[EconomyModule] Hora de cambio de turno minero ({resetTime:hh\\:mm}). Limpiando pisos desocupados...", LogLevel.Info);
                        ResetMines();
                    }
                }
            }
        }

        private static void CheckMineFloorCooldowns()
        {
            if (context == null || context.Config.MineFloorCooldownMinutes <= 0)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            string[] disconnectLevels = Game1.getAllFarmhands()
                .Where(fh => fh.disconnectDay.Value == Game1.MasterPlayer.stats.DaysPlayed)
                .Select(fh => fh.disconnectLocation.Value)
                .Where(loc => loc != null)
                .ToArray();

            // 1. Minas regulares y Caverna Calavera
            for (int i = MineShaft.activeMines.Count - 1; i >= 0; i--)
            {
                MineShaft mine = MineShaft.activeMines[i];
                int level = mine.mineLevel;
                int farmersCount = mine.farmers.Count;

                if (farmersCount > 0)
                {
                    MineFloorLastOccupiedUtc[level] = nowUtc;
                    continue;
                }

                if (!MineFloorLastOccupiedUtc.TryGetValue(level, out DateTime lastOccupied))
                {
                    MineFloorLastOccupiedUtc[level] = nowUtc;
                    continue;
                }

                bool isPendingWarp = Game1.locationRequest?.Location is MineShaft reqMine && mine.NameOrUniqueName == reqMine.NameOrUniqueName;
                bool hasDisconnect = disconnectLevels.Contains(mine.NameOrUniqueName);

                if (MineCooldownLogic.ShouldUnloadFloor(level, farmersCount, lastOccupied, nowUtc, context.Config.MineFloorCooldownMinutes, isPendingWarp, hasDisconnect))
                {
                    context.Monitor.Log($"[EconomyModule] Piso {level} desocupado durante {context.Config.MineFloorCooldownMinutes}m. Descargando para regeneración.", LogLevel.Trace);
                    mine.OnRemoved();
                    MineShaft.activeMines.RemoveAt(i);
                    MineFloorLastOccupiedUtc.Remove(level);
                }
            }

            // 2. Mazmorra del Volcán
            for (int i = VolcanoDungeon.activeLevels.Count - 1; i >= 0; i--)
            {
                VolcanoDungeon dungeon = VolcanoDungeon.activeLevels[i];
                int level = dungeon.level.Value;
                int farmersCount = dungeon.farmers.Count;

                if (farmersCount > 0)
                {
                    VolcanoLevelLastOccupiedUtc[level] = nowUtc;
                    continue;
                }

                if (!VolcanoLevelLastOccupiedUtc.TryGetValue(level, out DateTime lastOccupied))
                {
                    VolcanoLevelLastOccupiedUtc[level] = nowUtc;
                    continue;
                }

                bool isPendingWarp = Game1.locationRequest?.Location is VolcanoDungeon reqVd && dungeon.NameOrUniqueName == reqVd.NameOrUniqueName;
                bool hasDisconnect = disconnectLevels.Contains(dungeon.NameOrUniqueName);

                if (MineCooldownLogic.ShouldUnloadFloor(level, farmersCount, lastOccupied, nowUtc, context.Config.MineFloorCooldownMinutes, isPendingWarp, hasDisconnect))
                {
                    context.Monitor.Log($"[EconomyModule] Volcán nivel {level} desocupado durante {context.Config.MineFloorCooldownMinutes}m. Descargando para regeneración.", LogLevel.Trace);
                    dungeon.OnRemoved();
                    VolcanoDungeon.activeLevels.RemoveAt(i);
                    VolcanoLevelLastOccupiedUtc.Remove(level);
                }
            }
        }

        public static void TriggerMineResetManual()
        {
            if (context == null) return;
            context.Monitor.Log("[EconomyModule] Limpieza manual de minas invocada...", LogLevel.Info);
            ResetMines();
        }

        private static void ResetMines()
        {
            isCleaningMines = true;
            try
            {
                // Limpiar pisos de la mina donde no haya jugadores
                MineShaft.activeMines.RemoveAll(mine =>
                {
                    if (mine.farmers.Any())
                        return false;

                    MineFloorLastOccupiedUtc.Remove(mine.mineLevel);
                    mine.OnRemoved();
                    return true;
                });

                // Limpiar niveles del volcán donde no haya jugadores
                VolcanoDungeon.activeLevels.RemoveAll(level =>
                {
                    if (level.farmers.Any())
                        return false;

                    VolcanoLevelLastOccupiedUtc.Remove(level.level.Value);
                    level.OnRemoved();
                    return true;
                });

                Game1.addHUDMessage(new HUDMessage("Turno minero renovado: Las vetas de mineral han reaparecido.", HUDMessage.newQuest_type));
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[EconomyModule] Error al resetear minas: {ex}", LogLevel.Error);
            }
            finally
            {
                isCleaningMines = false;
            }
        }

        private void ApplyHarmonyPatches(Harmony harmony)
        {
            try
            {
                // Parche en Farmer.doneEating para aplicar SatietyBudget
                harmony.Patch(
                    original: AccessTools.Method(typeof(Farmer), nameof(Farmer.doneEating)),
                    prefix: new HarmonyMethod(typeof(EconomyModule), nameof(Prefix_DoneEating)),
                    postfix: new HarmonyMethod(typeof(EconomyModule), nameof(Postfix_DoneEating))
                );

                // Parche en Farmer.Update para bloquear la regeneración ilimitada nativa de la piscina
                harmony.Patch(
                    original: AccessTools.Method(typeof(Farmer), nameof(Farmer.Update)),
                    prefix: new HarmonyMethod(typeof(EconomyModule), nameof(Prefix_FarmerUpdate))
                );

                // Parche en MineShaft.clearInactiveMines para mantener los pisos persistentes durante el turno
                MethodInfo clearInactiveMethod = AccessTools.Method(typeof(MineShaft), "clearInactiveMines");
                if (clearInactiveMethod != null)
                {
                    harmony.Patch(
                        original: clearInactiveMethod,
                        prefix: new HarmonyMethod(typeof(EconomyModule), nameof(Prefix_ClearInactiveMines))
                    );
                }

                // Parche en Object.sellToStorePrice para multiplicadores agrícolas y artesanales
                harmony.Patch(
                    original: AccessTools.Method(typeof(StardewValley.Object), nameof(StardewValley.Object.sellToStorePrice), new[] { typeof(long) }),
                    postfix: new HarmonyMethod(typeof(EconomyModule), nameof(Postfix_SellToStorePrice))
                );

                // Parche en Farmer.DailyLuck para asignar suerte diaria individual por jugador (GAP-19)
                harmony.Patch(
                    original: AccessTools.PropertyGetter(typeof(Farmer), nameof(Farmer.DailyLuck)),
                    prefix: new HarmonyMethod(typeof(EconomyModule), nameof(Prefix_DailyLuck))
                );

                // Parche en BuffManager.Apply para escalar la duración de buffs de comida y bebida al tiempo 1:1 (GAP-11)
                harmony.Patch(
                    original: AccessTools.Method(typeof(BuffManager), nameof(BuffManager.Apply)),
                    prefix: new HarmonyMethod(typeof(EconomyModule), nameof(Prefix_BuffManagerApply))
                );

                context?.Monitor.Log("[EconomyModule] Parches Harmony de economía y minas aplicados.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[EconomyModule] Error al aplicar parches Harmony: {ex}", LogLevel.Error);
            }
        }

        public static void Prefix_DoneEating(Farmer __instance)
        {
            if (__instance == Game1.player)
            {
                preEatStamina = __instance.Stamina;
            }
        }

        public static void Postfix_DoneEating(Farmer __instance)
        {
            if (__instance != Game1.player || context == null)
                return;

            // Stardrop (O)434 no consume saciedad
            if (__instance.itemToEat is StardewValley.Object obj && obj.QualifiedItemId == "(O)434")
                return;

            float rawGain = __instance.Stamina - preEatStamina;
            if (rawGain > 0)
            {
                var record = context.WorldState.GetOrCreatePlayer(__instance.UniqueMultiplayerID, __instance.Name);
                record.EnsureDailyCounters(Game1.Date.TotalDays);

                int budget = SatietyBudget.DailyBudget(__instance.MaxStamina, context.Config.DailySatietyFraction);
                int allowedGain = SatietyBudget.AllowedGain((int)Math.Round(rawGain), record.SatietyUsed, budget);

                record.SatietyUsed += allowedGain;
                __instance.Stamina = preEatStamina + allowedGain;

                int remaining = Math.Max(0, budget - record.SatietyUsed);
                if (remaining > 0)
                {
                    Game1.addHUDMessage(new HUDMessage($"Saciedad: +{allowedGain} energía ({remaining} restante hoy).", HUDMessage.newQuest_type));
                }
                else
                {
                    Game1.showRedMessage("Presupuesto diario de saciedad agotado (solo recuperas salud y buffs).");
                }
            }

            // Comprobar si otorga el buff pasivo "Bien comido"
            if (__instance.itemToEat is StardewValley.Object food && food.Category == StardewValley.Object.CookingCategory)
            {
                var record = context.WorldState.GetOrCreatePlayer(__instance.UniqueMultiplayerID, __instance.Name);
                record.EnsureDailyCounters(Game1.Date.TotalDays);

                if (record.WellFedBuffsUsed < context.Config.MaxDailyWellFedBuffs)
                {
                    record.WellFedBuffsUsed++;
                    wellFedRemainingSeconds = context.Config.WellFedDurationMinutes * 60;
                    Game1.addHUDMessage(new HUDMessage("Bien comido: Regeneración pasiva de energía activada (60 min).", HUDMessage.newQuest_type));
                }
            }
        }

        public static void Prefix_FarmerUpdate(Farmer __instance)
        {
            // Bloquea el exploit vanilla de multijugador donde quedarse acostado en la cama sin desconectar
            // regenera estamina y salud infinitas sorteando el presupuesto de Saciedad y el Spa (GAP-08).
            if (__instance == Game1.player && __instance.isInBed.Value)
            {
                __instance.regenTimer = Math.Max(__instance.regenTimer, 500);
            }

            // La piscina nativa tiene un temporizador swimTimer que cada 100ms da +1 Stamina si swimTimer < 0.
            // Para evitar que la piscina recupere energía ilimitada al doble, fijamos swimTimer = 100 si no queremos que sume nativamente.
            if (__instance == Game1.player && __instance.swimming.Value && __instance.timerSinceLastMovement > 700)
            {
                // Nuestro OnOneSecondUpdateTicked se encarga de la recuperación regulada con tope diario.
                __instance.swimTimer = Math.Max(__instance.swimTimer, 50);
            }
        }

        public static bool Prefix_ClearInactiveMines()
        {
            // Bloqueamos la descarga rutinaria de pisos cada 10 min.
            // Solo se permite si el flag isCleaningMines está activo en el cambio de turno de 12 horas.
            if (isCleaningMines)
            {
                return true;
            }

            return false; // Mantiene la mina en memoria
        }

        public static void Postfix_SellToStorePrice(StardewValley.Object __instance, ref int __result)
        {
            if (context == null || __result <= 0)
                return;

            float cropMult = context.Config.CropPriceMultiplier;
            float artisanMult = context.Config.ArtisanPriceMultiplier;

            if (cropMult == 1.0f && artisanMult == 1.0f)
                return;

            // Categorías agrícolas directas: -75 (Vegetable), -79 (Fruit), -80 (Flower)
            if (__instance.Category is -75 or -79 or -80)
            {
                __result = (int)Math.Max(1, Math.Round(__result * cropMult));
            }
            // Categoría artesanal: -26 (ArtisanGoods)
            else if (__instance.Category == -26)
            {
                __result = (int)Math.Max(1, Math.Round(__result * artisanMult));
            }
        }

        public const float BuffTimeScaleFactor = 600_000f / 7_000f; // ~85.714f (1 min real = 1 min juego)
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Buff, object> ScaledBuffs = new();

        public static bool Prefix_DailyLuck(Farmer __instance, ref double __result)
        {
            __result = IndividualLuckLogic.Calculate(
                Game1.uniqueIDForThisGame,
                Game1.Date.TotalDays,
                __instance.UniqueMultiplayerID,
                __instance.hasSpecialCharm
            );
            return false;
        }

        public static void Prefix_BuffManagerApply(Buff buff)
        {
            if (buff == null || buff.millisecondsDuration <= 0 || buff.millisecondsDuration == -2)
                return;

            if (ScaledBuffs.TryGetValue(buff, out _))
                return;

            if (IsCombatDebuff(buff))
                return;

            buff.millisecondsDuration = (int)Math.Round(buff.millisecondsDuration * BuffTimeScaleFactor);
            ScaledBuffs.Add(buff, true);
        }

        private static bool IsCombatDebuff(Buff buff)
        {
            return buff.id is "12" or "13" or "14" or "17" or "19" or "20" or "21" or "25" or "26"
                || buff.id.ToLowerInvariant().Contains("debuff")
                || buff.id.ToLowerInvariant().Contains("slimed")
                || buff.id.ToLowerInvariant().Contains("frozen");
        }
    }
}
