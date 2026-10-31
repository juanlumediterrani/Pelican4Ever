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

        /// <summary>Cualquier jugador puede entrar (sin herramientas destructivas ni cofres).</summary>
        Public
    }

    /// <summary>Asignación de una puerta del pueblo a un hueco de granja y a su propietario.</summary>
    public class DoorAssignment
    {
        public string DoorId { get; set; } = string.Empty;

        public string FarmSlotId { get; set; } = string.Empty;

        public string FarmType { get; set; } = "Standard";

        public long OwnerId { get; set; }

        public AccessPolicy Policy { get; set; } = AccessPolicy.Locked;

        public HashSet<long> Friends { get; set; } = new();
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

    /// <summary>
    /// Estado global autoritativo del servidor. Lo escribe SOLO el host mediante
    /// <c>Helper.Data.WriteSaveData</c> y se guarda junto a la partida maestra.
    /// </summary>
    public class ServerWorldState
    {
        public const string SaveDataKey = "Pelican4Ever.ServerWorldState";

        public const int CurrentSchemaVersion = 2;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>Registros de jugadores (clave: UniqueMultiplayerID). Fuente única de la verdad del estado del jugador.</summary>
        public Dictionary<long, PlayerRecord> Players { get; set; } = new();

        /// <summary>Puertas reclamadas (clave: DoorId del catálogo <c>assets/doors.json</c>).</summary>
        public Dictionary<string, DoorAssignment> Doors { get; set; } = new();

        /// <summary>Ofertas activas e históricas del mercado vecinal asíncrono (TASK-503).</summary>
        public List<MarketListing> MarketListings { get; set; } = new();

        /// <summary>Festivales reprogramados: clave fecha original ("spring13"), valor nueva fecha ("spring16").</summary>
        public Dictionary<string, string> RescheduledFestivals { get; set; } = new();

        /// <summary>Puntuaciones de minijuegos en festivales asíncronos (TASK-701).</summary>
        public List<FestivalScoreEntry> FestivalScores { get; set; } = new();

        /// <summary>Último día de juego cerrado por un Rollover (TotalDays), o -1.</summary>
        public int LastRolloverGameDay { get; set; } = -1;

        /// <summary>Momento (UTC) del último reinicio de minas.</summary>
        public DateTimeOffset LastMineResetUtc { get; set; } = DateTimeOffset.MinValue;

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

        /// <summary>Residencia que posee el jugador, o null. Cada granjero posee como mucho una.</summary>
        public DoorAssignment? FindResidenceOf(long ownerId)
        {
            return Doors.Values.FirstOrDefault(d => d.OwnerId == ownerId);
        }

        /// <summary>Valida en el host el reclamo de <paramref name="doorId"/> por <paramref name="playerId"/>.</summary>
        public DoorClaimRejection CheckDoorClaim(long playerId, string doorId, string? farmType)
        {
            if (Doors.TryGetValue(doorId, out DoorAssignment? existing) && existing.OwnerId != playerId)
                return DoorClaimRejection.DoorTaken;

            if (Doors.Values.Any(d => d.OwnerId == playerId && d.DoorId != doorId))
                return DoorClaimRejection.AlreadyOwnsResidence;

            if (!string.IsNullOrWhiteSpace(farmType) && !FarmTemplateCatalog.Templates.ContainsKey(farmType))
                return DoorClaimRejection.UnknownFarmType;

            return DoorClaimRejection.None;
        }

        /// <summary>
        /// Repara asignaciones heredadas: elimina puertas fuera del catálogo o con un hueco distinto al suyo,
        /// deja una sola residencia por propietario (la de <see cref="PlayerRecord.FarmSlotId"/> si existe),
        /// normaliza tipos de granja desconocidos a "Standard" y alinea <see cref="PlayerRecord.FarmSlotId"/>.
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
                if (string.IsNullOrWhiteSpace(assignment.FarmType) || !FarmTemplateCatalog.Templates.ContainsKey(assignment.FarmType))
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

            foreach (PlayerRecord record in Players.Values)
                record.FarmSlotId = FindResidenceOf(record.UniqueMultiplayerID)?.FarmSlotId;

            return released;
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

            return changed;
        }
    }
}
