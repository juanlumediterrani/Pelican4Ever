using Pelican4Ever.Core.Time;
using Xunit;

namespace Pelican4Ever.Tests;

public class ServerCalendarTests
{
    private static readonly DateTime Epoch = new(2026, 11, 2); // lunes
    private static readonly TimeSpan CalendarDayChange = new(2, 0, 0);
    private readonly ServerCalendar calendar = new(Epoch, CalendarDayChange);

    [Fact]
    public void Constructor_RejectsNonMondayEpoch()
    {
        Assert.Throws<ArgumentException>(() => new ServerCalendar(new DateTime(2026, 11, 3), CalendarDayChange));
    }

    [Fact]
    public void EpochMorning_IsSpring1Year1()
    {
        Assert.Equal(new GameDate(1, Season.Spring, 1), calendar.GetDate(Epoch.AddHours(2)));
    }

    [Fact]
    public void TryGetDate_BeforeEpochReportsUnavailableInsteadOfThrowing()
    {
        Assert.False(calendar.TryGetDate(Epoch.AddDays(-1).AddHours(2), out _));
    }

    [Fact]
    public void CalendarDateChangesAtTwoWhileClockStartRemainsIndependent()
    {
        DateTime tuesday0159 = Epoch.AddDays(1).AddHours(1).AddMinutes(59);
        DateTime tuesday0200 = Epoch.AddDays(1).AddHours(2);

        Assert.Equal(1, calendar.GetDate(tuesday0159).Day);
        Assert.Equal(2, calendar.GetDate(tuesday0200).Day);
    }

    [Fact]
    public void EverySeasonStartsOnMonday()
    {
        for (int totalDays = 0; totalDays < GameDate.DaysPerYear * 3; totalDays++)
        {
            DateTime real = Epoch.AddDays(totalDays).AddHours(2);
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
