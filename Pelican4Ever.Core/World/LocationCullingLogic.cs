using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Lógica de dominio para el culling de ubicaciones de granjas y anexos inactivos (TASK-603 / TASK-903).
    /// </summary>
    public static class LocationCullingLogic
    {
        public const string FarmPrefix = "Pelican4Ever_Farm_";
        public const string AnnexPrefix = "Pelican4Ever_Annex_";

        /// <summary>
        /// Indica si el nombre de ubicación corresponde a una residencia privada o a un anexo de residencia.
        /// </summary>
        public static bool IsPrivateFarmOrAnnex(string? locationName)
        {
            if (string.IsNullOrWhiteSpace(locationName))
                return false;

            return locationName.StartsWith(FarmPrefix, StringComparison.OrdinalIgnoreCase)
                || locationName.StartsWith(AnnexPrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determina si una ubicación debe permanecer activa en la sincronización de red.
        /// </summary>
        public static bool ShouldKeepActive(
            string? locationName,
            int farmersInsideCount,
            long? ownerId,
            IReadOnlyCollection<long>? onlinePlayerIds)
        {
            return ShouldKeepActive(locationName, farmersInsideCount, ownerId, tenantId: null, onlinePlayerIds);
        }

        /// <summary>
        /// Determina si una ubicación debe permanecer activa en la sincronización de red considerando propietario e inquilino.
        /// </summary>
        public static bool ShouldKeepActive(
            string? locationName,
            int farmersInsideCount,
            long? ownerId,
            long? tenantId,
            IReadOnlyCollection<long>? onlinePlayerIds)
        {
            if (string.IsNullOrWhiteSpace(locationName))
                return true;

            // Las ubicaciones nativas del juego nunca se cullean por esta regla
            if (!IsPrivateFarmOrAnnex(locationName))
                return true;

            // Si hay visitantes o jugadores dentro de la granja o anexo, se mantiene activa
            if (farmersInsideCount > 0)
                return true;

            // Si el propietario o el inquilino están conectados en el servidor, se mantiene activa
            if (onlinePlayerIds != null)
            {
                if (ownerId.HasValue && ownerId.Value != 0 && onlinePlayerIds.Contains(ownerId.Value))
                    return true;
                if (tenantId.HasValue && tenantId.Value != 0 && onlinePlayerIds.Contains(tenantId.Value))
                    return true;
            }

            // Residentes offline y sin visitantes: se desactiva de la sincronización continua
            return false;
        }

        /// <summary>
        /// Resuelve el hueco residencial (<c>Pelican4Ever_Farm_XX</c>) al que pertenece una ubicación
        /// o su ubicación raíz/padre (incluidos interiores de edificios como <c>Barn</c>, <c>Coop</c>, etc.).
        /// </summary>
        public static string? TryResolveResidenceSlotId(string? locationName, string? rootOrParentLocationName = null)
        {
            return VisitorPermissionLogic.ResidenceSlotOf(locationName)
                ?? VisitorPermissionLogic.ResidenceSlotOf(rootOrParentLocationName);
        }

        /// <summary>
        /// Indica si la ubicación (o la raíz de red de su edificio padre) pertenece a una residencia privada.
        /// </summary>
        public static bool IsPrivateResidenceOrDependent(string? locationName, string? rootOrParentLocationName = null)
        {
            return TryResolveResidenceSlotId(locationName, rootOrParentLocationName) != null;
        }

        /// <summary>
        /// Decide por receptor si un cliente debe recibir el estado inicial o deltas de una raíz residencial
        /// (ADR-0011, TASK-2606): propietario, inquilino, ocupante del árbol de la raíz o visitante con reserva autorizada.
        /// </summary>
        public static bool ShouldPeerReceiveResidenceRoot(
            long peerId,
            long? ownerId,
            long? tenantId,
            IReadOnlyCollection<long>? occupantsInRootTree,
            IReadOnlyCollection<long>? peersWithAuthorizedEntryToRootTree)
        {
            if (peerId == 0)
                return false;

            if (ownerId.HasValue && ownerId.Value == peerId)
                return true;

            if (tenantId.HasValue && tenantId.Value == peerId)
                return true;

            if (occupantsInRootTree != null && occupantsInRootTree.Contains(peerId))
                return true;

            if (peersWithAuthorizedEntryToRootTree != null && peersWithAuthorizedEntryToRootTree.Contains(peerId))
                return true;

            return false;
        }

        /// <summary>
        /// Filtra la lista de destinatarios candidatos conservando únicamente los receptores con interés en la raíz residencial.
        /// </summary>
        public static IReadOnlyList<long> FilterInterestedPeers(
            IEnumerable<long> candidatePeerIds,
            long? excludeSenderPeerId,
            long? ownerId,
            long? tenantId,
            IReadOnlyCollection<long>? occupantsInRootTree,
            IReadOnlyCollection<long>? peersWithAuthorizedEntryToRootTree)
        {
            if (candidatePeerIds == null)
                return Array.Empty<long>();

            var result = new List<long>();
            foreach (long peerId in candidatePeerIds)
            {
                if (peerId == 0 || (excludeSenderPeerId.HasValue && peerId == excludeSenderPeerId.Value))
                    continue;

                if (ShouldPeerReceiveResidenceRoot(peerId, ownerId, tenantId, occupantsInRootTree, peersWithAuthorizedEntryToRootTree))
                    result.Add(peerId);
            }
            return result;
        }
    }

    /// <summary>
    /// Rastrea qué clientes conservan una copia completa vigente de cada raíz residencial para garantizar que,
    /// al adquirir interés o regresar tras perderlo, reciban una foto completa antes de aplicar nuevos deltas (TASK-2606).
    /// </summary>
    public sealed class ResidenceRootSyncTracker
    {
        private readonly Dictionary<string, long> _rootRevisions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<long, long>> _peerSyncedRevisions = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new();

        public void RecordFullSync(long peerId, string? rootLocationName)
        {
            if (peerId == 0 || string.IsNullOrWhiteSpace(rootLocationName))
                return;

            string key = rootLocationName.Trim();
            lock (_sync)
            {
                if (!_rootRevisions.TryGetValue(key, out long rev) || rev <= 0)
                {
                    rev = 1L;
                    _rootRevisions[key] = rev;
                }

                if (!_peerSyncedRevisions.TryGetValue(key, out var peerMap))
                {
                    peerMap = new Dictionary<long, long>();
                    _peerSyncedRevisions[key] = peerMap;
                }

                peerMap[peerId] = rev;
            }
        }

        public void RecordDeltaBroadcast(string? rootLocationName, IEnumerable<long>? deliveredPeerIds)
        {
            if (string.IsNullOrWhiteSpace(rootLocationName))
                return;

            string key = rootLocationName.Trim();
            lock (_sync)
            {
                _rootRevisions.TryGetValue(key, out long rev);
                rev = rev <= 0 ? 1L : rev + 1L;
                _rootRevisions[key] = rev;

                if (deliveredPeerIds != null && _peerSyncedRevisions.TryGetValue(key, out var peerMap))
                {
                    foreach (long pid in deliveredPeerIds)
                    {
                        if (pid != 0 && peerMap.ContainsKey(pid))
                            peerMap[pid] = rev;
                    }
                }
            }
        }

        public bool NeedsFullSnapshotBeforeDeltas(long peerId, string? rootLocationName)
        {
            if (peerId == 0 || string.IsNullOrWhiteSpace(rootLocationName))
                return false;

            string key = rootLocationName.Trim();
            lock (_sync)
            {
                if (!_peerSyncedRevisions.TryGetValue(key, out var peerMap) ||
                    !peerMap.TryGetValue(peerId, out long peerRev))
                {
                    return true;
                }

                if (_rootRevisions.TryGetValue(key, out long hostRev) && peerRev < hostRev)
                    return true;

                return false;
            }
        }

        public void InvalidateRootForAbsentPeers(string? rootLocationName, IReadOnlyCollection<long>? currentlyInterestedPeers)
        {
            if (string.IsNullOrWhiteSpace(rootLocationName))
                return;

            string key = rootLocationName.Trim();
            lock (_sync)
            {
                if (!_peerSyncedRevisions.TryGetValue(key, out var peerMap) || peerMap.Count == 0)
                    return;

                List<long>? toRemove = null;
                foreach (long pid in peerMap.Keys)
                {
                    if (currentlyInterestedPeers == null || !currentlyInterestedPeers.Contains(pid))
                    {
                        toRemove ??= new List<long>();
                        toRemove.Add(pid);
                    }
                }

                if (toRemove != null)
                {
                    foreach (long pid in toRemove)
                        peerMap.Remove(pid);
                }
            }
        }

        public void OnPeerDisconnected(long peerId)
        {
            if (peerId == 0)
                return;

            lock (_sync)
            {
                foreach (var peerMap in _peerSyncedRevisions.Values)
                    peerMap.Remove(peerId);
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _rootRevisions.Clear();
                _peerSyncedRevisions.Clear();
            }
        }
    }
}
