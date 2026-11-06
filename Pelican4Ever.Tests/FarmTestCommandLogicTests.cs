using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests;

public class FarmTestCommandLogicTests
{
    [Theory]
    [InlineData("03", "Pelican4Ever_Farm_03")]
    [InlineData("3", "Pelican4Ever_Farm_03")]
    [InlineData("12", "Pelican4Ever_Farm_12")]
    [InlineData("Pelican4Ever_Farm_07", "Pelican4Ever_Farm_07")]
    [InlineData("03/North", "Pelican4Ever_Annex_03_North")]
    [InlineData("3/south", "Pelican4Ever_Annex_03_South")]
    [InlineData("Pelican4Ever_Annex_05_East", "Pelican4Ever_Annex_05_East")]
    [InlineData("host", "Farm")]
    [InlineData("Farm", "Farm")]
    public void TryParseTarget_AcceptsSlotsAnnexesAndHost(string text, string expectedLocation)
    {
        Assert.True(FarmTestCommandLogic.TryParseTarget(text, out FarmTestTarget target));
        Assert.Equal(expectedLocation, target.LocationName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("100")]
    [InlineData("-1")]
    [InlineData("03/West")]
    [InlineData("03/")]
    [InlineData("Pelican4Ever_Annex_03")]
    public void TryParseTarget_RejectsInvalidText(string? text)
    {
        Assert.False(FarmTestCommandLogic.TryParseTarget(text, out _));
    }

    [Theory]
    [InlineData("beach", "Beach")]
    [InlineData("FOURCORNERS", "FourCorners")]
    [InlineData("Meadowlands", "Ranching")]
    [InlineData("Ranching", "Ranching")]
    public void TryParseFarmType_NormalizesToCatalogIds(string text, string expected)
    {
        Assert.True(FarmTestCommandLogic.TryParseFarmType(text, out string id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData("Farm_Beach")]
    [InlineData("Island")]
    [InlineData("")]
    public void TryParseFarmType_RejectsUnknownTypes(string text)
    {
        Assert.False(FarmTestCommandLogic.TryParseFarmType(text, out _));
    }

    [Theory]
    [InlineData("farming", 0)]
    [InlineData("Mining", 3)]
    [InlineData("COMBAT", 4)]
    [InlineData("luck", 5)]
    public void TryParseSkill_MapsToFarmerExperienceIndex(string text, int expected)
    {
        Assert.True(FarmTestCommandLogic.TryParseSkill(text, out int index));
        Assert.Equal(expected, index);
    }

    [Fact]
    public void TryParseSkill_RejectsUnknown()
    {
        Assert.False(FarmTestCommandLogic.TryParseSkill("magic", out _));
        Assert.False(FarmTestCommandLogic.TryParseSkill(null, out _));
    }

    [Theory]
    // Farmer.getBaseExperienceForLevel
    [InlineData(0, 0)]
    [InlineData(1, 100)]
    [InlineData(4, 1300)]
    [InlineData(8, 6900)]
    [InlineData(9, 10000)]
    [InlineData(10, 15000)]
    public void ExperienceForLevel_MatchesVanillaTable(int level, int expected)
    {
        Assert.Equal(expected, FarmTestCommandLogic.ExperienceForLevel(level));
    }

    [Theory]
    [InlineData(null, 1, true, 1)]
    [InlineData("5", 1, true, 5)]
    [InlineData("0", 1, false, 0)]
    [InlineData("51", 1, false, 51)]
    [InlineData("x", 1, false, 0)]
    public void TryParseCount_IsBounded(string? text, int fallback, bool ok, int expected)
    {
        Assert.Equal(ok, FarmTestCommandLogic.TryParseCount(text, fallback, 1, 50, out int count));
        Assert.Equal(expected, count);
    }

    [Fact]
    public void TryParseEnums_AcceptNamesNotNumbers()
    {
        Assert.True(FarmTestCommandLogic.TryParseEntry("cave", out FarmTestEntry entry));
        Assert.Equal(FarmTestEntry.Cave, entry);
        Assert.False(FarmTestCommandLogic.TryParseEntry("1", out _));
        Assert.True(FarmTestCommandLogic.TryParseSpawn("Crate", out FarmTestSpawn spawn));
        Assert.Equal(FarmTestSpawn.Crate, spawn);
        Assert.False(FarmTestCommandLogic.TryParseSpawn("trees", out _));
        Assert.True(FarmTestCommandLogic.TryParseWeather("storm", out FarmTestWeather weather));
        Assert.Equal("Storm", FarmTestCommandLogic.VanillaWeatherId(weather));
        Assert.Equal("Sun", FarmTestCommandLogic.VanillaWeatherId(FarmTestWeather.Sun));
    }
}
