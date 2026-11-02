namespace Pelican4Ever.Core.Admin;

public enum SafeRolloverStage
{
    Idle,
    SavingBeforeClose,
    WaitingForPeers,
    ReadyToAdvanceDay,
    AdvancingDay,
    Completed,
    Failed
}

public enum SafeRolloverAction
{
    None,
    SaveAndBackup,
    RequestPeerClose,
    AdvanceDay
}

/// <summary>Pure, ordered state machine for the unattended rollover boundary.</summary>
public sealed class SafeRolloverWorkflow
{
    public SafeRolloverStage Stage { get; private set; } = SafeRolloverStage.Idle;

    public SafeRolloverAction Begin()
    {
        if (Stage != SafeRolloverStage.Idle)
            return SafeRolloverAction.None;

        Stage = SafeRolloverStage.SavingBeforeClose;
        return SafeRolloverAction.SaveAndBackup;
    }

    public SafeRolloverAction SaveAndBackupCompleted(bool succeeded, int connectedPeers)
    {
        if (Stage != SafeRolloverStage.SavingBeforeClose)
            return SafeRolloverAction.None;
        if (!succeeded)
        {
            Stage = SafeRolloverStage.Failed;
            return SafeRolloverAction.None;
        }

        if (connectedPeers > 0)
        {
            Stage = SafeRolloverStage.WaitingForPeers;
            return SafeRolloverAction.RequestPeerClose;
        }

        Stage = SafeRolloverStage.ReadyToAdvanceDay;
        return SafeRolloverAction.AdvanceDay;
    }

    public SafeRolloverAction PeersChanged(int connectedPeers)
    {
        if (Stage != SafeRolloverStage.WaitingForPeers || connectedPeers > 0)
            return SafeRolloverAction.None;

        Stage = SafeRolloverStage.ReadyToAdvanceDay;
        return SafeRolloverAction.AdvanceDay;
    }

    public bool MarkAdvancingDay()
    {
        if (Stage != SafeRolloverStage.ReadyToAdvanceDay)
            return false;

        Stage = SafeRolloverStage.AdvancingDay;
        return true;
    }

    public void DayStarted()
    {
        if (Stage == SafeRolloverStage.AdvancingDay)
            Stage = SafeRolloverStage.Completed;
    }

    public void Fail()
    {
        if (Stage is not SafeRolloverStage.Completed)
            Stage = SafeRolloverStage.Failed;
    }

    public void ResetAfterDayStarted()
    {
        if (Stage == SafeRolloverStage.Completed)
            Stage = SafeRolloverStage.Idle;
    }
}
