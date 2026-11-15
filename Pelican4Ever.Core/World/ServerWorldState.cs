using System;
using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Core.Economy;
using Pelican4Ever.Core.Festivals;
using Pelican4Ever.Core.Players;

namespace Pelican4Ever.Core.World
{
    /// <summary>Política de acceso de una granja para visitantes.</summary>
    public enum AccessPolicy
    {
        /// <summary>Solo el propietario.</summary>
        Locked,

        /// <summary>Jugadores incluidos en la lista de amigos del propietario.</summary>
        Friends,

        /// <summary>Cualquier jugador puede entrar. Lo que puede tocar lo decide <see cref="DoorAssignment.VisitorsCanEdit"/>.</summary>
        Public
    }

    /// <summary>Asignación de una puerta del pueblo a un hueco de granja y a su propietario.</summary>
    public class DoorAssignment
    {
        public string DoorId { get; set; } = string.Empty;

        public string FarmSlotId { get; set; } = string.Empty;

        public string FarmType { get; set; } = "Standard";

        public long OwnerId { get; set; }

        /// <summary>ID del inquilino activo en la residencia (máximo 1), o null si no hay inquilino.</summary>
        public long? TenantId { get; set; }

        /// <summary>Solicitudes de inquilinato pendientes enviadas desde el buzón exterior.</summary>
        public HashSet<long> PendingTenantRequests { get; set; } = new();

        public AccessPolicy Policy { get; set; } = AccessPolicy.Locked;

        /// <summary>Si los visitantes pueden usar herramientas, colocar, cosechar, abrir cofres y usar máquinas.</summary>
        public bool VisitorsCanEdit { get; set; }

        public HashSet<long> Friends { get; set; } = new();

        /// <summary>Indica si el jugador vive en esta residencia (como propietario o como inquilino).</summary>
        public bool IsResident(long playerId)
        {
            return playerId != 0 && (OwnerId == playerId || (TenantId.HasValue && TenantId.Value == playerId));
        }
    }

    /// <summary>Asignación de un anexo temático adquirido para una residencia privada (TASK-901).</summary>
    public class AnnexAssignment
    {
        public const int CurrentMapVersion = 4;

        public string AnnexId { get; set; } = string.Empty;

        public string ResidenceSlotId { get; set; } = string.Empty;

        public AnnexSide Side { get; set; }

        public string TemplateId { get; set; } = "Standard";

        public long OwnerId { get; set; }

        public DateTimeOffset AcquiredAtUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>Versión de generación del mapa del anexo (v2 = recorte canónico + saneado de bordes).</summary>
        public int MapVersion { get; set; } = 1;
    }

    /// <summary>Motivo por el que el host rechaza el reclamo de una puerta.</summary>
    public enum DoorClaimRejection
    {
        None,

        /// <summary>La puerta ya pertenece a otro jugador.</summary>
        DoorTaken,

        /// <summary>El jugador ya posee otra residencia (máximo una por granjero).</summary>
        AlreadyOwnsResidence,

        /// <summary>El tipo de granja no está en <see cref="FarmTemplateCatalog"/>.</summary>
        UnknownFarmType
    }

    /// <summary>Metadatos del último checkpoint intradía persistido en <see cref="ServerWorldState"/> (TASK-1606).</summary>
    public sealed class CheckpointMetadata
    {
        /// <summary>Día de juego (<c>Game1.Date.TotalDays</c>) en que se tomó el checkpoint, o -1 si no hay ninguno.</summary>
        public int GameDay { get; set; } = -1;

        /// <summary>Hora del juego (<c>Game1.timeOfDay</c>, HHMM) en el momento del checkpoint.</summary>
        public int GameTime { get; set; } = 600;

        /// <summary>Instante UTC ISO-8601 en que se creó el checkpoint.</summary>
        public string SavedAtUtc { get; set; } = string.Empty;

        /// <summary>Nombre de carpeta de la copia verificada en <c>checkpoints/</c>, si se creó.</summary>
        public string BackupName { get; set; } = string.Empty;

        /// <summary>Duración total del checkpoint en ms.</summary>
        public double TotalMs { get; set; }

        /// <summary>Duración del tick más largo del checkpoint en ms.</summary>
        public double LongestTickMs { get; set; }
    }

