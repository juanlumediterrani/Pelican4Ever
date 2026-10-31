using Pelican4Ever.Core.Festivals;
using Xunit;

namespace Pelican4Ever.Tests;

public class FestivalEntryScriptTests
{
    // Inicio real de Data/Festivals/spring13 (Fiesta del Huevo), Stardew 1.6.15
    private static readonly string[] EggFestivalSetUp =
    {
        "fallFest", "27 60", "farmer 12 54 1", "changeToTemporaryMap Town-EggFestival", "viewport -1000 -1000",
        "loadActors Set-Up", "advancedMove Vincent true -2 0 0 -2 2 0 0 2", "playerControl eggFestival",
        "viewport player clamp true unfreeze", "globalFadeToClear"
    };

    [Fact]
    public void KeepPlayerInPlace_FollowsPlayerAndKeepsArrivalTile()
    {
        string[] script = FestivalEntryScript.KeepPlayerInPlace(EggFestivalSetUp, 1, 89, 1);

        Assert.Equal("fallFest", script[0]);
        Assert.Equal("follow", script[1]);
        Assert.Equal("farmer 1 89 1", script[2]);
    }

    [Fact]
    public void KeepPlayerInPlace_DropsHiddenViewportAndFadeButKeepsTheRest()
    {
        string[] script = FestivalEntryScript.KeepPlayerInPlace(EggFestivalSetUp, 1, 89, 1);

        Assert.DoesNotContain("viewport -1000 -1000", script);
        Assert.DoesNotContain("globalFadeToClear", script);
        Assert.Contains("changeToTemporaryMap Town-EggFestival", script);
        Assert.Contains("loadActors Set-Up", script);
        Assert.Contains("playerControl eggFestival", script);
        Assert.Contains("viewport player clamp true unfreeze", script);
    }

    [Theory]
    [InlineData("viewport -1000 -1000", true)]
    [InlineData("viewport -1000 01000", true)]   // errata real en summer28 (set-up_y2)
    [InlineData("viewport player clamp true unfreeze", false)]
    [InlineData("viewport 27 67 true unfreeze", false)]
    [InlineData("playerControl eggFestival", false)]
    public void IsHiddenViewport_DetectsOffscreenCamera(string command, bool expected)
    {
        Assert.Equal(expected, FestivalEntryScript.IsHiddenViewport(command));
    }

    [Fact]
    public void KeepPlayerInPlace_OnlyMovesTheFarmerInMixedPositions()
    {
        string[] script = FestivalEntryScript.KeepPlayerInPlace(new[] { "none", "10 10", "Lewis 5 6 2 farmer 12 54 1" }, 3, 4, 0);

        Assert.Equal("Lewis 5 6 2 farmer 3 4 0", script[2]);
    }

    [Fact]
    public void TemporaryMapName_FindsFestivalMap()
    {
        Assert.Equal("Town-EggFestival", FestivalEntryScript.TemporaryMapName(EggFestivalSetUp));
        Assert.Null(FestivalEntryScript.TemporaryMapName(new[] { "none", "10 10", "farmer 1 1 0" }));
    }
}
