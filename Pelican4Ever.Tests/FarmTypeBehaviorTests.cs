using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests;

public class FarmTypeBehaviorTests
{
    [Fact]
    public void Mining_UsesVanillaHilltopOreArea()
    {
        FarmTypeBehavior mining = FarmTypeBehaviors.For("Mining");

        Assert.Equal(new TileArea(5, 37, 22, 8), mining.MountainOreArea);
        Assert.Equal("5 37 22 8", mining.MountainOreArea!.Value.ToMapProperty());
        Assert.Equal(28, mining.InitialMountainOreDays);
    }

    [Fact]
    public void FourCorners_UsesVanillaOreAreaAndExtras()
    {
        FarmTypeBehavior fourCorners = FarmTypeBehaviors.For("FourCorners");

        Assert.Equal(new TileArea(51, 67, 11, 3), fourCorners.MountainOreArea);
        Assert.Equal(10, fourCorners.InitialMountainOreDays);
        Assert.True(fourCorners.FourCornersExtras);
    }

    [Theory]
    [InlineData("Fishing", false, false, true, false)]
    [InlineData("Foraging", true, true, false, false)]
    [InlineData("Combat", false, false, false, true)]
    [InlineData("Standard", false, false, false, false)]
    [InlineData("Ranching", false, false, false, false)]
    public void Template_EnablesOnlyItsOwnBehaviors(string templateId, bool forestForage, bool forestStumps, bool fishSplashes, bool monsters)
    {
        FarmTypeBehavior behavior = FarmTypeBehaviors.For(templateId);

        Assert.Equal(forestForage, behavior.ForestForage);
        Assert.Equal(forestStumps, behavior.ForestStumps);
        Assert.Equal(fishSplashes, behavior.RiverFishSplashes);
        Assert.Equal(monsters, behavior.NightMonsters);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("No")]
    public void UnknownType_BehavesAsStandard(string? templateId)
    {
        Assert.Same(FarmTypeBehavior.None, FarmTypeBehaviors.For(templateId));
    }

    [Fact]
    public void EveryCatalogTemplate_HasBehaviorEntry()
    {
        // Un tipo nuevo en el catálogo sin entrada aquí caería en silencio a "estándar"
        foreach (string templateId in FarmTemplateCatalog.Templates.Keys)
            Assert.True(templateId == "Standard" || templateId == "Ranching" || FarmTypeBehaviors.For(templateId) != FarmTypeBehavior.None, templateId);
    }
}
