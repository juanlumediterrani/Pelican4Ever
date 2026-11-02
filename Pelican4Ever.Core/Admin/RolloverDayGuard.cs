namespace Pelican4Ever.Core.Admin;

/// <summary>Prevents replaying end-of-day penalties when a saved day is loaded again.</summary>
public static class RolloverDayGuard
{
    public static bool ShouldProcessEndedDay(int lastRolloverGameDay, int endedGameDay) =>
        lastRolloverGameDay < endedGameDay;
}
