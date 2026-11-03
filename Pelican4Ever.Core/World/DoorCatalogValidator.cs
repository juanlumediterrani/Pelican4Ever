using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    public sealed record DoorSpec(
        string DoorId,
        string LocationName,
        int X,
        int Y,
        PortalEdge Edge,
        string DefaultSlotId,
        int? MailboxX = null,
        int? MailboxY = null
    );

    public static class DoorCatalogValidator
    {
        public static IReadOnlyList<string> ValidateOne(DoorSpec door, int farmSlotCount = 16)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(door.DoorId))
                errors.Add("DoorId no puede estar vacío.");

            if (string.IsNullOrWhiteSpace(door.DefaultSlotId))
                errors.Add("DefaultSlotId no puede estar vacío.");

            var loc = HostPlazaLayout.Find(door.LocationName, farmSlotCount);
            if (loc == null)
            {
                errors.Add($"La ubicación '{door.LocationName}' no pertenece a la plaza (farmSlotCount={farmSlotCount}).");
                return errors;
            }

            switch (door.Edge)
            {
                case PortalEdge.North:
                    if (door.Y < 0 || door.Y > 2)
                        errors.Add($"Portal Norte '{door.DoorId}' tiene Y={door.Y}, debe estar entre 0 y 2.");
                    if (door.X < 0 || door.X + 1 >= loc.Width)
                        errors.Add($"Portal Norte '{door.DoorId}' apertura fuera de ancho [0, {loc.Width - 1}]: X={door.X}.");
                    break;

                case PortalEdge.South:
                    if (door.Y < loc.Height - 3 || door.Y >= loc.Height)
                        errors.Add($"Portal Sur '{door.DoorId}' tiene Y={door.Y}, debe estar entre {loc.Height - 3} y {loc.Height - 1}.");
                    if (door.X < 0 || door.X + 1 >= loc.Width)
                        errors.Add($"Portal Sur '{door.DoorId}' apertura fuera de ancho [0, {loc.Width - 1}]: X={door.X}.");
                    break;

                case PortalEdge.West:
                    if (door.X < 0 || door.X > 2)
                        errors.Add($"Portal Oeste '{door.DoorId}' tiene X={door.X}, debe estar entre 0 y 2.");
                    if (door.Y < 0 || door.Y + 1 >= loc.Height)
                        errors.Add($"Portal Oeste '{door.DoorId}' apertura fuera de alto [0, {loc.Height - 1}]: Y={door.Y}.");
                    break;

                case PortalEdge.East:
                    if (door.X < loc.Width - 3 || door.X >= loc.Width)
                        errors.Add($"Portal Este '{door.DoorId}' tiene X={door.X}, debe estar entre {loc.Width - 3} y {loc.Width - 1}.");
                    if (door.Y < 0 || door.Y + 1 >= loc.Height)
                        errors.Add($"Portal Este '{door.DoorId}' apertura fuera de alto [0, {loc.Height - 1}]: Y={door.Y}.");
                    break;

                default:
                    errors.Add($"Borde desconocido: {door.Edge}.");
                    break;
            }

            return errors;
        }

        public static IReadOnlyList<string> Validate(IEnumerable<DoorSpec> doors, int farmSlotCount = 16)
        {
            var errors = new List<string>();
            var seenDoorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenSlotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var door in doors)
            {
                if (!string.IsNullOrWhiteSpace(door.DoorId) && !seenDoorIds.Add(door.DoorId))
                    errors.Add($"DoorId duplicado: '{door.DoorId}'.");

                if (!string.IsNullOrWhiteSpace(door.DefaultSlotId) && !seenSlotIds.Add(door.DefaultSlotId))
                    errors.Add($"DefaultSlotId duplicado: '{door.DefaultSlotId}'.");

                errors.AddRange(ValidateOne(door, farmSlotCount));
            }

            return errors;
        }
    }
}
