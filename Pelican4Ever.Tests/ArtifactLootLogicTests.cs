using System;
using System.Collections.Generic;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class ArtifactLootLogicTests
    {
        [Fact]
        public void GetFarmResidenceLocationNames_ContainsAllSlotsAndPotentialAnnexes()
        {
            var names = ArtifactLootLogic.GetFarmResidenceLocationNames(4);

            // 4 residencias + 4 * 4 anexos potenciales = 20 ubicaciones
            Assert.Contains("Pelican4Ever_Farm_01", names);
            Assert.Contains("Pelican4Ever_Farm_04", names);
            Assert.DoesNotContain("Pelican4Ever_Farm_05", names);

            Assert.Contains("Pelican4Ever_Annex_01_North", names);
            Assert.Contains("Pelican4Ever_Annex_01_South", names);
            Assert.Contains("Pelican4Ever_Annex_04_West", names);
        }

        [Fact]
        public void GetFarmResidenceLocationNames_ClampsSlots()
        {
            var names = ArtifactLootLogic.GetFarmResidenceLocationNames(0);
            Assert.Contains("Pelican4Ever_Farm_01", names);

            var namesMax = ArtifactLootLogic.GetFarmResidenceLocationNames(20);
            Assert.Contains("Pelican4Ever_Farm_16", namesMax);
            Assert.DoesNotContain("Pelican4Ever_Farm_17", namesMax);
        }

        [Fact]
        public void GetFarmResidenceLocationNames_IncludesCustomActiveAnnexIds()
        {
            var customAnnexes = new[] { "Pelican4Ever_Annex_99_Special" };
            var names = ArtifactLootLogic.GetFarmResidenceLocationNames(2, customAnnexes);

            Assert.Contains("Pelican4Ever_Annex_99_Special", names);
        }

        [Fact]
        public void UpdateLostBookCondition_ReplacesConditionCorrectly()
        {
            string baseCondition = "!PLAYER_LOCATION_NAME Current Farm";
            string updated = ArtifactLootLogic.UpdateLostBookCondition(baseCondition, 2);

            Assert.StartsWith("!PLAYER_LOCATION_NAME Current Farm", updated);
            Assert.Contains("Pelican4Ever_Farm_01", updated);
            Assert.Contains("Pelican4Ever_Farm_02", updated);
            Assert.Contains("Pelican4Ever_Annex_01_North", updated);
        }

        [Fact]
        public void UpdateLostBookCondition_PreservesOtherConditions()
        {
            string baseCondition = "LOCATION_SEASON Here Winter, !PLAYER_LOCATION_NAME Current Farm, TIME 0600";
            string updated = ArtifactLootLogic.UpdateLostBookCondition(baseCondition, 2);

            Assert.StartsWith("LOCATION_SEASON Here Winter, ", updated);
            Assert.EndsWith(", TIME 0600", updated);
            Assert.Contains("!PLAYER_LOCATION_NAME Current Farm Pelican4Ever_Farm_01", updated);
        }

        [Fact]
        public void UpdateLostBookCondition_IsIdempotent()
        {
            string baseCondition = "!PLAYER_LOCATION_NAME Current Farm";
            string pass1 = ArtifactLootLogic.UpdateLostBookCondition(baseCondition, 2);
            string pass2 = ArtifactLootLogic.UpdateLostBookCondition(pass1, 2);

            Assert.Equal(pass1, pass2);
        }

        [Fact]
        public void UpdateLostBookCondition_LeavesUnrelatedConditionUntouched()
        {
            string condition = "PLAYER_HAS_MAIL Host guntherBones";
            string updated = ArtifactLootLogic.UpdateLostBookCondition(condition, 4);

            Assert.Equal(condition, updated);
        }

        [Fact]
        public void ApplyFarmArtifactChances_PopulatesResidencesWhenFarmPresent()
        {
            var chances = new Dictionary<string, float>
            {
                ["Farm"] = 0.1f,
                ["Town"] = 0.05f
            };

            bool modified = ArtifactLootLogic.ApplyFarmArtifactChances(chances, 2);

            Assert.True(modified);
            Assert.Equal(0.1f, chances["Pelican4Ever_Farm_01"]);
            Assert.Equal(0.1f, chances["Pelican4Ever_Farm_02"]);
            Assert.Equal(0.1f, chances["Pelican4Ever_Annex_01_North"]);
            Assert.Equal(0.05f, chances["Town"]);
        }

        [Fact]
        public void ApplyFarmArtifactChances_DoesNothingWhenFarmMissing()
        {
            var chances = new Dictionary<string, float>
            {
                ["Mountain"] = 0.08f,
                ["Forest"] = 0.02f
            };

            bool modified = ArtifactLootLogic.ApplyFarmArtifactChances(chances, 4);

            Assert.False(modified);
            Assert.DoesNotContain("Pelican4Ever_Farm_01", chances.Keys);
        }
    }
}
