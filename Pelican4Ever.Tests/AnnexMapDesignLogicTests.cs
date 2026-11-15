using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests;

/// <summary>
/// Tests del diseño de minimapas temáticos: el margen decorativo inalcanzable y el estilo de
/// entrada deben derivarse del comportamiento de cada tipo de granja (TASK-902).
/// </summary>
public class AnnexMapDesignLogicTests
{
    [Fact]
    public void RimStyles_DerivaDelComportamientoDeCadaTipoDeGranja()
    {
        // Fluvial (RiverFishSplashes): orillas de río en los cuatro bordes.
        var fluvial = RimStylesDe("Fishing");
        Assert.Equal(AnnexRimStyle.RiverBank, fluvial.North);
        Assert.Equal(AnnexRimStyle.RiverBank, fluvial.East);

        // Forestal (ForestForage + ForestStumps): franja de bosque.
        var forestal = RimStylesDe("Foraging");
        Assert.Equal(AnnexRimStyle.ForestEdge, forestal.North);
        Assert.Equal(AnnexRimStyle.ForestEdge, forestal.South);

        // Colinas (MountainOreArea): desfiladero de roca.
        var colinas = RimStylesDe("Mining");
        Assert.Equal(AnnexRimStyle.RockyCanyon, colinas.North);
        Assert.Equal(AnnexRimStyle.RockyCanyon, colinas.West);

        // Salvaje (NightMonsters): matorral impenetrable.
        var salvaje = RimStylesDe("Combat");
        Assert.Equal(AnnexRimStyle.WildThorns, salvaje.North);
        Assert.Equal(AnnexRimStyle.WildThorns, salvaje.East);

        // Playa (BeachForage): orilla oceánica.
        var playa = RimStylesDe("Beach");
        Assert.Equal(AnnexRimStyle.OceanShore, playa.North);
        Assert.Equal(AnnexRimStyle.OceanShore, playa.South);

        // Cuatro Esquinas (FourCornersExtras): bordes mixtos por cuadrante.
        var mixto = RimStylesDe("FourCorners");
        Assert.Equal(AnnexRimStyle.ForestEdge, mixto.North);
        Assert.Equal(AnnexRimStyle.HedgeAndTrees, mixto.East);
        Assert.Equal(AnnexRimStyle.RockyCanyon, mixto.South);
        Assert.Equal(AnnexRimStyle.ForestEdge, mixto.West);

        // Estándar y rancho (sin bonificadores): setos y cerco de rancho.
        Assert.Equal(AnnexRimStyle.HedgeAndTrees, RimStylesDe("Standard").North);
        Assert.Equal(AnnexRimStyle.RanchFence, RimStylesDe("Ranching").North);
    }

    private static (AnnexRimStyle North, AnnexRimStyle East, AnnexRimStyle South, AnnexRimStyle West)
        RimStylesDe(string templateId)
    {
        return AnnexMapDesignLogic.RimStyles(AnnexTemplateCatalog.GetTemplate(templateId).Behavior, templateId);
    }

    [Fact]
    public void EntranceStyle_DerivaDelComportamientoDeCadaTipoDeGranja()
    {
        Assert.Equal(AnnexEntranceStyle.WoodBridge, AnnexMapDesignLogic.EntranceStyleFor(AnnexTemplateCatalog.GetTemplate("Fishing")));
        Assert.Equal(AnnexEntranceStyle.ForestTunnel, AnnexMapDesignLogic.EntranceStyleFor(AnnexTemplateCatalog.GetTemplate("Foraging")));
        Assert.Equal(AnnexEntranceStyle.RockCanyon, AnnexMapDesignLogic.EntranceStyleFor(AnnexTemplateCatalog.GetTemplate("Mining")));
        Assert.Equal(AnnexEntranceStyle.WildGrove, AnnexMapDesignLogic.EntranceStyleFor(AnnexTemplateCatalog.GetTemplate("Combat")));
        Assert.Equal(AnnexEntranceStyle.SandShorePath, AnnexMapDesignLogic.EntranceStyleFor(AnnexTemplateCatalog.GetTemplate("Beach")));
        Assert.Equal(AnnexEntranceStyle.DirtGate, AnnexMapDesignLogic.EntranceStyleFor(AnnexTemplateCatalog.GetTemplate("FourCorners")));
        Assert.Equal(AnnexEntranceStyle.DirtGate, AnnexMapDesignLogic.EntranceStyleFor(AnnexTemplateCatalog.GetTemplate("Standard")));
        Assert.Equal(AnnexEntranceStyle.DirtGate, AnnexMapDesignLogic.EntranceStyleFor(AnnexTemplateCatalog.GetTemplate("Ranching")));
    }

