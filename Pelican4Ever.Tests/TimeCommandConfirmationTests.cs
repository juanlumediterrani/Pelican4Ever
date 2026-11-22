using System;
using System.Collections.Generic;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Time;
using Xunit;

namespace Pelican4Ever.Tests;

public class TimeCommandConfirmationTests
{
    [Theory]
    [InlineData("set 14:00 confirmar", TimeCommandParser.Kind.Set, true)]
    [InlineData("set 14:00 confirm", TimeCommandParser.Kind.Set, true)]
    [InlineData("+2 --confirmar", TimeCommandParser.Kind.Hours, true)]
    [InlineData("-30m --confirm", TimeCommandParser.Kind.Minutes, true)]
    [InlineData("reset CONFIRMAR", TimeCommandParser.Kind.Reset, true)]
    [InlineData("set 14:00", TimeCommandParser.Kind.Set, false)]
    [InlineData("+2", TimeCommandParser.Kind.Hours, false)]
    [InlineData("reset", TimeCommandParser.Kind.Reset, false)]
    public void ParseWithConfirmation_DetectsSuffixAndPreservesCommand(
        string input,
        TimeCommandParser.Kind expectedKind,
        bool expectedConfirmed)
    {
        TimeCommandParser.Parsed parsed = TimeCommandParser.ParseWithConfirmation(input, out bool confirmed);
        Assert.Equal(expectedKind, parsed.Kind);
        Assert.Equal(expectedConfirmed, confirmed);
    }

    [Fact]
    public void ConfirmationGate_RequiresConfirmOrRepeatWithinWindow()
    {
        DateTimeOffset now = new(2026, 11, 22, 22, 0, 0, TimeSpan.Zero);
        var gate = new TimeCommandConfirmationGate(() => now, TimeSpan.FromSeconds(45));
        var ctx = new TimeCommandSafetyContext(OnlineRemotePlayersCount: 2, ExternalControlledNpcs: new[] { "Leah" });
        TimeCommandParser.Parsed cmdSet14 = TimeCommandParser.ParseWithConfirmation("set 14:00", out bool c1);

        var first = gate.Evaluate(cmdSet14, c1, ctx);
        Assert.False(first.Allowed);
        Assert.True(first.Context.HasMixedPlayers);
        Assert.True(first.Context.HasExternalNpcs);

        // Otro comando distinto no queda confirmado por el aviso anterior
        TimeCommandParser.Parsed cmdPlus2 = TimeCommandParser.ParseWithConfirmation("+2", out bool c2);
        var other = gate.Evaluate(cmdPlus2, c2, ctx);
        Assert.False(other.Allowed);

        // Repetir el comando vigente (+2) dentro de la ventana lo aprueba
        now = now.AddSeconds(10);
        var repeat = gate.Evaluate(cmdPlus2, explicitConfirm: false, ctx);
        Assert.True(repeat.Allowed);
        Assert.True(repeat.ConfirmedByRepeat);

        // Un comando nuevo tras consumir confirmación vuelve a requerir aviso previo
        var afterConsume = gate.Evaluate(cmdSet14, explicitConfirm: false, ctx);
        Assert.False(afterConsume.Allowed);

        // Caducada la ventana (>45s) vuelve a pedir confirmación
        now = now.AddSeconds(46);
        var expired = gate.Evaluate(cmdSet14, explicitConfirm: false, ctx);
        Assert.False(expired.Allowed);
    }

    [Fact]
    public void ConfirmationGate_AllowsExplicitConfirmImmediately()
    {
        var gate = new TimeCommandConfirmationGate();
        var ctx = new TimeCommandSafetyContext(OnlineRemotePlayersCount: 1, ExternalControlledNpcs: new[] { "Leah" });
        TimeCommandParser.Parsed parsed = TimeCommandParser.ParseWithConfirmation("set 14:00 confirmar", out bool confirmed);

        var decision = gate.Evaluate(parsed, confirmed, ctx);
        Assert.True(decision.Allowed);
        Assert.False(decision.ConfirmedByRepeat);
    }

    [Fact]
    public void RemoteCommandCatalog_AcceptsConfirmedTimeCommands()
    {
        Assert.Null(RemoteCommandCatalog.Validate("p4e_time", new[] { "set", "14:00", "confirmar" }, out _));
        Assert.Null(RemoteCommandCatalog.Validate("p4e_time", new[] { "+2", "confirm" }, out _));
        Assert.NotNull(RemoteCommandCatalog.Validate("p4e_time", new[] { "bad", "confirmar" }, out _));
    }

    [Fact]
    public void TimeJumpHookRegistry_PassesTimesDeduplicatesUnregistersAndIsolatesErrors()
    {
        var registry = new TimeJumpHookRegistry();
        var calls = new List<(int Prev, int Next)>();
        Action<int, int> hook = (prev, next) => calls.Add((prev, next));
        Action<int, int> failing = (_, _) => throw new InvalidOperationException("boom");

        registry.Register(hook);
        registry.Register(hook);
        registry.Register(failing);
        Assert.Equal(2, registry.Count);

        Exception? captured = null;
        int succeeded = registry.InvokeAll(2200, 1400, ex => captured = ex);

        Assert.Equal(1, succeeded);
        Assert.NotNull(captured);
        Assert.Single(calls);
        Assert.Equal((2200, 1400), calls[0]);

        registry.Unregister(hook);
        Assert.Equal(1, registry.Count);
    }
}
