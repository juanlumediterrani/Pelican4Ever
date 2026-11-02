using System;

namespace Pelican4Ever.Core.Time;

public static class RolloverTiming
{
    public static TimeSpan UntilNextBoundary(TimeSpan now, TimeSpan boundary)
    {
        if (now < TimeSpan.Zero || now >= TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(now));
        if (boundary < TimeSpan.Zero || boundary >= TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(boundary));

        TimeSpan remaining = boundary - now;
        return remaining < TimeSpan.Zero ? remaining + TimeSpan.FromDays(1) : remaining;
    }

    public static bool IsWarningDue(TimeSpan remaining, int warningMinutes) =>
        warningMinutes > 0 && remaining > TimeSpan.Zero && remaining <= TimeSpan.FromMinutes(warningMinutes);

    public static bool IsBoundaryWindow(TimeSpan now, TimeSpan boundary, int secondsTolerance = 2) =>
        now >= boundary && now <= boundary.Add(TimeSpan.FromSeconds(secondsTolerance));
}