    /// <summary>
    /// Estado global autoritativo del servidor. Lo escribe SOLO el host mediante
    /// <c>Helper.Data.WriteSaveData</c> y se guarda junto a la partida maestra.
    /// </summary>
    public class ServerWorldState
    {
        public const string SaveDataKey = "Pelican4Ever.ServerWorldState";

        public const int CurrentSchemaVersion = 8;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>Registros de jugadores (clave: UniqueMultiplayerID). Fuente única de la verdad del estado del jugador.</summary>
        public Dictionary<long, PlayerRecord> Players { get; set; } = new();

        /// <summary>Puertas reclamadas (clave: DoorId del catálogo <c>assets/doors.json</c>).</summary>
        public Dictionary<string, DoorAssignment> Doors { get; set; } = new();

        /// <summary>Anexos de residencia adquiridos (clave: AnnexId ej. "Pelican4Ever_Annex_01_East").</summary>
        public Dictionary<string, AnnexAssignment> Annexes { get; set; } = new();

        /// <summary>Ofertas activas e históricas del mercado vecinal asíncrono (TASK-503).</summary>
        public List<MarketListing> MarketListings { get; set; } = new();

        /// <summary>Reservas y devoluciones pendientes de confirmar desde el inventario del cliente propietario.</summary>
        public Dictionary<string, PendingMarketDelivery> PendingMarketDeliveries { get; set; } = new();

        /// <summary>Festivales reprogramados: clave fecha original ("spring13"), valor nueva fecha ("spring16").</summary>
        public Dictionary<string, string> RescheduledFestivals { get; set; } = new();

        /// <summary>Puntuaciones de minijuegos en festivales asíncronos (TASK-701).</summary>
        public List<FestivalScoreEntry> FestivalScores { get; set; } = new();

