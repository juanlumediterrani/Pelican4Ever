using Pelican4Ever.Core.Admin;
using Xunit;

namespace Pelican4Ever.Tests;

public class RolloverDayGuardTests
{
    [Theory]
    [InlineData(-1, -1, false)] // fresh Spring 1 has no ended day
    [InlineData(-1, 0, true)]  // first real rollover closes day 0
    [InlineData(0, 0, false)]  // reloading the saved new day must not replay penalties
    [InlineData(2, 1, false)]  // never replay a day already committed
    [InlineData(0, 1, true)]   // process the next, not-yet-committed day
    public void EndOfDaySideEffectsAreIdempotent(int lastCommitted, int endedDay, bool expected) =>
        Assert.Equal(expected, RolloverDayGuard.ShouldProcessEndedDay(lastCommitted, endedDay));

    [Theory]
    // Regression: restart after rollover. The saved LastRolloverGameDay is stale (-1/0) while the
    // save already contains the new day, so the committed-day check alone would replay the penalty.
    [InlineData(true, 0, 1, false)]
    [InlineData(true, -1, 0, false)]
    [InlineData(true, 5, 9, false)]
    // A real rollover (not caused by loading) still processes the ended day exactly once.
    [InlineData(false, 0, 1, true)]
    [InlineData(false, 1, 1, false)]
    public void DayStartRaisedByLoadNeverAppliesConsequences(bool startedByLoad, int lastCommitted, int endedDay, bool expected) =>
        Assert.Equal(expected, RolloverDayGuard.ShouldProcessDayStart(startedByLoad, lastCommitted, endedDay));
}
