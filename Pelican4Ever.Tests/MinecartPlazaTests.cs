using Pelican4Ever.Core.Config;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class MinecartPlazaTests
    {
        [Theory]
        [InlineData(1, false)]
        [InlineData(4, false)]
        [InlineData(5, true)]
        [InlineData(16, true)]
        public void ShouldIncludePlazaDestination_RequiresAtLeastOneBridge(int farmSlotCount, bool expected)
        {
            Assert.Equal(expected, MinecartPlazaLogic.ShouldIncludePlazaDestination(farmSlotCount));
        }

        [Fact]
        public void GetBridgeMinecartAction_ExcludesPlazaOnlyOnBridgeOne()
        {
            Assert.Equal(
                "MinecartTransport Default Pelican4Ever_Plaza",
                MinecartPlazaLogic.GetBridgeMinecartAction("Pelican4Ever_Bridge_1"));
            Assert.Equal(
                "MinecartTransport Default",
                MinecartPlazaLogic.GetBridgeMinecartAction("Pelican4Ever_Bridge_2"));
            Assert.Equal(
                "MinecartTransport Default",
                MinecartPlazaLogic.GetBridgeMinecartAction("Pelican4Ever_Bridge_3"));
        }

        [Fact]
        public void ServerConfig_BypassMinecartUnlockRequirement_DefaultsToFalse()
        {
            var cfg = new ServerConfig();
            Assert.False(cfg.BypassMinecartUnlockRequirement);
        }
    }
}
