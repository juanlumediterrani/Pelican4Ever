using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    public sealed record BridgeMinecartDestination(
        string DestinationId,
        int BridgeNumber,
        string BridgeLocationName);

    /// <summary>
    /// Reglas de dominio de la red de vagonetas conectada a los Puentes de la Plaza (TASK-1501, TASK-2405).
    /// En el recorte <c>Maps/BusStop</c> (<c>srcX = 10, srcY = 0</c>), la vagoneta queda en (4, 3)
    /// y la casilla de llegada frontal equivale a vanilla (14, 4) → (4, 4).
    /// </summary>
    public static class MinecartPlazaLogic
    {
        public const string NetworkId = "Default";
        public const string PlazaDestinationId = "Pelican4Ever_Plaza";
        public const int BridgeMinecartTileX = 4;
        public const int BridgeMinecartTileY = 3;
        public const int BridgeArrivalTileX = 4;
        public const int BridgeArrivalTileY = 4;
        public const int BridgeArrivalFacingDirection = 2;
        public const string BridgeArrivalDirectionName = "down";

        /// <summary>
        /// Determina si la Plaza dispone al menos del Puente 1 para exponerse como destino en la red de vagonetas.
        /// </summary>
        public static bool ShouldIncludePlazaDestination(int farmSlotCount)
        {
            return HostPlazaLayout.BridgeCount(farmSlotCount) >= 1;
        }

        public static string GetDestinationIdForBridge(int bridgeNumber)
        {
            return bridgeNumber == 1 ? PlazaDestinationId : HostPlazaLayout.BridgeName(bridgeNumber);
        }

        /// <summary>
        /// Devuelve todos los destinos de vagoneta activos en la Plaza (Puente 1 + puentes del este 4..N).
        /// </summary>
        public static IReadOnlyList<BridgeMinecartDestination> GetActiveMinecartDestinations(int farmSlotCount)
        {
            int bridges = HostPlazaLayout.BridgeCount(farmSlotCount);
            if (bridges < 1)
                return Array.Empty<BridgeMinecartDestination>();

            var list = new List<BridgeMinecartDestination>
            {
                new(PlazaDestinationId, 1, HostPlazaLayout.BridgeName(1))
            };

            for (int n = 4; n <= bridges; n++)
            {
                list.Add(new BridgeMinecartDestination(
                    GetDestinationIdForBridge(n),
                    n,
                    HostPlazaLayout.BridgeName(n)));
            }

            return list;
        }

        /// <summary>
        /// Acción nativa de Stardew Valley 1.6 (<c>MinecartTransport</c>) asignada a la vagoneta del puente.
        /// En <c>Pelican4Ever_Bridge_1</c> y en los puentes del este (<c>4..15</c>) excluye el propio destino
        /// para no duplicar el punto actual en el menú.
        /// </summary>
        public static string GetBridgeMinecartAction(string bridgeLocationName)
        {
            if (HostPlazaLayout.TryParseBridgeNumber(bridgeLocationName, out int n))
            {
                if (n == 1)
                    return $"MinecartTransport {NetworkId} {PlazaDestinationId}";
                if (n >= 4)
                    return $"MinecartTransport {NetworkId} {GetDestinationIdForBridge(n)}";
            }

            return $"MinecartTransport {NetworkId}";
        }
    }
}
