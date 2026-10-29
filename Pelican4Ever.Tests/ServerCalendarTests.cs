using Pelican4Ever.Core.Time;
using Xunit;

namespace Pelican4Ever.Tests;

public class ServerCalendarTests
{
    private static readonly DateTime Epoch = new(2026, 11, 2); // lunes
    private static readonly TimeSpan DayStart = new(6, 0, 0);
    private readonly ServerCalendar calendar = new(Epoch, DayStart);

    [Fact]
    public void Constructor_RejectsNonMondayEpoch()
    {
        Assert.Throws<ArgumentException>(() => new ServerCalendar(new DateTime(2026, 11, 3), DayStart));
    }

    [Fact]
    public void EpochMorning_IsSpring1Year1()
    {
        Assert.Equal(new GameDate(1, Season.Spring, 1), calendar.GetDate(Epoch.AddHours(6)));
    }

    [Fact]
    public void BeforeDayStart_StillBelongsToPreviousGameDay()
    {
        DateTime tuesday0559 = Epoch.AddDays(1).AddHours(5).AddMinutes(59);
        DateTime tuesday0600 = Epoch.AddDays(1).AddHours(6);

        Assert.Equal(1, calendar.GetDate(tuesday0559).Day);
        Assert.Equal(2, calendar.GetDate(tuesday0600).Day);
    }

    [Fact]
    public void EverySeasonStartsOnMonday()
    {
        for (int totalDays = 0; totalDays < GameDate.DaysPerYear * 3; totalDays++)
        {
            DateTime real = Epoch.AddDays(totalDays).AddHours(12);
            GameDate date = calendar.GetDate(real);
            if (date.Day == 1)
                Assert.Equal(DayOfWeek.Monday, real.DayOfWeek);
            if (date.Day % 7 == 0)
                Assert.Equal(DayOfWeek.Sunday, real.DayOfWeek);
        }
    }

    [Theory]
    [InlineData(0, 1, Season.Spring, 1)]
    [InlineData(27, 1, Season.Spring, 28)]
    [InlineData(28, 1, Season.Summer, 1)]
    [InlineData(111, 1, Season.Winter, 28)]
    [InlineData(112, 2, Season.Spring, 1)]
    public void GameDate_FromTotalDays(int totalDays, int year, Season season, int day)
    {
        GameDate date = GameDate.FromTotalDays(totalDays);

        Assert.Equal(new GameDate(year, season, day), date);
        Assert.Equal(totalDays, date.TotalDays);
    }

    [Fact]
    public void PlanCatchUp_UpToDate_DoesNothing()
    {
        CatchUpPlan plan = calendar.PlanCatchUp(savedTotalDays: 10, Epoch.AddDays(10).AddHours(9), maxCatchUp: 3);

        Assert.Equal(new CatchUpPlan(0, null, false), plan);
    }

    [Fact]
    public void PlanCatchUp_SmallGap_RunsMissingRollovers()
    {
        CatchUpPlan plan = calendar.PlanCatchUp(savedTotalDays: 8, Epoch.AddDays(10).AddHours(9), maxCatchUp: 3);

        Assert.Equal(new CatchUpPlan(2, null, false), plan);
    }

    [Fact]
    public void PlanCatchUp_LargeGap_RunsOneRolloverAndSkips()
    {
        CatchUpPlan plan = calendar.PlanCatchUp(savedTotalDays: 2, Epoch.AddDays(10).AddHours(9), maxCatchUp: 3);

        Assert.Equal(new CatchUpPlan(1, 10, false), plan);
    }

    [Fact]
    public void PlanCatchUp_SaveAhead_IsFlagged()
    {
        CatchUpPlan plan = calendar.PlanCatchUp(savedTotalDays: 12, Epoch.AddDays(10).AddHours(9), maxCatchUp: 3);

        Assert.True(plan.IsAhead);
        Assert.Equal(0, plan.RolloversToRun);
    }
}
