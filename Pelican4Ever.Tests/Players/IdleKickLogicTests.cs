using System;
using Pelican4Ever.Core.Players;
using Xunit;

namespace Pelican4Ever.Tests.Players
{
    public class IdleKickLogicTests
    {
        private static readonly DateTimeOffset T0 = new(2026, 11, 15, 18, 0, 0, TimeSpan.Zero);

        [Theory]
        [InlineData(16, 8, false)]
        [InlineData(16, 9, true)]
        [InlineData(5, 2, false)]
        [InlineData(5, 3, true)]
        [InlineData(1, 0, false)]
        [InlineData(1, 1, true)]
        public void ShouldEnforceIdleKick_RespectsHalfCapacityIncludingOddCapacities(
            int maxPlayers, int connectedClients, bool expected)
        {
            var logic = new IdleKickLogic(maxPlayers, idleKickMinutes: 30, idleKickWarningMinutes: 2);
            Assert.Equal(expected, logic.ShouldEnforceIdleKick(connectedClients));
        }

        [Fact]
        public void Evaluate_BelowHalfCapacity_NeverWarnsOrKicks_EvenAfter30Minutes()
        {
            var logic = new IdleKickLogic(maxConcurrentPlayers: 16, idleKickMinutes: 30, idleKickWarningMinutes: 2);
            logic.RecordActivity(101L, T0);

            Assert.Empty(logic.Evaluate(connectedClientCount: 8, T0.AddMinutes(45)));
        }

        [Fact]
        public void Evaluate_NinthPlayerJoins_WarnsFirstThenKicksAfterWarningLead()
        {
            var logic = new IdleKickLogic(maxConcurrentPlayers: 16, idleKickMinutes: 30, idleKickWarningMinutes: 2);
            logic.RecordActivity(101L, T0);

            // Still 8 players at 40 min -> nothing
            Assert.Empty(logic.Evaluate(8, T0.AddMinutes(40)));

            // 9th player joins at +40 min -> Warn first!
            var warn = logic.Evaluate(9, T0.AddMinutes(40));
            Assert.Single(warn);
            Assert.Equal(IdleActionType.Warn, warn[0].ActionType);
            Assert.Equal(101L, warn[0].PlayerId);

            // 1 min later -> still waiting warning lead
            Assert.Empty(logic.Evaluate(9, T0.AddMinutes(41)));

            // 2 min after warning -> Kick!
            var kick = logic.Evaluate(9, T0.AddMinutes(42));
            Assert.Single(kick);
            Assert.Equal(IdleActionType.Kick, kick[0].ActionType);
            Assert.Equal(101L, kick[0].PlayerId);
        }

        [Fact]
        public void RecordActivity_ResetsIdleTimerAndWarning()
        {
            var logic = new IdleKickLogic(maxConcurrentPlayers: 16, idleKickMinutes: 30, idleKickWarningMinutes: 2);
            logic.RecordActivity(101L, T0);

            var warn = logic.Evaluate(9, T0.AddMinutes(28));
            Assert.Single(warn);
            Assert.Equal(IdleActionType.Warn, warn[0].ActionType);

            // Player moves -> resets timer
            logic.RecordActivity(101L, T0.AddMinutes(29));
            Assert.Empty(logic.Evaluate(9, T0.AddMinutes(31)));
            Assert.Empty(logic.Evaluate(9, T0.AddMinutes(56)));

            // At +57 (28 min since last activity) -> warns again
            var warnAgain = logic.Evaluate(9, T0.AddMinutes(57));
            Assert.Single(warnAgain);
            Assert.Equal(IdleActionType.Warn, warnAgain[0].ActionType);
        }
    }
}
