using Pelican4Ever.Core.Admin;
using Xunit;

namespace Pelican4Ever.Tests;

public class SafeRolloverWorkflowTests
{
    [Fact]
    public void SavesBackupBeforePeerCloseThenPersistsDisconnectedFarmhandBeforeAdvance()
    {
        var workflow = new SafeRolloverWorkflow();

        Assert.Equal(SafeRolloverAction.SaveAndBackup, workflow.Begin());
        Assert.Equal(SafeRolloverStage.SavingBeforeClose, workflow.Stage);
        Assert.Equal(SafeRolloverAction.RequestPeerClose, workflow.SaveAndBackupCompleted(true, connectedPeers: 2));
        Assert.Equal(SafeRolloverStage.WaitingForPeers, workflow.Stage);
        Assert.Equal(SafeRolloverAction.None, workflow.PeersChanged(1));
        Assert.Equal(SafeRolloverAction.SaveAndBackupAfterClose, workflow.PeersChanged(0));
        Assert.Equal(SafeRolloverStage.SavingAfterClose, workflow.Stage);
        Assert.Equal(SafeRolloverAction.AdvanceDay, workflow.SaveAndBackupAfterCloseCompleted(true));
        Assert.True(workflow.MarkAdvancingDay());
        workflow.DayStarted();
        Assert.Equal(SafeRolloverStage.Completed, workflow.Stage);
    }

    [Fact]
    public void FailedPreCloseBackupNeverClosesPeersOrAdvancesDay()
    {
        var workflow = new SafeRolloverWorkflow();
        Assert.Equal(SafeRolloverAction.SaveAndBackup, workflow.Begin());

        Assert.Equal(SafeRolloverAction.None, workflow.SaveAndBackupCompleted(false, connectedPeers: 2));
        Assert.Equal(SafeRolloverStage.Failed, workflow.Stage);
        Assert.Equal(SafeRolloverAction.None, workflow.PeersChanged(0));
        Assert.False(workflow.MarkAdvancingDay());
    }

    [Fact]
    public void FailedPostCloseSaveNeverAdvancesCalendar()
    {
        var workflow = new SafeRolloverWorkflow();
        Assert.Equal(SafeRolloverAction.SaveAndBackup, workflow.Begin());
        Assert.Equal(SafeRolloverAction.SaveAndBackupAfterClose, workflow.SaveAndBackupCompleted(true, connectedPeers: 0));

        Assert.Equal(SafeRolloverAction.None, workflow.SaveAndBackupAfterCloseCompleted(false));
        Assert.Equal(SafeRolloverStage.Failed, workflow.Stage);
        Assert.False(workflow.MarkAdvancingDay());
    }

    [Fact]
    public void DuplicateStartAndDuplicateAdvanceAreRejected()
    {
        var workflow = new SafeRolloverWorkflow();
        Assert.Equal(SafeRolloverAction.SaveAndBackup, workflow.Begin());
        Assert.Equal(SafeRolloverAction.None, workflow.Begin());
        Assert.Equal(SafeRolloverAction.SaveAndBackupAfterClose, workflow.SaveAndBackupCompleted(true, connectedPeers: 0));
        Assert.True(workflow.MarkAdvancingDay() == false);
        Assert.Equal(SafeRolloverAction.AdvanceDay, workflow.SaveAndBackupAfterCloseCompleted(true));
        Assert.True(workflow.MarkAdvancingDay());
        Assert.False(workflow.MarkAdvancingDay());
    }
}
