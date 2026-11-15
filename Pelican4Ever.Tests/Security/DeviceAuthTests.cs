using System;
using System.Security.Cryptography;
using Pelican4Ever.Core.Security;
using Xunit;

namespace Pelican4Ever.Tests.Security
{
    public class DeviceAuthTests
    {
        private static readonly DateTimeOffset BaseTime = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);

        [Fact]
        public void DeviceKeyPair_GeneratesValidP256AndCalculatesDeterministicFingerprint()
        {
            using var key = DeviceKeyPair.Generate();
            byte[] spki = key.ExportPublicKeySpki();
            string fp1 = key.GetFingerprint();
            string fp2 = DeviceSignatureVerifier.ComputeFingerprint(spki);

            Assert.NotNull(spki);
            Assert.NotEmpty(spki);
            Assert.Equal(64, fp1.Length);
            Assert.Equal(fp1, fp2);
            Assert.True(DeviceSignatureVerifier.IsValidFingerprint(fp1));
            Assert.Equal(12, key.GetShortFingerprint().Length);
            Assert.StartsWith(key.GetShortFingerprint(), fp1);
        }

        [Fact]
        public void DeviceKeyPair_CanExportAndImportPkcs8PrivateKey()
        {
            using var original = DeviceKeyPair.Generate();
            byte[] pkcs8 = original.ExportPkcs8PrivateKey();

            using var restored = DeviceKeyPair.FromPkcs8PrivateKey(pkcs8);

            Assert.Equal(original.GetFingerprint(), restored.GetFingerprint());
            Assert.Equal(original.GetPublicKeyBase64(), restored.GetPublicKeyBase64());

            byte[] data = new byte[] { 1, 2, 3, 4, 5 };
            byte[] signature = restored.Sign(data);
            Assert.True(DeviceSignatureVerifier.VerifySignature(original.ExportPublicKeySpki(), data, signature));
        }

        [Fact]
        public void DeviceSignatureVerifier_VerifiesValidSignature_AndRejectsTamperedDataOrSignature()
        {
            using var key = DeviceKeyPair.Generate();
            byte[] pubSpki = key.ExportPublicKeySpki();
            byte[] data = new byte[] { 10, 20, 30, 40 };

            byte[] sig = key.Sign(data);

            // 1. Firma válida sobre datos originales -> OK
            Assert.True(DeviceSignatureVerifier.VerifySignature(pubSpki, data, sig));

            // 2. Datos alterados -> Falla
            byte[] tamperedData = new byte[] { 10, 20, 30, 41 };
            Assert.False(DeviceSignatureVerifier.VerifySignature(pubSpki, tamperedData, sig));

            // 3. Firma alterada -> Falla
            byte[] tamperedSig = (byte[])sig.Clone();
            tamperedSig[^1] ^= 0xFF;
            Assert.False(DeviceSignatureVerifier.VerifySignature(pubSpki, data, tamperedSig));

            // 4. Datos o firma nulos / vacíos -> Falla de forma segura
            Assert.False(DeviceSignatureVerifier.VerifySignature(pubSpki, Array.Empty<byte>(), sig));
            Assert.False(DeviceSignatureVerifier.VerifySignature(pubSpki, data, Array.Empty<byte>()));
            Assert.False(DeviceSignatureVerifier.VerifySignature(Array.Empty<byte>(), data, sig));
        }

        [Fact]
        public void DeviceSignatureVerifier_RejectsWrongPublicKey()
        {
            using var keyAlice = DeviceKeyPair.Generate();
            using var keyBob = DeviceKeyPair.Generate();
            byte[] data = new byte[] { 1, 2, 3 };

            byte[] sigAlice = keyAlice.Sign(data);

            // Alice firma pero el verificador usa la clave pública de Bob -> Falla
            Assert.False(DeviceSignatureVerifier.VerifySignature(keyBob.ExportPublicKeySpki(), data, sigAlice));
        }

        [Fact]
        public void DeviceAuthChallenge_GeneratesCanonicalSignBytesDeterministically()
        {
            Guid sessionId = Guid.Parse("12345678-1234-1234-1234-123456789abc");
            byte[] nonce = new byte[32];
            nonce[0] = 0xAA;
            nonce[31] = 0xBB;

            var challenge = new DeviceAuthChallenge(sessionId, BaseTime, nonce, "127.0.0.1:24642");
            byte[] bytes1 = challenge.BuildSignBytes();
            byte[] bytes2 = challenge.BuildSignBytes();

            Assert.Equal(bytes1, bytes2);
            Assert.True(bytes1.Length > 32 + 16 + 8);
        }

