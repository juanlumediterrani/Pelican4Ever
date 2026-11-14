using System.Collections.Generic;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class FarmhandMailDeliveryLogicTests
    {
        [Fact]
        public void Plan_NoLetterFlag_GoesToMailReceived()
        {
            IReadOnlyList<PendingFarmhandMail> planned = FarmhandMailDeliveryLogic.Plan(
                new List<string?> { "copperFound%&NL&%" });

            PendingFarmhandMail mail = Assert.Single(planned);
            Assert.Equal("copperFound", mail.Id);
            Assert.False(mail.DeliverToMailbox);
        }

        [Fact]
        public void Plan_Letter_GoesToMailbox()
        {
            IReadOnlyList<PendingFarmhandMail> planned = FarmhandMailDeliveryLogic.Plan(
                new List<string?> { "spring_2_1" });

            PendingFarmhandMail mail = Assert.Single(planned);
            Assert.Equal("spring_2_1", mail.Id);
            Assert.True(mail.DeliverToMailbox);
        }

        [Fact]
        public void Plan_MixedEntries_PreservesDestinationAndSkipsDuplicates()
        {
            IReadOnlyList<PendingFarmhandMail> planned = FarmhandMailDeliveryLogic.Plan(
                new List<string?>
                {
                    "copperFound%&NL&%",
                    "hatter",
                    "copperFound%&NL&%",
                    " ",
                    null
                });

            Assert.Equal(2, planned.Count);
            Assert.Equal("copperFound", planned[0].Id);
            Assert.False(planned[0].DeliverToMailbox);
            Assert.Equal("hatter", planned[1].Id);
            Assert.True(planned[1].DeliverToMailbox);
        }

        [Fact]
        public void Plan_NullEntries_ReturnsEmpty()
        {
            Assert.Empty(FarmhandMailDeliveryLogic.Plan(null!));
        }

        [Fact]
        public void ShouldDeliverToFarmhand_IncludesKnownPlayerEvenIfUncustomized()
        {
            Assert.False(FarmhandMailDeliveryLogic.ShouldDeliverToFarmhand(isUnclaimedFarmhand: true, isKnownPlayer: false, pendingCount: 2));
            Assert.True(FarmhandMailDeliveryLogic.ShouldDeliverToFarmhand(isUnclaimedFarmhand: true, isKnownPlayer: true, pendingCount: 2));
            Assert.True(FarmhandMailDeliveryLogic.ShouldDeliverToFarmhand(isUnclaimedFarmhand: false, isKnownPlayer: false, pendingCount: 1));
            Assert.False(FarmhandMailDeliveryLogic.ShouldDeliverToFarmhand(isUnclaimedFarmhand: false, isKnownPlayer: true, pendingCount: 0));
        }
    }
}
