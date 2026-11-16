using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.Security;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Mensaje de red entre cliente y servidor cuando <see cref="VisitorGuardModule"/> bloquea una acción en el cliente.
    /// </summary>
    public sealed class VisitorDeniedAlertMessage
    {
        public string LocationName { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
    }

    /// <summary>
    /// Registra telemetría conductual ligera en el anfitrión para el periodo de prueba autónomo de Putre:
    /// sesiones/conexiones, bloqueos de <see cref="VisitorGuardModule"/> y abuso de chat (flood o inyección de etiquetas).
    /// </summary>
    public sealed class ProbationTelemetryModule : IModule
    {
        public const string VisitorDeniedMessageType = "Pelican4Ever.VisitorDeniedAlert";

        private static ModContext? context;
        private static readonly Dictionary<long, DateTimeOffset> activeSessions = new();
        private static readonly Dictionary<long, Queue<DateTimeOffset>> recentChatTimestamps = new();
        private static readonly Dictionary<long, DateTimeOffset> lastFloodHitUtc = new();
        private static readonly Dictionary<long, DateTimeOffset> lastVisitorEventUtc = new();
        private static readonly Dictionary<long, DateTimeOffset> lastChatAbuseEventUtc = new();

        public string Name => "ProbationTelemetry";

        public ModuleSide Side => ModuleSide.Host;

        public void Register(ModContext ctx)
        {
            context = ctx;
            ctx.Helper.Events.GameLoop.SaveLoaded += OnResetState;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += OnResetState;
            ctx.Helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
            ctx.Helper.Events.Multiplayer.PeerDisconnected += OnPeerDisconnected;
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;

            MethodInfo? receiveChat = AccessTools.Method(
                typeof(ChatBox),
                nameof(ChatBox.receiveChatMessage),
                new[] { typeof(long), typeof(int), typeof(LocalizedContentManager.LanguageCode), typeof(string) });

            if (receiveChat != null)
            {
                ctx.Harmony.Patch(
                    original: receiveChat,
                    postfix: new HarmonyMethod(typeof(ProbationTelemetryModule), nameof(Postfix_ReceiveChatMessage)));
            }
            else
            {
                ctx.Monitor.Log("[ProbationTelemetry] No se pudo localizar ChatBox.receiveChatMessage para telemetría de chat.", LogLevel.Warn);
            }
        }

        /// <summary>
        /// Devuelve la duración en segundos de la sesión abierta actual (si el jugador sigue conectado).
        /// </summary>
        public static long GetLiveSessionSeconds(long playerId, DateTimeOffset nowUtc)
        {
            if (activeSessions.TryGetValue(playerId, out DateTimeOffset start) && start > DateTimeOffset.MinValue)
                return Math.Max(0, (long)(nowUtc - start).TotalSeconds);

            return 0;
        }

        private void OnResetState(object? sender, EventArgs e)
        {
            activeSessions.Clear();
            recentChatTimestamps.Clear();
            lastFloodHitUtc.Clear();
            lastVisitorEventUtc.Clear();
            lastChatAbuseEventUtc.Clear();
        }

        private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null || !context.IsModEnabled)
                return;

            long playerId = e.Peer.PlayerID;
            if (playerId <= 0 || playerId == Game1.player?.UniqueMultiplayerID)
                return;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            Farmer? farmer = Game1.GetPlayer(playerId);
            PlayerRecord record = context.WorldState.GetOrCreatePlayer(playerId, farmer?.Name ?? $"Farmer-{playerId}");
            record.Behavior ??= new PlayerBehaviorMetrics();
            record.Behavior.ConnectCount++;
            record.Behavior.LastConnectedUtc = now;
            activeSessions[playerId] = now;
        }

        private void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null)
                return;

            long playerId = e.Peer.PlayerID;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (activeSessions.Remove(playerId, out DateTimeOffset start) &&
                context.WorldState.Players.TryGetValue(playerId, out PlayerRecord? record) &&
                record != null)
            {
                record.Behavior ??= new PlayerBehaviorMetrics();
                record.Behavior.TotalOnlineSeconds += Math.Max(0, (long)(now - start).TotalSeconds);
            }

            recentChatTimestamps.Remove(playerId);
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null || !context.IsModEnabled)
                return;

            if (e.FromModID != context.Manifest.UniqueID || e.Type != VisitorDeniedMessageType)
                return;

            long playerId = e.FromPlayerID;
            if (playerId <= 0 || playerId == Game1.player?.UniqueMultiplayerID)
                return;

            VisitorDeniedAlertMessage alert;
            try
            {
                alert = e.ReadAs<VisitorDeniedAlertMessage>();
            }
            catch
            {
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            string location = string.IsNullOrWhiteSpace(alert.LocationName) ? "desconocida" : alert.LocationName.Trim();
            string summary = string.IsNullOrWhiteSpace(alert.Summary) ? $"Bloqueo de guardia de visitante en {location}" : alert.Summary.Trim();

            Farmer? farmer = Game1.GetPlayer(playerId);
            PlayerRecord record = context.WorldState.GetOrCreatePlayer(playerId, farmer?.Name ?? $"Farmer-{playerId}");
            record.Behavior ??= new PlayerBehaviorMetrics();
            record.Behavior.VisitorDeniedCount++;
            record.Behavior.LastVisitorDeniedSummary = $"{location}: {summary}";
            record.Behavior.LastVisitorDeniedAtUtc = now;

            if (!lastVisitorEventUtc.TryGetValue(playerId, out DateTimeOffset lastLog) || (now - lastLog) >= TimeSpan.FromSeconds(5))
            {
                lastVisitorEventUtc[playerId] = now;
                int today = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;
                int time = Context.IsWorldReady ? Game1.timeOfDay : 600;
                AdminApiModule.EventLog.Append(
                    HostEventTypes.VisitorDenied,
                    today,
                    time,
                    $"Guardia de visitante bloqueó a {record.PlayerName} ({playerId}) en {location}.",
                    new Dictionary<string, object?>
                    {
                        ["playerId"] = playerId,
                        ["playerName"] = record.PlayerName,
                        ["location"] = location,
                        ["count"] = record.Behavior.VisitorDeniedCount
                    });
            }
        }

        private static void Postfix_ReceiveChatMessage(
            long sourceFarmer,
            int chatKind,
            LocalizedContentManager.LanguageCode language,
            string message)
        {
            try
            {
                if (!Context.IsMainPlayer || context == null || !context.IsModEnabled || string.IsNullOrEmpty(message))
                    return;

                if (sourceFarmer <= 0 || (Game1.player != null && sourceFarmer == Game1.player.UniqueMultiplayerID))
                    return;

                DateTimeOffset now = DateTimeOffset.UtcNow;
                Farmer? farmer = Game1.GetPlayer(sourceFarmer);
                PlayerRecord record = context.WorldState.GetOrCreatePlayer(sourceFarmer, farmer?.Name ?? $"Farmer-{sourceFarmer}");
                record.Behavior ??= new PlayerBehaviorMetrics();
                record.Behavior.ChatMessageCount++;

                bool tagAbuse = ProbationAndBlacklistLogic.IsSuspiciousChatTagInjection(message);
                if (tagAbuse)
                    record.Behavior.ChatTagInjectionHits++;

                if (!recentChatTimestamps.TryGetValue(sourceFarmer, out Queue<DateTimeOffset>? window))
                {
                    window = new Queue<DateTimeOffset>();
                    recentChatTimestamps[sourceFarmer] = window;
                }

                window.Enqueue(now);
                while (window.Count > 0 && (now - window.Peek()) > TimeSpan.FromSeconds(8))
                    window.Dequeue();

                bool floodHit = false;
                if (window.Count >= 6)
                {
                    if (!lastFloodHitUtc.TryGetValue(sourceFarmer, out DateTimeOffset lastFlood) || (now - lastFlood) >= TimeSpan.FromSeconds(4))
                    {
                        lastFloodHitUtc[sourceFarmer] = now;
                        record.Behavior.ChatFloodHits++;
                        floodHit = true;
                    }
                }

                if ((tagAbuse || floodHit) &&
                    (!lastChatAbuseEventUtc.TryGetValue(sourceFarmer, out DateTimeOffset lastLog) || (now - lastLog) >= TimeSpan.FromSeconds(5)))
                {
                    lastChatAbuseEventUtc[sourceFarmer] = now;
                    int today = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;
                    int time = Context.IsWorldReady ? Game1.timeOfDay : 600;
                    string kind = tagAbuse && floodHit ? "flood+tags" : (tagAbuse ? "tags" : "flood");
                    AdminApiModule.EventLog.Append(
                        HostEventTypes.ChatAbuse,
                        today,
                        time,
                        $"Abuso de chat ({kind}) de {record.PlayerName} ({sourceFarmer}).",
                        new Dictionary<string, object?>
                        {
                            ["playerId"] = sourceFarmer,
                            ["playerName"] = record.PlayerName,
                            ["kind"] = kind,
                            ["floodHits"] = record.Behavior.ChatFloodHits,
                            ["tagHits"] = record.Behavior.ChatTagInjectionHits
                        });
                }
            }
            catch
            {
                // Nunca interrumpir el receptor de chat por fallos de telemetría
            }
        }
    }
}
