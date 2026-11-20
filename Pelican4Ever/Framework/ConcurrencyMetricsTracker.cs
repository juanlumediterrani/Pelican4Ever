using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Lidgren.Network;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.World;
using Pelican4Ever.Modules;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Network;

namespace Pelican4Ever.Framework
{
    /// <summary>
    /// Captura de telemetría de concurrencia en ventanas de 10 s con bajo sobrecoste (<c>TASK-2601</c>):
    /// mensajes y bytes por tipo/receptor, origen (host vs reenvío), tráfico de transporte, colas pendientes,
    /// tiempos de tick, recorrido de raíces, ocupación por mapa y tiempo hasta jugar.
    /// </summary>
    public static class ConcurrencyMetricsTracker
    {
        private const double WindowDurationSeconds = 10.0;
        private const int MaxStoredWindows = 180; // 30 minutos de historial en ventanas de 10 s

        private static readonly object HistorySync = new();
        private static readonly List<ConcurrencyWindowSnapshot> RecentWindows = new(MaxStoredWindows);
        private static readonly ConcurrentDictionary<string, long> ConnectionHandshakeStartTicks = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<long, long> PlayerHandshakeStartTicks = new();

        private static ModContext? context;
        private static bool initialized;
        private static bool patchesApplied;
        private static volatile bool isEnabled;

        private static ConcurrencyWindowBuilder activeBuilder = new(DateTimeOffset.UtcNow);
        private static ConcurrencyWindowSnapshot? lastCompletedWindow;

        private static long windowStartTimestamp;
        private static long lastTickStartTimestamp;
        private static long currentTickStartTimestamp;
        private static double currentTickOverheadMs;

        private static TimeSpan lastProcessCpuTime;
        private static long lastCpuSampleTimestamp;
        private static long windowStartTransportUpBytes;
        private static long windowStartTransportDownBytes;

        [ThreadStatic]
        private static int rebroadcastDepth;

        [ThreadStatic]
        private static int broadcastDeltasDepth;

        [ThreadStatic]
        private static int cycleSerializedLocations;

        [ThreadStatic]
        private static long cycleSerializedLocationBytes;

        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        // Caché de reflexión para inspección puntual (cada 10 s) de colas fiables de Lidgren.
        private static FieldInfo? lidgrenPeersField;
        private static FieldInfo? netConnSendChannelsField;
        private static FieldInfo? netReliableStoredMessagesField;
        private static FieldInfo? netStoredMessageObjField;
        private static FieldInfo? netStoredMessageLastSentField;
        private static PropertyInfo? netSenderQueuedSendsCountProp;
        private static bool reflectionInitialized;

        public static bool IsEnabled => isEnabled;

        public static ConcurrencyWindowSnapshot? LastCompletedWindow
        {
            get { lock (HistorySync) return lastCompletedWindow; }
        }

        public static IReadOnlyList<ConcurrencyWindowSnapshot> GetRecentWindowsSnapshot()
        {
            lock (HistorySync) return RecentWindows.ToList();
        }

        /// <summary>Inicializa el rastreador de concurrencia. Solo instala parches Harmony si está habilitado en config.</summary>
        public static void Initialize(ModContext ctx)
        {
            context = ctx;
            isEnabled = ctx.Config.EnableConcurrencyInstrumentation;

            if (!initialized)
            {
                initialized = true;
                ctx.RawHelper.Events.GameLoop.UpdateTicking += OnUpdateTicking;
                ctx.RawHelper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
                ctx.RawHelper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
                ctx.RawHelper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
                ctx.RawHelper.Events.Multiplayer.PeerConnected += OnPeerConnected;
            }

            if (ctx.Config.EnableConcurrencyInstrumentation || ctx.Config.ExtendedConcurrency)
            {
                EnsureHarmonyPatchesRegistered(ctx);
            }

            ResetState(DateTimeOffset.UtcNow);
        }

