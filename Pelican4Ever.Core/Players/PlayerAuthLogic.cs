using System;

namespace Pelican4Ever.Core.Players
{
    /// <summary>Resultado de la comprobación de autenticación/posesión de un peón.</summary>
    public enum AuthResult
    {
        /// <summary>El peón estaba sin reclamar y ha sido asignado al token del cliente entrante.</summary>
        Claimed,

        /// <summary>El peón ya pertenecía a este cliente y el token coincide correctamente.</summary>
        Allowed,

        /// <summary>El peón pertenece a otro cliente y el token proporcionado no coincide.</summary>
        DeniedMismatch,

        /// <summary>El peón pertenece a un cliente pero la conexión no proporcionó ningún token.</summary>
        DeniedNoToken
    }

    /// <summary>
    /// Lógica pura para la autenticación y posesión de peones mediante token de cliente (Opción A).
    /// </summary>
    public static class PlayerAuthLogic
    {
        /// <summary>
        /// Valida el token del cliente contra el registro del peón o lo reclama si aún no tenía dueño.
        /// </summary>
        /// <param name="record">Registro del jugador en el servidor.</param>
        /// <param name="incomingToken">Token de cliente enviado en la conexión.</param>
        /// <returns>Resultado de la autenticación (<see cref="AuthResult"/>).</returns>
        public static AuthResult ValidateOrClaim(PlayerRecord record, string? incomingToken)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            // Si el peón aún no tiene dueño registrado
            if (string.IsNullOrWhiteSpace(record.OwnerToken))
            {
                if (!string.IsNullOrWhiteSpace(incomingToken))
                {
                    record.OwnerToken = incomingToken.Trim();
                    return AuthResult.Claimed;
                }

                // Sin token entrante en peón nuevo: se permite continuar
                return AuthResult.Allowed;
            }

            // El peón está protegido por un token existente
            if (string.IsNullOrWhiteSpace(incomingToken))
            {
                return AuthResult.DeniedNoToken;
            }

            return string.Equals(record.OwnerToken, incomingToken.Trim(), StringComparison.Ordinal)
                ? AuthResult.Allowed
                : AuthResult.DeniedMismatch;
        }

        /// <summary>
        /// Restablece la vinculación del peón, dejándolo libre para ser reclamado de nuevo (ej. comando de admin).
        /// </summary>
        public static void ResetClaim(PlayerRecord record)
        {
            if (record != null)
            {
                record.OwnerToken = null;
            }
        }

        /// <summary>
        /// Indica si el peón ya ha sido reclamado y tiene un token asignado.
        /// </summary>
        public static bool IsClaimed(PlayerRecord record)
        {
            return record != null && !string.IsNullOrWhiteSpace(record.OwnerToken);
        }

        /// <summary>
        /// Devuelve una representación ofuscada del token para logs o vistas administrativas (ej. "3fa8...afa6").
        /// </summary>
        public static string MaskToken(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return "—";

            string trimmed = token.Trim();
            if (trimmed.Length <= 8)
                return trimmed;

            return $"{trimmed[..4]}...{trimmed[^4..]}";
        }
    }
}
