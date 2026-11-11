using System;
using Pelican4Ever.Core.Localization;
using StardewModdingAPI;

namespace Pelican4Ever.Framework
{
    /// <summary>
    /// Adaptador que conecta <see cref="ITranslationProvider"/> de la capa de dominio con el
    /// <see cref="ITranslationHelper"/> oficial de SMAPI.
    /// </summary>
    public sealed class SmapiTranslationProvider : ITranslationProvider
    {
        private readonly ITranslationHelper helper;

        public SmapiTranslationProvider(ITranslationHelper helper)
        {
            this.helper = helper ?? throw new ArgumentNullException(nameof(helper));
        }

        public string Get(string key)
        {
            Translation translation = helper.Get(key);
            return translation.HasValue() ? translation.ToString() : key;
        }

        public string Get(string key, object? tokens)
        {
            if (tokens == null)
                return Get(key);

            Translation translation = helper.Get(key, tokens);
            return translation.HasValue() ? translation.ToString() : TokenInterpolator.Interpolate(key, tokens);
        }
    }
}
