using System;
using System.Security.Cryptography;
using System.Text;

namespace Pelican4Ever.Core.Security
{
    /// <summary>
    /// Verificador estático de firmas asimétricas ECDSA P-256 y cálculo de huellas digitales públicas.
    /// No requiere claves privadas.
    /// </summary>
    public static class DeviceSignatureVerifier
    {
        /// <summary>
        /// Calcula la huella digital criptográfica SHA-256 en formato hexadecimal en minúsculas (64 caracteres)
        /// a partir de los bytes SPKI de la clave pública.
        /// </summary>
        public static string ComputeFingerprint(byte[] publicKeySpki)
        {
            if (publicKeySpki == null || publicKeySpki.Length == 0)
                return string.Empty;

            byte[] hash = SHA256.HashData(publicKeySpki);
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("x2"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Comprueba si una cadena es una huella digital válida (64 caracteres hexadecimales).
        /// </summary>
        public static bool IsValidFingerprint(string? fingerprint)
        {
            if (string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length != 64)
                return false;

            for (int i = 0; i < fingerprint.Length; i++)
            {
                char c = fingerprint[i];
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Valida la firma ECDSA NIST P-256 sobre un bloque de datos utilizando la clave pública aportada en formato SPKI.
        /// </summary>
        /// <param name="publicKeySpki">Clave pública en formato SubjectPublicKeyInfo (SPKI).</param>
        /// <param name="data">Bloque de datos canónicos que fueron firmados.</param>
        /// <param name="signature">Firma en formato IEEE P1363 (64 bytes).</param>
        /// <returns>True si la firma es matemáticamente válida para esa clave pública y datos; false en caso contrario.</returns>
        public static bool VerifySignature(byte[] publicKeySpki, byte[] data, byte[] signature)
        {
            if (publicKeySpki == null || publicKeySpki.Length == 0)
                return false;
            if (data == null || data.Length == 0)
                return false;
            if (signature == null || signature.Length == 0 || signature.Length > DeviceCryptoConstants.MaxSignatureLength)
                return false;

            try
            {
                using var verifier = ECDsa.Create();
                verifier.ImportSubjectPublicKeyInfo(publicKeySpki, out _);
                return verifier.VerifyData(data, signature, HashAlgorithmName.SHA256);
            }
            catch
            {
                // Clave pública malformada o algoritmo no compatible
                return false;
            }
        }
    }
}
