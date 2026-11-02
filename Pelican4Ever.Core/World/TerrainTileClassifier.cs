using System;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Clasificador de tiles de terreno y selección de baldosas/losas para caminos residenciales (TASK-501).
    /// Proporciona la lógica de selección de Steppingstone Path (camino de peldaños / stepping stones)
    /// adaptado tanto a hierba como a tierra con autotiling canónico de 4 bits.
    /// </summary>
    public static class TerrainTileClassifier
    {
        /// <summary>Nombre de la hoja de tiles de Stepping Stones añadida a la Granja Host.</summary>
        public const string SteppingStonesTileSheetId = "z_Pelican4Ever_SteppingStones";

        /// <summary>Offset base para tiles de Stepping Stones sobre tierra (tiles 0..15).</summary>
        public const int DirtBaseIndex = 0;

        /// <summary>Offset base para tiles de Stepping Stones sobre hierba (tiles 16..31).</summary>
        public const int GrassBaseIndex = 16;

        /// <summary>Tiles de baldosas de piedra del patio exterior / spouse area: 1964..1966.</summary>
        public static readonly int[] GrassBaldosas = { 1964, 1965, 1966 };

        /// <summary>Tiles de camino pedregado / empedrado alternativo (adoquines del pueblo en spring_town: 736, 768, 769, 737).</summary>
        public static readonly int[] DirtCobblestones = { 736, 768, 769, 737 };

        /// <summary>Baldosa de piedra lisa sobre tierra alternativa (altar del abuelo): 838.</summary>
        public const int LegacyDirtBaldosa = 838;

        /// <summary>
        /// Determina si un tile corresponde a césped/hierba en spring_outdoorsTileSheet o en z_Pelican4Ever_SteppingStones.
        /// </summary>
        public static bool IsGrassTileIndex(int tileIndex, string? typeProperty = null, string? tileSheetId = null)
        {
            if (string.Equals(typeProperty, "Grass", StringComparison.OrdinalIgnoreCase))
                return true;

            // En la hoja z_Pelican4Ever_SteppingStones, los tiles 16..31 son Stepping Stones sobre hierba
            if (string.Equals(tileSheetId, SteppingStonesTileSheetId, StringComparison.OrdinalIgnoreCase))
                return tileIndex >= GrassBaseIndex && tileIndex < GrassBaseIndex + 16;

            if (tileIndex is >= 150 and <= 152 or 175) return true;
            if (tileIndex is >= 250 and <= 256 or >= 275 and <= 281) return true;
            if (tileIndex is >= 300 and <= 305 or >= 325 and <= 330) return true;
            if (tileIndex is >= 350 and <= 355 or >= 375 and <= 381) return true;
            if (tileIndex is >= 400 and <= 405 or >= 1964 and <= 1966) return true;
            if (tileIndex is 326 or 329 or 351 or 380 or 376 or 594) return true;

            return false;
        }

        /// <summary>
        /// Sobrecarga compatible con versiones anteriores de IsGrassTileIndex.
        /// </summary>
        public static bool IsGrassTileIndex(int tileIndex, string? typeProperty)
        {
            return IsGrassTileIndex(tileIndex, typeProperty, null);
        }

        /// <summary>
        /// Sobrecarga simple para comprobación directa de índice de tile.
        /// </summary>
        public static bool IsGrassTileIndex(int tileIndex)
        {
            return IsGrassTileIndex(tileIndex, null, null);
        }

        /// <summary>
        /// Determina si una coordenada en la senda residencial de la Granja Host corresponde a un entorno de hierba o tierra,
        /// analizando la zonificación canónica del terreno de la granja.
        /// </summary>
        public static bool IsGrassEnvironment(
            string doorId,
            int townTileX,
            int townTileY,
            int tileX,
            int tileY)
        {
            // 1. Portales 08 y 14 (Valla Noreste): están en la pradera oriental de la casa, 100% césped verde
            if (doorId is "Farm_Portal_08" or "Farm_Portal_14")
                return true;

            // 2. Portales Norte con desfiladero rocoso (05, 06, 07, 13):
            // - y=0..3: cresta superior en el risco norte -> césped verde
            // - y=4..10: interior de la garganta y desembocadura en cultivo -> tierra
            if (townTileY <= 2 && doorId is "Farm_Portal_05" or "Farm_Portal_06" or "Farm_Portal_07" or "Farm_Portal_13")
            {
                return tileY <= 3;
            }

            // 3. Portales Oeste (01, 02, 03, 04):
            // - Portal 02 (Claro del Sauce): amplia pradera verde hasta x=5; entra en tierra en x=6..7
            if (doorId == "Farm_Portal_02")
            {
                return tileX <= 5;
            }
            // - Portales 01, 03, 04: margen boscoso verde en x=0..1; campo de labor en x=2..6
            if (townTileX <= 2)
            {
                return tileX <= 1;
            }

            // 4. Portales Sur (09..12, 15, 16):
            // - y=58..61: campo de cultivo de tierra
            // - y=62..64: ribera meridional con césped hacia el bosque
            if (townTileY >= 60)
            {
                return tileY >= 62;
            }

            // 5. Portales Este (si los hubiera en x >= 75)
            if (townTileX >= 75)
            {
                return tileX >= 78;
            }

            return false;
        }

        /// <summary>
        /// Determina si el buzón de la parcela debe plantarse sobre hierba (tile 175) o tierra (tile 587)
        /// según si el entorno circundante en esa posición es de hierba o de tierra.
        /// </summary>
        public static bool IsMailboxOnGrass(string doorId, int townTileX, int townTileY, int mailboxX, int mailboxY)
        {
            // Portales Norte: todos los buzones están en y=2 sobre la cresta de hierba del acantilado norte
            if (townTileY <= 2)
                return true;

            // Portales Sur: todos los buzones están en y=62 sobre la pradera verde de la ribera
            if (townTileY >= 60)
                return true;

            // Portal 02 (Oeste - Sauce): buzón en (2, 25), rodeado de césped verde junto al cerezo
            if (doorId == "Farm_Portal_02")
                return true;

            // Portales 01, 03, 04 (Oeste): buzones en x=2, y=15/35/45 sobre suelo de labor
            return mailboxX <= 1;
        }

        /// <summary>
        /// Calcula la máscara de autotiling de 4 bits (top=1, right=2, bottom=4, left=8)
        /// según la presencia de casillas vecinas en el conjunto del camino.
        /// </summary>
        public static int CalculateAutotileMask(int tileX, int tileY, Func<int, int, bool> isPathTile)
        {
            int mask = 0;
            if (isPathTile(tileX, tileY - 1)) mask |= 1;
            if (isPathTile(tileX + 1, tileY)) mask |= 2;
            if (isPathTile(tileX, tileY + 1)) mask |= 4;
            if (isPathTile(tileX - 1, tileY)) mask |= 8;
            return mask;
        }

        /// <summary>
        /// Obtiene el índice de tile de Steppingstone Path en la hoja <see cref="SteppingStonesTileSheetId"/>
        /// según sea terreno de hierba o tierra y la máscara de conexión de 4 bits.
        /// </summary>
        public static int GetSteppingStoneTileIndex(bool isGrass, int autotileMask)
        {
            int baseIndex = isGrass ? GrassBaseIndex : DirtBaseIndex;
            return baseIndex + (autotileMask & 0x0F);
        }

        /// <summary>
        /// Obtiene la información del pavimento: índice de tile y confirmación de que pertenece a <see cref="SteppingStonesTileSheetId"/>.
        /// </summary>
        public static (int TileIndex, bool IsSteppingStonesSheet) GetPathTileInfo(
            string doorId,
            int townTileX,
            int townTileY,
            int tileX,
            int tileY,
            int? underlyingTileIndex = null,
            string? underlyingType = null,
            string? underlyingSheetId = null,
            int autotileMask = 15)
        {
            // Determinación del entorno de hierba vs tierra según la zonificación canónica de la parcela
            bool isGrass = IsGrassEnvironment(doorId, townTileX, townTileY, tileX, tileY);

            int tileIndex = GetSteppingStoneTileIndex(isGrass, autotileMask);
            return (tileIndex, true);
        }

        /// <summary>
        /// Sobrecarga compatible con versiones anteriores de GetPathTileInfo.
        /// </summary>
        public static (int TileIndex, bool IsSteppingStonesSheet) GetPathTileInfo(
            string doorId,
            int townTileY,
            int tileX,
            int tileY,
            int? underlyingTileIndex,
            string? underlyingType = null,
            int autotileMask = 15)
        {
            int townTileX = doorId is "Farm_Portal_01" or "Farm_Portal_02" or "Farm_Portal_03" or "Farm_Portal_04" ? 1 : 18;
            return GetPathTileInfo(doorId, townTileX, townTileY, tileX, tileY, underlyingTileIndex, underlyingType, null, autotileMask);
        }

        /// <summary>
        /// Obtiene el índice de baldosa adecuado para un tile de senda según el terreno circundante y la ubicación.
        /// </summary>
        public static int GetPathTileIndex(string doorId, int townTileY, int tileX, int tileY, int? underlyingTileIndex, string? underlyingType = null)
        {
            return GetPathTileInfo(doorId, townTileY, tileX, tileY, underlyingTileIndex, underlyingType).TileIndex;
        }
    }
}
