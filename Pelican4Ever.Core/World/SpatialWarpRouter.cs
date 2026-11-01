using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Gestiona la correspondencia espacial inmersiva entre la Plaza Central (Granja Host)
    /// y las residencias de los jugadores (Pelican4Ever_Farm_XX).
    /// </summary>
    public static class SpatialWarpRouter
    {
        /// <summary>
        /// Calcula las coordenadas y orientación de entrada a la residencia privada según la ubicación del portal en la Plaza Central
        /// y el tipo de granja de la residencia (TASK-602 / Praderas azules).
        /// - Portales del SUR del Host (TownTileY >= 60) -> Entra por el NORTE de la residencia (salida Backwoods), mirando al Sur (2).
        /// - Portales del NORTE del Host (TownTileY <= 2) -> Entra por el SUR de la residencia (salida Forest), mirando al Norte (0).
        /// - Portales del OESTE del Host (TownTileX <= 2) -> Entra por el ESTE de la residencia (salida BusStop), mirando al Oeste (3).
        /// </summary>
        public static void GetResidenceArrivalCoordinates(int townTileX, int townTileY, string? farmType, out int targetX, out int targetY, out int facingDirection)
        {
            FarmTemplate template = FarmTemplateCatalog.GetTemplate(farmType);

            if (townTileY >= 60)
            {
                targetX = template.NorthArrivalTileX;
                targetY = template.NorthArrivalTileY;
                facingDirection = 2; // Sur
            }
            else if (townTileY <= 2)
            {
                targetX = template.SouthArrivalTileX;
                targetY = template.SouthArrivalTileY;
                facingDirection = 0; // Norte
            }
            else
            {
                targetX = template.EastArrivalTileX;
                targetY = template.EastArrivalTileY;
                facingDirection = 3; // Oeste
            }
        }

        public static void GetResidenceArrivalCoordinates(int townTileX, int townTileY, out int targetX, out int targetY, out int facingDirection)
        {
            GetResidenceArrivalCoordinates(townTileX, townTileY, null, out targetX, out targetY, out facingDirection);
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
        /// Casillas del camino de una parcela en la Granja Host: las dos de la apertura y el paso de 2 tiles de ancho
        /// que se despeja hasta la plaza. Algunas parcelas necesitan un paso más largo para salvar el acantilado o el seto.
        /// </summary>
        public static List<(int X, int Y)> GetPortalPathTiles(string doorId, int townTileX, int townTileY)
        {
            bool vertical = townTileX <= 2 || townTileX >= 75;
            (int X, int Y) t1 = (townTileX, townTileY);
            (int X, int Y) t2 = vertical ? (townTileX, townTileY + 1) : (townTileX + 1, townTileY);
            var tiles = new List<(int X, int Y)> { t1, t2 };

            if (townTileX <= 2)
            {
                int maxDepthX = doorId == "Farm_Portal_02" ? 7 : 6;
                for (int x = 0; x <= maxDepthX; x++)
                {
                    tiles.Add((x, t1.Y));
                    tiles.Add((x, t2.Y));
                }
            }
            else if (townTileX >= 75)
            {
                for (int x = 76; x <= HostFarmMaxTileX; x++)
                {
                    tiles.Add((x, t1.Y));
                    tiles.Add((x, t2.Y));
                }
            }
            else if (townTileY <= 2)
            {
                // Todos los pasos norte se prolongan hasta y=10 para alcanzar el campo despejado
                int maxDepthY = 10;
                for (int y = 0; y <= maxDepthY; y++)
                {
                    tiles.Add((t1.X, y));
                    tiles.Add((t2.X, y));
                }
            }
            else
            {
                for (int y = 60; y <= HostFarmMaxTileY; y++)
                {
                    tiles.Add((t1.X, y));
                    tiles.Add((t2.X, y));
                }
            }

            return tiles;
        }

        /// <summary>Última columna (este) y última fila (sur) de la Granja Host, de 80×65 tiles.</summary>
        public const int HostFarmMaxTileX = 79;
        public const int HostFarmMaxTileY = 64;

        /// <summary>
        /// Casillas que disparan el portal de una parcela: las dos de la apertura y las dos del borde del mapa a
        /// continuación, es decir, las dos últimas casillas del camino hacia el borde (las mismas que llevan warp).
        /// El resto del camino es transitable sin abrir el diálogo de la parcela.
        /// </summary>
        public static bool IsPortalTriggerTile(int townTileX, int townTileY, int tileX, int tileY)
        {
            if (townTileX <= 2 || townTileX >= 75)
            {
                // Apertura vertical en el borde oeste o este: filas townTileY y townTileY + 1
                if (tileY != townTileY && tileY != townTileY + 1)
                    return false;
                int borderX = townTileX <= 2 ? 0 : HostFarmMaxTileX;
                return tileX == townTileX || tileX == borderX;
            }

            // Apertura horizontal en el borde norte o sur: columnas townTileX y townTileX + 1
            if (tileX != townTileX && tileX != townTileX + 1)
                return false;
            int borderY = townTileY <= 2 ? 0 : HostFarmMaxTileY;
            return tileY == townTileY || tileY == borderY;
        }

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
