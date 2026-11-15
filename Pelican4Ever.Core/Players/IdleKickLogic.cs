using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.Players
{
    public enum IdleActionType
    {
        Warn,
        Kick
    }

    public sealed record IdleKickAction(
        long PlayerId,
        IdleActionType ActionType,
        TimeSpan IdleDuration);

    /// <summary>
    /// Lógica pura de detección de inactividad y expulsión solo por encima de media capacidad
    /// (ADR-0010, TASK-2403).
    /// </summary>
    public sealed class IdleKickLogic
    {
        private sealed class PlayerActivityState
        {
            public DateTimeOffset LastActivityUtc { get; set; }
            public bool WarningSent { get; set; }
            public DateTimeOffset? WarnedAtUtc { get; set; }
        }

        private readonly Dictionary<long, PlayerActivityState> _players = new();
        private readonly object _sync = new();

        public int MaxConcurrentPlayers { get; private set; }
        public int IdleKickMinutes { get; private set; }
        public int IdleKickWarningMinutes { get; private set; }

        public IdleKickLogic(int maxConcurrentPlayers = 16, int idleKickMinutes = 30, int idleKickWarningMinutes = 2)
        {
            Configure(maxConcurrentPlayers, idleKickMinutes, idleKickWarningMinutes);
        }

        public void Configure(int maxConcurrentPlayers, int idleKickMinutes, int idleKickWarningMinutes)
        {
            lock (_sync)
            {
                MaxConcurrentPlayers = Math.Clamp(maxConcurrentPlayers, 1, 16);
                IdleKickMinutes = Math.Max(0, idleKickMinutes);
                IdleKickWarningMinutes = Math.Max(0, idleKickWarningMinutes);
                if (IdleKickMinutes > 0 && IdleKickWarningMinutes >= IdleKickMinutes)
                    IdleKickWarningMinutes = Math.Max(0, IdleKickMinutes - 1);
            }
        }

        /// <summary>
        /// Solo se expulsa cuando los clientes conectados superan la mitad de <see cref="MaxConcurrentPlayers"/>.
        /// </summary>
        public bool ShouldEnforceIdleKick(int connectedClientCount)
        {
            return IdleKickMinutes > 0 && connectedClientCount > (MaxConcurrentPlayers / 2);
        }

        public void RecordActivity(long playerId, DateTimeOffset nowUtc)
        {
            if (playerId == 0)
                return;

            lock (_sync)
            {
                if (_players.TryGetValue(playerId, out var state))
                {
                    state.LastActivityUtc = nowUtc;
                    state.WarningSent = false;
                    state.WarnedAtUtc = null;
                }
                else
                {
                    _players[playerId] = new PlayerActivityState
                    {
                        LastActivityUtc = nowUtc,
                        WarningSent = false
                    };
                }
            }
        }

        public void RemovePlayer(long playerId)
        {
            lock (_sync)
            {
                _players.Remove(playerId);
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _players.Clear();
            }
        }

        public IReadOnlyList<IdleKickAction> Evaluate(int connectedClientCount, DateTimeOffset nowUtc)
        {
            lock (_sync)
            {
                if (!ShouldEnforceIdleKick(connectedClientCount) || _players.Count == 0)
                    return Array.Empty<IdleKickAction>();

                TimeSpan kickThreshold = TimeSpan.FromMinutes(IdleKickMinutes);
                TimeSpan warnLead = TimeSpan.FromMinutes(IdleKickWarningMinutes);
                TimeSpan warnThreshold = kickThreshold - warnLead;

                var actions = new List<IdleKickAction>();
                foreach (var kv in _players)
                {
                    long playerId = kv.Key;
                    PlayerActivityState state = kv.Value;
                    TimeSpan idle = nowUtc - state.LastActivityUtc;
                    if (idle < TimeSpan.Zero)
                        idle = TimeSpan.Zero;

                    if (IdleKickWarningMinutes > 0)
                    {
                        if (!state.WarningSent && idle >= warnThreshold)
                        {
                            state.WarningSent = true;
                            state.WarnedAtUtc = nowUtc;
                            actions.Add(new IdleKickAction(playerId, IdleActionType.Warn, idle));
                            continue;
                        }

                        if (state.WarningSent &&
                            state.WarnedAtUtc.HasValue &&
                            idle >= kickThreshold &&
                            (nowUtc - state.WarnedAtUtc.Value) >= warnLead)
                        {
                            actions.Add(new IdleKickAction(playerId, IdleActionType.Kick, idle));
                        }
                    }
                    else if (idle >= kickThreshold)
                    {
                        actions.Add(new IdleKickAction(playerId, IdleActionType.Kick, idle));
                    }
                }

                return actions;
            }
        }
    }
}
