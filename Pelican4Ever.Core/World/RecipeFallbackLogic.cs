using System;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Reglas puras para las redes de seguridad de recetas que en vanilla solo se obtienen con un evento.
    /// La concesión debe ser idempotente: si la receta ya existe no se vuelve a tocar, y nunca se arrancan
    /// eventos ni se reenvía correo desde aquí (eso evita bucles de eventos, HUD y rollover).
    /// </summary>
    public static class RecipeFallbackLogic
    {
        /// <summary>Hora del juego a partir de la cual se permite la red de seguridad (12:00).</summary>
        public const int DefaultFallbackTime = 1200;

        /// <summary>
        /// Indica si toca conceder la receta. La comprobación de "ya conocida" es la barrera principal
        /// contra bucles y duplicados.
        /// </summary>
        public static bool ShouldGrant(bool eligible, bool alreadyKnown, int timeOfDay, int earliestTime = DefaultFallbackTime)
        {
            return eligible && !alreadyKnown && timeOfDay >= earliestTime;
        }

        /// <summary>Amistad expresada en puntos (250 puntos por corazón) contra el mínimo requerido.</summary>
        public static bool HasFriendship(int points, int requiredPoints)
        {
            return points >= requiredPoints;
        }

        /// <summary>Corazones a puntos de amistad, para que las reglas declaren el requisito en corazones.</summary>
        public static int HeartsToPoints(int hearts)
        {
            return Math.Max(0, hearts) * 250;
        }

        /// <summary>
        /// El evento de Garden Pot comprueba el pantry del host. La red de seguridad solo debe activarse
        /// con el progreso propio del jugador (invernadero de su residencia o pantry propio).
        /// </summary>
        public static bool CanUseOwnGreenhouseFallback(bool ownGreenhouseUnlocked, bool ownPantryMail, bool completedCommunityCenter)
        {
            return ownGreenhouseUnlocked || ownPantryMail || completedCommunityCenter;
        }

        /// <summary>Basta con haber recibido la carta o con haber visto el evento que la origina.</summary>
        public static bool HasMailOrEvent(bool hasMail, bool sawEvent)
        {
            return hasMail || sawEvent;
        }
    }
}
