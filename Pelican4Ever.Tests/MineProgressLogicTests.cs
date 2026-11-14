using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests;

public class MineProgressLogicTests
{
    [Theory]
    [InlineData(0, -1, 0)]
    [InlineData(37, -1, 37)]
    [InlineData(145, -1, 120)]
    [InlineData(80, 30, 30)]
    [InlineData(30, 80, 30)]
    [InlineData(120, 120, 120)]
    public void ElevatorUsesPersonalDepthAndActiveOrderLimit(int personalDepth, int orderDepth, int expected)
    {
        Assert.Equal(expected, MineProgressLogic.ElevatorLevel(personalDepth, orderDepth));
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(100, false)]
    [InlineData(120, false)]
    [InlineData(121, true)]
    [InlineData(131, true)]
    [InlineData(220, true)]
    [InlineData(320, true)]
    [InlineData(420, true)]
    public void ResetsOnlySkullCavernChestsAbove120OnNewDay(int mineLevel, bool expected)
    {
        Assert.Equal(expected, MineProgressLogic.ShouldResetMineChestOnNewDay(mineLevel));
    }
}
