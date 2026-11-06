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
    public void EarlyRiser_InGraceWindow_IsNotPenalized()
    {
        // Conectado a las 01:40 con cambio de fecha a las 02:00 (ventana de gracia de 60 min)
        DateTime now = new DateTime(2026, 11, 2, 1, 40, 0);
        TimeSpan dayStart = new TimeSpan(2, 0, 0);
        var p = new PlayerRecord
        {
            LastActiveGameDay = Today,
            CurrentSessionStartUtc = DateTimeOffset.UtcNow.AddMinutes(-20),
            ActiveDayPriorToGrace = Today - 1 // Sin sesión previa en Today
        };

        bool isEarly = SleepPolicy.IsEarlyRiser(p, Today, now, dayStart, 60);
        Assert.True(isEarly);

        var outcome = SleepPolicy.Evaluate(p, Today, isOnline: true, isEarlyRiser: isEarly);
        Assert.Equal(RolloverOutcome.Rested, outcome);
    }

    [Fact]
    public void NightOwl_ConnectedHoursAgo_IsPenalized()
    {
        // Conectado a la 01:00 AM (hace más de 60 min)
        DateTime now = new DateTime(2026, 11, 2, 5, 40, 0);
        TimeSpan dayStart = new TimeSpan(6, 0, 0);
        var p = new PlayerRecord
        {
            LastActiveGameDay = Today,
            CurrentSessionStartUtc = DateTimeOffset.UtcNow.AddHours(-4), // 4 horas online
            ActiveDayPriorToGrace = Today - 1
        };

        bool isEarly = SleepPolicy.IsEarlyRiser(p, Today, now, dayStart, 60);
        Assert.False(isEarly);

        var outcome = SleepPolicy.Evaluate(p, Today, isOnline: true, isEarlyRiser: isEarly);
        Assert.Equal(RolloverOutcome.Penalized, outcome);
    }

    [Fact]
    public void AbandonedSessionYesterday_ReconnectingInMorning_IsPenalized()
    {
        // Jugó ayer por la tarde y desconectó sin dormir; reconecta a las 05:40
        DateTime now = new DateTime(2026, 11, 2, 5, 40, 0);
        TimeSpan dayStart = new TimeSpan(6, 0, 0);
        var p = new PlayerRecord
        {
            LastActiveGameDay = Today,
            CurrentSessionStartUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            ActiveDayPriorToGrace = Today // Ya había jugado en Today antes de las 05:00
        };

        bool isEarly = SleepPolicy.IsEarlyRiser(p, Today, now, dayStart, 60);
        Assert.False(isEarly);

        var outcome = SleepPolicy.Evaluate(p, Today, isOnline: true, isEarlyRiser: isEarly);
        Assert.Equal(RolloverOutcome.Penalized, outcome);
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
    public void DailyCounters_ResetOnNewDay_PreservesSatiety()
    {
        var p = new PlayerRecord { SatietyGameDay = 3, Satiety = 100f, WellFedBuffsUsed = 2, SpaBuffsUsed = 1 };

        p.EnsureDailyCounters(4);

        Assert.Equal(4, p.SatietyGameDay);
        Assert.Equal(100, p.SatietyUsed);
        Assert.Equal(0, p.WellFedBuffsUsed);
        Assert.Equal(0, p.SpaBuffsUsed);
    }

    [Fact]
    public void Satiety_EmptiesOverFourHours_OnlineAndOffline()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        var p = new PlayerRecord
        {
            Satiety = 135f, // 100% llena para 270 stamina (0.5 fraction = 135)
            LastSatietyUpdateUtc = now
        };

        // Transcurrida 1 hora (25% del total de 4h)
        p.UpdateSatietyDecay(now.AddHours(1), maxStamina: 270, dailyFraction: 0.5f, drainHours: 4.0f);
        Assert.Equal(101.25f, p.Satiety, precision: 2);

        // Transcurridas 2 horas en total (50% del total de 4h)
        p.UpdateSatietyDecay(now.AddHours(2), maxStamina: 270, dailyFraction: 0.5f, drainHours: 4.0f);
        Assert.Equal(67.5f, p.Satiety, precision: 2);

        // Transcurridas 4 horas en total (100% vacía)
        p.UpdateSatietyDecay(now.AddHours(4), maxStamina: 270, dailyFraction: 0.5f, drainHours: 4.0f);
        Assert.Equal(0f, p.Satiety, precision: 2);

        // Pasadas más de 4 horas
        p.UpdateSatietyDecay(now.AddHours(6), maxStamina: 270, dailyFraction: 0.5f, drainHours: 4.0f);
        Assert.Equal(0f, p.Satiety, precision: 2);
    }

    [Fact]
    public void Satiety_DoesNotResetOnDisconnectOrNewDay()
    {
        DateTimeOffset start = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);
        var p = new PlayerRecord
        {
            Satiety = 100f,
            LastSatietyUpdateUtc = start,
            SatietyGameDay = 1
        };

        // Simular desconexión de 30 minutos (30 min / 240 min = 12.5% de vaciado de 135 = 16.875 de drenaje)
        p.UpdateSatietyDecay(start.AddMinutes(30), maxStamina: 270, dailyFraction: 0.5f, drainHours: 4.0f);
        Assert.Equal(83.125f, p.Satiety, precision: 3);

        // Cambio de día de juego al reconectar
        p.EnsureDailyCounters(2);

        // La saciedad acumulada NO se resetea a 0, permanece en 83.125 (redondeado a 83)
        Assert.Equal(83, p.SatietyUsed);
        Assert.Equal(83.125f, p.Satiety, precision: 3);
    }

    [Fact]
    public void Serialization_RoundTrip_KeepsFractionalSatiety()
    {
        var p = new PlayerRecord { Satiety = 83.125f };

        string json = System.Text.Json.JsonSerializer.Serialize(p);
        var back = System.Text.Json.JsonSerializer.Deserialize<PlayerRecord>(json)!;

        Assert.Equal(83.125f, back.Satiety, precision: 3);
    }

    [Fact]
    public void ProjectedSatiety_DoesNotModifyRecord()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        var p = new PlayerRecord { Satiety = 135f, LastSatietyUpdateUtc = now };

        float projected = p.ProjectedSatiety(now.AddHours(1), maxStamina: 270, dailyFraction: 0.5f, drainHours: 4.0f);

        Assert.Equal(101.25f, projected, precision: 2);
        Assert.Equal(135f, p.Satiety);
        Assert.Equal(now, p.LastSatietyUpdateUtc);
    }

    [Fact]
    public void ApplyConsumptionReport_ClampsSatietyAndUsesHostClock()
    {
        DateTimeOffset hostNow = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        var p = new PlayerRecord { SatietyGameDay = 5, WellFedBuffsUsed = 2, SpaBuffsUsed = 1 };

        p.ApplyConsumptionReport(
            new ConsumptionReport { Satiety = 999f, GameDay = 5, WellFedBuffsUsed = 1, SpaBuffsUsed = 0, WellFedRemainingSeconds = 99999, SpaBuffRemainingSeconds = 600 },
            currentGameDay: 5, hostNow, maxSatiety: 135, maxWellFed: TimeSpan.FromMinutes(60), maxSpaBuff: TimeSpan.FromMinutes(120));

        Assert.Equal(135f, p.Satiety);
        Assert.Equal(hostNow, p.LastSatietyUpdateUtc);
        Assert.Equal((2, 1), (p.WellFedBuffsUsed, p.SpaBuffsUsed)); // los contadores diarios nunca bajan
        Assert.Equal(hostNow.AddMinutes(60), p.WellFedUntilUtc);   // acotado a la duración configurada
        Assert.Equal(hostNow.AddMinutes(10), p.SpaBuffUntilUtc);   // sellado con el reloj del host
    }

    [Fact]
    public void ApplyConsumptionReport_IgnoresCountersFromAnotherDay()
    {
        var p = new PlayerRecord { SatietyGameDay = 5 };

        p.ApplyConsumptionReport(
            new ConsumptionReport { Satiety = 40f, GameDay = 4, WellFedBuffsUsed = 2, SpaBuffsUsed = 1 },
            currentGameDay: 6, DateTimeOffset.UnixEpoch, maxSatiety: 135, maxWellFed: TimeSpan.FromMinutes(60), maxSpaBuff: TimeSpan.FromMinutes(120));

        Assert.Equal(40f, p.Satiety);
        Assert.Equal((6, 0, 0), (p.SatietyGameDay, p.WellFedBuffsUsed, p.SpaBuffsUsed));
    }

    [Fact]
    public void WellFed_ActivatesUpToDailyLimit_AndExpires()
    {
        DateTimeOffset t = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        var p = new PlayerRecord();
        p.EnsureDailyCounters(5);

        Assert.True(p.TryActivateWellFed(t, TimeSpan.FromMinutes(60), maxDailyBuffs: 2));
        Assert.True(p.IsWellFed(t.AddMinutes(59)));
        Assert.False(p.IsWellFed(t.AddMinutes(60)));

        Assert.True(p.TryActivateWellFed(t.AddHours(3), TimeSpan.FromMinutes(60), maxDailyBuffs: 2));
        Assert.False(p.TryActivateWellFed(t.AddHours(6), TimeSpan.FromMinutes(60), maxDailyBuffs: 2));

        p.EnsureDailyCounters(6);
        Assert.True(p.TryActivateWellFed(t.AddHours(24), TimeSpan.FromMinutes(60), maxDailyBuffs: 2));
    }

    [Fact]
    public void SpaBuff_OncePerDay_AndSurvivesDayChange()
    {
        DateTimeOffset t = new DateTimeOffset(2026, 11, 2, 23, 30, 0, TimeSpan.Zero);
        var p = new PlayerRecord();
        p.EnsureDailyCounters(5);

        Assert.True(p.TryActivateSpaBuff(t, TimeSpan.FromMinutes(120)));
        Assert.False(p.TryActivateSpaBuff(t.AddMinutes(130), TimeSpan.FromMinutes(120))); // ya usado hoy

        // Cambia el día con el buff aún activo: sigue hasta su fin y no se puede reactivar encima
        p.EnsureDailyCounters(6);
        Assert.True(p.IsSpaBuffActive(t.AddMinutes(119)));
        Assert.False(p.TryActivateSpaBuff(t.AddMinutes(60), TimeSpan.FromMinutes(120)));
        Assert.True(p.TryActivateSpaBuff(t.AddMinutes(121), TimeSpan.FromMinutes(120)));
    }

    [Fact]
    public void ConsumptionReport_SendsRemainingSecondsOfBuffs()
    {
        DateTimeOffset t = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        var p = new PlayerRecord { WellFedUntilUtc = t.AddMinutes(15), SpaBuffUntilUtc = t.AddMinutes(-5) };

        ConsumptionReport r = p.ToConsumptionReport(t);

        Assert.Equal((900, 0), (r.WellFedRemainingSeconds, r.SpaBuffRemainingSeconds));
    }

    [Fact]
    public void CopyConsumptionFrom_KeepsLocalSatietyOverHostSync()
    {
        DateTimeOffset t = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        var local = new PlayerRecord { Satiety = 70f, LastSatietyUpdateUtc = t, SatietyGameDay = 5, WellFedBuffsUsed = 1, WellFedUntilUtc = t.AddMinutes(30), SpaBuffsUsed = 1, SpaBuffUntilUtc = t.AddHours(1) };
        var fromHost = new PlayerRecord { Satiety = 0f, SatietyGameDay = 5, RestedOnGameDay = 4 };

        fromHost.CopyConsumptionFrom(local);

        Assert.Equal((70f, t, 5, 1, 1), (fromHost.Satiety, fromHost.LastSatietyUpdateUtc, fromHost.SatietyGameDay, fromHost.WellFedBuffsUsed, fromHost.SpaBuffsUsed));
        Assert.Equal((t.AddMinutes(30), t.AddHours(1)), (fromHost.WellFedUntilUtc, fromHost.SpaBuffUntilUtc));
        Assert.Equal(4, fromHost.RestedOnGameDay);
    }
}

public class BedLogoutPayloadTests
{
    [Fact]
    public void BedLogoutPayload_PropertiesHoldAssignedValues()
    {
        var payload = new BedLogoutPayload
        {
            GameDay = 15,
            LocationName = "Pelican4Ever_Farm_03_Home",
            BedTileX = 9,
            BedTileY = 9,
            PositionX = 9f * 64f,
            PositionY = 9f * 64f
        };

        Assert.Equal(15, payload.GameDay);
        Assert.Equal("Pelican4Ever_Farm_03_Home", payload.LocationName);
        Assert.Equal(9, payload.BedTileX);
        Assert.Equal(9, payload.BedTileY);
        Assert.Equal(576f, payload.PositionX);
        Assert.Equal(576f, payload.PositionY);
    }
}
