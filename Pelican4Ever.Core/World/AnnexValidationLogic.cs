using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.World
{
    public enum ValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed record ValidationIssue(
        ValidationSeverity Severity,
        string Code,
        string Message,
        TilePoint? Tile = null
    );

    /// <summary>
    /// Datos de inspección pura de un anexo para validación sin dependencias del juego (TASK-907).
    /// </summary>
    public sealed record AnnexMapInspectionData(
        string TemplateId,
        AnnexSide Side,
        int Width,
        int Height,
        TilePoint EntryTile,
        bool[,] BackTilePresent,
        bool[,] IsPassable,
        bool[,] IsWater,
        TileArea? MountainOreArea = null,
        TileArea? ForestForageArea = null,
        IReadOnlyList<string>? MissingTileSheets = null,
        IReadOnlyList<string>? RemainingVanillaWarpsOrActions = null
    );

    public sealed record AnnexValidationReport(
        string TargetName,
        bool IsValid,
        IReadOnlyList<ValidationIssue> Issues,
        int TotalTransitableTiles,
        int ReachableTiles,
        IReadOnlyList<TilePoint> OpeningBorderTiles,
        bool BonusZonesReachable
    );

    /// <summary>
    /// Datos de inspección de una residencia (8 tipos × 3 lados) (TASK-913).
    /// </summary>
    public sealed record ResidenceSideInspectionData(
        string FarmTypeId,
        AnnexSide Side,
        int MapWidth,
        int MapHeight,
        TilePoint ArrivalTile,
        IReadOnlyList<TilePoint> WarpTiles,
        bool[,] IsPassable
    );

    public sealed record ResidenceValidationReport(
        string FarmTypeId,
        AnnexSide Side,
        bool IsValid,
        IReadOnlyList<ValidationIssue> Issues,
        bool ArrivalPassable,
        bool WarpTilesPassable,
        bool PathFromArrivalToWarpExists
    );

    /// <summary>
    /// Lógica pura para validar mapas de anexo y pasos de residencia (TASK-907 / TASK-913).
    /// </summary>
    public static class AnnexValidationLogic
    {
        public const int ExpectedWidth = 40;
        public const int ExpectedHeight = 32;

        public static AnnexValidationReport ValidateAnnex(AnnexMapInspectionData data)
        {
            var issues = new List<ValidationIssue>();
            string targetName = $"{data.TemplateId}_{data.Side}";

            // 1. Comprobación de dimensiones
            if (data.Width != ExpectedWidth || data.Height != ExpectedHeight)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "DIMENSIONS",
                    $"Tamaño esperado {ExpectedWidth}×{ExpectedHeight} pero es {data.Width}×{data.Height}."));
            }

            int arrW = data.BackTilePresent.GetLength(0);
            int arrH = data.BackTilePresent.GetLength(1);
            if (arrW != data.Width || arrH != data.Height)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "ARRAY_DIMENSIONS_MISMATCH",
                    $"La cuadrícula de datos ({arrW}×{arrH}) no coincide con el tamaño del mapa ({data.Width}×{data.Height})."));
            }

            int w = Math.Min(data.Width, arrW);
            int h = Math.Min(data.Height, arrH);

            // 2. Comprobación de casillas Back nulas
            int nullBackCount = 0;
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    if (!data.BackTilePresent[x, y])
                    {
                        nullBackCount++;
                        if (nullBackCount <= 5)
                        {
                            issues.Add(new ValidationIssue(
                                ValidationSeverity.Error,
                                "NULL_BACK",
                                $"Casilla Back nula en ({x}, {y}).",
                                new TilePoint(x, y)));
                        }
                    }
                }
            }
            if (nullBackCount > 5)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "NULL_BACK_SUMMARY",
                    $"Hay {nullBackCount} casillas Back nulas en total."));
            }

            // 3. Comprobación de perímetro: una sola apertura en el borde esperado
            // y ningún borde transitable fuera de ella.
            var passableBorderTiles = new List<TilePoint>();
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    bool isBorder = x == 0 || x == w - 1 || y == 0 || y == h - 1;
                    if (isBorder && data.IsPassable[x, y])
                    {
                        passableBorderTiles.Add(new TilePoint(x, y));
                    }
                }
            }

            // Borde esperado según el lado cardinal de acceso:
            // - North (acceso norte de la residencia): entra por el borde Sur del anexo (y = h - 1).
            // - South (acceso sur): entra por el borde Norte del anexo (y = 0).
            // - East (acceso este): entra por el borde Oeste del anexo (x = 0).
            bool IsExpectedBorder(TilePoint p) => data.Side switch
            {
                AnnexSide.North => p.Y == h - 1 && p.X > 0 && p.X < w - 1,
                AnnexSide.South => p.Y == 0 && p.X > 0 && p.X < w - 1,
                AnnexSide.East => p.X == 0 && p.Y > 0 && p.Y < h - 1,
                _ => false
            };

            var openingTiles = passableBorderTiles.Where(IsExpectedBorder).ToList();
            var unexpectedBorderTiles = passableBorderTiles.Where(p => !IsExpectedBorder(p)).ToList();

            if (unexpectedBorderTiles.Count > 0)
            {
                foreach (var up in unexpectedBorderTiles.Take(5))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        "UNSEALED_BORDER",
                        $"Borde no sellado transitable fuera de la entrada en ({up.X}, {up.Y}).",
                        up));
                }
            }

            if (openingTiles.Count == 0)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "NO_ENTRANCE_OPENING",
                    $"No hay apertura transitable en el borde esperado para {data.Side}."));
            }
            else
            {
                // Verificar contigüidad de la apertura
                bool contiguous = true;
                if (data.Side == AnnexSide.East)
                {
                    var ys = openingTiles.Select(p => p.Y).OrderBy(y => y).ToList();
                    for (int i = 1; i < ys.Count; i++)
                    {
                        if (ys[i] != ys[i - 1] + 1) contiguous = false;
                    }
                }
                else
                {
                    var xs = openingTiles.Select(p => p.X).OrderBy(x => x).ToList();
                    for (int i = 1; i < xs.Count; i++)
                    {
                        if (xs[i] != xs[i - 1] + 1) contiguous = false;
                    }
                }

                if (!contiguous)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        "FRAGMENTED_OPENING",
                        "La apertura de entrada no es contigua."));
                }
            }

            // 4. Transitabilidad y recorrido en anchura (BFS) desde la casilla de entrada
            int totalTransitable = 0;
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (data.IsPassable[x, y]) totalTransitable++;

            var visited = new bool[w, h];
            int reachableCount = 0;

            bool entryInside = data.EntryTile.X >= 0 && data.EntryTile.X < w && data.EntryTile.Y >= 0 && data.EntryTile.Y < h;
            if (!entryInside || !data.IsPassable[data.EntryTile.X, data.EntryTile.Y])
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "ENTRY_BLOCKED",
                    $"La casilla de entrada ({data.EntryTile.X}, {data.EntryTile.Y}) es colisión o está fuera del mapa.",
                    data.EntryTile));
            }
            else
            {
                var queue = new Queue<TilePoint>();
                queue.Enqueue(data.EntryTile);
                visited[data.EntryTile.X, data.EntryTile.Y] = true;

                Span<TilePoint> neighbors = stackalloc TilePoint[4];
                while (queue.Count > 0)
                {
                    TilePoint curr = queue.Dequeue();
                    reachableCount++;

                    neighbors[0] = new TilePoint(curr.X + 1, curr.Y);
                    neighbors[1] = new TilePoint(curr.X - 1, curr.Y);
                    neighbors[2] = new TilePoint(curr.X, curr.Y + 1);
                    neighbors[3] = new TilePoint(curr.X, curr.Y - 1);

                    foreach (var n in neighbors)
                    {
                        if (n.X >= 0 && n.X < w && n.Y >= 0 && n.Y < h)
                        {
                            if (!visited[n.X, n.Y] && data.IsPassable[n.X, n.Y])
                            {
                                visited[n.X, n.Y] = true;
                                queue.Enqueue(n);
                            }
                        }
                    }
                }

                if (reachableCount < totalTransitable)
                {
                    int unreachable = totalTransitable - reachableCount;
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        "UNREACHABLE_TILES",
                        $"{unreachable} de {totalTransitable} casillas transitables son islas no alcanzables desde la entrada."));
                }
            }

            // 5. Zonas de bonificación alcanzables
            bool bonusReachable = true;

            // Minerales
            if (data.MountainOreArea is { } oreArea)
            {
                bool anyOreReachable = false;
                for (int x = oreArea.X; x < oreArea.X + oreArea.Width && x < w; x++)
                {
                    for (int y = oreArea.Y; y < oreArea.Y + oreArea.Height && y < h; y++)
                    {
                        if (x >= 0 && y >= 0 && visited[x, y])
                        {
                            anyOreReachable = true;
                            break;
                        }
                    }
                    if (anyOreReachable) break;
                }

                if (!anyOreReachable)
                {
                    bonusReachable = false;
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        "BONUS_ORE_UNREACHABLE",
                        $"La zona de cantera {oreArea} no es alcanzable desde la entrada."));
                }
            }

            // Forraje
            if (data.ForestForageArea is { } forageArea)
            {
                bool anyForageReachable = false;
                for (int x = forageArea.X; x < forageArea.X + forageArea.Width && x < w; x++)
                {
                    for (int y = forageArea.Y; y < forageArea.Y + forageArea.Height && y < h; y++)
                    {
                        if (x >= 0 && y >= 0 && visited[x, y])
                        {
                            anyForageReachable = true;
                            break;
                        }
                    }
                    if (anyForageReachable) break;
                }

                if (!anyForageReachable)
                {
                    bonusReachable = false;
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        "BONUS_FORAGE_UNREACHABLE",
                        $"La zona de forraje {forageArea} no es alcanzable desde la entrada."));
                }
            }

            // Agua pescable (específico para Fluvial u otros con agua)
            if (string.Equals(data.TemplateId, "Fishing", StringComparison.OrdinalIgnoreCase))
            {
                bool waterShoreReachable = false;
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        if (visited[x, y])
                        {
                            // ¿Está adyacente a una casilla de agua?
                            if ((x > 0 && data.IsWater[x - 1, y]) ||
                                (x < w - 1 && data.IsWater[x + 1, y]) ||
                                (y > 0 && data.IsWater[x, y - 1]) ||
                                (y < h - 1 && data.IsWater[x, y + 1]))
                            {
                                waterShoreReachable = true;
                                break;
                            }
                        }
                    }
                    if (waterShoreReachable) break;
                }

                if (!waterShoreReachable)
                {
                    bonusReachable = false;
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        "NO_FISHING_SHORE",
                        "El anexo fluvial no tiene orilla transitable accesible para pescar."));
                }
            }

            // 6. Hojas de tiles inexistentes
            if (data.MissingTileSheets != null && data.MissingTileSheets.Count > 0)
            {
                foreach (var sheet in data.MissingTileSheets)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        "MISSING_TILESHEET",
                        $"El mapa referencia la hoja de tiles inexistente '{sheet}'."));
                }
            }

            // 7. Warps o acciones vanilla restantes
            if (data.RemainingVanillaWarpsOrActions != null && data.RemainingVanillaWarpsOrActions.Count > 0)
            {
                foreach (var rem in data.RemainingVanillaWarpsOrActions)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        "VANILLA_LEFTOVER",
                        $"Warp o acción vanilla sin eliminar: {rem}."));
                }
            }

            bool isValid = !issues.Any(i => i.Severity == ValidationSeverity.Error);

            return new AnnexValidationReport(
                TargetName: targetName,
                IsValid: isValid,
                Issues: issues,
                TotalTransitableTiles: totalTransitable,
                ReachableTiles: reachableCount,
                OpeningBorderTiles: openingTiles,
                BonusZonesReachable: bonusReachable
            );
        }

        public static ResidenceValidationReport ValidateResidence(ResidenceSideInspectionData data)
        {
            var issues = new List<ValidationIssue>();
            int w = data.MapWidth;
            int h = data.MapHeight;

            // 1. Casilla de llegada es transitable
            bool arrivalInside = data.ArrivalTile.X >= 0 && data.ArrivalTile.X < w && data.ArrivalTile.Y >= 0 && data.ArrivalTile.Y < h;
            bool arrivalPassable = arrivalInside && data.IsPassable[data.ArrivalTile.X, data.ArrivalTile.Y];

            if (!arrivalPassable)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "ARRIVAL_COLLISION",
                    $"La casilla de llegada {data.ArrivalTile} para {data.Side} en {data.FarmTypeId} cae en colisión o fuera del mapa.",
                    data.ArrivalTile));
            }

            // 2. Casilla de llegada no es un warp tile
            if (data.WarpTiles.Contains(data.ArrivalTile))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "ARRIVAL_IS_WARP",
                    $"La casilla de llegada {data.ArrivalTile} coincide con una casilla de warp hacia el anexo.",
                    data.ArrivalTile));
            }

            // 3. Casillas de warp son transitables
            bool allWarpsPassable = true;
            foreach (var wt in data.WarpTiles)
            {
                bool inside = wt.X >= 0 && wt.X < w && wt.Y >= 0 && wt.Y < h;
                if (!inside || !data.IsPassable[wt.X, wt.Y])
                {
                    allWarpsPassable = false;
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        "WARP_COLLISION",
                        $"La casilla de warp {wt} para {data.Side} en {data.FarmTypeId} cae en colisión.",
                        wt));
                }
            }

            // 4. Camino BFS de la llegada a las casillas de warp
            bool pathExists = false;
            if (arrivalPassable && data.WarpTiles.Count > 0)
            {
                var visited = new bool[w, h];
                var queue = new Queue<TilePoint>();
                queue.Enqueue(data.ArrivalTile);
                visited[data.ArrivalTile.X, data.ArrivalTile.Y] = true;

                var warpSet = new HashSet<TilePoint>(data.WarpTiles);

                Span<TilePoint> neighbors = stackalloc TilePoint[4];
                while (queue.Count > 0)
                {
                    var curr = queue.Dequeue();
                    if (warpSet.Contains(curr))
                    {
                        pathExists = true;
                        break;
                    }

                    neighbors[0] = new TilePoint(curr.X + 1, curr.Y);
                    neighbors[1] = new TilePoint(curr.X - 1, curr.Y);
                    neighbors[2] = new TilePoint(curr.X, curr.Y + 1);
                    neighbors[3] = new TilePoint(curr.X, curr.Y - 1);

                    foreach (var n in neighbors)
                    {
                        if (n.X >= 0 && n.X < w && n.Y >= 0 && n.Y < h)
                        {
                            if (!visited[n.X, n.Y] && data.IsPassable[n.X, n.Y])
                            {
                                visited[n.X, n.Y] = true;
                                queue.Enqueue(n);
                            }
                        }
                    }
                }

                if (!pathExists)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        "NO_PATH_ARRIVAL_TO_WARP",
                        $"No hay camino transitable entre la llegada {data.ArrivalTile} y los warps de {data.Side} en {data.FarmTypeId}."));
                }
            }

            bool isValid = !issues.Any(i => i.Severity == ValidationSeverity.Error);

            return new ResidenceValidationReport(
                FarmTypeId: data.FarmTypeId,
                Side: data.Side,
                IsValid: isValid,
                Issues: issues,
                ArrivalPassable: arrivalPassable,
                WarpTilesPassable: allWarpsPassable,
                PathFromArrivalToWarpExists: pathExists
            );
        }
    }
}
