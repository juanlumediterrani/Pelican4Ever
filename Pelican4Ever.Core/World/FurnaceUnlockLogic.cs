using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Reglas puras del desbloqueo vanilla del Horno (receta <c>Furnace</c>).
    /// En Stardew Valley, Clint presenta el evento cuando el jugador ya ha recogido mineral de cobre
    /// (bandera de correo <c>copperFound</c>) y entra en la granja entre las 06:00 y las 11:30.
    /// El evento es por jugador, pero en Pelican 4Ever el correo «para mañana» de un farmhand se quedaba
    /// pendiente porque los clientes se desconectan antes de que avance el día. Estas reglas permiten
    /// marcar la bandera directamente en el cliente y, si el evento no llega a jugarse, entregar la receta
    /// como red de seguridad sin depender del anfitrión.
    /// </summary>
    public static class FurnaceUnlockLogic
    {
        public const string FurnaceRecipeKey = "Furnace";
        public const string CopperOreQualifiedId = "(O)378";
        public const string CopperFoundMailId = "copperFound";

        /// <summary>Hora del juego en la que termina la ventana del evento de Clint (11:30).</summary>
        public const int EventWindowEndTime = 1130;

        /// <summary>Hora del juego a partir de la cual se entrega la receta si el evento no se jugó (12:00).</summary>
        public const int FallbackGrantTime = 1200;

        /// <summary>
        /// Indica si hay que marcar la bandera <c>copperFound</c> para el jugador local.
        /// </summary>
        public static bool ShouldSetCopperFound(bool alreadyHasOrWillReceiveMail, bool hasCopperOre)
        {
            return hasCopperOre && !alreadyHasOrWillReceiveMail;
        }

        /// <summary>
        /// Indica si toca entregar la receta como red de seguridad: el jugador ya tiene la bandera (recibida
        /// o pendiente para el próximo día), no conoce la receta y la ventana del evento de Clint ya pasó.
        /// </summary>
        public static bool ShouldGrantFallback(bool hasOrWillReceiveCopperFoundMail, bool knowsFurnaceRecipe, int timeOfDay)
        {
            return hasOrWillReceiveCopperFoundMail && !knowsFurnaceRecipe && timeOfDay >= FallbackGrantTime;
        }

        /// <summary>Busca el mineral de cobre por su identificador cualificado.</summary>
        public static bool ContainsCopperOre(IEnumerable<string?> qualifiedItemIds)
        {
            if (qualifiedItemIds == null)
                return false;

            foreach (string? qualifiedItemId in qualifiedItemIds)
            {
                if (string.Equals(qualifiedItemId, CopperOreQualifiedId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
