using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Ventana de recorte 40×32 evaluada con su puntuación y características de composición vanilla (TASK-910).
    /// </summary>
    public sealed record ScoredWindowCandidate(
        int WindowX,
        int WindowY,
        int Score,
        TilePoint EntranceTile,
        bool HasNaturalExitOnEntrance,
        IReadOnlyList<string> NaturalBorders,
        IReadOnlyList<string> BordersToClose,
        string Details
    );

    /// <summary>
    /// Datos de entrada para la puntuación pura de ventanas de recorte de un mapa vanilla (TASK-910).
    /// </summary>
    public sealed record MapScoringInput(
        string TemplateId,
        AnnexSide Side,
        int MapWidth,
        int MapHeight,
        bool[,] IsPassable,
        bool[,] IsWater,
        bool[,] HasBuildingLayerTile,
        TileArea? OreArea = null,
        bool HasForestForage = false,
        TilePoint? VanillaHouseTile = null,
        VanillaExitPassage? VanillaExit = null
    );

    /// <summary>
    /// Lógica pura para puntuar y proponer las mejores ventanas de recorte 40×32 de cada mapa vanilla (TASK-910).
    /// </summary>
    public static class AnnexWindowScoringLogic
    {
        public const int WindowWidth = 40;
        public const int WindowHeight = 32;

        public static ScoredWindowCandidate ScoreCandidate(MapScoringInput input, int wx, int wy)
        {
            int mw = input.MapWidth;
            int mh = input.MapHeight;
            int score = 0;
            var naturalBorders = new List<string>(4);
            var bordersToClose = new List<string>(4);

            // 1. Bordes naturales que coinciden con el perímetro vanilla (+100 cada uno)
            if (wy == 0) { score += 100; naturalBorders.Add("North"); } else bordersToClose.Add("North");
            if (wy + WindowHeight == mh) { score += 100; naturalBorders.Add("South"); } else bordersToClose.Add("South");
            if (wx == 0) { score += 100; naturalBorders.Add("West"); } else bordersToClose.Add("West");
            if (wx + WindowWidth == mw) { score += 100; naturalBorders.Add("East"); } else bordersToClose.Add("East");

            // 2. Salida vanilla en el lado de entrada (+150)
            // Relación cardinal:
            // - AnnexSide.North (acceso norte de la residencia): se entra por el borde Sur del anexo (y = wy + H - 1).
            // - AnnexSide.South (acceso sur): se entra por el borde Norte del anexo (y = wy).
            // - AnnexSide.East (acceso este): se entra por el borde Oeste del anexo (x = wx).
            bool hasNaturalExit = false;
            int entX = wx + WindowWidth / 2;
            int entY = wy + WindowHeight / 2;

            if (input.VanillaExit != null)
            {
                if (input.Side == AnnexSide.South && wy == 0 && input.VanillaExit.NorthExitTiles.Count > 0)
                {
                    TilePoint nTile = input.VanillaExit.NorthExitTiles[0];
                    if (nTile.X >= wx && nTile.X < wx + WindowWidth)
                    {
                        hasNaturalExit = true;
                        score += 150;
                        entX = nTile.X;
                        entY = wy + 1; // 1 casilla al interior
                    }
                }
                else if (input.Side == AnnexSide.North && wy + WindowHeight == mh && input.VanillaExit.SouthExitTiles.Count > 0)
                {
                    TilePoint sTile = input.VanillaExit.SouthExitTiles[0];
                    if (sTile.X >= wx && sTile.X < wx + WindowWidth)
                    {
                        hasNaturalExit = true;
                        score += 150;
                        entX = sTile.X;
                        entY = wy + WindowHeight - 2; // 1 casilla al interior
                    }
                }
                else if (input.Side == AnnexSide.East && wx == 0 && input.VanillaExit.EastExitTiles.Count > 0)
                {
                    // En el oeste del mapa no suele haber salida vanilla, pero la entrada va pegada al borde oeste
                    entX = wx + 1;
                    entY = wy + WindowHeight / 2;
                }
            }

            if (!hasNaturalExit)
            {
                if (input.Side == AnnexSide.North) { entX = wx + WindowWidth / 2; entY = wy + WindowHeight - 2; }
                else if (input.Side == AnnexSide.South) { entX = wx + WindowWidth / 2; entY = wy + 1; }
                else { entX = wx + 1; entY = wy + WindowHeight / 2; }
            }

            // 3. Transitabilidad de la casilla de entrada (-500 si es colisión o agua)
            bool entInside = entX >= 0 && entX < mw && entY >= 0 && entY < mh;
            bool entPassable = entInside && input.IsPassable[entX, entY] && !input.IsWater[entX, entY];
            if (!entPassable)
            {
                score -= 500;
            }

            // 4. Zona de bonificación
            if (input.OreArea is { } ore)
            {
                bool oreInside = ore.X >= wx && ore.X + ore.Width <= wx + WindowWidth &&
                                 ore.Y >= wy && ore.Y + ore.Height <= wy + WindowHeight;
                if (oreInside) score += 120;
                else
                {
                    bool overlaps = Math.Max(wx, ore.X) < Math.Min(wx + WindowWidth, ore.X + ore.Width) &&
                                    Math.Max(wy, ore.Y) < Math.Min(wy + WindowHeight, ore.Y + ore.Height);
                    if (overlaps) score += 50;
                    else score -= 80;
                }
            }
            else if (input.HasForestForage)
            {
                // Franja forestal oeste
                if (wx == 0) score += 100;
                else score -= 50;
            }
            else if (string.Equals(input.TemplateId, "Fishing", StringComparison.OrdinalIgnoreCase))
            {
                int waterCount = 0;
                for (int x = wx; x < wx + WindowWidth && x < mw; x++)
                    for (int y = wy; y < wy + WindowHeight && y < mh; y++)
                        if (input.IsWater[x, y]) waterCount++;

                if (waterCount >= 80 && waterCount <= 500) score += 120;
                else if (waterCount > 500) score -= 60;
                else score -= 100;
            }

            // 5. Penalización por contener la casa vanilla
            if (input.VanillaHouseTile is { } house)
            {
                if (house.X >= wx && house.X <= wx + WindowWidth && house.Y >= wy && house.Y <= wy + WindowHeight)
                {
                    score -= 150;
                }
            }

            // 6. Penalización por bordes no naturales cortados que parten colisión/obstáculos
            int cutObstacles = 0;
            if (wy > 0)
            {
                for (int x = wx; x < wx + WindowWidth && x < mw; x++)
                    if (input.HasBuildingLayerTile[x, wy] && !input.IsPassable[x, wy]) cutObstacles++;
            }
            if (wy + WindowHeight < mh)
            {
                int bottomY = wy + WindowHeight - 1;
                for (int x = wx; x < wx + WindowWidth && x < mw; x++)
                    if (input.HasBuildingLayerTile[x, bottomY] && !input.IsPassable[x, bottomY]) cutObstacles++;
            }
            if (wx > 0)
            {
                for (int y = wy; y < wy + WindowHeight && y < mh; y++)
                    if (input.HasBuildingLayerTile[wx, y] && !input.IsPassable[wx, y]) cutObstacles++;
            }
            if (wx + WindowWidth < mw)
            {
                int rightX = wx + WindowWidth - 1;
                for (int y = wy; y < wy + WindowHeight && y < mh; y++)
                    if (input.HasBuildingLayerTile[rightX, y] && !input.IsPassable[rightX, y]) cutObstacles++;
            }

            score -= cutObstacles * 3;

            string details = $"BordesNaturales={string.Join("+", naturalBorders)}, PorCerrar={string.Join("+", bordersToClose)}, SalidaVanilla={hasNaturalExit}, ObstáculosCortados={cutObstacles}";

            return new ScoredWindowCandidate(
                WindowX: wx,
                WindowY: wy,
                Score: score,
                EntranceTile: new TilePoint(entX - wx, entY - wy),
                HasNaturalExitOnEntrance: hasNaturalExit,
                NaturalBorders: naturalBorders,
                BordersToClose: bordersToClose,
                Details: details
            );
        }

        public static IReadOnlyList<ScoredWindowCandidate> FindTopWindows(MapScoringInput input, int topN = 3)
        {
            var candidates = new List<ScoredWindowCandidate>();
            int maxWx = input.MapWidth - WindowWidth;
            int maxWy = input.MapHeight - WindowHeight;

            for (int y = 0; y <= maxWy; y++)
            {
                for (int x = 0; x <= maxWx; x++)
                {
                    candidates.Add(ScoreCandidate(input, x, y));
                }
            }

            return candidates.OrderByDescending(c => c.Score).Take(topN).ToList();
        }
    }
}