    [Theory]
    [InlineData(EntranceEsPuenteCasos.WoodBridge, true)]
    [InlineData(EntranceEsPuenteCasos.DirtGate, false)]
    [InlineData(EntranceEsPuenteCasos.ForestTunnel, false)]
    public void IsWoodBridgeStyle_SoloElPuenteDeMaderaCuentaComoPuente(EntranceEsPuenteCasos casus, bool esperado)
    {
        Assert.Equal(esperado, AnnexMapDesignLogic.IsWoodBridgeStyle((AnnexEntranceStyle)casus));
    }

    public enum EntranceEsPuenteCasos
    {
        DirtGate = AnnexEntranceStyle.DirtGate,
        WoodBridge = AnnexEntranceStyle.WoodBridge,
        ForestTunnel = AnnexEntranceStyle.ForestTunnel
    }

    [Fact]
    public void IsRimTile_SoloDelimitaLasBandasDeLosCuatroBordes()
    {
        const int W = 40, H = 32, Depth = 2;

        // Interior del mapa: nunca margen.
        Assert.False(AnnexMapDesignLogic.IsRimTile(W, H, Depth, 20, 15));
        Assert.False(AnnexMapDesignLogic.IsRimTile(W, H, Depth, 2, 29));

        // Bordes horizontales.
        Assert.True(AnnexMapDesignLogic.IsRimTile(W, H, Depth, 20, 0));
        Assert.True(AnnexMapDesignLogic.IsRimTile(W, H, Depth, 20, 1));
        Assert.True(AnnexMapDesignLogic.IsRimTile(W, H, Depth, 20, 31));
        Assert.False(AnnexMapDesignLogic.IsRimTile(W, H, Depth, 20, 2));

        // Bordes verticales.
        Assert.True(AnnexMapDesignLogic.IsRimTile(W, H, Depth, 0, 15));
        Assert.True(AnnexMapDesignLogic.IsRimTile(W, H, Depth, 39, 15));
        Assert.False(AnnexMapDesignLogic.IsRimTile(W, H, Depth, 2, 15));
    }

    [Fact]
    public void EnumerateRimTiles_RecorreExactamenteElMargenSinInteriores()
    {
        const int W = 40, H = 32, Depth = 2;

        int count = 0;
        foreach (var (x, y) in AnnexMapDesignLogic.EnumerateRimTiles(W, H, Depth))
        {
            count++;
            Assert.True(AnnexMapDesignLogic.IsRimTile(W, H, Depth, x, y));
        }

        // Total del margen de profundidad 2: 2·d·(ancho+alto) - 4·d² (esquinas).
        int esperado = 2 * Depth * (W + H) - 4 * Depth * Depth;
        Assert.Equal(esperado, count);
    }

    [Fact]
    public void RimStyleFor_RespetaElOrdenDeBordesPorLadosCardinales()
    {
        AnnexTemplate mixto = AnnexTemplateCatalog.GetTemplate("FourCorners");

        Assert.Equal(AnnexRimStyle.ForestEdge, AnnexMapDesignLogic.RimStyleFor(mixto, AnnexSide.North));
        Assert.Equal(AnnexRimStyle.HedgeAndTrees, AnnexMapDesignLogic.RimStyleFor(mixto, AnnexSide.East));
        Assert.Equal(AnnexRimStyle.RockyCanyon, AnnexMapDesignLogic.RimStyleFor(mixto, AnnexSide.South));
    }
}
