using System;
using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Core.Localization;

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
                return CoreI18n.Get("location.host_farm");

            if (string.Equals(locationName, HostPlazaLayout.HostFarm, StringComparison.OrdinalIgnoreCase))
                return CoreI18n.Get("location.host_farm");

            for (int i = 1; i <= HostPlazaLayout.MaxBridges; i++)
            {
                if (string.Equals(locationName, HostPlazaLayout.BridgeName(i), StringComparison.OrdinalIgnoreCase))
                    return CoreI18n.Get("location.bridge", new { number = i });
            }

            return locationName;
        }

        public static string GetDoorDescription(string doorId, string? fallbackDescription = null)
        {
            string key = $"door.{doorId}.description";
            string localized = CoreI18n.Get(key);
            return string.Equals(localized, key, StringComparison.OrdinalIgnoreCase)
                ? (fallbackDescription ?? string.Empty)
                : localized;
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
                string resolvedDesc = GetDoorDescription(d.DoorId, desc);
                entries.Add(new CatalogEntry(
                    d.DoorId,
                    d.LocationName,
                    locDisp,
                    d.X,
                    d.Y,
                    d.DefaultSlotId,
                    resolvedDesc,
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
                return CoreI18n.Get("catalog.free", new
                {
                    door = entry.DoorId,
                    location = entry.LocationDisplayName,
                    x = entry.TileX,
                    y = entry.TileY,
                    desc = entry.Description
                });
            }
            string owner = !string.IsNullOrWhiteSpace(entry.OwnerName) ? entry.OwnerName : CoreI18n.Get("common.neighbor");
            return CoreI18n.Get("catalog.occupied", new
            {
                door = entry.DoorId,
                owner,
                location = entry.LocationDisplayName,
                x = entry.TileX,
                y = entry.TileY
            });
        }

        /// <summary>
        /// Líneas de /residencias en el chat, que no tiene scroll: solo las ocupadas y un resumen de las libres.
        /// </summary>
        public static IReadOnlyList<string> FormatChatSummary(IReadOnlyList<CatalogEntry> entries)
        {
            var lines = new List<string> { CoreI18n.Get("catalog.header") };
            foreach (CatalogEntry entry in entries.Where(e => e.IsClaimed))
            {
                string owner = !string.IsNullOrWhiteSpace(entry.OwnerName) ? entry.OwnerName : CoreI18n.Get("common.neighbor");
                string type = string.IsNullOrWhiteSpace(entry.FarmType) ? string.Empty : $", {entry.FarmType}";
                lines.Add(CoreI18n.Get("catalog.summary_line", new
                {
                    door = entry.DoorId,
                    owner,
                    type,
                    location = entry.LocationDisplayName
                }));
            }

            int claimed = entries.Count(e => e.IsClaimed);
            int free = entries.Count - claimed;
            if (claimed == 0)
                lines.Add(CoreI18n.Get("catalog.none_occupied"));
            lines.Add(free > 0
                ? CoreI18n.Get("catalog.prompt_walk", new { occupied = claimed, free })
                : CoreI18n.Get("catalog.none_free", new { occupied = claimed }));
            return lines;
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
