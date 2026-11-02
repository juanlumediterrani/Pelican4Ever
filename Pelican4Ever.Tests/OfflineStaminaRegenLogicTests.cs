using System;
using Pelican4Ever.Core.Players;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class OfflineStaminaRegenLogicTests
    {
        [Fact]
        public void ZeroOrNegativeDuration_YieldsZeroGain()
        {
            float gainZero = OfflineStaminaRegenLogic.CalculateStaminaGain(270, TimeSpan.Zero, 4.0f);
            float gainNeg = OfflineStaminaRegenLogic.CalculateStaminaGain(270, TimeSpan.FromMinutes(-10), 4.0f);

            Assert.Equal(0f, gainZero);
            Assert.Equal(0f, gainNeg);
        }

        [Fact]
        public void DisabledFullBarHours_YieldsZeroGain()
        {
            float gainZero = OfflineStaminaRegenLogic.CalculateStaminaGain(270, TimeSpan.FromHours(2), 0f);
            float gainNeg = OfflineStaminaRegenLogic.CalculateStaminaGain(270, TimeSpan.FromHours(2), -1f);

            Assert.Equal(0f, gainZero);
            Assert.Equal(0f, gainNeg);
        }

        [Fact]
        public void OneHourOffline_RecoversQuarterOfFullBar()
        {
            // 1 hora de 4 horas = 25% de 270 = 67.5
            float gain = OfflineStaminaRegenLogic.CalculateStaminaGain(270, TimeSpan.FromHours(1), 4.0f);
            Assert.Equal(67.5f, gain, precision: 2);
        }

        [Fact]
        public void TwoHoursOffline_RecoversHalfOfFullBar()
        {
            // 2 horas de 4 horas = 50% de 270 = 135
            float gain = OfflineStaminaRegenLogic.CalculateStaminaGain(270, TimeSpan.FromHours(2), 4.0f);
            Assert.Equal(135.0f, gain, precision: 2);

            float newStamina = OfflineStaminaRegenLogic.CalculateNewStamina(
                currentStamina: 50f,
                maxStamina: 270,
                offlineDuration: TimeSpan.FromHours(2),
                fullBarHours: 4.0f);

            Assert.Equal(185.0f, newStamina, precision: 2);
        }

        [Fact]
        public void FourHoursOffline_RecoversFullBar()
        {
            // 4 horas de 4 horas = 100% de 270 = 270
            float gain = OfflineStaminaRegenLogic.CalculateStaminaGain(270, TimeSpan.FromHours(4), 4.0f);
            Assert.Equal(270.0f, gain, precision: 2);

            float newStamina = OfflineStaminaRegenLogic.CalculateNewStamina(
                currentStamina: 10f,
                maxStamina: 270,
                offlineDuration: TimeSpan.FromHours(4),
                fullBarHours: 4.0f);

            Assert.Equal(270.0f, newStamina, precision: 2);
        }

        [Fact]
        public void ExcessiveOfflineTime_ClampsToMaxStamina()
        {
            // 12 horas offline con 4h para barra completa -> tope en MaxStamina (270)
            float newStamina = OfflineStaminaRegenLogic.CalculateNewStamina(
                currentStamina: 50f,
                maxStamina: 270,
                offlineDuration: TimeSpan.FromHours(12),
                fullBarHours: 4.0f);

            Assert.Equal(270.0f, newStamina, precision: 2);
        }

        [Fact]
        public void NegativeStamina_RecoversGracefully()
        {
            // Jugador exhausto con -15 de estamina, descansa 2h (+135) -> -15 + 135 = 120
            float newStamina = OfflineStaminaRegenLogic.CalculateNewStamina(
                currentStamina: -15f,
                maxStamina: 270,
                offlineDuration: TimeSpan.FromHours(2),
                fullBarHours: 4.0f);

            Assert.Equal(120.0f, newStamina, precision: 2);
        }

        [Fact]
        public void AlreadyAtMax_DoesNotExceedMax()
        {
            float newStamina = OfflineStaminaRegenLogic.CalculateNewStamina(
                currentStamina: 270f,
                maxStamina: 270,
                offlineDuration: TimeSpan.FromHours(2),
                fullBarHours: 4.0f);

            Assert.Equal(270.0f, newStamina, precision: 2);
        }

        [Fact]
        public void CustomFullBarHours_ScalesCorrectly()
        {
            // Configurado a barra completa en 2 horas; 1 hora offline = 50%
            float gain = OfflineStaminaRegenLogic.CalculateStaminaGain(500, TimeSpan.FromHours(1), 2.0f);
            Assert.Equal(250.0f, gain, precision: 2);
        }

        [Fact]
        public void ApplyGain_PositiveGain_IncreasesStaminaUpToMax()
        {
            float result = OfflineStaminaRegenLogic.ApplyGain(currentStamina: 100f, maxStamina: 270, gained: 51f);
            Assert.Equal(151f, result);

            float clamped = OfflineStaminaRegenLogic.ApplyGain(currentStamina: 250f, maxStamina: 270, gained: 51f);
            Assert.Equal(270f, clamped);
        }

        [Fact]
        public void ApplyGain_ZeroOrNegativeGain_ReturnsCurrentStamina()
        {
            Assert.Equal(100f, OfflineStaminaRegenLogic.ApplyGain(100f, 270, 0f));
            Assert.Equal(100f, OfflineStaminaRegenLogic.ApplyGain(100f, 270, -10f));
        }

        [Fact]
        public void ApplyGain_WithNegativeStamina_RecoversCorrectly()
        {
            // Jugador con -15 de estamina recupera 51 -> 36
            float result = OfflineStaminaRegenLogic.ApplyGain(currentStamina: -15f, maxStamina: 270, gained: 51f);
            Assert.Equal(36f, result);
        }
    }
}
