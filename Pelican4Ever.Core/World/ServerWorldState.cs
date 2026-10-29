using System;
using System.Collections.Generic;
using Pelican4Ever.Core.Economy;
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

            return changed;
        }
    }
}
