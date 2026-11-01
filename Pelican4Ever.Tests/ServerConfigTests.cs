using Pelican4Ever.Core.Config;
using Xunit;

namespace Pelican4Ever.Tests;

public class ServerConfigTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        var config = new ServerConfig();

        Assert.Empty(config.Validate());
        Assert.Equal(new TimeSpan(6, 0, 0), config.ParsedDayStart);
        Assert.Equal(new TimeSpan(1, 50, 0), config.ParsedNightHoldStart);
        Assert.Equal(DayOfWeek.Monday, config.ParsedCalendarEpoch.DayOfWeek);
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("6am")]
    [InlineData("")]
    public void InvalidTimes_AreReported(string value)
    {
        var config = new ServerConfig { NightHoldStartTime = value };

        Assert.NotEmpty(config.Validate());
    }

    [Fact]
    public void DayStartOtherThanSix_IsRejected()
    {
        var config = new ServerConfig { DayStartTime = "05:30" };

        Assert.Contains(config.Validate(), e => e.Contains(nameof(ServerConfig.DayStartTime)));
    }

    [Fact]
    public void HoldAfter0150_IsRejected()
    {
        var config = new ServerConfig { NightHoldStartTime = "02:30" };

        Assert.Contains(config.Validate(), e => e.Contains(nameof(ServerConfig.NightHoldStartTime)));
    }

    [Fact]
    public void NonMondayEpoch_IsRejected()
    {
        var config = new ServerConfig { CalendarEpoch = "2026-11-04" };

        Assert.Contains(config.Validate(), e => e.Contains("lunes"));
    }

    [Fact]
    public void NegativeSittingOrMineCooldown_IsRejected()
    {
        var config = new ServerConfig
        {
            SittingRegenSeconds = -1,
            MineFloorCooldownMinutes = -5,
            OfflineRegenFullBarHours = -2.0f,
            TravelingCartClosingTime = 2800
        };

        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.SittingRegenSeconds)));
        Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.MineFloorCooldownMinutes)));
        Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.OfflineRegenFullBarHours)));
        Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.TravelingCartClosingTime)));
    }

    [Fact]
    public void ModulesAreEnabledUnlessExplicitlyDisabled()
    {
        var config = new ServerConfig();
        config.Modules["Festivals"] = false;

        Assert.True(config.IsModuleEnabled("Persistence"));
        Assert.False(config.IsModuleEnabled("Festivals"));
    }
}
