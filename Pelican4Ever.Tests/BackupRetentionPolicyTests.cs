using System;
using System.Linq;
using Pelican4Ever.Core.Admin;
using Xunit;

namespace Pelican4Ever.Tests;

public class BackupRetentionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static BackupEntry Daily(int daysAgo) => new($"d{daysAgo}", BackupKind.Daily, Now.AddDays(-daysAgo));

    private static BackupEntry Checkpoint(int hoursAgo) => new($"c{hoursAgo}", BackupKind.Checkpoint, Now.AddHours(-hoursAgo));

    [Fact]
    public void KeepsTheMostRecentCheckpoints()
    {
        var entries = Enumerable.Range(1, 5).Select(Checkpoint).Append(Daily(1));
        var deleted = BackupRetentionPolicy.ToDelete(entries, keepCheckpoints: 3, keepDailyDays: 0, Now);
        Assert.Equal(new[] { "c4", "c5" }, deleted.Select(e => e.Name).OrderBy(n => n));
    }

    [Fact]
    public void DeletesDailyCopiesOlderThanTheWindow()
    {
        var entries = new[] { Daily(1), Daily(6), Daily(8), Daily(30) };
        var deleted = BackupRetentionPolicy.ToDelete(entries, keepCheckpoints: 0, keepDailyDays: 7, Now);
        Assert.Equal(new[] { "d30", "d8" }, deleted.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void NeverDeletesTheNewestCopyOfEachKind()
    {
        var entries = new[] { Daily(40), Daily(50), Checkpoint(3) };
        var deleted = BackupRetentionPolicy.ToDelete(entries, keepCheckpoints: 1, keepDailyDays: 7, Now);
        Assert.Equal(new[] { "d50" }, deleted.Select(e => e.Name));
    }

    [Fact]
    public void ZeroDisablesRotationForThatKind()
    {
        var entries = Enumerable.Range(1, 20).Select(Checkpoint).Concat(Enumerable.Range(1, 20).Select(d => Daily(d * 10)));
        Assert.Empty(BackupRetentionPolicy.ToDelete(entries, keepCheckpoints: 0, keepDailyDays: 0, Now));
    }

    [Fact]
    public void FolderNamesRoundTripAndForeignFoldersAreIgnored()
    {
        var created = new DateTimeOffset(2026, 11, 6, 2, 0, 11, 345, TimeSpan.Zero);
        string name = BackupRetentionPolicy.FolderName("Pelican4Ever_453180189", created);
        Assert.Equal("Pelican4Ever_453180189_20261106_020011_345", name);

        Assert.True(BackupRetentionPolicy.TryParseFolderName(name, "Pelican4Ever_453180189", out DateTimeOffset parsed));
        Assert.Equal(created, parsed);

        Assert.False(BackupRetentionPolicy.TryParseFolderName(name, "Otra_123", out _));
        Assert.False(BackupRetentionPolicy.TryParseFolderName(name + ".partial", "Pelican4Ever_453180189", out _));
        Assert.False(BackupRetentionPolicy.TryParseFolderName("Pelican4Ever_453180189_spike", "Pelican4Ever_453180189", out _));
    }
}
