using Pelican4Ever.Core.Time;
using Xunit;

namespace Pelican4Ever.Tests;

public class RolloverTimingTests
{
    private static readonly TimeSpan Cutoff = new(2, 0, 0);

    [Fact]
    public void WarningIsDueAtTenMinutesAndRemainsDueIfOneSecondTickWasDelayed()
    {
        Assert.True(RolloverTiming.IsWarningDue(TimeSpan.FromMinutes(10), 10));
        Assert.True(RolloverTiming.IsWarningDue(TimeSpan.FromMinutes(9.5), 10));
        Assert.False(RolloverTiming.IsWarningDue(TimeSpan.FromMinutes(10.01), 10));
        Assert.False(RolloverTiming.IsWarningDue(TimeSpan.Zero, 10));
    }

    [Fact]
    public void DateCutoffIsTwoAndDoesNotChangeSixAmClockStart()
    {
        Assert.True(RolloverTiming.IsBoundaryWindow(new TimeSpan(2, 0, 1), Cutoff));
        Assert.False(RolloverTiming.IsBoundaryWindow(new TimeSpan(2, 0, 3), Cutoff));
        Assert.Equal(TimeSpan.FromMinutes(10), RolloverTiming.UntilNextBoundary(new TimeSpan(1, 50, 0), Cutoff));
        Assert.Equal(600, GameClock.Read(new TimeSpan(6, 0, 0), new TimeSpan(6, 0, 0), new TimeSpan(1, 50, 0)).TimeOfDay);
    }

    [Fact]
    public void DayAdvanceIsNudgedOnlyWhenStillPendingWithoutFadeAfterTheInterval()
    {
        TimeSpan due = RolloverTiming.DayAdvanceNudgeInterval;
        Assert.True(RolloverTiming.ShouldNudgeDayAdvance(newDayPending: true, fadeInProgress: false, due));
        Assert.False(RolloverTiming.ShouldNudgeDayAdvance(newDayPending: true, fadeInProgress: false, due - TimeSpan.FromSeconds(1)));
        Assert.False(RolloverTiming.ShouldNudgeDayAdvance(newDayPending: true, fadeInProgress: true, due * 4));
        Assert.False(RolloverTiming.ShouldNudgeDayAdvance(newDayPending: false, fadeInProgress: false, due * 4));
    }
}
