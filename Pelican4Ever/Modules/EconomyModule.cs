using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Economy;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
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

            // 3. En el Host: Comprobar turnos de 12 horas de minas (06:00 y 18:00)
            if (Context.IsMainPlayer)
            {
                CheckMineResetTimes();
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

        private void ResetMines()
        {
            isCleaningMines = true;
            try
            {
                // Limpiar pisos de la mina donde no haya jugadores
                MineShaft.activeMines.RemoveAll(mine =>
                {
                    if (mine.farmers.Any())
                        return false;

                    mine.OnRemoved();
                    return true;
                });

                // Limpiar niveles del volcán donde no haya jugadores
                VolcanoDungeon.activeLevels.RemoveAll(level =>
                {
                    if (level.farmers.Any())
                        return false;

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
    }
}
