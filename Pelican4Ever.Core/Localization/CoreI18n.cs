using System;
using System.Threading;

namespace Pelican4Ever.Core.Localization
{
    /// <summary>
    /// Punto de acceso estático para traducciones en la capa de dominio pura.
    /// Se inicializa con <see cref="FallbackTranslationProvider"/> por defecto y
    /// puede reconfigurarse en runtime desde SMAPI o durante los tests.
    /// </summary>
    public static class CoreI18n
    {
        private static readonly ITranslationProvider DefaultProvider = new FallbackTranslationProvider();
        private static readonly AsyncLocal<ITranslationProvider?> LocalCurrent = new();
        private static volatile ITranslationProvider globalCurrent = DefaultProvider;

        public static ITranslationProvider Current
        {
            get => LocalCurrent.Value ?? globalCurrent;
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                LocalCurrent.Value = value;
            }
        }

        public static void SetGlobal(ITranslationProvider provider)
        {
            globalCurrent = provider ?? throw new ArgumentNullException(nameof(provider));
            LocalCurrent.Value = null;
        }

        public static void ResetToDefault()
        {
            LocalCurrent.Value = null;
            globalCurrent = DefaultProvider;
        }

        public static string Get(string key) => Current.Get(key);

        public static string Get(string key, object? tokens) => Current.Get(key, tokens);
    }
}
