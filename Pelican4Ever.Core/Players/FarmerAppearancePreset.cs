namespace Pelican4Ever.Core.Players
{
    /// <summary>
    /// Preset inmutable de apariencia y personalización del granjero (capas vanilla, colores, género y mascota).
    /// </summary>
    public sealed record FarmerAppearancePreset(
        int Skin,
        int Hair,
        string Shirt,
        string Pants,
        int Accessory,
        bool IsMale,
        int EyeColorR, int EyeColorG, int EyeColorB,
        int HairColorR, int HairColorG, int HairColorB,
        int PantsColorR, int PantsColorG, int PantsColorB,
        string PetType,
        string PetBreed,
        bool CatPerson,
        string FavoriteThing = "",
        string Name = ""
    );
}
