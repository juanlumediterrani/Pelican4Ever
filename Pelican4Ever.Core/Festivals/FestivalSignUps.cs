using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.Festivals
{
    /// <summary>Resultado de apuntarse o borrarse del evento principal de un festival.</summary>
    public enum SignUpResult
    {
        Joined,
        Left,

        /// <summary>El jugador ya participó en una tanda de este festival hoy.</summary>
        AlreadyPlayed,

        NoChange
    }

    /// <summary>Tanda del evento principal: los apuntados que empiezan juntos cuando el host confirma.</summary>
    public sealed record FestivalRun(string FestivalId, int RunId, IReadOnlyList<long> Participants)
    {
        /// <summary>
        /// Identificador propio de la tanda para una puerta <c>waitForOtherPlayers</c> del guion. Las puertas de Stardew
        /// solo se reinician al cambiar de día, así que una segunda tanda con el mismo nombre las heredaría ya abiertas.
        /// </summary>
        public string GateId(string gate) => $"{gate}#{RunId}";
    }

    /// <summary>
    /// Inscripciones al evento principal del festival del día (búsqueda de huevos, baile, pesca en el hielo...).
    /// Vive en el host; cada jugador participa como mucho en una tanda por festival para no repetir premios.
    /// </summary>
    public sealed class FestivalSignUps
    {
        private readonly HashSet<long> signedUp = new();
        private readonly HashSet<long> played = new();
        private string festivalId = string.Empty;
        private int runs;

        /// <summary>Apuntados a la próxima tanda del festival actual.</summary>
        public IReadOnlyCollection<long> Participants => signedUp;

        public string FestivalId => festivalId;

        public SignUpResult SetSignedUp(string festival, long playerId, bool join)
        {
            EnsureFestival(festival);

            if (!join)
                return signedUp.Remove(playerId) ? SignUpResult.Left : SignUpResult.NoChange;

            if (played.Contains(playerId))
                return SignUpResult.AlreadyPlayed;

            return signedUp.Add(playerId) ? SignUpResult.Joined : SignUpResult.NoChange;
        }

        public bool HasPlayed(string festival, long playerId)
        {
            return festivalId == festival && played.Contains(playerId);
        }

        /// <summary>Cierra la inscripción y devuelve la tanda, o null si no hay nadie apuntado.</summary>
        public FestivalRun? StartRun(string festival)
        {
            EnsureFestival(festival);
            if (signedUp.Count == 0)
                return null;

            runs++;
            var participants = signedUp.OrderBy(id => id).ToList();
            played.UnionWith(participants);
            signedUp.Clear();
            return new FestivalRun(festival, runs, participants);
        }

        public void Reset()
        {
            festivalId = string.Empty;
            signedUp.Clear();
            played.Clear();
            runs = 0;
        }

        private void EnsureFestival(string festival)
        {
            if (!string.Equals(festival, festivalId, StringComparison.Ordinal))
            {
                Reset();
                festivalId = festival;
            }
        }
    }
}
