using System;
using System.Net;
using System.Net.Sockets;

namespace Pelican4Ever.Core.Chat
{
    /// <summary>
    /// Reglas puras para evitar exponer direcciones IP de red como identificadores visibles en el chat.
    /// </summary>
    public static class ChatIdentityPrivacyLogic
    {
        /// <summary>
        /// Determina si una etiqueta de usuario de transporte corresponde a una dirección IP (IPv4 o IPv6, con o sin puerto)
        /// y no a un nombre de usuario legible (p. ej. Steam o GOG).
        /// </summary>
        public static bool IsIpAddressLabel(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            string candidate = raw.Trim();

            if (candidate.Contains(':'))
            {
                if (IPAddress.TryParse(candidate, out IPAddress? addr6) &&
                    addr6.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    return true;
                }

                if (IPEndPoint.TryParse(candidate, out IPEndPoint? ep) &&
                    (ep.AddressFamily == AddressFamily.InterNetworkV6 || IsStrictDottedQuad(ExtractHostBeforePort(candidate))))
                {
                    return true;
                }

                return false;
            }

            return IsStrictDottedQuad(candidate);
        }

        /// <summary>
        /// Devuelve el nombre visible para el chat ocultando cualquier IP cuando <paramref name="hideClientIpInChat"/> está activo.
        /// </summary>
        public static string SanitizeChatUserName(string? userName, bool hideClientIpInChat)
        {
            if (string.IsNullOrWhiteSpace(userName))
                return string.Empty;

            if (hideClientIpInChat && IsIpAddressLabel(userName))
                return string.Empty;

            return userName;
        }

        private static string ExtractHostBeforePort(string candidate)
        {
            int colon = candidate.IndexOf(':');
            if (colon <= 0 || colon != candidate.LastIndexOf(':'))
                return string.Empty;

            return candidate.Substring(0, colon);
        }

        private static bool IsStrictDottedQuad(string host)
        {
            if (string.IsNullOrEmpty(host))
                return false;

            string[] parts = host.Split('.');
            if (parts.Length != 4)
                return false;

            foreach (string part in parts)
            {
                if (part.Length == 0)
                    return false;
            }

            return IPAddress.TryParse(host, out IPAddress? addr) &&
                   addr.AddressFamily == AddressFamily.InterNetwork;
        }
    }
}
