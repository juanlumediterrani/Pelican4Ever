using Pelican4Ever.Core.Admin;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class OperationalResilienceTests
    {
        [Theory]
        [InlineData(0.0, 120, true)]
        [InlineData(119.9, 120, true)]
        [InlineData(120.0, 120, true)]
        [InlineData(120.1, 120, false)]
        [InlineData(500.0, 0, true)]
        [InlineData(double.NaN, 120, false)]
        [InlineData(double.PositiveInfinity, 120, false)]
        public void WatchdogPolicy_ShouldSendPulse_RespectsHangThreshold(
            double secondsSinceLastTick,
            int hangThresholdSeconds,
            bool expected)
        {
            Assert.Equal(expected, WatchdogPolicy.ShouldSendPulse(secondsSinceLastTick, hangThresholdSeconds));
        }

        [Theory]
        [InlineData("NatureInTheValley.Creatures.MonarchButterfly", true)]
        [InlineData("NatureInTheValley.CustomCreature", true)]
        [InlineData("StardewValley.Characters.Junimo", false)]
        [InlineData("NatureInTheValley.Items.Net", false)]
        [InlineData(null, false)]
        [InlineData("", false)]
        public void LoadInstrumentation_IsNitvCreatureTypeName_DetectsOnlyNitvCreatures(
            string? fullTypeName,
            bool expected)
        {
            Assert.Equal(expected, LoadInstrumentationLogic.IsNitvCreatureTypeName(fullTypeName));
        }

        [Fact]
        public void LoadInstrumentation_Helpers_CalculateDeterministicValues()
        {
            Assert.Equal(16384L + 10 * 512L + 20 * 64L + 2 * 256L, LoadInstrumentationLogic.EstimateFarmhandBytes(10, 20, 2));
            Assert.Equal(16384L, LoadInstrumentationLogic.EstimateFarmhandBytes(-5, -1, -2));
            Assert.Equal(1500L, LoadInstrumentationLogic.SumByteSizes(new long[] { 1000L, -10L, 0L, 500L }));
            Assert.Equal(0L, LoadInstrumentationLogic.SumByteSizes(null));
            Assert.Equal("spring 14, Year 2", LoadInstrumentationLogic.FormatGameDate("spring", 14, 2));
            Assert.Equal("unknown 1, Year 1", LoadInstrumentationLogic.FormatGameDate(null, 1, 1));
        }
    }
}
