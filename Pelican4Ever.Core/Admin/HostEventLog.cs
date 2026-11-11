using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.Admin
{
    /// <summary>
    /// Tipos canónicos de sucesos del registro de anfitrión (TASK-803 y TASK-804).
    /// </summary>
    public static class HostEventTypes
    {
        public const string PlayerConnected = "player.connected";
        public const string PlayerDisconnected = "player.disconnected";
        public const string PlayerRejected = "player.rejected";
        public const string PlayerPairingInvited = "player.pairing.invited";
        public const string PlayerPaired = "player.paired";
        public const string PlayerPairingRevoked = "player.pairing.revoked";
        public const string FestivalSignUp = "festival.signup";
        public const string FestivalSignUpLeft = "festival.signup.left";
        public const string FestivalRunStarted = "festival.run.started";
        public const string FestivalRunEnded = "festival.run.ended";
        public const string FestivalCeremonyStarted = "festival.ceremony.started";
        public const string RolloverStarted = "rollover.started";
        public const string RolloverCompleted = "rollover.completed";
        public const string RolloverStalled = "rollover.stalled";
        public const string BackupCreated = "backup.created";
        public const string CheckpointCreated = "checkpoint.created";
        public const string CheckpointSkipped = "checkpoint.skipped";
        public const string CheckpointFailed = "checkpoint.failed";
        public const string HostRested = "host.rested";
        public const string HostAnnouncement = "host.announcement";
        public const string RestartScheduled = "host.restart.scheduled";
        public const string RestartCancelled = "host.restart.cancelled";
        public const string RestartExecuted = "host.restart.executed";
        public const string TestCommand = "host.test.command";
        public const string RemoteCommand = "host.command";
        public const string RenderLeaseGranted = "host.render.lease.granted";
        public const string RenderLeaseReleased = "host.render.lease.released";
    }

    /// <summary>
    /// Entrada inmutable del registro de sucesos de solo añadir (<c>GET /api/v1/host/events?since=N</c>).
    /// </summary>
    public sealed class HostEventEntry
    {
        public long Id { get; init; }
        public string Type { get; init; } = string.Empty;
        public string TimestampUtc { get; init; } = string.Empty;
        public int GameDay { get; init; }
        public int GameTime { get; init; }
        public string Summary { get; init; } = string.Empty;
        public Dictionary<string, object?> Data { get; init; } = new();
    }

    /// <summary>
    /// Respuesta paginada por cursor incremental para <c>GET /api/v1/host/events?since=N</c>.
    /// </summary>
    public sealed class HostEventsResponseDto
    {
        public long SinceId { get; set; }
        public long LastEventId { get; set; }
        public List<HostEventEntry> Events { get; set; } = new();
    }

    /// <summary>
    /// Registro en memoria de solo añadir (append-only) con identificadores secuenciales estrictamente crecientes (TASK-803).
    /// </summary>
    public sealed class HostEventLog
    {
        private readonly object syncRoot = new();
        private readonly List<HostEventEntry> entries = new();
        private readonly int maxRetainedEntries;
        private long nextId = 1;

        public HostEventLog(int maxRetainedEntries = 5000)
        {
            if (maxRetainedEntries <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxRetainedEntries));

            this.maxRetainedEntries = maxRetainedEntries;
        }

        /// <summary>Último ID de suceso emitido (0 si aún no se ha registrado ninguno).</summary>
        public long LastEventId
        {
            get
            {
                lock (syncRoot)
                {
                    return nextId - 1;
                }
            }
        }

        /// <summary>Añade un nuevo suceso al registro con un ID secuencial único.</summary>
        public HostEventEntry Append(
            string type,
            int gameDay,
            int gameTime,
            string summary,
            IDictionary<string, object?>? data = null,
            DateTimeOffset? nowUtc = null)
        {
            if (string.IsNullOrWhiteSpace(type))
                throw new ArgumentException("El tipo de suceso no puede estar vacío.", nameof(type));

            DateTimeOffset ts = nowUtc ?? DateTimeOffset.UtcNow;
            var copiedData = data != null
                ? new Dictionary<string, object?>(data, StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal);

            lock (syncRoot)
            {
                long id = nextId++;
                var entry = new HostEventEntry
                {
                    Id = id,
                    Type = type.Trim(),
                    TimestampUtc = ts.ToUniversalTime().ToString("O"),
                    GameDay = gameDay,
                    GameTime = gameTime,
                    Summary = summary ?? string.Empty,
                    Data = copiedData
                };

                entries.Add(entry);
                if (entries.Count > maxRetainedEntries)
                {
                    entries.RemoveAt(0);
                }

                return entry;
            }
        }

        /// <summary>
        /// Devuelve todos los sucesos con <c>Id &gt; sinceId</c> en orden ascendente estricto y sin duplicados.
        /// </summary>
        public HostEventsResponseDto GetSince(long sinceId, int limit = 500)
        {
            if (sinceId < 0)
                sinceId = 0;
            if (limit <= 0)
                limit = 500;

            lock (syncRoot)
            {
                long lastId = nextId - 1;
                List<HostEventEntry> result = entries
                    .Where(e => e.Id > sinceId)
                    .OrderBy(e => e.Id)
                    .Take(limit)
                    .ToList();

                return new HostEventsResponseDto
                {
                    SinceId = sinceId,
                    LastEventId = result.Count > 0 ? result[^1].Id : lastId,
                    Events = result
                };
            }
        }

        /// <summary>Limpia el registro (solo para pruebas).</summary>
        public void Clear()
        {
            lock (syncRoot)
            {
                entries.Clear();
                nextId = 1;
            }
        }
    }
}
