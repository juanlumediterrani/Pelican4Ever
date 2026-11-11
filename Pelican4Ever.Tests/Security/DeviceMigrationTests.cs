using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.Security;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests.Security
{
    public class DeviceMigrationTests : IDisposable
    {
        private readonly string tempDir;
        private readonly DateTimeOffset BaseTime = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

        public DeviceMigrationTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "p4e_migration_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch { }
        }

        [Fact]
        public void CreateVerifiableBackup_GeneratesFileAndMatchingSha256Checksum()
        {
            string sidecarPath = Path.Combine(tempDir, "pelican4ever_pairing.json");
            var state = new ServerWorldState();
            state.AuthorizedDevices["fp_dummy"] = new AuthorizedDevice { Fingerprint = "fp_dummy", Alias = "TestDev" };

            // Guardar archivo inicial
            SidecarPersistence.SaveAtomic(sidecarPath, state);

            bool ok = DeviceMigrationLogic.CreateVerifiableBackup(
                sidecarPath,
                state,
                BaseTime,
                out VerifiableBackupResult? result,
                out string? error);

            Assert.True(ok, error);
            Assert.NotNull(result);
            Assert.True(File.Exists(result!.BackupPath));
            Assert.True(File.Exists(result.BackupPath + ".sha256"));
            Assert.False(string.IsNullOrWhiteSpace(result.Sha256Checksum));

            // Verificar integridad con la función de verificación
            bool verified = DeviceMigrationLogic.VerifyBackupIntegrity(
                result.BackupPath,
                result.Sha256Checksum,
                out string actualSha,
                out string? verifyError);

            Assert.True(verified, verifyError);
            Assert.Equal(result.Sha256Checksum, actualSha);
        }

        [Fact]
        public void VerifyBackupIntegrity_DetectsTampering()
        {
            string sidecarPath = Path.Combine(tempDir, "pelican4ever_pairing.json");
            var state = new ServerWorldState();
            SidecarPersistence.SaveAtomic(sidecarPath, state);

            Assert.True(DeviceMigrationLogic.CreateVerifiableBackup(sidecarPath, state, BaseTime, out var result, out _));

            // Modificar 1 byte en el archivo de backup para simular corrupción / manipulación
            byte[] bytes = File.ReadAllBytes(result!.BackupPath);
            bytes[0] ^= 0xFF;
            File.WriteAllBytes(result.BackupPath, bytes);

            bool verified = DeviceMigrationLogic.VerifyBackupIntegrity(
                result.BackupPath,
                result.Sha256Checksum,
                out _,
                out string? error);

            Assert.False(verified);
            Assert.NotNull(error);
            Assert.Contains("no coincide", error);
        }

        [Fact]
        public void RestoreFromBackup_PreservesRevokedStatusAndDoesNotReactivateLegacyTokens()
        {
            string sidecarPath = Path.Combine(tempDir, "pelican4ever_pairing.json");
            var state = new ServerWorldState();
            state.AuthorizedDevices["fp_dev1"] = new AuthorizedDevice { Fingerprint = "fp_dev1", Status = DeviceStatus.Active };
            state.AuthorizedClients["legacy_compromised_token"] = new AuthorizedClient { ClientToken = "legacy_compromised_token" };

            SidecarPersistence.SaveAtomic(sidecarPath, state);
            Assert.True(DeviceMigrationLogic.CreateVerifiableBackup(sidecarPath, state, BaseTime, out var result, out _));

            // Simular que en targetState el dispositivo fp_dev1 fue REVOCADO
            var targetState = new ServerWorldState();
            targetState.AuthorizedDevices["fp_dev1"] = new AuthorizedDevice { Fingerprint = "fp_dev1", Status = DeviceStatus.Revoked };

            // Restaurar backup antiguo donde dev1 era Active y existía un legacy token
            bool restored = DeviceMigrationLogic.RestoreFromBackup(
                result!.BackupPath,
                result.Sha256Checksum,
                targetState,
                out string msg);

            Assert.True(restored, msg);
            // El dispositivo revocado NO se reactiva silenciosamente
            Assert.Equal(DeviceStatus.Revoked, targetState.AuthorizedDevices["fp_dev1"].Status);
            // Los tokens legados no se restauran como accesos válidos
            Assert.DoesNotContain("legacy_compromised_token", targetState.AuthorizedClients.Keys);
        }

        [Fact]
        public void MigratePlayer_MigratesMtxPreservingUniqueIdResidenceAndProgress()
        {
            var state = new ServerWorldState();

            // Configuración previa de mTx (personaje existente con token legado)
            long mtxPlayerId = 555L;
            var player = state.GetOrCreatePlayer(mtxPlayerId, "mTx");
            player.OwnerToken = "legacy_juanlu_secret_token";
            player.FarmSlotId = "Pelican4Ever_Farm_01";
            player.Satiety = 125.5f;

            // Residencia asociada al personaje
            state.Doors["Farm_Portal_01"] = new DoorAssignment
            {
                DoorId = "Farm_Portal_01",
                FarmSlotId = "Pelican4Ever_Farm_01",
                OwnerId = mtxPlayerId
            };

            // Cliente legado en AuthorizedClients
            state.AuthorizedClients["legacy_juanlu_secret_token"] = new AuthorizedClient
            {
                ClientToken = "legacy_juanlu_secret_token",
                Alias = "mTx",
                BoundPlayerId = mtxPlayerId
            };

            // Invitación temporal por IP para el nuevo cliente
            var invite = ClientPairingLogic.CreateInvite(state, "mTx", "83.44.12.34", TimeSpan.FromHours(72), BaseTime);

            // Nuevo par de claves de mTx en su dispositivo
            using var clientKey = DeviceKeyPair.Generate();
            byte[] spki = clientKey.ExportPublicKeySpki();
            string fp = clientKey.GetFingerprint();

            // Desafío firmado con prueba de posesión
            var challengeManager = new ServerChallengeManager();
            var challenge = challengeManager.Issue("83.44.12.34", BaseTime, "83.44.12.34:24642");
            byte[] sig = clientKey.Sign(challenge.BuildSignBytes());

            // Registro de alta pendiente tras validar el desafío
            var enrollLogic = DeviceEnrollmentLogic.ProcessEnrollment(
                state,
                challenge.Nonce,
                spki,
                sig,
                "83.44.12.34",
                BaseTime,
                challengeManager,
                out var enrollment,
                out _);
            Assert.Equal(EnrollmentResult.PendingApproval, enrollLogic);

            // Migración explícita aprobada por el propietario del host
            bool migrated = DeviceMigrationLogic.MigratePlayer(
                state,
                fp,
                "mTx",
                BaseTime.AddMinutes(5),
                out AuthorizedDevice? authorized,
                out PlayerRecord? boundPlayer,
                out string message);

            Assert.True(migrated, message);
            Assert.NotNull(authorized);
            Assert.NotNull(boundPlayer);

            // 1. UniqueMultiplayerID, residencia, slot y progreso intactos
            Assert.Equal(mtxPlayerId, boundPlayer!.UniqueMultiplayerID);
            Assert.Equal("mTx", boundPlayer.PlayerName);
            Assert.Equal("Pelican4Ever_Farm_01", boundPlayer.FarmSlotId);
            Assert.Equal(125.5f, boundPlayer.Satiety);
            Assert.Equal(mtxPlayerId, state.FindResidenceOf(mtxPlayerId)?.OwnerId);

            // 2. Token legado retirado y sustituido por la huella pública
            Assert.Equal(fp, boundPlayer.OwnerToken);
            Assert.DoesNotContain("legacy_juanlu_secret_token", state.AuthorizedClients.Keys);

            // 3. Dispositivo autorizado activo vinculado al personaje
            Assert.True(state.AuthorizedDevices.ContainsKey(fp));
            Assert.Equal(DeviceStatus.Active, state.AuthorizedDevices[fp].Status);
            Assert.Equal(mtxPlayerId, state.AuthorizedDevices[fp].BoundPlayerId);

            // 4. Solicitud pendiente e invitación consumidas
            Assert.Empty(state.PendingEnrollments);
            Assert.Empty(state.PairingInvites);
        }

        [Fact]
        public void MigratePlayer_RejectsThirdPartyWithLegacyTokenAlone()
        {
            var state = new ServerWorldState();
            long mtxPlayerId = 555L;
            var player = state.GetOrCreatePlayer(mtxPlayerId, "mTx");
            player.OwnerToken = "legacy_juanlu_secret_token";

            // Un tercero intenta migrar presentando únicamente el token legado como identificador de dispositivo
            bool ok = DeviceMigrationLogic.MigratePlayer(
                state,
                "legacy_juanlu_secret_token",
                "mTx",
                BaseTime,
                out _,
                out _,
                out string error);

            Assert.False(ok);
            Assert.Contains("desafío criptográfico", error);
        }

        [Fact]
        public void MigratePlayer_IsIdempotent()
        {
            var state = new ServerWorldState();
            long mtxPlayerId = 555L;
            var player = state.GetOrCreatePlayer(mtxPlayerId, "mTx");

            using var clientKey = DeviceKeyPair.Generate();
            string fp = clientKey.GetFingerprint();

            state.AuthorizedDevices[fp] = new AuthorizedDevice
            {
                Fingerprint = fp,
                PublicKeyBase64 = clientKey.GetPublicKeyBase64(),
                Alias = "mTx",
                BoundPlayerId = mtxPlayerId,
                Status = DeviceStatus.Active
            };
            player.OwnerToken = fp;

            // Llamar una segunda vez con los mismos datos
            bool ok = DeviceMigrationLogic.MigratePlayer(
                state,
                fp,
                "mTx",
                BaseTime,
                out var dev,
                out var rec,
                out string msg);

            Assert.True(ok, msg);
            Assert.Equal(mtxPlayerId, dev!.BoundPlayerId);
            Assert.Equal(fp, rec!.OwnerToken);
            Assert.Contains("ya está vinculado", msg);
        }

        [Fact]
        public void MigratePlayer_AllowsConnectingFromDifferentIpAfterMigration()
        {
            var state = new ServerWorldState();
            long mtxPlayerId = 555L;
            var player = state.GetOrCreatePlayer(mtxPlayerId, "mTx");
            player.OwnerToken = "legacy_token_old";

            using var clientKey = DeviceKeyPair.Generate();
            string fp = clientKey.GetFingerprint();

            // Simular pre-registro en AuthorizedDevices
            state.AuthorizedDevices[fp] = new AuthorizedDevice
            {
                Fingerprint = fp,
                PublicKeyBase64 = clientKey.GetPublicKeyBase64(),
                Alias = "mTx",
                BoundPlayerId = 0,
                Status = DeviceStatus.Active
            };

            // Migración explícita
            Assert.True(DeviceMigrationLogic.MigratePlayer(state, fp, "mTx", BaseTime, out _, out _, out _));

            // Simular que mTx se conecta desde una IP COMPLETAMENTE DISTINTA (ej. 92.50.10.20)
            string newIp = "92.50.10.20";

            // 1. IsDeviceAuthorized lo reconoce por huella digital independientemente de la IP
            Assert.True(ServerDeviceRegistry.IsDeviceAuthorized(state, fp, out var dev));
            Assert.Equal(mtxPlayerId, dev!.BoundPlayerId);

            // 2. IsClientTokenAuthorized lo reconoce por huella y actualiza LastSeenIp si aplica
            Assert.Equal(LobbyAccessResult.AllowedPaired, ClientPairingLogic.EvaluateLobbyAccess(state, requireWhitelist: true, fp, newIp, BaseTime.AddDays(1)));

            // 3. PlayerAuthLogic concede acceso al peón para la huella pública
            Assert.Equal(AuthResult.Allowed, PlayerAuthLogic.ValidateOrClaim(player, fp));

            // 4. El atacante con solo el token antiguo "legacy_token_old" es tajantemente denegado
            Assert.Equal(AuthResult.DeniedMismatch, PlayerAuthLogic.ValidateOrClaim(player, "legacy_token_old"));
            Assert.False(ClientPairingLogic.IsClientTokenAuthorized(state, "legacy_token_old", out _));
        }

        [Fact]
        public void MigratePlayer_FailsIfPlayerDoesNotExist()
        {
            var state = new ServerWorldState();
            using var clientKey = DeviceKeyPair.Generate();
            string fp = clientKey.GetFingerprint();
            state.AuthorizedDevices[fp] = new AuthorizedDevice
            {
                Fingerprint = fp,
                PublicKeyBase64 = clientKey.GetPublicKeyBase64(),
                Alias = "Test"
            };

            bool ok = DeviceMigrationLogic.MigratePlayer(
                state,
                fp,
                "Inexistente",
                BaseTime,
                out _,
                out _,
                out string msg);

            Assert.False(ok);
            Assert.Contains("No se encontró ningún personaje", msg);
        }
    }
}
