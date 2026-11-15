using System;
using Pelican4Ever.Core.Localization;
using StardewModdingAPI;

namespace Pelican4Ever
{
    /// <summary>
    /// Fachada estática de internacionalización para Pelican 4Ever.
    /// Resuelve cadenas según el idioma configurado por el jugador en Stardew Valley.
    /// </summary>
    public static class I18n
    {
        private static ITranslationProvider provider = new FallbackTranslationProvider();

        public static void Init(ITranslationHelper helper)
        {
            var smapiProvider = new Framework.SmapiTranslationProvider(helper);
            provider = smapiProvider;
            CoreI18n.SetGlobal(smapiProvider);
        }

        public static string Get(string key) => provider.Get(key);

        public static string Get(string key, object? tokens) => provider.Get(key, tokens);
    }
}
