using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Pelican4Ever.Core.Admin;

namespace Pelican4Ever.Framework
{
    /// <summary>
    /// Instantánea atómica sin bloqueos de la salud del servidor, actualizada en cada tick y leída
    /// directamente por el hilo HTTP (<c>GET /api/v1/host/health</c>) y el watchdog systemd (ADR-0010, TASK-2503).
    /// </summary>
    public sealed class HostHealthTracker
    {
        private sealed record Snapshot(
            HostHealthStatus Status,
            IReadOnlyList<string> LockdownReasons,
            IReadOnlyList<string> Overridden,
            int TotalPatches,
            IReadOnlyList<HostHealthPatchDto> FailedPatches,
            string GameVersion,
            string SmapiVersion,
            string ModVersion,
            bool SaveLoaded);

        private readonly DateTimeOffset processStartedUtc = DateTimeOffset.UtcNow;
        private long lastTickUtcTicks = DateTimeOffset.UtcNow.UtcTicks;
        private Snapshot snapshot = new(
            HostHealthStatus.Starting,
            Array.Empty<string>(),
            Array.Empty<string>(),
            0,
            Array.Empty<HostHealthPatchDto>(),
            string.Empty,
            string.Empty,
            string.Empty,
            false);

        public void RecordTick(bool saveLoaded)
        {
            long nowTicks = DateTimeOffset.UtcNow.UtcTicks;
            Interlocked.Exchange(ref lastTickUtcTicks, nowTicks);

            Snapshot current = Volatile.Read(ref snapshot);
            if (current.SaveLoaded != saveLoaded)
            {
                Volatile.Write(ref snapshot, current with { SaveLoaded = saveLoaded });
            }
        }

        public void UpdateDecision(
            PatchHealthDecision decision,
            string gameVersion,
            string smapiVersion,
            string modVersion,
            bool saveLoaded)
        {
            List<HostHealthPatchDto> failed = decision.FailedPatches
                .Select(p => new HostHealthPatchDto
                {
                    Name = p.Name,
                    Module = p.ModuleName,
                    Criticality = p.Criticality.ToString(),
                    Reason = p.FailureReason ?? string.Empty
                })
                .ToList();

            var next = new Snapshot(
                decision.Status,
                decision.LockdownReasons.ToArray(),
                decision.OverriddenPatchNames.ToArray(),
                decision.TotalPatches,
                failed,
                gameVersion,
                smapiVersion,
                modVersion,
                saveLoaded);

            Volatile.Write(ref snapshot, next);
        }

        public double GetSecondsSinceLastTick(DateTimeOffset? nowOverride = null)
        {
            DateTimeOffset now = nowOverride ?? DateTimeOffset.UtcNow;
            long ticks = Interlocked.Read(ref lastTickUtcTicks);
            var lastTick = new DateTimeOffset(ticks, TimeSpan.Zero);
            return Math.Max(0.0, (now - lastTick).TotalSeconds);
        }

        public HostHealthDto BuildDto(DateTimeOffset? nowOverride = null)
        {
            DateTimeOffset now = nowOverride ?? DateTimeOffset.UtcNow;
            Snapshot current = Volatile.Read(ref snapshot);
            long ticks = Interlocked.Read(ref lastTickUtcTicks);
            var lastTick = new DateTimeOffset(ticks, TimeSpan.Zero);
            double secondsSinceTick = Math.Max(0.0, (now - lastTick).TotalSeconds);
            double uptime = Math.Max(0.0, (now - processStartedUtc).TotalSeconds);

            return new HostHealthDto
            {
                Status = current.Status.ToString(),
                LastTickUtc = lastTick.ToString("O"),
                SecondsSinceLastTick = Math.Round(secondsSinceTick, 3),
                SaveLoaded = current.SaveLoaded,
                Lockdown = new HostHealthLockdownDto
                {
                    Reasons = new List<string>(current.LockdownReasons),
                    Overridden = new List<string>(current.Overridden)
                },
                Patches = new HostHealthPatchesDto
                {
                    Total = current.TotalPatches,
                    Failed = new List<HostHealthPatchDto>(current.FailedPatches)
                },
                GameVersion = current.GameVersion,
                SmapiVersion = current.SmapiVersion,
                ModVersion = current.ModVersion,
                UptimeSeconds = Math.Round(uptime, 2)
            };
        }
    }
}
