using System.Collections.Generic;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class ConstructionLocationFilterTests
    {
        [Fact]
        public void FarmhandWithoutClaimedResidence_ReturnsEmpty()
        {
            var locations = ConstructionLocationFilter.GetAllowedConstructionLocations(
                playerId: 12345L,
                isHost: false,
                claimedFarmSlotId: null,
                playerAnnexes: null
            );

            Assert.Empty(locations);
        }

        [Fact]
        public void FarmhandWithClaimedResidence_ReturnsOnlyTheirResidence()
        {
            var locations = ConstructionLocationFilter.GetAllowedConstructionLocations(
                playerId: 12345L,
                isHost: false,
                claimedFarmSlotId: "Pelican4Ever_Farm_03",
                playerAnnexes: null
            );

            Assert.Single(locations);
            Assert.Equal("Pelican4Ever_Farm_03", locations[0].Key);
            Assert.DoesNotContain(locations, l => l.Key == "Farm");
        }

        [Fact]
        public void FarmhandWithAnnexes_ReturnsResidenceAndAnnexesOnly()
        {
            var annexes = new List<AnnexAssignment>
            {
                new AnnexAssignment
                {
                    AnnexId = "Pelican4Ever_Annex_03_East",
                    ResidenceSlotId = "Pelican4Ever_Farm_03",
                    Side = AnnexSide.East,
                    TemplateId = "Standard"
                },
                new AnnexAssignment
                {
                    AnnexId = "Pelican4Ever_Annex_03_South",
                    ResidenceSlotId = "Pelican4Ever_Farm_03",
                    Side = AnnexSide.South,
                    TemplateId = "Standard"
                }
            };

            var locations = ConstructionLocationFilter.GetAllowedConstructionLocations(
                playerId: 12345L,
                isHost: false,
                claimedFarmSlotId: "Pelican4Ever_Farm_03",
                playerAnnexes: annexes
            );

            Assert.Equal(3, locations.Count);
            Assert.Equal("Pelican4Ever_Farm_03", locations[0].Key);
            Assert.Equal("Pelican4Ever_Annex_03_East", locations[1].Key);
            Assert.Equal("Pelican4Ever_Annex_03_South", locations[2].Key);

            // Verificación crítica: NUNCA se incluye la granja del host ni de otros jugadores
            Assert.DoesNotContain(locations, l => l.Key == "Farm");
            Assert.DoesNotContain(locations, l => l.Key == "Pelican4Ever_Farm_01");
            Assert.DoesNotContain(locations, l => l.Key == "Pelican4Ever_Farm_02");
        }

        [Fact]
        public void HostWithoutClaimedDoor_ReturnsHostFarm()
        {
            var locations = ConstructionLocationFilter.GetAllowedConstructionLocations(
                playerId: 99999L,
                isHost: true,
                claimedFarmSlotId: null,
                playerAnnexes: null
            );

            Assert.Single(locations);
            Assert.Equal("Farm", locations[0].Key);
        }

        [Fact]
        public void HostWithClaimedDoor_ReturnsClaimedResidence()
        {
            var locations = ConstructionLocationFilter.GetAllowedConstructionLocations(
                playerId: 99999L,
                isHost: true,
                claimedFarmSlotId: "Pelican4Ever_Farm_01",
                playerAnnexes: null
            );

            Assert.Single(locations);
            Assert.Equal("Pelican4Ever_Farm_01", locations[0].Key);
        }
    }
}
