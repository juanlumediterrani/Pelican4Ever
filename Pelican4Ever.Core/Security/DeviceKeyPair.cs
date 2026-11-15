using System;
using System.Security.Cryptography;

namespace Pelican4Ever.Core.Security
{
    /// <summary>
    /// Gestiona un par de claves criptográficas asimétricas ECDSA sobre la curva NIST P-256 para la identidad del dispositivo.
    /// La clave privada permanece localmente en el dispositivo y nunca se transmite por la red.
    /// </summary>
    public sealed class DeviceKeyPair : IDisposable
    {
        private readonly ECDsa ecdsa;
        private byte[]? cachedPublicKeySpki;
        private string? cachedFingerprint;

        public DeviceKeyPair(ECDsa ecdsa)
        {
            this.ecdsa = ecdsa ?? throw new ArgumentNullException(nameof(ecdsa));
        }

        /// <summary>Genera un nuevo par de claves asimétricas NIST P-256 criptográficamente seguro.</summary>
        public static DeviceKeyPair Generate()
        {
            var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return new DeviceKeyPair(key);
        }

        /// <summary>Crea una instancia cargando una clave privada serializada en formato PKCS#8.</summary>
        public static DeviceKeyPair FromPkcs8PrivateKey(byte[] pkcs8Bytes)
        {
            if (pkcs8Bytes == null || pkcs8Bytes.Length == 0)
                throw new ArgumentException("Los bytes de la clave privada no pueden estar vacíos.", nameof(pkcs8Bytes));

            var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(pkcs8Bytes, out _);
            return new DeviceKeyPair(key);
        }

        /// <summary>Exporta la clave privada en formato binario estándar PKCS#8 DER.</summary>
        public byte[] ExportPkcs8PrivateKey() => ecdsa.ExportPkcs8PrivateKey();

        /// <summary>Exporta la clave pública en formato estándar SubjectPublicKeyInfo (SPKI) DER (~91 bytes).</summary>
        public byte[] ExportPublicKeySpki()
        {
            return cachedPublicKeySpki ??= ecdsa.ExportSubjectPublicKeyInfo();
        }

        /// <summary>Devuelve la clave pública codificada en Base64.</summary>
        public string GetPublicKeyBase64() => Convert.ToBase64String(ExportPublicKeySpki());

        /// <summary>
        /// Devuelve la huella digital pública del dispositivo: SHA-256 en minúsculas (64 hex) sobre la clave pública SPKI.
        /// Este valor es público y no constituye una credencial secreta.
        /// </summary>
        public string GetFingerprint()
        {
            return cachedFingerprint ??= DeviceSignatureVerifier.ComputeFingerprint(ExportPublicKeySpki());
        }

        /// <summary>Devuelve los primeros 12 caracteres de la huella digital para visualización cómoda en UI o Discord.</summary>
        public string GetShortFingerprint()
        {
            string fp = GetFingerprint();
            return fp.Length >= 12 ? fp[..12] : fp;
        }

        /// <summary>
        /// Firma un bloque de datos arbitrario con SHA-256 en formato compacto IEEE P1363 (64 bytes: R: 32B + S: 32B).
        /// </summary>
        public byte[] Sign(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            return ecdsa.SignData(data, HashAlgorithmName.SHA256);
        }

        public void Dispose()
        {
            ecdsa.Dispose();
        }
    }
}
