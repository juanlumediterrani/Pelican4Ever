using System;
using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class DoorCatalogValidatorTests
    {
        [Fact]
        public void RealCatalog_PassesValidationWithZeroErrors()
        {
            var specs = DoorCatalogFixture.LoadDoorSpecs();
            Assert.Equal(16, specs.Count);

            var errors = DoorCatalogValidator.Validate(specs, 16);
            Assert.Empty(errors);
        }

        [Fact]
        public void Validate_SouthPortalAtY10OnBridge_ProducesError()
        {
            // En un puente 35×30, South debe estar en y in [27, 29] (Height - 3 .. Height - 1)
            var door = new DoorSpec(
                DoorId: "Test_South_10",
                LocationName: "Pelican4Ever_Bridge_1",
                X: 8,
                Y: 10,
                Edge: PortalEdge.South,
                DefaultSlotId: "Slot_Test_01"
            );

            var errors = DoorCatalogValidator.ValidateOne(door, 16);
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Contains("Portal Sur") && e.Contains("Y=10"));
        }

        [Fact]
        public void Validate_DuplicateDefaultSlotId_ProducesError()
        {
            var doors = new List<DoorSpec>
            {
                new("Door_1", "Farm", 18, 1, PortalEdge.North, "DuplicateSlot"),
                new("Door_2", "Farm", 62, 1, PortalEdge.North, "DuplicateSlot")
            };

            var errors = DoorCatalogValidator.Validate(doors, 16);
            Assert.Contains(errors, e => e.Contains("DefaultSlotId duplicado"));
        }

        [Fact]
        public void Validate_DuplicateDoorId_ProducesError()
        {
            var doors = new List<DoorSpec>
            {
                new("DuplicateDoor", "Farm", 18, 1, PortalEdge.North, "Slot_01"),
                new("DuplicateDoor", "Farm", 62, 1, PortalEdge.North, "Slot_02")
            };

            var errors = DoorCatalogValidator.Validate(doors, 16);
            Assert.Contains(errors, e => e.Contains("DoorId duplicado"));
        }

        [Fact]
        public void Validate_OpeningOutOfWidth_ProducesError()
        {
            // Puente 1 tiene Width = 35. Con X = 34, X + 1 = 35 (fuera de ancho)
            var door = new DoorSpec(
                DoorId: "Test_OutOfWidth",
                LocationName: "Pelican4Ever_Bridge_1",
                X: 34,
                Y: 1,
                Edge: PortalEdge.North,
                DefaultSlotId: "Slot_Test_Width"
            );

            var errors = DoorCatalogValidator.ValidateOne(door, 16);
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Contains("fuera de ancho"));
        }

        [Fact]
        public void Validate_UnknownLocation_ProducesError()
        {
            var door = new DoorSpec(
                DoorId: "Test_UnknownLoc",
                LocationName: "TownSquare",
                X: 10,
                Y: 1,
                Edge: PortalEdge.North,
                DefaultSlotId: "Slot_Test_Loc"
            );

            var errors = DoorCatalogValidator.ValidateOne(door, 16);
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Contains("no pertenece a la plaza"));
        }
    }
}
