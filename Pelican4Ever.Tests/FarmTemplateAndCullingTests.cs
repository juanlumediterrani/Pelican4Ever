using System.Collections.Generic;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class FarmTemplateAndCullingTests
    {
        [Theory]
        [InlineData("Standard", "Maps\\Farm", 64, 15)]
        [InlineData("Fishing", "Maps\\Farm_Fishing", 38, 14)]
        [InlineData("Foraging", "Maps\\Farm_Foraging", 40, 10)]
        [InlineData("Mining", "Maps\\Farm_Mining", 64, 24)]
        [InlineData("Combat", "Maps\\Farm_Combat", 68, 14)]
        [InlineData("FourCorners", "Maps\\Farm_FourCorners", 48, 39)]
        [InlineData("Ranching", "Maps\\Farm_Ranching", 59, 17)]
        public void FarmTemplateCatalog_ReturnsCorrectMapPathAndCoords(
            string templateId,
            string expectedMapPath,
            int expectedCabinX,
            int expectedCabinY)
        {
            var template = FarmTemplateCatalog.GetTemplate(templateId);
            Assert.Equal(expectedMapPath, template.MapPath);
            Assert.Equal(expectedCabinX, template.CabinTileX);
            Assert.Equal(expectedCabinY, template.CabinTileY);
        }

        [Fact]
        public void FarmTemplateCatalog_UnknownTemplate_DefaultsToStandard()
        {
            var template = FarmTemplateCatalog.GetTemplate("NonExistentTemplate");
            Assert.Equal("Standard", template.Id);
            Assert.Equal("Maps\\Farm", template.MapPath);
        }

        [Fact]
        public void LocationCulling_VanillaLocations_AlwaysActive()
        {
            bool keep = LocationCullingLogic.ShouldKeepActive("Town", 0, null, new HashSet<long>());
            Assert.True(keep);

            keep = LocationCullingLogic.ShouldKeepActive("Mountain", 0, null, new HashSet<long>());
            Assert.True(keep);
        }

        [Fact]
        public void LocationCulling_CustomFarm_ActiveIfFarmersInside()
        {
            bool keep = LocationCullingLogic.ShouldKeepActive("Pelican4Ever_Farm_01", 1, 100L, new HashSet<long>());
            Assert.True(keep);
        }

        [Fact]
        public void LocationCulling_CustomFarm_ActiveIfOwnerOnline()
        {
            var online = new HashSet<long> { 100L, 200L };
            bool keep = LocationCullingLogic.ShouldKeepActive("Pelican4Ever_Farm_01", 0, 100L, online);
            Assert.True(keep);
        }

        [Fact]
        public void LocationCulling_CustomFarm_CulledIfEmptyAndOwnerOffline()
        {
            var online = new HashSet<long> { 200L, 300L };
            bool keep = LocationCullingLogic.ShouldKeepActive("Pelican4Ever_Farm_01", 0, 100L, online);
            Assert.False(keep);
        }
    }
}
