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
    /// Incluye además la migración v0.4.9 para rescatar eventos cuyo hito ya se alcanzó mientras
    /// estuvieron bloqueados (Centro Cívico <c>Town:611439</c>, Llave Oxidada <c>Farm:66</c> y ceremonias).
    /// </summary>
    public sealed class HostEventRelayModule : IModule
    {
        private const string MsgWarpRelay = "Pelican4Ever.HostEventWarpRelay";
        private const string MsgSyncUnlockedEvents = "Pelican4Ever.SyncUnlockedHostEvents";
        private const int RelayCooldownTicks = 300;
        private const int MaxWaitTicks = 120;

        private sealed class WarpRelayPayload
        {
            public string LocationName { get; set; } = string.Empty;
            public int TileX { get; set; }
            public int TileY { get; set; }
            public int FacingDirection { get; set; }
        }

        private sealed class SyncUnlockedEventsPayload
        {
            public bool UnlockCcDoor { get; set; }
            public bool UnlockRustyKey { get; set; }
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
            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.GameLoop.DayStarted += OnDayStarted;
            ctx.Helper.Events.GameLoop.TimeChanged += OnTimeChanged;
            ctx.Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            ctx.Helper.Events.Player.Warped += OnWarped;
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            ctx.Helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            RunMigrationOrFallback("carga de partida");
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            lastRelayedTickByEvent.Clear();
            if (!Context.IsMainPlayer)
                return;

            RunMigrationOrFallback("inicio del día");
        }

        private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            RunMigrationOrFallback("hora del juego");
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            activeRelay = null;
            lastRelayedTickByEvent.Clear();
            tickCounter = 0;
        }

        private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
        {
            if (context == null || !Context.IsMainPlayer || !Context.IsWorldReady || Game1.MasterPlayer == null)
                return;

            bool ccUnlocked = !Game1.MasterPlayer.mailReceived.Contains(HostEventMigrationLogic.JojaMemberMailId)
                && Game1.MasterPlayer.mailReceived.Contains(HostEventMigrationLogic.CcDoorUnlockMailId);
            bool rustyKeyUnlocked = Game1.MasterPlayer.mailReceived.Contains(HostEventMigrationLogic.HasRustyKeyMailId);

            if (!ccUnlocked && !rustyKeyUnlocked)
                return;

            SendSyncPayload(new[] { e.Peer.PlayerID }, ccUnlocked, rustyKeyUnlocked);
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
            if (context == null || !Context.IsWorldReady)
                return;

            if (!Context.IsMainPlayer && e.Type == MsgSyncUnlockedEvents && Game1.MasterPlayer != null && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
            {
                if (e.ReadAs<SyncUnlockedEventsPayload>() is { } syncPayload)
                    ApplyClientSync(syncPayload);
                return;
            }

            if (!Context.IsMainPlayer)
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

        private void RunMigrationOrFallback(string origin)
        {
            if (context == null || !Context.IsWorldReady || Game1.MasterPlayer == null || Game1.netWorldState?.Value == null)
                return;

            Farmer master = Game1.MasterPlayer;
            List<Farmer> claimedOfflineFarmhands = CollectClaimedOfflineFarmhands();
            List<Farmer> allClaimedFarmers = CollectAllClaimedFarmers(master, claimedOfflineFarmhands);

            var input = new HostEventMigrationInput
            {
                HostEventMigrationVersion = context.WorldState.HostEventMigrationVersion,
                DaysPlayed = Game1.stats?.DaysPlayed ?? 0,
                TimeOfDay = Game1.timeOfDay,
                IsWeatherSunny = !Game1.isRaining && !Game1.isSnowing && !Game1.isLightning && !Game1.isDebrisWeather,
                IsJojaMember = master.mailReceived.Contains(HostEventMigrationLogic.JojaMemberMailId),
                HasCcDoorUnlock = master.mailReceived.Contains(HostEventMigrationLogic.CcDoorUnlockMailId),
                AnyPlayerHasCcScrollOrText = AnyFarmerHasMail(allClaimedFarmers, HostEventMigrationLogic.SeenJunimoNoteMailId)
                    || AnyFarmerHasMail(allClaimedFarmers, HostEventMigrationLogic.CanReadJunimoTextMailId),
                HasCcIsCompleteMail = master.mailReceived.Contains(HostEventMigrationLogic.CcIsCompleteMailId),
                AnyPlayerSeenCcCeremony = AnyFarmerSeenEvent(allClaimedFarmers, HostEventMigrationLogic.CcCeremonyEventId),
                AnyPlayerSeenJojaCeremony = AnyFarmerSeenEvent(allClaimedFarmers, HostEventMigrationLogic.JojaCeremonyEventId),
                HasRustyKey = master.mailReceived.Contains(HostEventMigrationLogic.HasRustyKeyMailId),
                MuseumPiecesCount = Game1.netWorldState.Value.MuseumPieces.Length,
                AnyPlayerSeenMuseumRustyTrigger = AnyFarmerSeenEvent(allClaimedFarmers, HostEventMigrationLogic.MuseumRustyTriggerEventId)
            };

            HostEventMigrationPlan plan = HostEventMigrationLogic.EvaluateMigration(input);
            if (!plan.HasAnyWorldChange && !plan.StampMigrationVersion)
                return;

            var applied = new List<string>();

            if (plan.UnlockCommunityCenter)
            {
                ApplyCommunityCenterUnlock(master, claimedOfflineFarmhands);
                applied.Add("CentroCívico(611439/ccDoorUnlock)");
            }

            if (plan.UnlockRustyKey)
            {
                ApplyRustyKeyUnlock(master, claimedOfflineFarmhands);
                applied.Add("LlaveOxidada(66/HasRustyKey)");
            }

            if (plan.MarkCcCeremonySeen)
            {
                master.eventsSeen.Add(HostEventMigrationLogic.CcCeremonyEventId);
                applied.Add("CeremoniaCC(191393)");
            }

            if (plan.MarkJojaCeremonySeen)
            {
                master.eventsSeen.Add(HostEventMigrationLogic.JojaCeremonyEventId);
                applied.Add("CeremoniaJoja(502261)");
            }

            if (plan.StampMigrationVersion)
            {
                context.WorldState.HostEventMigrationVersion = HostEventMigrationLogic.CurrentMigrationVersion;
            }

            if (plan.HasAnyWorldChange)
            {
                context.Monitor.Log(
                    $"[HostEventRelay] Migración/rescate de eventos /H aplicado ({origin}, Día {input.DaysPlayed}, hora {input.TimeOfDay}): {string.Join(", ", applied)}.",
                    LogLevel.Info);

                if (plan.UnlockCommunityCenter || plan.UnlockRustyKey)
                    BroadcastSyncToConnectedPeers(plan.UnlockCommunityCenter, plan.UnlockRustyKey);
            }

            if (plan.HasAnyWorldChange || plan.StampMigrationVersion)
            {
                PersistenceModule.FlushToSaveData(context, $"HostEventMigration v0.4.9 ({origin})");
            }
        }

        private static void ApplyCommunityCenterUnlock(Farmer master, List<Farmer> claimedOfflineFarmhands)
        {
            master.mailReceived.Add(HostEventMigrationLogic.CcDoorUnlockMailId);
            bool hostNewlySeen = master.eventsSeen.Add(HostEventMigrationLogic.CcUnlockEventId);
            GrantRatProblemQuestIfEligible(master, !hostNewlySeen);

            foreach (Farmer farmhand in claimedOfflineFarmhands)
            {
                farmhand.mailReceived.Add(HostEventMigrationLogic.CcDoorUnlockMailId);
                bool newlySeen = farmhand.eventsSeen.Add(HostEventMigrationLogic.CcUnlockEventId);
                GrantRatProblemQuestIfEligible(farmhand, !newlySeen);
            }
        }

        private static void ApplyRustyKeyUnlock(Farmer master, List<Farmer> claimedOfflineFarmhands)
        {
            master.mailReceived.Add(HostEventMigrationLogic.HasRustyKeyMailId);
            master.eventsSeen.Add(HostEventMigrationLogic.RustyKeyEventId);

            foreach (Farmer farmhand in claimedOfflineFarmhands)
            {
                farmhand.mailReceived.Add(HostEventMigrationLogic.HasRustyKeyMailId);
                farmhand.eventsSeen.Add(HostEventMigrationLogic.RustyKeyEventId);
            }
        }

        private static void GrantRatProblemQuestIfEligible(Farmer farmer, bool alreadySawCcEvent)
        {
            if (HostEventMigrationLogic.ShouldGrantRatProblemQuest(
                alreadySawCcUnlockEvent: alreadySawCcEvent,
                hasSeenJunimoNote: farmer.mailReceived.Contains(HostEventMigrationLogic.SeenJunimoNoteMailId),
                canReadJunimoText: farmer.mailReceived.Contains(HostEventMigrationLogic.CanReadJunimoTextMailId),
                hasQuest26: farmer.hasQuest(HostEventMigrationLogic.RatProblemQuestId)))
            {
                farmer.addQuest(HostEventMigrationLogic.RatProblemQuestId);
            }
        }

        private void ApplyClientSync(SyncUnlockedEventsPayload payload)
        {
            if (Game1.player == null)
                return;

            if (payload.UnlockCcDoor)
            {
                bool newlySeen = Game1.player.eventsSeen.Add(HostEventMigrationLogic.CcUnlockEventId);
                Game1.player.mailReceived.Add(HostEventMigrationLogic.CcDoorUnlockMailId);
                GrantRatProblemQuestIfEligible(Game1.player, !newlySeen);
            }

            if (payload.UnlockRustyKey)
            {
                Game1.player.eventsSeen.Add(HostEventMigrationLogic.RustyKeyEventId);
                Game1.player.mailReceived.Add(HostEventMigrationLogic.HasRustyKeyMailId);
            }
        }

        private void BroadcastSyncToConnectedPeers(bool unlockCcDoor, bool unlockRustyKey)
        {
            if (Game1.otherFarmers == null || Game1.otherFarmers.Count == 0)
                return;

            var peerIds = new List<long>();
            foreach (Farmer other in Game1.otherFarmers.Values)
            {
                if (other != null && other.UniqueMultiplayerID != Game1.MasterPlayer.UniqueMultiplayerID)
                    peerIds.Add(other.UniqueMultiplayerID);
            }

            if (peerIds.Count > 0)
                SendSyncPayload(peerIds.ToArray(), unlockCcDoor, unlockRustyKey);
        }

        private void SendSyncPayload(long[] peerIds, bool unlockCcDoor, bool unlockRustyKey)
        {
            if (context == null || peerIds.Length == 0)
                return;

            context.Helper.Multiplayer.SendMessage(
                new SyncUnlockedEventsPayload
                {
                    UnlockCcDoor = unlockCcDoor,
                    UnlockRustyKey = unlockRustyKey
                },
                MsgSyncUnlockedEvents,
                new[] { context.Manifest.UniqueID },
                peerIds);
        }

        private List<Farmer> CollectClaimedOfflineFarmhands()
        {
            var result = new List<Farmer>();
            if (context == null)
                return result;

            foreach (Farmer farmhand in Game1.getOfflineFarmhands())
            {
                if (farmhand == null)
                    continue;
                if (farmhand.isUnclaimedFarmhand && !context.WorldState.Players.ContainsKey(farmhand.UniqueMultiplayerID))
                    continue;

                result.Add(farmhand);
            }

            return result;
        }

        private static List<Farmer> CollectAllClaimedFarmers(Farmer master, List<Farmer> claimedOfflineFarmhands)
        {
            var result = new List<Farmer> { master };
            if (Game1.otherFarmers != null)
            {
                foreach (Farmer other in Game1.otherFarmers.Values)
                {
                    if (other != null && other.UniqueMultiplayerID != master.UniqueMultiplayerID)
                        result.Add(other);
                }
            }

            result.AddRange(claimedOfflineFarmhands);
            return result;
        }

        private static bool AnyFarmerHasMail(List<Farmer> farmers, string mailId)
        {
            foreach (Farmer farmer in farmers)
            {
                if (farmer.mailReceived.Contains(mailId))
                    return true;
            }

            return false;
        }

        private static bool AnyFarmerSeenEvent(List<Farmer> farmers, string eventId)
        {
            foreach (Farmer farmer in farmers)
            {
                if (farmer.eventsSeen.Contains(eventId))
                    return true;
            }

            return false;
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
