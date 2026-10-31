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
            Assert.Equal("Pelican4Ever_Annex_01_East", annexId);

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

            var foundById = state.FindAnnexById("pelican4ever_annex_01_east");
            Assert.NotNull(foundById);
            Assert.Equal(annexId, foundById!.AnnexId);

            Assert.Null(state.FindAnnex("Pelican4Ever_Farm_01", AnnexSide.North));

            var annexes = state.GetAnnexesForResidence("Pelican4Ever_Farm_01");
            Assert.Single(annexes);
            Assert.Equal(annexId, annexes[0].AnnexId);
        }

        [Fact]
        public void AnnexTemplateCatalog_Task902_HasValid40x32CropsAndRecalculatedBonusZones()
        {
            foreach (var kvp in AnnexTemplateCatalog.Templates)
            {
                AnnexTemplate t = kvp.Value;
                Assert.Equal(40, t.Width);
                Assert.Equal(32, t.Height);
                Assert.StartsWith("Maps/", t.SourceVanillaMap);
                Assert.InRange(t.SourceCropX, 0, 40);
                Assert.InRange(t.SourceCropY, 0, 33);

                if (t.Behavior.MountainOreArea is { } area)
                {
                    // La zona de mineral recalculada debe caber íntegramente dentro del interior de 40×32 (sin tocar bordes)
                    Assert.InRange(area.X, 2, t.Width - 3);
                    Assert.InRange(area.Y, 2, t.Height - 3);
                    Assert.InRange(area.X + area.Width, 3, t.Width - 2);
                    Assert.InRange(area.Y + area.Height, 3, t.Height - 2);
                }
            }

            var fishing = AnnexTemplateCatalog.GetTemplate("Fishing");
            Assert.True(fishing.Behavior.RiverFishSplashes);
            Assert.Equal("Town 0.7", fishing.FishLocationOverride);
            Assert.Equal("Farm_Riverland", fishing.LocationDataKey);

            var mining = AnnexTemplateCatalog.GetTemplate("Mining");
            Assert.NotNull(mining.Behavior.MountainOreArea);
            Assert.Equal(new TileArea(5, 13, 22, 8), mining.Behavior.MountainOreArea!.Value);

            var foraging = AnnexTemplateCatalog.GetTemplate("Foraging");
            Assert.True(foraging.Behavior.ForestForage);
            Assert.True(foraging.Behavior.ForestStumps);

            var combat = AnnexTemplateCatalog.GetTemplate("Combat");
            Assert.True(combat.Behavior.NightMonsters);

            var fourCorners = AnnexTemplateCatalog.GetTemplate("FourCorners");
            Assert.NotNull(fourCorners.Behavior.MountainOreArea);
            Assert.True(fourCorners.Behavior.FourCornersExtras);
        }

        [Theory]
        [InlineData("Maps/Pelican4Ever_Annex_Fishing_East", true, "Fishing", AnnexSide.East)]
        [InlineData("Maps\\Pelican4Ever_Annex_Mining_South", true, "Mining", AnnexSide.South)]
        [InlineData("Maps/Pelican4Ever_Annex_Foraging", true, "Foraging", AnnexSide.North)]
        [InlineData("Maps/Pelican4Ever_HostFarm", false, "Standard", AnnexSide.North)]
        [InlineData("Maps/Farm", false, "Standard", AnnexSide.North)]
        public void TryParseAnnexMapAsset_ParsesTemplateAndSideCorrectly(
            string assetName,
            bool expectedMatch,
            string expectedTemplateId,
            AnnexSide expectedSide)
        {
            bool matched = AnnexTemplateCatalog.TryParseAnnexMapAsset(assetName, out AnnexTemplate template, out AnnexSide side);
            Assert.Equal(expectedMatch, matched);
            if (expectedMatch)
            {
                Assert.Equal(expectedTemplateId, template.Id);
                Assert.Equal(expectedSide, side);
            }
        }

        [Fact]
        public void LocationCullingLogic_Task903_CullsInactiveAnnexAndKeepsActiveWhenVisitedOrOwnerOnline()
        {
            string annexId = "Pelican4Ever_Annex_01_East";
            Assert.True(LocationCullingLogic.IsPrivateFarmOrAnnex(annexId));

            // Propietario desconectado y sin visitantes -> se cullea (false)
            Assert.False(LocationCullingLogic.ShouldKeepActive(annexId, farmersInsideCount: 0, ownerId: 100L, onlinePlayerIds: new long[] { 200L }));

            // Visitante dentro aunque el propietario esté desconectado -> activa (true)
            Assert.True(LocationCullingLogic.ShouldKeepActive(annexId, farmersInsideCount: 1, ownerId: 100L, onlinePlayerIds: new long[] { 200L }));

            // Propietario conectado aunque no haya nadie dentro -> activa (true)
            Assert.True(LocationCullingLogic.ShouldKeepActive(annexId, farmersInsideCount: 0, ownerId: 100L, onlinePlayerIds: new long[] { 100L, 200L }));
        }

        [Fact]
        public void NormalizeDoorAssignments_Task903_ReleasesAnnexesOfReleasedResidencesAndKeepsActiveOnes()
        {
            var state = new ServerWorldState
            {
                Doors = new Dictionary<string, DoorAssignment>
                {
                    ["Farm_Portal_01"] = new DoorAssignment
                    {
                        DoorId = "Farm_Portal_01",
                        FarmSlotId = "Pelican4Ever_Farm_01",
                        FarmType = "Standard",
                        OwnerId = 100L
                    },
                    ["Obsolete_Door"] = new DoorAssignment
                    {
                        DoorId = "Obsolete_Door",
                        FarmSlotId = "Pelican4Ever_Farm_99",
                        FarmType = "Mining",
                        OwnerId = 200L
                    }
                },
                Annexes = new Dictionary<string, AnnexAssignment>
                {
                    ["Pelican4Ever_Annex_01_East"] = new AnnexAssignment
                    {
                        AnnexId = "Pelican4Ever_Annex_01_East",
                        ResidenceSlotId = "Pelican4Ever_Farm_01",
                        Side = AnnexSide.East,
                        TemplateId = "InvalidTemplate",
                        OwnerId = 999L
                    },
                    ["Pelican4Ever_Annex_99_North"] = new AnnexAssignment
                    {
                        AnnexId = "Pelican4Ever_Annex_99_North",
                        ResidenceSlotId = "Pelican4Ever_Farm_99",
                        Side = AnnexSide.North,
                        TemplateId = "Mining",
                        OwnerId = 200L
                    }
                }
            };

            var catalog = new Dictionary<string, string>
            {
                ["Farm_Portal_01"] = "Pelican4Ever_Farm_01"
            };

            var released = state.NormalizeDoorAssignments(catalog);

            Assert.Single(released);
            Assert.Equal("Obsolete_Door", released[0]);

            // El anexo de la residencia liberada (Farm_99) se eliminó; el de Farm_01 se conserva y repara
            Assert.Single(state.Annexes);
            Assert.True(state.Annexes.ContainsKey("Pelican4Ever_Annex_01_East"));
            Assert.False(state.Annexes.ContainsKey("Pelican4Ever_Annex_99_North"));
            Assert.Equal(100L, state.Annexes["Pelican4Ever_Annex_01_East"].OwnerId);
            Assert.Equal("Standard", state.Annexes["Pelican4Ever_Annex_01_East"].TemplateId);
        }

        [Fact]
        public void AnnexPurchaseLogic_Task905_ValidatesOwnerFundsAndFreeSides()
        {
            var worldState = new ServerWorldState();
            long ownerId = 100L;
            long strangerId = 200L;
            string slotId = "Pelican4Ever_Farm_01";
            var residenceDoor = new DoorAssignment
            {
                DoorId = "Farm_Portal_01",
                FarmSlotId = slotId,
                FarmType = "Standard",
                OwnerId = ownerId
            };
            var freeSides = new[] { AnnexSide.North, AnnexSide.East };

            // 1. Rechazo por no ser el propietario
            var resNotOwner = AnnexPurchaseLogic.ValidateAndPurchase(
                strangerId, slotId, AnnexSide.North, "Fishing", 50000, residenceDoor, freeSides, worldState);
            Assert.False(resNotOwner.Success);
            Assert.Equal(AnnexPurchaseRejection.NotResidenceOwner, resNotOwner.Rejection);

            // 2. Rechazo si el lado no es libre para esta residencia
            var resSideNotFree = AnnexPurchaseLogic.ValidateAndPurchase(
                ownerId, slotId, AnnexSide.South, "Fishing", 50000, residenceDoor, freeSides, worldState);
            Assert.False(resSideNotFree.Success);
            Assert.Equal(AnnexPurchaseRejection.SideNotFree, resSideNotFree.Rejection);

            // 3. Rechazo por fondos insuficientes
            int fishingPrice = AnnexTemplateCatalog.GetTemplate("Fishing").BasePriceGold; // 30,000g
            var resNoFunds = AnnexPurchaseLogic.ValidateAndPurchase(
                ownerId, slotId, AnnexSide.North, "Fishing", fishingPrice - 1, residenceDoor, freeSides, worldState);
            Assert.False(resNoFunds.Success);
            Assert.Equal(AnnexPurchaseRejection.InsufficientFunds, resNoFunds.Rejection);

            // 4. Rechazo por plantilla desconocida
            var resUnknownTpl = AnnexPurchaseLogic.ValidateAndPurchase(
                ownerId, slotId, AnnexSide.North, "PlutoniumFarm", 100000, residenceDoor, freeSides, worldState);
            Assert.False(resUnknownTpl.Success);
            Assert.Equal(AnnexPurchaseRejection.UnknownTemplate, resUnknownTpl.Rejection);

            // 5. Compra exitosa
            int initialMoney = 50000;
            var resSuccess = AnnexPurchaseLogic.ValidateAndPurchase(
                ownerId, slotId, AnnexSide.North, "Fishing", initialMoney, residenceDoor, freeSides, worldState);
            Assert.True(resSuccess.Success);
            Assert.Equal(AnnexPurchaseRejection.None, resSuccess.Rejection);
            Assert.NotNull(resSuccess.Assignment);
            Assert.Equal("Pelican4Ever_Annex_01_North", resSuccess.Assignment!.AnnexId);
            Assert.Equal("Fishing", resSuccess.Assignment.TemplateId);
            Assert.Equal(ownerId, resSuccess.Assignment.OwnerId);
            Assert.Equal(initialMoney - fishingPrice, resSuccess.RemainingPlayerMoney);

            // Añadir la asignación al estado
            worldState.Annexes[resSuccess.Assignment.AnnexId] = resSuccess.Assignment;

            // 6. Rechazo si el anexo ya existe en ese lado
            var resAlreadyExists = AnnexPurchaseLogic.ValidateAndPurchase(
                ownerId, slotId, AnnexSide.North, "Mining", 100000, residenceDoor, freeSides, worldState);
            Assert.False(resAlreadyExists.Success);
            Assert.Equal(AnnexPurchaseRejection.AnnexAlreadyExists, resAlreadyExists.Rejection);

            // 7. Compra del segundo lado libre (Este)
            var resSecond = AnnexPurchaseLogic.ValidateAndPurchase(
                ownerId, slotId, AnnexSide.East, "Mining", 100000, residenceDoor, freeSides, worldState, maxAnnexesPerResidence: 2);
            Assert.True(resSecond.Success);
            worldState.Annexes[resSecond.Assignment!.AnnexId] = resSecond.Assignment;

            // 8. Rechazo por alcanzar el máximo de anexos
            var resMaxReached = AnnexPurchaseLogic.ValidateAndPurchase(
                ownerId, slotId, AnnexSide.East, "Mining", 100000, residenceDoor, freeSides, worldState, maxAnnexesPerResidence: 2);
            Assert.False(resMaxReached.Success);
            Assert.Equal(AnnexPurchaseRejection.AnnexAlreadyExists, resMaxReached.Rejection);
        }

        [Fact]
        public void AnnexTypeBonuses_Task906_BehaviorAppliesPerAnnexTemplateIndependentlyOfResidence()
        {
            // Una residencia estándar tiene NightMonsters = false y RiverFishSplashes = false
            var standardResidenceBehavior = FarmTypeBehaviors.For("Standard");
            Assert.False(standardResidenceBehavior.NightMonsters);
            Assert.False(standardResidenceBehavior.RiverFishSplashes);
            Assert.Null(standardResidenceBehavior.MountainOreArea);

            // Un anexo de combate ("Combat") tiene NightMonsters = true
            var combatAnnex = AnnexTemplateCatalog.GetTemplate("Combat");
            Assert.True(combatAnnex.Behavior.NightMonsters);

            // Un anexo fluvial ("Fishing") tiene RiverFishSplashes = true y override de pesca
            var fishingAnnex = AnnexTemplateCatalog.GetTemplate("Fishing");
            Assert.True(fishingAnnex.Behavior.RiverFishSplashes);
            Assert.Equal("Farm_Riverland", fishingAnnex.LocationDataKey);
            Assert.Equal("Town 0.7", fishingAnnex.FishLocationOverride);

            // Un anexo minero ("Mining") tiene área de mineral recalculada en 40×32
            var miningAnnex = AnnexTemplateCatalog.GetTemplate("Mining");
            Assert.NotNull(miningAnnex.Behavior.MountainOreArea);
            Assert.Equal(new TileArea(5, 13, 22, 8), miningAnnex.Behavior.MountainOreArea!.Value);
            Assert.Equal(14, miningAnnex.Behavior.InitialMountainOreDays);
        }
    }
}
