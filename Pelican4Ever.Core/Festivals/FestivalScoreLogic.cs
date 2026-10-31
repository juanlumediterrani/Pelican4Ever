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
        /// Normaliza el identificador del festival eliminando el prefijo "festival_" y convirtiendo a minúsculas.
        /// </summary>
        public static string NormalizeFestivalId(string? festivalId)
        {
            if (string.IsNullOrWhiteSpace(festivalId))
                return string.Empty;

            string trimmed = festivalId.Trim().ToLowerInvariant();
            if (trimmed.StartsWith("festival_", StringComparison.Ordinal))
                return trimmed.Substring("festival_".Length);

            return trimmed;
        }

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
            string normId = NormalizeFestivalId(festivalId);
            var existing = entries.FirstOrDefault(e =>
                NormalizeFestivalId(e.FestivalId) == normId
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
                FestivalId = normId,
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
            string normId = NormalizeFestivalId(festivalId);
            return entries
                .Where(e => (NormalizeFestivalId(e.FestivalId) == normId || string.IsNullOrEmpty(normId)) && e.GameDay == gameDay && e.Score > 0)
                .OrderByDescending(e => e.Score)
                .ThenBy(e => e.RecordedAtUtc)
                .FirstOrDefault();
        }

        /// <summary>
        /// Genera una lista clasificada formateada para mostrar en el chat.
        /// </summary>
        public static List<string> FormatLeaderboard(IEnumerable<FestivalScoreEntry> entries, string festivalId, int gameDay)
        {
            string normId = NormalizeFestivalId(festivalId);
            var ordered = entries
                .Where(e => (NormalizeFestivalId(e.FestivalId) == normId || string.IsNullOrEmpty(normId)) && e.GameDay == gameDay)
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

        /// <summary>
        /// Genera el texto formateado para el menú de carta o tablón oficial del festival (LetterViewerMenu).
        /// </summary>
        public static string BuildLeaderboardLetterText(
            IEnumerable<FestivalScoreEntry> entries,
            string festivalId,
            int gameDay,
            string? festivalTitle = null)
        {
            string normId = NormalizeFestivalId(festivalId);
            var ordered = entries
                .Where(e => (NormalizeFestivalId(e.FestivalId) == normId || string.IsNullOrEmpty(normId) || e.GameDay == gameDay) && e.Score > 0)
                .OrderByDescending(e => e.Score)
                .ThenBy(e => e.RecordedAtUtc)
                .ToList();

            string title = !string.IsNullOrWhiteSpace(festivalTitle)
                ? festivalTitle
                : "Festival de Pelican Town";

            if (ordered.Count == 0)
            {
                return $"=== Tablón Oficial: {title} ===^^"
                    + "Todavía no hay marcas registradas en el evento de hoy.^^"
                    + "¡Habla con el alcalde Lewis para participar en la próxima tanda!^^"
                    + "El premio oficial se entregará a la mejor marca al cierre del festival (23:50).";
            }

            var lines = new List<string>
            {
                $"=== Clasificación Oficial: {title} ===^^"
            };

            for (int i = 0; i < ordered.Count; i++)
            {
                var entry = ordered[i];
                string medal = i switch
                {
                    0 => "1. [1er Puesto]",
                    1 => "2. [2º Puesto]",
                    2 => "3. [3er Puesto]",
                    _ => $"{i + 1}."
                };
                lines.Add($"{medal} {entry.PlayerName} — {entry.Score} pts");
            }

            lines.Add("^^El ganador con la mayor puntuación de la jornada recibirá el premio oficial a las 23:50.");
            return string.Join("^", lines);
        }
    }
}
