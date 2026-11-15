using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Networking;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Expulsión por inactividad cuando el servidor supera la mitad de su capacidad
    /// (ADR-0010, TASK-2403).
    /// </summary>
    public sealed class IdleKickModule : IModule
    {
        private const string ActivityHeartbeatMsgType = "IdleKick.Activity";
        private const string IdleWarningMsgType = "IdleKick.Warning";

        private sealed class ActivityPayload
        {
            public long TimestampUnixMs { get; set; }
        }

        private sealed class WarningPayload
        {
            public int WarningMinutes { get; set; }
        }

        public static IdleKickLogic Logic { get; } = new();
        public static int TotalIdleKicks { get; private set; }

        private ModContext? context;
        private readonly Dictionary<long, (string LocationName, Point Tile)> lastObservedPositions = new();
        private DateTimeOffset lastClientActivitySentUtc = DateTimeOffset.MinValue;
        private bool localHasPendingClientActivity;

        public string Name => "IdleKick";
        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;
            Logic.Configure(
                ctx.Config.MaxConcurrentPlayers,
                ctx.Config.IdleKickMinutes,
                ctx.Config.IdleKickWarningMinutes);

            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            ctx.Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            ctx.Helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
            ctx.Helper.Events.Multiplayer.PeerDisconnected += OnPeerDisconnected;
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            ctx.Helper.Events.Input.ButtonPressed += OnInputButtonPressed;
            ctx.Helper.Events.Input.MouseWheelScrolled += OnMouseWheelScrolled;
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            lastObservedPositions.Clear();
            Logic.Clear();
            TotalIdleKicks = 0;

            if (context != null)
            {
                Logic.Configure(
                    context.Config.MaxConcurrentPlayers,
                    context.Config.IdleKickMinutes,
                    context.Config.IdleKickWarningMinutes);
            }
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            lastObservedPositions.Clear();
            Logic.Clear();
            localHasPendingClientActivity = false;
        }

        private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
        {
            if (!Context.IsMainPlayer || e.Peer.IsHost)
                return;

            Logic.RecordActivity(e.Peer.PlayerID, DateTimeOffset.UtcNow);
        }

        private void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
        {
            if (!Context.IsMainPlayer || e.Peer.IsHost)
                return;

            lastObservedPositions.Remove(e.Peer.PlayerID);
            Logic.RemovePlayer(e.Peer.PlayerID);
        }

        private void OnInputButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || Context.IsMainPlayer)
                return;

            localHasPendingClientActivity = true;
        }

        private void OnMouseWheelScrolled(object? sender, MouseWheelScrolledEventArgs e)
        {
            if (!Context.IsWorldReady || Context.IsMainPlayer)
                return;

            localHasPendingClientActivity = true;
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || e.FromModID != context.Helper.ModRegistry.ModID)
                return;

            if (Context.IsMainPlayer && e.Type == ActivityHeartbeatMsgType)
            {
                if (e.FromPlayerID != 0 && e.FromPlayerID != Game1.MasterPlayer?.UniqueMultiplayerID)
                {
                    Logic.RecordActivity(e.FromPlayerID, DateTimeOffset.UtcNow);
                }
                return;
            }

            if (!Context.IsMainPlayer && e.Type == IdleWarningMsgType)
            {
                var payload = e.ReadAs<WarningPayload>();
                int minutes = payload?.WarningMinutes ?? context.Config.IdleKickWarningMinutes;
                string fallback = $"Aviso de inactividad: serás desconectado en {minutes} min si no realizas ninguna acción.";
                var trans = context.Helper.Translation.Get("idle.warning_hud", new { minutes });
                string msg = trans.HasValue() ? trans.ToString() : fallback;
                Game1.addHUDMessage(new HUDMessage(msg, HUDMessage.error_type));
                Game1.chatBox?.addInfoMessage(msg);
            }
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (context == null || !Context.IsWorldReady)
                return;

            if (!Context.IsMainPlayer)
            {
                if (localHasPendingClientActivity && e.IsMultipleOf(60))
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    if ((now - lastClientActivitySentUtc).TotalSeconds >= 15)
                    {
                        lastClientActivitySentUtc = now;
                        localHasPendingClientActivity = false;
                        context.Helper.Multiplayer.SendMessage(
                            new ActivityPayload { TimestampUnixMs = now.ToUnixTimeMilliseconds() },
                            ActivityHeartbeatMsgType,
                            modIDs: new[] { context.Helper.ModRegistry.ModID },
                            playerIDs: new[] { Game1.MasterPlayer.UniqueMultiplayerID });
                    }
                }
                return;
            }

            // Host: observar movimiento/herramientas cada ~1s
            if (e.IsMultipleOf(60))
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                long hostId = Game1.MasterPlayer?.UniqueMultiplayerID ?? 0;
                foreach (Farmer farmer in Game1.getOnlineFarmers())
                {
                    if (farmer == null || farmer.UniqueMultiplayerID == hostId)
                        continue;

                    long pid = farmer.UniqueMultiplayerID;
                    string locName = farmer.currentLocation?.NameOrUniqueName ?? string.Empty;
                    Point tile = farmer.TilePoint;

                    if (!lastObservedPositions.TryGetValue(pid, out var prev) ||
                        prev.LocationName != locName ||
                        prev.Tile != tile ||
                        farmer.UsingTool)
                    {
                        lastObservedPositions[pid] = (locName, tile);
                        Logic.RecordActivity(pid, now);
                    }
                }
            }

            // Host: evaluar expulsiones cada ~5s
            if (e.IsMultipleOf(300))
            {
                long hostId = Game1.MasterPlayer?.UniqueMultiplayerID ?? 0;
                int connectedClients = Game1.getOnlineFarmers().Count(f => f != null && f.UniqueMultiplayerID != hostId);
                var actions = Logic.Evaluate(connectedClients, DateTimeOffset.UtcNow);
                foreach (var action in actions)
                {
                    if (action.ActionType == IdleActionType.Warn)
                    {
                        context.Helper.Multiplayer.SendMessage(
                            new WarningPayload { WarningMinutes = context.Config.IdleKickWarningMinutes },
                            IdleWarningMsgType,
                            modIDs: new[] { context.Helper.ModRegistry.ModID },
                            playerIDs: new[] { action.PlayerId });
                    }
                    else if (action.ActionType == IdleActionType.Kick)
                    {
                        ExecuteIdleKick(action.PlayerId, action.IdleDuration);
                    }
                }
            }
        }

        private void ExecuteIdleKick(long playerId, TimeSpan idleDuration)
        {
            if (context == null)
                return;

            Farmer? target = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == playerId);
            string playerName = target?.Name ?? playerId.ToString();

            TotalIdleKicks++;
            ClientSecurityModule.MarkPlayerDisconnectClassification(playerId, DisconnectClassification.IdleKick);
            PersistenceModule.FlushToSaveData(context, "IdleKick");
            Logic.RemovePlayer(playerId);
            lastObservedPositions.Remove(playerId);

            string logMsg = $"Jugador '{playerName}' ({playerId}) expulsado por inactividad ({(int)idleDuration.TotalMinutes} min).";
            context.Monitor.Log($"[IdleKick] {logMsg}", LogLevel.Info);
            AdminApiModule.EventLog.Append("player_idle_kicked", Game1.dayOfMonth, Game1.timeOfDay, logMsg, new Dictionary<string, object?>
            {
                ["playerId"] = playerId,
                ["playerName"] = playerName,
                ["idleMinutes"] = (int)idleDuration.TotalMinutes
            });

            Game1.server?.kick(playerId);
        }
    }
}
