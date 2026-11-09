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
    public class AnnexTests
    {
        [Fact]
        public void FreeSides_For16PortalsInDoorsJson_ReturnsExactVanillaSidesThatDoNotReturnToHost()
        {
            var doors = DoorCatalogFixture.LoadDoors();
            Assert.Equal(16, doors.Count);

            foreach (var door in doors)
            {
                var freeSides = ResidenceExits.FreeSides(door.Edge);
                Assert.Equal(2, freeSides.Count);

                // Portales del SUR: Backwoods (Norte) vuelve al origen -> libres Este y Sur
                if (door.Edge == PortalEdge.South)
                {
                    Assert.Contains(AnnexSide.East, freeSides);
                    Assert.Contains(AnnexSide.South, freeSides);
                    Assert.DoesNotContain(AnnexSide.North, freeSides);
                }
                // Portales del NORTE: Forest (Sur) vuelve al host -> libres Norte y Este
                else if (door.Edge == PortalEdge.North)
                {
                    Assert.Contains(AnnexSide.North, freeSides);
                    Assert.Contains(AnnexSide.East, freeSides);
                    Assert.DoesNotContain(AnnexSide.South, freeSides);
                }
                // Portales OESTE / ESTE: BusStop (Este) vuelve a la plaza -> libres Norte y Sur
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
        public void AnnexEntrance_Meadowlands_ReturnsToMeadowlandsCoordinates()
        {
            var northWarp = AnnexEntrance.For(AnnexSide.North, 40, 32, "Ranching");
            Assert.Equal(new TilePoint(63, 1), northWarp.ResidenceReturnTile);

            var southWarp = AnnexEntrance.For(AnnexSide.South, 40, 32, "Ranching");
            Assert.Equal(new TilePoint(52, 73), southWarp.ResidenceReturnTile);

            var eastWarp = AnnexEntrance.For(AnnexSide.East, 40, 32, "Meadowlands");
            Assert.Equal(new TilePoint(98, 22), eastWarp.ResidenceReturnTile);
        }

        [Fact]
        public void AnnexTemplateCatalog_ContainsAll8FarmTypes()
        {
            Assert.Equal(8, AnnexTemplateCatalog.Templates.Count);
            Assert.True(AnnexTemplateCatalog.Templates.ContainsKey("Standard"));
            Assert.True(AnnexTemplateCatalog.Templates.ContainsKey("Fishing"));
            Assert.True(AnnexTemplateCatalog.Templates.ContainsKey("Foraging"));
            Assert.True(AnnexTemplateCatalog.Templates.ContainsKey("Mining"));
            Assert.True(AnnexTemplateCatalog.Templates.ContainsKey("Combat"));
            Assert.True(AnnexTemplateCatalog.Templates.ContainsKey("FourCorners"));
            Assert.True(AnnexTemplateCatalog.Templates.ContainsKey("Ranching"));
            Assert.True(AnnexTemplateCatalog.Templates.ContainsKey("Beach"));

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
            Assert.Equal(ServerWorldState.CurrentSchemaVersion, state.SchemaVersion);
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
                var mapDims = VanillaFarmExitTable.Table[t.Id];
                Assert.InRange(t.SourceCropX, 0, mapDims.MapWidth - t.Width);
                Assert.InRange(t.SourceCropY, 0, mapDims.MapHeight - t.Height);

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
            Assert.Equal(new TileArea(5, 4, 21, 7), mining.Behavior.MountainOreArea!.Value);

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
        public void AnnexTemplateCatalog_NormalPrices_AreRestored()
        {
            Assert.Equal(25000, AnnexTemplateCatalog.GetTemplate("Standard").BasePriceGold);
            Assert.Equal(30000, AnnexTemplateCatalog.GetTemplate("Fishing").BasePriceGold);
            Assert.Equal(30000, AnnexTemplateCatalog.GetTemplate("Foraging").BasePriceGold);
            Assert.Equal(35000, AnnexTemplateCatalog.GetTemplate("Mining").BasePriceGold);
            Assert.Equal(35000, AnnexTemplateCatalog.GetTemplate("Combat").BasePriceGold);
            Assert.Equal(30000, AnnexTemplateCatalog.GetTemplate("FourCorners").BasePriceGold);
            Assert.Equal(30000, AnnexTemplateCatalog.GetTemplate("Ranching").BasePriceGold);
            Assert.Equal(30000, AnnexTemplateCatalog.GetTemplate("Beach").BasePriceGold);
        }

        [Theory]
        [InlineData("Standard", 10000, 25000)]
        [InlineData("Fishing", 12000, 30000)]
        [InlineData("Foraging", 12000, 30000)]
        [InlineData("Mining", 14000, 35000)]
        [InlineData("Combat", 14000, 35000)]
        [InlineData("FourCorners", 12000, 30000)]
        [InlineData("Ranching", 12000, 30000)]
        public void CalculatePrice_FirstAnnexHasDiscount_SecondAnnexFullPrice(
            string templateId,
            int expectedFirstPrice,
            int expectedSecondPrice)
        {
            // El primer minimapa (0 anexos previos) cuesta bastante menos (40% del precio base)
            int firstPrice = AnnexPurchaseLogic.CalculatePrice(templateId, existingAnnexCount: 0);
            Assert.Equal(expectedFirstPrice, firstPrice);

            // El segundo minimapa (1 anexo previo) cuesta el precio normal completo (100% del precio base)
            int secondPrice = AnnexPurchaseLogic.CalculatePrice(templateId, existingAnnexCount: 1);
            Assert.Equal(expectedSecondPrice, secondPrice);
            Assert.True(firstPrice < secondPrice);
        }

        [Fact]
        public void CalculatePrice_CustomMultipliers_CalculatesAccurately()
        {
            var template = AnnexTemplateCatalog.GetTemplate("Standard"); // Base 25,000g
            int price50 = AnnexPurchaseLogic.CalculatePrice(template, existingAnnexCount: 0, firstMultiplier: 0.50f);
            Assert.Equal(12500, price50);

            int price150 = AnnexPurchaseLogic.CalculatePrice(template, existingAnnexCount: 1, secondMultiplier: 1.50f);
            Assert.Equal(37500, price150);
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

            // 3. Rechazo por fondos insuficientes para el primer anexo (Fishing base 30k * 0.40 = 12k)
            int firstFishingPrice = AnnexPurchaseLogic.CalculatePrice("Fishing", 0);
            Assert.Equal(12000, firstFishingPrice);
            var resNoFunds = AnnexPurchaseLogic.ValidateAndPurchase(
                ownerId, slotId, AnnexSide.North, "Fishing", firstFishingPrice - 1, residenceDoor, freeSides, worldState);
            Assert.False(resNoFunds.Success);
            Assert.Equal(AnnexPurchaseRejection.InsufficientFunds, resNoFunds.Rejection);

            // 4. Rechazo por plantilla desconocida
            var resUnknownTpl = AnnexPurchaseLogic.ValidateAndPurchase(
                ownerId, slotId, AnnexSide.North, "PlutoniumFarm", 100000, residenceDoor, freeSides, worldState);
            Assert.False(resUnknownTpl.Success);
            Assert.Equal(AnnexPurchaseRejection.UnknownTemplate, resUnknownTpl.Rejection);

            // 5. Compra exitosa del PRIMER anexo (12.000g)
            int initialMoney = 50000;
            var resSuccess = AnnexPurchaseLogic.ValidateAndPurchase(
                ownerId, slotId, AnnexSide.North, "Fishing", initialMoney, residenceDoor, freeSides, worldState);
            Assert.True(resSuccess.Success);
            Assert.Equal(AnnexPurchaseRejection.None, resSuccess.Rejection);
            Assert.NotNull(resSuccess.Assignment);
            Assert.Equal("Pelican4Ever_Annex_01_North", resSuccess.Assignment!.AnnexId);
            Assert.Equal("Fishing", resSuccess.Assignment.TemplateId);
            Assert.Equal(ownerId, resSuccess.Assignment.OwnerId);
            Assert.Equal(firstFishingPrice, resSuccess.PricePaid);
            Assert.Equal(initialMoney - firstFishingPrice, resSuccess.RemainingPlayerMoney);

            // Añadir la asignación al estado
            worldState.Annexes[resSuccess.Assignment.AnnexId] = resSuccess.Assignment;

            // 6. Rechazo si el anexo ya existe en ese lado
            var resAlreadyExists = AnnexPurchaseLogic.ValidateAndPurchase(
                ownerId, slotId, AnnexSide.North, "Mining", 100000, residenceDoor, freeSides, worldState);
            Assert.False(resAlreadyExists.Success);
            Assert.Equal(AnnexPurchaseRejection.AnnexAlreadyExists, resAlreadyExists.Rejection);

            // 7. Compra del SEGUNDO lado libre (Este) -> Tarifa normal completa (Mining base 35k * 1.0 = 35k)
            int secondMiningPrice = AnnexPurchaseLogic.CalculatePrice("Mining", 1);
            Assert.Equal(35000, secondMiningPrice);
            var resSecond = AnnexPurchaseLogic.ValidateAndPurchase(
                ownerId, slotId, AnnexSide.East, "Mining", 100000, residenceDoor, freeSides, worldState, maxAnnexesPerResidence: 2);
            Assert.True(resSecond.Success);
            Assert.Equal(secondMiningPrice, resSecond.PricePaid);
            Assert.Equal(100000 - secondMiningPrice, resSecond.RemainingPlayerMoney);
            worldState.Annexes[resSecond.Assignment!.AnnexId] = resSecond.Assignment;

            // 8. Rechazo por alcanzar el máximo de anexos
            var freeSidesWithSouth = new[] { AnnexSide.North, AnnexSide.East, AnnexSide.South };
            var resMaxReached = AnnexPurchaseLogic.ValidateAndPurchase(
                ownerId, slotId, AnnexSide.South, "Mining", 100000, residenceDoor, freeSidesWithSouth, worldState, maxAnnexesPerResidence: 2);
            Assert.False(resMaxReached.Success);
            Assert.Equal(AnnexPurchaseRejection.MaxAnnexesReached, resMaxReached.Rejection);
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
            Assert.Equal(new TileArea(5, 4, 21, 7), miningAnnex.Behavior.MountainOreArea!.Value);
            Assert.Equal(14, miningAnnex.Behavior.InitialMountainOreDays);
        }

        [Fact]
        public void AnnexTemplates_All8Templates_HaveMeticulousDesignParameters()
        {
            var templates = AnnexTemplateCatalog.Templates;
            Assert.Equal(8, templates.Count);

            foreach (var kvp in templates)
            {
                var t = kvp.Value;
                Assert.Equal(40, t.Width);
                Assert.Equal(32, t.Height);
                Assert.False(string.IsNullOrWhiteSpace(t.DisplayName));
                Assert.False(string.IsNullOrWhiteSpace(t.MapAsset));
                Assert.StartsWith("Maps/", t.SourceVanillaMap);
                var mapDims = VanillaFarmExitTable.Table[t.Id];
                Assert.InRange(t.SourceCropX, 0, mapDims.MapWidth - t.Width);
                Assert.InRange(t.SourceCropY, 0, mapDims.MapHeight - t.Height);

                // Cada lado cardinal genera una ruta de asset válida
                Assert.Equal($"{t.MapAsset}_North", t.GetMapAssetForSide(AnnexSide.North));
                Assert.Equal($"{t.MapAsset}_East", t.GetMapAssetForSide(AnnexSide.East));
                Assert.Equal($"{t.MapAsset}_South", t.GetMapAssetForSide(AnnexSide.South));
            }

            // Validación específica de la plantilla Fluvial (Fishing)
            var fishing = templates["Fishing"];
            Assert.Equal("Maps/Farm_Fishing", fishing.SourceVanillaMap);
            Assert.Equal(22, fishing.SourceCropX);
            Assert.Equal(25, fishing.SourceCropY);
            Assert.True(fishing.Behavior.RiverFishSplashes);
            Assert.Equal("Farm_Riverland", fishing.LocationDataKey);
        }

        [Fact]
        public void AnnexEntrance_AllFreeSides_GenerateConsistentOpeningTiles()
        {
            foreach (var side in new[] { AnnexSide.North, AnnexSide.East, AnnexSide.South })
            {
                var pts = AnnexEntrance.For(side, 40, 32);
                Assert.InRange(pts.EntryTile.X, 0, 39);
                Assert.InRange(pts.EntryTile.Y, 0, 31);
                Assert.InRange(pts.ReturnExitTile.X, 0, 39);
                Assert.InRange(pts.ReturnExitTile.Y, 0, 31);

                // El ReturnExitTile siempre está situado en el perímetro exterior (borde del mapa)
                bool isBorder = pts.ReturnExitTile.X == 0 || pts.ReturnExitTile.X == 39
                    || pts.ReturnExitTile.Y == 0 || pts.ReturnExitTile.Y == 31;
                Assert.True(isBorder);
            }
        }

        [Fact]
        public void AnnexTemplates_FourCornersAndForaging_HaveValidThematicConfiguration()
        {
            var fc = AnnexTemplateCatalog.GetTemplate("FourCorners");
            Assert.Equal("FourCorners", fc.Id);
            Assert.True(fc.Behavior.FourCornersExtras);
            Assert.Equal(40, fc.Width);
            Assert.Equal(32, fc.Height);

            var foraging = AnnexTemplateCatalog.GetTemplate("Foraging");
            Assert.Equal("Foraging", foraging.Id);
            Assert.True(foraging.Behavior.ForestForage);
            Assert.True(foraging.Behavior.ForestStumps);
            Assert.Equal(40, foraging.Width);
            Assert.Equal(32, foraging.Height);
        }

        [Fact]
        public void AnnexTemplates_Fishing_ConfiguresAuthenticCentralIslandBridgeEntrances()
        {
            var fishing = AnnexTemplateCatalog.GetTemplate("Fishing");
            Assert.Equal(22, fishing.SourceCropX);
            Assert.Equal(25, fishing.SourceCropY);

            var northLayout = fishing.GetWindowLayoutForSide(AnnexSide.North);
            Assert.Equal(25, northLayout.EntranceTile.X);
            Assert.Equal(30, northLayout.EntranceTile.Y);

            var eastLayout = fishing.GetWindowLayoutForSide(AnnexSide.East);
            Assert.Equal(1, eastLayout.EntranceTile.X);
            Assert.Equal(8, eastLayout.EntranceTile.Y);

            var southLayout = fishing.GetWindowLayoutForSide(AnnexSide.South);
            Assert.Equal(4, southLayout.EntranceTile.X);
            Assert.Equal(1, southLayout.EntranceTile.Y);
        }

        [Fact]
        public void AnnexTemplates_Foraging_ConfiguresAuthenticForestLayouts()
        {
            var foraging = AnnexTemplateCatalog.GetTemplate("Foraging");
            Assert.Equal("Maps/Farm_Foraging", foraging.SourceVanillaMap);

            var northLayout = foraging.GetWindowLayoutForSide(AnnexSide.North);
            Assert.Equal(26, northLayout.EntranceTile.X);
            Assert.Equal(30, northLayout.EntranceTile.Y);
            Assert.NotNull(northLayout.RenewableStumpTiles);
            Assert.Contains(new TilePoint(8, 18), northLayout.RenewableStumpTiles);

            var eastLayout = foraging.GetWindowLayoutForSide(AnnexSide.East);
            Assert.Equal(1, eastLayout.EntranceTile.X);
            Assert.Equal(14, eastLayout.EntranceTile.Y);
            Assert.NotNull(eastLayout.RenewableStumpTiles);

            var southLayout = foraging.GetWindowLayoutForSide(AnnexSide.South);
            Assert.Equal(26, southLayout.EntranceTile.X);
            Assert.Equal(1, southLayout.EntranceTile.Y);
            Assert.NotNull(southLayout.RenewableStumpTiles);
        }

        [Fact]
        public void AnnexTemplates_Mining_ConfiguresAuthenticHilltopLayouts()
        {
            var mining = AnnexTemplateCatalog.GetTemplate("Mining");
            Assert.Equal("Maps/Farm_Mining", mining.SourceVanillaMap);

            var northLayout = mining.GetWindowLayoutForSide(AnnexSide.North);
            Assert.Equal(24, northLayout.EntranceTile.X);
            Assert.Equal(30, northLayout.EntranceTile.Y);
            Assert.NotNull(northLayout.MountainOreArea);
            Assert.Equal(5, northLayout.MountainOreArea.Value.X);
            Assert.Equal(4, northLayout.MountainOreArea.Value.Y);

            var eastLayout = mining.GetWindowLayoutForSide(AnnexSide.East);
            Assert.Equal(1, eastLayout.EntranceTile.X);
            Assert.Equal(19, eastLayout.EntranceTile.Y);
            Assert.NotNull(eastLayout.MountainOreArea);

            var southLayout = mining.GetWindowLayoutForSide(AnnexSide.South);
            Assert.Equal(15, southLayout.EntranceTile.X);
            Assert.Equal(1, southLayout.EntranceTile.Y);
            Assert.NotNull(southLayout.MountainOreArea);
        }

        [Fact]
        public void AnnexTemplates_Combat_ConfiguresAuthenticWildernessLayouts()
        {
            var combat = AnnexTemplateCatalog.GetTemplate("Combat");
            Assert.Equal("Maps/Farm_Combat", combat.SourceVanillaMap);
            Assert.True(combat.Behavior.NightMonsters);

            var northLayout = combat.GetWindowLayoutForSide(AnnexSide.North);
            Assert.Equal(16, northLayout.EntranceTile.X);
            Assert.Equal(30, northLayout.EntranceTile.Y);

            var eastLayout = combat.GetWindowLayoutForSide(AnnexSide.East);
            Assert.Equal(1, eastLayout.EntranceTile.X);
            Assert.Equal(14, eastLayout.EntranceTile.Y);

            var southLayout = combat.GetWindowLayoutForSide(AnnexSide.South);
            Assert.Equal(16, southLayout.EntranceTile.X);
            Assert.Equal(1, southLayout.EntranceTile.Y);
        }

        [Fact]
        public void AnnexTemplates_FourCorners_ConfiguresAuthenticCrossroadsLayouts()
        {
            var fourCorners = AnnexTemplateCatalog.GetTemplate("FourCorners");
            Assert.Equal("Maps/Farm_FourCorners", fourCorners.SourceVanillaMap);
            Assert.True(fourCorners.Behavior.FourCornersExtras);
            Assert.Equal(20, fourCorners.SourceCropX);
            Assert.Equal(24, fourCorners.SourceCropY);
            Assert.NotNull(fourCorners.Behavior.MountainOreArea);

            var northLayout = fourCorners.GetWindowLayoutForSide(AnnexSide.North);
            Assert.Equal(13, northLayout.EntranceTile.X);
            Assert.Equal(30, northLayout.EntranceTile.Y);
            Assert.Equal(20, northLayout.CropX);
            Assert.Equal(24, northLayout.CropY);

            var eastLayout = fourCorners.GetWindowLayoutForSide(AnnexSide.East);
            Assert.Equal(1, eastLayout.EntranceTile.X);
            Assert.Equal(5, eastLayout.EntranceTile.Y);
            Assert.Equal(20, eastLayout.CropX);
            Assert.Equal(24, eastLayout.CropY);

            var southLayout = fourCorners.GetWindowLayoutForSide(AnnexSide.South);
            Assert.Equal(13, southLayout.EntranceTile.X);
            Assert.Equal(1, southLayout.EntranceTile.Y);
            Assert.Equal(20, southLayout.CropX);
            Assert.Equal(24, southLayout.CropY);
        }

        [Fact]
        public void AnnexTemplates_Ranching_ConfiguresAuthenticMeadowlandsLayouts()
        {
            var ranching = AnnexTemplateCatalog.GetTemplate("Ranching");
            Assert.Equal("Maps/Farm_Ranching", ranching.SourceVanillaMap);
            Assert.Equal(40, ranching.SourceCropX);
            Assert.Equal(0, ranching.SourceCropY);

            var northLayout = ranching.GetWindowLayoutForSide(AnnexSide.North);
            Assert.Equal(23, northLayout.EntranceTile.X);
            Assert.Equal(30, northLayout.EntranceTile.Y);
            Assert.Equal(40, northLayout.CropX);
            Assert.Equal(0, northLayout.CropY);

            var eastLayout = ranching.GetWindowLayoutForSide(AnnexSide.East);
            Assert.Equal(1, eastLayout.EntranceTile.X);
            Assert.Equal(20, eastLayout.EntranceTile.Y);
            Assert.Equal(40, eastLayout.CropX);
            Assert.Equal(0, eastLayout.CropY);

            var southLayout = ranching.GetWindowLayoutForSide(AnnexSide.South);
            Assert.Equal(23, southLayout.EntranceTile.X);
            Assert.Equal(1, southLayout.EntranceTile.Y);
            Assert.Equal(40, southLayout.CropX);
            Assert.Equal(0, southLayout.CropY);
        }

        [Fact]
        public void AnnexTemplates_Beach_ConfiguresAuthenticIslandLayouts()
        {
            var beach = AnnexTemplateCatalog.GetTemplate("Beach");
            Assert.Equal("Maps/Farm_Island", beach.SourceVanillaMap);
            Assert.Equal(29, beach.SourceCropX);
            Assert.Equal(0, beach.SourceCropY);

            var northLayout = beach.GetWindowLayoutForSide(AnnexSide.North);
            Assert.Equal(11, northLayout.EntranceTile.X);
            Assert.Equal(30, northLayout.EntranceTile.Y);
            Assert.Equal(29, northLayout.CropX);
            Assert.Equal(0, northLayout.CropY);

            var eastLayout = beach.GetWindowLayoutForSide(AnnexSide.East);
            Assert.Equal(1, eastLayout.EntranceTile.X);
            Assert.Equal(20, eastLayout.EntranceTile.Y);
            Assert.Equal(29, eastLayout.CropX);
            Assert.Equal(0, eastLayout.CropY);

            var southLayout = beach.GetWindowLayoutForSide(AnnexSide.South);
            Assert.Equal(11, southLayout.EntranceTile.X);
            Assert.Equal(1, southLayout.EntranceTile.Y);
            Assert.Equal(29, southLayout.CropX);
            Assert.Equal(0, southLayout.CropY);
        }

        [Fact]
        public void AnnexValidation_PassableBridgeOverWater_PassesClearanceValidation()
        {
            const int w = 40;
            const int h = 32;
            var back = new bool[w, h];
            var passable = new bool[w, h];
            var water = new bool[w, h];

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    back[x, y] = true;
                    bool isBorder = x == 0 || x == w - 1 || y == 0 || y == h - 1;
                    passable[x, y] = !isBorder;
                }
            }

            // Simulamos el puente norte de Riverland (x = 2..3, y = 0..4) sobre canal de agua
            // El canal de agua abarca y = 0..3
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y <= 3; y++)
                {
                    water[x, y] = true;
                    // El agua fuera del puente es intransitable
                    if (x < 2 || x > 3)
                    {
                        passable[x, y] = false;
                    }
                }
            }

            // Apertura de entrada por el norte en x=2..3, y=0 (borde)
            passable[2, 0] = true;
            passable[3, 0] = true;
            // Pasarela de puente transitable sobre el agua
            passable[2, 1] = true;
            passable[3, 1] = true;
            passable[2, 2] = true;
            passable[3, 2] = true;

            var data = new AnnexMapInspectionData(
                TemplateId: "Fishing",
                Side: AnnexSide.South,
                Width: w,
                Height: h,
                EntryTile: new TilePoint(2, 1),
                BackTilePresent: back,
                IsPassable: passable,
                IsWater: water
            );

            var report = AnnexValidationLogic.ValidateAnnex(data);
            Assert.True(report.IsValid);
            Assert.DoesNotContain(report.Issues, i => i.Code == "CLEARANCE_WATER");
            Assert.DoesNotContain(report.Issues, i => i.Code == "CLEARANCE_BLOCKED");
        }
    }
}
