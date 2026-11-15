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
        public const string TownAnchor = "Town";
        public const string BridgePrefix = "Pelican4Ever_Bridge_";
        public const int BaseFarmSlotCount = 16;
        public const int MaxBridges = 15;
        public const int MaxWestBridges = 3;

        // Hallazgo empírico sobre Maps/Town.xnb (130x110) mediante TownInspect:
        // - Este-Herrería (puentes pares 4,6,8,10,12,14): filas y=81..82 (inmediatamente al este de la herrería de Clint).
        // - Este-Museo (puentes impares 5,7,9,11,13,15): filas y=92..93 (inmediatamente al este del Museo).
        public const int TownEastBlacksmithGateFirstRow = 81;
        public const int TownEastBlacksmithGateRowCount = 2;
        public const int TownEastBlacksmithArrivalX = 128;
        public const int TownEastBlacksmithArrivalY = 81;

        public const int TownEastMuseumGateFirstRow = 92;
        public const int TownEastMuseumGateRowCount = 2;
        public const int TownEastMuseumArrivalX = 128;
        public const int TownEastMuseumArrivalY = 92;

        public static string BridgeName(int n)
        {
            if (n < 1 || n > MaxBridges)
                throw new ArgumentOutOfRangeException(nameof(n), n, $"El número de puente debe estar entre 1 y {MaxBridges}.");
            return $"{BridgePrefix}{n}";
        }

        public static bool TryParseBridgeNumber(string? name, out int bridgeNumber)
        {
            bridgeNumber = 0;
            if (string.IsNullOrWhiteSpace(name) || !name.StartsWith(BridgePrefix, StringComparison.OrdinalIgnoreCase))
                return false;
            return int.TryParse(name.AsSpan(BridgePrefix.Length), out bridgeNumber) && bridgeNumber >= 1 && bridgeNumber <= MaxBridges;
        }

        public static IReadOnlyList<string> AllBridgeNames { get; } = CreateAllBridgeNames();

        private static string[] CreateAllBridgeNames()
        {
            var names = new string[MaxBridges];
            for (int i = 0; i < MaxBridges; i++)
                names[i] = $"{BridgePrefix}{i + 1}";
            return names;
        }

        public static int BridgeCount(int farmSlotCount)
        {
            if (farmSlotCount <= 4)
                return 0;
            return Math.Clamp((int)Math.Ceiling((farmSlotCount - 4) / 4.0), 0, MaxBridges);
        }

        /// <summary>
        /// Devuelve el destino seguro de retorno en el ancla correspondiente (Farm o Town) cuando
        /// un puente queda fuera de la cadena activa.
        /// </summary>
        public static (string LocationName, int X, int Y, int Facing) GetAnchorReturnTarget(string? bridgeOrLocationName)
        {
            if (TryParseBridgeNumber(bridgeOrLocationName, out int n) && n >= 4)
            {
                return n % 2 == 0
                    ? (TownAnchor, TownEastBlacksmithArrivalX, TownEastBlacksmithArrivalY, 3)
                    : (TownAnchor, TownEastMuseumArrivalX, TownEastMuseumArrivalY, 3);
            }

            return (HostFarm, 64, 16, 2);
        }

        /// <summary>
        /// Desescalado seguro (ADR-0010, punto 7): no reduce las residencias por debajo de la
        /// residencia reclamada más alta; devuelve el menor de los valores admitidos (16, 32, 64)
        /// que la conserva cuando <paramref name="configuredSlotCount"/> es insuficiente.
        /// </summary>
        public static int GetEffectiveFarmSlotCount(int configuredSlotCount, IEnumerable<string?>? claimedSlotIds)
        {
            int highestClaimed = 0;
            if (claimedSlotIds != null)
            {
                foreach (string? slotId in claimedSlotIds)
                {
                    if (string.IsNullOrWhiteSpace(slotId))
                        continue;
                    int idx = slotId.LastIndexOf('_');
                    if (idx >= 0 && int.TryParse(slotId.AsSpan(idx + 1), out int num) && num > highestClaimed)
                        highestClaimed = num;
                }
            }

            if (configuredSlotCount >= highestClaimed)
                return configuredSlotCount;

            if (highestClaimed <= BaseFarmSlotCount)
                return BaseFarmSlotCount;
            if (highestClaimed <= 32)
                return 32;
            return 64;
        }

        public static IReadOnlyList<PlazaLocation> Chain(int farmSlotCount)
        {
            int bridges = BridgeCount(farmSlotCount);
            int westBridges = Math.Min(bridges, MaxWestBridges);
            var locations = new List<PlazaLocation>(bridges + 1);

            // Granja Host: 80x65, NorthPathDepth = 10 (acantilado hasta y=7/9), SouthPathStartOffset = 3
            PlazaPassage? farmWest = westBridges > 0
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

            // Cadena Oeste: Puentes 1..3 (sin cambios respecto a v0.4)
            for (int n = 1; n <= westBridges; n++)
            {
                PlazaPassage east = n == 1
                    ? new PlazaPassage(PortalEdge.East, FirstRow: 22, RowCount: 4, TargetLocation: HostFarm, ArrivalX: 2, ArrivalY: 26, ArrivalFacing: 1)
                    : new PlazaPassage(PortalEdge.East, FirstRow: 22, RowCount: 4, TargetLocation: BridgeName(n - 1), ArrivalX: 1, ArrivalY: 24, ArrivalFacing: 1);

                PlazaPassage? west = n < westBridges
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

            // Cadenas Este ancladas a Town: pares 4,6,8,10,12,14 (Herrería) e impares 5,7,9,11,13,15 (Museo)
            for (int n = 4; n <= bridges; n++)
            {
                PlazaPassage west;
                if (n == 4)
                {
                    west = new PlazaPassage(PortalEdge.West, FirstRow: 22, RowCount: 4, TargetLocation: TownAnchor, ArrivalX: TownEastBlacksmithArrivalX, ArrivalY: TownEastBlacksmithArrivalY, ArrivalFacing: 3);
                }
                else if (n == 5)
                {
                    west = new PlazaPassage(PortalEdge.West, FirstRow: 22, RowCount: 4, TargetLocation: TownAnchor, ArrivalX: TownEastMuseumArrivalX, ArrivalY: TownEastMuseumArrivalY, ArrivalFacing: 3);
                }
                else
                {
                    west = new PlazaPassage(PortalEdge.West, FirstRow: 22, RowCount: 4, TargetLocation: BridgeName(n - 2), ArrivalX: 33, ArrivalY: 24, ArrivalFacing: 3);
                }

                PlazaPassage? east = n + 2 <= bridges
                    ? new PlazaPassage(PortalEdge.East, FirstRow: 22, RowCount: 4, TargetLocation: BridgeName(n + 2), ArrivalX: 1, ArrivalY: 24, ArrivalFacing: 1)
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

        public static PlazaLocation? Find(string? name, int farmSlotCount = BaseFarmSlotCount)
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
