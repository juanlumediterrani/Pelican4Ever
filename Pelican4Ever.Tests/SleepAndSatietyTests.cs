using Pelican4Ever.Core.Economy;
using Pelican4Ever.Core.Players;
using Xunit;

namespace Pelican4Ever.Tests;

public class SleepPolicyTests
{
    private const int Today = 40;

    [Fact]
    public void RestedToday_IsRested()
    {
        var p = new PlayerRecord { LastActiveGameDay = Today, RestedOnGameDay = Today };

        Assert.Equal(RolloverOutcome.Rested, SleepPolicy.Evaluate(p, Today, isOnline: false));
        Assert.Equal(SleepState.Rested, SleepPolicy.GetState(p, Today, isOnline: false));
    }

    [Fact]
    public void PlayedTodayWithoutBed_IsPenalized()
    {
        var p = new PlayerRecord { LastActiveGameDay = Today, RestedOnGameDay = Today - 1 };

        Assert.Equal(RolloverOutcome.Penalized, SleepPolicy.Evaluate(p, Today, isOnline: false));
    }

    [Fact]
    public void StillOnlineAtRollover_IsPenalized()
    {
        var p = new PlayerRecord { LastActiveGameDay = Today - 1 };

        Assert.Equal(RolloverOutcome.Penalized, SleepPolicy.Evaluate(p, Today, isOnline: true));
    }

    [Fact]
    public void AbsentPlayer_IsNeverPenalizedTwice()
    {
        var p = new PlayerRecord { LastActiveGameDay = Today - 3 };
        SleepPolicy.Commit(p, RolloverOutcome.Penalized);

        for (int day = Today - 2; day <= Today + 5; day++)
        {
            RolloverOutcome outcome = SleepPolicy.Evaluate(p, day, isOnline: false);
            Assert.Equal(RolloverOutcome.Untouched, outcome);
            SleepPolicy.Commit(p, outcome);
        }

        Assert.Equal(SleepState.OfflinePenalized, SleepPolicy.GetState(p, Today + 5, isOnline: false));
    }

    [Fact]
    public void RestedThenAbsent_StaysOfflineSafe()
    {
        var p = new PlayerRecord { LastActiveGameDay = Today, RestedOnGameDay = Today };
        SleepPolicy.Commit(p, SleepPolicy.Evaluate(p, Today, false));
        SleepPolicy.Commit(p, SleepPolicy.Evaluate(p, Today + 1, false));

        Assert.Equal(SleepState.OfflineSafe, SleepPolicy.GetState(p, Today + 2, isOnline: false));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(500, 50)]
    [InlineData(9_999, 999)]
    [InlineData(50_000, 1000)]
    public void MedicalFee_IsPercentWithCap(int gold, int expected)
    {
        Assert.Equal(expected, SleepPolicy.MedicalFee(gold, 0.10f, 1000));
    }

    [Fact]
    public void WakeUpEnergy_DependsOnOutcome()
    {
        Assert.Equal(270, SleepPolicy.WakeUpEnergy(RolloverOutcome.Rested, 270, 0.5f, 12));
        Assert.Equal(135, SleepPolicy.WakeUpEnergy(RolloverOutcome.Penalized, 270, 0.5f, 12));
        Assert.Equal(12, SleepPolicy.WakeUpEnergy(RolloverOutcome.Untouched, 270, 0.5f, 12));
    }
}

public class SatietyBudgetTests
{
    [Fact]
    public void DailyBudget_IsFractionOfMaxStamina()
    {
        Assert.Equal(135, SatietyBudget.DailyBudget(270, 0.5f));
    }

    [Theory]
    [InlineData(50, 0, 135, 50)]
    [InlineData(50, 100, 135, 35)]
    [InlineData(50, 135, 135, 0)]
    [InlineData(0, 0, 135, 0)]
    public void AllowedGain_IsCappedByRemainingBudget(int requested, int used, int budget, int expected)
    {
        Assert.Equal(expected, SatietyBudget.AllowedGain(requested, used, budget));
    }

    [Fact]
    public void DailyCounters_ResetOnNewDay()
    {
        var p = new PlayerRecord { SatietyGameDay = 3, SatietyUsed = 100, WellFedBuffsUsed = 2, SpaEnergyUsed = 60 };

        p.EnsureDailyCounters(4);

        Assert.Equal((4, 0, 0, 0), (p.SatietyGameDay, p.SatietyUsed, p.WellFedBuffsUsed, p.SpaEnergyUsed));
    }
}
