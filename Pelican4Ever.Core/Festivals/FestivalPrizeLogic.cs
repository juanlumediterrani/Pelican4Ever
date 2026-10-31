using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.Festivals
{
    /// <summary>Premio del campeón diario de un minijuego: objetos y marca de primera victoria (como en vanilla).</summary>
    public sealed record FestivalPrize(IReadOnlyList<string> ItemIds, string? FirstWinMailFlag);

    /// <summary>
    /// Premios de los minijuegos competitivos (TASK-701), con los mismos objetos que <c>Event.DefaultCommands.AwardFestivalPrize</c>.
    /// El premio se entrega en el cliente del ganador: su inventario y su <c>mailReceived</c> los controla su cliente.
    /// </summary>
    public static class FestivalPrizeLogic
    {
        public const string PrizeTicket = "(O)PrizeTicket";

        /// <summary>Marca de primera victoria del festival («Egg Festival», «Ice Festival»), o null si no tiene.</summary>
        public static string? FirstWinMailFlag(string festivalId)
        {
            return FestivalScoreLogic.NormalizeFestivalId(festivalId) switch
            {
                "spring13" => "Egg Festival",
                "winter8" => "Ice Festival",
                _ => null
            };
        }

        /// <summary>Objetos del premio: el especial la primera vez que se gana; después, un ticket de premio.</summary>
        public static IReadOnlyList<string> PrizeItems(string festivalId, bool firstWin)
        {
            if (!firstWin)
                return new[] { PrizeTicket };

            return FestivalScoreLogic.NormalizeFestivalId(festivalId) switch
            {
                "spring13" => new[] { "(H)4" },
                "winter8" => new[] { "(H)17", "(O)687", "(O)691", "(O)703" },
                _ => new[] { PrizeTicket }
            };
        }

        /// <summary>Clave única de un premio pendiente: festival y día de juego, para no entregarlo dos veces.</summary>
        public static string PrizeKey(string festivalId, int gameDay)
        {
            return $"{FestivalScoreLogic.NormalizeFestivalId(festivalId)}@{gameDay}";
        }

        /// <summary>Festival de una clave de premio, o null si la clave no es válida.</summary>
        public static string? FestivalOfKey(string prizeKey)
        {
            int at = prizeKey.IndexOf('@');
            return at > 0 ? prizeKey.Substring(0, at) : null;
        }

        /// <summary>Añade la clave a una lista separada por comas (formato de <c>modData</c>) sin duplicarla.</summary>
        public static string AddToList(string? list, string key)
        {
            var keys = (list ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!keys.Contains(key))
                keys.Add(key);
            return string.Join(",", keys);
        }

        public static bool ListContains(string? list, string key)
        {
            return (list ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(key);
        }
    }
}
