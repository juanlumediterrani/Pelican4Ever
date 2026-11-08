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
        /// Permite especificar un <paramref name="customEntrance"/> determinado por la ventana de recorte vanilla (TASK-911).
        /// </summary>
        public static AnnexWarpPoints For(
            AnnexSide side,
            int mapWidth = 40,
            int mapHeight = 32,
            string? farmType = null,
            TilePoint? customEntrance = null)
        {
            int defaultCenterX = mapWidth / 2;
            int defaultCenterY = mapHeight / 2;
            FarmTemplate template = FarmTemplateCatalog.GetTemplate(farmType);

            int entX = customEntrance?.X ?? (side == AnnexSide.East ? 1 : defaultCenterX);
            int entY = customEntrance?.Y ?? (side switch
            {
                AnnexSide.North => mapHeight - 2,
                AnnexSide.South => 1,
                _ => defaultCenterY
            });

            TilePoint entryTile = new TilePoint(entX, entY);

            TilePoint returnExitTile = side switch
            {
                AnnexSide.North => new TilePoint(entX, mapHeight - 1),
                AnnexSide.South => new TilePoint(entX, 0),
                AnnexSide.East => new TilePoint(0, entY),
                _ => throw new ArgumentOutOfRangeException(nameof(side), side, null)
            };

            return side switch
            {
                AnnexSide.North => new AnnexWarpPoints(
                    EntryTile: entryTile,
                    FacingDirection: 0, // Mirando al Norte
                    ReturnExitTile: returnExitTile,
                    ResidenceReturnTile: new TilePoint(template.NorthArrivalTileX, template.NorthArrivalTileY),
                    ResidenceReturnFacingDirection: 2 // Mirando al Sur
                ),
                AnnexSide.East => new AnnexWarpPoints(
                    EntryTile: entryTile,
                    FacingDirection: 1, // Mirando al Este
                    ReturnExitTile: returnExitTile,
                    ResidenceReturnTile: new TilePoint(template.EastArrivalTileX, template.EastArrivalTileY),
                    ResidenceReturnFacingDirection: 3 // Mirando al Oeste
                ),
                AnnexSide.South => new AnnexWarpPoints(
                    EntryTile: entryTile,
                    FacingDirection: 2, // Mirando al Sur
                    ReturnExitTile: returnExitTile,
                    ResidenceReturnTile: new TilePoint(template.SouthArrivalTileX, template.SouthArrivalTileY),
                    ResidenceReturnFacingDirection: 0 // Mirando al Norte
                ),
                _ => throw new ArgumentOutOfRangeException(nameof(side), side, null)
            };
        }

        /// <summary>
        /// Sobrecarga que calcula los puntos de entrada utilizando la ventana de recorte configurada para la plantilla (TASK-911).
        /// </summary>
        public static AnnexWarpPoints For(AnnexTemplate template, AnnexSide side, string? farmType = null)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            AnnexWindowLayout layout = template.GetWindowLayoutForSide(side);
            return For(side, template.Width, template.Height, farmType, layout.EntranceTile);
        }
    }
}
