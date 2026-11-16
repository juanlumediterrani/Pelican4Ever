using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public sealed class HostEventRelayLogicTests
    {
        [Theory]
        [InlineData("Town", "611439/j 4/t 800 1300/w sunny/a 0 54/H", "distantBanjo/53 23/farmer 53 31 0/broadcastEvent/addMailReceived ccDoorUnlock", true)]
        [InlineData("Town", "191393/Hn ccFishTank/w sunny/H", "junimoStarSong/-1000 -1000/farmer -100 -100 0/broadcastEvent/skippable", true)]
        [InlineData("Town", "502261/J/w sunny/H", "sweet/-1000 -1000/farmer 53 34 0/broadcastEvent/pause 500", true)]
        [InlineData("SeedShop", "3102768/j 15/H", "playful/6 21/farmer 6 29 0/broadcastEvent/skippable", true)]
        [InlineData("Farm", "66/e 295672/t 600 700/H", "continue/64 15/farmer 64 16 2/broadcastEvent/rustyKey/skippable", true)]
        [InlineData("Farm", "690006/n slimeHutchBuilt/H", "continue/64 15/farmer 64 16 2/broadcastEvent/skippable", true)]
        [InlineData("Farm", "63/y 2/t 600 1800/H", "continue/64 15/farmer 64 16 2/broadcastEvent/skippable", true)]
        [InlineData("Farm", "102/z spring/y 1/H", "continue/64 15/farmer 64 16 2/broadcastEvent/skippable", true)]
        [InlineData("Farm", "65/m 25000/t 600 1200/H", "continue/64 15/farmer 64 16 2/pause 1500", false)]
        [InlineData("Farm", "1590166/m 1000/h cat/H", "continue/64 15/broadcastEvent", false)]
        [InlineData("Farm", "897405/m 1000/h dog/H", "continue/64 15/broadcastEvent", false)]
        [InlineData("Town", "12345/f Lewis 500/h cat", "playful/0 0/broadcastEvent", false)]
        [InlineData("WizardHouse", "112/n seenJunimoNote", "WizardSong/-1000 -1000/broadcastEvent", false)]
        [InlineData("Town", "611439/j 4/H", "distantBanjo/53 23/pause 500", false)]
        public void IsRelayableHostBroadcastEvent_MatchesExpectedEvents(
            string location,
            string key,
            string script,
            bool expected)
        {
            Assert.Equal(expected, HostEventRelayLogic.IsRelayableHostBroadcastEvent(location, key, script));
        }

        [Theory]
        [InlineData(false, false, "Town", true)]
        [InlineData(false, false, "SeedShop", true)]
        [InlineData(false, false, "Farm", true)]
        [InlineData(false, false, "WizardHouse", false)]
        [InlineData(true, false, "Town", false)]
        [InlineData(false, true, "Town", false)]
        public void ShouldClientNotifyWarp_RespectsRoleAndDedicatedHost(
            bool isMainPlayer,
            bool isDedicatedHost,
            string location,
            bool expected)
        {
            Assert.Equal(expected, HostEventRelayLogic.ShouldClientNotifyWarp(isMainPlayer, isDedicatedHost, location));
        }

        [Theory]
        [InlineData(true, false, true, false, false, false, "Town", true)]
        [InlineData(false, false, true, false, false, false, "Town", false)]
        [InlineData(true, true, true, false, false, false, "Town", false)]
        [InlineData(true, false, false, false, false, false, "Town", false)]
        [InlineData(true, false, true, true, false, false, "Town", false)]
        [InlineData(true, false, true, false, true, false, "Town", false)]
        [InlineData(true, false, true, false, false, true, "Town", false)]
        [InlineData(true, false, true, false, false, false, "Saloon", false)]
        public void CanHostRelayWarp_RequiresIdleDedicatedNonNativeHost(
            bool isMainPlayer,
            bool isDedicatedHost,
            bool dedicatedServerMode,
            bool isEventActive,
            bool isWarping,
            bool isNewDay,
            string location,
            bool expected)
        {
            Assert.Equal(
                expected,
                HostEventRelayLogic.CanHostRelayWarp(
                    isMainPlayer,
                    isDedicatedHost,
                    dedicatedServerMode,
                    isEventActive,
                    isWarping,
                    isNewDay,
                    location));
        }
    }
}
