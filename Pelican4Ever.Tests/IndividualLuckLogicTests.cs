using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class IndividualLuckLogicTests
    {
        private const ulong GameId = 123456789;
        private const int TotalDays = 42;
        private const long Player1 = 1001;
        private const long Player2 = 1002;

        [Fact]
        public void DifferentPlayers_HaveIndependentLuck()
        {
            double luck1 = IndividualLuckLogic.Calculate(GameId, TotalDays, Player1, false);
            double luck2 = IndividualLuckLogic.Calculate(GameId, TotalDays, Player2, false);

            Assert.InRange(luck1, -0.2, 0.2);
            Assert.InRange(luck2, -0.2, 0.2);
            Assert.NotEqual(luck1, luck2);
        }

        [Fact]
        public void SamePlayer_HasDeterministicLuckOnSameDay()
        {
            double luckA = IndividualLuckLogic.Calculate(GameId, TotalDays, Player1, false);
            double luckB = IndividualLuckLogic.Calculate(GameId, TotalDays, Player1, false);

            Assert.Equal(luckA, luckB);
        }

        [Fact]
        public void DifferentDays_YieldDifferentLuck()
        {
            double luckDay1 = IndividualLuckLogic.Calculate(GameId, TotalDays, Player1, false);
            double luckDay2 = IndividualLuckLogic.Calculate(GameId, TotalDays + 1, Player1, false);

            Assert.NotEqual(luckDay1, luckDay2);
        }

        [Fact]
        public void SpecialCharm_AddsBonusWithinBounds()
        {
            double normal = IndividualLuckLogic.Calculate(GameId, TotalDays, Player1, false);
            double withCharm = IndividualLuckLogic.Calculate(GameId, TotalDays, Player1, true);

            Assert.Equal(normal + IndividualLuckLogic.SpecialCharmBonus, withCharm, precision: 4);
            Assert.InRange(withCharm, -0.2, 0.2);
        }
    }
}
