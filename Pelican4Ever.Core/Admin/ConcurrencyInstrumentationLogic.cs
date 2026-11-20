using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.Admin
{
    /// <summary>Origen de un mensaje saliente del host para atribuir carga propia frente a reenvíos (TASK-2601).</summary>
    public enum MessageOriginKind
    {
        HostGenerated = 0,
        ClientRebroadcast = 1
    }

    /// <summary>Resumen de mensajes y bytes de carga útil acumulados por tipo de mensaje en una ventana.</summary>
    public sealed class MessageTypeTrafficSummary
    {
        public byte MessageType { get; set; }
        public string MessageTypeName { get; set; } = string.Empty;
        public long HostMessages { get; set; }
        public long HostPayloadBytes { get; set; }
        public long RebroadcastMessages { get; set; }
        public long RebroadcastPayloadBytes { get; set; }
        public long TotalMessages { get; set; }
        public long TotalPayloadBytes { get; set; }
    }

    /// <summary>Resumen de tráfico y estado de cola pendiente por receptor conectado en una ventana.</summary>
    public sealed class PeerTrafficSummary
    {
        public long PeerId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public string LocationName { get; set; } = string.Empty;
        public long MessagesSent { get; set; }
        public long PayloadBytesSent { get; set; }
        public long FarmerDeltaBytes { get; set; }
        public long LocationDeltaBytes { get; set; }
        public long LocationIntroBytes { get; set; }
        public int PendingQueueCount { get; set; }
        public long PendingQueueBytes { get; set; }
        public double OldestPendingAgeMs { get; set; }
        public double PingMs { get; set; }
    }

    /// <summary>Instantánea auxiliar del estado vivo de un receptor pasado al generador de snapshots.</summary>
    public sealed record PeerLiveTelemetryInput(
        long PeerId,
        string PlayerName,
        string LocationName,
        int PendingQueueCount,
        long PendingQueueBytes,
        double OldestPendingAgeMs,
        double PingMs);

    /// <summary>Métricas de recorrido y serialización de raíces de red en el host.</summary>
    public sealed class RootSyncMetricsSummary
    {
        public long UpdateRootsCalls { get; set; }
        public double UpdateRootsTotalMs { get; set; }
        public double UpdateRootsMaxMs { get; set; }
        public long BroadcastLocationDeltasCalls { get; set; }
        public double BroadcastLocationDeltasTotalMs { get; set; }
        public double BroadcastLocationDeltasMaxMs { get; set; }
        public long LocationsSerializedCount { get; set; }
        public long LocationsSerializedBytes { get; set; }
        public long ResidenceDeltasFilteredOutCount { get; set; }
    }

    /// <summary>Tiempo medido desde el primer contacto hasta el control efectivo del personaje al entrar.</summary>
    public sealed class JoinTimingRecord
    {
        public long PeerId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public double HandshakeToPlayableMs { get; set; }
        public string RecordedAtUtc { get; set; } = string.Empty;
    }

    /// <summary>Estadísticos resumidos de una serie temporal en milisegundos.</summary>
    public sealed record TickPercentiles(double MedianMs, double P95Ms, double MaxMs, double MeanMs, int SampleCount);

    /// <summary>Instantánea completa de una ventana de 10 s de telemetría de concurrencia (TASK-2601).</summary>
    public sealed class ConcurrencyWindowSnapshot
    {
        public string WindowStartUtc { get; set; } = string.Empty;
        public string WindowEndUtc { get; set; } = string.Empty;
        public double WindowDurationSeconds { get; set; }
        public bool ExtendedConcurrencyEnabled { get; set; }
        public bool InstrumentationEnabled { get; set; }
        public int ConnectedClientsCount { get; set; }
        public int MaxConcurrentPlayers { get; set; }
        public int FarmerDeltaBroadcastPeriod { get; set; }

        public double TickMedianMs { get; set; }
        public double TickP95Ms { get; set; }
        public double TickMaxMs { get; set; }
        public double TickMeanMs { get; set; }
        public int TickCount { get; set; }
        public double TickCadenceMedianMs { get; set; }
        public double TickCadenceP95Ms { get; set; }
        public double TickCadenceMaxMs { get; set; }

        public double InstrumentationOverheadMs { get; set; }
        public double InstrumentationOverheadPercent { get; set; }

        public long PayloadTotalMessages { get; set; }
        public long PayloadTotalBytes { get; set; }
        public double PayloadBytesPerSecond { get; set; }
        public long TransportBytesUp { get; set; }
        public long TransportBytesDown { get; set; }
        public double TransportBytesUpPerSecond { get; set; }
        public long Type0FarmerDeltaBytes { get; set; }
        public long Type6LocationDeltaBytes { get; set; }
        public long Type3LocationIntroBytes { get; set; }

        public RootSyncMetricsSummary RootSync { get; set; } = new();
        public Dictionary<string, int> LocationOccupancy { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<MessageTypeTrafficSummary> ByMessageType { get; set; } = new();
        public List<PeerTrafficSummary> ByPeer { get; set; } = new();
        public List<JoinTimingRecord> RecentJoinTimings { get; set; } = new();

        public double CpuUsagePercent { get; set; }
        public long WorkingSetBytes { get; set; }
        public long ManagedHeapBytes { get; set; }
        public SaveLoadMetricsSnapshot? LastCheckpointMetrics { get; set; }
        public SaveLoadMetricsSnapshot? LastRolloverMetrics { get; set; }
    }

    /// <summary>
    /// Funciones puras de cálculo y clasificación para instrumentación de red y ticks (TASK-2601).
    /// </summary>
    public static class ConcurrencyInstrumentationLogic
    {
        public static string GetMessageTypeName(byte messageType) => messageType switch
        {
            0 => "farmerDelta",
            1 => "serverIntroduction",
            2 => "playerIntroduction",
            3 => "locationIntroduction",
            4 => "forceEvent",
            5 => "warpFarmer",
            6 => "locationDelta",
            7 => "locationSprites",
            8 => "characterWarp",
            9 => "availableFarmhands",
            10 => "chatMessage",
            11 => "connectionMessage",
            12 => "worldDelta",
            13 => "teamDelta",
            14 => "newDaySync",
            15 => "chatInfoMessage",
            16 => "userNameUpdate",
            17 => "farmerGainExperience",
            18 => "serverToClientsMessage",
            19 => "disconnecting",
            20 => "sharedAchievement",
            21 => "globalMessage",
            22 => "partyWideMail",
            23 => "forceKick",
            24 => "removeLocationFromLookup",
            25 => "farmerKilledMonster",
            26 => "requestGrandpaReevaluation",
            27 => "digBuriedNut",
            28 => "requestPassout",
            29 => "passout",
            30 => "startNewDaySync",
            31 => "readySync",
            32 => "chestHitSync",
            33 => "dedicatedServerSync",
            127 => "compressed",
            _ => $"type_{messageType}"
        };

        /// <summary>Calcula mediana (p50), p95, máximo y media aritmética de una serie en milisegundos.</summary>
        public static TickPercentiles ComputePercentiles(IReadOnlyList<double>? samplesMs)
        {
            if (samplesMs == null || samplesMs.Count == 0)
                return new TickPercentiles(0d, 0d, 0d, 0d, 0);

            var valid = new List<double>(samplesMs.Count);
            double sum = 0d;
            for (int i = 0; i < samplesMs.Count; i++)
            {
                double v = samplesMs[i];
                if (double.IsFinite(v) && v >= 0d)
                {
                    valid.Add(v);
                    sum += v;
                }
            }

            if (valid.Count == 0)
                return new TickPercentiles(0d, 0d, 0d, 0d, 0);

            valid.Sort();
            double median = PercentileSorted(valid, 0.50);
            double p95 = PercentileSorted(valid, 0.95);
            double max = valid[^1];
            double mean = sum / valid.Count;
            return new TickPercentiles(
                Math.Round(median, 3),
                Math.Round(p95, 3),
                Math.Round(max, 3),
                Math.Round(mean, 3),
                valid.Count);
        }

        private static double PercentileSorted(List<double> sorted, double quantile)
        {
            if (sorted.Count == 1)
                return sorted[0];

            double index = quantile * (sorted.Count - 1);
            int lower = (int)Math.Floor(index);
            int upper = (int)Math.Ceiling(index);
            if (lower == upper)
                return sorted[lower];

            double weight = index - lower;
            return sorted[lower] + ((sorted[upper] - sorted[lower]) * weight);
        }

        /// <summary>Calcula el porcentaje de sobrecoste propio respecto al trabajo total medido de los ticks.</summary>
        public static double ComputeOverheadPercent(double overheadMs, double totalTickWorkMs)
        {
            if (!double.IsFinite(overheadMs) || !double.IsFinite(totalTickWorkMs) || totalTickWorkMs <= 0d || overheadMs <= 0d)
                return 0d;

            return Math.Round((overheadMs / totalTickWorkMs) * 100d, 3);
        }
    }

    /// <summary>
    /// Acumulador determinista en memoria para ventanas de 10 s de telemetría de red y ticks (TASK-2601).
    /// </summary>
    public sealed class ConcurrencyWindowBuilder
    {
        private sealed class MessageTypeBucket
        {
            public byte MessageType;
            public long HostMessages;
            public long HostPayloadBytes;
            public long RebroadcastMessages;
            public long RebroadcastPayloadBytes;
        }

        private sealed class PeerTrafficBucket
        {
            public long PeerId;
            public long MessagesSent;
            public long PayloadBytesSent;
            public long FarmerDeltaBytes;
            public long LocationDeltaBytes;
            public long LocationIntroBytes;
        }

        private readonly object _sync = new();
        private readonly Dictionary<byte, MessageTypeBucket> _byType = new();
        private readonly Dictionary<long, PeerTrafficBucket> _byPeer = new();
        private readonly List<double> _tickWorkSamplesMs = new(640);
        private readonly List<double> _tickCadenceSamplesMs = new(640);
        private readonly List<JoinTimingRecord> _recentJoins = new();

        private DateTimeOffset _windowStartUtc;
        private double _overheadMs;
        private double _totalTickWorkMs;
        private long _updateRootsCalls;
        private double _updateRootsTotalMs;
        private double _updateRootsMaxMs;
        private long _broadcastLocationCalls;
        private double _broadcastLocationTotalMs;
        private double _broadcastLocationMaxMs;
        private long _locationsSerializedCount;
        private long _locationsSerializedBytes;
        private long _residenceDeltasFilteredOut;

        public ConcurrencyWindowBuilder(DateTimeOffset windowStartUtc)
        {
            _windowStartUtc = windowStartUtc;
        }

        public DateTimeOffset WindowStartUtc
        {
            get { lock (_sync) return _windowStartUtc; }
        }

        public void RecordOutgoingMessage(byte messageType, long peerId, long payloadBytes, MessageOriginKind origin)
        {
            long safeBytes = Math.Max(0L, payloadBytes);
            lock (_sync)
            {
                if (!_byType.TryGetValue(messageType, out var typeBucket))
                {
                    typeBucket = new MessageTypeBucket { MessageType = messageType };
                    _byType[messageType] = typeBucket;
                }

                if (origin == MessageOriginKind.ClientRebroadcast)
                {
                    typeBucket.RebroadcastMessages++;
                    typeBucket.RebroadcastPayloadBytes += safeBytes;
                }
                else
                {
                    typeBucket.HostMessages++;
                    typeBucket.HostPayloadBytes += safeBytes;
                }

                if (peerId != 0)
                {
                    if (!_byPeer.TryGetValue(peerId, out var peerBucket))
                    {
                        peerBucket = new PeerTrafficBucket { PeerId = peerId };
                        _byPeer[peerId] = peerBucket;
                    }

                    peerBucket.MessagesSent++;
                    peerBucket.PayloadBytesSent += safeBytes;
                    if (messageType == 0)
                        peerBucket.FarmerDeltaBytes += safeBytes;
                    else if (messageType == 6)
                        peerBucket.LocationDeltaBytes += safeBytes;
                    else if (messageType == 3)
                        peerBucket.LocationIntroBytes += safeBytes;
                }
            }
        }

        public void RecordRootTraversal(double durationMs)
        {
            double safeMs = double.IsFinite(durationMs) && durationMs >= 0d ? durationMs : 0d;
            lock (_sync)
            {
                _updateRootsCalls++;
                _updateRootsTotalMs += safeMs;
                if (safeMs > _updateRootsMaxMs)
                    _updateRootsMaxMs = safeMs;
            }
        }

        public void RecordLocationBroadcast(double durationMs, int serializedLocationCount, long serializedBytes)
        {
            double safeMs = double.IsFinite(durationMs) && durationMs >= 0d ? durationMs : 0d;
            lock (_sync)
            {
                _broadcastLocationCalls++;
                _broadcastLocationTotalMs += safeMs;
                if (safeMs > _broadcastLocationMaxMs)
                    _broadcastLocationMaxMs = safeMs;
                _locationsSerializedCount += Math.Max(0, serializedLocationCount);
                _locationsSerializedBytes += Math.Max(0L, serializedBytes);
            }
        }

        public void RecordFilteredResidenceSend(int skippedRecipientsCount = 1)
        {
            if (skippedRecipientsCount <= 0)
                return;

            lock (_sync)
            {
                _residenceDeltasFilteredOut += skippedRecipientsCount;
            }
        }

        public void RecordTickWork(double workMs, double cadenceMs, double overheadMs)
        {
            lock (_sync)
            {
                if (double.IsFinite(workMs) && workMs >= 0d)
                {
                    _tickWorkSamplesMs.Add(workMs);
                    _totalTickWorkMs += workMs;
                }

                if (double.IsFinite(cadenceMs) && cadenceMs >= 0d)
                {
                    _tickCadenceSamplesMs.Add(cadenceMs);
                }

                if (double.IsFinite(overheadMs) && overheadMs > 0d)
                {
                    _overheadMs += overheadMs;
                }
            }
        }

        public void RecordJoinTiming(long peerId, string playerName, double handshakeToPlayableMs, DateTimeOffset recordedAtUtc)
        {
            lock (_sync)
            {
                _recentJoins.Add(new JoinTimingRecord
                {
                    PeerId = peerId,
                    PlayerName = playerName ?? string.Empty,
                    HandshakeToPlayableMs = Math.Round(Math.Max(0d, handshakeToPlayableMs), 2),
                    RecordedAtUtc = recordedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                });
                if (_recentJoins.Count > 32)
                    _recentJoins.RemoveAt(0);
            }
        }

        public void Reset(DateTimeOffset windowStartUtc)
        {
            lock (_sync)
            {
                _windowStartUtc = windowStartUtc;
                _byType.Clear();
                _byPeer.Clear();
                _tickWorkSamplesMs.Clear();
                _tickCadenceSamplesMs.Clear();
                _recentJoins.Clear();
                _overheadMs = 0d;
                _totalTickWorkMs = 0d;
                _updateRootsCalls = 0L;
                _updateRootsTotalMs = 0d;
                _updateRootsMaxMs = 0d;
                _broadcastLocationCalls = 0L;
                _broadcastLocationTotalMs = 0d;
                _broadcastLocationMaxMs = 0d;
                _locationsSerializedCount = 0L;
                _locationsSerializedBytes = 0L;
                _residenceDeltasFilteredOut = 0L;
            }
        }

        public ConcurrencyWindowSnapshot BuildSnapshot(
            DateTimeOffset windowEndUtc,
            bool extendedConcurrencyEnabled,
            bool instrumentationEnabled,
            int connectedClientsCount,
            int maxConcurrentPlayers,
            int farmerDeltaBroadcastPeriod,
            long transportBytesUp,
            long transportBytesDown,
            IReadOnlyDictionary<string, int>? locationOccupancy = null,
            IReadOnlyList<PeerLiveTelemetryInput>? livePeers = null,
            double cpuUsagePercent = 0d,
            long workingSetBytes = 0L,
            long managedHeapBytes = 0L,
            SaveLoadMetricsSnapshot? lastCheckpointMetrics = null,
            SaveLoadMetricsSnapshot? lastRolloverMetrics = null)
        {
            lock (_sync)
            {
                double durationSec = Math.Max(0.001d, (windowEndUtc - _windowStartUtc).TotalSeconds);
                TickPercentiles tickStats = ConcurrencyInstrumentationLogic.ComputePercentiles(_tickWorkSamplesMs);
                TickPercentiles cadenceStats = ConcurrencyInstrumentationLogic.ComputePercentiles(_tickCadenceSamplesMs);

                long totalMessages = 0L;
                long totalBytes = 0L;
                long type0Bytes = 0L;
                long type6Bytes = 0L;
                long type3Bytes = 0L;

                var byTypeList = new List<MessageTypeTrafficSummary>(_byType.Count);
                foreach (var bucket in _byType.Values.OrderByDescending(b => b.HostPayloadBytes + b.RebroadcastPayloadBytes))
                {
                    long msgSum = bucket.HostMessages + bucket.RebroadcastMessages;
                    long bytesSum = bucket.HostPayloadBytes + bucket.RebroadcastPayloadBytes;
                    totalMessages += msgSum;
                    totalBytes += bytesSum;

                    if (bucket.MessageType == 0) type0Bytes = bytesSum;
                    else if (bucket.MessageType == 6) type6Bytes = bytesSum;
                    else if (bucket.MessageType == 3) type3Bytes = bytesSum;

                    byTypeList.Add(new MessageTypeTrafficSummary
                    {
                        MessageType = bucket.MessageType,
                        MessageTypeName = ConcurrencyInstrumentationLogic.GetMessageTypeName(bucket.MessageType),
                        HostMessages = bucket.HostMessages,
                        HostPayloadBytes = bucket.HostPayloadBytes,
                        RebroadcastMessages = bucket.RebroadcastMessages,
                        RebroadcastPayloadBytes = bucket.RebroadcastPayloadBytes,
                        TotalMessages = msgSum,
                        TotalPayloadBytes = bytesSum
                    });
                }

                var livePeerMap = new Dictionary<long, PeerLiveTelemetryInput>();
                if (livePeers != null)
                {
                    foreach (var p in livePeers)
                        livePeerMap[p.PeerId] = p;
                }

                var allPeerIds = new HashSet<long>(_byPeer.Keys);
                foreach (long pid in livePeerMap.Keys)
                    allPeerIds.Add(pid);

                var byPeerList = new List<PeerTrafficSummary>(allPeerIds.Count);
                foreach (long pid in allPeerIds)
                {
                    _byPeer.TryGetValue(pid, out var bucket);
                    livePeerMap.TryGetValue(pid, out var live);

                    byPeerList.Add(new PeerTrafficSummary
                    {
                        PeerId = pid,
                        PlayerName = live?.PlayerName ?? string.Empty,
                        LocationName = live?.LocationName ?? string.Empty,
                        MessagesSent = bucket?.MessagesSent ?? 0L,
                        PayloadBytesSent = bucket?.PayloadBytesSent ?? 0L,
                        FarmerDeltaBytes = bucket?.FarmerDeltaBytes ?? 0L,
                        LocationDeltaBytes = bucket?.LocationDeltaBytes ?? 0L,
                        LocationIntroBytes = bucket?.LocationIntroBytes ?? 0L,
                        PendingQueueCount = live?.PendingQueueCount ?? 0,
                        PendingQueueBytes = live?.PendingQueueBytes ?? 0L,
                        OldestPendingAgeMs = Math.Round(live?.OldestPendingAgeMs ?? 0d, 2),
                        PingMs = Math.Round(live?.PingMs ?? -1d, 2)
                    });
                }
                byPeerList.Sort((a, b) => b.PayloadBytesSent.CompareTo(a.PayloadBytesSent));

                var occupancyCopy = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (locationOccupancy != null)
                {
                    foreach (var kv in locationOccupancy)
                    {
                        if (!string.IsNullOrWhiteSpace(kv.Key))
                            occupancyCopy[kv.Key] = kv.Value;
                    }
                }

                return new ConcurrencyWindowSnapshot
                {
                    WindowStartUtc = _windowStartUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    WindowEndUtc = windowEndUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    WindowDurationSeconds = Math.Round(durationSec, 3),
                    ExtendedConcurrencyEnabled = extendedConcurrencyEnabled,
                    InstrumentationEnabled = instrumentationEnabled,
                    ConnectedClientsCount = connectedClientsCount,
                    MaxConcurrentPlayers = maxConcurrentPlayers,
                    FarmerDeltaBroadcastPeriod = farmerDeltaBroadcastPeriod,

                    TickMedianMs = tickStats.MedianMs,
                    TickP95Ms = tickStats.P95Ms,
                    TickMaxMs = tickStats.MaxMs,
                    TickMeanMs = tickStats.MeanMs,
                    TickCount = tickStats.SampleCount,
                    TickCadenceMedianMs = cadenceStats.MedianMs,
                    TickCadenceP95Ms = cadenceStats.P95Ms,
                    TickCadenceMaxMs = cadenceStats.MaxMs,

                    InstrumentationOverheadMs = Math.Round(_overheadMs, 3),
                    InstrumentationOverheadPercent = ConcurrencyInstrumentationLogic.ComputeOverheadPercent(_overheadMs, _totalTickWorkMs),

                    PayloadTotalMessages = totalMessages,
                    PayloadTotalBytes = totalBytes,
                    PayloadBytesPerSecond = Math.Round(totalBytes / durationSec, 2),
                    TransportBytesUp = Math.Max(0L, transportBytesUp),
                    TransportBytesDown = Math.Max(0L, transportBytesDown),
                    TransportBytesUpPerSecond = Math.Round(Math.Max(0L, transportBytesUp) / durationSec, 2),
                    Type0FarmerDeltaBytes = type0Bytes,
                    Type6LocationDeltaBytes = type6Bytes,
                    Type3LocationIntroBytes = type3Bytes,

                    RootSync = new RootSyncMetricsSummary
                    {
                        UpdateRootsCalls = _updateRootsCalls,
                        UpdateRootsTotalMs = Math.Round(_updateRootsTotalMs, 3),
                        UpdateRootsMaxMs = Math.Round(_updateRootsMaxMs, 3),
                        BroadcastLocationDeltasCalls = _broadcastLocationCalls,
                        BroadcastLocationDeltasTotalMs = Math.Round(_broadcastLocationTotalMs, 3),
                        BroadcastLocationDeltasMaxMs = Math.Round(_broadcastLocationMaxMs, 3),
                        LocationsSerializedCount = _locationsSerializedCount,
                        LocationsSerializedBytes = _locationsSerializedBytes,
                        ResidenceDeltasFilteredOutCount = _residenceDeltasFilteredOut
                    },
                    LocationOccupancy = occupancyCopy,
                    ByMessageType = byTypeList,
                    ByPeer = byPeerList,
                    RecentJoinTimings = _recentJoins.ToList(),

                    CpuUsagePercent = Math.Round(Math.Max(0d, cpuUsagePercent), 2),
                    WorkingSetBytes = Math.Max(0L, workingSetBytes),
                    ManagedHeapBytes = Math.Max(0L, managedHeapBytes),
                    LastCheckpointMetrics = lastCheckpointMetrics,
                    LastRolloverMetrics = lastRolloverMetrics
                };
            }
        }
    }
}
