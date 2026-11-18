using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Reglas puras para reenviar eventos de mundo marcados con la precondición de anfitrión (<c>/H</c>)
    /// cuando el servidor corre sin el modo dedicado nativo (<c>Game1.IsDedicatedHost == false</c>).
    /// Cubre eventos compartidos con <c>broadcastEvent</c> como el desbloqueo del Centro Cívico (<c>Town:611439</c>),
    /// las ceremonias de finalización (<c>Town:191393</c>, <c>Town:502261</c>), Pierre/Morris (<c>SeedShop:3102768</c>)
    /// y los eventos matutinos de <c>Farm</c> (<c>66</c> Llave Oxidada, <c>63</c> Kent, <c>102</c> Pierre, <c>690006</c> Marlon).
    /// </summary>
    public static class HostEventRelayLogic
    {
        private static readonly HashSet<string> SupportedLocations = new(StringComparer.Ordinal)
        {
            "Town",
            "SeedShop",
            "Farm"
        };

        // Eventos /H en Farm que ya tienen módulo propio por jugador en Pelican 4Ever
        // (PrivateFarmCaves para la cueva y ClientPetAdoptionModule para mascotas).
        private static readonly HashSet<string> ExcludedEventIds = new(StringComparer.Ordinal)
        {
            "65",
            "1590166",
            "897405"
        };

        /// <summary>Indica si la ubicación admite reenvío de eventos <c>/H</c> disparados por clientes.</summary>
        public static bool IsSupportedLocation(string? locationName)
        {
            return !string.IsNullOrEmpty(locationName) && SupportedLocations.Contains(locationName);
        }

        /// <summary>Extrae el identificador del evento (primer segmento antes de <c>/</c>).</summary>
        public static string ExtractEventId(string? eventKey)
        {
            if (string.IsNullOrWhiteSpace(eventKey))
                return string.Empty;

            int slash = eventKey.IndexOf('/');
            return (slash >= 0 ? eventKey[..slash] : eventKey).Trim();
        }

        /// <summary>
        /// Comprueba si la clave del evento exige anfitrión (<c>H</c> exacto en mayúscula o <c>IS_HOST</c>).
        /// Distingue <c>H</c> (host) de <c>h</c> minúscula (mascota).
        /// </summary>
        public static bool HasHostPrecondition(string? eventKey)
        {
            if (string.IsNullOrWhiteSpace(eventKey))
                return false;

            string[] parts = eventKey.Split('/');
            for (int i = 1; i < parts.Length; i++)
            {
                string token = parts[i].Trim();
                if (string.Equals(token, "H", StringComparison.Ordinal)
                    || string.Equals(token, "IS_HOST", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Comprueba si el guion del evento incluye el comando <c>broadcastEvent</c>.</summary>
        public static bool HasBroadcastEventCommand(string? eventScript)
        {
            if (string.IsNullOrWhiteSpace(eventScript))
                return false;

            string[] commands = eventScript.Split('/');
            foreach (string rawCommand in commands)
            {
                string trimmed = rawCommand.Trim();
                if (trimmed.Length == 0)
                    continue;

                int space = trimmed.IndexOf(' ');
                string commandName = space >= 0 ? trimmed[..space] : trimmed;
                if (string.Equals(commandName, "broadcastEvent", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Determina si un evento concreto debe reenviarse al anfitrión cuando entra un cliente.
        /// </summary>
        public static bool IsRelayableHostBroadcastEvent(string? locationName, string? eventKey, string? eventScript)
        {
            if (!IsSupportedLocation(locationName))
                return false;

            string eventId = ExtractEventId(eventKey);
            if (eventId.Length == 0 || ExcludedEventIds.Contains(eventId))
                return false;

            return HasHostPrecondition(eventKey) && HasBroadcastEventCommand(eventScript);
        }

        /// <summary>Indica si el cliente debe notificar su entrada al anfitrión para evaluar eventos <c>/H</c>.</summary>
        public static bool ShouldClientNotifyWarp(bool isMainPlayer, bool isDedicatedHost, string? locationName)
        {
            return !isMainPlayer && !isDedicatedHost && IsSupportedLocation(locationName);
        }

        /// <summary>Indica si el anfitrión está en condiciones de evaluar y ejecutar un evento reenviado.</summary>
        public static bool CanHostRelayWarp(
            bool isMainPlayer,
            bool isDedicatedHost,
            bool dedicatedServerMode,
            bool isEventActive,
            bool isWarping,
            bool isNewDay,
            string? locationName)
        {
            return isMainPlayer
                && !isDedicatedHost
                && dedicatedServerMode
                && !isEventActive
                && !isWarping
                && !isNewDay
                && IsSupportedLocation(locationName);
        }
    }
}
