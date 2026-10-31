using Pelican4Ever.Core.Festivals;
using Pelican4Ever.Core.Players;
using Xunit;

namespace Pelican4Ever.Tests;

public class FestivalPresenceAndPrizeTests
{
    [Fact]
    public void Presence_TracksEnterAndLeavePerFestival()
    {
        var presence = new FestivalPresence();
        presence.Enter("festival_summer11", 20L);
        presence.Enter("festival_summer11", 10L);
        presence.Leave(20L);

        Assert.Equal(new[] { 10L }, presence.Present("festival_summer11"));
        Assert.Empty(presence.Present("festival_summer28"));
    }

    [Fact]
    public void Presence_ANewFestivalForgetsThePreviousOne()
    {
        var presence = new FestivalPresence();
        presence.Enter("festival_summer11", 10L);
        presence.Enter("festival_summer28", 20L);

        Assert.Equal(new[] { 20L }, presence.Present("festival_summer28"));
        Assert.Empty(presence.Present("festival_summer11"));
    }

    [Theory]
    [InlineData("spring13", true, new[] { "(H)4" })]
    [InlineData("festival_winter8", true, new[] { "(H)17", "(O)687", "(O)691", "(O)703" })]
    [InlineData("spring13", false, new[] { "(O)PrizeTicket" })]
    [InlineData("winter8", false, new[] { "(O)PrizeTicket" })]
    public void PrizeItems_MatchVanillaAwardFestivalPrize(string festivalId, bool firstWin, string[] expected)
    {
        Assert.Equal(expected, FestivalPrizeLogic.PrizeItems(festivalId, firstWin));
    }

    [Theory]
    [InlineData("spring13", "Egg Festival")]
    [InlineData("festival_winter8", "Ice Festival")]
    [InlineData("summer11", null)]
    public void FirstWinMailFlag_MatchesVanilla(string festivalId, string? expected)
    {
        Assert.Equal(expected, FestivalPrizeLogic.FirstWinMailFlag(festivalId));
    }

    [Fact]
    public void PrizeKey_RoundTripsFestivalAndDedupesInList()
    {
        string key = FestivalPrizeLogic.PrizeKey("festival_spring13", 12);

        Assert.Equal("spring13@12", key);
        Assert.Equal("spring13", FestivalPrizeLogic.FestivalOfKey(key));

        string list = FestivalPrizeLogic.AddToList(null, key);
        list = FestivalPrizeLogic.AddToList(list, key);
        Assert.Equal("spring13@12", list);
        Assert.True(FestivalPrizeLogic.ListContains(list, key));
        Assert.False(FestivalPrizeLogic.ListContains(list, "winter8@40"));
    }

    [Fact]
    public void PlayerRecord_StartsWithoutPendingPrizes()
    {
        Assert.Empty(new PlayerRecord().PendingFestivalPrizes);
    }
}
