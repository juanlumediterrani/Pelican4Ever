using System;
using System.Collections.Generic;
using Pelican4Ever.Core.Admin;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class PatchHealthPolicyTests
    {
        private static readonly string[] DefaultVerified = { "1.6.15" };

        [Fact]
        public void Decide_ReturnsOk_WhenAllPatchesAppliedAndVersionsVerified()
        {
            var patches = new[]
            {
                new PatchRegistrationEntry("GameServer.checkFarmhandRequest", "ClientSecurity", PatchCriticality.Security, PatchRegistrationStatus.Applied),
                new PatchRegistrationEntry("Game1.UpdateGameClock", "RealTimeClock", PatchCriticality.Integrity, PatchRegistrationStatus.Applied),
                new PatchRegistrationEntry("NITV.OnWarp", "FarmSlot", PatchCriticality.Experience, PatchRegistrationStatus.SkippedOptional)
            };

            PatchHealthDecision decision = PatchHealthPolicy.Decide(
                patches,
                gameVersion: "1.6.15",
                verifiedGameVersions: DefaultVerified,
                allowUnverifiedGameVersion: false,
                smapiVersion: "4.5.2",
                minimumSmapiVersion: "4.1.0");

            Assert.Equal(HostHealthStatus.Ok, decision.Status);
            Assert.False(decision.IsLockdown);
            Assert.Empty(decision.LockdownReasons);
            Assert.Empty(decision.FailedPatches);
            Assert.Equal(3, decision.TotalPatches);
        }

        [Theory]
        [InlineData(PatchCriticality.Security)]
        [InlineData(PatchCriticality.Integrity)]
        public void Decide_ReturnsLockdown_WhenSecurityOrIntegrityPatchFails(PatchCriticality criticality)
        {
            var patches = new[]
            {
                new PatchRegistrationEntry("CriticalPatch", "TestModule", criticality, PatchRegistrationStatus.Failed, "Method not found")
            };

            PatchHealthDecision decision = PatchHealthPolicy.Decide(
                patches,
                gameVersion: "1.6.15",
                verifiedGameVersions: DefaultVerified,
                allowUnverifiedGameVersion: false,
                smapiVersion: "4.5.2",
                minimumSmapiVersion: "4.1.0");

            Assert.Equal(HostHealthStatus.Lockdown, decision.Status);
            Assert.True(decision.IsLockdown);
            Assert.Contains($"PatchFailed:{criticality}:CriticalPatch", decision.LockdownReasons);
            Assert.Single(decision.FailedPatches);
        }

        [Fact]
        public void Decide_ReturnsDegraded_WhenOnlyExperiencePatchFails()
        {
            var patches = new[]
            {
                new PatchRegistrationEntry("GameServer.checkFarmhandRequest", "ClientSecurity", PatchCriticality.Security, PatchRegistrationStatus.Applied),
                new PatchRegistrationEntry("AgentCosmetics.draw", "AgentCosmetics", PatchCriticality.Experience, PatchRegistrationStatus.Failed, "Missing target")
            };

            PatchHealthDecision decision = PatchHealthPolicy.Decide(
                patches,
                gameVersion: "1.6.15",
                verifiedGameVersions: DefaultVerified,
                allowUnverifiedGameVersion: false,
                smapiVersion: "4.5.2",
                minimumSmapiVersion: "4.1.0");

            Assert.Equal(HostHealthStatus.Degraded, decision.Status);
            Assert.False(decision.IsLockdown);
            Assert.Empty(decision.LockdownReasons);
            Assert.Single(decision.FailedPatches);
        }

        [Fact]
        public void Decide_LockdownOverride_OnlyLiftsNamedPatches()
        {
            var patches = new[]
            {
                new PatchRegistrationEntry("PatchA", "ModA", PatchCriticality.Security, PatchRegistrationStatus.Failed, "err"),
                new PatchRegistrationEntry("PatchB", "ModB", PatchCriticality.Integrity, PatchRegistrationStatus.Failed, "err")
            };

            PatchHealthDecision partial = PatchHealthPolicy.Decide(
                patches,
                gameVersion: "1.6.15",
                verifiedGameVersions: DefaultVerified,
                allowUnverifiedGameVersion: false,
                smapiVersion: "4.5.2",
                minimumSmapiVersion: "4.1.0",
                lockdownOverride: new[] { "PatchA" });

            Assert.True(partial.IsLockdown);
            Assert.Single(partial.OverriddenPatchNames, "PatchA");
            Assert.Single(partial.LockdownReasons, "PatchFailed:Integrity:PatchB");

            PatchHealthDecision full = PatchHealthPolicy.Decide(
                patches,
                gameVersion: "1.6.15",
                verifiedGameVersions: DefaultVerified,
                allowUnverifiedGameVersion: false,
                smapiVersion: "4.5.2",
                minimumSmapiVersion: "4.1.0",
                lockdownOverride: new[] { "patcha", "PATCHB" });

            Assert.Equal(HostHealthStatus.Degraded, full.Status);
            Assert.False(full.IsLockdown);
            Assert.Equal(2, full.OverriddenPatchNames.Count);
        }

        [Fact]
        public void Decide_ReturnsLockdown_OnUnverifiedGameVersion_UnlessAllowed()
        {
            PatchHealthDecision blocked = PatchHealthPolicy.Decide(
                Array.Empty<PatchRegistrationEntry>(),
                gameVersion: "1.6.16",
                verifiedGameVersions: DefaultVerified,
                allowUnverifiedGameVersion: false,
                smapiVersion: "4.5.2",
                minimumSmapiVersion: "4.1.0");

            Assert.True(blocked.IsLockdown);
            Assert.Contains("UnverifiedGameVersion:1.6.16", blocked.LockdownReasons);

            PatchHealthDecision allowed = PatchHealthPolicy.Decide(
                Array.Empty<PatchRegistrationEntry>(),
                gameVersion: "1.6.16",
                verifiedGameVersions: DefaultVerified,
                allowUnverifiedGameVersion: true,
                smapiVersion: "4.5.2",
                minimumSmapiVersion: "4.1.0");

            Assert.Equal(HostHealthStatus.Ok, allowed.Status);
        }

        [Fact]
        public void Decide_AcceptsBuildSuffixForSamePatchVersion_AndChecksMinimumSmapi()
        {
            PatchHealthDecision withBuild = PatchHealthPolicy.Decide(
                Array.Empty<PatchRegistrationEntry>(),
                gameVersion: "1.6.15.24356",
                verifiedGameVersions: DefaultVerified,
                allowUnverifiedGameVersion: false,
                smapiVersion: "4.1.0",
                minimumSmapiVersion: "4.1.0");

            Assert.Equal(HostHealthStatus.Ok, withBuild.Status);

            PatchHealthDecision oldSmapi = PatchHealthPolicy.Decide(
                Array.Empty<PatchRegistrationEntry>(),
                gameVersion: "1.6.15",
                verifiedGameVersions: DefaultVerified,
                allowUnverifiedGameVersion: false,
                smapiVersion: "4.0.9",
                minimumSmapiVersion: "4.1.0");

            Assert.True(oldSmapi.IsLockdown);
            Assert.Contains("UnverifiedSmapiVersion:4.0.9", oldSmapi.LockdownReasons);
        }

        [Fact]
        public void Decide_ReturnsLockdown_WhenModuleRegistrationThrows()
        {
            PatchHealthDecision decision = PatchHealthPolicy.Decide(
                Array.Empty<PatchRegistrationEntry>(),
                gameVersion: "1.6.15",
                verifiedGameVersions: DefaultVerified,
                allowUnverifiedGameVersion: false,
                smapiVersion: "4.5.2",
                minimumSmapiVersion: "4.1.0",
                moduleRegistrationFailures: new[] { "ClientSecurity" });

            Assert.True(decision.IsLockdown);
            Assert.Contains("ModuleRegisterFailed:ClientSecurity", decision.LockdownReasons);
        }
    }
}
