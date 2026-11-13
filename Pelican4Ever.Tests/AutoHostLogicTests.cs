using System.Collections.Generic;
using Pelican4Ever.Core.Config;
using Pelican4Ever.Core.Server;
using Xunit;

namespace Pelican4Ever.Tests;

public class AutoHostLogicTests
{
    private static readonly HashSet<string> Saves = new() { "Pelican4Ever_452974881", "prueba1_453124756" };

    [Fact]
    public void Decide_WithoutSaveName_IsDisabled()
    {
        Assert.Equal(AutoHostDecision.Disabled, AutoHostLogic.Decide("", Saves.Contains));
        Assert.Equal(AutoHostDecision.Disabled, AutoHostLogic.Decide(null, Saves.Contains));
    }

    [Fact]
    public void Decide_ExistingSave_Loads()
    {
        Assert.Equal(AutoHostDecision.Load, AutoHostLogic.Decide("Pelican4Ever_452974881", Saves.Contains));
    }

    [Fact]
    public void Decide_MissingSave_NeverFallsBackToAnotherOne()
    {
        // Con varias partidas en Saves/, cargar «la más reciente» abriría la de pruebas como servidor
        Assert.Equal(AutoHostDecision.SaveMissing, AutoHostLogic.Decide("Pelican4Ever_000", Saves.Contains));
    }

    [Theory]
    [InlineData("../Pelican4Ever_452974881")]
    [InlineData("Saves/Pelican4Ever_452974881")]
    [InlineData("..")]
    [InlineData(" Pelican4Ever_452974881")]
    [InlineData("   ")]
    public void Decide_PathsOrPaddedNames_AreInvalid(string name)
    {
        Assert.Equal(AutoHostDecision.InvalidName, AutoHostLogic.Decide(name, _ => true));
    }

    [Theory]
    [InlineData(true, "Pelican4Ever_452974881", true)]
    [InlineData(true, "", false)]   // Sin carga automática, cerrar el proceso dejaría el servidor caído en el título
    [InlineData(false, "Pelican4Ever_452974881", false)]
    public void ShouldExitProcessOnRestart_OnlyWithAutoHost(bool exits, string save, bool expected)
    {
        Assert.Equal(expected, AutoHostLogic.ShouldExitProcessOnRestart(exits, save));
    }

    [Fact]
    public void Config_AutoHostDefaults_AreOff()
    {
        var config = new ServerConfig();

        Assert.Equal(string.Empty, config.AutoHostSaveName);
        Assert.False(config.RestartExitsProcess);
    }

    [Fact]
    public void Config_RestartExitsProcessWithoutAutoHost_IsRejected()
    {
        var config = new ServerConfig { RestartExitsProcess = true };

        Assert.Contains(config.Validate(), e => e.Contains(nameof(ServerConfig.RestartExitsProcess)));
    }

    [Fact]
    public void Config_InvalidAutoHostSaveName_IsRejected()
    {
        var config = new ServerConfig { AutoHostSaveName = "../otra" };

        Assert.Contains(config.Validate(), e => e.Contains(nameof(ServerConfig.AutoHostSaveName)));
    }

    [Fact]
    public void Config_ValidAutoHost_IsAccepted()
    {
        var config = new ServerConfig { AutoHostSaveName = "Pelican4Ever_452974881", RestartExitsProcess = true };

        Assert.Empty(config.Validate());
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 600)]
    public void PortGate_FreePort_Loads(bool available, int waited) =>
        Assert.Equal(PortGateDecision.Load, AutoHostLogic.DecidePortGate(available, waited));

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(59)]
    public void PortGate_BusyPort_WaitsWithinTheWindow(int waited) =>
        Assert.Equal(PortGateDecision.KeepWaiting, AutoHostLogic.DecidePortGate(portAvailable: false, waitedSeconds: waited));

    [Theory]
    [InlineData(60)]
    [InlineData(600)]
    public void PortGate_BusyPort_GivesUpAfterTheWindow(int waited) =>
        Assert.Equal(PortGateDecision.GiveUp, AutoHostLogic.DecidePortGate(portAvailable: false, waitedSeconds: waited));

    [Fact]
    public void PortGate_DefaultWindowIsSixtySeconds() =>
        Assert.Equal(60, AutoHostLogic.PortWaitSeconds);
}
