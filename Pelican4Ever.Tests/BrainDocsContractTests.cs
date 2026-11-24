using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class BrainDocsContractTests
    {
        private const int MaxAgentsFileBytes = 24_000;

        private static readonly string[] TranslationSuffixes = { ".en.md", ".zh-CN.md" };

        private static readonly Regex AdrFileNameRegex = new(@"^(\d{4})-.*\.md$", RegexOptions.Compiled);
        private static readonly Regex FencedCodeRegex = new(@"```[\s\S]*?```", RegexOptions.Compiled);
        private static readonly Regex InlineCodeRegex = new(@"`[^`\n]+`", RegexOptions.Compiled);
        private static readonly Regex MarkdownLinkRegex = new(@"\[[^\]]+\]\(([^)#\s]+)(?:#[^)]*)?\)", RegexOptions.Compiled);

        [Fact]
        public void AgentsEntrypoint_ExistsFitsBudgetAndDeclaresExclusionsAndWriteBack()
        {
            string root = FindRepoRoot();
            string agentsPath = Path.Combine(root, "AGENTS.md");
            string claudePath = Path.Combine(root, "CLAUDE.md");

            Assert.True(File.Exists(agentsPath), $"No se encontró {agentsPath}");
            Assert.True(File.Exists(claudePath), $"No se encontró {claudePath}");

            byte[] rawBytes = File.ReadAllBytes(agentsPath);
            Assert.True(
                rawBytes.Length < MaxAgentsFileBytes,
                $"AGENTS.md pesa {rawBytes.Length} bytes (límite de regla always-on: {MaxAgentsFileBytes}).");

            string agentsText = File.ReadAllText(agentsPath);
            Assert.False(
                agentsText.TrimStart().StartsWith("---", StringComparison.Ordinal),
                "AGENTS.md en raíz no debe llevar frontmatter YAML.");

            foreach (string required in new[]
            {
                "docs/archive/",
                "docs/verifications/",
                ".en.md",
                ".zh-CN.md",
                "host-api.md",
                "write-back",
            })
            {
                Assert.Contains(required, agentsText, StringComparison.OrdinalIgnoreCase);
            }

            string claudeText = File.ReadAllText(claudePath);
            Assert.Contains("@AGENTS.md", claudeText, StringComparison.Ordinal);
        }

        [Fact]
        public void CanonicalAdrs_HaveUniqueNumbersAndAreIndexedInAdrReadme()
        {
            string root = FindRepoRoot();
            string adrDir = Path.Combine(root, "docs", "adr");
            string readmePath = Path.Combine(adrDir, "README.md");

            Assert.True(Directory.Exists(adrDir), $"No se encontró {adrDir}");
            Assert.True(File.Exists(readmePath), $"No se encontró {readmePath}");

            string readmeText = File.ReadAllText(readmePath);
            var seenNumbers = new Dictionary<string, string>(StringComparer.Ordinal);

            string[] canonicalAdrs = Directory.GetFiles(adrDir, "*.md")
                .Where(p => !IsTranslation(Path.GetFileName(p)))
                .Where(p => AdrFileNameRegex.IsMatch(Path.GetFileName(p)))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();

            Assert.NotEmpty(canonicalAdrs);

            foreach (string adrPath in canonicalAdrs)
            {
                string fileName = Path.GetFileName(adrPath);
                string number = AdrFileNameRegex.Match(fileName).Groups[1].Value;

                Assert.False(
                    seenNumbers.TryGetValue(number, out string? existing),
                    $"Número ADR duplicado {number}: '{existing}' y '{fileName}'.");

                seenNumbers[number] = fileName;
                Assert.Contains(fileName, readmeText, StringComparison.Ordinal);

                string adrHead = string.Join('\n', File.ReadLines(adrPath).Take(20));
                foreach (string metaKey in new[] { "**Estado:**", "**Fecha:**", "**Ámbito:**" })
                {
                    Assert.Contains(metaKey, adrHead, StringComparison.Ordinal);
                }
            }
        }

        [Fact]
        public void DocsRoot_ContainsNoVerificationReportsOrArchiveFilenameCollisions()
        {
            string root = FindRepoRoot();
            string docsDir = Path.Combine(root, "docs");
            string archiveDir = Path.Combine(docsDir, "archive");

            HashSet<string> archiveNames = Directory.Exists(archiveDir)
                ? Directory.GetFiles(archiveDir, "*.md")
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string docPath in Directory.GetFiles(docsDir, "*.md"))
            {
                string fileName = Path.GetFileName(docPath);
                Assert.DoesNotContain("-verification", fileName, StringComparison.OrdinalIgnoreCase);

                if (!string.Equals(fileName, "README.md", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.False(
                        archiveNames.Contains(fileName),
                        $"docs/{fileName} colisiona en nombre con docs/archive/{fileName}.");
                }
            }
        }

        [Fact]
        public void BrainEntrypoints_HaveNoBrokenRelativeLinks()
        {
            string root = FindRepoRoot();
            var targets = new[]
            {
                Path.Combine(root, "AGENTS.md"),
                Path.Combine(root, "CLAUDE.md"),
                Path.Combine(root, "docs", "README.md"),
                Path.Combine(root, "docs", "adr", "README.md"),
            };

            foreach (string filePath in targets)
            {
                Assert.True(File.Exists(filePath), $"No se encontró {filePath}");
                string content = File.ReadAllText(filePath);
                string cleaned = InlineCodeRegex.Replace(FencedCodeRegex.Replace(content, string.Empty), "code");

                string baseDir = Path.GetDirectoryName(filePath)!;
                foreach (Match match in MarkdownLinkRegex.Matches(cleaned))
                {
                    string target = match.Groups[1].Value;
                    if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                        || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                        || target.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string resolved = Path.GetFullPath(Path.Combine(baseDir, target));
                    Assert.True(
                        File.Exists(resolved) || Directory.Exists(resolved),
                        $"Enlace relativo roto en {Path.GetRelativePath(root, filePath)}: '{target}' -> '{resolved}'.");
                }
            }
        }

        private static bool IsTranslation(string fileName) =>
            TranslationSuffixes.Any(sfx => fileName.EndsWith(sfx, StringComparison.OrdinalIgnoreCase));

        private static string FindRepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Pelican4Ever.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            return Path.GetFullPath(".");
        }
    }
}
