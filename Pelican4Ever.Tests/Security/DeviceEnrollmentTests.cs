using System;
using System.Security.Cryptography;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.Security;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests.Security
{
    public class DeviceEnrollmentTests
    {
        private static readonly DateTimeOffset BaseTime = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);

        [Fact]
        public void ProcessEnrollment_ValidProofAndActiveInvite_CreatesPendingEnrollment_WithoutClaimingPlayerOrResidence()
        {
            var state = new ServerWorldState();
            var challengeManager = new ServerChallengeManager();
            using var clientKey = DeviceKeyPair.Generate();

            // Invitación de 72 horas para Dextrure en 83.44.12.34
            ClientPairingLogic.CreateInvite(state, "Dextrure", "83.44.12.34", TimeSpan.FromHours(72), BaseTime, "INV123");

            // Host emite desafío
            var challenge = challengeManager.Issue("83.44.12.34", BaseTime, "host:24642");
            byte[] signature = clientKey.Sign(challenge.BuildSignBytes());

            // Procesar primer contacto
            var result = DeviceEnrollmentLogic.ProcessEnrollment(
                state,
                challenge.Nonce,
                clientKey.ExportPublicKeySpki(),
                signature,
                "83.44.12.34",
                BaseTime.AddSeconds(2),
                challengeManager,
                out var enrollment,
                out string message);

            Assert.Equal(EnrollmentResult.PendingApproval, result);
            Assert.NotNull(enrollment);
            Assert.Equal(clientKey.GetFingerprint(), enrollment.Fingerprint);
            Assert.Equal("Dextrure", enrollment.Alias);
            Assert.Equal("83.44.12.34", enrollment.RemoteIp);
            Assert.Equal(1, enrollment.Attempts);

            // Invariante crítica: NO se reclama personaje ni residencia antes de la aprobación
            Assert.Empty(state.AuthorizedDevices);
            Assert.Empty(state.Players);
            Assert.Empty(state.Doors);
            Assert.Empty(state.Annexes);
            Assert.Single(state.PendingEnrollments);
        }

        [Fact]
        public void ProcessEnrollment_InvalidProof_ReturnsFailedInvalidProof_AndDoesNotEnroll()
        {
            var state = new ServerWorldState();
            var challengeManager = new ServerChallengeManager();
            using var clientKey = DeviceKeyPair.Generate();

            ClientPairingLogic.CreateInvite(state, "Dextrure", "83.44.12.34", TimeSpan.FromHours(72), BaseTime);

            var challenge = challengeManager.Issue("83.44.12.34", BaseTime, "host:24642");

            // Firma sobre datos alterados (prueba falsa)
            byte[] badSig = clientKey.Sign(new byte[] { 1, 2, 3 });

            var result = DeviceEnrollmentLogic.ProcessEnrollment(
                state,
                challenge.Nonce,
                clientKey.ExportPublicKeySpki(),
                badSig,
                "83.44.12.34",
                BaseTime.AddSeconds(1),
                challengeManager,
                out var enrollment,
                out string message);

            Assert.Equal(EnrollmentResult.FailedInvalidProof, result);
            Assert.Null(enrollment);
            Assert.Empty(state.PendingEnrollments);
            Assert.Empty(state.AuthorizedDevices);
        }

        [Fact]
        public void ProcessEnrollment_NoActiveInvite_ReturnsDeniedNoInvite()
        {
            var state = new ServerWorldState();
            var challengeManager = new ServerChallengeManager();
            using var clientKey = DeviceKeyPair.Generate();

            // Sin ninguna invitación activa en el servidor
            var challenge = challengeManager.Issue("99.88.77.66", BaseTime, "host:24642");
            byte[] signature = clientKey.Sign(challenge.BuildSignBytes());

            var result = DeviceEnrollmentLogic.ProcessEnrollment(
                state,
                challenge.Nonce,
                clientKey.ExportPublicKeySpki(),
                signature,
                "99.88.77.66",
                BaseTime.AddSeconds(1),
                challengeManager,
                out var enrollment,
                out string message);

            Assert.Equal(EnrollmentResult.DeniedNoInvite, result);
            Assert.Null(enrollment);
            Assert.Empty(state.PendingEnrollments);
        }

        [Fact]
        public void ProcessEnrollment_ExpiredInvite_ReturnsDeniedNoInvite()
        {
            var state = new ServerWorldState();
            var challengeManager = new ServerChallengeManager();
            using var clientKey = DeviceKeyPair.Generate();

            // Invitación creada hace más de 72 horas (caducada)
            ClientPairingLogic.CreateInvite(state, "Dextrure", "83.44.12.34", TimeSpan.FromHours(72), BaseTime);

            DateTimeOffset threeDaysLater = BaseTime.AddHours(73);
            var challenge = challengeManager.Issue("83.44.12.34", threeDaysLater, "host:24642");
            byte[] signature = clientKey.Sign(challenge.BuildSignBytes());

            var result = DeviceEnrollmentLogic.ProcessEnrollment(
                state,
                challenge.Nonce,
                clientKey.ExportPublicKeySpki(),
                signature,
                "83.44.12.34",
                threeDaysLater.AddSeconds(1),
                challengeManager,
                out _,
                out string message);

            Assert.Equal(EnrollmentResult.DeniedNoInvite, result);
            Assert.Empty(state.PairingInvites); // Purga automática
            Assert.Empty(state.PendingEnrollments);
        }

        [Fact]
        public void ProcessEnrollment_AlreadyAuthorizedDevice_ReturnsAlreadyAuthorized()
        {
            var state = new ServerWorldState();
            var challengeManager = new ServerChallengeManager();
            using var clientKey = DeviceKeyPair.Generate();

            // Dispositivo ya registrado previamente
            ServerDeviceRegistry.RegisterOrUpdateDevice(state, clientKey.GetPublicKeyBase64(), "Dextrure");

            var challenge = challengeManager.Issue("83.44.12.34", BaseTime, "host:24642");
            byte[] signature = clientKey.Sign(challenge.BuildSignBytes());

            var result = DeviceEnrollmentLogic.ProcessEnrollment(
                state,
                challenge.Nonce,
                clientKey.ExportPublicKeySpki(),
                signature,
                "83.44.12.34",
                BaseTime.AddSeconds(1),
                challengeManager,
                out _,
                out string message);

            Assert.Equal(EnrollmentResult.AlreadyAuthorized, result);
            Assert.Empty(state.PendingEnrollments);
        }

        [Fact]
        public void ProcessEnrollment_ExceedsMaxAttempts_ReturnsMaxAttemptsExceeded()
        {
            var state = new ServerWorldState();
            var challengeManager = new ServerChallengeManager();
            using var clientKey = DeviceKeyPair.Generate();

            ClientPairingLogic.CreateInvite(state, "Spammer", "83.44.12.34", TimeSpan.FromHours(72), BaseTime);

            // Realizar intentos sucesivos
            for (int i = 0; i < DeviceEnrollmentLogic.MaxEnrollmentAttempts; i++)
            {
                var challenge = challengeManager.Issue("83.44.12.34", BaseTime.AddSeconds(i * 5), "host:24642");
                byte[] sig = clientKey.Sign(challenge.BuildSignBytes());
                var res = DeviceEnrollmentLogic.ProcessEnrollment(
                    state, challenge.Nonce, clientKey.ExportPublicKeySpki(), sig, "83.44.12.34", BaseTime.AddSeconds(i * 5 + 1), challengeManager, out _, out _);
                Assert.Equal(EnrollmentResult.PendingApproval, res);
            }

            // Intento que excede el límite
            var overChallenge = challengeManager.Issue("83.44.12.34", BaseTime.AddSeconds(100), "host:24642");
            byte[] overSig = clientKey.Sign(overChallenge.BuildSignBytes());
            var overRes = DeviceEnrollmentLogic.ProcessEnrollment(
                state, overChallenge.Nonce, clientKey.ExportPublicKeySpki(), overSig, "83.44.12.34", BaseTime.AddSeconds(101), challengeManager, out _, out string overMsg);

            Assert.Equal(EnrollmentResult.MaxAttemptsExceeded, overRes);
            Assert.Contains("límite", overMsg, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ApproveEnrollment_ByFullFingerprint_AuthorizesDevice_ConsumesInvite_AndClearsPending()
        {
            var state = new ServerWorldState();
            using var clientKey = DeviceKeyPair.Generate();
            string fp = clientKey.GetFingerprint();

            // Invitación activa
            ClientPairingLogic.CreateInvite(state, "Dextrure", "83.44.12.34", TimeSpan.FromHours(72), BaseTime, "INV789");

            // Solicitud pendiente registrada
            state.PendingEnrollments[fp] = new DeviceEnrollmentRequest
            {
                Fingerprint = fp,
                PublicKeyBase64 = clientKey.GetPublicKeyBase64(),
                Alias = "Dextrure",
                RemoteIp = "83.44.12.34",
                InviteCode = "INV789",
                RequestedAtUtc = BaseTime,
                ExpiresAtUtc = BaseTime.AddHours(72)
            };

            // Aprobación fuera de banda por el dueño
            bool approved = DeviceEnrollmentLogic.ApproveEnrollment(
                state, fp, boundPlayerId: 1001L, BaseTime.AddMinutes(5), out var device, out string message);

            Assert.True(approved, message);
            Assert.NotNull(device);
            Assert.Equal(fp, device.Fingerprint);
            Assert.Equal("Dextrure", device.Alias);
            Assert.Equal(1001L, device.BoundPlayerId);
            Assert.Equal(DeviceStatus.Active, device.Status);

            // Invariante: La invitación de un solo uso queda consumida
            Assert.Empty(state.PairingInvites);

            // Invariante: La solicitud pendiente queda eliminada
            Assert.Empty(state.PendingEnrollments);

            // Invariante: El dispositivo queda autorizado
            Assert.True(ServerDeviceRegistry.IsDeviceAuthorized(state, fp, out _));

            // El peón queda asignado al dispositivo
            Assert.True(state.Players.ContainsKey(1001L));
            Assert.Equal(fp, state.Players[1001L].OwnerToken);
        }

        [Fact]
        public void ApproveEnrollment_ByShortFingerprint_AuthorizesDevice()
        {
            var state = new ServerWorldState();
            using var clientKey = DeviceKeyPair.Generate();
            string fp = clientKey.GetFingerprint();
            string shortFp = fp[..12];

            ClientPairingLogic.CreateInvite(state, "Alice", "10.0.0.5", TimeSpan.FromHours(72), BaseTime, "ALICE1");
            state.PendingEnrollments[fp] = new DeviceEnrollmentRequest
            {
                Fingerprint = fp,
                PublicKeyBase64 = clientKey.GetPublicKeyBase64(),
                Alias = "Alice",
                RemoteIp = "10.0.0.5",
                InviteCode = "ALICE1",
                RequestedAtUtc = BaseTime,
                ExpiresAtUtc = BaseTime.AddHours(72)
            };

            // Aprobación usando prefijo de 12 caracteres (como lo vería el dueño en Discord)
            bool ok = DeviceEnrollmentLogic.ApproveEnrollment(state, shortFp, out var dev, out string msg);

            Assert.True(ok, msg);
            Assert.NotNull(dev);
            Assert.Equal(fp, dev.Fingerprint);
            Assert.Empty(state.PendingEnrollments);
            Assert.Empty(state.PairingInvites);
        }

        [Fact]
        public void ApproveEnrollment_ByAlias_AuthorizesDevice()
        {
            var state = new ServerWorldState();
            using var clientKey = DeviceKeyPair.Generate();
            string fp = clientKey.GetFingerprint();

            ClientPairingLogic.CreateInvite(state, "Bob", "10.0.0.6", TimeSpan.FromHours(72), BaseTime, "BOB123");
            state.PendingEnrollments[fp] = new DeviceEnrollmentRequest
            {
                Fingerprint = fp,
                PublicKeyBase64 = clientKey.GetPublicKeyBase64(),
                Alias = "Bob",
                RemoteIp = "10.0.0.6",
                InviteCode = "BOB123",
                RequestedAtUtc = BaseTime,
                ExpiresAtUtc = BaseTime.AddHours(72)
            };

            bool ok = DeviceEnrollmentLogic.ApproveEnrollment(state, "bob", out var dev, out string msg);

            Assert.True(ok, msg);
            Assert.Equal(fp, dev?.Fingerprint);
            Assert.Empty(state.PendingEnrollments);
        }

        [Fact]
        public void RejectOrCancelEnrollment_RemovesPending_WithoutClaimingPlayer()
        {
            var state = new ServerWorldState();
            using var clientKey = DeviceKeyPair.Generate();
            string fp = clientKey.GetFingerprint();

            ClientPairingLogic.CreateInvite(state, "Stranger", "9.9.9.9", TimeSpan.FromHours(72), BaseTime, "STR999");
            state.PendingEnrollments[fp] = new DeviceEnrollmentRequest
            {
                Fingerprint = fp,
                PublicKeyBase64 = clientKey.GetPublicKeyBase64(),
                Alias = "Stranger",
                RemoteIp = "9.9.9.9",
                InviteCode = "STR999",
                RequestedAtUtc = BaseTime,
                ExpiresAtUtc = BaseTime.AddHours(72)
            };

            bool canceled = DeviceEnrollmentLogic.RejectOrCancelEnrollment(state, fp, out string msg);

            Assert.True(canceled, msg);
            Assert.Empty(state.PendingEnrollments);
            Assert.Empty(state.AuthorizedDevices);
            Assert.Empty(state.Players);
            // La invitación original sigue existiendo si el dueño solo rechazó la solicitud concreta
            Assert.Single(state.PairingInvites);
        }

        [Fact]
        public void ApproveEnrollment_NonExistent_ReturnsFalse()
        {
            var state = new ServerWorldState();
            bool ok = DeviceEnrollmentLogic.ApproveEnrollment(state, "unknown_user", out _, out string msg);

            Assert.False(ok);
            Assert.Contains("No se encontró", msg, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TwoPlayers_SameIp_GetDifferentInvites_AndApprovalsDoNotInterfere()
        {
            var state = new ServerWorldState();
            var challengeManager = new ServerChallengeManager();
            using var key1 = DeviceKeyPair.Generate();
            using var key2 = DeviceKeyPair.Generate();
            string sameIp = "83.44.12.34";

            // Dos invitaciones distintas para la misma IP (mismo hogar/NAT)
            ClientPairingLogic.CreateInvite(state, "Jugador1", sameIp, TimeSpan.FromHours(72), BaseTime, "AAA111");
            ClientPairingLogic.CreateInvite(state, "Jugador2", sameIp, TimeSpan.FromHours(72), BaseTime, "BBB222");
            Assert.Equal(2, state.PairingInvites.Count);

            // Jugador1 conecta primero -> reserva AAA111
            var ch1 = challengeManager.Issue(sameIp, BaseTime, "host:24642");
            Assert.Equal(
                EnrollmentResult.PendingApproval,
                DeviceEnrollmentLogic.ProcessEnrollment(
                    state, ch1.Nonce, key1.ExportPublicKeySpki(), key1.Sign(ch1.BuildSignBytes()),
                    sameIp, BaseTime.AddSeconds(1), challengeManager, out var enroll1, out _));
            Assert.Equal("Jugador1", enroll1!.Alias);
            Assert.Equal("AAA111", enroll1.InviteCode);

            // Jugador2 conecta desde la misma IP -> debe reservar BBB222, no duplicar AAA111
            var ch2 = challengeManager.Issue(sameIp, BaseTime.AddSeconds(2), "host:24642");
            Assert.Equal(
                EnrollmentResult.PendingApproval,
                DeviceEnrollmentLogic.ProcessEnrollment(
                    state, ch2.Nonce, key2.ExportPublicKeySpki(), key2.Sign(ch2.BuildSignBytes()),
                    sameIp, BaseTime.AddSeconds(3), challengeManager, out var enroll2, out _));
            Assert.Equal("Jugador2", enroll2!.Alias);
            Assert.Equal("BBB222", enroll2.InviteCode);

            // Aprobar Jugador1 solo consume AAA111; la invitación de Jugador2 sobrevive
            Assert.True(DeviceEnrollmentLogic.ApproveEnrollment(state, key1.GetFingerprint(), out _, out _));
            Assert.Single(state.PairingInvites);
            Assert.Equal("BBB222", state.PairingInvites[0].Code);

            // Aprobar Jugador2 consume la restante
            Assert.True(DeviceEnrollmentLogic.ApproveEnrollment(state, key2.GetFingerprint(), out _, out _));
            Assert.Empty(state.PairingInvites);
        }

        [Fact]
        public void Revoke_ByIp_OnlyClearsInvites_DoesNotRevokeDevicesSharingIp()
        {
            var state = new ServerWorldState();
            using var key1 = DeviceKeyPair.Generate();
            using var key2 = DeviceKeyPair.Generate();
            string sameIp = "83.44.12.34";

            ServerDeviceRegistry.RegisterOrUpdateDevice(state, key1.GetPublicKeyBase64(), "Jugador1", 1001L, sameIp, BaseTime);
            ServerDeviceRegistry.RegisterOrUpdateDevice(state, key2.GetPublicKeyBase64(), "Jugador2", 1002L, sameIp, BaseTime);
            ClientPairingLogic.CreateInvite(state, "Pendiente", sameIp, TimeSpan.FromHours(72), BaseTime, "CCC333");

            Assert.True(ClientPairingLogic.Revoke(state, sameIp, out _, out _));

            // La invitación pendiente se limpia, pero ningún dispositivo autorizado cae por compartir IP
            Assert.Empty(state.PairingInvites);
            Assert.True(ServerDeviceRegistry.IsDeviceAuthorized(state, key1.GetFingerprint(), out _));
            Assert.True(ServerDeviceRegistry.IsDeviceAuthorized(state, key2.GetFingerprint(), out _));
        }
    }
}
