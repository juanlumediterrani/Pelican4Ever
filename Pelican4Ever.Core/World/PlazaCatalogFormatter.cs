using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.World
{
    public sealed record CatalogEntry(
        string DoorId,
        string LocationName,
        string LocationDisplayName,
        int TileX,
        int TileY,
        string DefaultSlotId,
        string Description,
        bool IsClaimed,
        string? OwnerName,
        string? FarmType
    );

    /// <summary>
    /// Helper puro en Core para el filtrado, nombrado amigable y formateo de residencias
    /// en los comandos /residencias, /doors y p4e_doors según FarmSlotCount (TASK-1304).
    /// </summary>
    public static class PlazaCatalogFormatter
    {
        public static string GetLocationDisplayName(string? locationName)
        {
            if (string.IsNullOrWhiteSpace(locationName))
                return "Granja Host";

            if (string.Equals(locationName, HostPlazaLayout.HostFarm, StringComparison.OrdinalIgnoreCase))
                return "Granja Host";

            for (int i = 1; i <= HostPlazaLayout.MaxBridges; i++)
            {
                if (string.Equals(locationName, HostPlazaLayout.BridgeName(i), StringComparison.OrdinalIgnoreCase))
                    return $"Puente {i}";
            }

            return locationName;
        }

        public static int ExtractSlotNumber(string defaultSlotId)
        {
            if (string.IsNullOrWhiteSpace(defaultSlotId))
                return int.MaxValue;

            int lastUnderscore = defaultSlotId.LastIndexOf('_');
            if (lastUnderscore >= 0 && int.TryParse(defaultSlotId.Substring(lastUnderscore + 1), out int num))
                return num;

            return int.MaxValue;
        }

        public static bool IsDoorActive(string? defaultSlotId, string? locationName, int farmSlotCount)
        {
            if (string.IsNullOrWhiteSpace(defaultSlotId))
                return false;

            int slotNum = ExtractSlotNumber(defaultSlotId);
            if (slotNum < 1 || slotNum > farmSlotCount)
                return false;

            string resolvedLocation = string.IsNullOrWhiteSpace(locationName) ? HostPlazaLayout.HostFarm : locationName;
            return HostPlazaLayout.Find(resolvedLocation, farmSlotCount) != null;
        }

        public static IReadOnlyList<DoorSpec> FilterActiveDoors(IEnumerable<DoorSpec> doors, int farmSlotCount)
        {
            return doors
                .Where(d => IsDoorActive(d.DefaultSlotId, d.LocationName, farmSlotCount))
                .OrderBy(d => ExtractSlotNumber(d.DefaultSlotId))
                .ToList();
        }

        public static IReadOnlyList<CatalogEntry> BuildCatalogEntries(
            IEnumerable<DoorSpec> doors,
            int farmSlotCount,
            Func<string, (bool isClaimed, string? ownerName, string? farmType, string? description)> statusLookup)
        {
            var activeDoors = FilterActiveDoors(doors, farmSlotCount);
            var entries = new List<CatalogEntry>(activeDoors.Count);

            foreach (var d in activeDoors)
            {
                var (isClaimed, ownerName, farmType, desc) = statusLookup(d.DoorId);
                string locDisp = GetLocationDisplayName(d.LocationName);
                entries.Add(new CatalogEntry(
                    d.DoorId,
                    d.LocationName,
                    locDisp,
                    d.X,
                    d.Y,
                    d.DefaultSlotId,
                    desc ?? string.Empty,
                    isClaimed,
                    ownerName,
                    farmType
                ));
            }

            return entries;
        }

        public static string FormatChatLine(CatalogEntry entry)
        {
            if (!entry.IsClaimed)
            {
                return $"[Libre] {entry.DoorId}: {entry.LocationDisplayName} ({entry.TileX},{entry.TileY}) - {entry.Description}";
            }
            string owner = !string.IsNullOrWhiteSpace(entry.OwnerName) ? entry.OwnerName : "Vecino";
            return $"[Ocupada] {entry.DoorId} ({owner}): {entry.LocationDisplayName} ({entry.TileX},{entry.TileY})";
        }

        public static string FormatConsoleLine(CatalogEntry entry)
        {
            string status = entry.IsClaimed
                ? $"Ocupada por {entry.OwnerName ?? "Vecino"}{(string.IsNullOrEmpty(entry.FarmType) ? "" : $" ({entry.FarmType})")}"
                : "LIBRE";
            return $"  {entry.DoorId} [{status}] en {entry.LocationDisplayName} ({entry.TileX},{entry.TileY}) - {entry.Description}";
        }
    }
}
