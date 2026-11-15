using Pelican4Ever.Core.Config;
using Pelican4Ever.Core.Economy;
using Xunit;

namespace Pelican4Ever.Tests;

public class SkillExperienceCurveTests
{
    [Fact]
    public void SplitsEventAtThresholdAfterFixedMultiplier()
    {
        var result = SkillExperienceCurve.Apply(290, 20, 1m, 300, 0.25m, 0);
        Assert.Equal(12, result.GrantedXp);
        Assert.Equal(310, result.EarnedToday);
        Assert.Equal(0.5m, result.Remainder);

        result = SkillExperienceCurve.Apply(299, 4, 0.5m, 300, 0.25m, 0);
        Assert.Equal(1, result.GrantedXp);
        Assert.Equal(301, result.EarnedToday);
        Assert.Equal(0.25m, result.Remainder);
    }

    [Fact]
    public void SmallOverflowEventsPreserveFractions()
    {
        decimal earned = 300, remainder = 0;
        int granted = 0;
        for (int i = 0; i < 4; i++)
        {
            var result = SkillExperienceCurve.Apply(earned, 1, 1, 300, 0.25m, remainder);
            earned = result.EarnedToday;
            remainder = result.Remainder;
            granted += result.GrantedXp;
        }
        Assert.Equal(1, granted);
        Assert.Equal(0, remainder);
    }

    [Fact]
    public void NoCapAndNeutralValuesPreserveExperience()
    {
        Assert.Equal(42, SkillExperienceCurve.Apply(500, 42, 1, 0, 0.25m, 0).GrantedXp);
        Assert.Equal(42, SkillExperienceCurve.Apply(500, 42, 1, 300, 1, 0).GrantedXp);
        Assert.Equal(42, SkillExperienceCurve.Apply(0, 42, 1, 300, 0.25m, 0).GrantedXp);
    }

    [Fact]
    public void InvalidConfigurationIsRejected()
    {
        var config = new ServerConfig { SkillOverflowXpMultiplier = -1 };
        config.SkillDailyFullXp["Unknown"] = 3;
        config.SkillDailyFullXp["Mining"] = -1;
        config.SkillXpMultiplier["Fishing"] = -0.1m;
        config.SkillXpMultiplier["unknown"] = 1;
        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains(nameof(config.SkillOverflowXpMultiplier)));
        Assert.Equal(2, errors.Count(e => e.Contains(nameof(config.SkillDailyFullXp))));
        Assert.Equal(2, errors.Count(e => e.Contains(nameof(config.SkillXpMultiplier))));
    }
}
