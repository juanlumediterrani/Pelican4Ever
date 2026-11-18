using Pelican4Ever.Core.Config;
using Xunit;

namespace Pelican4Ever.Tests;

public class ServerConfigTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        var config = new ServerConfig();

        Assert.Empty(config.Validate());
        Assert.Equal(new TimeSpan(6, 0, 0), config.ParsedDayStart);
        Assert.Equal(new TimeSpan(2, 0, 0), config.ParsedCalendarDayChangeTime);
        Assert.Equal(new TimeSpan(1, 50, 0), config.ParsedNightHoldStart);
        Assert.Equal(DayOfWeek.Monday, config.ParsedCalendarEpoch.DayOfWeek);
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("6am")]
    [InlineData("")]
    public void InvalidTimes_AreReported(string value)
    {
        var config = new ServerConfig { NightHoldStartTime = value };

        Assert.NotEmpty(config.Validate());
    }

    [Fact]
    public void CalendarCutoffOtherThanTwo_IsRejected()
    {
        var config = new ServerConfig { CalendarDayChangeTime = "03:00" };

        Assert.Contains(config.Validate(), e => e.Contains(nameof(ServerConfig.CalendarDayChangeTime)));
    }

    [Fact]
    public void DayStartOtherThanSix_IsRejected()
    {
        var config = new ServerConfig { DayStartTime = "05:30" };

        Assert.Contains(config.Validate(), e => e.Contains(nameof(ServerConfig.DayStartTime)));
    }

    [Fact]
    public void HoldAfter0150_IsRejected()
    {
        var config = new ServerConfig { NightHoldStartTime = "02:30" };

        Assert.Contains(config.Validate(), e => e.Contains(nameof(ServerConfig.NightHoldStartTime)));
    }

    [Fact]
    public void NonMondayEpoch_IsRejected()
    {
        var config = new ServerConfig { CalendarEpoch = "2026-11-04" };

        Assert.Contains(config.Validate(), e => e.Contains("lunes"));
    }

    [Fact]
    public void NegativeSittingOrMineCooldown_IsRejected()
    {
        var config = new ServerConfig
        {
            SittingRegenSeconds = -1,
            MineFloorCooldownMinutes = -5,
            OfflineRegenFullBarHours = -2.0f,
            TravelingCartClosingTime = 2800,
            FirstAnnexPriceMultiplier = -0.1f,
            SecondAnnexPriceMultiplier = -0.5f
        };

        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.SittingRegenSeconds)));
        Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.MineFloorCooldownMinutes)));
        Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.OfflineRegenFullBarHours)));
        Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.TravelingCartClosingTime)));
        Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.FirstAnnexPriceMultiplier)));
        Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.SecondAnnexPriceMultiplier)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(1441)]
    public void CheckpointIntervalOutOfRange_IsRejected(int minutes)
    {
        var config = new ServerConfig { CheckpointIntervalMinutes = minutes };
        Assert.Contains(config.Validate(), e => e.Contains(nameof(ServerConfig.CheckpointIntervalMinutes)));
    }

    [Fact]
    public void CheckpointsAreOffAndBackupRetentionIsFourteenDaysByDefault()
    {
        var config = new ServerConfig();
        Assert.Equal(0, config.CheckpointIntervalMinutes);
        Assert.False(config.CheckpointDebugMode);
        Assert.Equal(14, config.BackupRetentionDays);
        Assert.Equal(120, config.HangThresholdSeconds);
        Assert.Contains("1.6.15", config.VerifiedGameVersions);
        Assert.Equal("4.1.0", config.MinimumSmapiVersion);
        Assert.False(config.AllowUnverifiedGameVersion);
        Assert.Empty(config.LockdownOverride);
        Assert.True(config.AutoResolveHostEvents);
        Assert.Equal(5, config.HostEventAutoResolveDelaySeconds);
        Assert.Empty(config.Validate());

        var negative = new ServerConfig { CheckpointRetention = -1, BackupRetentionDays = -1, CheckpointMaxDurationMs = -1, HangThresholdSeconds = -1, HostEventAutoResolveDelaySeconds = 0 };
        var errors = negative.Validate();
        Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.CheckpointRetention)));
        Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.BackupRetentionDays)));
        Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.CheckpointMaxDurationMs)));
        Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.HangThresholdSeconds)));
        Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.HostEventAutoResolveDelaySeconds)));
    }

    [Fact]
    public void ModulesAreEnabledUnlessExplicitlyDisabled()
    {
        var config = new ServerConfig();
        config.Modules["Festivals"] = false;

        Assert.True(config.IsModuleEnabled("Persistence"));
        Assert.False(config.IsModuleEnabled("Festivals"));
    }

    [Fact]
    public void PairingWhitelistAndPublicHubProtection_HaveExpectedDefaultsAndValidation()
    {
        var config = new ServerConfig();
        Assert.True(config.RequirePairingWhitelist);
        Assert.Equal(72, config.PairingInviteTtlHours);
        Assert.False(config.ProtectPublicHubs);
        Assert.Empty(config.Validate());

        var invalidTtl = new ServerConfig { PairingInviteTtlHours = 0 };
        Assert.Contains(invalidTtl.Validate(), e => e.Contains(nameof(ServerConfig.PairingInviteTtlHours)));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(16, true)]
    [InlineData(32, true)]
    [InlineData(64, true)]
    [InlineData(0, false)]
    [InlineData(17, false)]
    [InlineData(20, false)]
    [InlineData(48, false)]
    [InlineData(128, false)]
    public void FarmSlotCount_Allows1To16Or32Or64(int count, bool expectedValid)
    {
        var config = new ServerConfig { FarmSlotCount = count };
        var errors = config.Validate();
        if (expectedValid)
            Assert.DoesNotContain(errors, e => e.Contains(nameof(ServerConfig.FarmSlotCount)));
        else
            Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.FarmSlotCount)));
    }

    [Fact]
    public void ResidenceScalingAndAdmissionDefaults_AreValid()
    {
        var config = new ServerConfig();
        Assert.Equal(16, config.MaxConcurrentPlayers);
        Assert.Equal(16, config.AdmissionQueueCapacity);
        Assert.Equal(5, config.ReconnectGraceMinutes);
        Assert.Equal(30, config.IdleKickMinutes);
        Assert.Equal(2, config.IdleKickWarningMinutes);
        Assert.Empty(config.Validate());
    }

    [Fact]
    public void AdmissionAndIdleKickBounds_AreValidated()
    {
        var invalid = new ServerConfig
        {
            MaxConcurrentPlayers = 17,
            AdmissionQueueCapacity = 33,
            ReconnectGraceMinutes = 31,
            IdleKickMinutes = 10,
            IdleKickWarningMinutes = 10
        };
        var errors = invalid.Validate();
        Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.MaxConcurrentPlayers)));
        Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.AdmissionQueueCapacity)));
        Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.ReconnectGraceMinutes)));
        Assert.Contains(errors, e => e.Contains(nameof(ServerConfig.IdleKickWarningMinutes)));

        var idleDisabled = new ServerConfig { IdleKickMinutes = 0, IdleKickWarningMinutes = 2 };
        Assert.Empty(idleDisabled.Validate());
    }
}
