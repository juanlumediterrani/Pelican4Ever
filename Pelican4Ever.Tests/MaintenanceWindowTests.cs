using Pelican4Ever.Core.Config;
using Pelican4Ever.Core.Time;
using Xunit;

namespace Pelican4Ever.Tests;

public class MaintenanceWindowTests
{
    private static readonly TimeSpan Start = new(2, 0, 0);

    [Theory]
    [InlineData(1, 59, 59, false)]
    [InlineData(2, 0, 0, true)]
    [InlineData(2, 12, 30, true)]
    [InlineData(2, 24, 59, true)]
    [InlineData(2, 25, 0, false)]
    [InlineData(6, 0, 0, false)]
    [InlineData(23, 59, 59, false)]
    public void DefaultWindow_CoversTwoToTwentyFive(int h, int m, int s, bool expected) =>
        Assert.Equal(expected, MaintenanceWindow.IsActive(new TimeSpan(h, m, s), Start, 25));

    [Fact]
    public void ZeroMinutes_DisablesTheWindow() =>
        Assert.False(MaintenanceWindow.IsActive(new TimeSpan(2, 5, 0), Start, 0));

    [Fact]
    public void WindowCrossingMidnight_Works()
    {
        var start = new TimeSpan(23, 50, 0);
        Assert.True(MaintenanceWindow.IsActive(new TimeSpan(0, 5, 0), start, 30));
        Assert.False(MaintenanceWindow.IsActive(new TimeSpan(0, 30, 0), start, 30));
    }

    [Fact]
    public void TimeOutsideADay_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MaintenanceWindow.IsActive(TimeSpan.FromDays(1), Start, 25));

    [Fact]
    public void EndsAt_IsStartPlusMinutes() =>
        Assert.Equal(new TimeSpan(2, 25, 0), MaintenanceWindow.EndsAt(Start, 25));

    [Fact]
    public void Config_DefaultIs25_AndIsValid()
    {
        var config = new ServerConfig();
        Assert.Equal(25, config.MaintenanceWindowMinutes);
        Assert.Empty(config.Validate());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(181)]
    public void Config_RejectsOutOfRange(int minutes)
    {
        var config = new ServerConfig { MaintenanceWindowMinutes = minutes };
        Assert.Contains(config.Validate(), e => e.Contains(nameof(ServerConfig.MaintenanceWindowMinutes)));
    }

    [Fact]
    public void Config_ZeroDisables_AndIsValid()
    {
        var config = new ServerConfig { MaintenanceWindowMinutes = 0 };
        Assert.Empty(config.Validate());
    }
}
