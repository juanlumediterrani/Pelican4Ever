using System.Collections.Generic;
using Pelican4Ever.Core.Admin;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class BackupManifestTests
    {
        [Fact]
        public void SerializeAndValidate_RoundTripsValidManifest()
        {
            var manifest = new BackupManifest
            {
                FormatVersion = BackupManifest.CurrentFormatVersion,
                Kind = "Daily",
                SaveFolderName = "PelicanValley_123456",
                TotalGameDays = 42,
                GameDate = "Summer 14, Year 1",
                CreatedUtc = "2026-11-16T01:00:00.0000000Z",
                ModVersion = "0.4.9",
                GameVersion = "1.6.15",
                Compressed = true,
                Files = new List<BackupManifestFileEntry>
                {
                    new()
                    {
                        RelativePath = "PelicanValley_123456.gz",
                        Bytes = 2048,
                        Sha256 = new string('a', 64)
                    },
                    new()
                    {
                        RelativePath = "SaveGameInfo.gz",
                        Bytes = 512,
                        Sha256 = new string('b', 64)
                    }
                }
            };

            string json = BackupManifestLogic.Serialize(manifest);
            BackupManifest? parsed = BackupManifestLogic.Deserialize(json);

            Assert.NotNull(parsed);
            Assert.Empty(BackupManifestLogic.Validate(parsed));
            Assert.Equal("Daily", parsed!.Kind);
            Assert.Equal(2, parsed.Files.Count);
        }

        [Fact]
        public void Validate_RejectsTraversalBadHashesAndEmptyFiles()
        {
            var bad = new BackupManifest
            {
                FormatVersion = 99,
                Kind = "Unknown",
                SaveFolderName = "",
                TotalGameDays = -1,
                CreatedUtc = "not-a-date",
                Files = new List<BackupManifestFileEntry>
                {
                    new() { RelativePath = "../evil.xml", Bytes = -5, Sha256 = "xyz" }
                }
            };

            IReadOnlyList<string> errors = BackupManifestLogic.Validate(bad);
            Assert.True(errors.Count >= 6);
            Assert.False(BackupManifestLogic.IsSafeRelativePath("/abs/path"));
            Assert.False(BackupManifestLogic.IsSafeRelativePath("a\\b"));
            Assert.False(BackupManifestLogic.IsSafeRelativePath("../x"));
            Assert.True(BackupManifestLogic.IsSafeRelativePath("SaveGameInfo"));
        }
    }
}
