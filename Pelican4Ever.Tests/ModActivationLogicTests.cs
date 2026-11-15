using System;
using Pelican4Ever.Core.Config;
using Pelican4Ever.Core.Security;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class ModActivationLogicTests
    {
        [Fact]
        public void ServerConfig_EnabledDefaultsToTrue()
        {
            var config = new ServerConfig();
            Assert.True(config.Enabled);
        }

        [Theory]
        [InlineData(true, null, true)]
        [InlineData(false, null, false)]
        [InlineData(false, "", false)]
        [InlineData(false, "MiGranjaAutoHost", true)]
        [InlineData(true, "MiGranjaAutoHost", true)]
        public void IsEffectivelyEnabled_AlwaysActiveInAutoHostMode(bool configEnabled, string? autoHostSaveName, bool expected)
        {
            Assert.Equal(expected, ModActivationLogic.IsEffectivelyEnabled(configEnabled, autoHostSaveName));
        }

        [Theory]
        [InlineData(true, null, false, SaveLoadPromptDecision.PromptToDisableBeforeLoad)]
        [InlineData(false, null, false, SaveLoadPromptDecision.ProceedNormally)]
        [InlineData(true, "", false, SaveLoadPromptDecision.PromptToDisableBeforeLoad)]
        [InlineData(true, "ServidorAuto", false, SaveLoadPromptDecision.ProceedNormally)]
        [InlineData(true, null, true, SaveLoadPromptDecision.ProceedNormally)]
        [InlineData(false, null, true, SaveLoadPromptDecision.ProceedNormally)]
        public void DecideOnSaveLoad_PromptsOnlyForLocalSavesWhenEnabledWithoutAutoHost(
            bool isModEnabled,
            string? autoHostSaveName,
            bool isFarmhandSlot,
            SaveLoadPromptDecision expected)
        {
            SaveLoadPromptDecision result = ModActivationLogic.DecideOnSaveLoad(
                isModEnabled,
                autoHostSaveName,
                isFarmhandSlot);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(false, true, ServerConnectPromptDecision.PromptToEnableBeforeConnect)]
        [InlineData(true, true, ServerConnectPromptDecision.ProceedNormally)]
        [InlineData(false, false, ServerConnectPromptDecision.ProceedNormally)]
        [InlineData(true, false, ServerConnectPromptDecision.ProceedNormally)]
        public void DecideOnServerConnect_PromptsOnlyWhenDisabledAndServerRunsPelican(
            bool isModEnabled,
            bool serverHasPelican,
            ServerConnectPromptDecision expected)
        {
            ServerConnectPromptDecision result = ModActivationLogic.DecideOnServerConnect(
                isModEnabled,
                serverHasPelican);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void HasPelicanDiscoveryChallenge_RejectsNullEmptyWrongLengthAndGarbage()
        {
            Assert.False(ModActivationLogic.HasPelicanDiscoveryChallenge(null));
            Assert.False(ModActivationLogic.HasPelicanDiscoveryChallenge(Array.Empty<byte>()));
            Assert.False(ModActivationLogic.HasPelicanDiscoveryChallenge(new byte[10]));
            Assert.False(ModActivationLogic.HasPelicanDiscoveryChallenge(new byte[DeviceAuthWire.DiscoveryChallengePayloadLength + 1]));

            var garbage = new byte[DeviceAuthWire.DiscoveryChallengePayloadLength];
            Array.Fill(garbage, (byte)0x55);
            Assert.False(ModActivationLogic.HasPelicanDiscoveryChallenge(garbage));
        }

        [Fact]
        public void HasPelicanDiscoveryChallenge_AcceptsPackedPelicanChallenge()
        {
            var manager = new ServerChallengeManager(Guid.NewGuid());
            DeviceAuthChallenge challenge = manager.Issue("127.0.0.1", DateTimeOffset.UtcNow, string.Empty);
            byte[] payload = DeviceAuthWire.PackDiscoveryChallenge(challenge);

            Assert.Equal(DeviceAuthWire.DiscoveryChallengePayloadLength, payload.Length);
            Assert.True(ModActivationLogic.HasPelicanDiscoveryChallenge(payload));
        }
    }
}