        /// <summary>Activa o desactiva la captura en caliente (por ejemplo desde consola o <c>AdminApi</c>).</summary>
        public static void SetEnabled(bool enabled)
        {
            if (enabled && context != null && !patchesApplied)
            {
                EnsureHarmonyPatchesRegistered(context);
            }

            isEnabled = enabled;
            if (enabled)
            {
                EnsureLidgrenBandwidthLogger();
                ResetState(DateTimeOffset.UtcNow);
            }
        }

        /// <summary>Reinicia los contadores y la ventana actual.</summary>
        public static void Reset()
        {
            ResetState(DateTimeOffset.UtcNow);
        }

        public static void MarkConnectionHandshakeStart(string connectionId)
        {
            if (!isEnabled || string.IsNullOrWhiteSpace(connectionId))
                return;
            ConnectionHandshakeStartTicks[connectionId] = Stopwatch.GetTimestamp();
        }

        public static void BindConnectionToPlayer(string connectionId, long playerId)
        {
            if (!isEnabled || playerId == 0 || string.IsNullOrWhiteSpace(connectionId))
                return;

            if (ConnectionHandshakeStartTicks.TryRemove(connectionId, out long startTicks))
            {
                PlayerHandshakeStartTicks[playerId] = startTicks;
            }
            else if (!PlayerHandshakeStartTicks.ContainsKey(playerId))
            {
                PlayerHandshakeStartTicks[playerId] = Stopwatch.GetTimestamp();
            }
        }

        public static void RecordFilteredResidenceSend(int skippedRecipientsCount = 1)
        {
            if (!isEnabled || skippedRecipientsCount <= 0)
                return;
            activeBuilder.RecordFilteredResidenceSend(skippedRecipientsCount);
        }

        public static void BeginRebroadcastScope() => rebroadcastDepth++;
        public static void EndRebroadcastScope()
        {
            if (rebroadcastDepth > 0)
                rebroadcastDepth--;
        }

        /// <summary>Devuelve la última instantánea cerrada o construye una parcial de la ventana en curso.</summary>
        public static ConcurrencyWindowSnapshot GetLatestOrCurrentSnapshot()
        {
            lock (HistorySync)
            {
                if (lastCompletedWindow != null && !isEnabled)
                    return lastCompletedWindow;
            }

            if (!isEnabled)
            {
                return activeBuilder.BuildSnapshot(
                    DateTimeOffset.UtcNow,
                    context?.Config.ExtendedConcurrency ?? false,
                    instrumentationEnabled: false,
                    connectedClientsCount: CountConnectedClients(),
                    maxConcurrentPlayers: context?.Config.EffectiveMaxConcurrentPlayers ?? 16,
                    farmerDeltaBroadcastPeriod: context?.Config.FarmerDeltaBroadcastPeriod ?? 3,
                    transportBytesUp: 0L,
                    transportBytesDown: 0L,
                    locationOccupancy: CollectLiveLocationOccupancy(),
                    lastCheckpointMetrics: LoadMetricsTracker.LastCheckpointMetrics,
                    lastRolloverMetrics: LoadMetricsTracker.LastRolloverMetrics);
            }

            lock (HistorySync)
            {
                if (lastCompletedWindow != null)
                    return lastCompletedWindow;
            }

            return BuildCurrentSnapshot(DateTimeOffset.UtcNow);
        }

