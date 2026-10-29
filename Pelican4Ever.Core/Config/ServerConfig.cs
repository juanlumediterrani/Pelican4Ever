using System;
using System.Collections.Generic;
using System.Globalization;

namespace Pelican4Ever.Core.Config
{
    /// <summary>
    /// Configuración del servidor serializada en <c>config.json</c>.
    /// Los tiempos se expresan como cadenas "HH:mm" (24h) para que el JSON sea legible;
    /// usa los accesores <c>Parsed*</c> tras llamar a <see cref="Validate"/>.
    /// </summary>
    public class ServerConfig
    {
        // ─────────────────────────── Tiempo y calendario ───────────────────────────

        /// <summary>
        /// Hora real a la que empieza el día de juego y se ejecuta el Rollover diario (HH:mm).
        /// Debe ser 06:00 para que el reloj del juego (que arranca a las 0600) quede alineado 1:1.
        /// </summary>
        public string DayStartTime { get; set; } = "06:00";

        /// <summary>
        /// Hora real a partir de la cual el reloj del juego se congela en 2550 ("Madrugada")
        /// hasta el siguiente Rollover. El motor no admite horas ≥ 2600 sin forzar el desmayo.
        /// </summary>
        public string NightHoldStartTime { get; set; } = "01:50";

        /// <summary>Minutos de antelación con los que se avisa del Rollover a los jugadores conectados.</summary>
        public int[] RolloverWarningMinutes { get; set; } = { 15, 5, 1 };

        /// <summary>
        /// Fecha real (yyyy-MM-dd, debe ser lunes) que corresponde al Día 1 de Primavera del Año 1.
        /// La fecha de juego se calcula siempre a partir de esta época, nunca se "incrementa".
        /// </summary>
        public string CalendarEpoch { get; set; } = "2026-11-02";

        /// <summary>
        /// Máximo de Rollovers que se ejecutan seguidos al arrancar si el servidor estuvo caído.
        /// Si el desfase es mayor, se salta directamente a la fecha actual.
        /// </summary>
        public int MaxCatchUpRollovers { get; set; } = 3;

        /// <summary>ID IANA de zona horaria (ej. "Europe/Madrid"). Vacío = hora local del sistema.</summary>
        public string TimeZoneId { get; set; } = "Europe/Madrid";

        // ─────────────────────────── Sueño y penalización ───────────────────────────

        /// <summary>Porcentaje del oro cobrado como gasto médico al no dormir (0–1).</summary>
        public float PenaltyGoldPercent { get; set; } = 0.10f;

        /// <summary>Tope del gasto médico.</summary>
        public int PenaltyGoldCap { get; set; } = 1000;

        /// <summary>Fracción de la energía máxima con la que se despierta tras una penalización (0–1).</summary>
        public float PenaltyEnergyFraction { get; set; } = 0.5f;

        // ─────────────────────────── Energía y comida ───────────────────────────

        /// <summary>
        /// Presupuesto diario de Saciedad, como fracción de la energía máxima del jugador.
        /// Es la energía instantánea total que la comida puede devolver en un día real.
        /// </summary>
        public float DailySatietyFraction { get; set; } = 0.5f;

        /// <summary>Máximo de platos cocinados por día que conceden el buff "Bien comido" (regeneración pasiva).</summary>
        public int MaxDailyWellFedBuffs { get; set; } = 2;

        /// <summary>Energía regenerada por minuto real mientras dura "Bien comido".</summary>
        public int WellFedRegenPerMinute { get; set; } = 1;

        /// <summary>Duración de "Bien comido" en minutos reales.</summary>
        public int WellFedDurationMinutes { get; set; } = 60;

        /// <summary>Segundos reales en el Spa para recuperar 1 punto de energía.</summary>
        public int SpaRegenSeconds { get; set; } = 30;

        /// <summary>Energía máxima recuperable en el Spa por día real.</summary>
        public int SpaDailyCap { get; set; } = 60;

        // ─────────────────────────── Economía ───────────────────────────

        /// <summary>Multiplicador de precio de cosechas directas (provisional; pendiente de simulación).</summary>
        public float CropPriceMultiplier { get; set; } = 1.0f;

        /// <summary>Multiplicador de precio de productos artesanales (provisional; pendiente de simulación).</summary>
        public float ArtisanPriceMultiplier { get; set; } = 1.0f;

        // ─────────────────────────── Minas ───────────────────────────

        /// <summary>Horas reales (HH:mm) en las que se regeneran los pisos de minas, Caverna Calavera y Volcán.</summary>
        public string[] MineResetTimes { get; set; } = { "06:00", "18:00" };

        // ─────────────────────────── Festivales ───────────────────────────

        /// <summary>Hora (formato juego, ej. 1800) a la que abre la ventana de los festivales.</summary>
        public int FestivalWindowStart { get; set; } = 1800;

        /// <summary>Hora (formato juego, ej. 2350) a la que se fuerza el cierre del festival.</summary>
        public int FestivalWindowEnd { get; set; } = 2350;

        // ─────────────────────────── Servidor ───────────────────────────

        /// <summary>Si es true, el host se marca como host dedicado nativo (1.6.15+) y se oculta al jugador anfitrión.</summary>
        public bool DedicatedServerMode { get; set; } = true;

