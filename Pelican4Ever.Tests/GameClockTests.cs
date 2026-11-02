using Pelican4Ever.Core.Time;
using Xunit;

namespace Pelican4Ever.Tests;

public class GameClockTests
{
    private static readonly TimeSpan DayStart = new(6, 0, 0);
    private static readonly TimeSpan HoldStart = new(1, 50, 0);

    [Theory]
    [InlineData(6, 0, 600, false)]
    [InlineData(6, 9, 600, false)]
    [InlineData(6, 10, 610, false)]
    [InlineData(12, 34, 1230, false)]
    [InlineData(23, 59, 2350, false)]
    [InlineData(0, 0, 2400, false)]
    [InlineData(0, 30, 2430, false)]
    [InlineData(1, 49, 2540, false)]
    [InlineData(1, 50, 2550, true)]
    [InlineData(3, 0, 2550, true)]
    [InlineData(5, 59, 2550, true)]
    public void Read_MapsRealTimeToEngineTime(int hour, int minute, int expectedTime, bool expectedHold)
    {
        ClockReading reading = GameClock.Read(new TimeSpan(hour, minute, 0), DayStart, HoldStart);

        Assert.Equal(expectedTime, reading.TimeOfDay);
        Assert.Equal(expectedHold, reading.IsNightHold);
    }

    [Fact]
    public void Read_NeverReachesForcedPassOutTime()
    {
        for (int minute = 0; minute < 24 * 60; minute++)
        {
            ClockReading reading = GameClock.Read(TimeSpan.FromMinutes(minute), DayStart, HoldStart);
            Assert.InRange(reading.TimeOfDay, 600, 2550);
            Assert.True(reading.TimeOfDay % 10 == 0 && reading.TimeOfDay % 100 < 60, $"Hora inválida {reading.TimeOfDay}");
        }
    }

    [Fact]
    public void Read_EarlierHoldStart_FreezesEarlier()
    {
        ClockReading reading = GameClock.Read(new TimeSpan(1, 0, 0), DayStart, new TimeSpan(0, 30, 0));

        Assert.True(reading.IsNightHold);
        Assert.Equal(2430, reading.TimeOfDay);
    }

    [Fact]
    public void Read_ReportsMillisecondsIntoSlotForLightInterpolation()
    {
        ClockReading reading = GameClock.Read(new TimeSpan(0, 14, 7, 30), DayStart, HoldStart);

        Assert.Equal(1400, reading.TimeOfDay);
        Assert.Equal((7 * 60 + 30) * 1000, reading.MillisecondsIntoSlot);
    }

    [Theory]
    [InlineData(600, 600, 0)]
    [InlineData(600, 610, 1)]
    [InlineData(1250, 1310, 2)]
    [InlineData(2350, 2400, 1)]
    [InlineData(1300, 1200, 0)] // nunca hacia atrás (DST / ajustes de reloj)
    public void TicksToReach_CountsTenMinuteSteps(int current, int target, int expected)
    {
        Assert.Equal(expected, GameClock.TicksToReach(current, target));
    }

    [Theory]
    [InlineData(12, 0, 0)]   // Mediodía: no estamos en NightHold
    [InlineData(1, 49, 0)]   // Justo antes de NightHold
    [InlineData(1, 50, 0)]   // Inicio exacto de NightHold
    [InlineData(1, 59, 0)]   // Menos de 10 min en NightHold
    [InlineData(2, 0, 1)]    // 10 minutos en NightHold -> 1 tick
    [InlineData(2, 30, 4)]   // 40 minutos en NightHold -> 4 ticks
    [InlineData(3, 0, 7)]    // 70 minutos en NightHold -> 7 ticks
    [InlineData(5, 50, 24)]  // 240 minutos en NightHold -> 24 ticks
    public void NightHoldTicksElapsed_CalculatesExpectedTicks(int hour, int minute, int expectedTicks)
    {
        int ticks = GameClock.NightHoldTicksElapsed(new TimeSpan(hour, minute, 0), DayStart, HoldStart);
        Assert.Equal(expectedTicks, ticks);
    }

    [Theory]
    [InlineData(600, 360)]
    [InlineData(2430, 1470)]
    [InlineData(2550, 1550)]
    public void MinuteConversion_RoundTrips(int time, int minutes)
    {
        Assert.Equal(minutes, GameClock.ToMinutes(time));
        Assert.Equal(time, GameClock.FromMinutes(minutes));
    }

    [Theory]
    [InlineData(12, 0, 2, 1400)]   // 12:00 + 2h -> 14:00
    [InlineData(12, 0, -1, 1100)]  // 12:00 - 1h -> 11:00
    [InlineData(8, 0, 0.5, 830)]   // 08:00 + 30m -> 08:30
    public void Read_WithTimeOffset_ShiftsGameClockAccurately(int hour, int minute, double offsetHours, int expectedTime)
    {
        DateTime baseTime = new DateTime(2026, 10, 30, hour, minute, 0);
        DateTime shifted = baseTime.AddHours(offsetHours);
        ClockReading reading = GameClock.Read(shifted.TimeOfDay, DayStart, HoldStart);

        Assert.Equal(expectedTime, reading.TimeOfDay);
    }

    [Theory]
    [InlineData(6, 0, 600)]
    [InlineData(12, 34, 1230)]
    [InlineData(0, 0, 2400)]
    [InlineData(1, 49, 2540)]
    [InlineData(1, 50, 2550)]
    [InlineData(2, 0, 2600)]
    [InlineData(2, 30, 2630)]
    [InlineData(3, 45, 2740)]
    [InlineData(5, 50, 2950)]
    [InlineData(5, 59, 2950)]
    public void GetDisplayTimeOfDay_MapsRealTimeToDisplayTimeBeyondPassOut(int hour, int minute, int expectedDisplayTime)
    {
        int displayTime = GameClock.GetDisplayTimeOfDay(new TimeSpan(hour, minute, 0), DayStart);
        Assert.Equal(expectedDisplayTime, displayTime);
    }
}
