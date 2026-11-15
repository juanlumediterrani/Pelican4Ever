using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Estilo del margen decorativo perimetral de un minimapa temático (TASK-902).
    /// El margen es una franja del propio mapa (1-2 casillas) no alcanzable por el jugador:
    /// se apoya en decoración visible canónica de Stardew Valley y nunca en colisiones
    /// invisibles, siguiendo el estilo de los bordes vanilla (setos, acantilados, agua, bosque).
    /// </summary>
    public enum AnnexRimStyle
    {
        /// <summary>Setos y arboleda completa (granja estándar).</summary>
        HedgeAndTrees,

        /// <summary>Orillas de río: agua perimetral con juncos y rocas de orilla (granja fluvial).</summary>
        RiverBank,

        /// <summary>Franja de bosque: doble fila de árboles con tronco y copa completos (forestal).</summary>
        ForestEdge,

        /// <summary>Desfiladero de roca: acantilados con remates superiores y piedras (colinas).</summary>
        RockyCanyon,

        /// <summary>Matorral salvaje: matorrales impenetrables y madera muerta (salvaje).</summary>
        WildThorns,

        /// <summary>Mixto por cuadrantes (Cuatro Esquinas): bosque al N y O, roca al S, setos al E.</summary>
        MixedCorners,

        /// <summary>Cerco de rancho: vallas continuas sobre pasto con remates completos (rancheras).</summary>
        RanchFence,

        /// <summary>Orilla oceánica: mar perimetral con arena, rocas y palmeras (playa).</summary>
        OceanShore
    }

    /// <summary>
    /// Estilo de la entrada del minimapa según el tipo de granja del que procede (TASK-902).
    /// La entrada debe estar delimitada de forma coherente con el tipo: puente de madera si
    /// cruza agua, túnel arbóreo si es forestal, desfiladero si es de minerales, etc.
    /// </summary>
    public enum AnnexEntranceStyle
    {
        /// <summary>Camino de tierra flanqueado por vallas de madera (granja estándar y Cuatro Esquinas).</summary>
        DirtGate,

        /// <summary>Puente de madera canónico de Stardew Valley (planchas + vigas + barandillas) para entradas fluviales o corredores con agua.</summary>
        WoodBridge,

        /// <summary>Túnel arbóreo: copa continua encima de la entrada y troncos flanqueando (forestal).</summary>
        ForestTunnel,

        /// <summary>Desfiladero de roca: taludes acantilados a ambos lados de la entrada (colinas).</summary>
        RockCanyon,

        /// <summary>Claro salvaje: matorrales y troncos muertos flanqueando la entrada (salvaje).</summary>
        WildGrove,

        /// <summary>Puerta de rancho: vallas verticales de madera junto al camino (rancheras).</summary>
        RanchGate,

        /// <summary>Sendero de arena con rocas de orilla (entrada costera).</summary>
        SandShorePath
    }

    /// <summary>
    /// Lógica pura de diseño de los minimapas temáticos (TASK-902).
    /// Interpola los comportamientos de <see cref="FarmTypeBehavior"/> (mecánicas de la gama de
    /// tipos de granja vanilla compartida con la rama de corrección de granjas) para decidir qué
    /// margen decorativo inalcanzable y qué estilo de entrada corresponden a cada minimapa, de
    /// modo que los elementos del minimapa reflejen el tipo de granja que lo representa.
    /// </summary>
    public static class AnnexMapDesignLogic
    {
        /// <summary>Profundidad del margen decorativo (nº de casillas de franja por borde).</summary>
        public const int RimDepth = 2;

        /// <summary>
        /// Resuelve el estilo del margen decorativo por borde cardinal a partir de los
        /// comportamientos de tipo de granja. Devuelve (norte, este, sur, oeste).
        /// Cada minimapa presenta el estilo coherente con su tipo; Cuatro Esquinas,
        /// como en vanilla, combina cuadrantes: bosque al N/O, roca al S y setos al E.
        /// </summary>
        public static (AnnexRimStyle North, AnnexRimStyle East, AnnexRimStyle South, AnnexRimStyle West)
            RimStyles(FarmTypeBehavior behavior, string? templateId)
        {
            if (behavior.FourCornersExtras || string.Equals(templateId, "FourCorners", StringComparison.OrdinalIgnoreCase))
            {
                return (AnnexRimStyle.ForestEdge, AnnexRimStyle.HedgeAndTrees, AnnexRimStyle.RockyCanyon, AnnexRimStyle.ForestEdge);
            }

            if (behavior.RiverFishSplashes)
                return (AnnexRimStyle.RiverBank, AnnexRimStyle.RiverBank, AnnexRimStyle.RiverBank, AnnexRimStyle.RiverBank);

            if (behavior.ForestForage || behavior.ForestStumps)
                return (AnnexRimStyle.ForestEdge, AnnexRimStyle.ForestEdge, AnnexRimStyle.ForestEdge, AnnexRimStyle.ForestEdge);

            if (behavior.MountainOreArea is not null)
                return (AnnexRimStyle.RockyCanyon, AnnexRimStyle.RockyCanyon, AnnexRimStyle.RockyCanyon, AnnexRimStyle.RockyCanyon);

            if (behavior.NightMonsters)
                return (AnnexRimStyle.WildThorns, AnnexRimStyle.WildThorns, AnnexRimStyle.WildThorns, AnnexRimStyle.WildThorns);

            if (behavior.BeachForage)
                return (AnnexRimStyle.OceanShore, AnnexRimStyle.OceanShore, AnnexRimStyle.OceanShore, AnnexRimStyle.OceanShore);

            // Pradera Azul / MeadowlandsFarm: prado con cerco de rancho.
            if (FarmGrassLogic.IsMeadowlands(templateId) || string.Equals(templateId, "Ranching", StringComparison.OrdinalIgnoreCase))
                return (AnnexRimStyle.RanchFence, AnnexRimStyle.RanchFence, AnnexRimStyle.RanchFence, AnnexRimStyle.RanchFence);

            // Estándar (y tipos desconocidos): setos y arboleda como en la granja vanilla.
            return (AnnexRimStyle.HedgeAndTrees, AnnexRimStyle.HedgeAndTrees, AnnexRimStyle.HedgeAndTrees, AnnexRimStyle.HedgeAndTrees);
        }

        /// <summary>
        /// Resuelve el estilo decorativo del margen para la plantilla y el lado de acceso indicado.
        /// </summary>
        public static AnnexRimStyle RimStyleFor(AnnexTemplate template, AnnexSide side)
        {
            var styles = RimStyles(template.Behavior, template.Id);
            return side switch
            {
                AnnexSide.North => styles.North,
                AnnexSide.East => styles.East,
                AnnexSide.South => styles.South,
                _ => styles.South
            };
        }

        /// <summary>
        /// Estilo de entrada declarado por tipo de granja. Si el corredor de acceso cruza agua,
        /// el pintor del mapa debe escalar a un puente de madera con independencia de la plantilla
        /// (regla: toda entrada con agua debajo puente; si no, estilo del tipo).
        /// </summary>
        public static AnnexEntranceStyle EntranceStyleFor(AnnexTemplate template)
        {
            FarmTypeBehavior behavior = template.Behavior;

            // Cuatro Esquinas: la entrada llega al cuadrante praderas del recorte SE, al estilo
            // estándar vanilla (camino de tierra con vallas) aunque el mapa contenga rocas.
            if (behavior.FourCornersExtras || string.Equals(template.Id, "FourCorners", StringComparison.OrdinalIgnoreCase))
                return AnnexEntranceStyle.DirtGate;

            if (behavior.RiverFishSplashes)
                return AnnexEntranceStyle.WoodBridge;

            if (behavior.ForestForage || behavior.ForestStumps)
                return AnnexEntranceStyle.ForestTunnel;

            if (behavior.MountainOreArea is not null)
                return AnnexEntranceStyle.RockCanyon;

            if (behavior.NightMonsters)
                return AnnexEntranceStyle.WildGrove;

            if (behavior.BeachForage)
                return AnnexEntranceStyle.SandShorePath;

            return AnnexEntranceStyle.DirtGate;
        }

        /// <summary>
        /// ¿El estilo de entrada es un puente de madera? (plantillas fluviales y forzados por agua).
        /// </summary>
        public static bool IsWoodBridgeStyle(AnnexEntranceStyle style)
        {
            return style == AnnexEntranceStyle.WoodBridge;
        }

        /// <summary>
        /// ¿La casilla (x, y) del mapa pertenece al margen decorativo de alguno de los 4 bordes?
        /// Comprobar tras colocar la entrada, porque en el lado de acceso la abertura interrumpe
        /// el margen y sus casillas deben quedar navegables como parte del pasaje.
        /// </summary>
        public static bool IsRimTile(int width, int height, int rimDepth, int x, int y)
        {
            if (rimDepth <= 0 || x < 0 || x >= width || y < 0 || y >= height)
                return false;

            return x < rimDepth || x >= width - rimDepth || y < rimDepth || y >= height - rimDepth;
        }

        /// <summary>
        /// Enumera todas las casillas del margen de cada borde (sin descontar la abertura de entrada).
        /// Devuelve los pares (x, y) correctos por casilla en orden de lectura.
        /// </summary>
        public static IEnumerable<(int X, int Y)> EnumerateRimTiles(int width, int height, int rimDepth)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (IsRimTile(width, height, rimDepth, x, y))
                        yield return (x, y);
                }
            }
        }
    }
}
