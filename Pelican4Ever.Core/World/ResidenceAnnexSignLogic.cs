using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Lógica pura para la colocación y textos de los letreros indicadores de anexos (TASK-914).
    /// </summary>
    public static class ResidenceAnnexSignLogic
    {
        public const string ModDataKey = "Pelican4Ever.AnnexSign";
        public const string ReturnSignModDataKey = "Pelican4Ever.AnnexReturnSign";

        public const string ForSaleText = "Terreno en venta";
        public const string ReturnText = "Volver a la residencia";

        /// <summary>
        /// Genera el texto visible del letrero según el estado de compra del anexo.
        /// </summary>
        public static string GetResidenceSignText(AnnexAssignment? annex)
        {
            if (annex == null)
                return ForSaleText;

            AnnexTemplate template = AnnexTemplateCatalog.GetTemplate(annex.TemplateId);
            return $"Anexo: {template.DisplayName}";
        }

        /// <summary>
        /// Obtiene casillas candidatas adyacentes al paso para colocar el letrero en una residencia.
        /// Se eligen casillas inmediatamente flanqueando el paso transitable.
        /// </summary>
        public static IReadOnlyList<TilePoint> GetResidenceCandidateTiles(string? farmType, AnnexSide side)
        {
            var passage = VanillaFarmExitTable.Table.TryGetValue(farmType ?? "Standard", out var p)
                ? p
                : VanillaFarmExitTable.Table["Standard"];

            return side switch
            {
                AnnexSide.North => new[]
                {
                    new TilePoint(passage.NorthArrivalTile.X - 1, passage.NorthArrivalTile.Y),
                    new TilePoint(passage.NorthArrivalTile.X + 2, passage.NorthArrivalTile.Y),
                    new TilePoint(passage.NorthArrivalTile.X - 1, passage.NorthArrivalTile.Y + 1),
                    new TilePoint(passage.NorthArrivalTile.X + 2, passage.NorthArrivalTile.Y + 1)
                },
                AnnexSide.South => new[]
                {
                    new TilePoint(passage.SouthArrivalTile.X - 1, passage.SouthArrivalTile.Y),
                    new TilePoint(passage.SouthArrivalTile.X + 2, passage.SouthArrivalTile.Y),
                    new TilePoint(passage.SouthArrivalTile.X - 1, passage.SouthArrivalTile.Y - 1),
                    new TilePoint(passage.SouthArrivalTile.X + 2, passage.SouthArrivalTile.Y - 1)
                },
                AnnexSide.East => new[]
                {
                    new TilePoint(passage.EastArrivalTile.X, passage.EastArrivalTile.Y - 2),
                    new TilePoint(passage.EastArrivalTile.X, passage.EastArrivalTile.Y + 2),
                    new TilePoint(passage.EastArrivalTile.X - 1, passage.EastArrivalTile.Y - 2),
                    new TilePoint(passage.EastArrivalTile.X - 1, passage.EastArrivalTile.Y + 2)
                },
                _ => Array.Empty<TilePoint>()
            };
        }

        /// <summary>
        /// Obtiene casillas candidatas para el letrero de vuelta interior en el minimapa de anexo.
        /// </summary>
        public static IReadOnlyList<TilePoint> GetAnnexReturnCandidateTiles(AnnexSide side, TilePoint entranceTile, int mapWidth = 40, int mapHeight = 32)
        {
            return side switch
            {
                AnnexSide.North => new[]
                {
                    new TilePoint(Math.Clamp(entranceTile.X - 2, 1, mapWidth - 2), entranceTile.Y),
                    new TilePoint(Math.Clamp(entranceTile.X + 2, 1, mapWidth - 2), entranceTile.Y),
                    new TilePoint(Math.Clamp(entranceTile.X - 2, 1, mapWidth - 2), entranceTile.Y - 1),
                    new TilePoint(Math.Clamp(entranceTile.X + 2, 1, mapWidth - 2), entranceTile.Y - 1)
                },
                AnnexSide.South => new[]
                {
                    new TilePoint(Math.Clamp(entranceTile.X - 2, 1, mapWidth - 2), entranceTile.Y),
                    new TilePoint(Math.Clamp(entranceTile.X + 2, 1, mapWidth - 2), entranceTile.Y),
                    new TilePoint(Math.Clamp(entranceTile.X - 2, 1, mapWidth - 2), entranceTile.Y + 1),
                    new TilePoint(Math.Clamp(entranceTile.X + 2, 1, mapWidth - 2), entranceTile.Y + 1)
                },
                AnnexSide.East => new[]
                {
                    new TilePoint(entranceTile.X, Math.Clamp(entranceTile.Y - 2, 1, mapHeight - 2)),
                    new TilePoint(entranceTile.X, Math.Clamp(entranceTile.Y + 2, 1, mapHeight - 2)),
                    new TilePoint(entranceTile.X + 1, Math.Clamp(entranceTile.Y - 2, 1, mapHeight - 2)),
                    new TilePoint(entranceTile.X + 1, Math.Clamp(entranceTile.Y + 2, 1, mapHeight - 2))
                },
                _ => Array.Empty<TilePoint>()
            };
        }

        /// <summary>
        /// Elige la primera casilla libre de la lista de candidatas que sea transitable y no esté ocupada.
        /// </summary>
        public static TilePoint? ChooseFreeSignTile(
            IReadOnlyList<TilePoint> candidates,
            Func<int, int, bool> isTileFreeAndPassable)
        {
            foreach (var cand in candidates)
            {
                if (isTileFreeAndPassable(cand.X, cand.Y))
                    return cand;
            }
            return null;
        }
    }
}
