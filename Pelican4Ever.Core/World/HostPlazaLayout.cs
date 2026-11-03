using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    public enum PortalEdge { North, South, West, East }

    public sealed record PlazaPassage(
        PortalEdge Edge,
        int FirstRow,
        int RowCount,
        string TargetLocation,
        int ArrivalX,
        int ArrivalY,
        int ArrivalFacing
    );

    public sealed record PlazaLocation(
        string Name,
        int Width,
        int Height,
        PlazaPassage? West,
        PlazaPassage? East,
        int NorthPathDepth = 4,
        int SouthPathStartOffset = 3
    );

    public static class HostPlazaLayout
    {
        public const string HostFarm = "Farm";
        public const string BridgePrefix = "Pelican4Ever_Bridge_";
        public const int MaxBridges = 3;

        public static string BridgeName(int n)
        {
            if (n < 1 || n > MaxBridges)
                throw new ArgumentOutOfRangeException(nameof(n), n, $"El número de puente debe estar entre 1 y {MaxBridges}.");
            return $"{BridgePrefix}{n}";
        }

        public static IReadOnlyList<string> AllBridgeNames { get; } = new[]
        {
            $"{BridgePrefix}1",
            $"{BridgePrefix}2",
            $"{BridgePrefix}3"
        };

        public static int BridgeCount(int farmSlotCount)
        {
            if (farmSlotCount <= 4)
                return 0;
            return Math.Clamp((int)Math.Ceiling((farmSlotCount - 4) / 4.0), 0, MaxBridges);
        }

        public static IReadOnlyList<PlazaLocation> Chain(int farmSlotCount)
        {
            int bridges = BridgeCount(farmSlotCount);
            var locations = new List<PlazaLocation>(bridges + 1);

            // Granja Host: 80x65, NorthPathDepth = 10 (acantilado hasta y=7/9), SouthPathStartOffset = 3
            PlazaPassage? farmWest = bridges > 0
                ? new PlazaPassage(PortalEdge.West, FirstRow: 26, RowCount: 2, TargetLocation: BridgeName(1), ArrivalX: 33, ArrivalY: 24, ArrivalFacing: 3)
                : null;

            locations.Add(new PlazaLocation(
                Name: HostFarm,
                Width: 80,
                Height: 65,
                West: farmWest,
                East: null,
                NorthPathDepth: 10,
                SouthPathStartOffset: 3
            ));

            for (int n = 1; n <= bridges; n++)
            {
                // En los puentes (recorte Maps/BusStop 35x30), la salida natural vanilla es la carretera este-oeste (y = 22..25).
                PlazaPassage east = n == 1
                    ? new PlazaPassage(PortalEdge.East, FirstRow: 22, RowCount: 4, TargetLocation: HostFarm, ArrivalX: 2, ArrivalY: 26, ArrivalFacing: 1)
                    : new PlazaPassage(PortalEdge.East, FirstRow: 22, RowCount: 4, TargetLocation: BridgeName(n - 1), ArrivalX: 1, ArrivalY: 24, ArrivalFacing: 1);

                PlazaPassage? west = n < bridges
                    ? new PlazaPassage(PortalEdge.West, FirstRow: 22, RowCount: 4, TargetLocation: BridgeName(n + 1), ArrivalX: 33, ArrivalY: 24, ArrivalFacing: 3)
                    : null;

                locations.Add(new PlazaLocation(
                    Name: BridgeName(n),
                    Width: 35,
                    Height: 30,
                    West: west,
                    East: east,
                    NorthPathDepth: 5,
                    SouthPathStartOffset: 3
                ));
            }

            return locations;
        }

        public static bool IsPlazaLocation(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (string.Equals(name, HostFarm, StringComparison.OrdinalIgnoreCase))
                return true;

            for (int i = 1; i <= MaxBridges; i++)
            {
                if (string.Equals(name, BridgeName(i), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static PlazaLocation? Find(string? name, int farmSlotCount = 16)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            foreach (var loc in Chain(farmSlotCount))
            {
                if (string.Equals(loc.Name, name, StringComparison.OrdinalIgnoreCase))
                    return loc;
            }

            return null;
        }
    }
}