        /// <summary>Dispositivos de clientes autorizados en la lista blanca por huella digital pública (TASK-1802).</summary>
        public Dictionary<string, Pelican4Ever.Core.Security.AuthorizedDevice> AuthorizedDevices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Solicitudes de alta de nuevos dispositivos pendientes de aprobación por el propietario (TASK-1803).</summary>
        public Dictionary<string, Pelican4Ever.Core.Security.DeviceEnrollmentRequest> PendingEnrollments { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Dispositivos de clientes emparejados y autorizados en la lista blanca (clave: ClientToken).</summary>
        public Dictionary<string, AuthorizedClient> AuthorizedClients { get; set; } = new(StringComparer.Ordinal);

        /// <summary>Invitaciones de emparejamiento pendientes (con IP autorizada, código de un solo uso y caducidad de 3 días).</summary>
        public List<PairingInvite> PairingInvites { get; set; } = new();

        /// <summary>Último día de juego cerrado por un Rollover (TotalDays), o -1.</summary>
        public int LastRolloverGameDay { get; set; } = -1;

        /// <summary>Momento (UTC) del último reinicio de minas.</summary>
        public DateTimeOffset LastMineResetUtc { get; set; } = DateTimeOffset.MinValue;

        /// <summary>Metadatos del último checkpoint intradía persistido en esta partida (TASK-1606).</summary>
        public CheckpointMetadata? LastCheckpoint { get; set; }

        /// <summary>Pedidos de objetos por buzón pendientes de materializarse a mediodía (TASK-2201).</summary>
        public List<PendingMailItemOrder> PendingMailItemOrders { get; set; } = new();

        /// <summary>Pedidos de construcción/mejora por buzón pendientes de iniciar andamios a mediodía (TASK-2201).</summary>
        public List<PendingBuildingOrder> PendingBuildingOrders { get; set; } = new();

        /// <summary>Pedidos de compra de animales por buzón pendientes de llegar a mediodía (TASK-2201).</summary>
        public List<PendingAnimalOrder> PendingAnimalOrders { get; set; } = new();

        /// <summary>Devuelve el registro del jugador, creándolo si no existe.</summary>
        public PlayerRecord GetOrCreatePlayer(long uniqueMultiplayerId, string name)
        {
            if (!Players.TryGetValue(uniqueMultiplayerId, out PlayerRecord? record))
            {
                record = new PlayerRecord { UniqueMultiplayerID = uniqueMultiplayerId };
                Players[uniqueMultiplayerId] = record;
            }

            if (!string.IsNullOrWhiteSpace(name))
                record.PlayerName = name;

            return record;
        }

        /// <summary>Indica si el jugador es residente (propietario o inquilino) de la puerta indicada.</summary>
        public static bool IsResident(DoorAssignment? door, long playerId)
        {
            return door != null && door.IsResident(playerId);
        }

        /// <summary>Residencia en la que vive el jugador (como propietario o inquilino), o null.</summary>
        public DoorAssignment? FindResidenceOf(long playerId)
        {
            if (playerId == 0)
                return null;

            return Doors.Values.FirstOrDefault(d => d.IsResident(playerId));
        }

        /// <summary>Devuelve el identificador de hueco de residencia asignado al jugador (p. ej. "Pelican4Ever_Farm_01"), o null si no posee ninguna.</summary>
        public string? ResolvePlayerResidenceSlot(long playerId)
        {
            if (FindResidenceOf(playerId) is { } assignment && !string.IsNullOrEmpty(assignment.FarmSlotId))
            {
                return assignment.FarmSlotId;
            }

            if (Players.TryGetValue(playerId, out PlayerRecord? record) && !string.IsNullOrEmpty(record.FarmSlotId))
            {
                return record.FarmSlotId;
            }

            return null;
        }

        /// <summary>Valida en el host el reclamo de <paramref name="doorId"/> por <paramref name="playerId"/>.</summary>
        public DoorClaimRejection CheckDoorClaim(long playerId, string doorId, string? farmType)
        {
            if (Doors.TryGetValue(doorId, out DoorAssignment? existing) && existing.OwnerId != playerId)
                return DoorClaimRejection.DoorTaken;

            if (Doors.Values.Any(d => d.IsResident(playerId) && d.DoorId != doorId))
                return DoorClaimRejection.AlreadyOwnsResidence;

            if (!string.IsNullOrWhiteSpace(farmType) && !FarmTemplateCatalog.Templates.ContainsKey(farmType) && !FarmGrassLogic.IsMeadowlands(farmType))
                return DoorClaimRejection.UnknownFarmType;

            return DoorClaimRejection.None;
        }

        /// <summary>
        /// Repara asignaciones heredadas: elimina puertas fuera del catálogo o con un hueco distinto al suyo,
        /// deja una sola residencia por propietario (la de <see cref="PlayerRecord.FarmSlotId"/> si existe),
        /// normaliza tipos de granja desconocidos a "Standard", alinea <see cref="PlayerRecord.FarmSlotId"/>
        /// y libera los anexos asociados a residencias liberadas o huérfanas (TASK-903).
        /// Devuelve las DoorId liberadas.
        /// </summary>
        /// <param name="catalogSlotByDoorId">DoorId del catálogo → hueco de granja por defecto.</param>
        public List<string> NormalizeDoorAssignments(IReadOnlyDictionary<string, string> catalogSlotByDoorId)
        {
            var released = new List<string>();

            foreach (KeyValuePair<string, DoorAssignment> entry in Doors.ToList())
            {
                DoorAssignment assignment = entry.Value;
                if (!catalogSlotByDoorId.TryGetValue(entry.Key, out string? slotId)
                    || !string.Equals(slotId, assignment.FarmSlotId, StringComparison.OrdinalIgnoreCase))
                {
                    Doors.Remove(entry.Key);
                    released.Add(entry.Key);
                    continue;
                }

                assignment.DoorId = entry.Key;
                if (FarmGrassLogic.IsMeadowlands(assignment.FarmType))
                    assignment.FarmType = "Ranching";
                else if (string.IsNullOrWhiteSpace(assignment.FarmType) || !FarmTemplateCatalog.Templates.ContainsKey(assignment.FarmType))
                    assignment.FarmType = "Standard";
            }

            foreach (IGrouping<long, DoorAssignment> owned in Doors.Values.GroupBy(d => d.OwnerId).Where(g => g.Count() > 1).ToList())
            {
                Players.TryGetValue(owned.Key, out PlayerRecord? record);
                DoorAssignment keep = owned.FirstOrDefault(d => string.Equals(d.FarmSlotId, record?.FarmSlotId, StringComparison.OrdinalIgnoreCase))
                    ?? owned.OrderBy(d => d.DoorId, StringComparer.Ordinal).First();

                foreach (DoorAssignment extra in owned.Where(d => !ReferenceEquals(d, keep)))
                {
                    Doors.Remove(extra.DoorId);
                    released.Add(extra.DoorId);
                }
            }

            // TASK-2301: normalizar inquilinos (sin auto-inquilinato, sin doble residencia ni solicitudes de jugadores ya residentes).
            var activeOwners = new HashSet<long>(Doors.Values.Select(d => d.OwnerId).Where(id => id != 0));
            var assignedTenants = new HashSet<long>();
            foreach (DoorAssignment door in Doors.Values.OrderBy(d => d.DoorId, StringComparer.Ordinal))
            {
                door.PendingTenantRequests ??= new HashSet<long>();
                door.Friends ??= new HashSet<long>();

                if (door.TenantId.HasValue && (door.TenantId.Value == 0 || door.TenantId.Value == door.OwnerId || activeOwners.Contains(door.TenantId.Value) || !assignedTenants.Add(door.TenantId.Value)))
                {
                    door.TenantId = null;
                }
            }

            foreach (DoorAssignment door in Doors.Values)
            {
                door.PendingTenantRequests.RemoveWhere(id => id == 0 || activeOwners.Contains(id) || assignedTenants.Contains(id));
            }

            foreach (PlayerRecord record in Players.Values)
                record.FarmSlotId = FindResidenceOf(record.UniqueMultiplayerID)?.FarmSlotId;

            // TASK-903: al liberar una residencia, sus anexos se liberan con ella (no pueden quedar huérfanos sin residencia activa).
            if (Annexes != null)
            {
                foreach (KeyValuePair<string, AnnexAssignment> annexEntry in Annexes.ToList())
                {
                    AnnexAssignment annex = annexEntry.Value;
                    DoorAssignment? parentDoor = Doors.Values.FirstOrDefault(d =>
                        string.Equals(d.FarmSlotId, annex.ResidenceSlotId, StringComparison.OrdinalIgnoreCase));

                    if (parentDoor == null)
                    {
                        Annexes.Remove(annexEntry.Key);
                        continue;
                    }

                    annex.OwnerId = parentDoor.OwnerId;
                    if (string.IsNullOrWhiteSpace(annex.TemplateId) || !AnnexTemplateCatalog.Templates.ContainsKey(annex.TemplateId))
                        annex.TemplateId = "Standard";
                }
            }

            return released;
        }

        /// <summary>
        /// Genera el identificador estándar de un anexo a partir del hueco de residencia y el lado cardinal
        /// (ej. "Pelican4Ever_Farm_01" + East → "Pelican4Ever_Annex_01_East").
        /// </summary>
        public static string BuildAnnexId(string residenceSlotId, AnnexSide side)
        {
            string slotNumber = residenceSlotId != null && residenceSlotId.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase)
                ? residenceSlotId.Substring(LocationCullingLogic.FarmPrefix.Length)
                : residenceSlotId ?? string.Empty;

            return $"{LocationCullingLogic.AnnexPrefix}{slotNumber}_{side}";
        }

