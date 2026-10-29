using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class TownMailboxLogicTests
    {
        [Fact]
        public void GetMailboxCoords_WithCustomCoords_ReturnsCustomCoords()
        {
            var coords = TownMailboxLogic.GetMailboxCoords(50, 60, 52, 61);
            Assert.Equal((52, 61), coords);
        }

        [Fact]
        public void GetMailboxCoords_WithoutCustomCoords_DefaultsToRightTile()
        {
            var coords = TownMailboxLogic.GetMailboxCoords(50, 60, null, null);
            Assert.Equal((51, 60), coords);
        }

        [Theory]
        [InlineData(51, 60, 51, 60, true)]
        [InlineData(52, 60, 51, 60, true)]
        [InlineData(51, 61, 51, 60, true)]
        [InlineData(50, 59, 51, 60, true)]
        [InlineData(53, 60, 51, 60, false)]
        [InlineData(51, 62, 51, 60, false)]
        public void IsMailboxTarget_CorrectlyDetectsAdjacency(int targetX, int targetY, int mbX, int mbY, bool expected)
        {
            bool result = TownMailboxLogic.IsMailboxTarget(targetX, targetY, mbX, mbY);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void IsMailboxTarget_DoorTileTarget_ReturnsFalse()
        {
            // Puerta en (50, 60), buzón en (51, 60)
            bool onDoor = TownMailboxLogic.IsMailboxTarget(50, 60, 51, 60, 50, 60);
            Assert.False(onDoor);

            // Clic en el propio buzón (51, 60)
            bool onMailbox = TownMailboxLogic.IsMailboxTarget(51, 60, 51, 60, 50, 60);
            Assert.True(onMailbox);
        }

        [Fact]
        public void EvaluateAccess_UnassignedMailbox_ReturnsUnassignedStatus()
        {
            var result = TownMailboxLogic.EvaluateAccess(100L, null, null, 0);
            Assert.Equal(MailboxAccessStatus.Unassigned, result.Status);
            Assert.Contains("desocupada", result.Message);
        }

        [Fact]
        public void EvaluateAccess_OwnerWithPendingMail_ReturnsOwnerStatusWithMailCount()
        {
            var result = TownMailboxLogic.EvaluateAccess(100L, 100L, "Alice", 3);
            Assert.Equal(MailboxAccessStatus.Owner, result.Status);
            Assert.Contains("3", result.Message);
        }

        [Fact]
        public void EvaluateAccess_OwnerWithEmptyMailbox_ReturnsOwnerStatusEmpty()
        {
            var result = TownMailboxLogic.EvaluateAccess(100L, 100L, "Alice", 0);
            Assert.Equal(MailboxAccessStatus.Owner, result.Status);
            Assert.Contains("vacío", result.Message);
        }

        [Fact]
        public void EvaluateAccess_OtherPlayerMailbox_ReturnsOtherPlayerStatusWithNeighborName()
        {
            var result = TownMailboxLogic.EvaluateAccess(100L, 200L, "Bob", 5);
            Assert.Equal(MailboxAccessStatus.OtherPlayer, result.Status);
            Assert.Contains("Bob", result.Message);
            Assert.Contains("Solo su propietario", result.Message);
        }
    }
}
