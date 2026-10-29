using System;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Resultado de la evaluación de acceso al buzón urbano.
    /// </summary>
    public enum MailboxAccessStatus
    {
        Unassigned,
        Owner,
        OtherPlayer
    }

    /// <summary>
    /// Respuesta evaluada para la interacción con el buzón urbano.
    /// </summary>
    public record MailboxAccessResult(MailboxAccessStatus Status, string Message);

    /// <summary>
    /// Lógica de dominio pura para buzones urbanos interactivos (TASK-501).
    /// </summary>
    public static class TownMailboxLogic
    {
        /// <summary>
        /// Obtiene las coordenadas del buzón asociadas a una puerta urbana.
        /// Si no se definen coordenadas personalizadas, por defecto se ubica un tile a la derecha (+1 en X).
        /// </summary>
        public static (int X, int Y) GetMailboxCoords(int doorX, int doorY, int? customMbX, int? customMbY)
        {
            return (customMbX ?? (doorX + 1), customMbY ?? doorY);
        }

        /// <summary>
        /// Comprueba si el cursor o tile objetivo corresponde al buzón.
        /// Si se especifican doorX y doorY, descarta el clic si coincide exactamente con la puerta residencial contigua.
        /// </summary>
        public static bool IsMailboxTarget(int targetX, int targetY, int mbX, int mbY, int doorX = -1, int doorY = -1)
        {
            if (doorX >= 0 && doorY >= 0 && targetX == doorX && targetY == doorY)
                return false;

            return Math.Abs(targetX - mbX) <= 1 && Math.Abs(targetY - mbY) <= 1;
        }

        /// <summary>
        /// Evalúa el acceso y mensaje al interactuar con el buzón según su propietario.
        /// </summary>
        public static MailboxAccessResult EvaluateAccess(long localPlayerId, long? ownerPlayerId, string? ownerName, int pendingLettersCount)
        {
            if (!ownerPlayerId.HasValue || ownerPlayerId.Value == 0)
            {
                return new MailboxAccessResult(MailboxAccessStatus.Unassigned, "Este buzón pertenece a una residencia desocupada.");
            }

            if (ownerPlayerId.Value == localPlayerId)
            {
                string msg = pendingLettersCount > 0
                    ? $"Tienes {pendingLettersCount} carta(s) por leer."
                    : "Tu buzón está vacío.";
                return new MailboxAccessResult(MailboxAccessStatus.Owner, msg);
            }

            string neighbor = string.IsNullOrWhiteSpace(ownerName) ? "un vecino" : ownerName;
            return new MailboxAccessResult(MailboxAccessStatus.OtherPlayer, $"Buzón de {neighbor}. Solo su propietario puede abrir el correo.");
        }
    }
}
