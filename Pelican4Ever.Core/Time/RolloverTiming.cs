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

    /// <summary>Tiempo que se deja al cambio de día nativo antes de volver a empujarlo.</summary>
    public static readonly TimeSpan DayAdvanceNudgeInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Decide si hay que volver a lanzar el fundido del cambio de día: el juego sigue esperando un fundido a negro
    /// (<c>Game1.newDay</c> sin consumir), no hay ninguno en curso y ha pasado el intervalo desde el último intento.
    /// </summary>
    public static bool ShouldNudgeDayAdvance(bool newDayPending, bool fadeInProgress, TimeSpan sinceLastAttempt) =>
        newDayPending && !fadeInProgress && sinceLastAttempt >= DayAdvanceNudgeInterval;
}