        private static void EnsureHarmonyPatchesRegistered(ModContext ctx)
        {
            if (patchesApplied)
                return;
            patchesApplied = true;

            ctx.Patch(
                "GameServer.sendMessage.Metrics",
                "ConcurrencyMetrics",
                PatchCriticality.Experience,
                AccessTools.Method(typeof(GameServer), nameof(GameServer.sendMessage), new[] { typeof(long), typeof(OutgoingMessage) }),
                prefix: new HarmonyMethod(typeof(ConcurrencyMetricsTracker), nameof(Prefix_GameServer_SendMessage)),
                optional: true);

            ctx.Patch(
                "GameServer.rebroadcastClientMessage.Metrics",
                "ConcurrencyMetrics",
                PatchCriticality.Experience,
                AccessTools.Method(typeof(GameServer), "rebroadcastClientMessage", new[] { typeof(IncomingMessage), typeof(long) }),
                prefix: new HarmonyMethod(typeof(ConcurrencyMetricsTracker), nameof(Prefix_GameServer_RebroadcastClientMessage)),
                finalizer: new HarmonyMethod(typeof(ConcurrencyMetricsTracker), nameof(Finalizer_GameServer_RebroadcastClientMessage)),
                optional: true);

            ctx.Patch(
                "Multiplayer.updateRoots.Metrics",
                "ConcurrencyMetrics",
                PatchCriticality.Experience,
                AccessTools.Method(typeof(Multiplayer), nameof(Multiplayer.updateRoots), Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(ConcurrencyMetricsTracker), nameof(Prefix_Multiplayer_UpdateRoots)),
                postfix: new HarmonyMethod(typeof(ConcurrencyMetricsTracker), nameof(Postfix_Multiplayer_UpdateRoots)),
                optional: true);

            ctx.Patch(
                "Multiplayer.broadcastLocationDeltas.Metrics",
                "ConcurrencyMetrics",
                PatchCriticality.Experience,
                AccessTools.Method(typeof(Multiplayer), nameof(Multiplayer.broadcastLocationDeltas), Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(ConcurrencyMetricsTracker), nameof(Prefix_Multiplayer_BroadcastLocationDeltas)),
                postfix: new HarmonyMethod(typeof(ConcurrencyMetricsTracker), nameof(Postfix_Multiplayer_BroadcastLocationDeltas)),
                optional: true);

            ctx.Patch(
                "Multiplayer.broadcastLocationBytes.Metrics",
                "ConcurrencyMetrics",
                PatchCriticality.Experience,
                AccessTools.Method(typeof(Multiplayer), "broadcastLocationBytes", new[] { typeof(GameLocation), typeof(byte), typeof(byte[]) }),
                prefix: new HarmonyMethod(typeof(ConcurrencyMetricsTracker), nameof(Prefix_Multiplayer_BroadcastLocationBytes)),
                optional: true);
        }

        private static void ResetState(DateTimeOffset nowUtc)
        {
            activeBuilder.Reset(nowUtc);
            windowStartTimestamp = Stopwatch.GetTimestamp();
            lastTickStartTimestamp = 0L;
            currentTickStartTimestamp = 0L;
            currentTickOverheadMs = 0d;

            try
            {
                using Process proc = Process.GetCurrentProcess();
                lastProcessCpuTime = proc.TotalProcessorTime;
                lastCpuSampleTimestamp = windowStartTimestamp;
            }
            catch
            {
                lastProcessCpuTime = TimeSpan.Zero;
                lastCpuSampleTimestamp = windowStartTimestamp;
            }

            (long upBytes, long downBytes) = ReadCumulativeTransportBytes();
            windowStartTransportUpBytes = upBytes;
            windowStartTransportDownBytes = downBytes;

            lock (HistorySync)
            {
                lastCompletedWindow = null;
                RecentWindows.Clear();
            }
        }

        private static void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            if (isEnabled)
                EnsureLidgrenBandwidthLogger();
            ResetState(DateTimeOffset.UtcNow);
        }

