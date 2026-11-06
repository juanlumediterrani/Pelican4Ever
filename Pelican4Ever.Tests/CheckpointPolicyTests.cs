using System;
using Pelican4Ever.Core.Admin;
using Xunit;

namespace Pelican4Ever.Tests;

public class CheckpointPolicyTests
{
    /// <summary>Un instante en el que el checkpoint automático toca y nada lo impide.</summary>
    private static CheckpointInputs Due() => new()
    {
        IntervalMinutes = 30,
        SinceLastCheckpoint = TimeSpan.FromMinutes(31),
        UntilRollover = TimeSpan.FromHours(6),
        ActivitySinceLast = true,
        OnlineFarmhands = 1,
        CloneFarmhands = true
    };

    [Fact]
    public void RunsWhenDueWithActivityAndNothingBlocks()
    {
        CheckpointDecision d = CheckpointPolicy.Decide(Due());
        Assert.True(d.Run);
        Assert.Equal(CheckpointSkipReason.None, d.Reason);
        Assert.Equal(TimeSpan.FromMinutes(30), d.EffectiveInterval);
    }

    [Fact]
    public void IntervalZeroDisablesAutomaticButNotManual()
    {
        Assert.Equal(CheckpointSkipReason.Disabled, CheckpointPolicy.Decide(Due() with { IntervalMinutes = 0 }).Reason);
        Assert.True(CheckpointPolicy.Decide(Due() with { IntervalMinutes = 0, Manual = true }).Run);
    }

    [Fact]
    public void NotDueOrIdleSkipsQuietly()
    {
        CheckpointDecision early = CheckpointPolicy.Decide(Due() with { SinceLastCheckpoint = TimeSpan.FromMinutes(29) });
        Assert.Equal(CheckpointSkipReason.NotDue, early.Reason);
        Assert.False(early.Due);

        CheckpointDecision idle = CheckpointPolicy.Decide(Due() with { ActivitySinceLast = false, OnlineFarmhands = 0 });
        Assert.Equal(CheckpointSkipReason.NoActivity, idle.Reason);
        Assert.False(idle.Due);
    }

    [Theory]
    [InlineData(nameof(CheckpointSkipReason.SaveInProgress))]
    [InlineData(nameof(CheckpointSkipReason.RolloverActive))]
    [InlineData(nameof(CheckpointSkipReason.NearRollover))]
    [InlineData(nameof(CheckpointSkipReason.FestivalOrEvent))]
    [InlineData(nameof(CheckpointSkipReason.FarmhandsWithoutClone))]
    public void SafetyReasonsBlockEvenManualAndAreReportedAsDue(string reasonName)
    {
        var reason = Enum.Parse<CheckpointSkipReason>(reasonName);
        CheckpointInputs blocked = reason switch
        {
            CheckpointSkipReason.SaveInProgress => Due() with { SaveInProgress = true },
            CheckpointSkipReason.RolloverActive => Due() with { RolloverActive = true },
            CheckpointSkipReason.NearRollover => Due() with { UntilRollover = TimeSpan.FromMinutes(10) },
            CheckpointSkipReason.FestivalOrEvent => Due() with { FestivalOrEvent = true },
            _ => Due() with { CloneFarmhands = false }
        };

        CheckpointDecision automatic = CheckpointPolicy.Decide(blocked);
        Assert.False(automatic.Run);
        Assert.Equal(reason, automatic.Reason);
        Assert.True(automatic.Due);

        CheckpointDecision manual = CheckpointPolicy.Decide(blocked with { Manual = true });
        Assert.False(manual.Run);
        Assert.Equal(reason, manual.Reason);
    }

    [Fact]
    public void WithoutCloneOnlyRunsWhenNoFarmhandIsOnline()
    {
        Assert.True(CheckpointPolicy.Decide(Due() with { CloneFarmhands = false, OnlineFarmhands = 0 }).Run);
    }

    [Fact]
    public void JustOutsideTheRolloverWarningStillRuns()
    {
        Assert.True(CheckpointPolicy.Decide(Due() with { UntilRollover = TimeSpan.FromMinutes(10.5) }).Run);
    }

    [Fact]
    public void SlowCheckpointsDoubleTheIntervalUpToEightTimes()
    {
        Assert.Equal(TimeSpan.FromMinutes(60), CheckpointPolicy.EffectiveInterval(30, 1));
        Assert.Equal(TimeSpan.FromMinutes(240), CheckpointPolicy.EffectiveInterval(30, 3));
        Assert.Equal(TimeSpan.FromMinutes(240), CheckpointPolicy.EffectiveInterval(30, 9));
        Assert.Equal(TimeSpan.Zero, CheckpointPolicy.EffectiveInterval(0, 2));

        CheckpointDecision afterSlow = CheckpointPolicy.Decide(Due() with { SlowStreak = 1 });
        Assert.Equal(CheckpointSkipReason.NotDue, afterSlow.Reason);
        Assert.True(CheckpointPolicy.Decide(Due() with { SlowStreak = 1, SinceLastCheckpoint = TimeSpan.FromMinutes(61) }).Run);
    }

    [Fact]
    public void SlownessUsesTheLongestTickAndZeroMeansNoLimit()
    {
        Assert.True(CheckpointPolicy.IsSlow(3001, 3000));
        Assert.False(CheckpointPolicy.IsSlow(3000, 3000));
        Assert.False(CheckpointPolicy.IsSlow(60000, 0));
    }
}
