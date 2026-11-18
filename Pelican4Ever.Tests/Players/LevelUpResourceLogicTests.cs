using Pelican4Ever.Core.Players;
using Xunit;

namespace Pelican4Ever.Tests.Players
{
    public class LevelUpResourceLogicTests
    {
        [Fact]
        public void ShouldPreserveResources_FalseDuringEndOfNight_TrueOtherwise()
        {
            Assert.False(LevelUpResourceLogic.ShouldPreserveResources(showingEndOfNightStuff: true));
            Assert.True(LevelUpResourceLogic.ShouldPreserveResources(showingEndOfNightStuff: false));
        }

        [Fact]
        public void ResolveHealth_KeepsMissingHealth_WhenMaxHealthUnchanged()
        {
            var before = new LevelUpResourceLogic.ResourceSnapshot(Health: 42, MaxHealth: 100, Stamina: 35f);

            int result = LevelUpResourceLogic.ResolveHealth(before, currentMaxHealth: 100);

            Assert.Equal(42, result);
        }

        [Fact]
        public void ResolveHealth_AppliesOnlyMaxHealthGain_WhenCombatOrProfessionIncreasesCap()
        {
            var injured = new LevelUpResourceLogic.ResourceSnapshot(Health: 42, MaxHealth: 100, Stamina: 35f);
            var full = new LevelUpResourceLogic.ResourceSnapshot(Health: 100, MaxHealth: 100, Stamina: 35f);

            Assert.Equal(57, LevelUpResourceLogic.ResolveHealth(injured, currentMaxHealth: 115));
            Assert.Equal(115, LevelUpResourceLogic.ResolveHealth(full, currentMaxHealth: 115));
        }

        [Fact]
        public void ResolveHealth_PreservesZeroOrNegativeHealth()
        {
            var knockedOut = new LevelUpResourceLogic.ResourceSnapshot(Health: 0, MaxHealth: 100, Stamina: 10f);

            Assert.Equal(0, LevelUpResourceLogic.ResolveHealth(knockedOut, currentMaxHealth: 125));
        }

        [Fact]
        public void ResolveStamina_PreservesPreviousValueIncludingExhausted()
        {
            var low = new LevelUpResourceLogic.ResourceSnapshot(Health: 42, MaxHealth: 100, Stamina: 35.5f);
            var exhausted = new LevelUpResourceLogic.ResourceSnapshot(Health: 42, MaxHealth: 100, Stamina: -12f);

            Assert.Equal(35.5f, LevelUpResourceLogic.ResolveStamina(low, currentMaxStamina: 270));
            Assert.Equal(-12f, LevelUpResourceLogic.ResolveStamina(exhausted, currentMaxStamina: 270));
        }

        [Fact]
        public void ResolveStamina_ClampsToCurrentMaxStamina()
        {
            var over = new LevelUpResourceLogic.ResourceSnapshot(Health: 100, MaxHealth: 100, Stamina: 310f);

            Assert.Equal(270f, LevelUpResourceLogic.ResolveStamina(over, currentMaxStamina: 270));
        }
    }
}
