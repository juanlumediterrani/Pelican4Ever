using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Pelican4Ever.Core.Networking;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class ClientVersionPolicyTests
    {
        [Theory]
        [InlineData("0.5.3", 0, 5, 3)]
        [InlineData("0.5.4-android", 0, 5, 4)]
        [InlineData("1.2.10+meta", 1, 2, 10)]
        public void SemVer_TryParse_ValidVersions(string raw, int major, int minor, int patch)
        {
            Assert.True(SemVer.TryParse(raw, out SemVer parsed));
            Assert.Equal(new SemVer(major, minor, patch), parsed);
        }

        [Theory]
        [InlineData("")]
        [InlineData("0.5")]
        [InlineData("0.5.3.1")]
        [InlineData("v0.5.3")]
        [InlineData("-1.0.0")]
        public void SemVer_TryParse_RejectsInvalid(string raw)
        {
            Assert.False(SemVer.TryParse(raw, out _));
        }

        [Fact]
        public void Evaluate_AllowsOlderClientDownToMinFloor_AndNewerClientInSameMinor()
        {
            var host = new SemVer(0, 5, 5);
            var floor = new SemVer(0, 5, 4);

            Assert.True(ClientVersionPolicy.Evaluate(host, floor, new SemVer(0, 5, 4)).IsCompatible);
            Assert.True(ClientVersionPolicy.Evaluate(host, floor, new SemVer(0, 5, 5)).IsCompatible);
            Assert.True(ClientVersionPolicy.Evaluate(host, floor, new SemVer(0, 5, 6)).IsCompatible);

            ClientVersionDecision belowFloor = ClientVersionPolicy.Evaluate(host, floor, new SemVer(0, 5, 3));
            Assert.False(belowFloor.IsCompatible);
            Assert.Contains("0.5.3", belowFloor.RejectionReason);
            Assert.Contains(">= 0.5.4", belowFloor.RejectionReason);

            ClientVersionDecision wrongMinor = ClientVersionPolicy.Evaluate(host, floor, new SemVer(0, 6, 0));
            Assert.False(wrongMinor.IsCompatible);
            Assert.Contains("0.5.x", wrongMinor.RejectionReason);
        }

        [Fact]
        public void Evaluate_FailClosedWhenFloorIsOutOfSyncWithHost()
        {
            // Suelo de una serie Minor anterior: fail-closed exige host exacto (0.6.0).
            ClientVersionDecision minorMismatch = ClientVersionPolicy.Evaluate(
                hostVersion: new SemVer(0, 6, 0),
                minClientVersion: new SemVer(0, 5, 4),
                clientVersion: new SemVer(0, 5, 9));
            Assert.False(minorMismatch.IsCompatible);
            Assert.Equal(new SemVer(0, 6, 0), minorMismatch.EffectiveMinClientVersion);

            // Suelo superior al host: fail-closed normaliza al host (0.5.4).
            ClientVersionDecision aboveHost = ClientVersionPolicy.Evaluate(
                hostVersion: new SemVer(0, 5, 4),
                minClientVersion: new SemVer(0, 5, 9),
                clientVersion: new SemVer(0, 5, 4));
            Assert.True(aboveHost.IsCompatible);
            Assert.Equal(new SemVer(0, 5, 4), aboveHost.EffectiveMinClientVersion);
        }

        [Fact]
        public void ComputeNextMinVersion_DoesNotBump_ForHostOnlyAndClientUiSafeAndDocsChanges()
        {
            var calc = ClientVersionPolicy.ComputeNextMinVersion(
                currentMinVersion: new SemVer(0, 5, 4),
                targetHostVersion: new SemVer(0, 5, 5),
                changedPaths: new[]
                {
                    "Pelican4Ever/Modules/HeadlessEventGuardModule.cs",
                    "Pelican4Ever/Modules/AdminApiModule.cs",
                    "Pelican4Ever.Core/Server/HostEventGuardLogic.cs",
                    "Pelican4Ever.Core/Admin/HostApiContract.cs",
                    "Pelican4Ever/Framework/AndroidMenuCompat.cs",
                    "Pelican4Ever/Modules/ModToggleModule.cs",
                    "Pelican4Ever.Tests/HostEventGuardLogicTests.cs",
                    "Pelican4Ever/i18n/es.json",
                    "Pelican4Ever/manifest.json",
                    "docs/host-api.md",
                    "backlog.md",
                });

            Assert.False(calc.MustBump);
            Assert.Equal(new SemVer(0, 5, 4), calc.RecommendedMinVersion);
            Assert.Empty(calc.BlockingImpacts);
        }

        [Fact]
        public void ComputeNextMinVersion_Bumps_OnWorldDefinitionOrModpackChange()
        {
            var calc = ClientVersionPolicy.ComputeNextMinVersion(
                currentMinVersion: new SemVer(0, 5, 3),
                targetHostVersion: new SemVer(0, 5, 4),
                changedPaths: new[]
                {
                    "Pelican4Ever.Core/World/FarmTemplateLogic.cs",
                    "deploy/modpack/Pelican4Ever.NITVCompat/content.json",
                });

            Assert.True(calc.MustBump);
            Assert.Equal(new SemVer(0, 5, 4), calc.RecommendedMinVersion);
            Assert.All(calc.BlockingImpacts, i => Assert.Equal(CompatibilityImpactKind.WorldDefinitionChange, i.Kind));
        }

        [Fact]
        public void ComputeNextMinVersion_Bumps_OnProtocolModelOrClientRuleChange()
        {
            var modelCalc = ClientVersionPolicy.ComputeNextMinVersion(
                currentMinVersion: new SemVer(0, 5, 4),
                targetHostVersion: new SemVer(0, 5, 5),
                changedPaths: new[] { "Pelican4Ever/Models/MailOrderModels.cs" });
            Assert.True(modelCalc.MustBump);
            Assert.Equal(CompatibilityImpactKind.ProtocolOrModelChange, modelCalc.BlockingImpacts.Single().Kind);

            var protocolCalc = ClientVersionPolicy.ComputeNextMinVersion(
                currentMinVersion: new SemVer(0, 5, 4),
                targetHostVersion: new SemVer(0, 5, 5),
                changedPaths: Array.Empty<string>(),
                protocolRevisionChanged: true);
            Assert.True(protocolCalc.MustBump);
            Assert.Equal(CompatibilityImpactKind.ProtocolOrModelChange, protocolCalc.BlockingImpacts.Single().Kind);

            var ruleCalc = ClientVersionPolicy.ComputeNextMinVersion(
                currentMinVersion: new SemVer(0, 5, 4),
                targetHostVersion: new SemVer(0, 5, 5),
                changedPaths: new[] { "Pelican4Ever/Modules/EconomyModule.cs" });
            Assert.True(ruleCalc.MustBump);
            Assert.Equal(CompatibilityImpactKind.ClientRuleChange, ruleCalc.BlockingImpacts.Single().Kind);
        }

        [Fact]
        public void ComputeNextMinVersion_AlwaysBumps_OnMajorOrMinorRelease()
        {
            var calc = ClientVersionPolicy.ComputeNextMinVersion(
                currentMinVersion: new SemVer(0, 5, 4),
                targetHostVersion: new SemVer(0, 6, 0),
                changedPaths: new[] { "docs/README.md" });

            Assert.True(calc.MustBump);
            Assert.Equal(new SemVer(0, 6, 0), calc.RecommendedMinVersion);
            Assert.Contains(calc.BlockingImpacts, i => i.Kind == CompatibilityImpactKind.MajorOrMinorBump);
        }

        [Fact]
        public void ManifestVersion_SatisfiesClientVersionPolicyContract()
        {
            string repoRoot = FindRepoRoot();
            string manifestPath = Path.Combine(repoRoot, "Pelican4Ever", "manifest.json");
            Assert.True(File.Exists(manifestPath), $"No se encontró {manifestPath}");

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            string? rawVersion = doc.RootElement.GetProperty("Version").GetString();
            Assert.True(SemVer.TryParse(rawVersion, out SemVer manifestVersion), $"Versión inválida en manifest.json: {rawVersion}");

            Assert.True(
                ClientVersionPolicy.IsValidFloor(manifestVersion, ClientVersionPolicy.MinCompatibleClientVersion),
                $"MinCompatibleClientVersion ({ClientVersionPolicy.MinCompatibleClientVersion}) debe compartir Major.Minor con manifest.json ({manifestVersion}) y ser <= manifest.");
        }

        [Fact]
        public void AllHostOnlyModulesInRepo_AreClassifiedAsSafeByClientVersionPolicy()
        {
            string repoRoot = FindRepoRoot();
            string modulesDir = Path.Combine(repoRoot, "Pelican4Ever", "Modules");
            Assert.True(Directory.Exists(modulesDir), $"No se encontró {modulesDir}");

            foreach (string file in Directory.GetFiles(modulesDir, "*.cs"))
            {
                string content = File.ReadAllText(file);
                if (!content.Contains("Side => ModuleSide.Host", StringComparison.Ordinal)
                    && !content.Contains("Side => Framework.ModuleSide.Host", StringComparison.Ordinal))
                {
                    continue;
                }

                string relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                PathImpact impact = ClientVersionPolicy.EvaluatePathImpact(relative);
                Assert.False(
                    impact.RequiresBump,
                    $"El módulo solo host '{relative}' debe estar registrado como seguro en ClientVersionPolicy.HostOnlyFiles.");
            }
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Pelican4Ever", "manifest.json")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            throw new InvalidOperationException("No se encontró la raíz del repositorio Pelican4Ever.");
        }
    }
}
