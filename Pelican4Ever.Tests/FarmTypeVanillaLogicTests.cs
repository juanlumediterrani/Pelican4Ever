using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests;

public class FarmTypeVanillaLogicTests
{
    [Theory]
    [InlineData("Standard", 0)]
    [InlineData("Fishing", 1)]
    [InlineData("Foraging", 2)]
    [InlineData("Mining", 3)]
    [InlineData("Combat", 4)]
    [InlineData("FourCorners", 5)]
    [InlineData("Beach", 6)]
    [InlineData("Ranching", 7)]
    [InlineData("Desconocido", 0)]
    [InlineData(null, 0)]
    public void WhichFarmFor_MatchesVanillaIds(string? templateId, int expected)
    {
        Assert.Equal(expected, FarmTypeVanillaLogic.WhichFarmFor(templateId));
    }

    [Theory]
    // Valores de FarmHouse.GetStarterWallpaper / GetStarterFlooring
    [InlineData("Fishing", "11", "1")]
    [InlineData("Foraging", "92", "34")]
    [InlineData("Mining", "12", "18")]
    [InlineData("Combat", "95", "4")]
    [InlineData("FourCorners", "65", "5")]
    [InlineData("Beach", "106", "35")]
    [InlineData("Standard", null, null)]
    [InlineData("Ranching", null, null)]
    public void StarterStyleFor_UsesVanillaDefaultsWithoutMapProperties(string templateId, string? wallpaper, string? flooring)
    {
        FarmHouseStarterStyle style = FarmTypeVanillaLogic.StarterStyleFor(templateId, null, null);

        Assert.Equal(wallpaper, style.Wallpaper);
        Assert.Equal(flooring, style.Flooring);
    }

    [Fact]
    public void StarterStyleFor_MapPropertiesWin()
    {
        // Farm_Ranching define FarmHouseWallpaper y FarmHouseFlooring = 41
        FarmHouseStarterStyle style = FarmTypeVanillaLogic.StarterStyleFor("Ranching", "41", "41");
        Assert.Equal(new FarmHouseStarterStyle("41", "41"), style);

        FarmHouseStarterStyle mixed = FarmTypeVanillaLogic.StarterStyleFor("Beach", null, "7");
        Assert.Equal(new FarmHouseStarterStyle("106", "7"), mixed);
    }

    [Theory]
    [InlineData("Fishing", true)]
    [InlineData("Standard", false)]
    [InlineData("Beach", false)]
    [InlineData(null, false)]
    public void StartsWithFishSmoker_OnlyRiverland(string? templateId, bool expected)
    {
        Assert.Equal(expected, FarmTypeVanillaLogic.StartsWithFishSmoker(templateId));
    }

    [Theory]
    [InlineData("Maps\\Farm_Island", true)]
    [InlineData("Maps/Farm_Island", true)]
    [InlineData("Maps/Pelican4Ever_Annex_Beach_North", true)]
    [InlineData("Maps\\Farm", false)]
    [InlineData("Maps\\Farm_Island_Extra", false)]
    [InlineData("Maps/Pelican4Ever_Annex_Standard_East", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsBeachMap_RecognizesBeachResidencesAndAnnexes(string? mapPath, bool expected)
    {
        Assert.Equal(expected, FarmTypeVanillaLogic.IsBeachMap(mapPath));
    }
}
