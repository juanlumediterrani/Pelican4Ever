namespace Pelican4Ever.Core.Admin;

/// <summary>Prevents replaying end-of-day consequences when a saved day is loaded again.</summary>
public static class RolloverDayGuard
{
    public static bool ShouldProcessEndedDay(int lastRolloverGameDay, int endedGameDay) =>
        lastRolloverGameDay < endedGameDay;

    /// <summary>
    /// The end-of-day save is written BEFORE DayStarted records LastRolloverGameDay, so a reloaded save
    /// always carries a stale value. A DayStarted raised by loading a save is therefore never a rollover:
    /// consequences were already applied by the process that advanced the day. Failing toward "skip"
    /// can never penalise a farmhand twice.
    /// </summary>
    public static bool ShouldProcessDayStart(bool startedByLoad, int lastRolloverGameDay, int endedGameDay) =>
        !startedByLoad && ShouldProcessEndedDay(lastRolloverGameDay, endedGameDay);
}
