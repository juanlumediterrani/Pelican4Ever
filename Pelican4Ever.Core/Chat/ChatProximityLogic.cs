using System;

namespace Pelican4Ever.Core.Chat
{
    public enum ChatChannelType
    {
        Global,
        Local,
        Whisper,
        Command
    }

    public record ParsedChatMessage(ChatChannelType Channel, string? Target, string Message);

    /// <summary>
    /// Lógica de dominio pura para canales de chat y cálculo de proximidad (TASK-502).
    /// </summary>
    public static class ChatProximityLogic
    {
        public const double DefaultProximityRadius = 30.0;

        /// <summary>
        /// Determina si dos jugadores se encuentran en la misma ubicación y a una distancia no superior al radio de proximidad.
        /// </summary>
        public static bool IsWithinProximity(
            string? senderLocation,
            double senderX,
            double senderY,
            string? receiverLocation,
            double receiverX,
            double receiverY,
            double maxDistance = DefaultProximityRadius)
        {
            if (string.IsNullOrWhiteSpace(senderLocation) || string.IsNullOrWhiteSpace(receiverLocation))
                return false;

            if (!string.Equals(senderLocation, receiverLocation, StringComparison.OrdinalIgnoreCase))
                return false;

            double dx = senderX - receiverX;
            double dy = senderY - receiverY;
            double distSquared = (dx * dx) + (dy * dy);

            return distSquared <= (maxDistance * maxDistance);
        }

        /// <summary>
        /// Formatea un mensaje para el canal local de proximidad.
        /// </summary>
        public static string FormatLocalMessage(string senderName, string message)
        {
            return $"[Local] {senderName}: {message}";
        }

        /// <summary>
        /// Analiza una cadena de texto introducida en el chat y extrae su canal, objetivo y mensaje limpio.
        /// </summary>
        public static ParsedChatMessage ParseInput(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new ParsedChatMessage(ChatChannelType.Global, null, string.Empty);
            }

            string trimmed = input.Trim();
            if (!trimmed.StartsWith('/'))
            {
                return new ParsedChatMessage(ChatChannelType.Global, null, trimmed);
            }

            string[] parts = trimmed.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            string command = parts[0].Substring(1).ToLowerInvariant();

            if (command is "l" or "local")
            {
                string msg = parts.Length > 1 ? trimmed.Substring(parts[0].Length).Trim() : string.Empty;
                return new ParsedChatMessage(ChatChannelType.Local, null, msg);
            }

            if (command is "g" or "global")
            {
                string msg = parts.Length > 1 ? trimmed.Substring(parts[0].Length).Trim() : string.Empty;
                return new ParsedChatMessage(ChatChannelType.Global, null, msg);
            }

            if (command is "w" or "whisper" or "dm" or "pm" or "message")
            {
                if (parts.Length < 2)
                {
                    return new ParsedChatMessage(ChatChannelType.Whisper, null, string.Empty);
                }

                string target = parts[1];
                string msg = parts.Length > 2 ? parts[2] : string.Empty;
                return new ParsedChatMessage(ChatChannelType.Whisper, target, msg);
            }

            return new ParsedChatMessage(ChatChannelType.Command, null, trimmed);
        }
    }
}
