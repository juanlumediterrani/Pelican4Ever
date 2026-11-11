using System;
using System.IO;
using System.Security.Cryptography;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.Security;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests.Security
{
    /// <summary>
    /// Pruebas integrales de extremo a extremo para la Épica 18 (TASK-1807):
    /// Simula el ciclo completo de dos jugadores simultáneos (mTx migrado y Dextrure nueva alta),
    /// aislamiento de ranuras, cambio de IP, revocación, recuperación, replay y fallos de versión.
    /// </summary>
    public class DeviceIdentityIntegrationTests : IDisposable
    {
        private readonly string tempDir;
        private readonly DateTimeOffset BaseTime = new(2026, 11, 11, 15, 0, 0, TimeSpan.Zero);

        public DeviceIdentityIntegrationTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "p4e_integration_tests_" + Guid.NewGuid().ToString("N"));
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
        public void CompleteLifecycle_TwoPlayers_SlotIsolation_MultiIp_RevocationAndRecovery()
        {
            string sidecarPath = Path.Combine(tempDir, "pelican4ever_pairing.json");
            var state = new ServerWorldState();
            var challengeManager = new ServerChallengeManager();

            // ────────────────── 1. Estado inicial de la partida con mTx (jugador preexistente) ──────────────────
            long mtxId = 555L;
            var mtxRecord = state.GetOrCreatePlayer(mtxId, "mTx");
            mtxRecord.OwnerToken = "legacy_compromised_token_mtx";
            mtxRecord.FarmSlotId = "Pelican4Ever_Farm_01";
            mtxRecord.Satiety = 100f;
            state.Doors["Farm_Portal_01"] = new DoorAssignment
            {
                DoorId = "Farm_Portal_01",
                FarmSlotId = "Pelican4Ever_Farm_01",
                OwnerId = mtxId
            };
            SidecarPersistence.SaveAtomic(sidecarPath, state);

            // ────────────────── 2. Migración explícita de mTx ──────────────────
            // Generación de par de claves en el cliente de mTx
            using var mtxKey = DeviceKeyPair.Generate();
            byte[] mtxSpki = mtxKey.ExportPublicKeySpki();
            string mtxFp = mtxKey.GetFingerprint();

            // Descubrimiento y emisión de desafío
            string mtxOriginalIp = "83.44.12.34";
            var mtxChallenge = challengeManager.Issue(mtxOriginalIp, BaseTime, $"{mtxOriginalIp}:24642");
            byte[] mtxSig = mtxKey.Sign(mtxChallenge.BuildSignBytes());

            // Registro en PendingEnrollments tras validar firma
            var enrollMtx = DeviceEnrollmentLogic.ProcessEnrollment(
                state,
                mtxChallenge.Nonce,
                mtxSpki,
                mtxSig,
                mtxOriginalIp,
                BaseTime,
                challengeManager,
                out var mtxEnrollmentReq,
                out _);
            // Sin invitación previa, una solicitud no autorizada no puede entrar al lobby
            Assert.Equal(EnrollmentResult.DeniedNoInvite, enrollMtx);

            // Propietario genera invitación de 72h para mTx y mTx repite desafío
            ClientPairingLogic.CreateInvite(state, "mTx", mtxOriginalIp, TimeSpan.FromHours(72), BaseTime);
            var mtxChallenge2 = challengeManager.Issue(mtxOriginalIp, BaseTime.AddSeconds(1), $"{mtxOriginalIp}:24642");
            byte[] mtxSig2 = mtxKey.Sign(mtxChallenge2.BuildSignBytes());
            Assert.Equal(EnrollmentResult.PendingApproval, DeviceEnrollmentLogic.ProcessEnrollment(
                state,
                mtxChallenge2.Nonce,
                mtxSpki,
                mtxSig2,
                mtxOriginalIp,
                BaseTime.AddSeconds(1),
                challengeManager,
                out _,
                out _));

            // Copia de seguridad verificable previa a migrar
            Assert.True(DeviceMigrationLogic.CreateVerifiableBackup(sidecarPath, state, BaseTime, out var mtxBackup, out _));

            // Propietario ejecuta migración vinculando huella a mTx
            Assert.True(DeviceMigrationLogic.MigratePlayer(state, mtxFp, "mTx", BaseTime.AddMinutes(2), out var mtxDev, out _, out string migMsg));
            Assert.Equal(mtxId, mtxDev!.BoundPlayerId);
            Assert.Equal(mtxFp, state.Players[mtxId].OwnerToken);
            Assert.DoesNotContain("legacy_compromised_token_mtx", state.AuthorizedClients.Keys);

            // ────────────────── 3. mTx cambia de IP y entra sin invitación ──────────────────
            string mtxNewIp = "92.50.10.20"; // IP residencial distinta
            var mtxChallengeNewIp = challengeManager.Issue(mtxNewIp, BaseTime.AddHours(2), $"{mtxNewIp}:24642");
            byte[] mtxSigNewIp = mtxKey.Sign(mtxChallengeNewIp.BuildSignBytes());

            // Verificación del desafío
            Assert.True(challengeManager.TryVerifyAndConsume(
                mtxNewIp,
                mtxChallengeNewIp.Nonce,
                mtxSpki,
                mtxSigNewIp,
                BaseTime.AddHours(2),
                out string verifiedMtxFp,
                out _));
            Assert.Equal(mtxFp, verifiedMtxFp);

            // Host autoriza acceso a la lista de peones sin requerir nueva invitación
            Assert.True(FarmhandAuthLogic.CanAccessFarmhandList(state, true, false, verifiedMtxFp, out _));

            // Host autoriza a mTx entrar en su slot 555L
            Assert.Equal(SlotSelectionResult.AllowedExisting, FarmhandAuthLogic.AuthorizeSlotSelection(state, mtxId, verifiedMtxFp, out _));

            // ────────────────── 4. Alta asíncrona de Dextrure (nuevo jugador) ──────────────────
            string dexIp = "190.10.20.30";
            ClientPairingLogic.CreateInvite(state, "Dextrure", dexIp, TimeSpan.FromHours(72), BaseTime.AddHours(2));

            using var dexKey = DeviceKeyPair.Generate();
            byte[] dexSpki = dexKey.ExportPublicKeySpki();
            string dexFp = dexKey.GetFingerprint();

            var dexChallenge = challengeManager.Issue(dexIp, BaseTime.AddHours(2), $"{dexIp}:24642");
            byte[] dexSig = dexKey.Sign(dexChallenge.BuildSignBytes());

            Assert.Equal(EnrollmentResult.PendingApproval, DeviceEnrollmentLogic.ProcessEnrollment(
                state,
                dexChallenge.Nonce,
                dexSpki,
                dexSig,
                dexIp,
                BaseTime.AddHours(2),
                challengeManager,
                out var dexEnrollment,
                out _));

            // Aprobación fuera de banda por el propietario (Putre DM o p4e_pair_approve)
            Assert.True(DeviceEnrollmentLogic.ApproveEnrollment(state, dexFp, out var dexDev, out _));
            Assert.NotNull(dexDev);
            Assert.Equal(DeviceStatus.Active, dexDev!.Status);
            Assert.Equal(0, dexDev.BoundPlayerId); // Libre para seleccionar slot

            // Dextrure obtiene la lista de peones higienizada
            Assert.True(FarmhandAuthLogic.CanAccessFarmhandList(state, true, false, dexFp, out _));
            string sanitizedMtxSlot = FarmhandAuthLogic.GetSanitizedFarmhandUserId(state, mtxId, state.Players[mtxId]);
            Assert.Equal(mtxFp, sanitizedMtxSlot); // Solo huella pública, ningún secreto

            // ────────────────── 5. Aislamiento de ranuras entre jugadores ──────────────────
            // Dextrure intenta seleccionar el personaje de mTx (555L) -> RECHAZADO
            Assert.Equal(SlotSelectionResult.DeniedBelongsToAnother, FarmhandAuthLogic.AuthorizeSlotSelection(state, mtxId, dexFp, out string denyMsg));
            Assert.Contains("otro jugador", denyMsg);

            // Dextrure selecciona una ranura libre nueva (1002L) -> PERMITIDO
            long freeSlotId = 1002L;
            Assert.Equal(SlotSelectionResult.AllowedClaimedNew, FarmhandAuthLogic.AuthorizeSlotSelection(state, freeSlotId, dexFp, out _));
            dexDev.BoundPlayerId = freeSlotId;
            var dexRecord = state.GetOrCreatePlayer(freeSlotId, "Dextrure");
            dexRecord.OwnerToken = dexFp;

            // Dextrure intenta ahora tomar una segunda ranura libre (1003L) -> RECHAZADO
            Assert.Equal(SlotSelectionResult.DeniedAlreadyHasBoundPlayer, FarmhandAuthLogic.AuthorizeSlotSelection(state, 1003L, dexFp, out _));

            // ────────────────── 6. Revocación de Dextrure ──────────────────
            // Propietario revoca a Dextrure por extravío o incidente
            Assert.True(ClientPairingLogic.Revoke(state, "Dextrure", out var affectedIds, out _));
            Assert.Contains(freeSlotId, affectedIds);
            Assert.Equal(DeviceStatus.Revoked, state.AuthorizedDevices[dexFp].Status);

            // Dextrure revocado intenta conectar de nuevo -> BLOQUEADO en el lobby
            Assert.False(FarmhandAuthLogic.CanAccessFarmhandList(state, true, false, dexFp, out string revReason));
            Assert.Contains("revocado", revReason);

            // La ranura y residencia de Dextrure NO se eliminan (progreso preservado)
            Assert.True(state.Players.ContainsKey(freeSlotId));

            // ────────────────── 7. Recuperación con equipo nuevo de Dextrure ──────────────────
            // Dextrure consigue una máquina nueva y genera un nuevo par de claves
            using var dexReplacementKey = DeviceKeyPair.Generate();
            string dexNewFp = dexReplacementKey.GetFingerprint();
            state.AuthorizedDevices[dexNewFp] = new AuthorizedDevice
            {
                Fingerprint = dexNewFp,
                PublicKeyBase64 = dexReplacementKey.GetPublicKeyBase64(),
                Alias = "Dextrure_NewDevice",
                Status = DeviceStatus.Active
            };

            // Propietario aprueba la recuperación vinculando la nueva huella al personaje existente (1002L)
            Assert.True(DeviceMigrationLogic.MigratePlayer(state, dexNewFp, "1002", BaseTime.AddDays(1), out var recoveredDev, out var recoveredRec, out _));
            Assert.Equal(freeSlotId, recoveredDev!.BoundPlayerId);
            Assert.Equal(dexNewFp, recoveredRec!.OwnerToken);

            // El dispositivo revocado anterior SIGUE bloqueado permanentemente
            Assert.False(FarmhandAuthLogic.CanAccessFarmhandList(state, true, false, dexFp, out _));

            // El dispositivo recuperado entra exitosamente a su personaje 1002L
            Assert.True(FarmhandAuthLogic.CanAccessFarmhandList(state, true, false, dexNewFp, out _));
            Assert.Equal(SlotSelectionResult.AllowedExisting, FarmhandAuthLogic.AuthorizeSlotSelection(state, freeSlotId, dexNewFp, out _));
        }

        [Fact]
        public void NegativeSecurityTests_Replay_Spoofing_VersionMismatch_Tampering()
        {
            var state = new ServerWorldState();
            var challengeManager = new ServerChallengeManager();
            using var aliceKey = DeviceKeyPair.Generate();
            byte[] aliceSpki = aliceKey.ExportPublicKeySpki();
            string aliceFp = aliceKey.GetFingerprint();
            string aliceIp = "83.44.12.34";

            state.AuthorizedDevices[aliceFp] = new AuthorizedDevice
            {
                Fingerprint = aliceFp,
                PublicKeyBase64 = aliceKey.GetPublicKeyBase64(),
                Alias = "Alice",
                Status = DeviceStatus.Active
            };

            var challenge = challengeManager.Issue(aliceIp, BaseTime, $"{aliceIp}:24642");
            byte[] signature = aliceKey.Sign(challenge.BuildSignBytes());

            // 1. Verificación legítima consume el nonce (un solo uso atómico)
            Assert.True(challengeManager.TryVerifyAndConsume(
                aliceIp,
                challenge.Nonce,
                aliceSpki,
                signature,
                BaseTime,
                out _,
                out _));

            // 2. Ataque de Replay: Un atacante intercepta la firma y la reenvía
            Assert.False(challengeManager.TryVerifyAndConsume(
                aliceIp,
                challenge.Nonce,
                aliceSpki,
                signature,
                BaseTime.AddSeconds(1),
                out _,
                out string replayErr));
            Assert.Contains("ya consumido", replayErr);

            // 3. Ataque de Suplantación: Atacante usa la clave pública de Alice pero firma con otra clave privada
            var challengeSpoof = challengeManager.Issue("192.168.1.50", BaseTime, "192.168.1.50:24642");
            using var attackerKey = DeviceKeyPair.Generate();
            byte[] forgedSignature = attackerKey.Sign(challengeSpoof.BuildSignBytes());

            Assert.False(challengeManager.TryVerifyAndConsume(
                "192.168.1.50",
                challengeSpoof.Nonce,
                aliceSpki, // Clave pública de Alice
                forgedSignature, // Firma del atacante
                BaseTime,
                out _,
                out string spoofErr));
            Assert.Contains("Firma criptográfica inválida", spoofErr);

            // 4. Caducidad de desafío (> 30 segundos)
            var challengeExpired = challengeManager.Issue(aliceIp, BaseTime, $"{aliceIp}:24642");
            byte[] sigExpired = aliceKey.Sign(challengeExpired.BuildSignBytes());
            Assert.False(challengeManager.TryVerifyAndConsume(
                aliceIp,
                challengeExpired.Nonce,
                aliceSpki,
                sigExpired,
                BaseTime.AddSeconds(31), // 31 segundos después
                out _,
                out string expiredErr));
            Assert.Contains("caducado", expiredErr);

            // 5. Incompatibilidad de versión: Hail corrupto o cliente antiguo
            byte[] badHail = new byte[] { 1, 2, 3, 4 };
            Assert.False(DeviceAuthWire.TryParseHailPayload(badHail, out _, out _, out _, out _));
        }
    }
}
