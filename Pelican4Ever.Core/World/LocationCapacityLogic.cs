using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>Origen de una transición de ubicación para decidir exenciones de aforo (ADR-0011, TASK-2605).</summary>
    public enum LocationEntryOrigin
    {
        Voluntary = 0,
        Passout = 1,
        DayRolloverWake = 2,
        ResidenceEviction = 3,
        EventOrFestivalEnd = 4,
        AdminTeleport = 5
    }

    /// <summary>Motivo de exención cuando una entrada no consume o supera un tope de aforo.</summary>
    public enum LocationEntryExemption
    {
        None = 0,
        FeatureDisabled = 1,
        SameLocation = 2,
        OwnResidenceOrDependency = 3,
        TransitBridgeToOwnResidence = 4,
        ForcedSystemTransition = 5
    }

    public sealed record LocationEntryDecision(
        bool Allowed,
        string TargetLocationName,
        string EffectiveTargetLocationName,
        long AttemptId,
        int OccupiedCount,
        int CurrentOccupants,
        int ActiveReservations,
        int Capacity,
        LocationEntryExemption Exemption,
        bool ReservedSlot);

    public sealed record LocationReservationEntry(
        long PlayerId,
        string TargetLocationName,
        long AttemptId,
        DateTimeOffset ReservedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        LocationEntryExemption Exemption);

    public sealed record LocationOccupancyInfo(
        string LocationName,
        int CurrentOccupants,
        int ActiveReservations,
        int Capacity);

    /// <summary>
    /// Lógica pura y atómica de aforo por mapa y reservas previas al fundido (ADR-0011, TASK-2605).
    /// </summary>
    public sealed class LocationCapacityLogic
    {
        public static readonly TimeSpan DefaultReservationTtl = TimeSpan.FromSeconds(8);

        private readonly Dictionary<long, LocationReservationEntry> _activeReservationsByPlayer = new();
        private readonly Dictionary<string, int> _overrides = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new();

        public bool ExtendedConcurrency { get; private set; }
        public int DefaultLocationCapacity { get; private set; } = 16;
        public TimeSpan ReservationTtl { get; private set; } = DefaultReservationTtl;

        public void Configure(
            bool extendedConcurrency,
            int defaultLocationCapacity = 16,
            IReadOnlyDictionary<string, int>? overrides = null,
            TimeSpan? reservationTtl = null)
        {
            lock (_sync)
            {
                ExtendedConcurrency = extendedConcurrency;
                DefaultLocationCapacity = Math.Clamp(defaultLocationCapacity, 1, 64);
                ReservationTtl = reservationTtl.HasValue && reservationTtl.Value > TimeSpan.Zero
                    ? reservationTtl.Value
                    : DefaultReservationTtl;

                _overrides.Clear();
                if (overrides != null)
                {
                    foreach (var kv in overrides)
                    {
                        if (!string.IsNullOrWhiteSpace(kv.Key))
                            _overrides[kv.Key.Trim()] = Math.Clamp(kv.Value, 1, 64);
                    }
                }
            }
        }

        public int GetCapacityForLocation(string? locationName)
        {
            lock (_sync)
            {
                if (!string.IsNullOrWhiteSpace(locationName) &&
                    _overrides.TryGetValue(locationName.Trim(), out int specific))
                {
                    return specific;
                }
                return DefaultLocationCapacity;
            }
        }

        public static bool IsOwnResidenceOrDependency(
            string? targetLocationName,
            string? playerResidenceSlotId,
            IReadOnlyCollection<string>? playerAnnexIds = null,
            string? targetRootLocationName = null)
        {
            if (string.IsNullOrWhiteSpace(targetLocationName) || string.IsNullOrWhiteSpace(playerResidenceSlotId))
                return false;

            string cleanTarget = targetLocationName.Trim();
            string cleanHome = playerResidenceSlotId.Trim();

            string? targetSlot = VisitorPermissionLogic.ResidenceSlotOf(cleanTarget)
                              ?? VisitorPermissionLogic.ResidenceSlotOf(targetRootLocationName);
            if (!string.IsNullOrWhiteSpace(targetSlot) &&
                string.Equals(targetSlot, cleanHome, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (playerAnnexIds != null)
            {
                foreach (string annexId in playerAnnexIds)
                {
                    if (string.Equals(annexId, cleanTarget, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(targetRootLocationName) &&
                         string.Equals(annexId, targetRootLocationName.Trim(), StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Determina si un puente saturado pertenece al trayecto de regreso hacia la residencia del jugador,
        /// garantizándole un retorno directo efectivo a casa (ADR-0011, TASK-2605).
        /// </summary>
        public static bool IsBridgeOnPathToOwnResidence(
            string? targetBridgeLocation,
            string? playerResidenceSlotId,
            int farmSlotCount = HostPlazaLayout.BaseFarmSlotCount,
            string? currentLocationName = null)
        {
            if (string.IsNullOrWhiteSpace(targetBridgeLocation) || string.IsNullOrWhiteSpace(playerResidenceSlotId))
                return false;

            if (!HostPlazaLayout.TryParseBridgeNumber(targetBridgeLocation.Trim(), out int targetBridge))
                return false;

            int activeBridges = HostPlazaLayout.BridgeCount(farmSlotCount);
            if (targetBridge > activeBridges)
                return false;

            // Si el residente está saliendo desde su propia parcela o dependencias hacia el puente, no es un regreso a casa.
            string? curSlot = VisitorPermissionLogic.ResidenceSlotOf(currentLocationName);
            if (!string.IsNullOrWhiteSpace(curSlot) &&
                string.Equals(curSlot, playerResidenceSlotId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int homeSlot = ParseSlotNumber(playerResidenceSlotId);
            if (homeSlot <= 0)
                return false;

            // Residencias 1..4 viven en Farm (sin puente): si están en la cadena oeste regresando a Farm por un puente lleno, deben volver a casa.
            if (homeSlot <= 4)
            {
                if (targetBridge <= HostPlazaLayout.MaxWestBridges &&
                    HostPlazaLayout.TryParseBridgeNumber(currentLocationName, out int curBridge) &&
                    curBridge <= HostPlazaLayout.MaxWestBridges &&
                    curBridge > targetBridge)
                {
                    return true;
                }
                return false;
            }

            int homeBridge = (homeSlot - 1) / 4;
            if (homeBridge <= HostPlazaLayout.MaxWestBridges)
            {
                return targetBridge <= homeBridge;
            }

            // Cadenas Este (pares Herrería: 4,6,8,10,12,14; impares Museo: 5,7,9,11,13,15)
            return targetBridge >= 4
                && (targetBridge % 2) == (homeBridge % 2)
                && targetBridge <= homeBridge;
        }

        private static int ParseSlotNumber(string slotId)
        {
            int idx = slotId.LastIndexOf('_');
            if (idx >= 0 && int.TryParse(slotId.AsSpan(idx + 1), out int n))
                return n;
            return 0;
        }

        public LocationEntryDecision TryRequestEntry(
            long playerId,
            string? targetLocationName,
            long attemptId,
            int currentLiveOccupantsInTarget,
            DateTimeOffset nowUtc,
            LocationEntryOrigin origin = LocationEntryOrigin.Voluntary,
            string? currentLocationName = null,
            string? playerResidenceSlotId = null,
            IReadOnlyCollection<string>? playerAnnexIds = null,
            string? targetRootLocationName = null,
            int farmSlotCount = HostPlazaLayout.BaseFarmSlotCount)
        {
            string target = string.IsNullOrWhiteSpace(targetLocationName) ? string.Empty : targetLocationName.Trim();
            int liveOccupants = Math.Max(0, currentLiveOccupantsInTarget);

            lock (_sync)
            {
                PruneExpiredLocked(nowUtc);

                int capacity = GetCapacityForLocationLocked(target);
                if (string.IsNullOrEmpty(target) || !ExtendedConcurrency)
                {
                    int resCount = CountReservationsForLocationLocked(target);
                    return new LocationEntryDecision(
                        Allowed: true,
                        TargetLocationName: target,
                        EffectiveTargetLocationName: target,
                        AttemptId: attemptId,
                        OccupiedCount: liveOccupants + resCount,
                        CurrentOccupants: liveOccupants,
                        ActiveReservations: resCount,
                        Capacity: capacity,
                        Exemption: LocationEntryExemption.FeatureDisabled,
                        ReservedSlot: false);
                }

                if (!string.IsNullOrWhiteSpace(currentLocationName) &&
                    string.Equals(currentLocationName.Trim(), target, StringComparison.OrdinalIgnoreCase))
                {
                    int resCount = CountReservationsForLocationLocked(target);
                    return new LocationEntryDecision(
                        Allowed: true,
                        TargetLocationName: target,
                        EffectiveTargetLocationName: target,
                        AttemptId: attemptId,
                        OccupiedCount: liveOccupants + resCount,
                        CurrentOccupants: liveOccupants,
                        ActiveReservations: resCount,
                        Capacity: capacity,
                        Exemption: LocationEntryExemption.SameLocation,
                        ReservedSlot: false);
                }

                // Idempotencia si el mismo jugador reintenta exactamente el mismo (destino, intento)
                if (_activeReservationsByPlayer.TryGetValue(playerId, out var existing))
                {
                    if (string.Equals(existing.TargetLocationName, target, StringComparison.OrdinalIgnoreCase) &&
                        existing.AttemptId == attemptId)
                    {
                        int resCount = CountReservationsForLocationLocked(target);
                        return new LocationEntryDecision(
                            Allowed: true,
                            TargetLocationName: target,
                            EffectiveTargetLocationName: target,
                            AttemptId: attemptId,
                            OccupiedCount: liveOccupants + resCount,
                            CurrentOccupants: liveOccupants,
                            ActiveReservations: resCount,
                            Capacity: capacity,
                            Exemption: existing.Exemption,
                            ReservedSlot: existing.Exemption == LocationEntryExemption.None);
                    }

                    // Sustituye reserva de otro intento anterior del mismo jugador
                    _activeReservationsByPlayer.Remove(playerId);
                }

                if (origin != LocationEntryOrigin.Voluntary)
                {
                    int resCount = CountReservationsForLocationLocked(target);
                    return new LocationEntryDecision(
                        Allowed: true,
                        TargetLocationName: target,
                        EffectiveTargetLocationName: target,
                        AttemptId: attemptId,
                        OccupiedCount: liveOccupants + resCount,
                        CurrentOccupants: liveOccupants,
                        ActiveReservations: resCount,
                        Capacity: capacity,
                        Exemption: LocationEntryExemption.ForcedSystemTransition,
                        ReservedSlot: false);
                }

                if (IsOwnResidenceOrDependency(target, playerResidenceSlotId, playerAnnexIds, targetRootLocationName))
                {
                    int resCount = CountReservationsForLocationLocked(target);
                    return new LocationEntryDecision(
                        Allowed: true,
                        TargetLocationName: target,
                        EffectiveTargetLocationName: target,
                        AttemptId: attemptId,
                        OccupiedCount: liveOccupants + resCount,
                        CurrentOccupants: liveOccupants,
                        ActiveReservations: resCount,
                        Capacity: capacity,
                        Exemption: LocationEntryExemption.OwnResidenceOrDependency,
                        ReservedSlot: false);
                }

                int activeReservations = CountReservationsForLocationLocked(target);
                int totalBeforeGrant = liveOccupants + activeReservations;
                if (totalBeforeGrant < capacity)
                {
                    var entry = new LocationReservationEntry(
                        playerId,
                        target,
                        attemptId,
                        nowUtc,
                        nowUtc.Add(ReservationTtl),
                        LocationEntryExemption.None);
                    _activeReservationsByPlayer[playerId] = entry;

                    return new LocationEntryDecision(
                        Allowed: true,
                        TargetLocationName: target,
                        EffectiveTargetLocationName: target,
                        AttemptId: attemptId,
                        OccupiedCount: totalBeforeGrant + 1,
                        CurrentOccupants: liveOccupants,
                        ActiveReservations: activeReservations + 1,
                        Capacity: capacity,
                        Exemption: LocationEntryExemption.None,
                        ReservedSlot: true);
                }

                if (IsBridgeOnPathToOwnResidence(target, playerResidenceSlotId, farmSlotCount, currentLocationName) &&
                    !string.IsNullOrWhiteSpace(playerResidenceSlotId))
                {
                    return new LocationEntryDecision(
                        Allowed: true,
                        TargetLocationName: target,
                        EffectiveTargetLocationName: playerResidenceSlotId!.Trim(),
                        AttemptId: attemptId,
                        OccupiedCount: totalBeforeGrant,
                        CurrentOccupants: liveOccupants,
                        ActiveReservations: activeReservations,
                        Capacity: capacity,
                        Exemption: LocationEntryExemption.TransitBridgeToOwnResidence,
                        ReservedSlot: false);
                }

                return new LocationEntryDecision(
                    Allowed: false,
                    TargetLocationName: target,
                    EffectiveTargetLocationName: target,
                    AttemptId: attemptId,
                    OccupiedCount: totalBeforeGrant,
                    CurrentOccupants: liveOccupants,
                    ActiveReservations: activeReservations,
                    Capacity: capacity,
                    Exemption: LocationEntryExemption.None,
                    ReservedSlot: false);
            }
        }

        /// <summary>
        /// Confirma una sola vez la llegada del jugador y libera la reserva asociada a ese intento.
        /// Devuelve <c>true</c> solo si existía una reserva vigente coincidente que fue consumida.
        /// </summary>
        public bool ConfirmArrival(long playerId, string? arrivedLocationName, long? attemptId, DateTimeOffset nowUtc)
        {
            lock (_sync)
            {
                PruneExpiredLocked(nowUtc);

                if (!_activeReservationsByPlayer.TryGetValue(playerId, out var res))
                    return false;

                if (attemptId.HasValue && res.AttemptId != attemptId.Value)
                    return false;

                if (!string.IsNullOrWhiteSpace(arrivedLocationName) &&
                    !string.Equals(res.TargetLocationName, arrivedLocationName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                _activeReservationsByPlayer.Remove(playerId);
                return true;
            }
        }

        /// <summary>Libera explícitamente la reserva del intento indicado si sigue activa.</summary>
        public bool CancelReservation(long playerId, long attemptId)
        {
            lock (_sync)
            {
                if (_activeReservationsByPlayer.TryGetValue(playerId, out var res) && res.AttemptId == attemptId)
                {
                    _activeReservationsByPlayer.Remove(playerId);
                    return true;
                }
                return false;
            }
        }

        /// <summary>Libera cualquier reserva de entrada al mapa de un jugador desconectado (sin tocar la reserva de reconexión global).</summary>
        public void OnPlayerDisconnected(long playerId)
        {
            lock (_sync)
            {
                _activeReservationsByPlayer.Remove(playerId);
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _activeReservationsByPlayer.Clear();
            }
        }

        public IReadOnlyList<LocationReservationEntry> GetActiveReservationsSnapshot(DateTimeOffset nowUtc)
        {
            lock (_sync)
            {
                PruneExpiredLocked(nowUtc);
                return new List<LocationReservationEntry>(_activeReservationsByPlayer.Values);
            }
        }

        /// <summary>
        /// Devuelve los IDs de jugadores con reserva vigente cuyo destino pertenezca a la residencia indicada.
        /// </summary>
        public HashSet<long> GetPeersWithReservationForResidence(string residenceSlotId, DateTimeOffset nowUtc)
        {
            var result = new HashSet<long>();
            if (string.IsNullOrWhiteSpace(residenceSlotId))
                return result;

            lock (_sync)
            {
                PruneExpiredLocked(nowUtc);
                foreach (var res in _activeReservationsByPlayer.Values)
                {
                    string? resSlot = VisitorPermissionLogic.ResidenceSlotOf(res.TargetLocationName);
                    if (!string.IsNullOrWhiteSpace(resSlot) &&
                        string.Equals(resSlot, residenceSlotId, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(res.PlayerId);
                    }
                }
            }
            return result;
        }

        public IReadOnlyList<LocationOccupancyInfo> GetOccupancySnapshot(
            IReadOnlyDictionary<string, int>? liveOccupantsByLocation,
            DateTimeOffset nowUtc)
        {
            lock (_sync)
            {
                PruneExpiredLocked(nowUtc);

                var resCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var res in _activeReservationsByPlayer.Values)
                {
                    resCounts.TryGetValue(res.TargetLocationName, out int c);
                    resCounts[res.TargetLocationName] = c + 1;
                }

                var allNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (liveOccupantsByLocation != null)
                {
                    foreach (string k in liveOccupantsByLocation.Keys)
                    {
                        if (!string.IsNullOrWhiteSpace(k))
                            allNames.Add(k);
                    }
                }
                foreach (string k in resCounts.Keys)
                    allNames.Add(k);
                foreach (string k in _overrides.Keys)
                    allNames.Add(k);

                var result = new List<LocationOccupancyInfo>(allNames.Count);
                foreach (string loc in allNames)
                {
                    int occ = 0;
                    if (liveOccupantsByLocation != null)
                        liveOccupantsByLocation.TryGetValue(loc, out occ);
                    resCounts.TryGetValue(loc, out int reserved);
                    int cap = GetCapacityForLocationLocked(loc);
                    result.Add(new LocationOccupancyInfo(loc, Math.Max(0, occ), reserved, cap));
                }

                result.Sort((a, b) =>
                {
                    int cmp = (b.CurrentOccupants + b.ActiveReservations).CompareTo(a.CurrentOccupants + a.ActiveReservations);
                    return cmp != 0 ? cmp : string.Compare(a.LocationName, b.LocationName, StringComparison.OrdinalIgnoreCase);
                });
                return result;
            }
        }

        private int GetCapacityForLocationLocked(string? locationName)
        {
            if (!string.IsNullOrWhiteSpace(locationName) &&
                _overrides.TryGetValue(locationName.Trim(), out int specific))
            {
                return specific;
            }
            return DefaultLocationCapacity;
        }

        private int CountReservationsForLocationLocked(string locationName)
        {
            if (string.IsNullOrEmpty(locationName))
                return 0;

            int count = 0;
            foreach (var res in _activeReservationsByPlayer.Values)
            {
                if (string.Equals(res.TargetLocationName, locationName, StringComparison.OrdinalIgnoreCase))
                    count++;
            }
            return count;
        }

        private void PruneExpiredLocked(DateTimeOffset nowUtc)
        {
            if (_activeReservationsByPlayer.Count == 0)
                return;

            List<long>? expired = null;
            foreach (var kv in _activeReservationsByPlayer)
            {
                if (nowUtc >= kv.Value.ExpiresAtUtc)
                {
                    expired ??= new List<long>();
                    expired.Add(kv.Key);
                }
            }

            if (expired != null)
            {
                foreach (long pid in expired)
                    _activeReservationsByPlayer.Remove(pid);
            }
        }
    }
}
