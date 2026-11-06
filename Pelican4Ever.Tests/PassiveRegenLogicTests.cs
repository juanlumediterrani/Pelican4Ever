using Pelican4Ever.Core.Economy;
using Xunit;

namespace Pelican4Ever.Tests;

public class PassiveRegenLogicTests
{
    [Fact]
    public void SpaBuff_Grants120PointsOver120Minutes()
    {
        double carry = 0;
        int total = 0;
        for (int second = 0; second < 120 * 60; second++)
            total += PassiveRegenLogic.TickOneSecond(ref carry, totalPoints: 120, durationMinutes: 120);

        Assert.Equal(120, total);
    }

    [Fact]
    public void SpaBuff_GrantsOnePointPerMinute()
    {
        double carry = 0;
        int total = 0;
        for (int second = 0; second < 59; second++)
            total += PassiveRegenLogic.TickOneSecond(ref carry, 120, 120);
        Assert.Equal(0, total);

        total += PassiveRegenLogic.TickOneSecond(ref carry, 120, 120);
        Assert.Equal(1, total);
    }

    [Fact]
    public void InvalidConfig_GrantsNothing()
    {
        double carry = 0.5;
        Assert.Equal(0, PassiveRegenLogic.TickOneSecond(ref carry, 0, 120));
        Assert.Equal(0, PassiveRegenLogic.TickOneSecond(ref carry, 120, 0));
    }
}
