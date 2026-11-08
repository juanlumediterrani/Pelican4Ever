using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.Players
{
    /// <summary>Estado de descanso visible para el jugador. Se DERIVA de <see cref="PlayerRecord"/>; no se almacena.</summary>
    public enum SleepState
    {
        /// <summary>Ha jugado en el día de juego en curso y aún no ha descansado en su cama.</summary>
        Awake,

        /// <summary>Ha usado "Descansar y desconectar" en el día de juego en curso.</summary>
        Rested,

        /// <summary>Desconectado; el último Rollover que le afectó fue un descanso correcto.</summary>
        OfflineSafe,

        /// <summary>Desconectado; el último Rollover que le afectó le aplicó la penalización (una única vez).</summary>
        OfflinePenalized
    }

    /// <summary>Resultado del Rollover para un jugador concreto.</summary>
    public enum RolloverOutcome
    {
        /// <summary>No jugó el día que termina: no se toca nada (sin penalizaciones acumuladas).</summary>
        Untouched,

        /// <summary>Durmió correctamente: energía al 100%.</summary>
        Rested,

        /// <summary>Jugó y no durmió en su cama: gasto médico y energía reducida.</summary>
        Penalized
    }

    /// <summary>
    /// Registro autoritativo de un jugador, guardado SOLO en el host (<see cref="World.ServerWorldState"/>).
    /// Los días se expresan en "TotalDays" de juego (<see cref="Time.GameDate.TotalDays"/>); -1 = nunca.
    /// </summary>
    public class PlayerRecord
    {
        public long UniqueMultiplayerID { get; set; }

        public string PlayerName { get; set; } = string.Empty;

        /// <summary>Hueco de granja asignado (ej. "Pelican4Ever_Farm_02"), o null si aún no ha reclamado puerta.</summary>
        public string? FarmSlotId { get; set; }

        /// <summary>Token único del equipo/cliente propietario para evitar usurpación de partida (Opción A).</summary>
        public string? OwnerToken { get; set; }

        /// <summary>Último día de juego en el que el jugador estuvo conectado.</summary>
        public int LastActiveGameDay { get; set; } = -1;

        /// <summary>Día de juego en el que el jugador usó "Descansar y desconectar".</summary>
        public int RestedOnGameDay { get; set; } = -1;

        /// <summary>Resultado del último Rollover que le afectó (para derivar el estado Offline_*).</summary>
        public RolloverOutcome LastOutcome { get; set; } = RolloverOutcome.Untouched;

        /// <summary>Nivel actual de Saciedad (puntos de energía acumulados por comida).</summary>
        public float Satiety { get; set; }

        /// <summary>
        /// Compatibilidad con DTOs y API: nivel de saciedad redondeado a entero. Es de solo lectura para que,
        /// al deserializar (partida o sincronización), no sobrescriba <see cref="Satiety"/> con un valor redondeado.
        /// </summary>
        public int SatietyUsed => (int)Math.Round(Satiety);

        /// <summary>Última vez que se actualizó el nivel de saciedad (UTC) para computar el vaciado continuo.</summary>
        public DateTimeOffset LastSatietyUpdateUtc { get; set; } = DateTimeOffset.MinValue;

        /// <summary>Platos "Bien comido" consumidos en el día de juego <see cref="SatietyGameDay"/>.</summary>
        public int WellFedBuffsUsed { get; set; }

        /// <summary>Fin (UTC) del buff "Bien comido" en curso: mientras dure, sentarse recupera energía.</summary>
        public DateTimeOffset WellFedUntilUtc { get; set; } = DateTimeOffset.MinValue;

        /// <summary>Buffs de Spa activados en el día de juego <see cref="SatietyGameDay"/> (máximo 1).</summary>
        public int SpaBuffsUsed { get; set; }

        /// <summary>Fin (UTC) del buff del Spa en curso: recuperación pasiva de energía y salud.</summary>
        public DateTimeOffset SpaBuffUntilUtc { get; set; } = DateTimeOffset.MinValue;

        /// <summary>Día de juego al que corresponden los contadores diarios (si no coincide, se consideran a 0).</summary>
        public int SatietyGameDay { get; set; } = -1;

        /// <summary>Oro acumulado por ventas en el mercado vecinal mientras el jugador estaba desconectado.</summary>
        public int PendingMarketGold { get; set; }

        /// <summary>Premios de festival ganados y aún no confirmados por su cliente (claves de <c>FestivalPrizeLogic.PrizeKey</c>).</summary>
        public List<string> PendingFestivalPrizes { get; set; } = new();

        /// <summary>Última vez que se vio al jugador (UTC).</summary>
        public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.MinValue;

        /// <summary>Momento UTC en el que inició la sesión actual (se reinicia a MinValue al desconectar).</summary>
        public DateTimeOffset CurrentSessionStartUtc { get; set; } = DateTimeOffset.MinValue;

        /// <summary>Momento UTC desde el que se computa la regeneración de energía offline (se resetea al conectar o en Rollover).</summary>
        public DateTimeOffset OfflineStaminaAnchorUtc { get; set; } = DateTimeOffset.MinValue;

        /// <summary>
        /// Último día de juego en el que el jugador estuvo activo ANTES de la ventana de madrugador.
        /// Permite distinguir a un madrugador recién conectado de quien lleva jugando toda la noche o abandonó su sesión previa.
        /// </summary>
        public int ActiveDayPriorToGrace { get; set; } = -1;

        /// <summary>Resetea los contadores diarios si pertenecen a otro día (sin alterar la Saciedad acumulada).</summary>
        public void EnsureDailyCounters(int currentGameDay)
        {
            if (SatietyGameDay == currentGameDay)
                return;

            SatietyGameDay = currentGameDay;
            WellFedBuffsUsed = 0;
            SpaBuffsUsed = 0;
        }

        /// <summary>Compatibilidad: true si el buff temporal "Bien comido" sigue activo en <paramref name="nowUtc"/>.</summary>
        public bool IsWellFed(DateTimeOffset nowUtc) => nowUtc < WellFedUntilUtc;

        /// <summary>
        /// True si el jugador tiene activo el efecto "Bien comido":
        /// - Por nivel de Saciedad (al menos 1/4 de la barra de saciedad según <paramref name="thresholdFraction"/>).
        /// - O por buff temporal activo (<see cref="WellFedUntilUtc"/>).
        /// </summary>
        public bool IsWellFed(DateTimeOffset nowUtc, int maxStamina, float dailyFraction, float drainHours, float thresholdFraction = 0.25f)
        {
            if (nowUtc < WellFedUntilUtc)
                return true;

            int maxSatiety = Economy.SatietyBudget.DailyBudget(maxStamina, dailyFraction);
            if (maxSatiety <= 0)
                return false;

            float projectedSatiety = ProjectedSatiety(nowUtc, maxStamina, dailyFraction, drainHours);
            return Economy.SatietyBudget.IsWellFed(projectedSatiety, maxSatiety, thresholdFraction);
        }

        /// <summary>
        /// True si la Saciedad actual es suficiente para considerar al jugador "Bien comido" (al menos 1/4 de la capacidad).
        /// </summary>
        public bool IsWellFed(int maxStamina, float dailyFraction, float thresholdFraction = 0.25f)
        {
            int maxSatiety = Economy.SatietyBudget.DailyBudget(maxStamina, dailyFraction);
            return Economy.SatietyBudget.IsWellFed(Satiety, maxSatiety, thresholdFraction);
        }

        /// <summary>
        /// Minutos restantes del efecto "Bien comido" (el mayor entre el temporizador de plato cocinado y el vaciado de saciedad hasta 1/4).
        /// </summary>
        public int RemainingWellFedMinutes(DateTimeOffset nowUtc, int maxStamina, float dailyFraction, float drainHours, float thresholdFraction = 0.25f)
        {
            int minutesFromTimer = WellFedUntilUtc > nowUtc
                ? (int)Math.Ceiling((WellFedUntilUtc - nowUtc).TotalMinutes)
                : 0;

            int maxSatiety = Economy.SatietyBudget.DailyBudget(maxStamina, dailyFraction);
            float projectedSatiety = ProjectedSatiety(nowUtc, maxStamina, dailyFraction, drainHours);
            int minutesFromSatiety = Economy.SatietyBudget.RemainingWellFedMinutes(projectedSatiety, maxSatiety, drainHours, thresholdFraction);

            return Math.Max(minutesFromTimer, minutesFromSatiety);
        }

        /// <summary>True si el buff del Spa sigue activo en <paramref name="nowUtc"/>.</summary>
        public bool IsSpaBuffActive(DateTimeOffset nowUtc) => nowUtc < SpaBuffUntilUtc;

        /// <summary>
        /// Activa (o renueva) "Bien comido" al comer un plato cocinado, si quedan usos en el día.
        /// Los contadores diarios deben estar al día (<see cref="EnsureDailyCounters"/>).
        /// </summary>
        public bool TryActivateWellFed(DateTimeOffset nowUtc, TimeSpan duration, int maxDailyBuffs)
        {
            if (WellFedBuffsUsed >= maxDailyBuffs || duration <= TimeSpan.Zero)
                return false;

            WellFedBuffsUsed++;
            WellFedUntilUtc = nowUtc + duration;
            return true;
        }

        /// <summary>
        /// Activa el buff del Spa si aún no se ha usado en el día. Los contadores diarios deben estar al día
        /// (<see cref="EnsureDailyCounters"/>). Un buff en curso sigue hasta su fin aunque cambie el día.
        /// </summary>
        public bool TryActivateSpaBuff(DateTimeOffset nowUtc, TimeSpan duration)
        {
            if (SpaBuffsUsed >= 1 || IsSpaBuffActive(nowUtc) || duration <= TimeSpan.Zero)
                return false;

            SpaBuffsUsed++;
            SpaBuffUntilUtc = nowUtc + duration;
            return true;
        }

        /// <summary>
        /// Nivel de Saciedad que tendría el jugador en <paramref name="nowUtc"/> tras el vaciado continuo, sin modificar el registro.
        /// </summary>
        public float ProjectedSatiety(DateTimeOffset nowUtc, int maxStamina, float dailyFraction, float drainHours)
        {
            if (Satiety <= 0)
                return 0f;
            if (LastSatietyUpdateUtc == DateTimeOffset.MinValue)
                return Satiety;

            double elapsedSeconds = (nowUtc - LastSatietyUpdateUtc).TotalSeconds;
            if (elapsedSeconds <= 0)
                return Satiety;

            int maxSatiety = Economy.SatietyBudget.DailyBudget(maxStamina, dailyFraction);
            if (maxSatiety <= 0 || drainHours <= 0)
                return 0f;

            double drainedPoints = maxSatiety * (elapsedSeconds / (drainHours * 3600.0));
            return (float)Math.Max(0.0, Satiety - drainedPoints);
        }

        /// <summary>
        /// Aplica el vaciado continuo de Saciedad en función del tiempo transcurrido (online u offline) desde la última actualización.
        /// </summary>
        public void UpdateSatietyDecay(DateTimeOffset nowUtc, int maxStamina, float dailyFraction, float drainHours)
        {
            if (LastSatietyUpdateUtc == DateTimeOffset.MinValue)
            {
                LastSatietyUpdateUtc = nowUtc;
                return;
            }

            if (nowUtc <= LastSatietyUpdateUtc)
                return;

            Satiety = ProjectedSatiety(nowUtc, maxStamina, dailyFraction, drainHours);
            LastSatietyUpdateUtc = nowUtc;
        }

        /// <summary>Copia el estado de consumo (Saciedad y contadores diarios de comida/Spa) de otro registro.</summary>
        public void CopyConsumptionFrom(PlayerRecord source)
        {
            Satiety = source.Satiety;
            LastSatietyUpdateUtc = source.LastSatietyUpdateUtc;
            SatietyGameDay = source.SatietyGameDay;
            WellFedBuffsUsed = source.WellFedBuffsUsed;
            WellFedUntilUtc = source.WellFedUntilUtc;
            SpaBuffsUsed = source.SpaBuffsUsed;
            SpaBuffUntilUtc = source.SpaBuffUntilUtc;
        }

        /// <summary>
        /// Crea el informe de consumo que el cliente envía al host tras comer o activar el Spa. Los buffs viajan como
        /// segundos restantes para no depender de que los relojes del cliente y del host coincidan.
        /// </summary>
        public ConsumptionReport ToConsumptionReport(DateTimeOffset nowUtc) => new()
        {
            Satiety = Satiety,
            GameDay = SatietyGameDay,
            WellFedBuffsUsed = WellFedBuffsUsed,
            WellFedRemainingSeconds = RemainingSeconds(WellFedUntilUtc, nowUtc),
            SpaBuffsUsed = SpaBuffsUsed,
            SpaBuffRemainingSeconds = RemainingSeconds(SpaBuffUntilUtc, nowUtc)
        };

        private static int RemainingSeconds(DateTimeOffset untilUtc, DateTimeOffset nowUtc)
            => untilUtc > nowUtc ? (int)Math.Ceiling((untilUtc - nowUtc).TotalSeconds) : 0;

        /// <summary>
        /// Aplica en el host el informe de consumo de un cliente. La Saciedad se acota a [0, <paramref name="maxSatiety"/>]
        /// y se sella con el reloj del host; los contadores diarios solo se aceptan si son del día en curso y nunca bajan;
        /// la duración restante de cada buff se acota a su duración configurada.
        /// </summary>
        public void ApplyConsumptionReport(ConsumptionReport report, int currentGameDay, DateTimeOffset nowUtc, int maxSatiety, TimeSpan maxWellFed, TimeSpan maxSpaBuff)
        {
            EnsureDailyCounters(currentGameDay);

            float satiety = float.IsFinite(report.Satiety) ? report.Satiety : 0f;
            Satiety = Math.Clamp(satiety, 0f, Math.Max(0, maxSatiety));
            LastSatietyUpdateUtc = nowUtc;

            if (report.GameDay == currentGameDay)
            {
                WellFedBuffsUsed = Math.Max(WellFedBuffsUsed, report.WellFedBuffsUsed);
                SpaBuffsUsed = Math.Max(SpaBuffsUsed, Math.Min(1, report.SpaBuffsUsed));
            }

            WellFedUntilUtc = nowUtc + ClampSeconds(report.WellFedRemainingSeconds, maxWellFed);
            SpaBuffUntilUtc = nowUtc + ClampSeconds(report.SpaBuffRemainingSeconds, maxSpaBuff);
        }

        private static TimeSpan ClampSeconds(int seconds, TimeSpan max)
            => TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Math.Max(0, (int)max.TotalSeconds)));
    }

    /// <summary>Estado de consumo que un cliente comunica al host (mensaje de mod <c>SatietyReport</c>).</summary>
    public class ConsumptionReport
    {
        public float Satiety { get; set; }

        public int GameDay { get; set; } = -1;

        public int WellFedBuffsUsed { get; set; }

        public int WellFedRemainingSeconds { get; set; }

        public int SpaBuffsUsed { get; set; }

        public int SpaBuffRemainingSeconds { get; set; }
    }
}
