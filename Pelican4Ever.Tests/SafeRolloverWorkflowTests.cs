using Pelican4Ever.Core.Admin;
using Xunit;

namespace Pelican4Ever.Tests;

public class SafeRolloverWorkflowTests
{
    [Fact]
    public void SavesAndBacksUpBeforeRequestingPeerClosureAndWaitsForAllPeers()
    {
        var workflow = new SafeRolloverWorkflow();

        Assert.Equal(SafeRolloverAction.SaveAndBackup, workflow.Begin());
        Assert.Equal(SafeRolloverStage.SavingBeforeClose, workflow.Stage);
        Assert.Equal(SafeRolloverAction.RequestPeerClose, workflow.SaveAndBackupCompleted(true, connectedPeers: 2));
        Assert.Equal(SafeRolloverStage.WaitingForPeers, workflow.Stage);
        Assert.Equal(SafeRolloverAction.None, workflow.PeersChanged(1));
        Assert.Equal(SafeRolloverAction.AdvanceDay, workflow.PeersChanged(0));
        Assert.True(workflow.MarkAdvancingDay());
        workflow.DayStarted();
        Assert.Equal(SafeRolloverStage.Completed, workflow.Stage);
    }

    [Fact]
    public void FailedSaveOrBackupNeverClosesPeersOrAdvancesDay()
    {
        var workflow = new SafeRolloverWorkflow();
        Assert.Equal(SafeRolloverAction.SaveAndBackup, workflow.Begin());

        Assert.Equal(SafeRolloverAction.None, workflow.SaveAndBackupCompleted(false, connectedPeers: 2));
        Assert.Equal(SafeRolloverStage.Failed, workflow.Stage);
        Assert.Equal(SafeRolloverAction.None, workflow.PeersChanged(0));
        Assert.False(workflow.MarkAdvancingDay());
    }

    [Fact]
    public void DuplicateStartAndDuplicateDayAdvanceAreRejected()
    {
        var workflow = new SafeRolloverWorkflow();
        Assert.Equal(SafeRolloverAction.SaveAndBackup, workflow.Begin());
        Assert.Equal(SafeRolloverAction.None, workflow.Begin());
        Assert.Equal(SafeRolloverAction.AdvanceDay, workflow.SaveAndBackupCompleted(true, connectedPeers: 0));
        Assert.True(workflow.MarkAdvancingDay());
        Assert.False(workflow.MarkAdvancingDay());
    }
}
