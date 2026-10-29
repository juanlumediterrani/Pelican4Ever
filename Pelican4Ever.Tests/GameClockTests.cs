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
    [InlineData(600, 360)]
    [InlineData(2430, 1470)]
    [InlineData(2550, 1550)]
    public void MinuteConversion_RoundTrips(int time, int minutes)
    {
        Assert.Equal(minutes, GameClock.ToMinutes(time));
        Assert.Equal(time, GameClock.FromMinutes(minutes));
    }
}
