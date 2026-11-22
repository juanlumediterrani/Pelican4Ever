using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.Time
{
    /// <summary>Estado del mundo relevante antes de ejecutar un salto manual «off-role» de reloj (TASK-2605).</summary>
    public sealed record TimeCommandSafetyContext(
        int OnlineRemotePlayersCount,
        IReadOnlyList<string> ExternalControlledNpcs)
    {
        public static TimeCommandSafetyContext Empty { get; } = new(0, Array.Empty<string>());

        public bool HasMixedPlayers => OnlineRemotePlayersCount > 0;

        public bool HasExternalNpcs => ExternalControlledNpcs != null && ExternalControlledNpcs.Count > 0;
    }

    public readonly record struct TimeCommandConfirmationDecision(
        bool Allowed,
        bool ConfirmedByRepeat,
        TimeCommandSafetyContext Context,
        string CanonicalCommand);

    /// <summary>
    /// Puerta de advertencia y confirmación previa para comandos administrativos «off-role» (<c>/time</c>,
    /// <c>p4e_time</c>) que reinician estado activo de NPCs externos y pueden descartar o mezclar progreso diario
    /// cuando hay otros jugadores conectados (TASK-2605).
    /// </summary>
    public sealed class TimeCommandConfirmationGate
    {
        public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(45);

        private readonly Func<DateTimeOffset> utcNow;
        private readonly TimeSpan window;
        private string? pendingCanonicalCommand;
        private DateTimeOffset pendingExpiresAtUtc;

        public TimeCommandConfirmationGate(Func<DateTimeOffset>? utcNow = null, TimeSpan? window = null)
        {
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            this.window = window is { TotalSeconds: > 0 } w ? w : DefaultWindow;
        }

        public static string BuildCanonicalCommand(TimeCommandParser.Parsed parsed)
            => parsed.Kind switch
            {
                TimeCommandParser.Kind.Reset => "reset",
                TimeCommandParser.Kind.Set => $"set:{parsed.Value:hh\\:mm}",
                TimeCommandParser.Kind.Hours => $"hours:{(parsed.IsMinus ? "-" : "+")}{parsed.Amount:G17}",
                TimeCommandParser.Kind.Minutes => $"minutes:{(parsed.IsMinus ? "-" : "+")}{parsed.Amount:G17}",
                _ => string.Empty
            };

        public TimeCommandConfirmationDecision Evaluate(
            TimeCommandParser.Parsed parsed,
            bool explicitConfirm,
            TimeCommandSafetyContext context)
        {
            string canonical = BuildCanonicalCommand(parsed);
            if (string.IsNullOrEmpty(canonical))
            {
                Clear();
                return new TimeCommandConfirmationDecision(false, false, context, canonical);
            }

            if (explicitConfirm)
            {
                Clear();
                return new TimeCommandConfirmationDecision(true, false, context, canonical);
            }

            DateTimeOffset now = utcNow();
            if (string.Equals(pendingCanonicalCommand, canonical, StringComparison.Ordinal) && now <= pendingExpiresAtUtc)
            {
                Clear();
                return new TimeCommandConfirmationDecision(true, true, context, canonical);
            }

            pendingCanonicalCommand = canonical;
            pendingExpiresAtUtc = now + window;
            return new TimeCommandConfirmationDecision(false, false, context, canonical);
        }

        public void Clear()
        {
            pendingCanonicalCommand = null;
            pendingExpiresAtUtc = DateTimeOffset.MinValue;
        }
    }
}
