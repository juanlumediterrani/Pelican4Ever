using System.Collections.Generic;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class ResidenceInteriorLogicTests
    {
        [Theory]
        [InlineData("Oak Table", 5, 4, true)]
        [InlineData("Table", 5, 3, true)]
        [InlineData("Table", 5, 6, false)]
        [InlineData("Oak Chair", 4, 4, true)]
        [InlineData("Chair", 4, 3, true)]
        [InlineData("Chair", 4, 6, false)]
        [InlineData("Budget TV", 1, 4, true)]
        [InlineData("Plasma TV", 1, 3, true)]
        [InlineData("Budget TV", 1, 5, false)]
        [InlineData("Stone Fireplace", 8, 4, false)] // Fireplace belongs at north wall chimney
        [InlineData("Floor Rug", 6, 7, false)]
        [InlineData("", 5, 4, false)]
        [InlineData(null, 5, 4, false)]
        public void IsCrampedNorthWallPlacement_DetectsStarterCrowding(string? itemName, int tileX, int tileY, bool expected)
        {
            bool isCramped = ResidenceInteriorLogic.IsCrampedNorthWallPlacement(itemName, tileX, tileY);
            Assert.Equal(expected, isCramped);
        }

        [Theory]
        [InlineData(3, 11, true)] // Door mat / exit warp tile
        [InlineData(3, 10, true)] // Spawn arrival tile
        [InlineData(3, 9, true)]  // Entrance corridor
        [InlineData(3, 8, true)]  // Entrance corridor
        [InlineData(3, 7, true)]  // Entrance corridor
        [InlineData(3, 6, false)] // Beyond entry corridor
        [InlineData(2, 9, false)] // Starter chest tile (adjacent, clear of walkway)
        [InlineData(5, 6, false)] // Centered dining table
        [InlineData(4, 6, false)] // Centered dining chair
        [InlineData(1, 5, false)] // TV
        [InlineData(9, 8, false)] // Bed
        public void IsWalkwayTile_ProtectsCorridorFromObstructions(int tileX, int tileY, bool expected)
        {
            bool isWalkway = ResidenceInteriorLogic.IsWalkwayTile(tileX, tileY);
            Assert.Equal(expected, isWalkway);
        }

        [Fact]
        public void TargetPlacements_NeverObstructWalkwayCorridor()
        {
            var targets = new[]
            {
                ResidenceInteriorLogic.BedPlacement,
                ResidenceInteriorLogic.DiningTablePlacement,
                ResidenceInteriorLogic.DiningChairPlacement,
                ResidenceInteriorLogic.TvPlacement,
                ResidenceInteriorLogic.FireplacePlacement,
                ResidenceInteriorLogic.RugPlacement,
                ResidenceInteriorLogic.PlantPlacement,
                ResidenceInteriorLogic.StarterChestPlacement
            };

            foreach (var target in targets)
            {
                // Verify within 12x12 cabin bounds
                Assert.InRange(target.TargetX, 0, 11);
                Assert.InRange(target.TargetY, 0, 11);

                // Verify completely clear of entry walkway
                Assert.False(
                    ResidenceInteriorLogic.IsWalkwayTile(target.TargetX, target.TargetY),
                    $"{target.ItemKey} placed at ({target.TargetX}, {target.TargetY}) obstructs the entry walkway!"
                );
            }
        }

        [Fact]
        public void SolidFurnitureTargets_DoNotOverlapEachOther()
        {
            var solidTargets = new Dictionary<string, (int X, int Y)>
            {
                { "Bed", (ResidenceInteriorLogic.BedPlacement.TargetX, ResidenceInteriorLogic.BedPlacement.TargetY) },
                { "Table", (ResidenceInteriorLogic.DiningTablePlacement.TargetX, ResidenceInteriorLogic.DiningTablePlacement.TargetY) },
                { "Chair", (ResidenceInteriorLogic.DiningChairPlacement.TargetX, ResidenceInteriorLogic.DiningChairPlacement.TargetY) },
                { "TV", (ResidenceInteriorLogic.TvPlacement.TargetX, ResidenceInteriorLogic.TvPlacement.TargetY) },
                { "Fireplace", (ResidenceInteriorLogic.FireplacePlacement.TargetX, ResidenceInteriorLogic.FireplacePlacement.TargetY) },
                { "Chest", (ResidenceInteriorLogic.StarterChestPlacement.TargetX, ResidenceInteriorLogic.StarterChestPlacement.TargetY) }
            };

            var occupied = new HashSet<(int X, int Y)>();
            foreach (var kvp in solidTargets)
            {
                Assert.DoesNotContain(kvp.Value, occupied);
                occupied.Add(kvp.Value);
            }
        }
    }
}