        /// <summary>Si es true, el host expulsa a los clientes que no tengan Pelican 4Ever con versión compatible.</summary>
        public bool RequireClientMod { get; set; } = true;

        /// <summary>Número de huecos de granja (= puertas reclamables) declarados en Data/Locations.</summary>
        public int FarmSlotCount { get; set; } = 4;

        // ─────────────────────────── Panel de Administración y API REST ───────────────────────────

        /// <summary>Puerto HTTP del servidor de administración y Dashboard web (TASK-801 y TASK-802).</summary>
        public int AdminApiPort { get; set; } = 8085;

        /// <summary>Token Bearer para autenticación en la API de administración.</summary>
        public string AdminApiToken { get; set; } = "pelican4ever_secret_admin_token";

        /// <summary>Activación por módulo (clave = nombre del módulo). Los módulos ausentes se consideran activos.</summary>
        public Dictionary<string, bool> Modules { get; set; } = new();

        // ─────────────────────────── Valores parseados ───────────────────────────

        public TimeSpan ParsedDayStart { get; private set; }
        public TimeSpan ParsedNightHoldStart { get; private set; }
        public DateTime ParsedCalendarEpoch { get; private set; }
        public TimeSpan[] ParsedMineResetTimes { get; private set; } = Array.Empty<TimeSpan>();

        public bool IsModuleEnabled(string moduleName)
        {
            return !Modules.TryGetValue(moduleName, out bool enabled) || enabled;
        }

        /// <summary>Valida y parsea la configuración. Devuelve la lista de errores (vacía si es válida).</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();

            ParsedDayStart = ParseTime(nameof(DayStartTime), DayStartTime, errors);
            ParsedNightHoldStart = ParseTime(nameof(NightHoldStartTime), NightHoldStartTime, errors);

            if (ParsedDayStart != new TimeSpan(6, 0, 0))
                errors.Add($"{nameof(DayStartTime)} debe ser 06:00 (el motor arranca cada día a las 0600). Valor: '{DayStartTime}'.");

            if (ParsedNightHoldStart < TimeSpan.Zero || ParsedNightHoldStart > new TimeSpan(1, 50, 0))
                errors.Add($"{nameof(NightHoldStartTime)} debe estar entre 00:00 y 01:50 (equivale a 2400–2550). Valor: '{NightHoldStartTime}'.");

            if (!DateTime.TryParseExact(CalendarEpoch, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime epoch))
                errors.Add($"{nameof(CalendarEpoch)} debe tener formato yyyy-MM-dd. Valor: '{CalendarEpoch}'.");
            else if (epoch.DayOfWeek != DayOfWeek.Monday)
                errors.Add($"{nameof(CalendarEpoch)} debe caer en lunes (Día 1 de estación = lunes). '{CalendarEpoch}' es {epoch.DayOfWeek}.");
            ParsedCalendarEpoch = epoch.Date;

            var mineTimes = new List<TimeSpan>();
            foreach (string raw in MineResetTimes ?? Array.Empty<string>())
                mineTimes.Add(ParseTime(nameof(MineResetTimes), raw, errors));
            ParsedMineResetTimes = mineTimes.ToArray();

            if (MaxCatchUpRollovers < 0)
                errors.Add($"{nameof(MaxCatchUpRollovers)} no puede ser negativo.");
            if (PenaltyGoldPercent is < 0 or > 1)
                errors.Add($"{nameof(PenaltyGoldPercent)} debe estar entre 0 y 1.");
            if (PenaltyGoldCap < 0)
                errors.Add($"{nameof(PenaltyGoldCap)} no puede ser negativo.");
            if (PenaltyEnergyFraction is < 0 or > 1)
                errors.Add($"{nameof(PenaltyEnergyFraction)} debe estar entre 0 y 1.");
            if (DailySatietyFraction < 0)
                errors.Add($"{nameof(DailySatietyFraction)} no puede ser negativo.");
            if (CropPriceMultiplier <= 0 || ArtisanPriceMultiplier <= 0)
                errors.Add("Los multiplicadores de precio deben ser > 0.");
            if (SpaRegenSeconds <= 0)
                errors.Add($"{nameof(SpaRegenSeconds)} debe ser > 0.");
            if (FarmSlotCount is < 1 or > 16)
                errors.Add($"{nameof(FarmSlotCount)} debe estar entre 1 y 16.");
            if (AdminApiPort is < 1024 or > 65535)
                errors.Add($"{nameof(AdminApiPort)} debe situarse entre 1024 y 65535.");
            if (FestivalWindowStart >= FestivalWindowEnd || FestivalWindowEnd > 2550)
                errors.Add($"La ventana de festival ({FestivalWindowStart}-{FestivalWindowEnd}) debe ser creciente y terminar como muy tarde a las 2550.");

            return errors;
        }

        private static TimeSpan ParseTime(string field, string? raw, List<string> errors)
        {
            if (TimeSpan.TryParseExact(raw, @"hh\:mm", CultureInfo.InvariantCulture, out TimeSpan value) && value < TimeSpan.FromDays(1))
                return value;

            errors.Add($"{field}: '{raw}' no es una hora válida en formato HH:mm.");
            return TimeSpan.Zero;
        }
    }
}
