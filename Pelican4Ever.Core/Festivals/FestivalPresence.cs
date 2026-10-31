using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.Festivals
{
    /// <summary>
    /// Jugadores que están ahora dentro del festival del día, según lo avisa cada cliente al empezar y terminar su
    /// evento de festival. El host no puede deducirlo de la ubicación: durante un festival el jugador está en la
    /// ubicación temporal «Temp» de <c>Event.ChangeToTemporaryMap</c>, no en «Beach» ni en «Town».
    /// </summary>
    public sealed class FestivalPresence
    {
        private readonly HashSet<long> present = new();
        private string festivalId = string.Empty;

        public string FestivalId => festivalId;

        public void Enter(string festival, long playerId)
        {
            if (!string.Equals(festival, festivalId, StringComparison.Ordinal))
            {
                present.Clear();
                festivalId = festival;
            }

            present.Add(playerId);
        }

        public void Leave(long playerId)
        {
            present.Remove(playerId);
        }

        /// <summary>Presentes en <paramref name="festival"/>, ordenados; vacío si es otro festival.</summary>
        public IReadOnlyList<long> Present(string festival)
        {
            return string.Equals(festival, festivalId, StringComparison.Ordinal)
                ? present.OrderBy(id => id).ToList()
                : Array.Empty<long>();
        }

        public void Reset()
        {
            present.Clear();
            festivalId = string.Empty;
        }
    }
}
