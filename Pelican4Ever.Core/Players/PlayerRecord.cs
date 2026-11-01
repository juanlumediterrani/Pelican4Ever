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

        /// <summary>Energía instantánea ya obtenida de la comida en el día de juego <see cref="SatietyGameDay"/>.</summary>
        public int SatietyUsed { get; set; }

        /// <summary>Platos "Bien comido" consumidos en el día de juego <see cref="SatietyGameDay"/>.</summary>
        public int WellFedBuffsUsed { get; set; }

        /// <summary>Energía obtenida en el Spa en el día de juego <see cref="SatietyGameDay"/>.</summary>
        public int SpaEnergyUsed { get; set; }

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

        /// <summary>Resetea los contadores diarios si pertenecen a otro día.</summary>
        public void EnsureDailyCounters(int currentGameDay)
        {
            if (SatietyGameDay == currentGameDay)
                return;

            SatietyGameDay = currentGameDay;
            SatietyUsed = 0;
            WellFedBuffsUsed = 0;
            SpaEnergyUsed = 0;
        }
    }
}
