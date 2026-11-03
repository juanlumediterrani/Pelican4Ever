using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Gestiona la correspondencia espacial inmersiva entre la Plaza Central (Granja Host y Mapas Puente)
    /// y las residencias de los jugadores (Pelican4Ever_Farm_XX).
    /// </summary>
    public static class SpatialWarpRouter
    {
        /// <summary>
        /// Calcula las coordenadas y orientación de entrada a la residencia privada según el borde del portal en la plaza
        /// y el tipo de granja de la residencia (TASK-602 / Praderas azules).
        /// - Portales del SUR -> Entra por el NORTE de la residencia (salida Backwoods), mirando al Sur (2).
        /// - Portales del NORTE -> Entra por el SUR de la residencia (salida Forest), mirando al Norte (0).
        /// - Portales del OESTE y ESTE -> Entra por el ESTE de la residencia (salida BusStop), mirando al Oeste (3).
        /// </summary>
        public static void GetResidenceArrivalCoordinates(PortalEdge edge, string? farmType, out int targetX, out int targetY, out int facingDirection)
        {
            FarmTemplate template = FarmTemplateCatalog.GetTemplate(farmType);

            switch (edge)
            {
                case PortalEdge.South:
                    targetX = template.NorthArrivalTileX;
                    targetY = template.NorthArrivalTileY;
                    facingDirection = 2; // Sur
                    break;

                case PortalEdge.North:
                    targetX = template.SouthArrivalTileX;
                    targetY = template.SouthArrivalTileY;
                    facingDirection = 0; // Norte
                    break;

                case PortalEdge.West:
                case PortalEdge.East:
                    targetX = template.EastArrivalTileX;
                    targetY = template.EastArrivalTileY;
                    facingDirection = 3; // Oeste
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(edge), edge, null);
            }
        }

        public static void GetResidenceArrivalCoordinates(PortalEdge edge, out int targetX, out int targetY, out int facingDirection)
        {
            GetResidenceArrivalCoordinates(edge, null, out targetX, out targetY, out facingDirection);
        }

        /// <summary>
        /// Determina si un warp que sale de la residencia privada debe volver a la plaza (Granja Host o Mapa Puente)
        /// o continuar hacia el mapa vanilla correspondiente.
        /// </summary>
        public static bool ShouldReturnToPlaza(PortalEdge edge, string exitTargetLocation)
        {
            if (string.IsNullOrWhiteSpace(exitTargetLocation)) return false;

            return edge switch
            {
                PortalEdge.South => exitTargetLocation.Equals("Backwoods", StringComparison.OrdinalIgnoreCase)
                                 || exitTargetLocation.Equals("Mountain", StringComparison.OrdinalIgnoreCase),
                PortalEdge.North => exitTargetLocation.Equals("Forest", StringComparison.OrdinalIgnoreCase),
                PortalEdge.West or PortalEdge.East => exitTargetLocation.Equals("BusStop", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        /// <summary>
        /// Calcula la casilla de reaparición y orientación al volver a la localización de la plaza (Granja Host o Mapa Puente)
        /// para que el jugador aparezca justo frente a su portal, caminando hacia el interior.
        /// </summary>
        public static void GetPlazaReturnCoordinates(PortalEdge edge, int x, int y, out int plazaTileX, out int plazaTileY, out int facingDirection)
        {
            switch (edge)
            {
                case PortalEdge.West:
                    plazaTileX = x + 2;
                    plazaTileY = y;
                    facingDirection = 1; // Mirando hacia el Este
                    break;

                case PortalEdge.East:
                    plazaTileX = x - 2;
                    plazaTileY = y;
                    facingDirection = 3; // Mirando hacia el Oeste
                    break;

                case PortalEdge.North:
                    plazaTileX = x;
                    plazaTileY = y + 2;
                    facingDirection = 2; // Mirando hacia el Sur
                    break;

                case PortalEdge.South:
                    plazaTileX = x;
                    plazaTileY = y - 2;
                    facingDirection = 0; // Mirando hacia el Norte
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(edge), edge, null);
            }
        }

        /// <summary>
        /// Casillas del camino de una parcela en una ubicación de la plaza: las dos de la apertura y el paso de 2 tiles de ancho
        /// que se despeja hasta la calzada/plaza despejada.
        /// </summary>
        public static List<(int X, int Y)> GetResidencePathTiles(PortalEdge edge, int x, int y, PlazaLocation loc)
        {
            var tiles = new List<(int X, int Y)>();

            switch (edge)
            {
                case PortalEdge.North:
                    for (int py = 0; py <= loc.NorthPathDepth; py++)
                    {
                        tiles.Add((x, py));
                        tiles.Add((x + 1, py));
                    }
                    break;

                case PortalEdge.South:
                    int startY = y - loc.SouthPathStartOffset;
                    int maxY = loc.Height - 1;
                    for (int py = startY; py <= maxY; py++)
                    {
                        tiles.Add((x, py));
                        tiles.Add((x + 1, py));
                    }
                    break;

                case PortalEdge.West:
                    for (int px = 0; px <= 6; px++)
                    {
                        tiles.Add((px, y));
                        tiles.Add((px, y + 1));
                    }
                    break;

                case PortalEdge.East:
                    for (int px = loc.Width - 4; px <= loc.Width - 1; px++)
                    {
                        tiles.Add((px, y));
                        tiles.Add((px, y + 1));
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(edge), edge, null);
            }

            return tiles;
        }

        /// <summary>
        /// Casillas que disparan el portal de una parcela: las dos de la apertura y las dos del borde del mapa a
        /// continuación, es decir, las dos últimas casillas del camino hacia el borde (las mismas que llevan warp).
        /// El resto del camino es transitable sin abrir el diálogo de la parcela.
        /// </summary>
        public static bool IsPortalTriggerTile(PortalEdge edge, int x, int y, PlazaLocation loc, int tileX, int tileY)
        {
            return edge switch
            {
                PortalEdge.North => (tileX == x || tileX == x + 1) && (tileY == y || tileY == 0),
                PortalEdge.South => (tileX == x || tileX == x + 1) && (tileY == y || tileY == loc.Height - 1),
                PortalEdge.West => (tileY == y || tileY == y + 1) && (tileX == x || tileX == 0),
                PortalEdge.East => (tileY == y || tileY == y + 1) && (tileX == x || tileX == loc.Width - 1),
                _ => false
            };
        }

        /// <summary>
        /// Determina si un tile de copa de árbol (hojas en tilesheet) debe conservarse visible en AlwaysFront
        /// por encima del camino a las residencias para que no quede cortado y el jugador camine bajo él sin bloqueo.
        /// </summary>
        public static bool ShouldPreserveCanopyOverPath(PortalEdge edge, int tileIndex)
        {
            return edge == PortalEdge.South || tileIndex < 150;
        }
    }
}
