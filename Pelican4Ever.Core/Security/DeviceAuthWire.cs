using System;
using System.IO;

namespace Pelican4Ever.Core.Security
{
    /// <summary>
    /// Serialización y deserialización binaria de los paquetes del protocolo P4E\x02 en Lidgren (UDP).
    /// </summary>
    public static class DeviceAuthWire
    {
        public const int DiscoveryChallengePayloadLength = 4 + 1 + 16 + 8 + 32; // 61 bytes exactos

        /// <summary>
        /// Serializa el desafío del servidor para incluirlo en la respuesta de descubrimiento UDP (sendDiscoveryResponse).
        /// </summary>
        public static byte[] PackDiscoveryChallenge(DeviceAuthChallenge challenge)
        {
            if (challenge == null)
                throw new ArgumentNullException(nameof(challenge));

            byte[] buffer = new byte[DiscoveryChallengePayloadLength];
            int offset = 0;

            // 1. Marker (4 bytes)
            Buffer.BlockCopy(DeviceCryptoConstants.MagicMarker, 0, buffer, offset, 4);
            offset += 4;

            // 2. ProtocolVersion (1 byte)
            buffer[offset++] = DeviceCryptoConstants.ProtocolVersion;

            // 3. ServerSessionId (16 bytes)
            byte[] sessionBytes = challenge.ServerSessionId.ToByteArray();
            Buffer.BlockCopy(sessionBytes, 0, buffer, offset, 16);
            offset += 16;

            // 4. TimestampUtc (8 bytes, Int64 ms)
            byte[] timeBytes = BitConverter.GetBytes(challenge.TimestampUtc.ToUnixTimeMilliseconds());
            Buffer.BlockCopy(timeBytes, 0, buffer, offset, 8);
            offset += 8;

            // 5. Nonce (32 bytes)
            Buffer.BlockCopy(challenge.Nonce, 0, buffer, offset, DeviceCryptoConstants.NonceLength);

            return buffer;
        }

        /// <summary>
        /// Intenta deserializar un desafío recibido en la respuesta de descubrimiento UDP del cliente.
        /// </summary>
        public static bool TryParseDiscoveryChallenge(ReadOnlySpan<byte> raw, string targetEndpoint, out DeviceAuthChallenge? challenge)
        {
            challenge = null;
            if (raw.Length != DiscoveryChallengePayloadLength)
                return false;

            // Comprobar Marker
            if (!raw[..4].SequenceEqual(DeviceCryptoConstants.MagicMarker))
                return false;

            // Comprobar versión
            if (raw[4] != DeviceCryptoConstants.ProtocolVersion)
                return false;

            Guid sessionId = new Guid(raw.Slice(5, 16));
            long ms = BitConverter.ToInt64(raw.Slice(21, 8));
            DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            byte[] nonce = raw.Slice(29, DeviceCryptoConstants.NonceLength).ToArray();

            challenge = new DeviceAuthChallenge(sessionId, timestamp, nonce, targetEndpoint);
            return true;
        }

        /// <summary>
        /// Serializa la prueba del cliente para enviarla en el mensaje de conexión inicial de Lidgren (RemoteHailMessage).
        /// </summary>
        public static byte[] PackHailPayload(byte[] nonce, DateTimeOffset timestampUtc, byte[] publicKeySpki, byte[] signature)
        {
            if (nonce == null || nonce.Length != DeviceCryptoConstants.NonceLength)
                throw new ArgumentException("Nonce inválido.", nameof(nonce));
            if (publicKeySpki == null || publicKeySpki.Length == 0 || publicKeySpki.Length > ushort.MaxValue)
                throw new ArgumentException("Clave pública inválida.", nameof(publicKeySpki));
            if (signature == null || signature.Length == 0 || signature.Length > ushort.MaxValue)
                throw new ArgumentException("Firma inválida.", nameof(signature));

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // 1. Marker (4 bytes)
            writer.Write(DeviceCryptoConstants.MagicMarker);

            // 2. Nonce (32 bytes)
            writer.Write(nonce);

            // 3. TimestampUtc (8 bytes, ms)
            writer.Write(timestampUtc.ToUnixTimeMilliseconds());

            // 4. Clave pública SPKI prefijada por su longitud (2 bytes uint16)
            writer.Write((ushort)publicKeySpki.Length);
            writer.Write(publicKeySpki);

            // 5. Firma ECDSA prefijada por su longitud (2 bytes uint16)
            writer.Write((ushort)signature.Length);
            writer.Write(signature);

            writer.Flush();
            return ms.ToArray();
        }

        /// <summary>
        /// Intenta deserializar el contenido de un RemoteHailMessage recibido en el host.
        /// </summary>
        public static bool TryParseHailPayload(
            ReadOnlySpan<byte> raw,
            out byte[]? nonce,
            out DateTimeOffset timestampUtc,
            out byte[]? publicKeySpki,
            out byte[]? signature)
        {
            nonce = null;
            timestampUtc = DateTimeOffset.MinValue;
            publicKeySpki = null;
            signature = null;

            int minHeader = 4 + DeviceCryptoConstants.NonceLength + 8 + 2 + 2;
            if (raw.Length < minHeader)
                return false;

            // Marker
            if (!raw[..4].SequenceEqual(DeviceCryptoConstants.MagicMarker))
                return false;

            int offset = 4;

            // Nonce
            nonce = raw.Slice(offset, DeviceCryptoConstants.NonceLength).ToArray();
            offset += DeviceCryptoConstants.NonceLength;

            // Timestamp
            long ms = BitConverter.ToInt64(raw.Slice(offset, 8));
            timestampUtc = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            offset += 8;

            // Longitud de clave pública
            ushort keyLen = BitConverter.ToUInt16(raw.Slice(offset, 2));
            offset += 2;

            if (raw.Length < offset + keyLen + 2)
                return false;

            publicKeySpki = raw.Slice(offset, keyLen).ToArray();
            offset += keyLen;

            // Longitud de firma
            ushort sigLen = BitConverter.ToUInt16(raw.Slice(offset, 2));
            offset += 2;

            if (raw.Length != offset + sigLen)
                return false;

            signature = raw.Slice(offset, sigLen).ToArray();
            return true;
        }
    }
}
