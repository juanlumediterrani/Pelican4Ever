using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class NitvCompatTests
    {
        [Theory]
        [InlineData("Farm_Standard", "Nature.NIV.Outside", "true")]
        [InlineData("Farm_Forest", "Nature.NIV.Forest", "true")]
        [InlineData("Farm_Forest", "Nature.NIV.Outside", "true")]
        [InlineData("Farm_Riverland", "Nature.NIV.Beach", "true")]
        [InlineData("Farm_Beach", "Nature.NIV.Beach", "true")]
        [InlineData("Farm_FourCorners", "Nature.NIV.Forest", "true")]
        [InlineData("Farm_Hilltop", "Nature.NIV.Outside", "true")]
        [InlineData("Farm_Wilderness", "Nature.NIV.Outside", "true")]
        [InlineData("Farm_MeadowlandsFarm", "Nature.NIV.Outside", "true")]
        [InlineData("FarmCave", "Nature.NIV.Underground", "true")]
        public void VanillaNitvLocationCustomFields_CoversAbsorbedCompatEntries(string locationId, string fieldKey, string expectedValue)
        {
            Assert.True(FarmTemplateCatalog.VanillaNitvLocationCustomFields.TryGetValue(locationId, out var fields));
            Assert.True(fields!.TryGetValue(fieldKey, out string? actual));
            Assert.Equal(expectedValue, actual);
        }
    }
}
