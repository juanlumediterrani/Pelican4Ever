using System.Text.Json;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class HouseholdRolloverLogicTests
    {
        [Fact]
        public void CanUpgradeHouseOrMarry_ReturnsNoResidence_WhenNullOrZeroPlayer()
        {
            var residence = new DoorAssignment
            {
                DoorId = "Door_01",
                FarmSlotId = "Pelican4Ever_Farm_01",
                OwnerId = 1001L
            };

            Assert.Equal(HouseholdRestrictionReason.NoResidence, HouseholdRolloverLogic.CanUpgradeHouseOrMarry(0L, residence));
            Assert.Equal(HouseholdRestrictionReason.NoResidence, HouseholdRolloverLogic.CanUpgradeHouseOrMarry(1001L, null));
        }

        [Fact]
        public void CanUpgradeHouseOrMarry_ReturnsNotOwner_ForTenantOrVisitor()
        {
            var residence = new DoorAssignment
            {
                DoorId = "Door_01",
                FarmSlotId = "Pelican4Ever_Farm_01",
                OwnerId = 1001L,
                TenantId = 2002L
            };

            Assert.Equal(HouseholdRestrictionReason.NotOwner, HouseholdRolloverLogic.CanUpgradeHouseOrMarry(2002L, residence));
            Assert.Equal(HouseholdRestrictionReason.NotOwner, HouseholdRolloverLogic.CanUpgradeHouseOrMarry(3003L, residence));
            Assert.Equal(HouseholdRestrictionReason.None, HouseholdRolloverLogic.CanUpgradeHouseOrMarry(1001L, residence));
        }

        [Theory]
        [InlineData(true, false, -20)]
        [InlineData(true, true, 0)]
        [InlineData(false, false, 0)]
        [InlineData(false, true, 0)]
        public void EvaluateDailySpouseFriendshipDelta_PenalizesOnlyUntalkedSpouse(
            bool isSpouseOrRoommate,
            bool talkedToToday,
            int expectedDelta)
        {
            int actual = HouseholdRolloverLogic.EvaluateDailySpouseFriendshipDelta(isSpouseOrRoommate, talkedToToday);
            Assert.Equal(expectedDelta, actual);
        }

        [Theory]
        [InlineData(true, true, 0, true, true)]
        [InlineData(true, true, -1, true, true)]
        [InlineData(true, true, 1, true, false)]
        [InlineData(true, true, 0, false, false)]
        [InlineData(false, true, 0, true, false)]
        [InlineData(true, false, 0, true, false)]
        public void ShouldCompleteOfflineWedding_ChecksCountdownAndWeddingDay(
            bool isEngaged,
            bool hasSpouseName,
            int countdownToWedding,
            bool canHaveWeddingToday,
            bool expected)
        {
            bool actual = HouseholdRolloverLogic.ShouldCompleteOfflineWedding(
                isEngaged,
                hasSpouseName,
                countdownToWedding,
                canHaveWeddingToday);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(true, false, false, true)]
        [InlineData(true, true, true, true)]
        [InlineData(true, true, false, false)]
        [InlineData(false, false, false, false)]
        public void IsStrandedResidenceNpc_DetectsMissingSpouseOrCabin(
            bool isResidenceCabinMap,
            bool hasLiveSpouse,
            bool isDefaultMapMissing,
            bool expected)
        {
            bool actual = HouseholdRolloverLogic.IsStrandedResidenceNpc(
                isResidenceCabinMap,
                hasLiveSpouse,
                isDefaultMapMissing);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ComputeResidencePorchSpot_UsesFarmhousePorchOffsetForMainBuilding()
        {
            var spot = HouseholdRolloverLogic.ComputeResidencePorchSpot(
                mainFarmHouseEntryX: 64,
                mainFarmHouseEntryY: 15,
                buildingTileX: 59,
                buildingTileY: 12,
                buildingTilesHigh: 6,
                isCabinBuilding: false);

            Assert.Equal((66, 15), spot);
        }

        [Fact]
        public void ComputeResidencePorchSpot_UsesCabinCornerWhenIsCabinBuilding()
        {
            var spot = HouseholdRolloverLogic.ComputeResidencePorchSpot(
                mainFarmHouseEntryX: 64,
                mainFarmHouseEntryY: 15,
                buildingTileX: 40,
                buildingTileY: 20,
                buildingTilesHigh: 3,
                isCabinBuilding: true);

            Assert.Equal((41, 22), spot);
        }

        [Fact]
        public void ShouldOfferPregnancyQuestion_RequiresAllGuardsAndFivePercentRoll()
        {
            Assert.True(HouseholdRolloverLogic.ShouldOfferPregnancyQuestion(
                isMarriedOrRoommates: true,
                hasSpouseNpc: true,
                spouseCanGetPregnant: true,
                spouseWantsChildren: true,
                hasNextBirthingDate: false,
                currentGameDay: 45,
                lastPromptGameDay: 44,
                randomRoll: 0.049));

            Assert.False(HouseholdRolloverLogic.ShouldOfferPregnancyQuestion(
                isMarriedOrRoommates: true,
                hasSpouseNpc: true,
                spouseCanGetPregnant: true,
                spouseWantsChildren: true,
                hasNextBirthingDate: false,
                currentGameDay: 45,
                lastPromptGameDay: 45,
                randomRoll: 0.01));

            Assert.False(HouseholdRolloverLogic.ShouldOfferPregnancyQuestion(
                isMarriedOrRoommates: true,
                hasSpouseNpc: true,
                spouseCanGetPregnant: true,
                spouseWantsChildren: true,
                hasNextBirthingDate: false,
                currentGameDay: 45,
                lastPromptGameDay: 44,
                randomRoll: 0.05));

            Assert.False(HouseholdRolloverLogic.ShouldOfferPregnancyQuestion(
                isMarriedOrRoommates: true,
                hasSpouseNpc: true,
                spouseCanGetPregnant: true,
                spouseWantsChildren: true,
                hasNextBirthingDate: true,
                currentGameDay: 45,
                lastPromptGameDay: -1,
                randomRoll: 0.01));
        }

        [Fact]
        public void PlayerRecord_FamilyTrackingFieldsRoundTripWithSafeDefaults()
        {
            var original = new PlayerRecord
            {
                UniqueMultiplayerID = 987654321L,
                PlayerName = "AbigailFan",
                PendingWeddingSpouse = "Abigail",
                LastPregnancyPromptGameDay = 42,
                LastBirthingHandledGameDay = 56
            };

            string json = JsonSerializer.Serialize(original);
            PlayerRecord? roundTripped = JsonSerializer.Deserialize<PlayerRecord>(json);

            Assert.NotNull(roundTripped);
            Assert.Equal("Abigail", roundTripped!.PendingWeddingSpouse);
            Assert.Equal(42, roundTripped.LastPregnancyPromptGameDay);
            Assert.Equal(56, roundTripped.LastBirthingHandledGameDay);

            var fresh = new PlayerRecord();
            Assert.Null(fresh.PendingWeddingSpouse);
            Assert.Equal(-1, fresh.LastPregnancyPromptGameDay);
            Assert.Equal(-1, fresh.LastBirthingHandledGameDay);
        }
    }
}
