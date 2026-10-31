using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class FarmGrassLogicTests
    {
        [Theory]
        [InlineData("Meadowlands", true)]
        [InlineData("MeadowlandsFarm", true)]
        [InlineData("meadowlands", true)]
        [InlineData("Standard", false)]
        [InlineData("Riverland", false)]
        [InlineData("Forest", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsMeadowlands_IdentifiesCorrectly(string? farmType, bool expected)
        {
            Assert.Equal(expected, FarmGrassLogic.IsMeadowlands(farmType));
        }

        [Theory]
        [InlineData(true, 0.05, FarmGrassLogic.BlueGrassType)]
        [InlineData(true, 0.099, FarmGrassLogic.BlueGrassType)]
        [InlineData(true, 0.10, FarmGrassLogic.StandardGrassType)]
        [InlineData(true, 0.50, FarmGrassLogic.StandardGrassType)]
        [InlineData(false, 0.01, FarmGrassLogic.StandardGrassType)]
        [InlineData(false, 0.50, FarmGrassLogic.StandardGrassType)]
        public void DetermineGrassType_SelectsBlueForMeadowlandsWithChance(bool isMeadowlands, double roll, int expectedType)
        {
            Assert.Equal(expectedType, FarmGrassLogic.DetermineGrassType(isMeadowlands, roll));
        }

        [Fact]
        public void CalculateDailyAttempts_NormalDay_ReturnsWithinRange()
        {
            int attempts = FarmGrassLogic.CalculateDailyAttempts(1, 5, 10, false, 15, 0.5);
            Assert.InRange(attempts, 1, 5);
        }

        [Fact]
        public void CalculateDailyAttempts_Spring1_MultipliesByFirstDayFactor()
        {
            int attempts = FarmGrassLogic.CalculateDailyAttempts(1, 5, 1, true, 15, 0.5);
            Assert.InRange(attempts, 15, 75);
        }

        [Theory]
        [InlineData(true, true, true, false, false, false, false)] // Winter -> false
        [InlineData(false, false, true, false, false, false, false)] // Not diggable -> false
        [InlineData(false, true, false, false, false, false, false)] // Tile not open -> false
        [InlineData(false, true, true, true, false, false, false)] // Tile occupied -> false
        [InlineData(false, true, true, false, true, false, false)] // Water tile -> false
        [InlineData(false, true, true, false, false, true, false)] // No spawn tile -> false
        [InlineData(false, true, true, false, false, false, true)] // Valid conditions -> true
        public void CanSpawnGrassOnTile_ValidatesAllTileConditions(
            bool isWinter,
            bool hasDiggable,
            bool isTileOpen,
            bool isOccupied,
            bool isWater,
            bool isNoSpawn,
            bool expected)
        {
            bool canSpawn = FarmGrassLogic.CanSpawnGrassOnTile(isWinter, hasDiggable, isTileOpen, isOccupied, isWater, isNoSpawn);
            Assert.Equal(expected, canSpawn);
        }
    }
}
