using System;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Reglas de dominio de la red de vagonetas conectada a los Puentes de la Plaza (TASK-1501).
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

        /// <summary>
        /// Acción nativa de Stardew Valley 1.6 (<c>MinecartTransport</c>) asignada a la vagoneta del puente.
        /// En <c>Pelican4Ever_Bridge_1</c> excluye la propia Plaza para no duplicar el punto actual en el menú;
        /// en los puentes 2 y 3 ofrece todos los destinos, incluida la vuelta directa al Puente 1.
        /// </summary>
        public static string GetBridgeMinecartAction(string bridgeLocationName)
        {
            if (string.Equals(bridgeLocationName, HostPlazaLayout.BridgeName(1), StringComparison.OrdinalIgnoreCase))
            {
                return $"MinecartTransport {NetworkId} {PlazaDestinationId}";
            }

            return $"MinecartTransport {NetworkId}";
        }
    }
}
