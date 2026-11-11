using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.Security;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests.Security
{
    public class DeviceStorageAndRegistryTests : IDisposable
    {
        private readonly string tempDir;
        private static readonly DateTimeOffset BaseTime = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);

        public DeviceStorageAndRegistryTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "p4e_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
                // Limpieza de directorio temporal
            }
        }

        [Fact]
        public void ClientKey_IsStableAcrossRestartsAndReloads()
        {
            string keyPath = Path.Combine(tempDir, "device_key.json");

            // 1. Primer arranque: genera y guarda la clave
            using var originalKey = DeviceKeyStorage.LoadOrGenerate(keyPath, out bool isNew);
            Assert.True(isNew);
            Assert.True(File.Exists(keyPath));
            string fpOriginal = originalKey.GetFingerprint();
            string pubOriginal = originalKey.GetPublicKeyBase64();

            // 2. Segundo arranque simulado: carga la clave existente
            using var reloadedKey = DeviceKeyStorage.LoadOrGenerate(keyPath, out bool isNewSecond);
            Assert.False(isNewSecond);
            Assert.Equal(fpOriginal, reloadedKey.GetFingerprint());
            Assert.Equal(pubOriginal, reloadedKey.GetPublicKeyBase64());

            // 3. Verificar que la firma generada por la clave recargada es válida para la clave pública original
            byte[] testData = new byte[] { 10, 20, 30, 40 };
            byte[] signature = reloadedKey.Sign(testData);
            Assert.True(DeviceSignatureVerifier.VerifySignature(originalKey.ExportPublicKeySpki(), testData, signature));
        }

        [Fact]
        public void ClientKey_CorruptedFile_ThrowsAndDoesNotGrantSilentAccess()
        {
            string keyPath = Path.Combine(tempDir, "corrupted_key.json");

            // Caso A: JSON malformado
            File.WriteAllText(keyPath, "{ invalid json content ]");
            Assert.Throws<InvalidDataException>(() => DeviceKeyStorage.Load(keyPath));

            // Caso B: Base64 no válido
            File.WriteAllText(keyPath, @"{ ""SchemaVersion"": 2, ""Pkcs8Base64"": ""not_base64!!!"" }");
            Assert.Throws<InvalidDataException>(() => DeviceKeyStorage.Load(keyPath));

            // Caso C: Huella alterada no coincide con la clave privada real
            using var realKey = DeviceKeyPair.Generate();
            var tamperedModel = new DeviceKeyFile
            {
                SchemaVersion = 2,
                Pkcs8Base64 = Convert.ToBase64String(realKey.ExportPkcs8PrivateKey()),
                Fingerprint = "0000000000000000000000000000000000000000000000000000000000000000",
                PublicKeyBase64 = realKey.GetPublicKeyBase64()
            };
            File.WriteAllText(keyPath, System.Text.Json.JsonSerializer.Serialize(tamperedModel));
            var ex = Assert.Throws<InvalidDataException>(() => DeviceKeyStorage.Load(keyPath));
            Assert.Contains("integridad", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ClientKey_InterruptedWrite_RecoversFromOrphanTmp()
        {
            string keyPath = Path.Combine(tempDir, "device_key.json");
            string tmpPath = keyPath + ".tmp";

            // Simular corte de energía o proceso antes del File.Move: existe .tmp íntegro pero no el destino
            using var key = DeviceKeyPair.Generate();
            DeviceKeyStorage.SaveAtomic(tmpPath, key);
            Assert.True(File.Exists(tmpPath));
            Assert.False(File.Exists(keyPath));

            // LoadOrGenerate debe recuperar el .tmp automáticamente
            using var recovered = DeviceKeyStorage.LoadOrGenerate(keyPath, out bool isNew);
            Assert.False(isNew);
            Assert.True(File.Exists(keyPath));
            Assert.Equal(key.GetFingerprint(), recovered.GetFingerprint());
        }

        [Fact]
        public void HostRegistry_NeverStoresOrReceivesPrivateKeys()
        {
            // Verificación por reflexión de que ni AuthorizedDevice ni PairingSidecarFile poseen propiedades de clave privada
            var deviceProperties = typeof(AuthorizedDevice).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var prop in deviceProperties)
            {
                Assert.DoesNotContain("private", prop.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("secret", prop.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("pkcs8", prop.Name, StringComparison.OrdinalIgnoreCase);
            }

            var sidecarProperties = typeof(PairingSidecarFile).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var prop in sidecarProperties)
            {
                Assert.DoesNotContain("private", prop.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("secret", prop.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("pkcs8", prop.Name, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void HostRegistry_RegisterDevice_ValidatesKeyAndFingerprint()
        {
            var state = new ServerWorldState();
            using var clientKey = DeviceKeyPair.Generate();
            string pubBase64 = clientKey.GetPublicKeyBase64();
            string expectedFp = clientKey.GetFingerprint();

            var device = ServerDeviceRegistry.RegisterOrUpdateDevice(
                state, pubBase64, "mTx", boundPlayerId: 1001L, ip: "83.44.12.34", BaseTime);

            Assert.NotNull(device);
            Assert.Equal(expectedFp, device.Fingerprint);
            Assert.Equal("mTx", device.Alias);
            Assert.Equal(1001L, device.BoundPlayerId);
            Assert.Equal(DeviceStatus.Active, device.Status);
            Assert.Equal("83.44.12.34", device.LastSeenIp);

            // Se vinculó también al PlayerRecord correspondiente
            Assert.True(state.Players.ContainsKey(1001L));
            Assert.Equal(expectedFp, state.Players[1001L].OwnerToken);
        }

        [Fact]
        public void HostRegistry_RegisterDevice_RejectsInvalidPublicKey()
        {
            var state = new ServerWorldState();

            // Clave pública vacía
            Assert.Throws<ArgumentException>(() =>
                ServerDeviceRegistry.RegisterOrUpdateDevice(state, "", "mTx"));

            // Clave pública con Base64 inválido
            Assert.Throws<ArgumentException>(() =>
                ServerDeviceRegistry.RegisterOrUpdateDevice(state, "not_valid_base64!!!", "mTx"));
        }

        [Fact]
        public void HostRegistry_PreventsDuplicates_AndUpdatesExistingDevice()
        {
            var state = new ServerWorldState();
            using var clientKey = DeviceKeyPair.Generate();
            string pubBase64 = clientKey.GetPublicKeyBase64();
            string fp = clientKey.GetFingerprint();

            // Alta inicial
            ServerDeviceRegistry.RegisterOrUpdateDevice(state, pubBase64, "mTx", boundPlayerId: 1001L, ip: "83.44.12.34", BaseTime);
            Assert.Single(state.AuthorizedDevices);

            // Segunda llamada con la misma clave pero distinta IP y alias
            ServerDeviceRegistry.RegisterOrUpdateDevice(state, pubBase64, "mTx_Updated", boundPlayerId: 1001L, ip: "83.44.12.99", BaseTime.AddHours(1));

            // No debe haber duplicados en el diccionario
            Assert.Single(state.AuthorizedDevices);
            var updated = state.AuthorizedDevices[fp];
            Assert.Equal("mTx_Updated", updated.Alias);
            Assert.Equal("83.44.12.99", updated.LastSeenIp);
        }

        [Fact]
        public void HostRegistry_RevokeAndQueryStatus()
        {
            var state = new ServerWorldState();
            using var clientKey = DeviceKeyPair.Generate();
            string fp = clientKey.GetFingerprint();

            ServerDeviceRegistry.RegisterOrUpdateDevice(state, clientKey.GetPublicKeyBase64(), "mTx", 1001L);

            // Activo inicialmente
            Assert.True(ServerDeviceRegistry.IsDeviceAuthorized(state, fp, out var activeDevice));
            Assert.Equal(DeviceStatus.Active, activeDevice?.Status);

            // Revocación
            Assert.True(ServerDeviceRegistry.RevokeDevice(state, fp, out var revokedDevice));
            Assert.Equal(DeviceStatus.Revoked, revokedDevice?.Status);

            // Ya no figura como autorizado
            Assert.False(ServerDeviceRegistry.IsDeviceAuthorized(state, fp, out _));
        }

        [Fact]
        public void SidecarPersistence_SavesAndLoadsAtomically_WithSchemaV2()
        {
            string sidecarPath = Path.Combine(tempDir, "pelican4ever_pairing.json");
            var state = new ServerWorldState();
            using var key1 = DeviceKeyPair.Generate();
            using var key2 = DeviceKeyPair.Generate();

            ServerDeviceRegistry.RegisterOrUpdateDevice(state, key1.GetPublicKeyBase64(), "Alice", 1001L);
            ServerDeviceRegistry.RegisterOrUpdateDevice(state, key2.GetPublicKeyBase64(), "Bob", 1002L);

            ClientPairingLogic.CreateInvite(state, "Charlie", "83.44.12.50", TimeSpan.FromHours(72), BaseTime, "ABC123");

            // Guardar atómicamente
            SidecarPersistence.SaveAtomic(sidecarPath, state);
            Assert.True(File.Exists(sidecarPath));
            Assert.False(File.Exists(sidecarPath + ".tmp"));

            // Inspeccionar JSON bruto: debe contener SchemaVersion = 2 y ninguna clave privada
            string rawJson = File.ReadAllText(sidecarPath);
            Assert.Contains("\"SchemaVersion\": 2", rawJson);
            Assert.Contains(key1.GetFingerprint(), rawJson);
            Assert.Contains(key2.GetFingerprint(), rawJson);
            Assert.DoesNotContain("Pkcs8", rawJson);
            Assert.DoesNotContain("private", rawJson, StringComparison.OrdinalIgnoreCase);

            // Cargar en un nuevo estado
            var loadedState = new ServerWorldState();
            bool loaded = SidecarPersistence.LoadAndMerge(sidecarPath, loadedState, out string? error);

            Assert.True(loaded, error);
            Assert.Equal(2, loadedState.AuthorizedDevices.Count);
            Assert.True(ServerDeviceRegistry.IsDeviceAuthorized(loadedState, key1.GetFingerprint(), out _));
            Assert.True(ServerDeviceRegistry.IsDeviceAuthorized(loadedState, key2.GetFingerprint(), out _));
            Assert.Single(loadedState.PairingInvites);
            Assert.Equal("ABC123", loadedState.PairingInvites[0].Code);
        }

        [Fact]
        public void SidecarPersistence_InterruptedWrite_RecoversFromTmp()
        {
            string sidecarPath = Path.Combine(tempDir, "pelican4ever_pairing.json");
            string tmpPath = sidecarPath + ".tmp";

            var state = new ServerWorldState();
            using var key = DeviceKeyPair.Generate();
            ServerDeviceRegistry.RegisterOrUpdateDevice(state, key.GetPublicKeyBase64(), "Alice", 1001L);

            // Simular corte tras escribir .tmp pero antes de renombrar
            SidecarPersistence.SaveAtomic(tmpPath, state);
            Assert.True(File.Exists(tmpPath));
            Assert.False(File.Exists(sidecarPath));

            // Cargar debe recuperar desde .tmp
            var loadedState = new ServerWorldState();
            bool loaded = SidecarPersistence.LoadAndMerge(sidecarPath, loadedState, out string? error);

            Assert.True(loaded, error);
            Assert.True(File.Exists(sidecarPath));
            Assert.Single(loadedState.AuthorizedDevices);
            Assert.True(ServerDeviceRegistry.IsDeviceAuthorized(loadedState, key.GetFingerprint(), out _));
        }

        [Fact]
        public void SidecarPersistence_CorruptedFile_ReturnsErrorWithoutCrashing()
        {
            string sidecarPath = Path.Combine(tempDir, "corrupted_sidecar.json");
            File.WriteAllText(sidecarPath, "{ malformed: json !");

            var state = new ServerWorldState();
            bool loaded = SidecarPersistence.LoadAndMerge(sidecarPath, state, out string? error);

            Assert.False(loaded);
            Assert.NotNull(error);
            Assert.Empty(state.AuthorizedDevices);
        }

        [Fact]
        public void ServerWorldState_SchemaV5_NormalizesAndMigratesAutomatically()
        {
            var state = new ServerWorldState
            {
                SchemaVersion = 4
            };

            // Cliente legacy con token que es una huella digital
            using var key = DeviceKeyPair.Generate();
            string fp = key.GetFingerprint();
            state.AuthorizedClients[fp] = new AuthorizedClient
            {
                ClientToken = fp,
                Alias = "LegacyPlayer",
                BoundPlayerId = 5005L,
                PairedAtUtc = BaseTime
            };

            // Normalización
            bool changed = state.Migrate();
            Assert.True(changed);
            Assert.Equal(5, state.SchemaVersion);
            Assert.Single(state.AuthorizedDevices);
            Assert.True(state.AuthorizedDevices.ContainsKey(fp));
            Assert.Equal("LegacyPlayer", state.AuthorizedDevices[fp].Alias);
            Assert.Equal(5005L, state.AuthorizedDevices[fp].BoundPlayerId);
        }
    }
}
