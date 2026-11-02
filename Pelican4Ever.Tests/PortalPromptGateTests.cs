using Pelican4Ever.Core.Admin;
using Xunit;

namespace Pelican4Ever.Tests;

public class PortalPromptGateTests
{
    [Fact]
    public void PromptsOnceWhilePlayerStaysOnTheTriggerTile()
    {
        var gate = new PortalPromptGate();

        Assert.True(gate.ShouldPrompt("Farm_Portal_01"));
        // Cancelled but still standing there: every following tick must stay quiet (regression: it reopened every ~2.7 s).
        for (int i = 0; i < 50; i++)
            Assert.False(gate.ShouldPrompt("Farm_Portal_01"));
    }

    [Fact]
    public void ArmsAgainOnlyAfterThePlayerLeavesTheTile()
    {
        var gate = new PortalPromptGate();
        Assert.True(gate.ShouldPrompt("Farm_Portal_01"));
        Assert.False(gate.ShouldPrompt(null));            // stepped off
        Assert.True(gate.ShouldPrompt("Farm_Portal_01")); // walked back on: ask again
    }

    [Fact]
    public void SteppingDirectlyBetweenDoorsPromptsForTheNewDoor()
    {
        var gate = new PortalPromptGate();
        Assert.True(gate.ShouldPrompt("Farm_Portal_01"));
        Assert.True(gate.ShouldPrompt("Farm_Portal_02"));
        Assert.False(gate.ShouldPrompt("Farm_Portal_02"));
    }

    [Fact]
    public void ResetRearmsTheGate()
    {
        var gate = new PortalPromptGate();
        Assert.True(gate.ShouldPrompt("Farm_Portal_01"));
        gate.Reset();
        Assert.True(gate.ShouldPrompt("Farm_Portal_01"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoDoorNeverPrompts(string? none)
    {
        var gate = new PortalPromptGate();
        Assert.False(gate.ShouldPrompt(none));
    }
}
