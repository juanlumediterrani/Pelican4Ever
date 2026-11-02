using System;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Coordenada bidimensional entera pura en casillas de mapa (TASK-901).
    /// </summary>
    public readonly record struct TilePoint(int X, int Y);

    /// <summary>
    /// Información de punto de entrada y retorno para un anexo temático (TASK-901).
    /// </summary>
    public sealed record AnnexWarpPoints(
        TilePoint EntryTile,
        int FacingDirection,
        TilePoint ReturnExitTile,
        TilePoint ResidenceReturnTile,
        int ResidenceReturnFacingDirection
    );

    /// <summary>
    /// Lógica pura para calcular puntos de entrada y retorno de los anexos (TASK-901).
    /// </summary>
    public static class AnnexEntrance
    {
        /// <summary>
        /// Calcula las coordenadas de entrada al anexo y de vuelta a la residencia según el lado de acceso y tipo de granja.
        /// En un mapa de tamaño (width × height, por defecto 40×32):
        /// - Acceso por el NORTE (salir de la residencia hacia el norte):
        ///   Entra por el borde SUR del anexo, mirando al Norte (0). Vuelve a la casilla norte de la residencia mirando al Sur (2).
        /// - Acceso por el ESTE (salir de la residencia hacia el este):
        ///   Entra por el borde OESTE del anexo, mirando al Este (1). Vuelve a la casilla este de la residencia mirando al Oeste (3).
        /// - Acceso por el SUR (salir de la residencia hacia el sur):
        ///   Entra por el borde NORTE del anexo, mirando al Sur (2). Vuelve a la casilla sur de la residencia mirando al Norte (0).
        /// </summary>
        public static AnnexWarpPoints For(AnnexSide side, int mapWidth = 40, int mapHeight = 32, string? farmType = null)
        {
            int centerX = mapWidth / 2;
            int centerY = mapHeight / 2;
            FarmTemplate template = FarmTemplateCatalog.GetTemplate(farmType);

            return side switch
            {
                AnnexSide.North => new AnnexWarpPoints(
                    EntryTile: new TilePoint(centerX, mapHeight - 2),
                    FacingDirection: 0, // Mirando al Norte
                    ReturnExitTile: new TilePoint(centerX, mapHeight - 1),
                    ResidenceReturnTile: new TilePoint(template.NorthArrivalTileX, template.NorthArrivalTileY),
                    ResidenceReturnFacingDirection: 2 // Mirando al Sur
                ),
                AnnexSide.East => new AnnexWarpPoints(
                    EntryTile: new TilePoint(1, centerY),
                    FacingDirection: 1, // Mirando al Este
                    ReturnExitTile: new TilePoint(0, centerY),
                    ResidenceReturnTile: new TilePoint(template.EastArrivalTileX, template.EastArrivalTileY),
                    ResidenceReturnFacingDirection: 3 // Mirando al Oeste
                ),
                AnnexSide.South => new AnnexWarpPoints(
                    EntryTile: new TilePoint(centerX, 1),
                    FacingDirection: 2, // Mirando al Sur
                    ReturnExitTile: new TilePoint(centerX, 0),
                    ResidenceReturnTile: new TilePoint(template.SouthArrivalTileX, template.SouthArrivalTileY),
                    ResidenceReturnFacingDirection: 0 // Mirando al Norte
                ),
                _ => throw new ArgumentOutOfRangeException(nameof(side), side, null)
            };
        }
    }
}
