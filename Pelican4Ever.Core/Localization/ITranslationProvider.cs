namespace Pelican4Ever.Core.Localization
{
    /// <summary>
    /// Proveedor de traducciones desacoplado de SMAPI para su uso en la capa de dominio pura.
    /// </summary>
    public interface ITranslationProvider
    {
        /// <summary>Obtiene el texto localizado para una clave dada.</summary>
        string Get(string key);

        /// <summary>Obtiene el texto localizado para una clave dada interpolando los tokens indicados ({{token}}).</summary>
        string Get(string key, object? tokens);
    }
}
