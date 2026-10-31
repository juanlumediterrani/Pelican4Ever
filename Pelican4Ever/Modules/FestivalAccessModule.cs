using System;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Festivals;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Festivales de entrada y salida libre en multijugador. Vanilla pide confirmación a todos para entrar
    /// («festivalStart»), monta la escena con salto de cámara y, al salir, pide confirmación a todos («festivalEnd»)
    /// y termina el festival para el servidor: hora a las 22:00, máquinas adelantadas, aldeanos a casa y jugadores
    /// a la granja. Aquí cada jugador entra donde llega, sin salto de cámara, y sale por el borde del mapa al mapa
    /// vecino sin tocar la hora ni a los demás.
    /// </summary>
    public sealed class FestivalAccessModule : IModule
    {
        private static ModContext? context;

        public string Name => "FestivalAccess";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;
            ctx.Helper.Events.GameLoop.TimeChanged += OnTimeChanged;
            ApplyHarmonyPatches(ctx.Harmony);
        }

        private void ApplyHarmonyPatches(Harmony harmony)
        {
            try
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(Game1), nameof(Game1.warpFarmer), new[] { typeof(LocationRequest), typeof(int), typeof(int), typeof(int) }),
                    prefix: new HarmonyMethod(typeof(FestivalAccessModule), nameof(Prefix_WarpFarmer)),
                    finalizer: new HarmonyMethod(typeof(FestivalAccessModule), nameof(Finalizer_WarpFarmer))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(Event), nameof(Event.tryToLoadFestival)),
                    postfix: new HarmonyMethod(typeof(FestivalAccessModule), nameof(Postfix_TryToLoadFestival))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(Event), nameof(Event.TryStartEndFestivalDialogue)),
                    prefix: new HarmonyMethod(typeof(FestivalAccessModule), nameof(Prefix_TryStartEndFestivalDialogue))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(Event), nameof(Event.exitEvent)),
                    prefix: new HarmonyMethod(typeof(FestivalAccessModule), nameof(Prefix_ExitEvent))
                );

                context?.Monitor.Log("[FestivalAccessModule] Parches de entrada y salida libre de festivales aplicados.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[FestivalAccessModule] Error al aplicar parches Harmony: {ex}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Game1.warpFarmer solo mira <c>weatherIcon == 1</c> para bloquear el mapa del festival antes de su hora y
        /// abrir la confirmación «festivalStart»; se oculta durante la llamada para entrar como a cualquier mapa.
        /// </summary>
        public static void Prefix_WarpFarmer(LocationRequest locationRequest, out bool __state)
        {
            __state = Game1.IsMultiplayer && Game1.weatherIcon == 1 && Game1.whereIsTodaysFest != null
                && locationRequest?.Name == Game1.whereIsTodaysFest;
            if (__state)
                Game1.weatherIcon = 0;
        }

        public static Exception? Finalizer_WarpFarmer(Exception? __exception, bool __state)
        {
            if (__state)
                Game1.weatherIcon = 1;
            return __exception;
        }

        /// <summary>El guion de montaje deja al jugador donde ha llegado y con la cámara sobre él.</summary>
        public static void Postfix_TryToLoadFestival(bool __result, ref Event ev)
        {
            if (!__result || ev?.eventCommands == null || !Game1.IsMultiplayer)
                return;

            FestivalParticipationModule.ReportPresence(true);

            Point tile = Game1.player.TilePoint;
            if (IsBlockedInFestivalMap(ev.eventCommands, tile))
            {
                context?.Monitor.Log($"[FestivalAccessModule] La casilla de llegada {tile} está ocupada en el mapa del festival; se usa la entrada vanilla.", LogLevel.Trace);
                return;
            }

            ev.eventCommands = FestivalEntryScript.KeepPlayerInPlace(ev.eventCommands, tile.X, tile.Y, Game1.player.FacingDirection);
        }

        private static bool IsBlockedInFestivalMap(string[] commands, Point tile)
        {
            string? mapName = FestivalEntryScript.TemporaryMapName(commands);
            if (mapName == null)
                return false;

            try
            {
                var buildings = Game1.content.Load<xTile.Map>("Maps\\" + mapName).GetLayer("Buildings");
                if (buildings == null)
                    return false;

                return tile.X < 0 || tile.Y < 0 || tile.X >= buildings.LayerWidth || tile.Y >= buildings.LayerHeight
                    || buildings.Tiles[tile.X, tile.Y] != null;
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[FestivalAccessModule] No se pudo leer el mapa del festival {mapName}: {ex.Message}", LogLevel.Trace);
                return true;
            }
        }

        /// <summary>
        /// Al llegar al borde del mapa del festival, el jugador sale por el warp más cercano del mapa real sin la
        /// confirmación «festivalEnd». Se apaga <c>isFestival</c> antes de terminar el evento para que exitEvent no
        /// fije las 22:00, ni adelante las máquinas, ni mande a casa a los aldeanos, ni lleve al jugador a la granja;
        /// tampoco se envía «endFest», que echaría del festival a todos los clientes.
        /// </summary>
        public static bool Prefix_TryStartEndFestivalDialogue(Event __instance, Farmer who, ref bool __result)
        {
            if (!Game1.IsMultiplayer || !__instance.isFestival || who == null || !who.IsLocalPlayer)
                return true;

            GameLocation? festivalLocation = Game1.getLocationFromName(Game1.whereIsTodaysFest ?? Game1.currentLocation?.Name);
            Point tile = who.TilePoint;
            Warp? exit = festivalLocation?.warps
                .OrderBy(w => Vector2.DistanceSquared(new Vector2(w.X, w.Y), new Vector2(tile.X, tile.Y)))
                .FirstOrDefault();
            if (exit == null)
                return true;

            who.Halt();
            who.Position = who.lastPosition;

            __instance.isFestival = false;
            __instance.setExitLocation(exit.TargetName, exit.TargetX, exit.TargetY);
            FestivalParticipationModule.OnFestivalEventEnded();
            Game1.currentMinigame = null;
            Game1.exitActiveMenu();
            __instance.endBehaviors();
            Game1.changeMusicTrack("none");

            context?.Monitor.Log($"[FestivalAccessModule] {who.Name} sale del festival hacia {exit.TargetName} ({exit.TargetX}, {exit.TargetY}).", LogLevel.Trace);
            __result = true;
            return false;
        }

        /// <summary>
        /// Final del evento principal (orden <c>end</c> del guion): en vez de cerrar el festival para el servidor, el
        /// jugador vuelve al mapa real del festival en su casilla y, al recargarse, sigue en el festival en su sitio.
        /// </summary>
        public static void Prefix_ExitEvent(Event __instance)
        {
            if (!Game1.IsMultiplayer || !__instance.isFestival)
                return;

            __instance.isFestival = false;
            Point tile = Game1.player.TilePoint;
            __instance.setExitLocation(Game1.whereIsTodaysFest ?? Game1.currentLocation.Name, tile.X, tile.Y);
            FestivalParticipationModule.OnFestivalEventEnded();
            context?.Monitor.Log($"[FestivalAccessModule] {Game1.player.Name} termina el evento del festival sin cerrarlo para el servidor.", LogLevel.Trace);
        }

        /// <summary>Si el festival abre con el jugador ya dentro del mapa, empieza en ese momento y en su sitio.</summary>
        private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
        {
            if (!Context.IsWorldReady || !Game1.IsMultiplayer || Game1.eventUp || Game1.farmEvent != null
                || Game1.activeClickableMenu != null || Game1.isWarping)
                return;

            GameLocation? location = Game1.currentLocation;
            if (location == null || location.currentEvent != null || location.Name != Game1.whereIsTodaysFest)
                return;

            if (Event.tryToLoadFestival($"{Game1.currentSeason}{Game1.dayOfMonth}", out Event festival))
                location.startEvent(festival);
        }
    }
}
