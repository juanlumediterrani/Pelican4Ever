using System;
using System.Collections.Generic;
using Pelican4Ever.Core.Config;
using Pelican4Ever.Core.Networking;
using Pelican4Ever.Core.Players;
using Xunit;

namespace Pelican4Ever.Tests.Networking;

public class ExtendedConcurrencyAdmissionTests
{
    [Fact]
    public void ServerConfig_Defaults_KeepV051ConcurrencyLimits()
    {
        var cfg = new ServerConfig();

        Assert.False(cfg.ExtendedConcurrency);
        Assert.Equal(16, cfg.MaxConcurrentPlayers);
        Assert.Equal(16, cfg.EffectiveMaxConcurrentPlayers);
        Assert.Equal(16, cfg.DefaultLocationCapacity);
        Assert.Empty(cfg.LocationCapacityOverrides);
        Assert.False(cfg.EnableConcurrencyInstrumentation);
        Assert.Equal(3, cfg.FarmerDeltaBroadcastPeriod);
        Assert.False(cfg.ClientLabHeadlessRendering);
        Assert.Empty(cfg.Validate());
    }

    [Fact]
    public void ServerConfig_EffectiveMaxConcurrentPlayers_ClampsAt16WithoutOptIn_And64WithOptIn()
    {
        var disabled = new ServerConfig
        {
            ExtendedConcurrency = false,
            MaxConcurrentPlayers = 48
        };
        Assert.Equal(16, disabled.EffectiveMaxConcurrentPlayers);
        Assert.NotEmpty(disabled.Validate());

        var enabled = new ServerConfig
        {
            ExtendedConcurrency = true,
            MaxConcurrentPlayers = 48,
            FarmSlotCount = 32,
            FarmerDeltaBroadcastPeriod = 6
        };
        Assert.Equal(48, enabled.EffectiveMaxConcurrentPlayers);
        Assert.Empty(enabled.Validate());
    }

    [Fact]
    public void ServerConfig_Validate_ChecksOverridesAndBroadcastPeriod()
    {
        var badPeriod = new ServerConfig
        {
            ExtendedConcurrency = true,
            FarmerDeltaBroadcastPeriod = 4
        };
        Assert.NotEmpty(badPeriod.Validate());

        var badOverride = new ServerConfig
        {
            ExtendedConcurrency = true,
            LocationCapacityOverrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Town"] = 100
            }
        };
        Assert.NotEmpty(badOverride.Validate());

        var validOverride = new ServerConfig
        {
            ExtendedConcurrency = true,
            DefaultLocationCapacity = 16,
            FarmerDeltaBroadcastPeriod = 3,
            LocationCapacityOverrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Town"] = 24,
                ["Saloon"] = 12
            }
        };
        Assert.Empty(validOverride.Validate());
    }

    [Fact]
    public void AdmissionLogic_WithoutExtendedOptIn_ClampsAt16EvenIf64Passed()
    {
        var logic = new AdmissionLogic(maxConcurrentPlayers: 64, queueCapacity: 0, reconnectGraceMinutes: 0, extendedConcurrency: false);
        Assert.Equal(16, logic.MaxConcurrentPlayers);
        Assert.False(logic.ExtendedConcurrency);

        var now = DateTimeOffset.UtcNow;
        for (int i = 1; i <= 16; i++)
        {
            var dec = logic.TryRequestAdmission($"conn-{i}", $"dev-{i}", now);
            Assert.Equal(AdmissionOutcome.Admitted, dec.Outcome);
        }

        var rejected = logic.TryRequestAdmission("conn-17", "dev-17", now);
        Assert.Equal(AdmissionOutcome.Rejected, rejected.Outcome);
        Assert.Equal(AdmissionRejectReason.QueueFull, rejected.RejectReason);
    }

    [Fact]
    public void AdmissionLogic_WithExtendedOptIn_AdmitsUpTo64()
    {
        var logic = new AdmissionLogic(maxConcurrentPlayers: 64, queueCapacity: 8, reconnectGraceMinutes: 5, extendedConcurrency: true);
        Assert.Equal(64, logic.MaxConcurrentPlayers);
        Assert.True(logic.ExtendedConcurrency);

        var now = DateTimeOffset.UtcNow;
        for (int i = 1; i <= 64; i++)
        {
            var dec = logic.TryRequestAdmission($"conn-{i}", $"dev-{i}", now);
            Assert.Equal(AdmissionOutcome.Admitted, dec.Outcome);
        }

        var queued = logic.TryRequestAdmission("conn-65", "dev-65", now);
        Assert.Equal(AdmissionOutcome.Queued, queued.Outcome);
        Assert.Equal(1, queued.QueuePosition);
        Assert.Equal(1, queued.QueueLength);
    }

    [Fact]
    public void IdleKickLogic_RespectsExtendedOptInClamp()
    {
        var legacy = new IdleKickLogic(maxConcurrentPlayers: 64, idleKickMinutes: 15, idleKickWarningMinutes: 2, extendedConcurrency: false);
        Assert.Equal(16, legacy.MaxConcurrentPlayers);

        var extended = new IdleKickLogic(maxConcurrentPlayers: 64, idleKickMinutes: 15, idleKickWarningMinutes: 2, extendedConcurrency: true);
        Assert.Equal(64, extended.MaxConcurrentPlayers);
    }
}
