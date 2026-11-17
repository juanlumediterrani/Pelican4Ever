using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.Networking
{
    /// <summary>Estado de la decisión de admisión de una conexión entrante.</summary>
    public enum AdmissionOutcome
    {
        Admitted,
        Queued,
        Rejected
    }

    /// <summary>Motivo de rechazo cuando la admisión es denegada.</summary>
    public enum AdmissionRejectReason
    {
        None,
        QueueFull
    }

    /// <summary>Clasificación de la desconexión de un jugador para decidir si se reserva su plaza.</summary>
    public enum DisconnectClassification
    {
        NetworkDrop,
        VoluntaryExit,
        IdleKick,
        AdminKick,
        DayRollover
    }

    public sealed record AdmissionDecision(
        AdmissionOutcome Outcome,
        int QueuePosition = 0,
        int QueueLength = 0,
        AdmissionRejectReason RejectReason = AdmissionRejectReason.None,
        bool UsedReconnectReservation = false);

    public sealed record QueuedConnectionInfo(
        string ConnectionId,
        string IdentityKey,
        int Position,
        DateTimeOffset EnqueuedAtUtc);

    public sealed record ReconnectReservationInfo(
        string IdentityKey,
        DateTimeOffset ReservedAtUtc,
        DateTimeOffset ExpiresAtUtc);

    /// <summary>
    /// Lógica pura de admisión por orden de llegada con cola de espera y reserva de plaza
    /// tras caídas de red (ADR-0010, TASK-2402).
    /// </summary>
    public sealed class AdmissionLogic
    {
        private sealed class QueueEntry
        {
            public string ConnectionId { get; }
            public string IdentityKey { get; }
            public DateTimeOffset EnqueuedAtUtc { get; }

            public QueueEntry(string connectionId, string identityKey, DateTimeOffset enqueuedAtUtc)
            {
                ConnectionId = connectionId;
                IdentityKey = identityKey;
                EnqueuedAtUtc = enqueuedAtUtc;
            }
        }

        private sealed class ReservationEntry
        {
            public string IdentityKey { get; }
            public DateTimeOffset ReservedAtUtc { get; }
            public DateTimeOffset ExpiresAtUtc { get; }

            public ReservationEntry(string identityKey, DateTimeOffset reservedAtUtc, DateTimeOffset expiresAtUtc)
            {
                IdentityKey = identityKey;
                ReservedAtUtc = reservedAtUtc;
                ExpiresAtUtc = expiresAtUtc;
            }
        }

        private readonly Dictionary<string, string> _activeConnectionToIdentity = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _activeIdentities = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ReservationEntry> _reservations = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<QueueEntry> _queue = new();
        private readonly object _sync = new();

        public int MaxConcurrentPlayers { get; private set; }
        public int QueueCapacity { get; private set; }
        public int ReconnectGraceMinutes { get; private set; }

        public AdmissionLogic(int maxConcurrentPlayers = 16, int queueCapacity = 16, int reconnectGraceMinutes = 5)
        {
            Configure(maxConcurrentPlayers, queueCapacity, reconnectGraceMinutes);
        }

        public void Configure(int maxConcurrentPlayers, int queueCapacity, int reconnectGraceMinutes)
        {
            lock (_sync)
            {
                MaxConcurrentPlayers = Math.Clamp(maxConcurrentPlayers, 1, 16);
                QueueCapacity = Math.Clamp(queueCapacity, 0, 32);
                ReconnectGraceMinutes = Math.Clamp(reconnectGraceMinutes, 0, 30);
            }
        }

        public int ActiveCount
        {
            get { lock (_sync) return _activeConnectionToIdentity.Count; }
        }

        public int QueueCount
        {
            get { lock (_sync) return _queue.Count; }
        }

        public int GetOccupiedSlotsCount(DateTimeOffset nowUtc)
        {
            lock (_sync)
            {
                PruneExpiredReservationsLocked(nowUtc);
                return _activeConnectionToIdentity.Count + _reservations.Count;
            }
        }

        /// <summary>
        /// Evalúa si una conexión ya autenticada entra en partida, pasa a la cola o es rechazada.
        /// </summary>
        public AdmissionDecision TryRequestAdmission(string connectionId, string identityKey, DateTimeOffset nowUtc)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
                throw new ArgumentException("connectionId requerido", nameof(connectionId));

            string cleanIdentity = string.IsNullOrWhiteSpace(identityKey) ? $"conn:{connectionId}" : identityKey.Trim();

            lock (_sync)
            {
                PruneExpiredReservationsLocked(nowUtc);

                // Si esta misma conexión ya está admitida (p. ej. reenvío de lista de peones tras rechazo de ranura)
                if (_activeConnectionToIdentity.ContainsKey(connectionId))
                {
                    return new AdmissionDecision(AdmissionOutcome.Admitted);
                }

                // Si el dispositivo tiene una reserva activa por caída de red, recupera su plaza sin pasar por la cola
                if (_reservations.Remove(cleanIdentity))
                {
                    RemoveFromQueueByConnectionOrIdentityLocked(connectionId, cleanIdentity);
                    AddActiveLocked(connectionId, cleanIdentity);
                    return new AdmissionDecision(AdmissionOutcome.Admitted, UsedReconnectReservation: true);
                }

                // Si ya está en la cola
                int existingQueueIdx = FindQueueIndexLocked(connectionId);
                if (existingQueueIdx >= 0)
                {
                    if (existingQueueIdx == 0 && GetOccupiedSlotsLocked() < MaxConcurrentPlayers)
                    {
                        _queue.RemoveAt(0);
                        AddActiveLocked(connectionId, cleanIdentity);
                        return new AdmissionDecision(AdmissionOutcome.Admitted);
                    }

                    return new AdmissionDecision(
                        AdmissionOutcome.Queued,
                        QueuePosition: existingQueueIdx + 1,
                        QueueLength: _queue.Count);
                }

                // Si hay plaza libre y nadie espera delante, entra directamente
                if (_queue.Count == 0 && GetOccupiedSlotsLocked() < MaxConcurrentPlayers)
                {
                    AddActiveLocked(connectionId, cleanIdentity);
                    return new AdmissionDecision(AdmissionOutcome.Admitted);
                }

                // Servidor lleno (o hay cola esperando delante): intentar encolar
                if (_queue.Count >= QueueCapacity)
                {
                    return new AdmissionDecision(
                        AdmissionOutcome.Rejected,
                        QueueLength: _queue.Count,
                        RejectReason: AdmissionRejectReason.QueueFull);
                }

                _queue.Add(new QueueEntry(connectionId, cleanIdentity, nowUtc));
                return new AdmissionDecision(
                    AdmissionOutcome.Queued,
                    QueuePosition: _queue.Count,
                    QueueLength: _queue.Count);
            }
        }

        /// <summary>
        /// Registra la desconexión de una conexión activa o en cola y devuelve las conexiones promocionadas
        /// desde la cola si se liberó alguna plaza.
        /// </summary>
        public IReadOnlyList<QueuedConnectionInfo> OnDisconnected(
            string connectionId,
            DisconnectClassification classification,
            DateTimeOffset nowUtc)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
                return Array.Empty<QueuedConnectionInfo>();

            lock (_sync)
            {
                PruneExpiredReservationsLocked(nowUtc);

                // 1. ¿Estaba en cola?
                int qIdx = FindQueueIndexLocked(connectionId);
                if (qIdx >= 0)
                {
                    _queue.RemoveAt(qIdx);
                    return Array.Empty<QueuedConnectionInfo>();
                }

                // 2. ¿Estaba activo en partida?
                if (_activeConnectionToIdentity.TryGetValue(connectionId, out string? identity))
                {
                    _activeConnectionToIdentity.Remove(connectionId);
                    if (!StillHasActiveConnectionForIdentityLocked(identity))
                    {
                        _activeIdentities.Remove(identity);

                        if (classification == DisconnectClassification.NetworkDrop &&
                            ReconnectGraceMinutes > 0 &&
                            !identity.StartsWith("conn:", StringComparison.Ordinal))
                        {
                            _reservations[identity] = new ReservationEntry(
                                identity,
                                nowUtc,
                                nowUtc.AddMinutes(ReconnectGraceMinutes));
                        }
                    }
                }

                return PromoteEligibleLocked(nowUtc);
            }
        }

        /// <summary>
        /// Promueve conexiones de la cola si hay plazas libres (p. ej. al vencer una reserva de reconexión).
        /// </summary>
        public IReadOnlyList<QueuedConnectionInfo> PromoteEligible(DateTimeOffset nowUtc)
        {
            lock (_sync)
            {
                PruneExpiredReservationsLocked(nowUtc);
                return PromoteEligibleLocked(nowUtc);
            }
        }

        public IReadOnlyList<QueuedConnectionInfo> GetQueueSnapshot()
        {
            lock (_sync)
            {
                var result = new List<QueuedConnectionInfo>(_queue.Count);
                for (int i = 0; i < _queue.Count; i++)
                {
                    var e = _queue[i];
                    result.Add(new QueuedConnectionInfo(e.ConnectionId, e.IdentityKey, i + 1, e.EnqueuedAtUtc));
                }
                return result;
            }
        }

        public IReadOnlyList<ReconnectReservationInfo> GetReservationsSnapshot(DateTimeOffset nowUtc)
        {
            lock (_sync)
            {
                PruneExpiredReservationsLocked(nowUtc);
                var result = new List<ReconnectReservationInfo>(_reservations.Count);
                foreach (var r in _reservations.Values)
                {
                    result.Add(new ReconnectReservationInfo(r.IdentityKey, r.ReservedAtUtc, r.ExpiresAtUtc));
                }
                return result;
            }
        }

        public void ClearAll()
        {
            lock (_sync)
            {
                _activeConnectionToIdentity.Clear();
                _activeIdentities.Clear();
                _reservations.Clear();
                _queue.Clear();
            }
        }

        private List<QueuedConnectionInfo> PromoteEligibleLocked(DateTimeOffset nowUtc)
        {
            var promoted = new List<QueuedConnectionInfo>();
            while (_queue.Count > 0 && GetOccupiedSlotsLocked() < MaxConcurrentPlayers)
            {
                var next = _queue[0];
                _queue.RemoveAt(0);
                AddActiveLocked(next.ConnectionId, next.IdentityKey);
                promoted.Add(new QueuedConnectionInfo(next.ConnectionId, next.IdentityKey, 0, next.EnqueuedAtUtc));
            }
            return promoted;
        }

        private int GetOccupiedSlotsLocked() => _activeConnectionToIdentity.Count + _reservations.Count;

        private void AddActiveLocked(string connectionId, string identityKey)
        {
            _activeConnectionToIdentity[connectionId] = identityKey;
            _activeIdentities.Add(identityKey);
        }

        private bool StillHasActiveConnectionForIdentityLocked(string identityKey)
        {
            foreach (var id in _activeConnectionToIdentity.Values)
            {
                if (string.Equals(id, identityKey, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private int FindQueueIndexLocked(string connectionId)
        {
            for (int i = 0; i < _queue.Count; i++)
            {
                if (string.Equals(_queue[i].ConnectionId, connectionId, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        private void RemoveFromQueueByConnectionOrIdentityLocked(string connectionId, string identityKey)
        {
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_queue[i].ConnectionId, connectionId, StringComparison.Ordinal) ||
                    string.Equals(_queue[i].IdentityKey, identityKey, StringComparison.OrdinalIgnoreCase))
                {
                    _queue.RemoveAt(i);
                }
            }
        }

        private void PruneExpiredReservationsLocked(DateTimeOffset nowUtc)
        {
            if (_reservations.Count == 0)
                return;

            List<string>? expired = null;
            foreach (var kv in _reservations)
            {
                if (nowUtc >= kv.Value.ExpiresAtUtc)
                {
                    expired ??= new List<string>();
                    expired.Add(kv.Key);
                }
            }

            if (expired != null)
            {
                foreach (string key in expired)
                    _reservations.Remove(key);
            }
        }
    }
}
