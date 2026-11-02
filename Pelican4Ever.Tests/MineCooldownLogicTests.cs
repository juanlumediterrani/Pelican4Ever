using System;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class MineCooldownLogicTests
    {
        private readonly DateTime now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void FloorWithFarmers_IsNotUnloaded()
        {
            bool shouldUnload = MineCooldownLogic.ShouldUnloadFloor(
                level: 15,
                farmersCount: 2,
                lastOccupiedUtc: now.AddMinutes(-60),
                nowUtc: now,
                cooldownMinutes: 45,
                isTargetOfPendingWarp: false,
                hasDisconnectedFarmer: false);

            Assert.False(shouldUnload);
        }

        [Fact]
        public void SpecialEventFloor_IsNotUnloaded()
        {
            bool shouldUnload = MineCooldownLogic.ShouldUnloadFloor(
                level: MineCooldownLogic.SpecialEventFloor,
                farmersCount: 0,
                lastOccupiedUtc: now.AddMinutes(-60),
                nowUtc: now,
                cooldownMinutes: 45,
                isTargetOfPendingWarp: false,
                hasDisconnectedFarmer: false);

            Assert.False(shouldUnload);
        }

        [Fact]
        public void TargetOfPendingWarp_IsNotUnloaded()
        {
            bool shouldUnload = MineCooldownLogic.ShouldUnloadFloor(
                level: 20,
                farmersCount: 0,
                lastOccupiedUtc: now.AddMinutes(-60),
                nowUtc: now,
                cooldownMinutes: 45,
                isTargetOfPendingWarp: true,
                hasDisconnectedFarmer: false);

            Assert.False(shouldUnload);
        }

        [Fact]
        public void HasDisconnectedFarmer_IsNotUnloaded()
        {
            bool shouldUnload = MineCooldownLogic.ShouldUnloadFloor(
                level: 25,
                farmersCount: 0,
                lastOccupiedUtc: now.AddMinutes(-60),
                nowUtc: now,
                cooldownMinutes: 45,
                isTargetOfPendingWarp: false,
                hasDisconnectedFarmer: true);

            Assert.False(shouldUnload);
        }

        [Fact]
        public void CooldownDisabled_IsNotUnloaded()
        {
            bool shouldUnload = MineCooldownLogic.ShouldUnloadFloor(
                level: 30,
                farmersCount: 0,
                lastOccupiedUtc: now.AddMinutes(-60),
                nowUtc: now,
                cooldownMinutes: 0,
                isTargetOfPendingWarp: false,
                hasDisconnectedFarmer: false);

            Assert.False(shouldUnload);
        }

        [Fact]
        public void Unoccupied_BeforeCooldownExpires_IsNotUnloaded()
        {
            bool shouldUnload = MineCooldownLogic.ShouldUnloadFloor(
                level: 35,
                farmersCount: 0,
                lastOccupiedUtc: now.AddMinutes(-30),
                nowUtc: now,
                cooldownMinutes: 45,
                isTargetOfPendingWarp: false,
                hasDisconnectedFarmer: false);

            Assert.False(shouldUnload);
        }

        [Fact]
        public void Unoccupied_AfterCooldownExpires_IsUnloaded()
        {
            bool shouldUnload = MineCooldownLogic.ShouldUnloadFloor(
                level: 40,
                farmersCount: 0,
                lastOccupiedUtc: now.AddMinutes(-46),
                nowUtc: now,
                cooldownMinutes: 45,
                isTargetOfPendingWarp: false,
                hasDisconnectedFarmer: false);

            Assert.True(shouldUnload);
        }

        [Fact]
        public void NullLastOccupied_IsNotUnloaded()
        {
            bool shouldUnload = MineCooldownLogic.ShouldUnloadFloor(
                level: 45,
                farmersCount: 0,
                lastOccupiedUtc: null,
                nowUtc: now,
                cooldownMinutes: 45,
                isTargetOfPendingWarp: false,
                hasDisconnectedFarmer: false);

            Assert.False(shouldUnload);
        }
    }
}