        [Fact]
        public void ServerChallengeManager_IssuesChallenge_AndConsumesNonceOnSuccessfulVerification()
        {
            var manager = new ServerChallengeManager();
            using var clientKey = DeviceKeyPair.Generate();

            var challenge = manager.Issue("192.168.1.50", BaseTime, "192.168.1.1:24642");
            Assert.Equal(1, manager.PendingCount);

            byte[] signBytes = challenge.BuildSignBytes();
            byte[] signature = clientKey.Sign(signBytes);

            // Verificación exitosa
            bool ok = manager.TryVerifyAndConsume(
                "192.168.1.50",
                challenge.Nonce,
                clientKey.ExportPublicKeySpki(),
                signature,
                BaseTime.AddSeconds(5),
                out string fingerprint,
                out string failureReason);

            Assert.True(ok, failureReason);
            Assert.Equal(clientKey.GetFingerprint(), fingerprint);
            Assert.Equal(0, manager.PendingCount);

            // Reintento con el mismo nonce (replay attack) debe fallar porque ya fue quemado
            bool replay = manager.TryVerifyAndConsume(
                "192.168.1.50",
                challenge.Nonce,
                clientKey.ExportPublicKeySpki(),
                signature,
                BaseTime.AddSeconds(6),
                out _,
                out string replayReason);

            Assert.False(replay);
            Assert.Contains("consumido", replayReason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ServerChallengeManager_ConsumesNonceOnFailedAttempt_PreventingRetries()
        {
            var manager = new ServerChallengeManager();
            using var clientKey = DeviceKeyPair.Generate();
            using var intruderKey = DeviceKeyPair.Generate();

            var challenge = manager.Issue("192.168.1.50", BaseTime, "192.168.1.1:24642");

            // Intruso envía firma errónea con otra clave
            byte[] intruderSig = intruderKey.Sign(new byte[] { 99, 99 });
            bool ok = manager.TryVerifyAndConsume(
                "192.168.1.50",
                challenge.Nonce,
                clientKey.ExportPublicKeySpki(),
                intruderSig,
                BaseTime.AddSeconds(2),
                out _,
                out string reason);

            Assert.False(ok);
            Assert.Contains("inválida", reason, StringComparison.OrdinalIgnoreCase);

            // El nonce quedó destruido; incluso el cliente legítimo ya no puede usarlo
            byte[] legitSig = clientKey.Sign(challenge.BuildSignBytes());
            bool retry = manager.TryVerifyAndConsume(
                "192.168.1.50",
                challenge.Nonce,
                clientKey.ExportPublicKeySpki(),
                legitSig,
                BaseTime.AddSeconds(3),
                out _,
                out string retryReason);

            Assert.False(retry);
            Assert.Contains("consumido", retryReason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ServerChallengeManager_RejectsExpiredChallengeAfter30Seconds()
        {
            var manager = new ServerChallengeManager();
            using var clientKey = DeviceKeyPair.Generate();

            var challenge = manager.Issue("192.168.1.50", BaseTime);
            byte[] sig = clientKey.Sign(challenge.BuildSignBytes());

            // 31 segundos después -> Caducado
            bool ok = manager.TryVerifyAndConsume(
                "192.168.1.50",
                challenge.Nonce,
                clientKey.ExportPublicKeySpki(),
                sig,
                BaseTime.AddSeconds(31),
                out _,
                out string reason);

            Assert.False(ok);
            Assert.Contains("caducado", reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ServerChallengeManager_RejectsMismatchedIp()
        {
            var manager = new ServerChallengeManager();
            using var clientKey = DeviceKeyPair.Generate();

            var challenge = manager.Issue("192.168.1.50", BaseTime);
            byte[] sig = clientKey.Sign(challenge.BuildSignBytes());

            // Petición llega desde una IP distinta a la que pidió el desafío
            bool ok = manager.TryVerifyAndConsume(
                "10.0.0.99",
                challenge.Nonce,
                clientKey.ExportPublicKeySpki(),
                sig,
                BaseTime.AddSeconds(2),
                out _,
                out string reason);

            Assert.False(ok);
            Assert.Contains("no coincide", reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ServerChallengeManager_EvictsOldestWhenMaxPendingExceeded()
        {
            var manager = new ServerChallengeManager();

            // Emitir el máximo de desafíos
            for (int i = 0; i < DeviceCryptoConstants.MaxPendingChallenges; i++)
            {
                manager.Issue($"10.0.0.{i % 250 + 1}", BaseTime.AddMilliseconds(i));
            }
            Assert.Equal(DeviceCryptoConstants.MaxPendingChallenges, manager.PendingCount);

            // Al emitir uno más, el contador no desborda la memoria (desaloja el más antiguo)
            manager.Issue("192.168.100.1", BaseTime.AddSeconds(1));
            Assert.Equal(DeviceCryptoConstants.MaxPendingChallenges, manager.PendingCount);
        }

        [Fact]
        public void DeviceAuthWire_PacksAndParsesDiscoveryResponseCorrectly()
        {
            Guid sessionId = Guid.NewGuid();
            byte[] nonce = RandomNumberGenerator.GetBytes(32);
            var challenge = new DeviceAuthChallenge(sessionId, BaseTime, nonce, "192.168.1.1:24642");

            byte[] wire = DeviceAuthWire.PackDiscoveryChallenge(challenge);
            Assert.Equal(DeviceAuthWire.DiscoveryChallengePayloadLength, wire.Length);

            bool ok = DeviceAuthWire.TryParseDiscoveryChallenge(wire, "192.168.1.1:24642", out DeviceAuthChallenge? parsed);
            Assert.True(ok);
            Assert.NotNull(parsed);
            Assert.Equal(sessionId, parsed.ServerSessionId);
            Assert.Equal(BaseTime.ToUnixTimeMilliseconds(), parsed.TimestampUtc.ToUnixTimeMilliseconds());
            Assert.Equal(nonce, parsed.Nonce);
            Assert.Equal("192.168.1.1:24642", parsed.TargetEndpoint);
        }

        [Fact]
        public void DeviceAuthWire_PacksAndParsesHailPayloadCorrectly()
        {
            using var clientKey = DeviceKeyPair.Generate();
            byte[] nonce = RandomNumberGenerator.GetBytes(32);
            byte[] spki = clientKey.ExportPublicKeySpki();
            byte[] signature = clientKey.Sign(new byte[] { 1, 2, 3 });

            byte[] packed = DeviceAuthWire.PackHailPayload(nonce, BaseTime, spki, signature);
            Assert.NotNull(packed);

            bool ok = DeviceAuthWire.TryParseHailPayload(
                packed,
                out byte[]? parsedNonce,
                out DateTimeOffset parsedTime,
                out byte[]? parsedSpki,
                out byte[]? parsedSig);

            Assert.True(ok);
            Assert.Equal(nonce, parsedNonce);
            Assert.Equal(BaseTime.ToUnixTimeMilliseconds(), parsedTime.ToUnixTimeMilliseconds());
            Assert.Equal(spki, parsedSpki);
            Assert.Equal(signature, parsedSig);
        }

        [Fact]
        public void DeviceAuthWire_RejectsMalformedPayloads()
        {
            Assert.False(DeviceAuthWire.TryParseDiscoveryChallenge(new byte[10], "host", out _));
            Assert.False(DeviceAuthWire.TryParseHailPayload(new byte[10], out _, out _, out _, out _));

            byte[] fakeMarker = new byte[61];
            Assert.False(DeviceAuthWire.TryParseDiscoveryChallenge(fakeMarker, "host", out _));
        }

        [Fact]
        public void EndToEnd_ClientHandshake_GeneratesProof_VerifiedByServer()
        {
            var serverChallenges = new ServerChallengeManager();
            using var clientKey = DeviceKeyPair.Generate();
            string serverHost = "stardew.example.com:24642";

            // 1. Host emite desafío durante sendVersionInfo
            var serverChallenge = serverChallenges.Issue("83.44.12.34", BaseTime, serverHost);
            byte[] discoveryPayload = DeviceAuthWire.PackDiscoveryChallenge(serverChallenge);

            // 2. Cliente recibe el paquete de descubrimiento en receiveHandshake
            Assert.True(DeviceAuthWire.TryParseDiscoveryChallenge(discoveryPayload, serverHost, out var clientChallenge));
            Assert.NotNull(clientChallenge);

            // 3. Cliente firma con su clave privada y empaqueta el hail
            byte[] clientSignBytes = clientChallenge.BuildSignBytes();
            byte[] clientSignature = clientKey.Sign(clientSignBytes);
            byte[] hailPayload = DeviceAuthWire.PackHailPayload(
                clientChallenge.Nonce,
                clientChallenge.TimestampUtc,
                clientKey.ExportPublicKeySpki(),
                clientSignature);

            // 4. Host recibe el hail y lo desempaqueta en sendAvailableFarmhands
            Assert.True(DeviceAuthWire.TryParseHailPayload(hailPayload, out var recNonce, out _, out var recSpki, out var recSig));
            Assert.NotNull(recNonce);
            Assert.NotNull(recSpki);
            Assert.NotNull(recSig);

            // 5. Host valida y consume el desafío
            bool verified = serverChallenges.TryVerifyAndConsume(
                "83.44.12.34",
                recNonce,
                recSpki,
                recSig,
                BaseTime.AddSeconds(2),
                out string fingerprint,
                out string error);

            Assert.True(verified, error);
            Assert.Equal(clientKey.GetFingerprint(), fingerprint);
        }
    }
}
