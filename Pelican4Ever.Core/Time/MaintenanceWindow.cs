using System;

namespace Pelican4Ever.Core.Time;

/// <summary>
/// Ventana de mantenimiento diaria que empieza en el cambio de fecha (02:00) y dura un número fijo de minutos.
/// Durante ella el host rechaza conexiones nuevas: ahí caben el guardado, la copia, el avance de día y,
/// si la hay, la actualización del runtime.
/// </summary>
public static class MaintenanceWindow
{
    public const int MaxMinutes = 180;

    /// <summary>True si <paramref name="timeOfDay"/> cae en [start, start + minutes). Con minutes ≤ 0 no hay ventana.</summary>
    public static bool IsActive(TimeSpan timeOfDay, TimeSpan start, int minutes)
    {
        if (minutes <= 0)
            return false;
        if (timeOfDay < TimeSpan.Zero || timeOfDay >= TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(timeOfDay));

        TimeSpan elapsed = timeOfDay - start;
        if (elapsed < TimeSpan.Zero)
            elapsed += TimeSpan.FromDays(1);
        return elapsed < TimeSpan.FromMinutes(minutes);
    }

    /// <summary>Hora (del día) a la que termina la ventana.</summary>
    public static TimeSpan EndsAt(TimeSpan start, int minutes) =>
        TimeSpan.FromMinutes((start.TotalMinutes + Math.Max(0, minutes)) % 1440);
}