        /// <summary>Busca el anexo por su identificador de ubicación (ej. "Pelican4Ever_Annex_01_East"), o null si no existe.</summary>
        public AnnexAssignment? FindAnnexById(string? annexId)
        {
            if (string.IsNullOrWhiteSpace(annexId) || Annexes == null)
                return null;

            if (Annexes.TryGetValue(annexId, out AnnexAssignment? direct))
                return direct;

            return Annexes.Values.FirstOrDefault(a => string.Equals(a.AnnexId, annexId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Busca el anexo correspondiente al hueco de residencia y lado cardinal indicados, o null si no ha sido adquirido.</summary>
        public AnnexAssignment? FindAnnex(string residenceSlotId, AnnexSide side)
        {
            string id = BuildAnnexId(residenceSlotId, side);
            return FindAnnexById(id);
        }

        /// <summary>Devuelve todos los anexos adquiridos asociados a una residencia privada.</summary>
        public List<AnnexAssignment> GetAnnexesForResidence(string residenceSlotId)
        {
            return Annexes.Values
                .Where(a => string.Equals(a.ResidenceSlotId, residenceSlotId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>Migra un estado leído de una versión anterior del esquema. Devuelve true si hubo cambios.</summary>
        public bool Migrate()
        {
            bool changed = false;

            // v1 → v2: el estado del jugador pasa de Farmer.modData a Players; DoorAssignments (string→long)
            // se sustituye por Doors. Los datos v1 eran de scaffolding (sin partidas reales), así que solo se normaliza.
            if (SchemaVersion < 2)
            {
                Players ??= new Dictionary<long, PlayerRecord>();
                Doors ??= new Dictionary<string, DoorAssignment>();
                RescheduledFestivals ??= new Dictionary<string, string>();
                MarketListings ??= new List<MarketListing>();
                SchemaVersion = 2;
                changed = true;
            }

            // v2 → v3: soporte para anexos de residencia adquiribles (TASK-901).
            if (SchemaVersion < 3)
            {
                Annexes ??= new Dictionary<string, AnnexAssignment>();
                SchemaVersion = 3;
                changed = true;
            }

            if (SchemaVersion < 4)
            {
                PendingMarketDeliveries ??= new Dictionary<string, PendingMarketDelivery>();
                SchemaVersion = 4;
                changed = true;
            }

            // v4 → v5: soporte para dispositivos autorizados por clave pública asimétrica (TASK-1802).
            if (SchemaVersion < 5)
            {
                AuthorizedDevices ??= new Dictionary<string, Pelican4Ever.Core.Security.AuthorizedDevice>(StringComparer.OrdinalIgnoreCase);
                Pelican4Ever.Core.Security.ServerDeviceRegistry.MigrateLegacyClients(this, DateTimeOffset.UtcNow);
                SchemaVersion = 5;
                changed = true;
            }

            // v5 → v6: metadatos del último checkpoint intradía (TASK-1606).
            if (SchemaVersion < 6)
            {
                if (LastCheckpoint != null && LastCheckpoint.GameDay < 0)
                    LastCheckpoint = null;
                SchemaVersion = 6;
                changed = true;
            }

            // v6 → v7: pedidos diferidos por buzón (TASK-2201).
            if (SchemaVersion < 7)
            {
                PendingMailItemOrders ??= new List<PendingMailItemOrder>();
                PendingBuildingOrders ??= new List<PendingBuildingOrder>();
                PendingAnimalOrders ??= new List<PendingAnimalOrder>();
                SchemaVersion = 7;
                changed = true;
            }

            // v7 → v8: co-residencia e inquilinos en parcelas privadas (TASK-2301).
            if (SchemaVersion < 8)
            {
                Doors ??= new Dictionary<string, DoorAssignment>();
                foreach (DoorAssignment door in Doors.Values)
                {
                    door.PendingTenantRequests ??= new HashSet<long>();
                    if (door.TenantId == 0)
                        door.TenantId = null;
                }
                SchemaVersion = 8;
                changed = true;
            }

            if (PendingMailItemOrders == null)
            {
                PendingMailItemOrders = new List<PendingMailItemOrder>();
                changed = true;
            }

            if (PendingBuildingOrders == null)
            {
                PendingBuildingOrders = new List<PendingBuildingOrder>();
                changed = true;
            }

            if (PendingAnimalOrders == null)
            {
                PendingAnimalOrders = new List<PendingAnimalOrder>();
                changed = true;
            }

            if (PendingMarketDeliveries == null)
            {
                PendingMarketDeliveries = new Dictionary<string, PendingMarketDelivery>();
                changed = true;
            }

            if (MarketListings == null)
            {
                MarketListings = new List<MarketListing>();
                changed = true;
            }

            if (FestivalScores == null)
            {
                FestivalScores = new List<FestivalScoreEntry>();
                changed = true;
            }

            if (Annexes == null)
            {
                Annexes = new Dictionary<string, AnnexAssignment>();
                changed = true;
            }

            if (AuthorizedDevices == null)
            {
                AuthorizedDevices = new Dictionary<string, Pelican4Ever.Core.Security.AuthorizedDevice>(StringComparer.OrdinalIgnoreCase);
                changed = true;
            }

            if (PendingEnrollments == null)
            {
                PendingEnrollments = new Dictionary<string, Pelican4Ever.Core.Security.DeviceEnrollmentRequest>(StringComparer.OrdinalIgnoreCase);
                changed = true;
            }

            if (AuthorizedClients == null)
            {
                AuthorizedClients = new Dictionary<string, AuthorizedClient>(StringComparer.Ordinal);
                changed = true;
            }

            if (PairingInvites == null)
            {
                PairingInvites = new List<PairingInvite>();
                changed = true;
            }

            return changed;
        }
    }
}
