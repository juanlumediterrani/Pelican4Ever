using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Pelican4Ever.Core.Economy;
using Pelican4Ever.Core.Festivals;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class DoorJsonModel
    {
        public string DoorId { get; set; } = string.Empty;
        public int TownTileX { get; set; }
        public int TownTileY { get; set; }
        public string DefaultSlotId { get; set; } = string.Empty;
    }

    public class AnnexTests
    {
        [Fact]
        public void FreeSides_For16PortalsInDoorsJson_ReturnsExactVanillaSidesThatDoNotReturnToHost()
        {
            // Cargar doors.json real
            string doorsPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "Pelican4Ever", "assets", "doors.json");
            Assert.True(File.Exists(doorsPath), $"No se encontró doors.json en: {doorsPath}");

            string json = File.ReadAllText(doorsPath);
            var doors = JsonSerializer.Deserialize<List<DoorJsonModel>>(json);
            Assert.NotNull(doors);
            Assert.Equal(16, doors!.Count);

            foreach (var door in doors)
            {
                var freeSides = ResidenceExits.FreeSides(door.TownTileX, door.TownTileY);
                Assert.Equal(2, freeSides.Count);

                // Portales del SUR del Host (TownTileY >= 60): Backwoods (Norte) vuelve al host -> libres Este y Sur
                if (door.TownTileY >= 60)
                {
                    Assert.Contains(AnnexSide.East, freeSides);
                    Assert.Contains(AnnexSide.South, freeSides);
                    Assert.DoesNotContain(AnnexSide.North, freeSides);
                }
                // Portales del NORTE del Host (TownTileY <= 2): Forest (Sur) vuelve al host -> libres Norte y Este
                else if (door.TownTileY <= 2)
                {
                    Assert.Contains(AnnexSide.North, freeSides);
                    Assert.Contains(AnnexSide.East, freeSides);
                    Assert.DoesNotContain(AnnexSide.South, freeSides);
                }
                // Portales del OESTE del Host (TownTileX <= 2): BusStop (Este) vuelve al host -> libres Norte y Sur
                else
                {
                    Assert.Contains(AnnexSide.North, freeSides);
                    Assert.Contains(AnnexSide.South, freeSides);
                    Assert.DoesNotContain(AnnexSide.East, freeSides);
                }
            }
        }

        [Theory]
        [InlineData("Backwoods", AnnexSide.North)]
        [InlineData("Mountain", AnnexSide.North)]
        [InlineData("BusStop", AnnexSide.East)]
        [InlineData("Forest", AnnexSide.South)]
        [InlineData("Town", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void SideForVanillaTarget_IdentifiesCorrectSide(string? target, AnnexSide? expected)
        {
            Assert.Equal(expected, ResidenceExits.SideForVanillaTarget(target));
        }

        [Fact]
        public void AnnexEntrance_ForNorth_PlacesEntryAtSouthEdgeAndReturnsToTop()
        {
            var warp = AnnexEntrance.For(AnnexSide.North, 40, 32);

            // Entrada al anexo: borde sur del anexo mirando al Norte
            Assert.Equal(new TilePoint(20, 30), warp.EntryTile);
            Assert.Equal(0, warp.FacingDirection); // Norte

            // Salida de vuelta en el anexo
            Assert.Equal(new TilePoint(20, 31), warp.ReturnExitTile);

            // Reaparición en la residencia privada: salida norte de la granja (40, 1) mirando al Sur
            Assert.Equal(new TilePoint(40, 1), warp.ResidenceReturnTile);
            Assert.Equal(2, warp.ResidenceReturnFacingDirection); // Sur
        }

        [Fact]
        public void AnnexEntrance_ForEast_PlacesEntryAtWestEdgeAndReturnsToRight()
        {
            var warp = AnnexEntrance.For(AnnexSide.East, 40, 32);

            // Entrada al anexo: borde oeste del anexo mirando al Este
            Assert.Equal(new TilePoint(1, 16), warp.EntryTile);
            Assert.Equal(1, warp.FacingDirection); // Este

            // Salida de vuelta en el anexo
            Assert.Equal(new TilePoint(0, 16), warp.ReturnExitTile);

            // Reaparición en la residencia privada: salida este de la granja (78, 17) mirando al Oeste
            Assert.Equal(new TilePoint(78, 17), warp.ResidenceReturnTile);
            Assert.Equal(3, warp.ResidenceReturnFacingDirection); // Oeste
        }

        [Fact]
        public void AnnexEntrance_ForSouth_PlacesEntryAtNorthEdgeAndReturnsToBottom()
        {
            var warp = AnnexEntrance.For(AnnexSide.South, 40, 32);

            // Entrada al anexo: borde norte del anexo mirando al Sur
            Assert.Equal(new TilePoint(20, 1), warp.EntryTile);
            Assert.Equal(2, warp.FacingDirection); // Sur

            // Salida de vuelta en el anexo
            Assert.Equal(new TilePoint(20, 0), warp.ReturnExitTile);

            // Reaparición en la residencia privada: salida sur de la granja (40, 63) mirando al Norte
            Assert.Equal(new TilePoint(40, 63), warp.ResidenceReturnTile);
            Assert.Equal(0, warp.ResidenceReturnFacingDirection); // Norte
        }

        [Fact]
        public void AnnexTemplateCatalog_ContainsAll7FarmTypes()
        {
            Assert.Equal(7, AnnexTemplateCatalog.Templates.Count);
            Assert.True(AnnexTemplateCatalog.Templates.ContainsKey("Standard"));
            Assert.True(AnnexTemplateCatalog.Templates.ContainsKey("Fishing"));
            Assert.True(AnnexTemplateCatalog.Templates.ContainsKey("Foraging"));
            Assert.True(AnnexTemplateCatalog.Templates.ContainsKey("Mining"));
            Assert.True(AnnexTemplateCatalog.Templates.ContainsKey("Combat"));
            Assert.True(AnnexTemplateCatalog.Templates.ContainsKey("FourCorners"));
            Assert.True(AnnexTemplateCatalog.Templates.ContainsKey("Ranching"));

            // GetTemplate con fallback a Standard
            var fallback = AnnexTemplateCatalog.GetTemplate("TipoInexistente");
            Assert.Equal("Standard", fallback.Id);
        }

        [Fact]
        public void ServerWorldState_Migrate_UpgradesV2ToV3WithoutLosingResidences()
        {
            var state = new ServerWorldState
            {
                SchemaVersion = 2,
                Players = new Dictionary<long, PlayerRecord>
                {
                    [100L] = new PlayerRecord { UniqueMultiplayerID = 100L, PlayerName = "Mario", FarmSlotId = "Pelican4Ever_Farm_01" }
                },
                Doors = new Dictionary<string, DoorAssignment>
                {
                    ["Farm_Portal_01"] = new DoorAssignment
                    {
                        DoorId = "Farm_Portal_01",
                        FarmSlotId = "Pelican4Ever_Farm_01",
                        FarmType = "Mining",
                        OwnerId = 100L,
                        Policy = AccessPolicy.Friends
                    }
                },
                MarketListings = new List<MarketListing>(),
                FestivalScores = new List<FestivalScoreEntry>()
            };

            bool changed = state.Migrate();

            Assert.True(changed);
            Assert.Equal(3, state.SchemaVersion);
            Assert.NotNull(state.Annexes);
            Assert.Empty(state.Annexes);

            // Residencias intactas
            Assert.Single(state.Doors);
            Assert.Equal("Pelican4Ever_Farm_01", state.Doors["Farm_Portal_01"].FarmSlotId);
            Assert.Equal(100L, state.Doors["Farm_Portal_01"].OwnerId);
            Assert.Equal("Mining", state.Doors["Farm_Portal_01"].FarmType);
            Assert.Equal(AccessPolicy.Friends, state.Doors["Farm_Portal_01"].Policy);

            // Jugador intacto
            Assert.Single(state.Players);
            Assert.Equal("Mario", state.Players[100L].PlayerName);
        }

        [Fact]
        public void ServerWorldState_AnnexHelpers_ManageAssignments()
        {
            var state = new ServerWorldState();
            string annexId = ServerWorldState.BuildAnnexId("Pelican4Ever_Farm_01", AnnexSide.East);
            Assert.Equal("Pelican4Ever_Farm_01_Annex_East", annexId);

            var assignment = new AnnexAssignment
            {
                AnnexId = annexId,
                ResidenceSlotId = "Pelican4Ever_Farm_01",
                Side = AnnexSide.East,
                TemplateId = "Fishing",
                OwnerId = 100L
            };
            state.Annexes[annexId] = assignment;

            var found = state.FindAnnex("Pelican4Ever_Farm_01", AnnexSide.East);
            Assert.NotNull(found);
            Assert.Equal("Fishing", found!.TemplateId);
            Assert.Equal(100L, found.OwnerId);

            Assert.Null(state.FindAnnex("Pelican4Ever_Farm_01", AnnexSide.North));

            var annexes = state.GetAnnexesForResidence("Pelican4Ever_Farm_01");
            Assert.Single(annexes);
            Assert.Equal(annexId, annexes[0].AnnexId);
        }
    }
}
