using System.Text.Json;
using Pelican4Ever.Core.Economy;
using Pelican4Ever.Core.Players;
using Xunit;

namespace Pelican4Ever.Tests;

public class DailySkillExperienceTests
{
    [Fact]
    public void SaveAndReconnectPreserveCapAndFractionUntilNextDay()
    {
        var record = new PlayerRecord();
        Assert.Equal(300, record.SkillExperience.Apply(12, "Fishing", 300, 1, 300, 0.25m));
        Assert.Equal(0, record.SkillExperience.Apply(12, "Fishing", 1, 1, 300, 0.25m));
        var restored = JsonSerializer.Deserialize<PlayerRecord>(JsonSerializer.Serialize(record))!;
        Assert.Equal(0, restored.SkillExperience.Apply(12, "Fishing", 1, 1, 300, 0.25m));
        Assert.Equal(0, restored.SkillExperience.Apply(12, "Fishing", 1, 1, 300, 0.25m));
        Assert.Equal(1, restored.SkillExperience.Apply(12, "Fishing", 1, 1, 300, 0.25m));
        Assert.Equal(1, restored.SkillExperience.Apply(13, "Fishing", 1, 1, 300, 0.25m));
        Assert.Equal(1, restored.SkillExperience.Skills["Fishing"].EarnedToday);
    }

    [Fact]
    public void ReportsCannotRollBackOrCrossDaysAndInvalidEntriesAreIgnored()
    {
        var host = new DailySkillExperience();
        host.Apply(5, "Mining", 301, 1, 300, 0.25m);
        var old = new DailySkillExperience { GameDay = 5, Skills = new() { ["Mining"] = new() { EarnedToday = 300 } } };
        host.MergeReport(old, 5);
        Assert.Equal(301, host.Skills["Mining"].EarnedToday);
        var invalid = new DailySkillExperience { GameDay = 5, Skills = new() {
            ["Mining"] = new() { EarnedToday = 500, Remainder = 1 },
            ["Unknown"] = new() { EarnedToday = 2 },
            ["Fishing"] = new() { EarnedToday = -1 } } };
        host.MergeReport(invalid, 5);
        Assert.Single(host.Skills);
        invalid.GameDay = 4;
        invalid.Skills["Mining"].Remainder = 0;
        host.MergeReport(invalid, 5);
        Assert.Equal(301, host.Skills["Mining"].EarnedToday);
        host.MergeReport(old, 6);
        Assert.Empty(host.Skills);
    }
}
