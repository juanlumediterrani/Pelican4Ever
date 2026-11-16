using System.Collections.Generic;

namespace Pelican4Ever.Core.Players
{
    /// <summary>
    /// Valores de <c>Farmer.modData</c> que seleccionan en Fashion Sense el cuerpo de un agente.
    /// Fashion Sense guarda cada apariencia como <c>{pack}/{tipo}/{nombre}</c> bajo
    /// <c>FashionSense.Custom{Tipo}.Id</c>; un valor vacío vuelve a la apariencia vanilla.
    /// Su API pública no admite fijar el cuerpo (tipo <c>Player</c>), por eso se escribe el modData.
    /// </summary>
    public static class AgentBodyPreset
    {
        /// <summary>UniqueID del pack <c>agents/Dr4co.FashionSense</c>.</summary>
        public const string Dr4coPackId = "Dr4co.Stardew.FashionSenseBody";

        /// <summary>Clave de <c>Farmer.modData</c> para la skin nativa completa de 126 cuadros de Pelican 4Ever.</summary>
        public const string AgentSkinModDataKey = "Pelican4Ever.AgentSkin";

        /// <summary>Identificador canónico de la skin de Putre.</summary>
        public const string PutreSkinId = "putre";

        /// <summary>Identificador canónico de la skin de Dr4co.</summary>
        public const string Dr4coSkinId = "dr4co";

        /// <summary>Skins nativas disponibles incluidas en <c>Pelican4Ever/assets/skins/</c>.</summary>
        public static IReadOnlyList<string> AvailableAgentSkins { get; } = new[]
        {
            PutreSkinId,
            Dr4coSkinId,
        };

        /// <summary>
        /// Claves de modData de Fashion Sense que se deben limpiar al aplicar o volver a una apariencia vanilla pura.
        /// </summary>
        public static IReadOnlyList<string> FashionSenseOverrideKeys { get; } = new[]
        {
            AgentToolOffset.BodyModDataKey,
            "FashionSense.CustomHair.Id",
            "FashionSense.CustomShirt.Id",
            "FashionSense.CustomPants.Id",
            "FashionSense.CustomShoes.Id",
        };

        /// <summary>
        /// Preset canónico del granjero Putre como orco guerrero (placeholder en capas vanilla de Stardew Valley).
        /// Parámetros extraídos de la pantalla de creación:
        /// - Piel verde: índice 17 (18 en UI).
        /// - Cresta / mohawk: 50.
        /// - Camisa con hombreras metálicas: "21".
        /// - Pantalón: "1".
        /// - Accesorio barba completa: 2.
        /// - Género masculino (true).
        /// - Ojos rojos: RGB(181, 50, 21) [deslizadores H:3, S:88, V:71].
        /// - Pelo y barba verde musgo: RGB(45, 91, 23) [deslizadores H:28, S:74, V:36].
        /// - Pantalón gris oliva oscuro: RGB(36, 43, 37) [deslizadores H:35, S:15, V:17].
        /// - Mascota: Perro raza 0 (perro clásico marrón de orejas caídas).
        /// - Cosa favorita: "Zerbesa".
        /// </summary>
        public static readonly FarmerAppearancePreset PutrePreset = new(
            Skin: 17,
            Hair: 50,
            Shirt: "21",
            Pants: "1",
            Accessory: 2,
            IsMale: true,
            EyeColorR: 181, EyeColorG: 50, EyeColorB: 21,
            HairColorR: 45, HairColorG: 91, HairColorB: 23,
            PantsColorR: 36, PantsColorG: 43, PantsColorB: 37,
            PetType: "Dog",
            PetBreed: "0",
            CatPerson: false,
            FavoriteThing: "Zerbesa",
            Name: "Putre"
        );

        /// <summary>
        /// Preset de apariencia estándar del granjero vanilla por defecto (para restaurar con off).
        /// </summary>
        public static readonly FarmerAppearancePreset DefaultVanillaPreset = new(
            Skin: 0,
            Hair: 0,
            Shirt: "0",
            Pants: "0",
            Accessory: -1,
            IsMale: true,
            EyeColorR: 122, EyeColorG: 68, EyeColorB: 52,
            HairColorR: 193, HairColorG: 90, HairColorB: 50,
            PantsColorR: 46, PantsColorG: 85, PantsColorB: 183,
            PetType: "Dog",
            PetBreed: "0",
            CatPerson: false,
            FavoriteThing: "",
            Name: ""
        );

