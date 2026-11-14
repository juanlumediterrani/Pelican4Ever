using Pelican4Ever.Core.Config;
using Pelican4Ever.Core.Economy;
using Xunit;

namespace Pelican4Ever.Tests;

public class EconomyRebalanceTests
{
    [Fact]
    public void SkullCavernFallCostsSameAsIndividualFloors()
    {
        double sequential = Enumerable.Range(2, 99).Sum(floor => SkullCavernDescent.Cost(floor - 1, floor, 1, 25));
        Assert.Equal(300.96, sequential, 8);
        Assert.Equal(sequential, SkullCavernDescent.Cost(1, 100, 1, 25), 8);
        Assert.Equal(9.76, SkullCavernDescent.Cost(1, 9, 1, 25), 8);
        Assert.Equal(0, SkullCavernDescent.Cost(9, 1, 1, 25));
        Assert.Equal(0, SkullCavernDescent.Cost(1, 100, 0, 0));
    }

    [Theory]
    [InlineData(-4, 3f)]
    [InlineData(-2, 4f)]
    [InlineData(-12, 4f)]
    [InlineData(-15, 4f)]
    [InlineData(-75, 2f)]
    [InlineData(-26, 5f)]
    [InlineData(-5, 1f)]
    public void CategoryPriceTargetsOnlyIntendedItems(int category, float expected)
        => Assert.Equal(expected, CategoryPriceMultiplier.ForCategory(category, 2, 5, 3, 4));

    [Fact]
    public void ClientReportsCannotEraseHostCreditsOrRollBack()
    {
        var host = new DailyGoldLedger();
        host.Add(3, 70, sharedWallet: false, hostApplied: true);
        var client = new DailyGoldLedger();
        client.Add(3, 100, sharedWallet: false, hostApplied: false);
        client.Add(3, 25, sharedWallet: true, hostApplied: false);
        host.MergeClient(client, 3);
        host.MergeClient(new DailyGoldLedger { Days = new() { [3] = new() { ClientIndividual = 1 } } }, 3);
        Assert.Equal(70, host.Days[3].HostIndividual);
        Assert.Equal(100, host.Days[3].ClientIndividual);
        Assert.Equal(25, host.Days[3].ClientShared);
    }

    [Fact]
    public void DailyGoldLedgerSeparatesDaysAndWallets()
    {
        var ledger = new DailyGoldLedger();
        ledger.Add(2, 375, sharedWallet: false, hostApplied: true);
        ledger.Add(2, 400, sharedWallet: false, hostApplied: false);
        ledger.Add(3, 1150, sharedWallet: true, hostApplied: true);
        ledger.Add(3, 400, sharedWallet: true, hostApplied: false);
        ledger.Add(3, 0, sharedWallet: true, hostApplied: true);
        ledger.Add(-1, 50, sharedWallet: false, hostApplied: true);

        Assert.Equal(2, ledger.Days.Count);
        Assert.Equal(375, ledger.Days[2].HostIndividual);
        Assert.Equal(400, ledger.Days[2].ClientIndividual);
        Assert.Equal(0, ledger.Days[2].HostShared);
        Assert.Equal(1150, ledger.Days[3].HostShared);
        Assert.Equal(400, ledger.Days[3].ClientShared);
        Assert.Equal(0, ledger.Days[3].HostIndividual);
    }

    [Fact]
    public void ClientReportsCannotInjectFutureDayOrHostFields()
    {
        var host = new DailyGoldLedger();
        host.Add(2, 750, sharedWallet: false, hostApplied: true);

        var rogue = new DailyGoldLedger
        {
            Days = new()
            {
                [-1] = new() { ClientIndividual = 99 },
                [2] = new() { ClientIndividual = 400, HostIndividual = 9999, HostShared = 9999 },
                [3] = new() { ClientIndividual = 500, ClientShared = 200 }
            }
        };
        host.MergeClient(rogue, currentDay: 2);

        Assert.Single(host.Days);
        Assert.Equal(750, host.Days[2].HostIndividual);
        Assert.Equal(0, host.Days[2].HostShared);
        Assert.Equal(400, host.Days[2].ClientIndividual);
    }

    [Fact]
    public void NewEconomyParametersRejectInvalidValues()
    {
        var config = new ServerConfig
        {
            FishingBiteTimeMultiplier = double.NaN,
            FishingMinigameDurationMultiplier = 0,
            MineStoneLadderChanceMultiplier = -1,
            MineMonsterLadderChance = 1.1,
            SkullCavernDescentBaseCost = -1,
            SkullCavernDescentDepthDivisor = double.PositiveInfinity,
            FishPriceMultiplier = float.NaN,
            MineralPriceMultiplier = 0
        };
        var errors = config.Validate();
        Assert.Equal(8, errors.Count(e => e.Contains("Multiplier") || e.Contains("Chance") || e.Contains("SkullCavern")));
    }
}
