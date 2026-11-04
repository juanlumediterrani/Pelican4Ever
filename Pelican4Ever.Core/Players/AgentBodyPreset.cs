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
    }
}
