using System;
using Pelican4Ever.Core.Time;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class NightHoldWorldLogicTests
    {
        [Theory]
        [InlineData(0, 0.0, false)]
        [InlineData(10, 0.0, false)]
        [InlineData(30, 0.0, false)]
        [InlineData(50, 0.0, false)]
        [InlineData(59, 0.0, false)]
        public void ShouldBubbleExpire_Under60Minutes_NeverExpires(int minutes, double roll, bool expected)
        {
            Assert.Equal(expected, NightHoldWorldLogic.ShouldBubbleExpire(minutes, roll));
        }

        [Fact]
        public void ShouldBubbleExpire_At60Minutes_ExpiresAccordingToThreshold()
        {
            // At 60 min: threshold = 0.1 + (60 / 1800) = 0.1 + 0.0333... = ~0.1333
            Assert.True(NightHoldWorldLogic.ShouldBubbleExpire(60, 0.10));
            Assert.False(NightHoldWorldLogic.ShouldBubbleExpire(60, 0.15));
        }

        [Fact]
        public void ShouldBubbleExpire_At120Minutes_HasHigherThreshold()
        {
            // At 120 min: threshold = 0.1 + (120 / 1800) = 0.1 + 0.0666... = ~0.1666
            Assert.True(NightHoldWorldLogic.ShouldBubbleExpire(120, 0.15));
            Assert.False(NightHoldWorldLogic.ShouldBubbleExpire(120, 0.18));
        }

        [Theory]
        [InlineData(180, 0.0, true)]
        [InlineData(180, 0.99, true)]
        [InlineData(240, 1.0, true)]
        public void ShouldBubbleExpire_AtOrOver180Minutes_AlwaysExpires(int minutes, double roll, bool expected)
        {
            Assert.Equal(expected, NightHoldWorldLogic.ShouldBubbleExpire(minutes, roll));
        }

        [Theory]
        [InlineData(0.0, true)]
        [InlineData(0.49, true)]
        [InlineData(0.50, false)]
        [InlineData(0.80, false)]
        public void ShouldSpawnBubble_FiftyPercentProbability(double roll, bool expected)
        {
            Assert.Equal(expected, NightHoldWorldLogic.ShouldSpawnBubble(roll));
        }

        [Theory]
        [InlineData(0.0, true)]
        [InlineData(0.49, true)]
        [InlineData(0.50, false)]
        public void ShouldSpawnOrePan_FiftyPercentProbability(double roll, bool expected)
        {
            Assert.Equal(expected, NightHoldWorldLogic.ShouldSpawnOrePan(roll));
        }

        [Theory]
        [InlineData(0.05, true)]
        [InlineData(0.099, true)]
        [InlineData(0.10, false)]
        [InlineData(0.50, false)]
        public void ShouldOrePanExpire_TenPercentProbability(double roll, bool expected)
        {
            Assert.Equal(expected, NightHoldWorldLogic.ShouldOrePanExpire(roll));
        }

        [Fact]
        public void ShouldSpawnNightMonster_NeutralLuck_UsesBaseThreshold()
        {
            // Luck 0: threshold = 0.25
            Assert.True(NightHoldWorldLogic.ShouldSpawnNightMonster(0.0, 0.20));
            Assert.False(NightHoldWorldLogic.ShouldSpawnNightMonster(0.0, 0.25));
        }

        [Fact]
        public void ShouldSpawnNightMonster_PositiveLuck_LowersThreshold()
        {
            // Luck 0.1: threshold = 0.25 - 0.05 = 0.20
            Assert.True(NightHoldWorldLogic.ShouldSpawnNightMonster(0.1, 0.19));
            Assert.False(NightHoldWorldLogic.ShouldSpawnNightMonster(0.1, 0.21));
        }

        [Fact]
        public void ShouldSpawnNightMonster_NegativeLuck_RaisesThreshold()
        {
            // Luck -0.1: threshold = 0.25 - (-0.05) = 0.30
            Assert.True(NightHoldWorldLogic.ShouldSpawnNightMonster(-0.1, 0.29));
            Assert.False(NightHoldWorldLogic.ShouldSpawnNightMonster(-0.1, 0.31));
        }

        [Theory]
        [InlineData(0.0, true)]
        [InlineData(0.24, true)]
        [InlineData(0.25, false)]
        [InlineData(0.50, false)]
        public void IsFlyingMonster_TwentyFivePercentProbability(double roll, bool expected)
        {
            Assert.Equal(expected, NightHoldWorldLogic.IsFlyingMonster(roll));
        }

        [Fact]
        public void NightHoldClock_RemainsAt2550ThroughoutMadrugada()
        {
            TimeSpan dayStart = new(6, 0, 0);
            TimeSpan nightHoldStart = new(1, 50, 0);

            // De 01:50 a 05:59, el reloj de motor siempre debe devolver 2550 con IsNightHold = true
            TimeSpan[] timesToCheck = new[]
            {
                new TimeSpan(1, 50, 0),
                new TimeSpan(2, 0, 0),
                new TimeSpan(2, 30, 0),
                new TimeSpan(3, 15, 0),
                new TimeSpan(4, 0, 0),
                new TimeSpan(5, 0, 0),
                new TimeSpan(5, 59, 59)
            };

            foreach (var time in timesToCheck)
            {
                ClockReading reading = GameClock.Read(time, dayStart, nightHoldStart);
                Assert.Equal(2550, reading.TimeOfDay);
                Assert.True(reading.IsNightHold, $"Falló IsNightHold a las {time}");
            }
        }

        [Fact]
        public void NightHoldTicksElapsed_IncrementsCleanlyEveryTenMinutes()
        {
            TimeSpan dayStart = new(6, 0, 0);
            TimeSpan nightHoldStart = new(1, 50, 0);

            // A la 01:50 transcurrieron 0 ticks dentro de NightHold
            Assert.Equal(0, GameClock.NightHoldTicksElapsed(new TimeSpan(1, 50, 0), dayStart, nightHoldStart));

            // A las 02:00 -> 1 tick (10 min)
            Assert.Equal(1, GameClock.NightHoldTicksElapsed(new TimeSpan(2, 0, 0), dayStart, nightHoldStart));

            // A las 03:00 -> 7 ticks (70 min)
            Assert.Equal(7, GameClock.NightHoldTicksElapsed(new TimeSpan(3, 0, 0), dayStart, nightHoldStart));

            // A las 05:50 -> 24 ticks (240 min)
            Assert.Equal(24, GameClock.NightHoldTicksElapsed(new TimeSpan(5, 50, 0), dayStart, nightHoldStart));
        }
    }
}
