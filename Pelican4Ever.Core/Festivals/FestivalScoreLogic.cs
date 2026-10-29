using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.Festivals
{
    /// <summary>
    /// Registro de puntuación individual en un minijuego de festival asíncrono (TASK-701).
    /// </summary>
    public class FestivalScoreEntry
    {
        public string FestivalId { get; set; } = string.Empty;

        public int GameDay { get; set; }

        public long PlayerId { get; set; }

        public string PlayerName { get; set; } = string.Empty;

        public int Score { get; set; }

        public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public bool RewardAwarded { get; set; }
    }

    /// <summary>
    /// Lógica de dominio pura para minijuegos y clasificaciones de festivales asíncronos (TASK-701).
    /// </summary>
    public static class FestivalScoreLogic
    {
        /// <summary>
        /// Registra o actualiza la puntuación de un jugador en el minijuego, conservando la puntuación más alta.
        /// </summary>
        public static FestivalScoreEntry RecordScore(
            List<FestivalScoreEntry> entries,
            string festivalId,
            int gameDay,
            long playerId,
            string playerName,
            int score)
        {
            var existing = entries.FirstOrDefault(e =>
                e.FestivalId == festivalId
                && e.GameDay == gameDay
                && e.PlayerId == playerId
            );

            if (existing != null)
            {
                if (score > existing.Score)
                {
                    existing.Score = score;
                    existing.RecordedAtUtc = DateTimeOffset.UtcNow;
                }
                return existing;
            }

            var newEntry = new FestivalScoreEntry
            {
                FestivalId = festivalId,
                GameDay = gameDay,
                PlayerId = playerId,
                PlayerName = playerName,
                Score = score,
                RecordedAtUtc = DateTimeOffset.UtcNow,
                RewardAwarded = false
            };
            entries.Add(newEntry);
            return newEntry;
        }

        /// <summary>
        /// Determina el ganador del minijuego con la mayor puntuación para el festival y día indicados.
        /// </summary>
        public static FestivalScoreEntry? DetermineWinner(IEnumerable<FestivalScoreEntry> entries, string festivalId, int gameDay)
        {
            return entries
                .Where(e => e.FestivalId == festivalId && e.GameDay == gameDay)
                .OrderByDescending(e => e.Score)
                .ThenBy(e => e.RecordedAtUtc)
                .FirstOrDefault();
        }

        /// <summary>
        /// Genera una lista clasificada formateada para mostrar en el tablón del festival.
        /// </summary>
        public static List<string> FormatLeaderboard(IEnumerable<FestivalScoreEntry> entries, string festivalId, int gameDay)
        {
            var ordered = entries
                .Where(e => e.FestivalId == festivalId && e.GameDay == gameDay)
                .OrderByDescending(e => e.Score)
                .ToList();

            var result = new List<string>();
            for (int i = 0; i < ordered.Count; i++)
            {
                var entry = ordered[i];
                result.Add($"{i + 1}. {entry.PlayerName}: {entry.Score} pts");
            }
            return result;
        }
    }
}
