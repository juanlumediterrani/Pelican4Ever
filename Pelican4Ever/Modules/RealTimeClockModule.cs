using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Time;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Network.Dedicated;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Sincroniza el reloj del juego 1:1 con la hora real y evita pausas indeseadas (TASK-101 / ADR-0002).
    /// </summary>
    public sealed class RealTimeClockModule : IModule
    {
        private static ModContext? context;
        public static bool IsNightHold { get; private set; }

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

            // Parches Harmony
            ApplyHarmonyPatches(ctx.Harmony);
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
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
            if (!Context.IsWorldReady || !Context.IsMainPlayer || context == null)
                return;

            var cfg = context.Config;
            DateTime now = ServerTime.LocalNow(cfg.TimeZoneId);
            ClockReading reading = GameClock.Read(now.TimeOfDay, cfg.ParsedDayStart, cfg.ParsedNightHoldStart);
            IsNightHold = reading.IsNightHold;

            // Sincronizar gameTimeInterval para iluminación ambiental (ms dentro del slot actual de 10 min)
            Game1.gameTimeInterval = reading.MillisecondsIntoSlot;

            // Si estamos en tramo nocturno congelado (NightHold), mantener la hora tope fija
            if (reading.IsNightHold)
            {
                if (Game1.timeOfDay != reading.TimeOfDay)
                {
                    Game1.timeOfDay = reading.TimeOfDay;
                    Game1.netWorldState.Value.UpdateFromGame1();
                }
                return;
            }

            // Calcular cuántos pasos de 10 minutos necesitamos para alcanzar la hora real actual
            int ticksNeeded = GameClock.TicksToReach(Game1.timeOfDay, reading.TimeOfDay);

            // Si por alguna razón la hora del juego está desfasada (o al cargar la partida por primera vez)
            if (ticksNeeded > 0)
            {
                // Si el salto es muy grande (> 3 horas = 18 ticks), ajustamos directamente para evitar spam de eventos
                if (ticksNeeded > 18)
                {
                    Game1.timeOfDay = reading.TimeOfDay;
                    Game1.netWorldState.Value.UpdateFromGame1();
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
    }
}
