using System;

namespace Pelican4Ever.Core.Security
{
    /// <summary>
    /// Estado de un dispositivo registrado en el servidor.
    /// </summary>
    public enum DeviceStatus
    {
        /// <summary>Dispositivo activo y autorizado para conectarse.</summary>
        Active,

        /// <summary>Dispositivo revocado por el propietario; conexión bloqueada.</summary>
        Revoked,

        /// <summary>Dispositivo suspendido temporalmente.</summary>
        Suspended
    }

    /// <summary>
    /// Registro de un dispositivo autorizado en el host (Protocolo P4E\x02).
    /// El host almacena exclusivamente la clave pública SPKI y su huella digital;
    /// NUNCA se reciben ni almacenan claves privadas.
    /// La huella digital y la clave pública son datos públicos, no credenciales secretas.
    /// </summary>
    public sealed class AuthorizedDevice
    {
        /// <summary>
        /// Huella digital SHA-256 (64 hex en minúsculas) de la clave pública SPKI.
        /// Identificador público del dispositivo.
        /// </summary>
        public string Fingerprint { get; set; } = string.Empty;

        /// <summary>Clave pública en formato SubjectPublicKeyInfo (SPKI) codificada en Base64.</summary>
        public string PublicKeyBase64 { get; set; } = string.Empty;

        /// <summary>Alias legible o nombre del usuario propietario del dispositivo (ej. 'mTx').</summary>
        public string Alias { get; set; } = string.Empty;

        /// <summary>Estado del dispositivo en el servidor.</summary>
        public DeviceStatus Status { get; set; } = DeviceStatus.Active;

        /// <summary>Identificador del peón vinculado (Farmer.UniqueMultiplayerID).</summary>
        public long BoundPlayerId { get; set; }

        /// <summary>Fecha y hora UTC de alta o aprobación del dispositivo.</summary>
        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>Fecha y hora UTC del último acceso exitoso.</summary>
        public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.MinValue;

        /// <summary>Última dirección IP conocida desde la que conectó (puramente informativo, no se usa como credencial).</summary>
        public string LastSeenIp { get; set; } = string.Empty;

        /// <summary>Decodifica y devuelve los bytes SPKI de la clave pública.</summary>
        public byte[] GetPublicKeySpki() => string.IsNullOrWhiteSpace(PublicKeyBase64)
            ? Array.Empty<byte>()
            : Convert.FromBase64String(PublicKeyBase64);
    }
}