        private static void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            ConnectionHandshakeStartTicks.Clear();
            PlayerHandshakeStartTicks.Clear();
            ResetState(DateTimeOffset.UtcNow);
        }

        private static void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
        {
            if (!isEnabled || !Context.IsMainPlayer || e.Peer.IsHost)
                return;

            long t0 = Stopwatch.GetTimestamp();
            if (PlayerHandshakeStartTicks.TryRemove(e.Peer.PlayerID, out long startTicks))
            {
                double elapsedMs = (t0 - startTicks) * TicksToMs;
                string name = Game1.GetPlayer(e.Peer.PlayerID)?.Name ?? e.Peer.PlayerID.ToString();
                activeBuilder.RecordJoinTiming(e.Peer.PlayerID, name, elapsedMs, DateTimeOffset.UtcNow);
            }
            currentTickOverheadMs += (Stopwatch.GetTimestamp() - t0) * TicksToMs;
        }

        private static void OnUpdateTicking(object? sender, UpdateTickingEventArgs e)
        {
            if (!isEnabled || !Context.IsMainPlayer)
                return;

            long nowTs = Stopwatch.GetTimestamp();
            currentTickStartTimestamp = nowTs;
            currentTickOverheadMs = 0d;
        }

        private static void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!isEnabled || !Context.IsMainPlayer)
                return;

            long endTs = Stopwatch.GetTimestamp();
            if (currentTickStartTimestamp == 0L)
                return;

            double workMs = (endTs - currentTickStartTimestamp) * TicksToMs;
            double cadenceMs = lastTickStartTimestamp > 0L
                ? (currentTickStartTimestamp - lastTickStartTimestamp) * TicksToMs
                : 0d;
            lastTickStartTimestamp = currentTickStartTimestamp;

            double overheadStart = endTs;
            activeBuilder.RecordTickWork(workMs, cadenceMs, currentTickOverheadMs);

            double windowElapsedSeconds = (endTs - windowStartTimestamp) / (double)Stopwatch.Frequency;
            if (windowElapsedSeconds >= WindowDurationSeconds)
            {
                RotateWindow(DateTimeOffset.UtcNow, endTs);
            }

            double postOverheadMs = (Stopwatch.GetTimestamp() - overheadStart) * TicksToMs;
            if (postOverheadMs > 0d)
                activeBuilder.RecordTickWork(double.NaN, double.NaN, postOverheadMs);
        }

        private static void RotateWindow(DateTimeOffset nowUtc, long nowTs)
        {
            ConcurrencyWindowSnapshot snapshot = BuildCurrentSnapshot(nowUtc);
            lock (HistorySync)
            {
                lastCompletedWindow = snapshot;
                RecentWindows.Add(snapshot);
                if (RecentWindows.Count > MaxStoredWindows)
                    RecentWindows.RemoveAt(0);
            }

            activeBuilder.Reset(nowUtc);
            windowStartTimestamp = nowTs;
            (windowStartTransportUpBytes, windowStartTransportDownBytes) = ReadCumulativeTransportBytes();
        }

        private static ConcurrencyWindowSnapshot BuildCurrentSnapshot(DateTimeOffset nowUtc)
        {
            EnsureLidgrenBandwidthLogger();
            (long upTotal, long downTotal) = ReadCumulativeTransportBytes();
            long deltaUp = Math.Max(0L, upTotal - windowStartTransportUpBytes);
            long deltaDown = Math.Max(0L, downTotal - windowStartTransportDownBytes);

            double cpuPercent = 0d;
            long workingSet = 0L;
            try
            {
                using Process proc = Process.GetCurrentProcess();
                workingSet = proc.WorkingSet64;
                long nowTs = Stopwatch.GetTimestamp();
                double wallSec = (nowTs - lastCpuSampleTimestamp) / (double)Stopwatch.Frequency;
                if (wallSec > 0.05d)
                {
                    TimeSpan cpuNow = proc.TotalProcessorTime;
                    double cpuSec = (cpuNow - lastProcessCpuTime).TotalSeconds;
                    cpuPercent = Math.Max(0d, (cpuSec / wallSec) * 100d);
                    lastProcessCpuTime = cpuNow;
                    lastCpuSampleTimestamp = nowTs;
                }
            }
            catch
            {
            }

            long managedHeap = GC.GetTotalMemory(forceFullCollection: false);
            var occupancy = CollectLiveLocationOccupancy();
            var livePeers = CollectLivePeerInputs();

            return activeBuilder.BuildSnapshot(
                nowUtc,
                context?.Config.ExtendedConcurrency ?? false,
                isEnabled,
                CountConnectedClients(),
                context?.Config.EffectiveMaxConcurrentPlayers ?? 16,
                context?.Config.FarmerDeltaBroadcastPeriod ?? 3,
                deltaUp,
                deltaDown,
                occupancy,
                livePeers,
                cpuPercent,
                workingSet,
                managedHeap,
                LoadMetricsTracker.LastCheckpointMetrics,
                LoadMetricsTracker.LastRolloverMetrics);
        }

        private static int CountConnectedClients()
        {
            if (!Context.IsWorldReady)
                return 0;
            long hostId = Game1.MasterPlayer?.UniqueMultiplayerID ?? 0L;
            int count = 0;
            foreach (Farmer f in Game1.getOnlineFarmers())
            {
                if (f != null && f.UniqueMultiplayerID != hostId)
                    count++;
            }
            return count;
        }

        private static Dictionary<string, int> CollectLiveLocationOccupancy()
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (!Context.IsWorldReady)
                return result;

            long hostId = Game1.MasterPlayer?.UniqueMultiplayerID ?? 0L;
            foreach (Farmer f in Game1.getOnlineFarmers())
            {
                if (f == null || f.UniqueMultiplayerID == hostId)
                    continue;

                string locName = f.currentLocation?.NameOrUniqueName ?? string.Empty;
                if (string.IsNullOrEmpty(locName))
                    continue;

                result.TryGetValue(locName, out int cur);
                result[locName] = cur + 1;
            }
            return result;
        }

        private static List<PeerLiveTelemetryInput> CollectLivePeerInputs()
        {
            var list = new List<PeerLiveTelemetryInput>();
            if (!Context.IsWorldReady || !Context.IsMainPlayer)
                return list;

            long hostId = Game1.MasterPlayer?.UniqueMultiplayerID ?? 0L;
            LidgrenServer? lidgren = TryGetLidgrenServer();
            Bimap<long, NetConnection>? peersBimap = GetLidgrenPeersBimap(lidgren);

            foreach (Farmer farmer in Game1.getOnlineFarmers())
            {
                if (farmer == null || farmer.UniqueMultiplayerID == hostId)
                    continue;

                long pid = farmer.UniqueMultiplayerID;
                int pendingCount = farmer.messageQueue?.Count ?? 0;
                long pendingBytes = 0L;
                if (farmer.messageQueue != null)
                {
                    foreach (OutgoingMessage queued in farmer.messageQueue)
                        pendingBytes += EstimateOutgoingMessageBytes(queued);
                }

                double oldestAgeMs = 0d;
                if (peersBimap != null && peersBimap.ContainsLeft(pid))
                {
                    NetConnection conn = peersBimap[pid];
                    (int lidgrenPending, double lidgrenOldestMs) = InspectLidgrenConnectionQueue(conn);
                    pendingCount += lidgrenPending;
                    if (lidgrenOldestMs > oldestAgeMs)
                        oldestAgeMs = lidgrenOldestMs;
                }

                double pingMs = Game1.server?.getPingToClient(pid) ?? -1f;
                list.Add(new PeerLiveTelemetryInput(
                    pid,
                    farmer.Name ?? pid.ToString(),
                    farmer.currentLocation?.NameOrUniqueName ?? string.Empty,
                    pendingCount,
                    pendingBytes,
                    oldestAgeMs,
                    pingMs));
            }

            return list;
        }

        private static FieldInfo? gameServerServersField;

        private static LidgrenServer? TryGetLidgrenServer()
        {
            if (Game1.server is not GameServer gs)
                return null;

            gameServerServersField ??= AccessTools.Field(typeof(GameServer), "servers");
            if (gameServerServersField?.GetValue(gs) is not System.Collections.IEnumerable servers)
                return null;

            foreach (object? srv in servers)
            {
                if (srv is LidgrenServer ls)
                    return ls;
            }

            return null;
        }

        private static void EnsureLidgrenBandwidthLogger()
        {
            if (!Context.IsMainPlayer || Game1.server == null)
                return;

            try
            {
                LidgrenServer? lidgren = TryGetLidgrenServer();
                if (lidgren != null && !lidgren.LogBandwidth)
                    lidgren.LogBandwidth = true;
            }
            catch
            {
            }
        }

        private static (long UpBytes, long DownBytes) ReadCumulativeTransportBytes()
        {
            if (!Context.IsMainPlayer || Game1.server == null)
                return (0L, 0L);

            try
            {
                LidgrenServer? lidgren = TryGetLidgrenServer();
                BandwidthLogger? logger = lidgren?.BandwidthLogger;
                if (logger != null)
                {
                    return ((long)(logger.TotalBitsUp / 8.0), (long)(logger.TotalBitsDown / 8.0));
                }
            }
            catch
            {
            }

            return (0L, 0L);
        }

        private static void EnsureLidgrenReflectionCache()
        {
            if (reflectionInitialized)
                return;
            reflectionInitialized = true;

            lidgrenPeersField = AccessTools.Field(typeof(LidgrenServer), "peers");
            netConnSendChannelsField = AccessTools.Field(typeof(NetConnection), "m_sendChannels");

            Assembly lidgrenAsm = typeof(NetConnection).Assembly;
            Type? senderBaseType = lidgrenAsm.GetType("Lidgren.Network.NetSenderChannelBase");
            Type? reliableSenderType = lidgrenAsm.GetType("Lidgren.Network.NetReliableSenderChannel");
            Type? storedMsgType = lidgrenAsm.GetType("Lidgren.Network.NetStoredReliableMessage");

            if (senderBaseType != null)
                netSenderQueuedSendsCountProp = senderBaseType.GetProperty("QueuedSendsCount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (reliableSenderType != null)
                netReliableStoredMessagesField = reliableSenderType.GetField("m_storedMessages", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (storedMsgType != null)
            {
                netStoredMessageObjField = storedMsgType.GetField("Message", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                netStoredMessageLastSentField = storedMsgType.GetField("LastSent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
        }

        private static Bimap<long, NetConnection>? GetLidgrenPeersBimap(LidgrenServer? lidgren)
        {
            if (lidgren == null)
                return null;
            EnsureLidgrenReflectionCache();
            return lidgrenPeersField?.GetValue(lidgren) as Bimap<long, NetConnection>;
        }

        private static (int PendingCount, double OldestAgeMs) InspectLidgrenConnectionQueue(NetConnection? conn)
        {
            if (conn == null)
                return (0, 0d);

            try
            {
                EnsureLidgrenReflectionCache();
                if (netConnSendChannelsField?.GetValue(conn) is not Array channels)
                    return (0, 0d);

                int pending = 0;
                double nowNet = NetTime.Now;
                double oldestAgeMs = 0d;

                foreach (object? channel in channels)
                {
                    if (channel == null)
                        continue;

                    if (netSenderQueuedSendsCountProp != null)
                    {
                        object? queuedObj = netSenderQueuedSendsCountProp.GetValue(channel);
                        if (queuedObj is int qCount && qCount > 0)
                            pending += qCount;
                    }

                    if (netReliableStoredMessagesField != null &&
                        netStoredMessageObjField != null &&
                        netStoredMessageLastSentField != null &&
                        channel.GetType().Name == "NetReliableSenderChannel" &&
                        netReliableStoredMessagesField.GetValue(channel) is Array storedArr)
                    {
                        for (int i = 0; i < storedArr.Length; i++)
                        {
                            object? item = storedArr.GetValue(i);
                            if (item == null || netStoredMessageObjField.GetValue(item) == null)
                                continue;

                            pending++;
                            object? lastSentObj = netStoredMessageLastSentField.GetValue(item);
                            if (lastSentObj is double lastSent && lastSent > 0d)
                            {
                                double ageMs = (nowNet - lastSent) * 1000.0;
                                if (ageMs > oldestAgeMs)
                                    oldestAgeMs = ageMs;
                            }
                        }
                    }
                }

                return (pending, oldestAgeMs);
            }
            catch
            {
                return (0, 0d);
            }
        }

        // ─────────────────────────── Harmony Hooks (sin asignaciones calientes) ───────────────────────────

        public static void Prefix_GameServer_SendMessage(long peerId, OutgoingMessage message)
        {
            if (!isEnabled || !Context.IsMainPlayer)
                return;

            long t0 = Stopwatch.GetTimestamp();
            long bytes = EstimateOutgoingMessageBytes(message);
            MessageOriginKind origin = rebroadcastDepth > 0
                ? MessageOriginKind.ClientRebroadcast
                : MessageOriginKind.HostGenerated;
            activeBuilder.RecordOutgoingMessage(message.MessageType, peerId, bytes, origin);
            currentTickOverheadMs += (Stopwatch.GetTimestamp() - t0) * TicksToMs;
        }

        public static void Prefix_GameServer_RebroadcastClientMessage()
        {
            if (isEnabled && Context.IsMainPlayer)
                rebroadcastDepth++;
        }

        public static void Finalizer_GameServer_RebroadcastClientMessage()
        {
            if (rebroadcastDepth > 0)
                rebroadcastDepth--;
        }

        public static void Prefix_Multiplayer_UpdateRoots(ref long __state)
        {
            if (!isEnabled || !Context.IsMainPlayer)
                return;
            __state = Stopwatch.GetTimestamp();
        }

        public static void Postfix_Multiplayer_UpdateRoots(long __state)
        {
            if (!isEnabled || !Context.IsMainPlayer || __state == 0L)
                return;

            long t0 = Stopwatch.GetTimestamp();
            double durationMs = (t0 - __state) * TicksToMs;
            activeBuilder.RecordRootTraversal(durationMs);
            currentTickOverheadMs += (Stopwatch.GetTimestamp() - t0) * TicksToMs;
        }

        public static void Prefix_Multiplayer_BroadcastLocationDeltas(ref long __state)
        {
            if (!isEnabled || !Context.IsMainPlayer)
                return;
            broadcastDeltasDepth++;
            cycleSerializedLocations = 0;
            cycleSerializedLocationBytes = 0L;
            __state = Stopwatch.GetTimestamp();
        }

        public static void Postfix_Multiplayer_BroadcastLocationDeltas(long __state)
        {
            if (broadcastDeltasDepth > 0)
                broadcastDeltasDepth--;

            if (!isEnabled || !Context.IsMainPlayer || __state == 0L)
                return;

            long t0 = Stopwatch.GetTimestamp();
            double durationMs = (t0 - __state) * TicksToMs;
            activeBuilder.RecordLocationBroadcast(durationMs, cycleSerializedLocations, cycleSerializedLocationBytes);
            currentTickOverheadMs += (Stopwatch.GetTimestamp() - t0) * TicksToMs;
        }

        public static void Prefix_Multiplayer_BroadcastLocationBytes(byte messageType, byte[] bytes)
        {
            if (!isEnabled || !Context.IsMainPlayer || broadcastDeltasDepth <= 0 || messageType != 6 || bytes == null)
                return;

            cycleSerializedLocations++;
            cycleSerializedLocationBytes += bytes.Length;
        }

        /// <summary>Calcula sin asignaciones de memoria el tamaño aproximado en bytes del <see cref="OutgoingMessage"/>.</summary>
        public static long EstimateOutgoingMessageBytes(OutgoingMessage message)
        {
            long total = 13L; // byte messageType (1) + long farmerID (8) + int32 length prefix (4)
            var data = message.Data;
            if (data == null)
                return total;

            for (int i = 0; i < data.Count; i++)
            {
                object? obj = data[i];
                switch (obj)
                {
                    case null:
                        break;
                    case byte[] buf:
                        total += buf.Length;
                        break;
                    case string str:
                        total += 1L + Encoding.UTF8.GetByteCount(str);
                        break;
                    case string[] arr:
                        total += 1L;
                        for (int j = 0; j < arr.Length; j++)
                        {
                            string item = arr[j] ?? string.Empty;
                            total += 1L + Encoding.UTF8.GetByteCount(item);
                        }
                        break;
                    case bool:
                    case byte:
                        total += 1L;
                        break;
                    case short:
                        total += 2L;
                        break;
                    case int:
                    case float:
                        total += 4L;
                        break;
                    case long:
                    case Microsoft.Xna.Framework.Vector2:
                        total += 8L;
                        break;
                    case Guid:
                        total += 16L;
                        break;
                    default:
                        total += 4L;
                        break;
                }
            }

            return total;
        }
    }
}
