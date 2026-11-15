using System;

namespace Pelican4Ever.Core.Security
{
    /// <summary>
    /// Constantes criptográficas y de protocolo para la autenticación asimétrica de dispositivos (Protocolo P4E\x02).
    /// </summary>
    public static class DeviceCryptoConstants
    {
        /// <summary>Marcador de 4 bytes para el protocolo asimétrico v2: 'P', '4', 'E', 0x02.</summary>
        public static readonly byte[] MagicMarker = { 0x50, 0x34, 0x45, 0x02 };

        /// <summary>Versión del protocolo de autenticación asimétrica.</summary>
        public const byte ProtocolVersion = 0x02;

        /// <summary>Longitud en bytes del nonce aleatorio del desafío (256 bits = 32 bytes).</summary>
        public const int NonceLength = 32;

        /// <summary>Longitud máxima en bytes de la firma ECDSA NIST P-256 en formato estándar ASN.1/DER.</summary>
        public const int MaxSignatureLength = 74;

        /// <summary>Longitud en bytes del identificador de sesión del servidor (GUID: 16 bytes).</summary>
        public const int ServerSessionIdLength = 16;

        /// <summary>Tiempo de vida máximo de un desafío emitido por el host: 30 segundos.</summary>
        public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromSeconds(30);

        /// <summary>Margen máximo de desfase de reloj permitido entre cliente y servidor (skew): 60 segundos.</summary>
        public static readonly TimeSpan AllowedClockSkew = TimeSpan.FromSeconds(60);

        /// <summary>Número máximo de desafíos pendientes almacenados en memoria en el host (protección DoS).</summary>
        public const int MaxPendingChallenges = 1024;

        /// <summary>Etiqueta de contexto canónico para el bloque firmado.</summary>
        public const string ContextTag = "P4E-AUTH-V2";
    }
}
