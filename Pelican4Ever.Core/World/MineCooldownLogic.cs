using System;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Lógica de dominio para el cooldown y regeneración de pisos de minas (GAP-14).
    /// Evita exploits de reseteo instantáneo manteniendo pisos en memoria mientras tengan jugadores
    /// o no hayan superado el tiempo de desocupación configurado.
    /// </summary>
    public static class MineCooldownLogic
    {
        public const int SpecialEventFloor = 77377;

        /// <summary>
        /// Determina si un piso de mina desocupado es elegible para descargarse y regenerarse.
        /// </summary>
        /// <param name="level">Número de piso de la mina.</param>
        /// <param name="farmersCount">Número de jugadores actualmente presentes en el piso.</param>
        /// <param name="lastOccupiedUtc">Instante UTC en que el piso tuvo jugadores por última vez (null si nunca se ha registrado).</param>
        /// <param name="nowUtc">Instante UTC actual.</param>
        /// <param name="cooldownMinutes">Minutos que debe permanecer desocupado para regenerarse (si &lt;= 0, no expira por cooldown).</param>
        /// <param name="isTargetOfPendingWarp">True si algún jugador está entrando actualmente a este piso.</param>
        /// <param name="hasDisconnectedFarmer">True si algún jugador desconectado tiene este piso como ubicación de reconexión.</param>
        public static bool ShouldUnloadFloor(
            int level,
            int farmersCount,
            DateTime? lastOccupiedUtc,
            DateTime nowUtc,
            int cooldownMinutes,
            bool isTargetOfPendingWarp,
            bool hasDisconnectedFarmer)
        {
            // Si hay jugadores dentro, jamás se descarga
            if (farmersCount > 0)
                return false;

            // Pisos especiales (ej. reto de Qi / ascensor calavera) no se descargan
            if (level == SpecialEventFloor)
                return false;

            // Si alguien está viajando/teletransportándose al piso, no se descarga
            if (isTargetOfPendingWarp)
                return false;

            // Si un jugador desconectado se quedó en este piso, se conserva para su reconexión
            if (hasDisconnectedFarmer)
                return false;

            // Si el cooldown está desactivado (<= 0), no se descarga por tiempo
            if (cooldownMinutes <= 0)
                return false;

            // Si nunca se registró lastOccupiedUtc, no se descarga aún (se inicializará)
            if (!lastOccupiedUtc.HasValue)
                return false;

            return (nowUtc - lastOccupiedUtc.Value) >= TimeSpan.FromMinutes(cooldownMinutes);
        }
    }
}
