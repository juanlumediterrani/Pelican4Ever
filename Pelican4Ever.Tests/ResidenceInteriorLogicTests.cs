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
        [InlineData("Budget TV", 1, 4, false)] // Standard TV placement at left wall facing south is valid
        [InlineData("Budget TV", 1, 5, true)]  // TV at (1, 5) from prior glitch is misplaced
        [InlineData("Plasma TV", 1, 3, true)]  // TV inside north wall is misplaced
        [InlineData("Single Bed", 1, 1, true)] // Bed jammed in top-left void is misplaced
        [InlineData("Bed", 1, 2, true)]
        [InlineData("Bed", 9, 8, false)]       // Bed at (9, 8) is valid
        [InlineData("House Plant", 1, 1, true)]// Plant on bed headboard is misplaced
        [InlineData("Plant", 1, 10, false)]    // Plant at (1, 10) corner is valid
        [InlineData("Stone Fireplace", 8, 4, false)] // Fireplace belongs at north wall chimney
        [InlineData("Floor Rug", 6, 8, false)]
        [InlineData("", 5, 4, false)]
        [InlineData(null, 5, 4, false)]
        public void IsCrampedNorthWallPlacement_DetectsStarterCrowding(string? itemName, int tileX, int tileY, bool expected)
        {
            bool isCramped = ResidenceInteriorLogic.IsCrampedNorthWallPlacement(itemName, tileX, tileY);
            Assert.Equal(expected, isCramped);
        }

        [Theory]
        [InlineData("Budget TV", 1, 4, 1, true)]  // TV with sideways rotation=1 is misplaced
        [InlineData("Budget TV", 1, 4, 0, false)] // TV with south-facing rotation=0 is correct
        [InlineData("Oak Chair", 4, 6, 1, false)] // Chair at (4, 6) facing table is correct
        [InlineData("Single Bed", 1, 1, 0, true)] // Bed at (1, 1) is misplaced
        [InlineData("Single Bed", 9, 8, 0, false)]// Bed at (9, 8) is correct
        public void IsMisplacedStarterFurniture_DetectsWrongRotationAndPlacement(string? itemName, int tileX, int tileY, int rotation, bool expected)
        {
            bool isMisplaced = ResidenceInteriorLogic.IsMisplacedStarterFurniture(itemName, tileX, tileY, rotation);
            Assert.Equal(expected, isMisplaced);
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
        [InlineData(1, 4, false)] // TV
        [InlineData(9, 8, false)] // Bed
        [InlineData(1, 10, false)]// Plant
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
                { "Chest", (ResidenceInteriorLogic.StarterChestPlacement.TargetX, ResidenceInteriorLogic.StarterChestPlacement.TargetY) },
                { "Plant", (ResidenceInteriorLogic.PlantPlacement.TargetX, ResidenceInteriorLogic.PlantPlacement.TargetY) }
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
