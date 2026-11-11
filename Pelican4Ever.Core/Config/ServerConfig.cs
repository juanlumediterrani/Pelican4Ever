using System;
using System.Collections.Generic;
using System.Globalization;
using Pelican4Ever.Core.Server;

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
        /// Hora real a la que el reloj de juego y los horarios de Pelican empiezan el día (HH:mm).
        /// Debe ser 06:00 para que el reloj del juego (que arranca a las 0600) quede alineado 1:1.
        /// </summary>
        public string DayStartTime { get; set; } = "06:00";

        /// <summary>Hora local a la que cambia la fecha de calendario y se coordina el guardado diario.</summary>
        public string CalendarDayChangeTime { get; set; } = "02:00";

        /// <summary>
        /// Hora real a partir de la cual el reloj del juego se congela en 2550 ("Madrugada")
        /// hasta el siguiente Rollover. El motor no admite horas ≥ 2600 sin forzar el desmayo.
        /// </summary>
        public string NightHoldStartTime { get; set; } = "01:50";

        /// <summary>Minutos de antelación con los que se avisa del Rollover a los jugadores conectados.</summary>
        public int[] RolloverWarningMinutes { get; set; } = { 10 };

        /// <summary>
        /// Minutos, desde el cambio de fecha (02:00), durante los que el host rechaza conexiones nuevas
        /// (modo mantenimiento: guardado, copia, avance de día y actualización del runtime). 0 lo desactiva.
        /// </summary>
        public int MaintenanceWindowMinutes { get; set; } = 25;

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

        /// <summary>
        /// Minutos antes del cambio de fecha (por defecto 60 min, ej. 01:00 a 02:00) en los que una conexión
        /// se considera madrugadora para el nuevo día y no se penaliza como trasnochada.
        /// </summary>
        public int EarlyMorningGraceMinutes { get; set; } = 60;

        // ─────────────────────────── Energía y comida ───────────────────────────

        /// <summary>
        /// Capacidad máxima de Saciedad, como fracción de la energía máxima del jugador.
        /// </summary>
        public float DailySatietyFraction { get; set; } = 0.5f;

        /// <summary>Horas reales necesarias para que la Saciedad se vacíe completamente (100% a 0%). Por defecto 4.0h.</summary>
        public float SatietyDrainHours { get; set; } = 4.0f;

        /// <summary>
        /// Fracción de la capacidad máxima de Saciedad necesaria para tener activo el efecto "Bien comido" (por defecto 0.25 = 1/4).
        /// Al tener al menos 1/4 de la barra de saciedad llena (o al vaciarse hasta 1/4), sentarse recupera energía.
        /// </summary>
        public float WellFedSatietyThresholdFraction { get; set; } = 0.25f;

        /// <summary>Máximo de platos cocinados por día que conceden el buff "Bien comido" (activa el descanso sentado).</summary>
        public int MaxDailyWellFedBuffs { get; set; } = 2;

        /// <summary>Duración de "Bien comido" en minutos reales.</summary>
        public int WellFedDurationMinutes { get; set; } = 60;

        /// <summary>
        /// Segundos reales sentado en un banco, silla o taberna para recuperar 1 punto de energía mientras dura "Bien comido"
        /// (sin "Bien comido", sentarse no recupera nada; 0 = desactivado).
        /// </summary>
        public int SittingRegenSeconds { get; set; } = 5;

        /// <summary>
        /// Tecla para sentarse en el suelo o levantarse (nombre de <c>SButton</c> de SMAPI, p. ej. "OemPeriod" para la tecla punto).
        /// Sentado en el suelo cuenta como sentado para el descanso de "Bien comido".
        /// </summary>
        public string GroundSitKey { get; set; } = "OemPeriod";

        /// <summary>Segundos reales quieto dentro del agua del Spa o de la piscina para activar el buff del Spa (una vez al día).</summary>
        public int SpaStillSeconds { get; set; } = 30;

        /// <summary>Puntos de energía y también de salud que el buff del Spa recupera de forma pasiva durante su duración.</summary>
        public int SpaBuffTotalPoints { get; set; } = 120;

        /// <summary>Duración del buff del Spa en minutos reales.</summary>
        public int SpaBuffDurationMinutes { get; set; } = 120;

        /// <summary>Horas reales de desconexión para recargar la barra de energía completa al 100% (por defecto 4.0h; 0 = desactivado).</summary>
        public float OfflineRegenFullBarHours { get; set; } = 4.0f;

        // ─────────────────────────── Economía ───────────────────────────

        /// <summary>Multiplicador de precio de cosechas directas (provisional; pendiente de simulación).</summary>
        public float CropPriceMultiplier { get; set; } = 1.0f;

        /// <summary>Multiplicador de precio de productos artesanales (provisional; pendiente de simulación).</summary>
        public float ArtisanPriceMultiplier { get; set; } = 1.0f;

        // ─────────────────────────── Minas ───────────────────────────

        /// <summary>Horas reales (HH:mm) en las que se regeneran los pisos de minas, Caverna Calavera y Volcán.</summary>
        public string[] MineResetTimes { get; set; } = { "06:00", "18:00" };

        /// <summary>Minutos reales consecutivos que un piso de la mina debe permanecer desocupado para regenerarse (GAP-14).</summary>
        public int MineFloorCooldownMinutes { get; set; } = 45;

        // ─────────────────────────── Comercio y Construcción ───────────────────────────

        /// <summary>Hora (formato Stardew, ej. 2600 = 02:00 AM) a la que cierra el Carro Ambulante los viernes y domingos (GAP-04).</summary>
        public int TravelingCartClosingTime { get; set; } = 2600;

        /// <summary>Si es true, Robin permanece en su tienda y horario normal durante obras en lugar de trasladarse a la granja (GAP-10).</summary>
        public bool KeepRobinInCarpentryShop { get; set; } = true;

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

        /// <summary>
        /// Si es true (por defecto), solo pueden acceder los dispositivos emparejados en la lista blanca o las conexiones
        /// cuya IP tenga una invitación activa (<c>p4e_pair_allow &lt;usuario&gt; &lt;ip&gt;</c>) pendiente de introducir el código de pairing.
        /// </summary>
        public bool RequirePairingWhitelist { get; set; } = true;

        /// <summary>
        /// Horas de validez de un código de emparejamiento asíncrono desde que se genera (por defecto 72h = 3 días).
        /// </summary>
        public int PairingInviteTtlHours { get; set; } = 72;

        /// <summary>
        /// Si es true, impide a los jugadores que no sean el anfitrión usar herramientas de trabajo, colocar objetos/bombas
        /// o abrir cofres/máquinas en la Plaza Central (<c>Farm</c>) y en los puentes (<c>Pelican4Ever_Bridge_*</c>).
        /// Desactivado (<c>false</c>) por defecto.
        /// </summary>
        public bool ProtectPublicHubs { get; set; } = false;

        /// <summary>
        /// Resto del modpack que debe tener cada cliente (UniqueID de SMAPI), además de Pelican 4Ever. La versión exigida es
        /// la que tiene instalada el host, exacta: actualizar un mod en el servidor cambia la exigencia sin tocar este
        /// fichero. Un ID que el host no tenga instalado se ignora con un aviso. Vacío (por defecto) = solo Pelican 4Ever.
        /// Requiere <see cref="RequireClientMod"/>.
        /// </summary>
        public List<string> RequiredClientMods { get; set; } = new();

        /// <summary>Número de huecos de granja (= puertas reclamables) declarados en Data/Locations.</summary>
        public int FarmSlotCount { get; set; } = 4;

        /// <summary>Límite máximo de anexos adquiribles por cada residencia (por defecto 2, uno por cada salida libre).</summary>
        public int MaxAnnexesPerResidence { get; set; } = 2;

        /// <summary>Multiplicador de precio para el primer anexo temático de una residencia (por defecto 0.40 = 40% del precio base).</summary>
        public float FirstAnnexPriceMultiplier { get; set; } = 0.40f;

        /// <summary>Multiplicador de precio para el segundo anexo temático de una residencia (por defecto 1.00 = 100% del precio base normal).</summary>
        public float SecondAnnexPriceMultiplier { get; set; } = 1.00f;

        // ─────────────────────────── Panel de Administración y API REST ───────────────────────────

        /// <summary>Valor por defecto inseguro del token; el servidor HTTP rechazará arrancar si no se cambia (TASK-803).</summary>
        public const string DefaultInsecureAdminToken = "pelican4ever_secret_admin_token";

        /// <summary>Marcador de <c>config.example.json</c>: es público, así que tampoco vale como token.</summary>
        public const string ExampleAdminTokenPlaceholder = "CAMBIA-ESTE-TOKEN";

        /// <summary>Puerto HTTP del servidor de administración y Dashboard web (TASK-801 y TASK-802).</summary>
        public int AdminApiPort { get; set; } = 8085;

        /// <summary>Token Bearer para autenticación en la API de administración.</summary>
        public string AdminApiToken { get; set; } = DefaultInsecureAdminToken;

        /// <summary>
        /// Si es false (por defecto), la API HTTP escucha únicamente en 127.0.0.1 / localhost (TASK-803).
        /// Si es true, escucha en todas las interfaces de red (<c>http://*:{port}/</c>).
        /// </summary>
        public bool AdminApiBindAllInterfaces { get; set; } = false;

        /// <summary>
        /// Modo «anfitrión agente» (TASK-804): el host puede registrar su descanso diario en la cama o vía API
        /// sin desconectarse ni cerrar la partida (<c>Game1.ExitToTitle</c>).
        /// </summary>
        public bool AgentHost { get; set; } = false;

        /// <summary>Minutos de antelación por defecto para avisar a los jugadores antes de un reinicio controlado (TASK-804).</summary>
        public int SafeRestartWarningMinutes { get; set; } = 5;

        /// <summary>
        /// Partida que el host carga como anfitrión de cooperativo al llegar a la pantalla de título (carpeta exacta de
        /// <c>Saves/</c>). Vacío (por defecto) = el anfitrión la carga a mano. Necesario para un servidor desatendido.
        /// </summary>
        public string AutoHostSaveName { get; set; } = string.Empty;

        /// <summary>
        /// Si es true, el reinicio controlado cierra el proceso del juego en vez de volver al título, para que el
        /// supervisor (systemd) lo relance con el mod actualizado. Requiere <see cref="AutoHostSaveName"/>.
        /// </summary>
        public bool RestartExitsProcess { get; set; } = false;

        /// <summary>
        /// Servidor desatendido sin dibujado: con <see cref="AutoHostSaveName"/> configurado, el host deja de dibujar
        /// mientras el mundo está cargado (la simulación sigue a 60 Hz y la red igual; cada cliente dibuja lo suyo).
        /// Sin GPU, dibujar por CPU ocupa núcleos enteros aunque no haya nadie. Se dibuja igualmente en el título, con la
        /// pantalla de guardado abierta (el juego la necesita) y mientras haya un préstamo de dibujado
        /// (<c>POST /api/v1/host/render</c>, <c>p4e_render</c> o la API de mod). Sin efecto si un humano hace de host.
        /// </summary>
        public bool HeadlessRendering { get; set; } = true;

        // ─────────────────────── Checkpoints intradía (Épica 11) ───────────────────────

        /// <summary>
        /// Minutos reales entre checkpoints intradía (guardado nativo sin cambiar de día). 0 (por defecto) los desactiva;
        /// <c>p4e_checkpoint_now</c> sigue funcionando. Ver la Épica 11 del backlog antes de activarlo.
        /// </summary>
        public int CheckpointIntervalMinutes { get; set; } = 0;

        /// <summary>Permite el spike de diagnóstico, que puede eliminar elementos temporales. Solo en partidas de prueba.</summary>
        public bool CheckpointDebugMode { get; set; } = false;

        /// <summary>
        /// Activa los comandos de prueba de residencias (<c>p4e_farm_*</c>, <c>p4e_skill</c>, <c>p4e_weather</c>, <c>p4e_lightning</c>),
        /// que refundan residencias, cambian niveles y fuerzan clima y rayos. Solo en partidas de prueba: hacer
        /// <c>p4e_checkpoint_now</c> y una copia antes de usarlos.
        /// </summary>
        public bool EnableTestCommands { get; set; } = false;

        /// <summary>Si un checkpoint bloquea el juego más de estos milisegundos, el intervalo se duplica hasta el día siguiente. 0 = sin límite.</summary>
        public int CheckpointMaxDurationMs { get; set; } = 3000;

        /// <summary>Checkpoints que se conservan en <c>Pelican4Ever_Backups/checkpoints/</c>. 0 = no se borra ninguno.</summary>
        public int CheckpointRetention { get; set; } = 12;

        /// <summary>Comprime con gzip cada archivo de la copia de un checkpoint.</summary>
        public bool CheckpointCompress { get; set; } = true;

        /// <summary>
        /// Copia el estado vivo de los farmhands conectados a la partida antes de guardar (gap G4). Sin esto, con
        /// farmhands conectados no se hace checkpoint: se guardarían como estaban al conectarse.
        /// </summary>
        public bool CheckpointCloneFarmhands { get; set; } = true;

        /// <summary>Días que se conservan las copias diarias del rollover. 0 (por defecto) = no se borra ninguna, como hasta ahora.</summary>
        public int BackupRetentionDays { get; set; } = 0;

        /// <summary>Activación por módulo (clave = nombre del módulo). Los módulos ausentes se consideran activos.</summary>
        public Dictionary<string, bool> Modules { get; set; } = new();

        // ─────────────────────────── Valores parseados ───────────────────────────

        public TimeSpan ParsedDayStart { get; private set; }
        public TimeSpan ParsedCalendarDayChangeTime { get; private set; }
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
            ParsedCalendarDayChangeTime = ParseTime(nameof(CalendarDayChangeTime), CalendarDayChangeTime, errors);
            ParsedNightHoldStart = ParseTime(nameof(NightHoldStartTime), NightHoldStartTime, errors);

            if (ParsedDayStart != new TimeSpan(6, 0, 0))
                errors.Add($"{nameof(DayStartTime)} debe ser 06:00 (el motor arranca cada día a las 0600). Valor: '{DayStartTime}'.");
            if (ParsedCalendarDayChangeTime != new TimeSpan(2, 0, 0))
                errors.Add($"{nameof(CalendarDayChangeTime)} debe ser 02:00 para separar la fecha del inicio de luz/horarios (06:00). Valor: '{CalendarDayChangeTime}'.");

            if (MaintenanceWindowMinutes < 0 || MaintenanceWindowMinutes > Pelican4Ever.Core.Time.MaintenanceWindow.MaxMinutes)
                errors.Add($"{nameof(MaintenanceWindowMinutes)} debe estar entre 0 y {Pelican4Ever.Core.Time.MaintenanceWindow.MaxMinutes}. Valor: {MaintenanceWindowMinutes}.");

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
            if (EarlyMorningGraceMinutes is < 0 or > 180)
                errors.Add($"{nameof(EarlyMorningGraceMinutes)} debe estar entre 0 y 180 minutos.");
            if (DailySatietyFraction < 0)
                errors.Add($"{nameof(DailySatietyFraction)} no puede ser negativo.");
            if (SatietyDrainHours <= 0)
                errors.Add($"{nameof(SatietyDrainHours)} debe ser > 0.");
            if (CropPriceMultiplier <= 0 || ArtisanPriceMultiplier <= 0)
                errors.Add("Los multiplicadores de precio deben ser > 0.");
            if (WellFedDurationMinutes <= 0)
                errors.Add($"{nameof(WellFedDurationMinutes)} debe ser > 0.");
            if (SpaStillSeconds <= 0)
                errors.Add($"{nameof(SpaStillSeconds)} debe ser > 0.");
            if (SpaBuffTotalPoints < 0)
                errors.Add($"{nameof(SpaBuffTotalPoints)} no puede ser negativo.");
            if (SpaBuffDurationMinutes <= 0)
                errors.Add($"{nameof(SpaBuffDurationMinutes)} debe ser > 0.");
            if (SittingRegenSeconds < 0)
                errors.Add($"{nameof(SittingRegenSeconds)} no puede ser negativo.");
            if (OfflineRegenFullBarHours < 0)
                errors.Add($"{nameof(OfflineRegenFullBarHours)} no puede ser negativo.");
            if (MineFloorCooldownMinutes < 0)
                errors.Add($"{nameof(MineFloorCooldownMinutes)} no puede ser negativo.");
            if (FarmSlotCount is < 1 or > 16)
                errors.Add($"{nameof(FarmSlotCount)} debe estar entre 1 y 16.");
            if (MaxAnnexesPerResidence is < 0 or > 3)
                errors.Add($"{nameof(MaxAnnexesPerResidence)} debe estar entre 0 y 3.");
            if (FirstAnnexPriceMultiplier < 0)
                errors.Add($"{nameof(FirstAnnexPriceMultiplier)} no puede ser negativo.");
            if (SecondAnnexPriceMultiplier < 0)
                errors.Add($"{nameof(SecondAnnexPriceMultiplier)} no puede ser negativo.");
            if (AdminApiPort is < 1024 or > 65535)
                errors.Add($"{nameof(AdminApiPort)} debe situarse entre 1024 y 65535.");
            if (FestivalWindowStart >= FestivalWindowEnd || FestivalWindowEnd > 2550)
                errors.Add($"La ventana de festival ({FestivalWindowStart}-{FestivalWindowEnd}) debe ser creciente y terminar como muy tarde a las 2550.");
            if (TravelingCartClosingTime is < 600 or > 2600)
                errors.Add($"{nameof(TravelingCartClosingTime)} debe situarse entre 0600 y 2600.");
            if (!string.IsNullOrEmpty(AutoHostSaveName) && !AutoHostLogic.IsValidSaveName(AutoHostSaveName))
                errors.Add($"{nameof(AutoHostSaveName)} debe ser el nombre exacto de una carpeta de Saves/, sin rutas. Valor: '{AutoHostSaveName}'.");
            if (CheckpointIntervalMinutes is < 0 or > 1440)
                errors.Add($"{nameof(CheckpointIntervalMinutes)} debe estar entre 0 (desactivado) y 1440. Valor: {CheckpointIntervalMinutes}.");
            else if (CheckpointIntervalMinutes is > 0 and < 5)
                errors.Add($"{nameof(CheckpointIntervalMinutes)} debe ser 0 o al menos 5: cada checkpoint congela el juego mientras guarda. Valor: {CheckpointIntervalMinutes}.");
            if (CheckpointMaxDurationMs < 0)
                errors.Add($"{nameof(CheckpointMaxDurationMs)} no puede ser negativo.");
            if (CheckpointRetention < 0)
                errors.Add($"{nameof(CheckpointRetention)} no puede ser negativo.");
            if (BackupRetentionDays < 0)
                errors.Add($"{nameof(BackupRetentionDays)} no puede ser negativo.");
            if (PairingInviteTtlHours is < 1 or > 720)
                errors.Add($"{nameof(PairingInviteTtlHours)} debe estar entre 1 y 720 horas. Valor: {PairingInviteTtlHours}.");
            foreach (string id in RequiredClientMods ?? new List<string>())
            {
                if (!Pelican4Ever.Core.Networking.ClientModRequirements.IsValidUniqueId(id))
                    errors.Add($"{nameof(RequiredClientMods)} contiene un UniqueID no válido: '{id}'.");
            }
            if (RestartExitsProcess && string.IsNullOrEmpty(AutoHostSaveName))
                errors.Add($"{nameof(RestartExitsProcess)} requiere {nameof(AutoHostSaveName)}: si no, el servidor relanzado se queda en la pantalla de título.");

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
