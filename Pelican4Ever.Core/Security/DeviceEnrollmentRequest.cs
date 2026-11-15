using System;

namespace Pelican4Ever.Core.Security
{
    /// <summary>
    /// Solicitud de alta de un nuevo dispositivo pendiente de aprobación por el propietario en Discord (TASK-1803).
    /// El dispositivo ya ha demostrado matemáticamente la posesión de su clave privada,
    /// pero no recibe acceso al juego ni reclama ningún personaje hasta ser aprobado fuera de banda.
    /// </summary>
    public sealed class DeviceEnrollmentRequest
    {
        /// <summary>Huella digital pública SHA-256 (64 hex en minúsculas) de la clave pública del dispositivo.</summary>
        public string Fingerprint { get; set; } = string.Empty;

        /// <summary>Clave pública SPKI DER en Base64.</summary>
        public string PublicKeyBase64 { get; set; } = string.Empty;

        /// <summary>Alias o nombre amigable del jugador según la invitación.</summary>
        public string Alias { get; set; } = string.Empty;

        /// <summary>Dirección IP desde la que se solicitó el alta.</summary>
        public string RemoteIp { get; set; } = string.Empty;

        /// <summary>Código o identificador de la invitación asociada.</summary>
        public string InviteCode { get; set; } = string.Empty;

        /// <summary>Momento en que se recibió la primera solicitud de alta con prueba válida.</summary>
        public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>Momento en que expira la solicitud de alta (alineada con la invitación de 72h).</summary>
        public DateTimeOffset ExpiresAtUtc { get; set; } = DateTimeOffset.UtcNow.AddHours(72);

        /// <summary>Contador de intentos de conexión asociados a esta solicitud.</summary>
        public int Attempts { get; set; } = 1;

        /// <summary>Comprueba si la solicitud ha superado su fecha de expiración.</summary>
        public bool IsExpired(DateTimeOffset nowUtc) => nowUtc > ExpiresAtUtc;

        /// <summary>Devuelve los primeros 12 caracteres de la huella para visualización amigable en Discord/consola.</summary>
        public string GetShortFingerprint() => Fingerprint.Length >= 12 ? Fingerprint[..12] : Fingerprint;
    }
}
