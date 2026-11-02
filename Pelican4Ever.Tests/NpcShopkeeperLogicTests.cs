using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class NpcShopkeeperLogicTests
    {
        [Theory]
        [InlineData("Robin", "ScienceHouse", 900, true)]
        [InlineData("Robin", "ScienceHouse", 1200, true)]
        [InlineData("Robin", "ScienceHouse", 1650, true)]
        [InlineData("Robin", "ScienceHouse", 1700, false)] // Closes at 17:00
        [InlineData("Robin", "ScienceHouse", 830, false)]  // Opens at 09:00
        [InlineData("Robin", "Town", 1200, false)]          // Not at ScienceHouse
        [InlineData("Pierre", "SeedShop", 900, true)]
        [InlineData("Pierre", "SeedShop", 1650, true)]
        [InlineData("Pierre", "SeedShop", 1700, false)]    // Closes at 17:00
        [InlineData("Marnie", "AnimalShop", 900, true)]
        [InlineData("Marnie", "AnimalShop", 1550, true)]
        [InlineData("Marnie", "AnimalShop", 1600, false)]   // Closes at 16:00
        [InlineData("Clint", "Blacksmith", 900, true)]
        [InlineData("Clint", "Blacksmith", 1550, true)]
        [InlineData("Clint", "Blacksmith", 1600, false)]   // Closes at 16:00
        [InlineData("Gus", "Saloon", 1200, true)]
        [InlineData("Gus", "Saloon", 2350, true)]
        [InlineData("Gus", "Saloon", 1150, false)]         // Opens at 12:00
        [InlineData("Willy", "FishShop", 900, true)]
        [InlineData("Willy", "FishShop", 1650, true)]
        [InlineData("Willy", "FishShop", 1700, false)]     // Closes at 17:00
        [InlineData("Harvey", "Hospital", 900, true)]
        [InlineData("Harvey", "Hospital", 1450, true)]
        [InlineData("Harvey", "Hospital", 1500, false)]    // Closes at 15:00
        [InlineData("Marlon", "AdventureGuild", 1400, true)]
        [InlineData("Marlon", "AdventureGuild", 2150, true)]
        [InlineData("Marlon", "AdventureGuild", 2200, false)]
        [InlineData("Sandy", "SandyHouse", 1000, true)]
        [InlineData("Krobus", "Sewer", 1000, true)]
        [InlineData("Abigail", "SeedShop", 1200, false)]   // Not a shopkeeper
        [InlineData("Demestrius", "ScienceHouse", 1200, false)]
        public void IsShopkeeperOnDuty_ValidatesCorrectly(string npc, string loc, int time, bool expected)
        {
            bool onDuty = NpcShopkeeperLogic.IsShopkeeperOnDuty(npc, loc, time);
            Assert.Equal(expected, onDuty);
        }

        [Fact]
        public void CanMicroWander_BlocksShopkeepersOnDuty()
        {
            // Pierre behind the counter during work hours must never micro-wander
            bool canWander = NpcShopkeeperLogic.CanMicroWander(
                npcName: "Pierre",
                locationName: "SeedShop",
                timeOfDay: 1000,
                isOutdoors: false,
                isDoingRouteAnimation: false,
                isSleeping: false,
                isSitting: false,
                hasActivity: false,
                isEmoting: false
            );
            Assert.False(canWander);

            // Robin behind the counter during work hours must never micro-wander
            canWander = NpcShopkeeperLogic.CanMicroWander(
                npcName: "Robin",
                locationName: "ScienceHouse",
                timeOfDay: 1200,
                isOutdoors: false,
                isDoingRouteAnimation: false,
                isSleeping: false,
                isSitting: false,
                hasActivity: false,
                isEmoting: false
            );
            Assert.False(canWander);
        }

        [Theory]
        [InlineData(true, false, false, false, false, "RouteAnimation")]
        [InlineData(false, true, false, false, false, "Sleeping")]
        [InlineData(false, false, true, false, false, "Sitting")]
        [InlineData(false, false, false, true, false, "Activity")]
        [InlineData(false, false, false, false, true, "Emoting")]
        public void CanMicroWander_BlocksWhenPerformingSpecialState(
            bool isDoingRouteAnim, bool isSleeping, bool isSitting, bool hasActivity, bool isEmoting, string reason)
        {
            _ = reason;
            bool canWander = NpcShopkeeperLogic.CanMicroWander(
                npcName: "Haley",
                locationName: "Town",
                timeOfDay: 1200,
                isOutdoors: true,
                isDoingRouteAnimation: isDoingRouteAnim,
                isSleeping: isSleeping,
                isSitting: isSitting,
                hasActivity: hasActivity,
                isEmoting: isEmoting
            );
            Assert.False(canWander);
        }

        [Fact]
        public void CanMicroWander_BlocksIndoorsExceptPublicSaloon()
        {
            // In tight indoor rooms (e.g. Haley/Emily house), micro-wandering is disabled to prevent blocking doors/paths
            bool canWander = NpcShopkeeperLogic.CanMicroWander(
                npcName: "Emily",
                locationName: "HaleyHouse",
                timeOfDay: 1200,
                isOutdoors: false,
                isDoingRouteAnimation: false,
                isSleeping: false,
                isSitting: false,
                hasActivity: false,
                isEmoting: false
            );
            Assert.False(canWander);

            // In Saloon during evening, regular patrons can micro-wander
            canWander = NpcShopkeeperLogic.CanMicroWander(
                npcName: "Shane",
                locationName: "Saloon",
                timeOfDay: 1800,
                isOutdoors: false,
                isDoingRouteAnimation: false,
                isSleeping: false,
                isSitting: false,
                hasActivity: false,
                isEmoting: false
            );
            Assert.True(canWander);
        }

        [Fact]
        public void CanMicroWander_AllowsOutdoorsWhenIdle()
        {
            bool canWander = NpcShopkeeperLogic.CanMicroWander(
                npcName: "Penny",
                locationName: "Town",
                timeOfDay: 1400,
                isOutdoors: true,
                isDoingRouteAnimation: false,
                isSleeping: false,
                isSitting: false,
                hasActivity: false,
                isEmoting: false
            );
            Assert.True(canWander);
        }
    }
}
