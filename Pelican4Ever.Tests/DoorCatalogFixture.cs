using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pelican4Ever.Core.World;

namespace Pelican4Ever.Tests
{
    public sealed class DoorCatalogEntry
    {
        public string DoorId { get; set; } = string.Empty;
        public string LocationName { get; set; } = string.Empty;
        public int TownTileX { get; set; }
        public int TownTileY { get; set; }
        public int? MailboxTileX { get; set; }
        public int? MailboxTileY { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public PortalEdge Edge { get; set; }
        public string DefaultSlotId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public DoorSpec ToDoorSpec() => new(
            DoorId,
            LocationName,
            TownTileX,
            TownTileY,
            Edge,
            DefaultSlotId,
            MailboxTileX,
            MailboxTileY
        );
    }

    public static class DoorCatalogFixture
    {
        private static IReadOnlyList<DoorCatalogEntry>? _cachedDoors;

        public static string FindDoorsJsonPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "Pelican4Ever", "assets", "doors.json");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }

            throw new FileNotFoundException("No se encontró Pelican4Ever/assets/doors.json subiendo desde " + AppContext.BaseDirectory);
        }

        public static IReadOnlyList<DoorCatalogEntry> LoadDoors()
        {
            if (_cachedDoors != null)
                return _cachedDoors;

            string path = FindDoorsJsonPath();
            string json = File.ReadAllText(path);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            options.Converters.Add(new JsonStringEnumConverter());
            var list = JsonSerializer.Deserialize<List<DoorCatalogEntry>>(json, options)
                ?? throw new InvalidOperationException("Falló la deserialización de doors.json");

            _cachedDoors = list;
            return _cachedDoors;
        }

        public static IReadOnlyList<DoorSpec> LoadDoorSpecs()
        {
            var doors = LoadDoors();
            var specs = new List<DoorSpec>(doors.Count);
            foreach (var d in doors)
                specs.Add(d.ToDoorSpec());
            return specs;
        }
    }
}
