using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Familia de objeto multi-casilla vertical cuyo cuerpo/colisión reside en una fila <c>y</c>
    /// (normalmente en la capa <c>Buildings</c>) y cuya parte superior se proyecta visualmente
    /// en la fila <c>y - 1</c> (en <c>Front</c>, <c>Front2</c> o <c>AlwaysFront</c>), como el buzón de correo.
    /// </summary>
    public enum VerticalMultiTileKind
    {
        None = 0,
        Mailbox,
        FenceLeftPost,
        FenceMiddleRail,
        FenceRightPost,
        FenceVerticalOrStandalone,
        Signpost,
        Bush,
        TreeTrunk
    }

    /// <summary>
    /// Clasificador y validador puro de objetos multi-casilla verticales (vallas de madera, postes indicadores,
    /// arbustos, troncos de árboles estáticos y buzones) en las hojas <c>spring_outdoorsTileSheet</c> y <c>spring_town</c>.
    /// Garantiza que toda base colocada en <c>(x, y)</c> tenga su remate superior correspondiente en <c>(x, y - 1)</c>
    /// y que ningún remate quede huérfano o solape el poste de un buzón.
    /// </summary>
    public static class VerticalMultiTileClassifier
    {
        // --- Buzón de correo (hoja spring_town / z_Pelican4Ever_Town) ---
        public const int MailboxPostTileIndex = 41;
        public const int MailboxBoxTileIndex = 9;

        // --- Vallas horizontales de madera (hoja spring_outdoorsTileSheet) ---
        /// <summary>Poste izquierdo de valla horizontal (<c>|--</c>, conecta hacia la derecha): base en Buildings.</summary>
        public const int FenceLeftBaseTileIndex = 383;
        /// <summary>Remate superior del poste izquierdo de valla horizontal (<c>|--</c>): va en Front <c>(x, y - 1)</c>.</summary>
        public const int FenceLeftTopTileIndex = 358;

        /// <summary>Tramo central de valla horizontal (<c>--|--</c>, conecta a ambos lados): base en Buildings.</summary>
        public const int FenceMidBaseTileIndex = 384;
        /// <summary>Remate superior del tramo central de valla horizontal (<c>--|--</c>): va en Front <c>(x, y - 1)</c>.</summary>
        public const int FenceMidTopTileIndex = 359;

        /// <summary>Variante envejecida de tramo central de valla horizontal: base en Buildings.</summary>
        public const int FenceWeatheredMidBaseTileIndex = 434;
        /// <summary>Remate superior envejecido de valla horizontal: va en Front <c>(x, y - 1)</c>.</summary>
        public const int FenceWeatheredMidTopTileIndex = 409;

        /// <summary>Poste derecho de valla horizontal (<c>--|</c>, conecta hacia la izquierda): base en Buildings.</summary>
        public const int FenceRightBaseTileIndex = 385;
        /// <summary>Remate superior del poste derecho de valla horizontal (<c>--|</c>): va en Front <c>(x, y - 1)</c>.</summary>
        public const int FenceRightTopTileIndex = 360;

        // --- Vallas verticales / poste individual cerrado (hoja spring_outdoorsTileSheet) ---
        /// <summary>Remate superior de valla vertical o poste individual sin travesaños laterales: Front <c>(x, y - 1)</c>.</summary>
        public const int FenceVerticalTopTileIndex = 361;
        /// <summary>Tramo intermedio superior de valla vertical: Buildings <c>(x, y)</c>.</summary>
        public const int FenceVerticalUpperMidTileIndex = 386;
        /// <summary>Tramo intermedio inferior de valla vertical: Buildings <c>(x, y)</c>.</summary>
        public const int FenceVerticalLowerMidTileIndex = 411;
        /// <summary>Base inferior de valla vertical o poste individual sin travesaños laterales: Buildings <c>(x, y)</c>.</summary>
        public const int FenceVerticalBaseTileIndex = 436;

        // --- Poste indicador de madera (hoja spring_outdoorsTileSheet) ---
        public const int SignpostBaseTileIndex = 435;
        public const int SignpostTopTileIndex = 410;

        private static readonly Dictionary<int, int[]> BaseToExpectedTops = new()
        {
            // Vallas de madera
            [FenceLeftBaseTileIndex] = new[] { FenceLeftTopTileIndex },
            [FenceMidBaseTileIndex] = new[] { FenceMidTopTileIndex, FenceWeatheredMidTopTileIndex },
            [FenceWeatheredMidBaseTileIndex] = new[] { FenceWeatheredMidTopTileIndex, FenceMidTopTileIndex },
            [FenceRightBaseTileIndex] = new[] { FenceRightTopTileIndex },
            [FenceVerticalBaseTileIndex] = new[] { FenceVerticalTopTileIndex, FenceVerticalLowerMidTileIndex, FenceVerticalUpperMidTileIndex },
            [FenceVerticalLowerMidTileIndex] = new[] { FenceVerticalUpperMidTileIndex, FenceVerticalTopTileIndex },

            // Poste indicador
            [SignpostBaseTileIndex] = new[] { SignpostTopTileIndex },

            // Arbusto 3x2 (base 63..65 -> copa 38..40)
            [63] = new[] { 38 },
            [64] = new[] { 39 },
            [65] = new[] { 40 },

            // Arbusto 2x2 (base 113..114 -> copa 88..89)
            [113] = new[] { 88 },
            [114] = new[] { 89 },

            // Arbusto alto 1x3 (base 126 -> medio 101 -> copa 76)
            [126] = new[] { 101 },
            [101] = new[] { 76 },

            // Árbol estático de 2 casillas de ancho (raíz 168,169 -> tronco medio 143,144 -> tronco alto 118,119)
            [168] = new[] { 143 },
            [169] = new[] { 144 },
            [143] = new[] { 118 },
            [144] = new[] { 119 },

            // Árboles estáticos de 1 casilla de ancho
            [129] = new[] { 104 },
            [104] = new[] { 79 },
            [132] = new[] { 107 },
            [107] = new[] { 82 },
            [133] = new[] { 108 },
            [108] = new[] { 83 },
            [136] = new[] { 111 },
            [111] = new[] { 86 },
        };

        private static readonly Dictionary<int, int[]> TopToExpectedBases = new()
        {
            [FenceLeftTopTileIndex] = new[] { FenceLeftBaseTileIndex },
            [FenceMidTopTileIndex] = new[] { FenceMidBaseTileIndex, FenceWeatheredMidBaseTileIndex },
            [FenceWeatheredMidTopTileIndex] = new[] { FenceWeatheredMidBaseTileIndex, FenceMidBaseTileIndex },
            [FenceRightTopTileIndex] = new[] { FenceRightBaseTileIndex },
            [FenceVerticalTopTileIndex] = new[] { FenceVerticalBaseTileIndex, FenceVerticalUpperMidTileIndex, FenceVerticalLowerMidTileIndex },
            [SignpostTopTileIndex] = new[] { SignpostBaseTileIndex },
            [38] = new[] { 63 },
            [39] = new[] { 64 },
            [40] = new[] { 65 },
            [88] = new[] { 113 },
            [89] = new[] { 114 },
            [76] = new[] { 101 },
            [101] = new[] { 126 },
            [143] = new[] { 168 },
            [144] = new[] { 169 },
            [118] = new[] { 143 },
            [119] = new[] { 144 },
            [104] = new[] { 129 },
            [107] = new[] { 132 },
            [82] = new[] { 107 },
            [108] = new[] { 133, 158 },
            [83] = new[] { 108 },
            [111] = new[] { 136 },
            [86] = new[] { 111 },
        };

        /// <summary>
        /// Clasifica un índice de tile base de la hoja exterior (<c>spring_outdoorsTileSheet</c>).
        /// </summary>
        public static VerticalMultiTileKind ClassifyOutdoorsBaseTile(int tileIndex)
        {
            return tileIndex switch
            {
                FenceLeftBaseTileIndex => VerticalMultiTileKind.FenceLeftPost,
                FenceMidBaseTileIndex or FenceWeatheredMidBaseTileIndex => VerticalMultiTileKind.FenceMiddleRail,
                FenceRightBaseTileIndex => VerticalMultiTileKind.FenceRightPost,
                FenceVerticalBaseTileIndex or FenceVerticalLowerMidTileIndex or FenceVerticalUpperMidTileIndex => VerticalMultiTileKind.FenceVerticalOrStandalone,
                SignpostBaseTileIndex => VerticalMultiTileKind.Signpost,
                63 or 64 or 65 or 113 or 114 or 126 or 101 => VerticalMultiTileKind.Bush,
                168 or 169 or 143 or 144 or 129 or 104 or 132 or 107 or 133 or 108 or 136 or 111 => VerticalMultiTileKind.TreeTrunk,
                _ => VerticalMultiTileKind.None
            };
        }

        /// <summary>
        /// Indica si la fila <paramref name="y"/> permite colocar la base de un objeto vertical de 2 casillas
        /// (requiere que exista <c>y - 1 &gt;= 0</c>).
        /// </summary>
        public static bool CanPlaceVerticalBaseAt(int y, int mapHeight)
        {
            return y >= 1 && y < mapHeight;
        }

        /// <summary>
        /// Obtiene el tile superior esperado en <c>(x, y - 1)</c> para un tile base situado en <c>(x, y)</c>
        /// dentro de <c>spring_outdoorsTileSheet</c>.
        /// </summary>
        public static bool TryGetExpectedTopTile(int baseTileIndex, out int defaultTopTileIndex, out IReadOnlyList<int> validTopTileIndices)
        {
            if (BaseToExpectedTops.TryGetValue(baseTileIndex, out int[]? tops) && tops.Length > 0)
            {
                defaultTopTileIndex = tops[0];
                validTopTileIndices = tops;
                return true;
            }

            defaultTopTileIndex = -1;
            validTopTileIndices = Array.Empty<int>();
            return false;
        }

        /// <summary>
        /// Obtiene los tiles base válidos en <c>(x, y + 1)</c> para un remate superior situado en <c>(x, y)</c>
        /// dentro de <c>spring_outdoorsTileSheet</c>.
        /// </summary>
        public static bool TryGetExpectedBaseTiles(int topTileIndex, out IReadOnlyList<int> validBaseTileIndices)
        {
            if (TopToExpectedBases.TryGetValue(topTileIndex, out int[]? bases) && bases.Length > 0)
            {
                validBaseTileIndices = bases;
                return true;
            }

            validBaseTileIndices = Array.Empty<int>();
            return false;
        }

        /// <summary>
        /// Devuelve el par <c>(BaseTileIndex, TopTileIndex)</c> para una posición <paramref name="x"/> dentro de un
        /// tramo continuo de valla horizontal desde <paramref name="startX"/> hasta <paramref name="endX"/> (ambos inclusive).
        /// <list type="bullet">
        /// <item><description>En el extremo izquierdo (<c>x == startX</c>): <c>(383, 358)</c> (<c>|--</c>, travesaños hacia la derecha).</description></item>
        /// <item><description>En el interior (<c>startX &lt; x &lt; endX</c>): <c>(384, 359)</c> (<c>--|--</c>, travesaños a ambos lados).</description></item>
        /// <item><description>En el extremo derecho (<c>x == endX</c>): <c>(385, 360)</c> (<c>--|</c>, travesaños hacia la izquierda).</description></item>
        /// <item><description>Si es un único poste aislado (<c>startX == endX</c>): <c>(436, 361)</c> (poste cerrado sin travesaños al aire).</description></item>
        /// </list>
        /// </summary>
        public static (int BaseTileIndex, int TopTileIndex) GetHorizontalFencePair(int x, int startX, int endX)
        {
            if (startX > endX)
                throw new ArgumentOutOfRangeException(nameof(startX), "startX no puede ser mayor que endX.");
            if (x < startX || x > endX)
                throw new ArgumentOutOfRangeException(nameof(x), "x debe estar dentro del intervalo [startX, endX].");

            if (startX == endX)
                return (FenceVerticalBaseTileIndex, FenceVerticalTopTileIndex);

            if (x == startX)
                return (FenceLeftBaseTileIndex, FenceLeftTopTileIndex);

            if (x == endX)
                return (FenceRightBaseTileIndex, FenceRightTopTileIndex);

            return (FenceMidBaseTileIndex, FenceMidTopTileIndex);
        }

        /// <summary>
        /// Devuelve los pares <c>(BaseTileIndex, TopTileIndex)</c> para rematar una apertura practicada en una
        /// valla horizontal existente: el lado izquierdo de la apertura cierra la valla izquierda con <c>(385, 360)</c> (<c>--|</c>)
        /// y el lado derecho de la apertura inicia la valla derecha con <c>(383, 358)</c> (<c>|--</c>).
        /// </summary>
        public static ((int BaseTileIndex, int TopTileIndex) LeftEndOfGap, (int BaseTileIndex, int TopTileIndex) RightStartOfGap) GetFenceGatePosts()
        {
            return (
                LeftEndOfGap: (FenceRightBaseTileIndex, FenceRightTopTileIndex),
                RightStartOfGap: (FenceLeftBaseTileIndex, FenceLeftTopTileIndex)
            );
        }

        /// <summary>
        /// Devuelve el tile en <c>Buildings</c> para una fila <paramref name="y"/> dentro de un tramo de valla
        /// vertical entre <paramref name="startY"/> y <paramref name="endY"/> (ambos inclusive).
        /// El remate superior <see cref="FenceVerticalTopTileIndex"/> (<c>361</c>) se coloca en <c>Front</c> en <c>startY - 1</c>.
        /// </summary>
        public static int GetVerticalFenceBodyTile(int y, int startY, int endY)
        {
            if (startY > endY)
                throw new ArgumentOutOfRangeException(nameof(startY), "startY no puede ser mayor que endY.");
            if (y < startY || y > endY)
                throw new ArgumentOutOfRangeException(nameof(y), "y debe estar dentro del intervalo [startY, endY].");

            if (y == endY)
                return FenceVerticalBaseTileIndex; // 436
            if (y == endY - 1 && endY > startY)
                return FenceVerticalLowerMidTileIndex; // 411
            return FenceVerticalUpperMidTileIndex; // 386
        }

        /// <summary>
        /// Comprueba si un poste de valla horizontal en <c>(x, y)</c> tiene continuidad válida con sus vecinos
        /// izquierdo (<paramref name="leftBuildingsTile"/>) y derecho (<paramref name="rightBuildingsTile"/>).
        /// </summary>
        public static bool HasValidHorizontalFenceNeighbors(int fenceBaseTileIndex, int? leftBuildingsTile, int? rightBuildingsTile)
        {
            static bool ConnectsFromLeft(int? tile) =>
                tile is FenceLeftBaseTileIndex or FenceMidBaseTileIndex or FenceWeatheredMidBaseTileIndex or FenceRightBaseTileIndex;

            static bool ConnectsFromRight(int? tile) =>
                tile is FenceRightBaseTileIndex or FenceMidBaseTileIndex or FenceWeatheredMidBaseTileIndex or FenceLeftBaseTileIndex;

            return fenceBaseTileIndex switch
            {
                FenceLeftBaseTileIndex => ConnectsFromRight(rightBuildingsTile),
                FenceRightBaseTileIndex => ConnectsFromLeft(leftBuildingsTile),
                FenceMidBaseTileIndex or FenceWeatheredMidBaseTileIndex => ConnectsFromLeft(leftBuildingsTile) && ConnectsFromRight(rightBuildingsTile),
                _ => true
            };
        }

        /// <summary>
        /// Determina si un tile en la capa <c>Front</c> en la misma coordenada <c>(mx, my)</c> del poste del buzón
        /// debe limpiarse para evitar que se dibuje por encima del poste (<c>Buildings[mx, my] = 41</c>).
        /// Conserva únicamente los rebordes de hierba rasantes (<c>412..414</c>).
        /// </summary>
        public static bool ShouldClearFrontOverMailboxPost(int? frontTileIndex)
        {
            if (!frontTileIndex.HasValue)
                return false;

            int idx = frontTileIndex.Value;
            // Los rebordes inferiores de hierba (412, 413, 414) van a ras de suelo y no tapan el poste vertical
            if (idx is 412 or 413 or 414)
                return false;

            return true;
        }
    }
}
