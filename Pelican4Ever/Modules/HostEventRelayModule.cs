using System;
using System.Collections.Generic;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Permite que los jugadores clientes disparen eventos con precondición de anfitrión (<c>/H</c>) y
    /// <c>broadcastEvent</c> cuando el servidor corre sin el modo dedicado nativo
    /// (<c>Game1.IsDedicatedHost == false</c>, p. ej. <see cref="AutoHostModule"/> sin JunimoServer).
    /// </summary>
    public sealed class HostEventRelayModule : IModule
    {
        private const string MsgWarpRelay = "Pelican4Ever.HostEventWarpRelay";
        private const int RelayCooldownTicks = 300;
        private const int MaxWaitTicks = 120;

        private sealed class WarpRelayPayload
        {
            public string LocationName { get; set; } = string.Empty;
            public int TileX { get; set; }
            public int TileY { get; set; }
            public int FacingDirection { get; set; }
        }

        private sealed class PendingRelayState
        {
            public PendingRelayState(string eventId, string returnLocation, int returnX, int returnY, int returnFacing)
            {
                EventId = eventId;
                ReturnLocation = returnLocation;
                ReturnX = returnX;
                ReturnY = returnY;
                ReturnFacing = returnFacing;
            }

            public string EventId { get; }
            public string ReturnLocation { get; }
            public int ReturnX { get; }
            public int ReturnY { get; }
            public int ReturnFacing { get; }
            public bool EventStarted { get; set; }
            public bool Skipped { get; set; }
            public int EventTicks { get; set; }
            public int WaitTicks { get; set; }
        }

        private ModContext? context;
        private PendingRelayState? activeRelay;
        private readonly Dictionary<string, int> lastRelayedTickByEvent = new(StringComparer.Ordinal);
        private int tickCounter;

        public string Name => "HostEventRelay";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;
            ctx.Helper.Events.Player.Warped += OnWarped;
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            ctx.Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            ctx.Helper.Events.GameLoop.DayStarted += OnDayStarted;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            lastRelayedTickByEvent.Clear();
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            activeRelay = null;
            lastRelayedTickByEvent.Clear();
            tickCounter = 0;
        }

        private void OnWarped(object? sender, WarpedEventArgs e)
        {
            if (context == null || !e.IsLocalPlayer || e.NewLocation == null || Game1.player == null || Game1.MasterPlayer == null)
                return;

            string locationName = e.NewLocation.NameOrUniqueName;
            if (!HostEventRelayLogic.ShouldClientNotifyWarp(Context.IsMainPlayer, Game1.HasDedicatedHost, locationName))
                return;

            var payload = new WarpRelayPayload
            {
                LocationName = locationName,
                TileX = Game1.player.TilePoint.X,
                TileY = Game1.player.TilePoint.Y,
                FacingDirection = Game1.player.FacingDirection
            };

            context.Helper.Multiplayer.SendMessage(
                payload,
                MsgWarpRelay,
                new[] { context.Manifest.UniqueID },
                new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || !Context.IsMainPlayer || !Context.IsWorldReady)
                return;

            if (e.Type != MsgWarpRelay || e.ReadAs<WarpRelayPayload>() is not { } payload)
                return;

            if (activeRelay != null)
                return;

            bool isEventActive = Game1.eventUp || Game1.CurrentEvent != null || Game1.farmEvent != null;
            if (!HostEventRelayLogic.CanHostRelayWarp(
                    Context.IsMainPlayer,
                    Game1.IsDedicatedHost,
                    context.Config.DedicatedServerMode,
                    isEventActive,
                    Game1.isWarping,
                    Game1.newDay,
                    payload.LocationName))
            {
                return;
            }

            GameLocation? location = Game1.getLocationFromName(payload.LocationName, isStructure: false);
            if (location == null || !location.TryGetLocationEvents(out _, out Dictionary<string, string>? events) || events == null)
                return;

            string? matchedEventId = FindRelayableEvent(location, payload, events);
            if (matchedEventId == null)
                return;

            if (lastRelayedTickByEvent.TryGetValue(matchedEventId, out int lastTick)
                && tickCounter - lastTick < RelayCooldownTicks)
            {
                return;
            }

            lastRelayedTickByEvent[matchedEventId] = tickCounter;
            string returnLocation = Game1.currentLocation?.NameOrUniqueName ?? "FarmHouse";
            int returnX = Game1.player.TilePoint.X;
            int returnY = Game1.player.TilePoint.Y;
            int returnFacing = Game1.player.FacingDirection;

            activeRelay = new PendingRelayState(matchedEventId, returnLocation, returnX, returnY, returnFacing);

            context.Monitor.Log(
                $"[HostEventRelay] Disparando evento /H '{matchedEventId}' en {payload.LocationName} ({payload.TileX},{payload.TileY}) por entrada del cliente {e.FromPlayerID}.",
                LogLevel.Info);

            LocationRequest request = Game1.getLocationRequest(payload.LocationName, isStructure: false);
            Game1.warpFarmer(request, payload.TileX, payload.TileY, payload.FacingDirection);
        }

        private static string? FindRelayableEvent(
            GameLocation location,
            WarpRelayPayload payload,
            Dictionary<string, string> events)
        {
            int prevX = Game1.xLocationAfterWarp;
            int prevY = Game1.yLocationAfterWarp;
            try
            {
                Game1.xLocationAfterWarp = payload.TileX;
                Game1.yLocationAfterWarp = payload.TileY;

                foreach (KeyValuePair<string, string> kvp in events)
                {
                    if (!HostEventRelayLogic.IsRelayableHostBroadcastEvent(payload.LocationName, kvp.Key, kvp.Value))
                        continue;

                    string resolvedId = location.checkEventPrecondition(kvp.Key);
                    if (!string.IsNullOrEmpty(resolvedId)
                        && resolvedId != "-1"
                        && GameLocation.IsValidLocationEvent(kvp.Key, kvp.Value))
                    {
                        return resolvedId;
                    }
                }
            }
            finally
            {
                Game1.xLocationAfterWarp = prevX;
                Game1.yLocationAfterWarp = prevY;
            }

            return null;
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            tickCounter++;
            if (activeRelay == null || !Context.IsMainPlayer || !Context.IsWorldReady || Game1.isWarping)
                return;

            if (Game1.CurrentEvent is { } currentEvent)
            {
                activeRelay.EventStarted = true;
                activeRelay.EventTicks++;

                // Esperar a que los comandos iniciales (broadcastEvent, addMailReceived, addQuest, rustyKey)
                // se ejecuten antes de saltar la escena en el host desatendido.
                if (!activeRelay.Skipped
                    && string.Equals(currentEvent.id, activeRelay.EventId, StringComparison.Ordinal)
                    && (currentEvent.skippable || Game1.activeClickableMenu is DialogueBox || activeRelay.EventTicks > MaxWaitTicks))
                {
                    currentEvent.skipEvent();
                    activeRelay.Skipped = true;
                }

                return;
            }

            if (Game1.eventUp)
                return;

            if (activeRelay.EventStarted || ++activeRelay.WaitTicks > MaxWaitTicks)
            {
                PendingRelayState finished = activeRelay;
                activeRelay = null;

                if (!string.Equals(Game1.currentLocation?.NameOrUniqueName, finished.ReturnLocation, StringComparison.Ordinal))
                {
                    LocationRequest backRequest = Game1.getLocationRequest(finished.ReturnLocation, isStructure: false);
                    Game1.warpFarmer(backRequest, finished.ReturnX, finished.ReturnY, finished.ReturnFacing);
                }
            }
        }
    }
}
