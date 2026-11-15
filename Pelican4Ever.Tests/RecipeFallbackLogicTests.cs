using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class RecipeFallbackLogicTests
    {
        [Theory]
        [InlineData(true, false, 1200, true)]
        [InlineData(true, false, 1800, true)]
        [InlineData(true, false, 1159, false)]
        [InlineData(true, true, 1800, false)]
        [InlineData(false, false, 1800, false)]
        public void ShouldGrant_IsIdempotentAndHonorsTimeWindow(
            bool eligible,
            bool alreadyKnown,
            int timeOfDay,
            bool expected)
        {
            Assert.Equal(expected, RecipeFallbackLogic.ShouldGrant(eligible, alreadyKnown, timeOfDay));
        }

        [Theory]
        [InlineData(0, 1000, false)]
        [InlineData(999, 1000, false)]
        [InlineData(1000, 1000, true)]
        [InlineData(1250, 1250, true)]
        public void HasFriendship_ComparesPoints(int points, int required, bool expected)
        {
            Assert.Equal(expected, RecipeFallbackLogic.HasFriendship(points, required));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 250)]
        [InlineData(4, 1000)]
        [InlineData(6, 1500)]
        [InlineData(-1, 0)]
        public void HeartsToPoints_UsesNative250PointsPerHeart(int hearts, int expected)
        {
            Assert.Equal(expected, RecipeFallbackLogic.HeartsToPoints(hearts));
        }

        [Theory]
        [InlineData(true, false, false, true)]
        [InlineData(false, true, false, true)]
        [InlineData(false, false, true, true)]
        [InlineData(false, false, false, false)]
        public void CanUseOwnGreenhouseFallback_RequiresOwnProgress(
            bool ownGreenhouseUnlocked,
            bool ownPantryMail,
            bool completedCommunityCenter,
            bool expected)
        {
            Assert.Equal(
                expected,
                RecipeFallbackLogic.CanUseOwnGreenhouseFallback(ownGreenhouseUnlocked, ownPantryMail, completedCommunityCenter));
        }

        [Theory]
        [InlineData(true, false, true)]
        [InlineData(false, true, true)]
        [InlineData(true, true, true)]
        [InlineData(false, false, false)]
        public void HasMailOrEvent_AcceptsEitherPath(bool hasMail, bool sawEvent, bool expected)
        {
            Assert.Equal(expected, RecipeFallbackLogic.HasMailOrEvent(hasMail, sawEvent));
        }
    }
}
