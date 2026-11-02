using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class ResidenceUpgradeLogicTests
    {
        [Theory]
        [InlineData("Pelican4Ever_Farm_01", "Pelican4Ever_Farm_01_Cellar")]
        [InlineData("Pelican4Ever_Farm_15", "Pelican4Ever_Farm_15_Cellar")]
        [InlineData("", "Cellar")]
        [InlineData(null, "Cellar")]
        public void GetSlotCellarName_ReturnsExpectedCellarName(string? slotId, string expected)
        {
            string result = ResidenceUpgradeLogic.GetSlotCellarName(slotId);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Pelican4Ever_Farm_01_Home", "Pelican4Ever_Farm_01")]
        [InlineData("Pelican4Ever_Farm_12_Home", "Pelican4Ever_Farm_12")]
        [InlineData("FarmHouse", null)]
        [InlineData("Cabin", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void ExtractSlotIdFromCabinName_ExtractsSlotCorrectly(string? cabinName, string? expected)
        {
            string? result = ResidenceUpgradeLogic.ExtractSlotIdFromCabinName(cabinName);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void AdjustFarmhouseBuildingSourceRect_Level0_DoesNotChange()
        {
            var (x, y) = ResidenceUpgradeLogic.AdjustFarmhouseBuildingSourceRect(
                buildingType: "Farmhouse",
                isCabinIndoors: true,
                upgradeLevel: 0,
                currentX: 0,
                currentY: 0,
                width: 160,
                height: 144
            );

            Assert.Equal(0, x);
            Assert.Equal(0, y);
        }

        [Fact]
        public void AdjustFarmhouseBuildingSourceRect_Level1_RevertsXAndAppliesY()
        {
            // Vanilla applied currentX = 160 (horizontal shift), currentY = 0
            var (x, y) = ResidenceUpgradeLogic.AdjustFarmhouseBuildingSourceRect(
                buildingType: "Farmhouse",
                isCabinIndoors: true,
                upgradeLevel: 1,
                currentX: 160,
                currentY: 0,
                width: 160,
                height: 144
            );

            // Expect X shifted back by 160 -> 0, Y shifted down by 144 -> 144
            Assert.Equal(0, x);
            Assert.Equal(144, y);
        }

        [Fact]
        public void AdjustFarmhouseBuildingSourceRect_Level2And3_ClampedToTier2()
        {
            // Vanilla applied currentX = 320, currentY = 0
            var (x, y) = ResidenceUpgradeLogic.AdjustFarmhouseBuildingSourceRect(
                buildingType: "Farmhouse",
                isCabinIndoors: true,
                upgradeLevel: 3, // Level 3 uses tier 2 house sprite with cellar inside
                currentX: 320,
                currentY: 0,
                width: 160,
                height: 144
            );

            Assert.Equal(0, x);
            Assert.Equal(288, y);
        }

        [Fact]
        public void AdjustFarmhouseBuildingSourceRect_NonFarmhouseBuilding_Untouched()
        {
            var (x, y) = ResidenceUpgradeLogic.AdjustFarmhouseBuildingSourceRect(
                buildingType: "Log Cabin",
                isCabinIndoors: true,
                upgradeLevel: 2,
                currentX: 160,
                currentY: 0,
                width: 80,
                height: 112
            );

            Assert.Equal(160, x);
            Assert.Equal(0, y);
        }

        [Fact]
        public void AdjustFarmhouseBuildingSourceRect_NotCabinIndoors_Untouched()
        {
            var (x, y) = ResidenceUpgradeLogic.AdjustFarmhouseBuildingSourceRect(
                buildingType: "Farmhouse",
                isCabinIndoors: false,
                upgradeLevel: 2,
                currentX: 0,
                currentY: 288,
                width: 160,
                height: 144
            );

            Assert.Equal(0, x);
            Assert.Equal(288, y);
        }

        [Fact]
        public void AdjustFarmhouseBuildingSourceRect_NegativeUpgradeLevel_ClampedToZero()
        {
            var (x, y) = ResidenceUpgradeLogic.AdjustFarmhouseBuildingSourceRect(
                buildingType: "Farmhouse",
                isCabinIndoors: true,
                upgradeLevel: -1,
                currentX: 0,
                currentY: 0,
                width: 160,
                height: 144
            );

            Assert.Equal(0, x);
            Assert.Equal(0, y);
        }

        [Fact]
        public void AdjustFarmhouseBuildingSourceRect_HighUpgradeLevel_ClampedToTier2()
        {
            var (x, y) = ResidenceUpgradeLogic.AdjustFarmhouseBuildingSourceRect(
                buildingType: "Farmhouse",
                isCabinIndoors: true,
                upgradeLevel: 10,
                currentX: 320,
                currentY: 0,
                width: 160,
                height: 144
            );

            Assert.Equal(0, x);
            Assert.Equal(288, y);
        }

        [Theory]
        [InlineData("Pelican4Ever_Farm_01", "Pelican4Ever_Farm_01_Greenhouse")]
        [InlineData("Pelican4Ever_Farm_15", "Pelican4Ever_Farm_15_Greenhouse")]
        [InlineData("", "Greenhouse")]
        [InlineData(null, "Greenhouse")]
        public void GetSlotGreenhouseName_ReturnsExpectedGreenhouseName(string? slotId, string expected)
        {
            string result = ResidenceUpgradeLogic.GetSlotGreenhouseName(slotId);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Pelican4Ever_Farm_01_Greenhouse", "Pelican4Ever_Farm_01")]
        [InlineData("Pelican4Ever_Farm_12_Greenhouse", "Pelican4Ever_Farm_12")]
        [InlineData("Greenhouse", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void ExtractSlotIdFromGreenhouseName_ExtractsSlotCorrectly(string? greenhouseName, string? expected)
        {
            string? result = ResidenceUpgradeLogic.ExtractSlotIdFromGreenhouseName(greenhouseName);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(true, false, false, false, false, true)]
        [InlineData(false, true, false, false, false, true)]
        [InlineData(false, false, true, false, false, true)]
        [InlineData(false, false, false, true, false, true)]
        [InlineData(false, false, false, false, true, true)]
        [InlineData(false, false, false, false, false, false)]
        public void CanUnlockGreenhouse_EvaluatesConditionsCorrectly(
            bool hasCcPantry,
            bool hasJojaPantry,
            bool hasCompletedCC,
            bool masterHasCcPantry,
            bool isFarmUnlocked,
            bool expected)
        {
            bool result = ResidenceUpgradeLogic.CanUnlockGreenhouse(
                hasCcPantry,
                hasJojaPantry,
                hasCompletedCC,
                masterHasCcPantry,
                isFarmUnlocked
            );
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(25000, 200, 100, 5, true)]
        [InlineData(50000, 500, 300, 10, true)]
        [InlineData(24999, 200, 100, 5, false)] // Missing money
        [InlineData(25000, 199, 100, 5, false)] // Missing wood
        [InlineData(25000, 200, 99, 5, false)]  // Missing stone
        [InlineData(25000, 200, 100, 4, false)]  // Missing gold bar
        public void CanAffordMaterialRepair_ValidatesInventory(int money, int wood, int stone, int goldBars, bool expected)
        {
            bool result = ResidenceUpgradeLogic.CanAffordMaterialRepair(money, wood, stone, goldBars);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(35000, true)]
        [InlineData(100000, true)]
        [InlineData(34999, false)]
        [InlineData(0, false)]
        public void CanAffordJojaRepair_ValidatesMoney(int money, bool expected)
        {
            bool result = ResidenceUpgradeLogic.CanAffordJojaRepair(money);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(true, "Robin", true)]
        [InlineData(true, "Wizard", false)]
        [InlineData(false, "Robin", false)]
        public void ShouldKeepRobinAvailable_RespectsConfigAndBuilder(bool config, string builder, bool expected)
        {
            bool result = ResidenceUpgradeLogic.ShouldKeepRobinAvailable(config, builder);
            Assert.Equal(expected, result);
        }
    }
}
