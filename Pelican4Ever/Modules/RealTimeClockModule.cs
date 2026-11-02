using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pelican4Ever.Core.Time;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Network.Dedicated;
using StardewValley.Pathfinding;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Sincroniza el reloj del juego 1:1 con la hora real y evita pausas indeseadas (TASK-101 / ADR-0002).
    /// Permite además al anfitrión ajustar u omitir horas con comandos /time +X, -X o reset.
    /// </summary>
    public sealed class RealTimeClockModule : IModule
    {
        private const string TimeOffsetMessageType = "RealTimeClock.Offset";
        private static ModContext? context;
        public static bool IsNightHold { get; private set; }
        public static TimeSpan ManualTimeOffset { get; set; } = TimeSpan.Zero;
        private static bool timeChatCommandsRegistered;

        /// <summary>Ticks de 10 min procesados sobre máquinas durante la Madrugada en curso (GAP-17).</summary>
        public static int NightHoldTicksSimulated { get; set; } = 0;

        /// <summary>Primera sincronización tras cargar la partida: el reloj salta de 0600 a la hora real.</summary>
        private static bool startupSyncPending;

        /// <summary>Recolocación de aldeanos aplazada porque el salto ocurrió durante un evento o festival.</summary>
        private static bool villagerCatchUpPending;

        /// <summary>Saltos mayores que esto (3 h) fijan la hora directamente en vez de simular cada tramo.</summary>
        private const int MaxSimulatedTicks = 18;

        public string Name => "RealTimeClock";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            // En el Host nos aseguramos de que el intervalo de 10 min de juego corresponda a 10 min reales
            // (600.000 ms) para interpolar correctamente la iluminación ambiental en Game1.UpdateGameClock.
            if (Context.IsMainPlayer)
            {
                Game1.realMilliSecondsPerGameTenMinutes = 600_000;
            }

            ctx.Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
            ctx.Helper.Events.GameLoop.DayStarted += OnDayStarted;
            ctx.Helper.Events.GameLoop.SaveLoaded += (_, _) => startupSyncPending = Context.IsMainPlayer;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += (_, _) =>
            {
                startupSyncPending = villagerCatchUpPending = false;
                ManualTimeOffset = TimeSpan.Zero;
            };
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            ctx.Helper.Events.Multiplayer.PeerContextReceived += OnPeerContextReceived;

            // Comandos para host para forzar o adelantar/retrasar horas (+X, -X, reset)
            ctx.Helper.ConsoleCommands.Add(
                "p4e_time",
                "Ajusta o fuerza la hora del juego en el servidor. Uso: p4e_time <+X|-X|reset|set HH:mm> (ej: p4e_time +2, p4e_time -1, p4e_time reset)",
                OnConsoleTime
            );
            ctx.Helper.Events.GameLoop.GameLaunched += (s, e) => RegisterTimeChatCommands();

            // Parches Harmony
            ApplyHarmonyPatches(ctx.Harmony);
        }

        private static void RegisterTimeChatCommands()
        {
            if (timeChatCommandsRegistered) return;
            try
            {
                if (!ChatCommands.Exists("time"))
                {
                    ChatCommands.Register(
                        "time",
                        HandleTimeChatCommand,
                        name => "Ajusta la hora del juego en el servidor (/time o /hora <+X|-X|reset|set HH:mm>)",
                        new[] { "hora" },
                        mainOnly: true,
                        multiplayerOnly: false,
                        cheatsOnly: false
                    );
                }
                timeChatCommandsRegistered = true;
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[RealTimeClockModule] Error registrando comando de chat /time: {ex}", LogLevel.Warn);
            }
        }

        private static void HandleTimeChatCommand(string[] command, ChatBox chat)
        {
            string arg = command.Length > 1 ? string.Join(" ", command.Skip(1)) : string.Empty;
            AdjustTime(arg, out string message);
            chat.addInfoMessage(message);
        }

        private void OnConsoleTime(string command, string[] args)
        {
            string arg = string.Join(" ", args);
            AdjustTime(arg, out string message);
            context?.Monitor.Log(message, LogLevel.Info);
        }

        public static bool AdjustTime(string input, out string message)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                message = $"[Reloj] Offset actual: {ManualTimeOffset.TotalHours:+0.##;-0.##;0}h. Hora del juego: {Game1.getTimeOfDayString(Game1.timeOfDay)}. Uso: /time <+X|-X|reset|set HH:mm>";
                return false;
            }

            input = input.Trim().ToLowerInvariant();

            if (input == "reset")
            {
                ManualTimeOffset = TimeSpan.Zero;
                ApplyCurrentClock();
                BroadcastTimeOffset();
                message = $"[Reloj] Restablecido a hora real. Hora del juego: {Game1.getTimeOfDayString(Game1.timeOfDay)}.";
                return true;
            }

            if (input.StartsWith("set ") || input.StartsWith("set="))
            {
                string val = input.Substring(4).Trim();
                if (TimeSpan.TryParse(val, out TimeSpan targetSpan) || (int.TryParse(val, out int rawTime) && TryConvertRawTimeToSpan(rawTime, out targetSpan)))
                {
                    DateTime baseNow = ServerTime.LocalNow(context?.Config.TimeZoneId ?? "Europe/Madrid");
                    ManualTimeOffset = targetSpan - baseNow.TimeOfDay;
                    ApplyCurrentClock();
                    BroadcastTimeOffset();
                    message = $"[Reloj] Hora fijada a {Game1.getTimeOfDayString(Game1.timeOfDay)} (Offset: {ManualTimeOffset.TotalHours:+0.##;-0.##;0}h).";
                    return true;
                }
                message = "[Reloj] Formato de hora no válido. Usa HH:mm (ej: /time set 14:00 o /time set 08:30).";
                return false;
            }

            bool isMinus = input.StartsWith("-");
            bool isPlus = input.StartsWith("+");
            string numStr = input.TrimStart('+', '-').TrimEnd('h');

            if (numStr.EndsWith("m"))
            {
                if (double.TryParse(numStr.TrimEnd('m'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double minutes))
                {
                    TimeSpan delta = TimeSpan.FromMinutes(isMinus ? -minutes : minutes);
                    ManualTimeOffset += delta;
                    ApplyCurrentClock();
                    BroadcastTimeOffset();
                    message = $"[Reloj] Ajustado {(isMinus ? "-" : "+")}{minutes} min. Nueva hora: {Game1.getTimeOfDayString(Game1.timeOfDay)} (Offset total: {ManualTimeOffset.TotalHours:+0.##;-0.##;0}h).";
                    return true;
                }
            }
            else if (double.TryParse(numStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double hours))
            {
                TimeSpan delta = TimeSpan.FromHours(isMinus ? -hours : hours);
                ManualTimeOffset += delta;
                ApplyCurrentClock();
                BroadcastTimeOffset();
                message = $"[Reloj] Ajustado {(isMinus ? "-" : "+")}{hours}h. Nueva hora: {Game1.getTimeOfDayString(Game1.timeOfDay)} (Offset total: {ManualTimeOffset.TotalHours:+0.##;-0.##;0}h).";
                return true;
            }

            message = "[Reloj] Formato no válido. Ejemplos válidos: /time +2, /time -1, /time +30m, /time reset, /time set 14:00";
            return false;
        }

        private static void BroadcastTimeOffset()
        {
            if (context == null || !Context.IsMainPlayer) return;
            context.Helper.Multiplayer.SendMessage(
                ManualTimeOffset.Ticks,
                TimeOffsetMessageType,
                new[] { context.Manifest.UniqueID }
            );
        }

        private void OnPeerContextReceived(object? sender, PeerContextReceivedEventArgs e)
        {
            if (context == null || !Context.IsMainPlayer || ManualTimeOffset == TimeSpan.Zero) return;
            context.Helper.Multiplayer.SendMessage(
                ManualTimeOffset.Ticks,
                TimeOffsetMessageType,
                new[] { context.Manifest.UniqueID },
                new[] { e.Peer.PlayerID }
            );
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || e.FromModID != context.Manifest.UniqueID) return;

            if (e.Type == TimeOffsetMessageType)
            {
                long ticks = e.ReadAs<long>();
                ManualTimeOffset = TimeSpan.FromTicks(ticks);
                context.Monitor.Log($"[RealTimeClockModule] Offset horario sincronizado desde anfitrión: {ManualTimeOffset.TotalHours:+0.##;-0.##;0}h.", LogLevel.Trace);
            }
        }

        private static bool TryConvertRawTimeToSpan(int raw, out TimeSpan span)
        {
            int hours = raw / 100;
            int mins = raw % 100;
            if (hours >= 0 && hours < 24 && mins >= 0 && mins < 60)
            {
                span = new TimeSpan(hours, mins, 0);
                return true;
            }
            span = TimeSpan.Zero;
            return false;
        }

        public static void ApplyCurrentClock()
        {
            if (context == null || !Context.IsMainPlayer) return;

            var cfg = context.Config;
            DateTime now = ServerTime.LocalNow(cfg.TimeZoneId).Add(ManualTimeOffset);
            ClockReading reading = GameClock.Read(now.TimeOfDay, cfg.ParsedDayStart, cfg.ParsedNightHoldStart);
            IsNightHold = reading.IsNightHold;
            Game1.timeOfDay = reading.TimeOfDay;
            Game1.gameTimeInterval = reading.MillisecondsIntoSlot;

            if (Game1.netWorldState?.Value != null)
            {
                Game1.netWorldState.Value.UpdateFromGame1();
            }

            CatchUpVillagerSchedules(Game1.timeOfDay);
        }

        /// <summary>Fija la hora sin simular los tramos intermedios y pone a los aldeanos donde estarían a esa hora.</summary>
        private static void JumpClockTo(int timeOfDay)
        {
            Game1.timeOfDay = timeOfDay;
            Game1.netWorldState.Value.UpdateFromGame1();
            CatchUpVillagerSchedules(timeOfDay);
        }

        /// <summary>
        /// Coloca a cada aldeano al final de la última entrada de su horario que ya debería haber empezado.
        /// Sin esto, tras un salto de reloj se quedan en su posición de las 0600: checkSchedule solo encola la
        /// entrada de la hora exacta de cada tramo, y los tramos saltados no se vuelven a mirar. Sirve igual para
        /// adelantar que para retrasar la hora (arranque a media jornada y comandos /time).
        /// </summary>
        public static void CatchUpVillagerSchedules(int timeOfDay)
        {
            if (!Context.IsMainPlayer)
                return;

            // Durante la escena de bienvenida del día 1 o un festival, los NPC no están en su horario: reintentar después
            villagerCatchUpPending = Game1.isFestival() || Game1.eventUp;
            if (villagerCatchUpPending)
                return;

            var villagers = new List<NPC>();
            Utility.ForEachVillager(npc =>
            {
                villagers.Add(npc);
                return true;
            });

            int moved = 0;
            foreach (NPC npc in villagers)
            {
                try
                {
                    if (CatchUpSchedule(npc, timeOfDay))
                        moved++;
                }
                catch (Exception ex)
                {
                    context?.Monitor.Log($"[RealTimeClockModule] No se pudo poner al día el horario de {npc.Name}: {ex.Message}", LogLevel.Trace);
                }
            }

            if (moved > 0)
                context?.Monitor.Log($"[RealTimeClockModule] {moved} aldeanos recolocados según su horario de las {Game1.getTimeOfDayString(timeOfDay)}.", LogLevel.Info);
        }

        private static bool CatchUpSchedule(NPC npc, int timeOfDay)
        {
            if (npc.Schedule == null || npc.ignoreScheduleToday || npc.IsInvisible)
                return false;

            // Si la hora retrocede antes del primer tramo, el aldeano vuelve a su posición de la mañana
            int? entryTime = ScheduleCatchUp.LatestEntryAtOrBefore(npc.Schedule.Keys, timeOfDay);
            SchedulePathDescription? entry = entryTime != null ? npc.Schedule[entryTime.Value] : null;
            if (entry == null && npc.isMarried())
                return false; // su posición de la mañana la decide marriageDuties, no DefaultMap
            GameLocation? target = Game1.getLocationFromName(entry?.targetLocationName ?? npc.DefaultMap);
            if (target == null)
                return false;

            ResetRouteState(npc);
            if (entry != null)
            {
                Game1.warpCharacter(npc, target, new Vector2(entry.targetTile.X, entry.targetTile.Y));
                npc.faceDirection(entry.facingDirection);
                npc.previousEndPoint = entry.targetTile;
            }
            else
            {
                Game1.warpCharacter(npc, target, npc.DefaultPosition / Game1.tileSize);
                npc.faceDirection(npc.DefaultFacingDirection);
                npc.previousEndPoint = new Point((int)npc.DefaultPosition.X / Game1.tileSize, (int)npc.DefaultPosition.Y / Game1.tileSize);
            }

            // Tras un retroceso, checkSchedule debe volver a encolar los tramos posteriores a la nueva hora
            npc.lastAttemptedSchedule = timeOfDay;

            // Animación o mensaje de fin de ruta (sentarse, trabajar en el mostrador, dormir...), como hace checkSchedule al llegar
            if (entry != null && GetRouteEndBehavior?.Invoke(npc, new object?[] { entry.endOfRouteBehavior, entry.endOfRouteMessage }) is PathFindController.endBehavior endBehavior)
                endBehavior(npc, target);

            return true;
        }

        /// <summary>
        /// Deja al aldeano sin ruta ni animación de fin de ruta en curso (dormido, sentado, paseando en cuadrado...),
        /// como hace <c>NPC.resetForNewDay</c>; si no, conserva la pose de la hora anterior en su nueva posición.
        /// </summary>
        private static void ResetRouteState(NPC npc)
        {
            npc.controller = null;
            npc.temporaryController = null;
            npc.queuedSchedulePaths.Clear();
            if (npc.doingEndOfRouteAnimation.Value)
                RouteEndAnimationFinished?.Invoke(npc, new object?[] { null });

            npc.isSleeping.Value = false;
            npc.goingToDoEndOfRouteAnimation.Value = false;
            npc.drawOffset = Vector2.Zero;
            npc.appliedRouteAnimationOffset = Vector2.Zero;
            if (npc.layingDown)
            {
                npc.layingDown = false;
                npc.HideShadow = false;
            }

            // Con returningToEndPoint activo, checkSchedule no atiende ningún tramo nuevo
            IsWalkingInSquareField?.SetValue(npc, false);
            ReturningToEndPointField?.SetValue(npc, false);
            StartedEndOfRouteBehaviorField?.SetValue(npc, null);
            npc.Sprite.StopAnimation();
            npc.Halt();
        }

        private static readonly System.Reflection.MethodInfo? GetRouteEndBehavior =
            AccessTools.Method(typeof(NPC), "getRouteEndBehaviorFunction");

        private static readonly System.Reflection.MethodInfo? RouteEndAnimationFinished =
            AccessTools.Method(typeof(NPC), "routeEndAnimationFinished");

        private static readonly System.Reflection.FieldInfo? IsWalkingInSquareField =
            AccessTools.Field(typeof(NPC), "isWalkingInSquare");

        private static readonly System.Reflection.FieldInfo? ReturningToEndPointField =
            AccessTools.Field(typeof(NPC), "returningToEndPoint");

        private static readonly System.Reflection.FieldInfo? StartedEndOfRouteBehaviorField =
            AccessTools.Field(typeof(NPC), "_startedEndOfRouteBehavior");

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            NightHoldTicksSimulated = 0;
            if (Context.IsMainPlayer)
            {
                Game1.realMilliSecondsPerGameTenMinutes = 600_000;
            }
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || !Context.IsMainPlayer)
                return;

            // En el servidor dedicado, DedicatedServer.Tick pausa el mundo si no hay nadie conectado.
            // Para mantener el mundo persistente 24/7, despausamos.
            if (Game1.netWorldState.Value.IsPaused)
            {
                Game1.netWorldState.Value.IsPaused = false;
            }
        }

        private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || context == null)
                return;

            var cfg = context.Config;
            DateTime now = ServerTime.LocalNow(cfg.TimeZoneId).Add(ManualTimeOffset);
            ClockReading reading = GameClock.Read(now.TimeOfDay, cfg.ParsedDayStart, cfg.ParsedNightHoldStart);
            IsNightHold = reading.IsNightHold;

            if (!Context.IsMainPlayer)
                return;

            // Sincronizar gameTimeInterval para iluminación ambiental (ms dentro del slot actual de 10 min)
            Game1.gameTimeInterval = reading.MillisecondsIntoSlot;

            bool startup = startupSyncPending;
            startupSyncPending = false;

            if (villagerCatchUpPending)
                CatchUpVillagerSchedules(Game1.timeOfDay);

            // Si estamos en tramo nocturno congelado (NightHold), mantener la hora tope fija
            // y procesar el avance de máquinas en tiempo real cada 10 min (GAP-17).
            if (reading.IsNightHold)
            {
                if (Game1.timeOfDay != reading.TimeOfDay)
                {
                    if (startup || GameClock.TicksToReach(Game1.timeOfDay, reading.TimeOfDay) > MaxSimulatedTicks)
                    {
                        JumpClockTo(reading.TimeOfDay);
                    }
                    else
                    {
                        Game1.timeOfDay = reading.TimeOfDay;
                        Game1.netWorldState.Value.UpdateFromGame1();
                    }
                }

                // Simulación en tiempo real de máquinas cada 10 minutos reales
                int targetTicks = GameClock.NightHoldTicksElapsed(now.TimeOfDay, cfg.ParsedDayStart, cfg.ParsedNightHoldStart);
                if (startup)
                {
                    // Si el servidor arrancó ya en madrugada, sincronizar sin simular ticks pasados
                    NightHoldTicksSimulated = targetTicks;
                }
                else if (targetTicks > NightHoldTicksSimulated)
                {
                    int ticksToRun = targetTicks - NightHoldTicksSimulated;
                    for (int i = 0; i < ticksToRun; i++)
                    {
                        PassTimeForNightHold(10);
                        NightHoldTicksSimulated++;
                    }
                    context.Monitor.Log($"[RealTimeClockModule] Madrugada: procesados {ticksToRun * 10} min en máquinas (total noche: {NightHoldTicksSimulated * 10} min).", LogLevel.Trace);
                }
                else if (targetTicks < NightHoldTicksSimulated)
                {
                    // Ajuste de reloj hacia atrás
                    NightHoldTicksSimulated = targetTicks;
                }

                return;
            }

            // Calcular cuántos pasos de 10 minutos necesitamos para alcanzar la hora real actual
            int ticksNeeded = GameClock.TicksToReach(Game1.timeOfDay, reading.TimeOfDay);

            if (Game1.timeOfDay > reading.TimeOfDay)
            {
                // Si la hora del juego quedó por delante (ej. retroceso manual con /time -X o DST), sincronizar hacia atrás
                // y devolver a los aldeanos a donde estarían a esa hora
                JumpClockTo(reading.TimeOfDay);
            }
            else if (ticksNeeded > 0)
            {
                // Al arrancar el servidor o si el salto es muy grande (> 3 horas), ajustamos directamente para evitar
                // spam de eventos y recolocamos a los aldeanos donde estarían a esa hora
                if (startup || ticksNeeded > MaxSimulatedTicks)
                {
                    JumpClockTo(reading.TimeOfDay);
                }
                else
                {
                    for (int i = 0; i < ticksNeeded; i++)
                    {
                        Game1.performTenMinuteClockUpdate();
                    }
                }
            }
        }

        private void ApplyHarmonyPatches(Harmony harmony)
        {
            try
            {
                // Parche en Game1.UpdateGameClock para anular el avance automático por acumulación de milisegundos en el Host
                // (nuestro OneSecondUpdateTicked se encarga de llamar a performTenMinuteClockUpdate cuando toca).
                harmony.Patch(
                    original: AccessTools.Method(typeof(Game1), nameof(Game1.UpdateGameClock)),
                    prefix: new HarmonyMethod(typeof(RealTimeClockModule), nameof(Prefix_UpdateGameClock))
                );

                // Parche en Game1.shouldTimePass para que en multijugador no se pause el reloj por abrir menús individuales
                harmony.Patch(
                    original: AccessTools.Method(typeof(Game1), nameof(Game1.shouldTimePass)),
                    postfix: new HarmonyMethod(typeof(RealTimeClockModule), nameof(Postfix_shouldTimePass))
                );

                // Parche en Utility.CalculateMinutesUntilMorning para descontar los minutos ya simulados en NightHold (GAP-17)
                harmony.Patch(
                    original: AccessTools.Method(typeof(Utility), nameof(Utility.CalculateMinutesUntilMorning), new[] { typeof(int), typeof(int) }),
                    postfix: new HarmonyMethod(typeof(RealTimeClockModule), nameof(Postfix_CalculateMinutesUntilMorning))
                );

                // Parche en DayTimeMoneyBox.draw para mostrar la hora real más allá de 01:50 y evitar que se pinte en rojo o vibre
                harmony.Patch(
                    original: AccessTools.Method(typeof(DayTimeMoneyBox), nameof(DayTimeMoneyBox.draw), new[] { typeof(SpriteBatch) }),
                    prefix: new HarmonyMethod(typeof(RealTimeClockModule), nameof(Prefix_DayTimeMoneyBox_draw)),
                    transpiler: new HarmonyMethod(typeof(RealTimeClockModule), nameof(Transpiler_DayTimeMoneyBox_draw))
                );

                context?.Monitor.Log("[RealTimeClockModule] Harmony patches aplicados correctamente.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[RealTimeClockModule] Error al aplicar parches Harmony: {ex}", LogLevel.Error);
            }
        }

        public static bool Prefix_UpdateGameClock(GameTime time)
        {
            // Permitimos que calcule la iluminación ambiental nativa, pero reseteamos antes de que llame a performTenMinuteClockUpdate
            if (Game1.timeOfDay >= Game1.getTrulyDarkTime(Game1.currentLocation))
            {
                int num = (int)((float)(Game1.timeOfDay - Game1.timeOfDay % 100) + (float)(Game1.timeOfDay % 100 / 10) * 16.66f);
                float num2 = Math.Min(0.93f, 0.75f + ((float)(num - Game1.getTrulyDarkTime(Game1.currentLocation)) + (float)Game1.gameTimeInterval / (float)Game1.realMilliSecondsPerGameTenMinutes * 16.6f) * 0.000625f);
                Game1.outdoorLight = (Game1.IsRainingHere() ? Game1.ambientLight : Game1.eveningColor) * num2;
            }
            else if (Game1.timeOfDay >= Game1.getStartingToGetDarkTime(Game1.currentLocation))
            {
                int num3 = (int)((float)(Game1.timeOfDay - Game1.timeOfDay % 100) + (float)(Game1.timeOfDay % 100 / 10) * 16.66f);
                float num4 = Math.Min(0.93f, 0.3f + ((float)(num3 - Game1.getStartingToGetDarkTime(Game1.currentLocation)) + (float)Game1.gameTimeInterval / (float)Game1.realMilliSecondsPerGameTenMinutes * 16.6f) * 0.00225f);
                Game1.outdoorLight = (Game1.IsRainingHere() ? Game1.ambientLight : Game1.eveningColor) * num4;
            }
            else if (Game1.IsRainingHere())
            {
                Game1.outdoorLight = Game1.ambientLight * 0.3f;
            }
            else
            {
                Game1.outdoorLight = Game1.ambientLight;
            }

            // Omitimos la llamada original de UpdateGameClock para tener control total del avance horario desde OneSecondUpdateTicked
            return false;
        }

        public static void Postfix_shouldTimePass(ref bool __result)
        {
            // En servidor multijugador persistente, el tiempo debe pasar siempre salvo que haya una boda o evento crítico global
            if (Game1.IsMultiplayer && !Game1.isFestival() && (Game1.CurrentEvent == null || !Game1.CurrentEvent.isWedding))
            {
                __result = true;
            }
        }

        private static void PassTimeForNightHold(int minutes)
        {
            foreach (GameLocation location in Game1.locations)
            {
                location.passTimeForObjects(minutes);

                for (int i = 0; i < location.furniture.Count; i++)
                {
                    location.furniture[i].minutesElapsed(minutes);
                }

                foreach (var building in location.buildings)
                {
                    if (building.daysOfConstructionLeft.Value <= 0)
                    {
                        building.performTenMinuteAction(minutes);
                        GameLocation indoors = building.GetIndoors();
                        if (indoors != null)
                        {
                            indoors.passTimeForObjects(minutes);
                            for (int j = 0; j < indoors.furniture.Count; j++)
                            {
                                indoors.furniture[j].minutesElapsed(minutes);
                            }
                        }
                    }
                }
            }
        }

        public static void Postfix_CalculateMinutesUntilMorning(int daysElapsed, ref int __result)
        {
            // Descuenta los minutos que ya se simularon en tiempo real durante la madrugada
            // para evitar que el Rollover los procese dos veces (GAP-17).
            if (daysElapsed == 1 && NightHoldTicksSimulated > 0)
            {
                int alreadySimulated = NightHoldTicksSimulated * 10;
                __result = Math.Max(0, __result - alreadySimulated);
            }
        }

        /// <summary>
        /// Obtiene la hora efectiva a mostrar en el reloj del HUD (DayTimeMoneyBox).
        /// Entre 06:00 y 01:50 coincide con la hora nativa del juego; durante la Madrugada
        /// (01:50 a 06:00), avanza según la hora real (02:00, 02:10, 03:30...) en vez de quedarse fija en 1:50.
        /// </summary>
        public static int GetEffectiveDisplayTimeOfDay()
        {
            if (!Context.IsWorldReady || context == null)
                return Game1.timeOfDay;

            if (Game1.timeOfDay < GameClock.MaxTimeOfDay && !IsNightHold)
                return Game1.timeOfDay;

            var cfg = context.Config;
            DateTime now = ServerTime.LocalNow(cfg.TimeZoneId).Add(ManualTimeOffset);
            return GameClock.GetDisplayTimeOfDay(now.TimeOfDay, cfg.ParsedDayStart);
        }

        public static void Prefix_DayTimeMoneyBox_draw(DayTimeMoneyBox __instance)
        {
            // Evitar que el reloj tiemble como alarma de desmayo nocturno
            __instance.timeShakeTimer = 0;
        }

        public static IEnumerable<CodeInstruction> Transpiler_DayTimeMoneyBox_draw(IEnumerable<CodeInstruction> instructions)
        {
            var methodGetEffectiveTime = AccessTools.Method(typeof(RealTimeClockModule), nameof(GetEffectiveDisplayTimeOfDay));
            var fieldTimeOfDay = AccessTools.Field(typeof(Game1), nameof(Game1.timeOfDay));

            var list = new List<CodeInstruction>(instructions);
            for (int i = 0; i < list.Count; i++)
            {
                // 1. Reemplazar lecturas de Game1.timeOfDay por GetEffectiveDisplayTimeOfDay()
                if (list[i].LoadsField(fieldTimeOfDay))
                {
                    list[i].opcode = OpCodes.Call;
                    list[i].operand = methodGetEffectiveTime;
                }
                // 2. Anular el color rojo: cuando compara timeOfDay >= 2400 para saltar a Color.Red,
                // cambiar 2400 por int.MaxValue para que la condición nunca se cumpla y use siempre textColor
                else if (list[i].opcode == OpCodes.Ldc_I4 && list[i].operand is int val && val == 2400)
                {
                    if (i + 1 < list.Count && list[i + 1].opcode == OpCodes.Bge)
                    {
                        list[i].operand = int.MaxValue;
                    }
                }
            }
            return list;
        }
    }
}
