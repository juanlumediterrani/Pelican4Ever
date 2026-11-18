using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Economy;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buffs;
using StardewValley.Locations;
using StardewValley.Menus;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Gestiona la reforma de comida (Saciedad con vaciado continuo y buff "Bien comido", que activa el descanso sentado),
    /// el buff diario del Spa, persistencia de minas por turnos de 12 horas
    /// y multiplicadores de precios agrícolas y artesanales (TASK-301..304 / ADR-0004).
    /// </summary>
    public sealed class EconomyModule : IModule
    {
        private static ModContext? context;
        private static StardewValley.Object? preEatFoodItem = null;
        private static float preEatStamina = 0f;
        private static bool isCleaningMines = false;

        // Seguimiento local del Spa (segundos quieto en el agua y fracción de punto pendiente del buff) y del descanso sentado
        private static int spaStillSeconds = 0;
        private static double spaRegenCarry = 0;
        private static int sittingSecondAccumulator = 0;
        private static bool wasSitting = false;

        // Seguimiento en el Host de cooldown de desocupación de pisos de minas (GAP-14)
        public static readonly System.Collections.Generic.Dictionary<int, DateTime> MineFloorLastOccupiedUtc = new();
        public static readonly System.Collections.Generic.Dictionary<int, DateTime> VolcanoLevelLastOccupiedUtc = new();

        /// <summary>Mensaje cliente → host con el estado de consumo (Saciedad y contadores diarios) del jugador local.</summary>
        public const string MsgSatietyReport = "SatietyReport";
        /// <summary>Mensajes para persistir y restaurar buffs de comida activos tras desconexión/reconexión.</summary>
        public const string MsgFoodBuffsRequest = "FoodBuffsRequest";
        public const string MsgFoodBuffsSnapshot = "FoodBuffsSnapshot";
        public const string MsgFoodBuffsReport = "FoodBuffsReport";
        private const string MsgPriceRulesRequest = "PriceRulesRequest";
        private const string MsgPriceRulesResponse = "PriceRulesResponse";
        private static PriceRules? syncedPriceRules;

        private static bool foodBuffsRestored;
        private static bool hasLocalFoodBuffChanges;
        private static bool isApplyingFoodBuffContext;
        private static bool isRestoringFoodBuffs;
        private static bool isInDayUpdate;
        private static int buffApplyNestDepth;
        private static int buffClearNestDepth;

        /// <summary>
        /// En un farmhand, true en cuanto el jugador ha comido o descansado en el Spa en esta sesión: a partir de ahí su copia
        /// local es la más reciente y la sincronización de residencias del host no debe pisarla (ver <see cref="FarmSlotModule"/>).
        /// </summary>
        public static bool HasLocalConsumptionChanges { get; private set; }

        /// <summary>
        /// En un farmhand, true una vez recibido el snapshot de buffs de comida o tras haber modificado buffs localmente,
        /// para evitar que <c>SyncDoorsState</c> sobrescriba la copia local.
        /// </summary>
        public static bool HasLocalFoodBuffState => foodBuffsRestored || hasLocalFoodBuffChanges;

        public string Name => "Economy";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.GameLoop.DayStarted += OnDayStarted;
            ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += (_, _) =>
            {
                HasLocalConsumptionChanges = false;
                foodBuffsRestored = false;
                hasLocalFoodBuffChanges = false;
                isApplyingFoodBuffContext = false;
                isRestoringFoodBuffs = false;
                isInDayUpdate = false;
                buffApplyNestDepth = 0;
                buffClearNestDepth = 0;
                syncedPriceRules = null;
                spaStillSeconds = 0;
                spaRegenCarry = 0;
                sittingSecondAccumulator = 0;
                wasSitting = false;
            };
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;

            ApplyHarmonyPatches(ctx.Harmony);
        }

        private static void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            if (Context.IsMainPlayer)
            {
                syncedPriceRules = CurrentPriceRules();
                if (context != null && Game1.player != null)
                {
                    DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
                    var record = context.WorldState.GetOrCreatePlayer(Game1.player.UniqueMultiplayerID, Game1.player.Name);
                    int restored = RestoreFoodBuffsToLocalPlayer(record, nowUtc);
                    foodBuffsRestored = true;
                    if (restored > 0)
                        context.Monitor.Log($"[EconomyModule] Restaurados {restored} buff(s) de comida para el anfitrión.", LogLevel.Info);
                }
            }
            else
            {
                foodBuffsRestored = false;
                hasLocalFoodBuffChanges = false;
                RequestPriceRules();
                RequestFoodBuffs();
            }
        }

        private static void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            if (!Context.IsWorldReady || context == null || Game1.player == null || !foodBuffsRestored)
                return;

            var record = context.WorldState.GetOrCreatePlayer(Game1.player.UniqueMultiplayerID, Game1.player.Name);
            int restored = RestoreFoodBuffsToLocalPlayer(record, DateTimeOffset.UtcNow);
            if (restored > 0)
                context.Monitor.Log($"[EconomyModule] Mantenidos {restored} buff(s) de comida vigentes tras el cambio de día.", LogLevel.Trace);
        }

        private static void RequestPriceRules()
        {
            if (context != null && Game1.MasterPlayer != null)
                context.Helper.Multiplayer.SendMessage(string.Empty, MsgPriceRulesRequest, new[] { context.Manifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        }

        private static void RequestFoodBuffs()
        {
            if (context != null && !Context.IsMainPlayer && Game1.MasterPlayer != null)
                context.Helper.Multiplayer.SendMessage(string.Empty, MsgFoodBuffsRequest, new[] { context.Manifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        }

        private static PriceRules CurrentPriceRules() => new()
        {
            Crop = context!.Config.CropPriceMultiplier,
            Artisan = context.Config.ArtisanPriceMultiplier,
            Fish = context.Config.FishPriceMultiplier,
            Mineral = context.Config.MineralPriceMultiplier
        };

        private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || context == null || Game1.player == null)
                return;

            if (!Context.IsMainPlayer && syncedPriceRules == null)
                RequestPriceRules();

            if (!Context.IsMainPlayer && !foodBuffsRestored)
                RequestFoodBuffs();

            ModContext modCtx = context;

            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

            // 0. Actualizar vaciado continuo de Saciedad del jugador local
            var playerRecord = modCtx.WorldState.GetOrCreatePlayer(Game1.player.UniqueMultiplayerID, Game1.player.Name);
            playerRecord.EnsureDailyCounters(Game1.Date.TotalDays);
            playerRecord.UpdateSatietyDecay(nowUtc, Game1.player.MaxStamina, modCtx.Config.DailySatietyFraction, modCtx.Config.SatietyDrainHours);

            if (foodBuffsRestored && ReconcileActiveFoodBuffTimers(playerRecord, nowUtc) && !Context.IsMainPlayer)
                ReportFoodBuffsToHost(playerRecord, nowUtc);

            // 1. Spa: quedarse quieto en el agua (Spa o piscina) activa una vez al día el buff de recuperación pasiva
            if (Game1.player.swimming.Value && Game1.player.timerSinceLastMovement > 700)
            {
                spaStillSeconds++;
                if (spaStillSeconds == modCtx.Config.SpaStillSeconds)
                {
                    if (playerRecord.TryActivateSpaBuff(nowUtc, TimeSpan.FromMinutes(modCtx.Config.SpaBuffDurationMinutes)))
                    {
                        spaRegenCarry = 0;
                        ReportConsumptionToHost(playerRecord, nowUtc);
                        Game1.addHUDMessage(new HUDMessage(
                            I18n.Get("economy.spa_relaxed", new
                            {
                                points = modCtx.Config.SpaBuffTotalPoints,
                                duration = FormatMinutes(modCtx.Config.SpaBuffDurationMinutes)
                            }),
                            HUDMessage.newQuest_type));
                    }
                    else if (!playerRecord.IsSpaBuffActive(nowUtc))
                    {
                        Game1.showRedMessage(I18n.Get("economy.spa_already_used"));
                    }
                }
            }
            else
            {
                spaStillSeconds = 0;
            }

            // 2. Buff del Spa: reparte la recuperación de energía y salud a lo largo de su duración
            if (playerRecord.IsSpaBuffActive(nowUtc))
            {
                int points = PassiveRegenLogic.TickOneSecond(ref spaRegenCarry, modCtx.Config.SpaBuffTotalPoints, modCtx.Config.SpaBuffDurationMinutes);
                if (points > 0)
                {
                    if (Game1.player.Stamina < Game1.player.MaxStamina)
                        Game1.player.Stamina = Math.Min(Game1.player.MaxStamina, Game1.player.Stamina + points);
                    if (Game1.player.health < Game1.player.maxHealth)
                        Game1.player.health = Math.Min(Game1.player.maxHealth, Game1.player.health + points);
                }
            }
            else
            {
                spaRegenCarry = 0;
            }

            // 3. Descanso sentado (GAP-07), en banco, silla o en el suelo: solo recupera energía mientras dura "Bien comido"
            bool isSitting = (Game1.player.IsSitting() || GroundSitModule.IsLocalSittingOnGround) && !Game1.player.isInBed.Value;
            if (isSitting && !wasSitting)
                ShowSittingRestHint(playerRecord, nowUtc);
            wasSitting = isSitting;

            bool isWellFed = playerRecord.IsWellFed(
                nowUtc,
                Game1.player.MaxStamina,
                modCtx.Config.DailySatietyFraction,
                modCtx.Config.SatietyDrainHours,
                modCtx.Config.WellFedSatietyThresholdFraction);

            if (SittingRegenLogic.Tick(
                isSitting,
                Game1.player.isInBed.Value,
                isWellFed,
                Game1.player.Stamina,
                Game1.player.MaxStamina,
                sittingSecondAccumulator,
                modCtx.Config.SittingRegenSeconds,
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

                Game1.addHUDMessage(new HUDMessage(I18n.Get("economy.mining_shift_renewed"), HUDMessage.newQuest_type));
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
            _ = harmony;
            if (context == null)
                return;

            // Parche en Farmer.doneEating para aplicar SatietyBudget y detectar buffs procedentes de comida/bebida
            context.Patch(
                "Farmer.doneEating",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(Farmer), nameof(Farmer.doneEating), Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(EconomyModule), nameof(Prefix_DoneEating)),
                postfix: new HarmonyMethod(typeof(EconomyModule), nameof(Postfix_DoneEating)),
                finalizer: new HarmonyMethod(typeof(EconomyModule), nameof(Finalizer_DoneEating)));

            // Parche en Farmer.dayupdate para distinguir el cambio de día de un desmayo/muerte al limpiar buffs
            context.Patch(
                "Farmer.dayupdate",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(Farmer), nameof(Farmer.dayupdate), new[] { typeof(int) }),
                prefix: new HarmonyMethod(typeof(EconomyModule), nameof(Prefix_FarmerDayUpdate)),
                finalizer: new HarmonyMethod(typeof(EconomyModule), nameof(Finalizer_FarmerDayUpdate)));

            // Parche en Farmer.Update para bloquear la regeneración ilimitada nativa de la piscina
            context.Patch(
                "Farmer.Update",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(Farmer), nameof(Farmer.Update)),
                prefix: new HarmonyMethod(typeof(EconomyModule), nameof(Prefix_FarmerUpdate)));

            // Parche en MineShaft.clearInactiveMines para mantener los pisos persistentes durante el turno
            context.Patch(
                "MineShaft.clearInactiveMines",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(MineShaft), "clearInactiveMines"),
                prefix: new HarmonyMethod(typeof(EconomyModule), nameof(Prefix_ClearInactiveMines)));

            // Solo las consultas del ascensor son personales; la generación de pisos sigue siendo compartida.
            var elevatorTranspiler = new HarmonyMethod(typeof(EconomyModule), nameof(Transpiler_PersonalElevator));
            context.Patch(
                "MineElevatorMenu.ctor",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Constructor(typeof(MineElevatorMenu)),
                transpiler: elevatorTranspiler);
            context.Patch(
                "GameLocation.performAction.Elevator",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(GameLocation), nameof(GameLocation.performAction),
                    new[] { typeof(string[]), typeof(Farmer), typeof(xTile.Dimensions.Location) }),
                transpiler: elevatorTranspiler);

            // Parche en Object.sellToStorePrice para multiplicadores agrícolas y artesanales
            context.Patch(
                "Object.sellToStorePrice",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(StardewValley.Object), nameof(StardewValley.Object.sellToStorePrice), new[] { typeof(long) }),
                postfix: new HarmonyMethod(typeof(EconomyModule), nameof(Postfix_SellToStorePrice)));

            // Parche en Farmer.DailyLuck para asignar suerte diaria individual por jugador (GAP-19)
            context.Patch(
                "Farmer.DailyLuck",
                Name,
                PatchCriticality.Integrity,
                AccessTools.PropertyGetter(typeof(Farmer), nameof(Farmer.DailyLuck)),
                prefix: new HarmonyMethod(typeof(EconomyModule), nameof(Prefix_DailyLuck)));

            // Parche en BuffManager.Apply/Remove/Clear para escalar duración 1:1 (GAP-11) y persistir buffs de comida tras reconexión
            context.Patch(
                "BuffManager.Apply",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(BuffManager), nameof(BuffManager.Apply), new[] { typeof(Buff) }),
                prefix: new HarmonyMethod(typeof(EconomyModule), nameof(Prefix_BuffManagerApply)),
                postfix: new HarmonyMethod(typeof(EconomyModule), nameof(Postfix_BuffManagerApply)),
                finalizer: new HarmonyMethod(typeof(EconomyModule), nameof(Finalizer_BuffManagerApply)));

            context.Patch(
                "BuffManager.Remove",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(BuffManager), nameof(BuffManager.Remove), new[] { typeof(string) }),
                postfix: new HarmonyMethod(typeof(EconomyModule), nameof(Postfix_BuffManagerRemove)));

            context.Patch(
                "BuffManager.Clear",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(BuffManager), nameof(BuffManager.Clear), Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(EconomyModule), nameof(Prefix_BuffManagerClear)),
                finalizer: new HarmonyMethod(typeof(EconomyModule), nameof(Finalizer_BuffManagerClear)));

            context.Monitor.Log("[EconomyModule] Parches Harmony de economía y minas registrados.", LogLevel.Trace);
        }

        public static IEnumerable<CodeInstruction> Transpiler_PersonalElevator(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo sharedGetter = AccessTools.PropertyGetter(typeof(MineShaft), nameof(MineShaft.lowestLevelReached));
            MethodInfo personalGetter = AccessTools.Method(typeof(EconomyModule), nameof(GetPersonalElevatorLevel));
            int replacements = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(sharedGetter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = personalGetter;
                    replacements++;
                }
                yield return instruction;
            }
            if (replacements != 1)
                throw new InvalidOperationException($"Se esperaba una consulta al progreso del ascensor y se encontraron {replacements}.");
        }

        public static int GetPersonalElevatorLevel()
        {
            return MineProgressLogic.ElevatorLevel(Game1.player.deepestMineLevel, Game1.netWorldState.Value.LowestMineLevelForOrder);
        }

        public static void Prefix_DoneEating(Farmer __instance)
        {
            if (__instance == Game1.player)
            {
                preEatStamina = __instance.Stamina;
                preEatFoodItem = __instance.itemToEat as StardewValley.Object;
                isApplyingFoodBuffContext = true;
            }
        }

        public static void Finalizer_DoneEating(Farmer __instance)
        {
            if (__instance == Game1.player)
                isApplyingFoodBuffContext = false;
        }

        public static void Prefix_FarmerDayUpdate(Farmer __instance, out bool __state)
        {
            __state = __instance == Game1.player;
            if (__state)
                isInDayUpdate = true;
        }

        public static void Finalizer_FarmerDayUpdate(bool __state)
        {
            if (__state)
                isInDayUpdate = false;
        }

        public static void Postfix_DoneEating(Farmer __instance)
        {
            if (__instance != Game1.player || context == null)
                return;

            StardewValley.Object? foodItem = preEatFoodItem ?? (__instance.itemToEat as StardewValley.Object);
            preEatFoodItem = null;

            // Stardrop (O)434 no consume saciedad
            if (foodItem != null && foodItem.QualifiedItemId == "(O)434")
                return;

            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            var record = context.WorldState.GetOrCreatePlayer(__instance.UniqueMultiplayerID, __instance.Name);
            record.EnsureDailyCounters(Game1.Date.TotalDays);
            bool changed = false;

            int budget = SatietyBudget.DailyBudget(__instance.MaxStamina, context.Config.DailySatietyFraction);
            float threshold = SatietyBudget.WellFedThreshold(budget, context.Config.WellFedSatietyThresholdFraction);

            // Determinar ganancia de estamina de la comida consumida
            float rawGain = __instance.Stamina - preEatStamina;
            if (rawGain <= 0 && preEatStamina >= __instance.MaxStamina && foodItem != null)
            {
                rawGain = Math.Max(0, foodItem.staminaRecoveredOnConsumption());
            }

            if (rawGain > 0)
            {
                record.UpdateSatietyDecay(nowUtc, __instance.MaxStamina, context.Config.DailySatietyFraction, context.Config.SatietyDrainHours);

                int allowedGain = SatietyBudget.AllowedGain((int)Math.Round(rawGain), record.Satiety, budget);
                float oldSatiety = record.Satiety;

                record.Satiety = Math.Min(budget, record.Satiety + allowedGain);
                record.LastSatietyUpdateUtc = nowUtc;
                if (preEatStamina < __instance.MaxStamina)
                {
                    __instance.Stamina = Math.Min(__instance.MaxStamina, preEatStamina + allowedGain);
                }
                changed = true;

                int remaining = Math.Max(0, budget - (int)Math.Floor(record.Satiety));
                if (allowedGain > 0)
                {
                    if (remaining > 0)
                    {
                        Game1.addHUDMessage(new HUDMessage(I18n.Get("economy.satiety_energy_remaining", new { gain = allowedGain, remaining }), HUDMessage.newQuest_type));
                    }
                    else
                    {
                        Game1.addHUDMessage(new HUDMessage(I18n.Get("economy.satiety_energy_max", new { gain = allowedGain }), HUDMessage.newQuest_type));
                    }

                    // Notificar si se alcanza el umbral de "Bien comido" (al menos 1/4 de saciedad)
                    if (oldSatiety < threshold && record.Satiety >= threshold)
                    {
                        Game1.addHUDMessage(new HUDMessage(
                            I18n.Get("economy.well_fed_threshold_reached"),
                            HUDMessage.newQuest_type));
                    }
                }
                else
                {
                    Game1.showRedMessage(I18n.Get("economy.satiety_full_warning"));
                }
            }

            // Los platos cocinados siguen pudiendo activar una ventana garantizada de descanso si quedan usos
            if (foodItem != null && foodItem.Category == StardewValley.Object.CookingCategory)
            {
                if (record.TryActivateWellFed(nowUtc, TimeSpan.FromMinutes(context.Config.WellFedDurationMinutes), context.Config.MaxDailyWellFedBuffs))
                {
                    changed = true;
                    Game1.addHUDMessage(new HUDMessage(
                        I18n.Get("economy.cooked_dish_buff", new { duration = FormatMinutes(context.Config.WellFedDurationMinutes) }),
                        HUDMessage.newQuest_type));
                }
            }

            if (changed)
                ReportConsumptionToHost(record, nowUtc);
        }

        /// <summary>
        /// Al sentarse, avisa de si el descanso recupera energía: solo lo hace con "Bien comido" (al menos 1/4 de saciedad).
        /// </summary>
        private static void ShowSittingRestHint(PlayerRecord record, DateTimeOffset nowUtc)
        {
            if (context == null)
                return;

            int maxStamina = Game1.player.MaxStamina;
            bool isWellFed = record.IsWellFed(
                nowUtc,
                maxStamina,
                context.Config.DailySatietyFraction,
                context.Config.SatietyDrainHours,
                context.Config.WellFedSatietyThresholdFraction);

            if (isWellFed)
            {
                int minutesLeft = record.RemainingWellFedMinutes(
                    nowUtc,
                    maxStamina,
                    context.Config.DailySatietyFraction,
                    context.Config.SatietyDrainHours,
                    context.Config.WellFedSatietyThresholdFraction);

                string durationStr = minutesLeft > 0 ? $", {FormatMinutes(minutesLeft)}" : string.Empty;
                Game1.addHUDMessage(new HUDMessage(
                    I18n.Get("economy.sitting_resting_hud", new { seconds = context.Config.SittingRegenSeconds, duration = durationStr }),
                    HUDMessage.stamina_type));
            }
            else if (Game1.player.Stamina < maxStamina)
            {
                Game1.addHUDMessage(new HUDMessage(
                    I18n.Get("economy.sitting_need_well_fed"),
                    HUDMessage.error_type));
            }
        }

        private static string FormatMinutes(int minutes)
            => minutes % 60 == 0 ? (minutes == 60 ? "1 h" : $"{minutes / 60} h") : $"{minutes} min";

        /// <summary>
        /// En un farmhand, envía al host el estado de consumo del jugador local para que lo guarde en la partida maestra
        /// (fuente de verdad, ADR-0004) y lo conserve al desconectar y reconectar. En el host no hace falta: el registro ya es el suyo.
        /// </summary>
        private static void ReportConsumptionToHost(PlayerRecord record, DateTimeOffset nowUtc)
        {
            if (context == null || Context.IsMainPlayer || Game1.MasterPlayer == null)
                return;

            HasLocalConsumptionChanges = true;
            context.Helper.Multiplayer.SendMessage(
                record.ToConsumptionReport(nowUtc),
                MsgSatietyReport,
                new[] { context.Manifest.UniqueID },
                new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        }

        private static void ReportFoodBuffsToHost(PlayerRecord record, DateTimeOffset nowUtc)
        {
            if (context == null || Context.IsMainPlayer || Game1.MasterPlayer == null)
                return;

            hasLocalFoodBuffChanges = true;
            context.Helper.Multiplayer.SendMessage(
                record.ToFoodBuffsPayload(nowUtc),
                MsgFoodBuffsReport,
                new[] { context.Manifest.UniqueID },
                new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        }

        private static void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || e.FromModID != context.Manifest.UniqueID)
                return;

            if (Context.IsMainPlayer && e.Type == MsgPriceRulesRequest && Game1.GetPlayer(e.FromPlayerID) != null)
            {
                context.Helper.Multiplayer.SendMessage(CurrentPriceRules(), MsgPriceRulesResponse, new[] { context.Manifest.UniqueID }, new[] { e.FromPlayerID });
                return;
            }

            if (!Context.IsMainPlayer && e.Type == MsgPriceRulesResponse && e.FromPlayerID == Game1.MasterPlayer?.UniqueMultiplayerID && syncedPriceRules == null)
            {
                PriceRules received = e.ReadAs<PriceRules>();
                if (received != null
                    && float.IsFinite(received.Crop) && received.Crop > 0
                    && float.IsFinite(received.Artisan) && received.Artisan > 0
                    && float.IsFinite(received.Fish) && received.Fish > 0
                    && float.IsFinite(received.Mineral) && received.Mineral > 0)
                    syncedPriceRules = received;
                return;
            }

            if (Context.IsMainPlayer && e.Type == MsgFoodBuffsRequest)
            {
                Farmer? requester = Game1.GetPlayer(e.FromPlayerID);
                if (requester == null)
                    return;

                var requesterRecord = context.WorldState.GetOrCreatePlayer(e.FromPlayerID, requester.Name);
                FoodBuffsPayload snapshot = requesterRecord.ToFoodBuffsPayload(DateTimeOffset.UtcNow);
                context.Helper.Multiplayer.SendMessage(snapshot, MsgFoodBuffsSnapshot, new[] { context.Manifest.UniqueID }, new[] { e.FromPlayerID });
                return;
            }

            if (!Context.IsMainPlayer && e.Type == MsgFoodBuffsSnapshot && e.FromPlayerID == Game1.MasterPlayer?.UniqueMultiplayerID && !foodBuffsRestored)
            {
                if (!Context.IsWorldReady || Game1.player == null)
                    return;

                FoodBuffsPayload? snapshot = e.ReadAs<FoodBuffsPayload>();
                DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
                bool hadLocalBeforeSnapshot = hasLocalFoodBuffChanges;

                var localRecord = context.WorldState.GetOrCreatePlayer(Game1.player.UniqueMultiplayerID, Game1.player.Name);
                localRecord.ApplyFoodBuffsSnapshot(snapshot, nowUtc);
                foodBuffsRestored = true;

                int restored = RestoreFoodBuffsToLocalPlayer(localRecord, nowUtc);
                if (hadLocalBeforeSnapshot)
                {
                    SyncTrackedFoodBuffsFromApplied(localRecord, nowUtc);
                    ReportFoodBuffsToHost(localRecord, nowUtc);
                }

                if (restored > 0)
                    context.Monitor.Log($"[EconomyModule] Restaurados {restored} buff(s) de comida tras reconexión.", LogLevel.Info);
                return;
            }

            if (Context.IsMainPlayer && e.Type == MsgFoodBuffsReport)
            {
                Farmer? senderFarmer = Game1.GetPlayer(e.FromPlayerID);
                FoodBuffsPayload? reportPayload = e.ReadAs<FoodBuffsPayload>();
                if (senderFarmer == null)
                    return;

                var senderRecord = context.WorldState.GetOrCreatePlayer(e.FromPlayerID, senderFarmer.Name);
                senderRecord.ApplyFoodBuffsReport(reportPayload, DateTimeOffset.UtcNow, IsDisallowedBuffIdOnHost);
                return;
            }

            if (e.Type != MsgSatietyReport || !Context.IsMainPlayer)
                return;

            ConsumptionReport report = e.ReadAs<ConsumptionReport>();
            Farmer? farmer = Game1.GetPlayer(e.FromPlayerID);
            if (report == null || farmer == null)
                return;

            var record = context.WorldState.GetOrCreatePlayer(e.FromPlayerID, farmer.Name);
            int maxSatiety = SatietyBudget.DailyBudget(farmer.MaxStamina, context.Config.DailySatietyFraction);
            record.ApplyConsumptionReport(
                report,
                Game1.Date.TotalDays,
                DateTimeOffset.UtcNow,
                maxSatiety,
                maxWellFed: TimeSpan.FromMinutes(context.Config.WellFedDurationMinutes),
                maxSpaBuff: TimeSpan.FromMinutes(context.Config.SpaBuffDurationMinutes));
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
                // La recuperación en el agua la da el buff del Spa (una vez al día) desde OnOneSecondUpdateTicked.
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

            PriceRules active = !Context.IsMainPlayer && syncedPriceRules != null
                ? syncedPriceRules
                : CurrentPriceRules();
            float multiplier = CategoryPriceMultiplier.ForCategory(__instance.Category,
                active.Crop, active.Artisan, active.Fish, active.Mineral);
            if (multiplier == 1.0f)
                return;
            __result = (int)Math.Max(1, Math.Round(__result * multiplier));
        }

        public sealed class PriceRules
        {
            public float Crop { get; set; } = 1f;
            public float Artisan { get; set; } = 1f;
            public float Fish { get; set; } = 1f;
            public float Mineral { get; set; } = 1f;
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

        public static void Prefix_BuffManagerApply(BuffManager __instance, Buff buff, out bool __state)
        {
            __state = false;
            if (__instance != null && __instance.IsLocallyControlled())
            {
                buffApplyNestDepth++;
                __state = true;
            }

            if (buff == null || buff.millisecondsDuration <= 0 || buff.millisecondsDuration == -2)
                return;

            if (ScaledBuffs.TryGetValue(buff, out _))
                return;

            if (IsCombatDebuff(buff) || FoodBuffPersistenceLogic.IsTransientModBuffId(buff.id))
                return;

            double scaled = Math.Round(buff.millisecondsDuration * BuffTimeScaleFactor);
            buff.millisecondsDuration = (int)Math.Clamp(scaled, 1, int.MaxValue);
            buff.totalMillisecondsDuration = Math.Max(buff.totalMillisecondsDuration, buff.millisecondsDuration);
            ScaledBuffs.Add(buff, true);
        }

        public static void Postfix_BuffManagerApply(BuffManager __instance, Buff buff)
        {
            if (context == null || !context.IsModEnabled || !Context.IsWorldReady || Game1.player == null || Game1.exitToTitle || isRestoringFoodBuffs)
                return;

            if (__instance == null || !__instance.IsLocallyControlled())
                return;

            if (buff == null || string.IsNullOrWhiteSpace(buff.id))
                return;

            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            var record = context.WorldState.GetOrCreatePlayer(Game1.player.UniqueMultiplayerID, Game1.player.Name);

            if (IsDebuffForPersistence(buff) || buff.millisecondsDuration <= 0 || buff.millisecondsDuration == -2)
            {
                if (record.RemoveFoodBuff(buff.id))
                    ReportFoodBuffsToHost(record, nowUtc);
                return;
            }

            bool isFoodOrDrink = isApplyingFoodBuffContext || buff.id is "food" or "drink";
            if (!isFoodOrDrink || !__instance.AppliedBuffs.ContainsKey(buff.id))
                return;

            PersistedFoodBuff persisted = SnapshotFromBuff(buff, nowUtc);
            record.UpsertFoodBuff(persisted, nowUtc);
            ReportFoodBuffsToHost(record, nowUtc);
        }

        public static void Finalizer_BuffManagerApply(bool __state)
        {
            if (__state)
                buffApplyNestDepth = Math.Max(0, buffApplyNestDepth - 1);
        }

        public static void Postfix_BuffManagerRemove(BuffManager __instance, string id)
        {
            if (context == null || !context.IsModEnabled || !Context.IsWorldReady || Game1.player == null || Game1.exitToTitle
                || isRestoringFoodBuffs || isInDayUpdate || buffApplyNestDepth > 0)
                return;

            if (__instance == null || !__instance.IsLocallyControlled() || string.IsNullOrWhiteSpace(id))
                return;

            var record = context.WorldState.GetOrCreatePlayer(Game1.player.UniqueMultiplayerID, Game1.player.Name);
            if (record.RemoveFoodBuff(id) && buffClearNestDepth == 0)
                ReportFoodBuffsToHost(record, DateTimeOffset.UtcNow);
        }

        public static void Prefix_BuffManagerClear(BuffManager __instance, out bool __state)
        {
            __state = false;
            if (context != null && context.IsModEnabled && Context.IsWorldReady && Game1.player != null && !Game1.exitToTitle
                && !isRestoringFoodBuffs && !isInDayUpdate && __instance != null && __instance.IsLocallyControlled())
            {
                buffClearNestDepth++;
                __state = true;
            }
        }

        public static void Finalizer_BuffManagerClear(bool __state)
        {
            if (!__state)
                return;

            buffClearNestDepth = Math.Max(0, buffClearNestDepth - 1);
            if (buffClearNestDepth == 0 && context != null && Game1.player != null)
            {
                var record = context.WorldState.GetOrCreatePlayer(Game1.player.UniqueMultiplayerID, Game1.player.Name);
                if (record.ClearFoodBuffs())
                    ReportFoodBuffsToHost(record, DateTimeOffset.UtcNow);
            }
        }

        /// <summary>
        /// Sincroniza los buffs de comida del anfitrión y purga los caducados de todos los jugadores antes de guardar en disco.
        /// </summary>
        public static void PrepareFoodBuffsForSave(ModContext? modCtx)
        {
            if (modCtx == null || !Context.IsMainPlayer)
                return;

            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            if (Context.IsWorldReady && Game1.player != null)
            {
                var hostRecord = modCtx.WorldState.GetOrCreatePlayer(Game1.player.UniqueMultiplayerID, Game1.player.Name);
                SyncTrackedFoodBuffsFromApplied(hostRecord, nowUtc);
            }

            foreach (PlayerRecord player in modCtx.WorldState.Players.Values)
            {
                player?.PruneExpiredFoodBuffs(nowUtc);
            }
        }

        private static bool ReconcileActiveFoodBuffTimers(PlayerRecord record, DateTimeOffset nowUtc)
        {
            if (Game1.player?.buffs == null || record.ActiveFoodBuffs == null || record.ActiveFoodBuffs.Count == 0)
                return false;

            bool changed = false;
            for (int i = record.ActiveFoodBuffs.Count - 1; i >= 0; i--)
            {
                PersistedFoodBuff persisted = record.ActiveFoodBuffs[i];
                if (persisted == null)
                {
                    record.ActiveFoodBuffs.RemoveAt(i);
                    changed = true;
                    continue;
                }

                if (Game1.player.buffs.AppliedBuffs.TryGetValue(persisted.Id, out Buff? activeBuff)
                    && activeBuff != null
                    && activeBuff.millisecondsDuration > 0)
                {
                    DateTimeOffset expectedExpiresAtUtc = nowUtc + TimeSpan.FromMilliseconds(activeBuff.millisecondsDuration);
                    if (Math.Abs((expectedExpiresAtUtc - persisted.ExpiresAtUtc).TotalSeconds) >= 15.0)
                    {
                        persisted.ExpiresAtUtc = expectedExpiresAtUtc;
                        changed = true;
                    }
                }
                else if (persisted.ExpiresAtUtc <= nowUtc)
                {
                    record.ActiveFoodBuffs.RemoveAt(i);
                    changed = true;
                }
            }

            return changed;
        }

        private static void SyncTrackedFoodBuffsFromApplied(PlayerRecord record, DateTimeOffset nowUtc)
        {
            if (Game1.player?.buffs == null)
                return;

            var trackedIds = new HashSet<string>(StringComparer.Ordinal) { "food", "drink" };
            if (record.ActiveFoodBuffs != null)
            {
                foreach (PersistedFoodBuff existing in record.ActiveFoodBuffs)
                {
                    if (!string.IsNullOrWhiteSpace(existing?.Id))
                        trackedIds.Add(existing.Id);
                }
            }

            foreach (string id in trackedIds)
            {
                if (Game1.player.buffs.AppliedBuffs.TryGetValue(id, out Buff? activeBuff)
                    && activeBuff != null
                    && activeBuff.millisecondsDuration > 0
                    && activeBuff.millisecondsDuration != -2
                    && !IsDebuffForPersistence(activeBuff))
                {
                    record.UpsertFoodBuff(SnapshotFromBuff(activeBuff, nowUtc), nowUtc);
                }
                else
                {
                    record.RemoveFoodBuff(id);
                }
            }

            record.PruneExpiredFoodBuffs(nowUtc);
        }

        private static int RestoreFoodBuffsToLocalPlayer(PlayerRecord record, DateTimeOffset nowUtc)
        {
            if (Game1.player?.buffs == null || record.ActiveFoodBuffs == null)
                return 0;

            record.PruneExpiredFoodBuffs(nowUtc);
            if (record.ActiveFoodBuffs.Count == 0)
                return 0;

            int restoredCount = 0;
            isRestoringFoodBuffs = true;
            try
            {
                foreach (PersistedFoodBuff saved in record.ActiveFoodBuffs.ToList())
                {
                    if (saved == null || FoodBuffPersistenceLogic.IsDisallowedBuffId(saved.Id))
                        continue;

                    int remainingMs = saved.RemainingMilliseconds(nowUtc);
                    if (remainingMs <= 0)
                        continue;

                    if (Game1.player.buffs.AppliedBuffs.ContainsKey(saved.Id))
                        continue;

                    Buff? buff = CreateBuffFromPersisted(saved, remainingMs);
                    if (buff == null)
                        continue;

                    ScaledBuffs.Add(buff, true);
                    Game1.player.buffs.Apply(buff);
                    restoredCount++;
                }
            }
            finally
            {
                isRestoringFoodBuffs = false;
            }

            return restoredCount;
        }

        private static PersistedFoodBuff SnapshotFromBuff(Buff buff, DateTimeOffset nowUtc)
        {
            string? iconTextureName = null;
            if (buff.iconTexture != null)
            {
                if (ReferenceEquals(buff.iconTexture, Game1.buffsIcons))
                    iconTextureName = "TileSheets\\BuffsIcons";
                else if (!string.IsNullOrWhiteSpace(buff.iconTexture.Name))
                    iconTextureName = buff.iconTexture.Name;
            }

            int durationMs = FoodBuffPersistenceLogic.ClampRemainingMs(buff.millisecondsDuration);
            return new PersistedFoodBuff
            {
                Id = buff.id,
                Source = buff.source,
                DisplaySource = buff.displaySource,
                DisplayName = buff.displayName,
                Description = buff.description,
                IconTextureName = iconTextureName,
                IconSheetIndex = buff.iconSheetIndex,
                GlowR = buff.glow.R,
                GlowG = buff.glow.G,
                GlowB = buff.glow.B,
                GlowA = buff.glow.A,
                TotalDurationMs = Math.Max(buff.totalMillisecondsDuration, durationMs),
                ExpiresAtUtc = nowUtc + TimeSpan.FromMilliseconds(durationMs),
                Visible = buff.visible,
                Effects = new FoodBuffEffectsSnapshot
                {
                    CombatLevel = buff.effects.CombatLevel.Value,
                    FarmingLevel = buff.effects.FarmingLevel.Value,
                    FishingLevel = buff.effects.FishingLevel.Value,
                    MiningLevel = buff.effects.MiningLevel.Value,
                    LuckLevel = buff.effects.LuckLevel.Value,
                    ForagingLevel = buff.effects.ForagingLevel.Value,
                    MaxStamina = buff.effects.MaxStamina.Value,
                    MagneticRadius = buff.effects.MagneticRadius.Value,
                    Speed = buff.effects.Speed.Value,
                    Defense = buff.effects.Defense.Value,
                    Attack = buff.effects.Attack.Value,
                    Immunity = buff.effects.Immunity.Value,
                    AttackMultiplier = buff.effects.AttackMultiplier.Value,
                    KnockbackMultiplier = buff.effects.KnockbackMultiplier.Value,
                    WeaponSpeedMultiplier = buff.effects.WeaponSpeedMultiplier.Value,
                    CriticalChanceMultiplier = buff.effects.CriticalChanceMultiplier.Value,
                    CriticalPowerMultiplier = buff.effects.CriticalPowerMultiplier.Value,
                    WeaponPrecisionMultiplier = buff.effects.WeaponPrecisionMultiplier.Value
                },
                CustomFields = buff.customFields != null && buff.customFields.Count > 0
                    ? new Dictionary<string, string>(buff.customFields, StringComparer.Ordinal)
                    : null
            };
        }

        private static Buff? CreateBuffFromPersisted(PersistedFoodBuff saved, int remainingMs)
        {
            if (string.IsNullOrWhiteSpace(saved.Id) || remainingMs <= 0)
                return null;

            Texture2D? iconTexture = null;
            int iconSheetIndex = saved.IconSheetIndex;
            if (!string.IsNullOrWhiteSpace(saved.IconTextureName))
            {
                if (string.Equals(saved.IconTextureName, "TileSheets\\BuffsIcons", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(saved.IconTextureName, "TileSheets/BuffsIcons", StringComparison.OrdinalIgnoreCase))
                {
                    iconTexture = Game1.buffsIcons;
                }
                else
                {
                    try
                    {
                        iconTexture = Game1.content.Load<Texture2D>(saved.IconTextureName);
                    }
                    catch (Exception ex)
                    {
                        context?.Monitor.Log($"[EconomyModule] No se pudo cargar icono '{saved.IconTextureName}' para buff '{saved.Id}': {ex.Message}", LogLevel.Trace);
                        iconTexture = null;
                        iconSheetIndex = -1;
                    }
                }
            }

            var buff = new Buff(
                id: saved.Id,
                source: saved.Source,
                displaySource: saved.DisplaySource,
                duration: remainingMs,
                iconTexture: iconTexture,
                iconSheetIndex: iconSheetIndex,
                effects: null,
                isDebuff: false,
                displayName: saved.DisplayName,
                description: saved.Description);

            buff.actionsOnApply = null;
            buff.millisecondsDuration = remainingMs;
            buff.totalMillisecondsDuration = Math.Max(remainingMs, saved.TotalDurationMs);
            buff.visible = saved.Visible;
            buff.glow = new Color(saved.GlowR, saved.GlowG, saved.GlowB, saved.GlowA);

            if (string.IsNullOrWhiteSpace(saved.IconTextureName) && saved.IconSheetIndex < 0)
            {
                buff.iconTexture = null;
                buff.iconSheetIndex = -1;
            }

            buff.effects.Clear();
            if (saved.Effects != null)
            {
                FoodBuffEffectsSnapshot fx = saved.Effects.CloneSanitized();
                buff.effects.CombatLevel.Value = fx.CombatLevel;
                buff.effects.FarmingLevel.Value = fx.FarmingLevel;
                buff.effects.FishingLevel.Value = fx.FishingLevel;
                buff.effects.MiningLevel.Value = fx.MiningLevel;
                buff.effects.LuckLevel.Value = fx.LuckLevel;
                buff.effects.ForagingLevel.Value = fx.ForagingLevel;
                buff.effects.MaxStamina.Value = fx.MaxStamina;
                buff.effects.MagneticRadius.Value = fx.MagneticRadius;
                buff.effects.Speed.Value = fx.Speed;
                buff.effects.Defense.Value = fx.Defense;
                buff.effects.Attack.Value = fx.Attack;
                buff.effects.Immunity.Value = fx.Immunity;
                buff.effects.AttackMultiplier.Value = fx.AttackMultiplier;
                buff.effects.KnockbackMultiplier.Value = fx.KnockbackMultiplier;
                buff.effects.WeaponSpeedMultiplier.Value = fx.WeaponSpeedMultiplier;
                buff.effects.CriticalChanceMultiplier.Value = fx.CriticalChanceMultiplier;
                buff.effects.CriticalPowerMultiplier.Value = fx.CriticalPowerMultiplier;
                buff.effects.WeaponPrecisionMultiplier.Value = fx.WeaponPrecisionMultiplier;
            }

            if (saved.CustomFields != null)
            {
                foreach (var kvp in saved.CustomFields)
                    buff.customFields[kvp.Key] = kvp.Value;
            }

            return buff;
        }

        private static bool IsDisallowedBuffIdOnHost(string id)
        {
            if (FoodBuffPersistenceLogic.IsDisallowedBuffId(id))
                return true;

            try
            {
                if (Game1.content != null && DataLoader.Buffs(Game1.content).TryGetValue(id, out var data) && data.IsDebuff)
                    return true;
            }
            catch
            {
                // Si el contenido aún no está disponible, basta la validación pura del Core.
            }

            return false;
        }

        private static bool IsDebuffForPersistence(Buff buff)
        {
            if (IsCombatDebuff(buff) || FoodBuffPersistenceLogic.IsDisallowedBuffId(buff.id))
                return true;

            try
            {
                if (Game1.content != null && DataLoader.Buffs(Game1.content).TryGetValue(buff.id, out var data) && data.IsDebuff)
                    return true;
            }
            catch
            {
                // Ignorar si no se puede consultar Data/Buffs.
            }

            return false;
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
