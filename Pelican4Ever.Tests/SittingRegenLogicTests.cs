using Pelican4Ever.Core.Economy;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class SittingRegenLogicTests
    {
        [Fact]
        public void NotSitting_DoesNotRegenAndResetsAccumulator()
        {
            bool regened = SittingRegenLogic.Tick(
                isSitting: false,
                isInBed: false,
                currentStamina: 100f,
                maxStamina: 270,
                currentAccumulator: 14,
                regenSeconds: 15,
                out float gain,
                out int newAcc);

            Assert.False(regened);
            Assert.Equal(0f, gain);
            Assert.Equal(0, newAcc);
        }

        [Fact]
        public void InBed_DoesNotRegenAndResetsAccumulator()
        {
            bool regened = SittingRegenLogic.Tick(
                isSitting: true,
                isInBed: true,
                currentStamina: 100f,
                maxStamina: 270,
                currentAccumulator: 14,
                regenSeconds: 15,
                out float gain,
                out int newAcc);

            Assert.False(regened);
            Assert.Equal(0f, gain);
            Assert.Equal(0, newAcc);
        }

        [Fact]
        public void DisabledRegenSeconds_DoesNotRegen()
        {
            bool regened = SittingRegenLogic.Tick(
                isSitting: true,
                isInBed: false,
                currentStamina: 100f,
                maxStamina: 270,
                currentAccumulator: 14,
                regenSeconds: 0,
                out float gain,
                out int newAcc);

            Assert.False(regened);
            Assert.Equal(0f, gain);
            Assert.Equal(0, newAcc);
        }

        [Fact]
        public void Sitting_TicksAccumulatorBeforeTarget()
        {
            bool regened = SittingRegenLogic.Tick(
                isSitting: true,
                isInBed: false,
                currentStamina: 100f,
                maxStamina: 270,
                currentAccumulator: 5,
                regenSeconds: 15,
                out float gain,
                out int newAcc);

            Assert.False(regened);
            Assert.Equal(0f, gain);
            Assert.Equal(6, newAcc);
        }

        [Fact]
        public void Sitting_ReachingTargetRegeneratesOneStaminaAndResetsAccumulator()
        {
            bool regened = SittingRegenLogic.Tick(
                isSitting: true,
                isInBed: false,
                currentStamina: 100f,
                maxStamina: 270,
                currentAccumulator: 14,
                regenSeconds: 15,
                out float gain,
                out int newAcc);

            Assert.True(regened);
            Assert.Equal(1f, gain);
            Assert.Equal(0, newAcc);
        }

        [Fact]
        public void Sitting_AtMaxStamina_DoesNotRegenBeyondMax()
        {
            bool regened = SittingRegenLogic.Tick(
                isSitting: true,
                isInBed: false,
                currentStamina: 270f,
                maxStamina: 270,
                currentAccumulator: 14,
                regenSeconds: 15,
                out float gain,
                out int newAcc);

            Assert.False(regened);
            Assert.Equal(0f, gain);
            Assert.Equal(0, newAcc);
        }

        [Fact]
        public void Sitting_NearMaxStamina_CapsGainToMax()
        {
            bool regened = SittingRegenLogic.Tick(
                isSitting: true,
                isInBed: false,
                currentStamina: 269.5f,
                maxStamina: 270,
                currentAccumulator: 14,
                regenSeconds: 15,
                out float gain,
                out int newAcc);

            Assert.True(regened);
            Assert.Equal(0.5f, gain);
            Assert.Equal(0, newAcc);
        }
    }
}