        /// <summary>
        /// Claves y valores para activar (o, con <paramref name="enable"/> a false, quitar) el cuerpo de Dr4co.
        /// Incluye pelo, camisa, pantalón y zapatos <c>None</c> del pack: si no, Fashion Sense dibuja las capas
        /// vanilla encima del cuerpo y se ve el granjero humano sobre el dragón.
        /// </summary>
        public static IReadOnlyDictionary<string, string> Dr4co(bool enable)
        {
            string Id(string type, string name) => enable ? $"{Dr4coPackId}/{type}/{name}" : string.Empty;

            return new Dictionary<string, string>
            {
                [AgentToolOffset.BodyModDataKey] = Id("Player", "Dr4co"),
                ["FashionSense.CustomHair.Id"] = Id("Hair", "None"),
                ["FashionSense.CustomShirt.Id"] = Id("Shirt", "None"),
                ["FashionSense.CustomPants.Id"] = Id("Pants", "None"),
                ["FashionSense.CustomShoes.Id"] = Id("Shoes", "None"),
            };
        }

        /// <summary>Interpreta el argumento on/off del comando (por defecto, activar).</summary>
        public static bool? ParseToggle(string? arg)
        {
            if (string.IsNullOrWhiteSpace(arg))
                return true;

            return arg.Trim().ToLowerInvariant() switch
            {
                "on" or "si" or "sí" or "1" or "true" => true,
                "off" or "no" or "0" or "false" => false,
                _ => null
            };
        }

        /// <summary>
        /// Normaliza el identificador de skin nativa (<c>"putre"</c>, <c>"dr4co"</c> o <c>""</c> para quitarla).
        /// Devuelve <c>null</c> si el argumento no corresponde a ninguna skin conocida ni a desactivar.
        /// </summary>
        public static string? ParseSkinId(string? arg)
        {
            if (string.IsNullOrWhiteSpace(arg))
                return null;

            return arg.Trim().ToLowerInvariant() switch
            {
                "putre" => PutreSkinId,
                "dr4co" => Dr4coSkinId,
                "off" or "none" or "vanilla" or "clear" or "reset" or "quitar" or "no" or "0" or "false" => string.Empty,
                _ => null
            };
        }

        /// <summary>Rutas relativas dentro del mod para la hoja normal y la hoja carry (144x672, 126 cuadros).</summary>
        public static (string NormalAsset, string CarryAsset)? GetSkinAssetPaths(string? skinId)
        {
            return skinId?.Trim().ToLowerInvariant() switch
            {
                PutreSkinId => ("assets/skins/putre_sheet.png", "assets/skins/putre_carry_sheet.png"),
                Dr4coSkinId => ("assets/skins/dr4co_sheet.png", "assets/skins/dr4co_carry_sheet.png"),
                _ => null
            };
        }

        /// <summary>
        /// Desplazamiento de origen (en píxeles de arte 1x) para centrar una celda de 24x32 sobre
        /// el marco vanilla de 16x32 respetando pies y espejado horizontal.
        /// </summary>
        public static (float OriginXOffset, float OriginYOffset) GetDrawOriginOffset(string? skinId, bool flip)
        {
            return skinId?.Trim().ToLowerInvariant() switch
            {
                PutreSkinId => (4f, 0f),
                Dr4coSkinId => (flip ? 6f : 2f, -2f),
                _ => (0f, 0f)
            };
        }

        /// <summary>Recorte de 24x32 en la hoja de 6x21 cuadros (144x672) para un índice de cuadro 0..125.</summary>
        public static (int X, int Y, int Width, int Height) GetFrameSourceRect(int frame)
        {
            int clamped = frame < 0 ? 0 : (frame >= 126 ? 125 : frame);
            return ((clamped % 6) * 24, (clamped / 6) * 32, 24, 32);
        }
    }
}
