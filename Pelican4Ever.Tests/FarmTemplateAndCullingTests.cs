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
        [InlineData("Beach", "Maps\\Farm_Island", 64, 15)]
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

        [Theory]
        [InlineData("Standard", 40, 1, 40, 63, 78, 17)]
        [InlineData("Fishing", 40, 1, 40, 63, 78, 17)]
        [InlineData("Foraging", 40, 1, 40, 63, 78, 17)]
        [InlineData("Mining", 40, 1, 40, 63, 78, 17)]
        [InlineData("Combat", 40, 1, 40, 63, 78, 17)]
        [InlineData("FourCorners", 40, 1, 40, 63, 78, 17)]
        [InlineData("Ranching", 63, 1, 52, 73, 98, 22)]
        [InlineData("Meadowlands", 63, 1, 52, 73, 98, 22)]
        [InlineData("Beach", 40, 1, 82, 103, 80, 17)]
        public void FarmTemplateCatalog_ReturnsCorrectArrivalCoords(
            string templateId, int nx, int ny, int sx, int sy, int ex, int ey)
        {
            var template = FarmTemplateCatalog.GetTemplate(templateId);
            Assert.Equal(nx, template.NorthArrivalTileX);
            Assert.Equal(ny, template.NorthArrivalTileY);
            Assert.Equal(sx, template.SouthArrivalTileX);
            Assert.Equal(sy, template.SouthArrivalTileY);
            Assert.Equal(ex, template.EastArrivalTileX);
            Assert.Equal(ey, template.EastArrivalTileY);
        }

        // Claves vanilla de Data/Locations (Game1.GetFarmTypeKey): sin ellas la residencia pesca solo basura
        [Theory]
        [InlineData("Standard", "Farm_Standard")]
        [InlineData("Fishing", "Farm_Riverland")]
        [InlineData("Foraging", "Farm_Forest")]
        [InlineData("Mining", "Farm_Hilltop")]
        [InlineData("Combat", "Farm_Wilderness")]
        [InlineData("FourCorners", "Farm_FourCorners")]
        [InlineData("Ranching", "Farm_MeadowlandsFarm")]
        [InlineData("Beach", "Farm_Beach")]
        public void FarmTemplateCatalog_MapsToVanillaLocationData(string templateId, string expectedKey)
        {
            Assert.Equal(expectedKey, FarmTemplateCatalog.GetTemplate(templateId).LocationDataKey);
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

        [Theory]
        [InlineData("Standard", 34, 5, 34, 6)]
        [InlineData("Fishing", 34, 5, 34, 6)]
        [InlineData("Foraging", 34, 5, 34, 6)]
        [InlineData("Mining", 34, 5, 34, 6)]
        [InlineData("Combat", 34, 5, 34, 6)]
        [InlineData("FourCorners", 30, 35, 30, 36)]
        [InlineData("Ranching", 88, 54, 88, 55)]
        [InlineData("Beach", 34, 15, 34, 16)]
        public void FarmTemplateCatalog_CaveCoordinates_ValidAndConfigured(string templateId, int expEntranceX, int expEntranceY, int expExitX, int expExitY)
        {
            var template = FarmTemplateCatalog.GetTemplate(templateId);
            Assert.Equal(expEntranceX, template.CaveEntranceX);
            Assert.Equal(expEntranceY, template.CaveEntranceY);
            Assert.Equal(expExitX, template.CaveExitX);
            Assert.Equal(expExitY, template.CaveExitY);
        }

        [Fact]
        public void LocationCulling_CustomCave_ActiveIfFarmersInsideOrOwnerOnline()
        {
            string caveId = "Pelican4Ever_Farm_01_Cave";
            Assert.True(LocationCullingLogic.IsPrivateFarmOrAnnex(caveId));

            // Activa si hay alguien dentro
            Assert.True(LocationCullingLogic.ShouldKeepActive(caveId, 1, 100L, new HashSet<long>()));

            // Activa si el dueño está online
            var online = new HashSet<long> { 100L, 200L };
            Assert.True(LocationCullingLogic.ShouldKeepActive(caveId, 0, 100L, online));

            // Culled si vacía y dueño offline
            var offline = new HashSet<long> { 200L, 300L };
            Assert.False(LocationCullingLogic.ShouldKeepActive(caveId, 0, 100L, offline));
        }

        [Fact]
        public void LocationCulling_CustomFarm_CulledIfEmptyAndOwnerOffline()
        {
            var online = new HashSet<long> { 200L, 300L };
            bool keep = LocationCullingLogic.ShouldKeepActive("Pelican4Ever_Farm_01", 0, 100L, online);
            Assert.False(keep);
        }

        [Theory]
        [InlineData("Standard", true, false, false)]
        [InlineData("Foraging", true, true, false)]
        [InlineData("Fishing", true, false, true)]
        [InlineData("Beach", true, false, true)]
        [InlineData("FourCorners", true, true, false)]
        [InlineData("Mining", true, false, false)]
        [InlineData("Combat", true, false, false)]
        [InlineData("Ranching", true, false, false)]
        public void FarmTemplateCatalog_ApplyNitvCustomFields_SetsCorrectBiomeTags(
            string templateId,
            bool expectOutside,
            bool expectForest,
            bool expectBeach)
        {
            var fields = new Dictionary<string, string>();
            FarmTemplateCatalog.ApplyNitvCustomFields(fields, templateId);

            Assert.Equal(expectOutside, fields.ContainsKey("Nature.NIV.Outside"));
            Assert.Equal(expectForest, fields.ContainsKey("Nature.NIV.Forest"));
            Assert.Equal(expectBeach, fields.ContainsKey("Nature.NIV.Beach"));
            Assert.Equal("true", fields["Nature.NIV.Outside"]);
            if (expectForest)
            {
                Assert.Equal("true", fields["Nature.NIV.Forest"]);
            }
            if (expectBeach)
            {
                Assert.Equal("true", fields["Nature.NIV.Beach"]);
            }
        }
    }
}
