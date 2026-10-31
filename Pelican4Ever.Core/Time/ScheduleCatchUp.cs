using System.Collections.Generic;

namespace Pelican4Ever.Core.Time
{
    /// <summary>
    /// Puesta al día de horarios de NPC cuando el reloj salta sin pasar por cada tramo de 10 minutos
    /// (arranque del servidor a media jornada, comandos /time).
    /// </summary>
    public static class ScheduleCatchUp
    {
        /// <summary>
        /// Hora de la última entrada del horario que ya debería haber empezado a <paramref name="timeOfDay"/>,
        /// o null si todavía no ha empezado ninguna (el NPC sigue en su posición de la mañana).
        /// </summary>
        public static int? LatestEntryAtOrBefore(IEnumerable<int> scheduleTimes, int timeOfDay)
        {
            int? latest = null;
            foreach (int time in scheduleTimes)
            {
                if (time <= timeOfDay && (latest == null || time > latest.Value))
                    latest = time;
            }

            return latest;
        }
    }
}
