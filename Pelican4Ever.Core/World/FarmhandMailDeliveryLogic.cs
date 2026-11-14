using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>Entrada de correo pendiente de un farmhand y su destino al avanzar el día.</summary>
    public readonly struct PendingFarmhandMail
    {
        public PendingFarmhandMail(string id, bool deliverToMailbox)
        {
            Id = id;
            DeliverToMailbox = deliverToMailbox;
        }

        /// <summary>Identificador de la carta o bandera, sin el sufijo interno de Stardew Valley.</summary>
        public string Id { get; }

        /// <summary>
        /// <c>true</c> si debe entregarse como carta en el buzón; <c>false</c> si es una bandera
        /// sin carta (<c>%&amp;NL&amp;%</c>) que pasa directamente a <c>mailReceived</c>.
        /// </summary>
        public bool DeliverToMailbox { get; }
    }

    /// <summary>
    /// Replica la entrega de <c>mailForTomorrow</c> de Stardew Valley para farmhands que están
    /// desconectados cuando Pelican 4Ever avanza el día. El juego nativo solo procesa el correo de
    /// <c>Game1.player</c> en <c>Game1.ReceiveMailForTomorrow</c>; como el rollover del mod cierra todos
    /// los peers antes de cambiar la fecha, el correo de los clientes quedaba atascado para siempre.
    /// </summary>
    public static class FarmhandMailDeliveryLogic
    {
        /// <summary>Sufijo nativo para el correo que no genera carta (solo bandera en <c>mailReceived</c>).</summary>
        public const string NoLetterSuffix = "%&NL&%";

        /// <summary>
        /// Convierte las entradas crudas de <c>mailForTomorrow</c> en entregas idempotentes.
        /// Se ignoran nulos, vacíos y duplicados.
        /// </summary>
        public static IReadOnlyList<PendingFarmhandMail> Plan(IEnumerable<string?> entries)
        {
            var planned = new List<PendingFarmhandMail>();
            if (entries == null)
                return planned;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string? entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry))
                    continue;

                string raw = entry.Trim();
                bool noLetter = raw.Contains(NoLetterSuffix, StringComparison.Ordinal);
                string id = raw.Replace(NoLetterSuffix, string.Empty, StringComparison.Ordinal);
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                if (!seen.Add(id))
                    continue;

                planned.Add(new PendingFarmhandMail(id, !noLetter));
            }

            return planned;
        }
    }
}
