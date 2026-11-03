using System;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Estilo estético y arquitectónico para las entradas residenciales de la plaza.
    /// Permite personalizar cada entrada con acabados canónicos vanilla.
    /// </summary>
    public enum EntranceStyle
    {
        Default,
        MountainGorge,
        WoodPlanks,
        Cobblestone,
        StoneWalkway,
        SteppingStones
    }

    /// <summary>
    /// Clasificador de tiles de terreno y selección de baldosas/losas para caminos residenciales (TASK-501).
    /// Proporciona la lógica de selección de Steppingstone Path (camino de peldaños / stepping stones)
    /// adaptado tanto a hierba como a tierra con autotiling canónico de 4 bits.
    /// </summary>
    public static class TerrainTileClassifier
    {
        /// <summary>
        /// Determina el estilo arquitectónico personalizado de una entrada residencial según su ubicación y orientación.
        /// </summary>
        public static EntranceStyle ResolveEntranceStyle(string locationName, string? doorId, PortalEdge edge, int townTileX, int townTileY)
        {
            if (string.Equals(locationName, HostPlazaLayout.HostFarm, StringComparison.OrdinalIgnoreCase))
            {
                if (edge == PortalEdge.North)
                    return townTileX <= 30 ? EntranceStyle.MountainGorge : EntranceStyle.WoodPlanks;
                return townTileX <= 30 ? EntranceStyle.Cobblestone : EntranceStyle.StoneWalkway;
            }

            // En los puentes (Pelican4Ever_Bridge_1..3):
            if (edge == PortalEdge.North)
            {
                // Izquierda (junto a la vagoneta): estilo de andén de madera
                // Derecha (cortando el acantilado): desfiladero de roca
                return townTileX <= 15 ? EntranceStyle.WoodPlanks : EntranceStyle.MountainGorge;
            }
            else
            {
                // Sur izquierda: adoquines que bajan de la acera
                // Sur derecha: losas de piedra natural
                return townTileX <= 15 ? EntranceStyle.Cobblestone : EntranceStyle.StoneWalkway;
            }
        }
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
        /// Determina si una coordenada en la senda residencial de la Plaza Central o Mapa Puente corresponde a un entorno de hierba o tierra.
        /// </summary>
        public static bool IsGrassEnvironment(
            PortalEdge edge,
            PlazaLocation loc,
            int portalX,
            int portalY,
            int tileX,
            int tileY)
        {
            bool isFarm = string.Equals(loc.Name, HostPlazaLayout.HostFarm, StringComparison.OrdinalIgnoreCase);
            return edge switch
            {
                PortalEdge.North => isFarm ? (portalX > 30 ? tileY <= 10 : tileY <= 3) : tileY <= 5,
                PortalEdge.South => isFarm ? tileY >= portalY - 1 : tileY >= portalY - 3,
                PortalEdge.West => tileX <= 1,
                PortalEdge.East => tileX >= loc.Width - 2,
                _ => false
            };
        }

        /// <summary>
        /// Determina si el buzón de la parcela debe plantarse sobre hierba (tile 175) o tierra (tile 587)
        /// según si el entorno circundante en esa posición es de hierba o de tierra.
        /// </summary>
        public static bool IsMailboxOnGrass(
            PortalEdge edge,
            PlazaLocation loc,
            int portalX,
            int portalY,
            int mailboxX,
            int mailboxY)
        {
            return edge switch
            {
                PortalEdge.North or PortalEdge.South => true,
                PortalEdge.West => mailboxX <= 1,
                PortalEdge.East => mailboxX >= loc.Width - 2,
                _ => false
            };
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
        /// Obtiene la información del pavimento: índice de tile y confirmación de si pertenece a <see cref="SteppingStonesTileSheetId"/>.
        /// </summary>
        public static (int TileIndex, bool IsSteppingStonesSheet) GetPathTileInfo(
            PortalEdge edge,
            PlazaLocation loc,
            int portalX,
            int portalY,
            int tileX,
            int tileY,
            int? underlyingTileIndex = null,
            string? underlyingType = null,
            string? underlyingSheetId = null,
            int autotileMask = 15,
            EntranceStyle style = EntranceStyle.Default)
        {
            bool isGrass = IsGrassEnvironment(edge, loc, portalX, portalY, tileX, tileY);

            // Sin baldosas artificiales: únicamente tierra (587) y camino de tierra con bordes suaves de hierba
            // (225 borde izquierdo, 228 borde derecho en caminos verticales; 201/251 en horizontales) de spring_outdoorsTileSheet.
            if (!isGrass)
            {
                return (587, false);
            }

            if (edge is PortalEdge.North or PortalEdge.South)
            {
                int grassEdgeDirtTile = (tileX == portalX) ? 225 : 228;
                return (grassEdgeDirtTile, false);
            }
            else
            {
                int horizEdgeDirtTile = (tileY == portalY) ? 201 : 251;
                return (horizEdgeDirtTile, false);
            }
        }

        /// <summary>
        /// Obtiene el índice de baldosa adecuado para un tile de senda según el terreno circundante y la ubicación.
        /// </summary>
        public static int GetPathTileIndex(
            PortalEdge edge,
            PlazaLocation loc,
            int portalX,
            int portalY,
            int tileX,
            int tileY,
            int? underlyingTileIndex = null,
            string? underlyingType = null,
            EntranceStyle style = EntranceStyle.Default)
        {
            return GetPathTileInfo(edge, loc, portalX, portalY, tileX, tileY, underlyingTileIndex, underlyingType, null, 15, style).TileIndex;
        }

        /// <summary>
        /// Genera de forma determinista una baldosa de terreno salvaje (pradera soleada de Stardew Valley)
        /// a partir de las coordenadas (x, y) usando tiles auténticos de spring_outdoorsTileSheet.
        /// </summary>
        public static (int TileIndex, string Type) GetWildTerrainTile(int x, int y)
        {
            int hash = ((x * 73856093) ^ (y * 19349663)) & 0x7FFFFFFF;
            int roll = hash % 100;

            if (roll < 82)
                return (175, "Grass"); // Hierba verde soleada estándar
            if (roll < 89)
                return (150, "Grass"); // Hierba soleada con brizna suave
            if (roll < 94)
                return (256, "Grass"); // Hierba soleada con textura tupida
            if (roll < 97)
                return (151, "Grass"); // Hierba soleada con florecillas blancas/azules
            return (152, "Grass");     // Hierba soleada con florecillas silvestres
        }
    }
}
