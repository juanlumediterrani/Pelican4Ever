using System;
using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class AnnexCompositionAndMigrationTests
    {
        [Fact]
        public void All24AnnexWindowLayouts_UseSingleCanonicalCropAndValidEntrances()
        {
            var templates = AnnexTemplateCatalog.Templates.Values;
            Assert.Equal(8, templates.Count());

            foreach (var template in templates)
            {
                foreach (AnnexSide side in new[] { AnnexSide.North, AnnexSide.East, AnnexSide.South })
                {
                    AnnexWindowLayout layout = template.GetWindowLayoutForSide(side);
                    Assert.NotNull(layout);

                    // Recorte canónico 40×32 unificado para cada tipo de granja (ADR-0007 / TASK-910)
                    Assert.Equal(template.SourceCropX, layout.CropX);
                    Assert.Equal(template.SourceCropY, layout.CropY);

                    // Coordenadas de ventana dentro de los límites del mapa vanilla
                    var exitPassage = VanillaFarmExitTable.Table[template.Id];
                    Assert.InRange(layout.CropX, 0, exitPassage.MapWidth - template.Width);
                    Assert.InRange(layout.CropY, 0, exitPassage.MapHeight - template.Height);

                    // Casilla de llegada interior dentro de 40×32 y alejada de esquinas
                    Assert.InRange(layout.EntranceTile.X, 1, template.Width - 2);
                    Assert.InRange(layout.EntranceTile.Y, 1, template.Height - 2);

                    switch (side)
                    {
                        case AnnexSide.North:
                            Assert.Equal(template.Height - 2, layout.EntranceTile.Y);
                            break;
                        case AnnexSide.South:
                            Assert.Equal(1, layout.EntranceTile.Y);
                            break;
                        case AnnexSide.East:
                            Assert.Equal(1, layout.EntranceTile.X);
                            break;
                    }

                    // Si tiene área de mineral, debe estar contenida en 40×32
                    if (layout.MountainOreArea is { } ore)
                    {
                        Assert.InRange(ore.X, 0, template.Width - 1);
                        Assert.InRange(ore.Y, 0, template.Height - 1);
                        Assert.True(ore.X + ore.Width <= template.Width);
                        Assert.True(ore.Y + ore.Height <= template.Height);
                    }
                }
            }
        }

        [Fact]
        public void AnnexEntrance_All24Cases_PlaceArrivalAndReturnSafely()
        {
            foreach (var template in AnnexTemplateCatalog.Templates.Values)
            {
                foreach (AnnexSide side in new[] { AnnexSide.North, AnnexSide.East, AnnexSide.South })
                {
                    var pts = AnnexEntrance.For(template, side);
                    AnnexWindowLayout layout = template.GetWindowLayoutForSide(side);

                    // EntryTile coincide con la entrada de la ventana
                    Assert.Equal(layout.EntranceTile.X, pts.EntryTile.X);
                    Assert.Equal(layout.EntranceTile.Y, pts.EntryTile.Y);

                    // ReturnExitTile está en el perímetro exterior sin tocar esquinas
                    switch (side)
                    {
                        case AnnexSide.North:
                            Assert.Equal(template.Height - 1, pts.ReturnExitTile.Y);
                            Assert.InRange(pts.ReturnExitTile.X, 1, template.Width - 2);
                            Assert.Equal(0, pts.FacingDirection);
                            break;
                        case AnnexSide.South:
                            Assert.Equal(0, pts.ReturnExitTile.Y);
                            Assert.InRange(pts.ReturnExitTile.X, 1, template.Width - 2);
                            Assert.Equal(2, pts.FacingDirection);
                            break;
                        case AnnexSide.East:
                            Assert.Equal(0, pts.ReturnExitTile.X);
                            Assert.InRange(pts.ReturnExitTile.Y, 1, template.Height - 2);
                            Assert.Equal(1, pts.FacingDirection);
                            break;
                    }
                }
            }
        }

        [Fact]
        public void AnnexMigrationLogic_DetectsDisplacedTilesCorrectly()
        {
            int w = 40;
            int h = 32;
            bool IsPassable(int x, int y) => x >= 10 && x <= 30 && y >= 10 && y <= 25;

            // Fuera de límites
            Assert.True(AnnexMigrationLogic.IsTileDisplaced(w, h, -1, 15, IsPassable));
            Assert.True(AnnexMigrationLogic.IsTileDisplaced(w, h, 45, 15, IsPassable));
            Assert.True(AnnexMigrationLogic.IsTileDisplaced(w, h, 20, -5, IsPassable));
            Assert.True(AnnexMigrationLogic.IsTileDisplaced(w, h, 20, 35, IsPassable));

            // Sobre colisión (fuera de 10..30, 10..25)
            Assert.True(AnnexMigrationLogic.IsTileDisplaced(w, h, 5, 5, IsPassable));
            Assert.True(AnnexMigrationLogic.IsTileDisplaced(w, h, 0, 15, IsPassable));

            // Suelo útil libre
            Assert.False(AnnexMigrationLogic.IsTileDisplaced(w, h, 15, 15, IsPassable));
            Assert.False(AnnexMigrationLogic.IsTileDisplaced(w, h, 20, 20, IsPassable));
        }

        [Fact]
        public void AnnexMigrationLogic_FindsRescueChestPlacementNearEntry()
        {
            int w = 40;
            int h = 32;
            TilePoint entry = new TilePoint(20, 30);

            // Casillas ocupadas / intransitables
            var occupied = new HashSet<TilePoint>
            {
                entry,
                new TilePoint(20, 29) // bloqueada
            };

            bool IsFreeAndPassable(int x, int y)
            {
                return !occupied.Contains(new TilePoint(x, y));
            }

            TileArea clearance = new TileArea(19, 27, 3, 4);
            TilePoint chest = AnnexMigrationLogic.FindRescueChestPlacement(w, h, entry, IsFreeAndPassable, clearance);

            // Nunca en la casilla de llegada, ni en los bordes, ni en el área de despeje interior
            Assert.NotEqual(entry, chest);
            Assert.False(clearance.Contains(chest.X, chest.Y));
            Assert.InRange(chest.X, 1, w - 2);
            Assert.InRange(chest.Y, 1, h - 2);

            // Debe estar cerca (distancia Chebyshev <= 2)
            int dist = Math.Max(Math.Abs(chest.X - entry.X), Math.Abs(chest.Y - entry.Y));
            Assert.InRange(dist, 1, 2);
        }

        [Fact]
        public void AnnexAssignment_DefaultMapVersionIsOne_AndCurrentVersionIsThree()
        {
            var assignment = new AnnexAssignment();
            Assert.Equal(1, assignment.MapVersion);
            Assert.Equal(3, AnnexAssignment.CurrentMapVersion);

            var door = new DoorAssignment { DoorId = "door_01", FarmSlotId = "Farm_01", OwnerId = 12345 };
            var worldState = new ServerWorldState();
            worldState.Doors["door_01"] = door;

            var purchase = AnnexPurchaseLogic.ValidateAndPurchase(
                playerId: 12345,
                residenceSlotId: "Farm_01",
                side: AnnexSide.North,
                templateId: "Standard",
                playerMoney: 50000,
                residenceDoor: door,
                freeSides: new[] { AnnexSide.North, AnnexSide.East },
                worldState: worldState);

            Assert.True(purchase.Success);
            Assert.NotNull(purchase.Assignment);
            Assert.Equal(AnnexAssignment.CurrentMapVersion, purchase.Assignment!.MapVersion);
        }

        [Fact]
        public void ResidenceAnnexSignLogic_GeneratesExpectedSignText()
        {
            Assert.Equal(ResidenceAnnexSignLogic.ForSaleText, ResidenceAnnexSignLogic.GetResidenceSignText(null));

            var annex = new AnnexAssignment { TemplateId = "Fishing" };
            Assert.Equal("Anexo: Remanso Fluvial", ResidenceAnnexSignLogic.GetResidenceSignText(annex));

            var standardAnnex = new AnnexAssignment { TemplateId = "Standard" };
            Assert.Equal("Anexo: Huerto de Cultivo Estándar", ResidenceAnnexSignLogic.GetResidenceSignText(standardAnnex));
        }

        [Fact]
        public void ResidenceAnnexSignLogic_AllFarmTypes_ReturnValidCandidateTiles()
        {
            foreach (var type in VanillaFarmExitTable.Table.Keys)
            {
                var passage = VanillaFarmExitTable.Table[type];
                foreach (AnnexSide side in new[] { AnnexSide.North, AnnexSide.East, AnnexSide.South })
                {
                    var candidates = ResidenceAnnexSignLogic.GetResidenceCandidateTiles(type, side);
                    Assert.NotEmpty(candidates);

                    foreach (var c in candidates)
                    {
                        Assert.InRange(c.X, 0, passage.MapWidth - 1);
                        Assert.InRange(c.Y, 0, passage.MapHeight - 1);
                    }
                }
            }
        }

        [Fact]
        public void ResidenceAnnexSignLogic_AnnexReturnSign_ReturnsValidCandidateTiles()
        {
            foreach (var side in new[] { AnnexSide.North, AnnexSide.East, AnnexSide.South })
            {
                TilePoint entry = side switch
                {
                    AnnexSide.North => new TilePoint(20, 30),
                    AnnexSide.South => new TilePoint(20, 1),
                    _ => new TilePoint(1, 16)
                };

                var candidates = ResidenceAnnexSignLogic.GetAnnexReturnCandidateTiles(side, entry, 40, 32);
                Assert.NotEmpty(candidates);

                foreach (var c in candidates)
                {
                    Assert.InRange(c.X, 1, 38);
                    Assert.InRange(c.Y, 1, 30);
                }
            }
        }

        [Fact]
        public void ResidenceAnnexSignLogic_ChooseFreeSignTile_SelectsFirstAvailable()
        {
            var candidates = new[]
            {
                new TilePoint(10, 10),
                new TilePoint(10, 11),
                new TilePoint(10, 12)
            };

            var occupied = new HashSet<TilePoint>
            {
                new TilePoint(10, 10)
            };

            var selected = ResidenceAnnexSignLogic.ChooseFreeSignTile(candidates, (x, y) => !occupied.Contains(new TilePoint(x, y)));
            Assert.NotNull(selected);
            Assert.Equal(new TilePoint(10, 11), selected!.Value);

            // Si todas están ocupadas
            occupied.Add(new TilePoint(10, 11));
            occupied.Add(new TilePoint(10, 12));
            var none = ResidenceAnnexSignLogic.ChooseFreeSignTile(candidates, (x, y) => !occupied.Contains(new TilePoint(x, y)));
            Assert.Null(none);
        }
    }
}
