using System;

namespace Pelican4Ever.Core.Time
{
    /// <summary>Hora local del servidor según la zona horaria configurada.</summary>
    public static class ServerTime
    {
        /// <summary>Resuelve una zona horaria IANA (ej. "Europe/Madrid"). Null o vacío = zona local del sistema.</summary>
        /// <exception cref="TimeZoneNotFoundException">Si el ID no existe.</exception>
        public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
        {
            return string.IsNullOrWhiteSpace(timeZoneId)
                ? TimeZoneInfo.Local
                : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }

        /// <summary>Convierte un instante UTC a la hora local de la zona indicada.</summary>
        public static DateTime ToLocal(DateTimeOffset utcNow, TimeZoneInfo zone)
        {
            return TimeZoneInfo.ConvertTime(utcNow, zone).DateTime;
        }

        /// <summary>Hora local actual en la zona configurada.</summary>
        public static DateTime LocalNow(string? timeZoneId)
        {
            return ToLocal(DateTimeOffset.UtcNow, ResolveTimeZone(timeZoneId));
        }
    }
}
