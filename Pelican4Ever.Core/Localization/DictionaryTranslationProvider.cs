using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.Localization
{
    /// <summary>
    /// Implementación de <see cref="ITranslationProvider"/> basada en diccionario en memoria con soporte de fallback.
    /// </summary>
    public class DictionaryTranslationProvider : ITranslationProvider
    {
        private readonly IReadOnlyDictionary<string, string> entries;
        private readonly ITranslationProvider? fallback;

        public DictionaryTranslationProvider(IReadOnlyDictionary<string, string> entries, ITranslationProvider? fallback = null)
        {
            this.entries = entries ?? throw new ArgumentNullException(nameof(entries));
            this.fallback = fallback;
        }

        public string Get(string key)
        {
            if (entries.TryGetValue(key, out string? text))
                return text;
            return fallback != null ? fallback.Get(key) : key;
        }

        public string Get(string key, object? tokens)
        {
            string template = Get(key);
            return TokenInterpolator.Interpolate(template, tokens);
        }
    }
}
