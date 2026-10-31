using System;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Gestiona la correspondencia espacial inmersiva entre la Plaza Central (Granja Host)
    /// y las residencias de los jugadores (Pelican4Ever_Farm_XX).
    /// </summary>
    public static class SpatialWarpRouter
    {
        /// <summary>
        /// Calcula las coordenadas y orientación de entrada a la residencia privada según la ubicación del portal en la Plaza Central.
        /// - Portales del SUR del Host (TownTileY >= 60) -> Entra por el NORTE de la residencia (salida Backwoods), mirando al Sur (2).
        /// - Portales del NORTE del Host (TownTileY <= 2) -> Entra por el SUR de la residencia (salida Forest), mirando al Norte (0).
        /// - Portales del OESTE del Host (TownTileX <= 2) -> Entra por el ESTE de la residencia (salida BusStop), mirando al Oeste (3).
        /// </summary>
        public static void GetResidenceArrivalCoordinates(int townTileX, int townTileY, out int targetX, out int targetY, out int facingDirection)
        {
            if (townTileY >= 60)
            {
                targetX = 40;
                targetY = 1;
                facingDirection = 2; // Sur
            }
            else if (townTileY <= 2)
            {
                targetX = 40;
                targetY = 63;
                facingDirection = 0; // Norte
            }
            else if (townTileX <= 2)
            {
                targetX = 78;
                targetY = 17;
                facingDirection = 3; // Oeste
            }
            else
            {
                targetX = 78;
                targetY = 17;
                facingDirection = 3; // Oeste
            }
        }

        /// <summary>
        /// Determina si un warp que sale de la residencia privada debe volver a la Plaza Central (Granja Host)
        /// o continuar hacia el mapa vanilla correspondiente.
        /// </summary>
        public static bool ShouldReturnToHostFarm(int townTileX, int townTileY, string exitTargetLocation)
        {
            if (string.IsNullOrWhiteSpace(exitTargetLocation)) return false;

            if (townTileY >= 60)
            {
                // Portal en el SUR del Host: la salida de ARRIBA (Backwoods / Mountain) vuelve al Host
                return exitTargetLocation.Equals("Backwoods", StringComparison.OrdinalIgnoreCase)
                    || exitTargetLocation.Equals("Mountain", StringComparison.OrdinalIgnoreCase);
            }
            else if (townTileY <= 2)
            {
                // Portal en el NORTE del Host: la salida de ABAJO (Forest) vuelve al Host
                return exitTargetLocation.Equals("Forest", StringComparison.OrdinalIgnoreCase);
            }
            else if (townTileX <= 2)
            {
                // Portal en el OESTE del Host: la salida de la DERECHA (BusStop) vuelve al Host
                return exitTargetLocation.Equals("BusStop", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                // Portal en el ESTE del Host: la salida de la DERECHA (BusStop) vuelve al Host
                return exitTargetLocation.Equals("BusStop", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Calcula la casilla de reaparición y orientación al volver a la Plaza Central (Granja Host)
        /// para que el jugador aparezca justo frente a su portal, caminando hacia el interior de la plaza.
        /// </summary>
        public static void GetHostFarmReturnCoordinates(int townTileX, int townTileY, out int hostTileX, out int hostTileY, out int facingDirection)
        {
            if (townTileX <= 2)
            {
                hostTileX = townTileX + 2;
                hostTileY = townTileY;
                facingDirection = 1; // Mirando hacia el Este (hacia la plaza)
            }
            else if (townTileX >= 75)
            {
                hostTileX = townTileX - 2;
                hostTileY = townTileY;
                facingDirection = 3; // Mirando hacia el Oeste
            }
            else if (townTileY <= 2)
            {
                hostTileX = townTileX;
                hostTileY = townTileY + 2;
                facingDirection = 2; // Mirando hacia el Sur (hacia la plaza)
            }
            else
            {
                hostTileX = townTileX;
                hostTileY = townTileY - 2;
                facingDirection = 0; // Mirando hacia el Norte (hacia la plaza)
            }
        }

        /// <summary>
        /// Determina si un portal está ubicado en la ribera sur de la Granja Host (TownTileY >= 60).
        /// </summary>
        public static bool IsSouthBorderPortal(int townTileY) => townTileY >= 60;

        /// <summary>
        /// Determina si un tile de copa de árbol (hojas en tilesheet) debe conservarse visible en AlwaysFront
        /// por encima del camino a las residencias para que no quede cortado y el jugador camine bajo él sin bloqueo.
        /// </summary>
        public static bool ShouldPreserveCanopyOverPath(int townTileY, int tileIndex)
        {
            return IsSouthBorderPortal(townTileY) || tileIndex < 150;
        }
    }
}
