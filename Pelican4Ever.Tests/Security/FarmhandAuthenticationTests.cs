using System;
using System.Text;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.Security;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests.Security
{
    public class FarmhandAuthenticationTests
    {
        private readonly DateTimeOffset BaseTime = new(2026, 11, 11, 14, 0, 0, TimeSpan.Zero);

        [Fact]
        public void GetSanitizedFarmhandUserId_NeverReturnsSecretTokensOrCodes()
        {
            var state = new ServerWorldState();

            // 1. Peón con dispositivo autorizado activo (debe devolver la huella pública SHA-256)
            using var devKey = DeviceKeyPair.Generate();
            string fp = devKey.GetFingerprint();
            state.AuthorizedDevices[fp] = new AuthorizedDevice
            {
                Fingerprint = fp,
                PublicKeyBase64 = devKey.GetPublicKeyBase64(),
                Alias = "Alice",
                BoundPlayerId = 1001L,
                Status = DeviceStatus.Active
            };
            var player1 = state.GetOrCreatePlayer(1001L, "Alice");
            player1.OwnerToken = fp;

            string userId1 = FarmhandAuthLogic.GetSanitizedFarmhandUserId(state, 1001L, player1);
            Assert.Equal(fp, userId1);
            Assert.Equal(64, userId1.Length);

            // 2. Peón sin reclamar (debe devolver cadena vacía)
            var playerFree = state.GetOrCreatePlayer(1002L, "Libre");
            playerFree.OwnerToken = null;
            string userIdFree = FarmhandAuthLogic.GetSanitizedFarmhandUserId(state, 1002L, playerFree);
            Assert.Equal(string.Empty, userIdFree);

            // 3. Peón no migrado con token secreto legado (ej. GUID o clave previa)
            var playerLegacy = state.GetOrCreatePlayer(1003L, "Legacy");
            playerLegacy.OwnerToken = "secret_guid_token_compromised_12345";
            string userIdLegacy = FarmhandAuthLogic.GetSanitizedFarmhandUserId(state, 1003L, playerLegacy);
            // Invariante crítico: NUNCA se filtra el secreto legado a la lista de peones
            Assert.Equal(string.Empty, userIdLegacy);
            Assert.DoesNotContain("secret_guid_token", userIdLegacy);
        }

        [Fact]
        public void CanAccessFarmhandList_AllowsAuthorizedDeviceFromAnyIp()
        {
            var state = new ServerWorldState();
            using var devKey = DeviceKeyPair.Generate();
            string fp = devKey.GetFingerprint();
            state.AuthorizedDevices[fp] = new AuthorizedDevice
            {
                Fingerprint = fp,
                PublicKeyBase64 = devKey.GetPublicKeyBase64(),
                Alias = "mTx",
                BoundPlayerId = 555L,
                Status = DeviceStatus.Active,
                LastSeenIp = "83.44.12.34"
            };

            // mTx entra desde una IP completamente distinta (ej. 92.50.10.20)
            bool allowed = FarmhandAuthLogic.CanAccessFarmhandList(
                state,
                requireWhitelist: true,
                isLoopback: false,
                verifiedFingerprint: fp,
                out string failureReason);

            Assert.True(allowed, failureReason);
            Assert.Empty(failureReason);
        }

        [Fact]
        public void CanAccessFarmhandList_BlocksUnknownFakeOrRevokedDevice()
        {
            var state = new ServerWorldState();
            using var devKey = DeviceKeyPair.Generate();
            string fp = devKey.GetFingerprint();

            // 1. Huella desconocida
            Assert.False(FarmhandAuthLogic.CanAccessFarmhandList(state, true, false, fp, out string reason1));
            Assert.Contains("no autorizado", reason1);

            // 2. Sin identidad verificada (cadena nula o vacía)
            Assert.False(FarmhandAuthLogic.CanAccessFarmhandList(state, true, false, null, out string reason2));
            Assert.Contains("No se ha verificado", reason2);

            // 3. Dispositivo revocado
            state.AuthorizedDevices[fp] = new AuthorizedDevice
            {
                Fingerprint = fp,
                PublicKeyBase64 = devKey.GetPublicKeyBase64(),
                Alias = "RevokedTester",
                Status = DeviceStatus.Revoked
            };
            Assert.False(FarmhandAuthLogic.CanAccessFarmhandList(state, true, false, fp, out string reason3));
            Assert.Contains("revocado", reason3);
        }

        [Fact]
        public void AuthorizeSlotSelection_AllowsOwnerToSelectTheirBoundSlot()
        {
            var state = new ServerWorldState();
            using var devKey = DeviceKeyPair.Generate();
            string fp = devKey.GetFingerprint();
            long playerId = 1001L;

            state.AuthorizedDevices[fp] = new AuthorizedDevice
            {
                Fingerprint = fp,
                PublicKeyBase64 = devKey.GetPublicKeyBase64(),
                Alias = "Owner",
                BoundPlayerId = playerId,
                Status = DeviceStatus.Active
            };
            var rec = state.GetOrCreatePlayer(playerId, "Owner");
            rec.OwnerToken = fp;

            var result = FarmhandAuthLogic.AuthorizeSlotSelection(state, playerId, fp, out string msg);
            Assert.Equal(SlotSelectionResult.AllowedExisting, result);
            Assert.Contains("concedido", msg);
        }

        [Fact]
        public void AuthorizeSlotSelection_PreventsDeviceFromSelectingAnotherPlayersSlot()
        {
            var state = new ServerWorldState();

            // Dispositivo 1: Alice (dueña del slot 1001)
            using var aliceKey = DeviceKeyPair.Generate();
            string aliceFp = aliceKey.GetFingerprint();
            state.AuthorizedDevices[aliceFp] = new AuthorizedDevice
            {
                Fingerprint = aliceFp,
                PublicKeyBase64 = aliceKey.GetPublicKeyBase64(),
                Alias = "Alice",
                BoundPlayerId = 1001L,
                Status = DeviceStatus.Active
            };
            var aliceRec = state.GetOrCreatePlayer(1001L, "Alice");
            aliceRec.OwnerToken = aliceFp;

            // Dispositivo 2: Bob (autorizado en el servidor)
            using var bobKey = DeviceKeyPair.Generate();
            string bobFp = bobKey.GetFingerprint();
            state.AuthorizedDevices[bobFp] = new AuthorizedDevice
            {
                Fingerprint = bobFp,
                PublicKeyBase64 = bobKey.GetPublicKeyBase64(),
                Alias = "Bob",
                BoundPlayerId = 1002L,
                Status = DeviceStatus.Active
            };

            // Bob intenta seleccionar el personaje de Alice (1001L)
            var result = FarmhandAuthLogic.AuthorizeSlotSelection(state, 1001L, bobFp, out string msg);
            Assert.Equal(SlotSelectionResult.DeniedBelongsToAnother, result);
            Assert.Contains("pertenece a otro jugador", msg);
        }

        [Fact]
        public void AuthorizeSlotSelection_AllowsUnboundDeviceToClaimFreeSlot()
        {
            var state = new ServerWorldState();
            using var devKey = DeviceKeyPair.Generate();
            string fp = devKey.GetFingerprint();

            // Dispositivo autorizado nuevo sin personaje vinculado aún (BoundPlayerId = 0)
            state.AuthorizedDevices[fp] = new AuthorizedDevice
            {
                Fingerprint = fp,
                PublicKeyBase64 = devKey.GetPublicKeyBase64(),
                Alias = "NewPlayer",
                BoundPlayerId = 0,
                Status = DeviceStatus.Active
            };

            long freeSlotId = 2005L;
            var result = FarmhandAuthLogic.AuthorizeSlotSelection(state, freeSlotId, fp, out string msg);
            Assert.Equal(SlotSelectionResult.AllowedClaimedNew, result);
            Assert.Contains("Ranura libre asignada", msg);
        }

        [Fact]
        public void AuthorizeSlotSelection_PreventsDeviceWithExistingCharacterFromClaimingSecondSlot()
        {
            var state = new ServerWorldState();
            using var devKey = DeviceKeyPair.Generate();
            string fp = devKey.GetFingerprint();

            // Dispositivo que ya tiene vinculado el personaje 1001L
            state.AuthorizedDevices[fp] = new AuthorizedDevice
            {
                Fingerprint = fp,
                PublicKeyBase64 = devKey.GetPublicKeyBase64(),
                Alias = "SinglePlayerOnly",
                BoundPlayerId = 1001L,
                Status = DeviceStatus.Active
            };

            long freeSlotId = 2005L;
            var result = FarmhandAuthLogic.AuthorizeSlotSelection(state, freeSlotId, fp, out string msg);
            Assert.Equal(SlotSelectionResult.DeniedAlreadyHasBoundPlayer, result);
            Assert.Contains("no puedes reclamar una segunda ranura", msg);
        }

        [Fact]
        public void WireInspection_NoSecretTokensOrCleartextCodesTransmitted()
        {
            var state = new ServerWorldState();
            using var devKey = DeviceKeyPair.Generate();
            string fp = devKey.GetFingerprint();
            state.AuthorizedDevices[fp] = new AuthorizedDevice
            {
                Fingerprint = fp,
                PublicKeyBase64 = devKey.GetPublicKeyBase64(),
                Alias = "WireAuditor",
                BoundPlayerId = 1001L,
                Status = DeviceStatus.Active
            };
            var rec = state.GetOrCreatePlayer(1001L, "WireAuditor");
            rec.OwnerToken = fp;

            // 1. Simulación de carga útil de descubrimiento (Discovery Challenge)
            var challenge = new DeviceAuthChallenge(Guid.NewGuid(), BaseTime, new byte[32], "127.0.0.1:24642");
            byte[] discoveryPayload = DeviceAuthWire.PackDiscoveryChallenge(challenge);
            string discoveryText = Encoding.Latin1.GetString(discoveryPayload);

            Assert.DoesNotContain("token_", discoveryText);
            Assert.DoesNotContain("secret", discoveryText);

            // 2. Simulación de carga útil de saludo (Hail Payload)
            byte[] spki = devKey.ExportPublicKeySpki();
            byte[] sig = devKey.Sign(challenge.BuildSignBytes());
            byte[] hailPayload = DeviceAuthWire.PackHailPayload(challenge.Nonce, challenge.TimestampUtc, spki, sig);
            string hailText = Encoding.Latin1.GetString(hailPayload);

            Assert.DoesNotContain("token_", hailText);
            Assert.DoesNotContain("secret", hailText);

            // 3. Simulación de campo userID en sendAvailableFarmhands
            string farmhandUserId = FarmhandAuthLogic.GetSanitizedFarmhandUserId(state, 1001L, rec);
            Assert.Equal(fp, farmhandUserId); // Solo la huella pública SHA-256
            Assert.DoesNotContain("password", farmhandUserId);
            Assert.DoesNotContain("secret", farmhandUserId);
            Assert.DoesNotContain("token", farmhandUserId);
        }
    }
}
