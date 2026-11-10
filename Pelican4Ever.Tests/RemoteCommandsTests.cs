using Pelican4Ever.Core.Admin;
using Xunit;

namespace Pelican4Ever.Tests;

public class RemoteCommandsTests
{
    private static string? Validate(string name, params string[] args) =>
        RemoteCommandCatalog.Validate(name, args, out _);

    [Theory]
    [InlineData("p4e_status")]
    [InlineData("p4e_doors")]
    [InlineData("p4e_claim_list")]
    [InlineData("p4e_checkpoint_now")]
    [InlineData("p4e_forage_wave")]
    [InlineData("P4E_STATUS")]        // sin distinguir mayúsculas
    public void AllowedCommandsWithoutArguments_AreAccepted(string name)
    {
        Assert.Null(Validate(name));
    }

    [Theory]
    [InlineData("p4e_rollover_now")]   // adelanta el día
    [InlineData("p4e_safe_restart")]   // tiene su propia ruta con avisos
    [InlineData("p4e_time")]           // desajusta el calendario real
    [InlineData("p4e_migrate_save")]
    [InlineData("p4e_give_gold")]
    [InlineData("p4e_give_item")]
    [InlineData("p4e_farm_found")]     // comandos de prueba
    [InlineData("p4e_checkpoint_spike")]
    [InlineData("p4e_render")]         // tiene su propia ruta
    [InlineData("help")]
    [InlineData("")]
    [InlineData("../p4e_status")]
    public void CommandsOutsideTheAllowlist_AreRejected(string name)
    {
        string? reason = RemoteCommandCatalog.Validate(name, Array.Empty<string>(), out var spec);

        Assert.NotNull(reason);
        Assert.Null(spec);
    }

    [Fact]
    public void Allowlist_NeverContainsTheDangerousCommands()
    {
        string[] banned = { "p4e_rollover_now", "p4e_safe_restart", "p4e_time", "p4e_migrate_save", "p4e_give_gold", "p4e_give_item" };
        Assert.DoesNotContain(RemoteCommandCatalog.Commands, c => banned.Contains(c.Name, StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(RemoteCommandCatalog.Commands, c => c.Name.StartsWith("p4e_farm_", StringComparison.Ordinal));
    }

    [Fact]
    public void ArgumentCount_IsEnforced()
    {
        Assert.NotNull(Validate("p4e_status", "extra"));
        Assert.NotNull(Validate("p4e_claim_reset"));
        Assert.Null(Validate("p4e_claim_reset", "mTx"));
        Assert.NotNull(Validate("p4e_claim_reset", "uno", "dos"));
    }

    [Theory]
    [InlineData("mTx", true)]
    [InlineData("-1375562964767351794", true)]
    [InlineData("Pelican4Ever_Farm_02", true)]
    [InlineData("a b", false)]
    [InlineData("x;y", false)]
    [InlineData("$(id)", false)]
    [InlineData("../x", false)]
    [InlineData("", false)]
    public void Arguments_AreRestrictedToASafeAlphabet(string arg, bool valid)
    {
        Assert.Equal(valid, Validate("p4e_claim_reset", arg) == null);
    }

    [Fact]
    public void ArgumentLength_IsLimited()
    {
        Assert.Null(Validate("p4e_claim_reset", new string('a', RemoteCommandCatalog.MaxArgLength)));
        Assert.NotNull(Validate("p4e_claim_reset", new string('a', RemoteCommandCatalog.MaxArgLength + 1)));
    }

    [Theory]
    [InlineData("spring", "13", "14", true)]
    [InlineData("WINTER", "1", "28", true)]
    [InlineData("autumn", "13", "14", false)]
    [InlineData("spring", "0", "14", false)]
    [InlineData("spring", "13", "29", false)]
    [InlineData("spring", "trece", "14", false)]
    public void FestivalMove_ValidatesSeasonAndDays(string season, string from, string to, bool valid)
    {
        Assert.Equal(valid, Validate("p4e_festival_move", season, from, to) == null);
    }

    [Fact]
    public void Risks_MatchWhatEachCommandDoes()
    {
        RemoteCommandRisk Risk(string name) => RemoteCommandCatalog.Find(name)!.Risk;

        Assert.Equal(RemoteCommandRisk.Read, Risk("p4e_status"));
        Assert.Equal(RemoteCommandRisk.Read, Risk("p4e_doors"));
        Assert.Equal(RemoteCommandRisk.Read, Risk("p4e_claim_list"));
        Assert.Equal(RemoteCommandRisk.Safe, Risk("p4e_checkpoint_now"));
        Assert.Equal(RemoteCommandRisk.Sensitive, Risk("p4e_claim_reset"));
        Assert.Equal(RemoteCommandRisk.Sensitive, Risk("p4e_forage_wave"));
        Assert.Equal(RemoteCommandRisk.Sensitive, Risk("p4e_festival_move"));
    }

    [Fact]
    public void CommandNames_AreUnique()
    {
        var names = RemoteCommandCatalog.Commands.Select(c => c.Name.ToLowerInvariant()).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Output_IsCapturedOnlyInsideAScope_AndOnlyForThatThread()
    {
        RemoteCommandOutput.Append("fuera de captura");
        Assert.Empty(RemoteCommandOutput.Snapshot());

        using (RemoteCommandOutput.Begin())
        {
            RemoteCommandOutput.Append("línea 1\nlínea 2\r\n\n  ");
            var otherThread = new Thread(() => RemoteCommandOutput.Append("otro hilo"));
            otherThread.Start();
            otherThread.Join();

            Assert.True(RemoteCommandOutput.IsCapturing);
            Assert.Equal(new[] { "línea 1", "línea 2" }, RemoteCommandOutput.Snapshot());
        }

        Assert.False(RemoteCommandOutput.IsCapturing);
        Assert.Empty(RemoteCommandOutput.Snapshot());
    }

    [Fact]
    public void Output_IsBoundedInLinesAndLength()
    {
        using (RemoteCommandOutput.Begin())
        {
            for (int i = 0; i < 500; i++)
                RemoteCommandOutput.Append(new string('x', 1000));

            var lines = RemoteCommandOutput.Snapshot();

            Assert.Equal(RemoteCommandCatalog.MaxOutputLines, lines.Count);
            Assert.All(lines, l => Assert.True(l.Length <= RemoteCommandCatalog.MaxOutputLineLength + 1));
        }
    }
}
