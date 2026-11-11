using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Pelican4Ever.Core.Localization
{
    /// <summary>
    /// Utilidad de interpolación de tokens compatible con la sintaxis de SMAPI {{token}}.
    /// Soporta diccionarios u objetos anónimos.
    /// </summary>
    public static class TokenInterpolator
    {
        private static readonly Regex TokenRegex = new(@"\{\{([a-zA-Z0-9_]+)\}\}", RegexOptions.Compiled);
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

        public static string Interpolate(string template, object? tokens)
        {
            if (string.IsNullOrEmpty(template) || tokens == null)
                return template;

            if (tokens is IDictionary<string, object?> dict)
            {
                return TokenRegex.Replace(template, m =>
                {
                    string key = m.Groups[1].Value;
                    return dict.TryGetValue(key, out object? val) && val != null ? val.ToString()! : m.Value;
                });
            }

            Type type = tokens.GetType();
            PropertyInfo[] props = PropertyCache.GetOrAdd(type, t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance));

            return TokenRegex.Replace(template, m =>
            {
                string key = m.Groups[1].Value;
                foreach (PropertyInfo prop in props)
                {
                    if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
                    {
                        object? val = prop.GetValue(tokens);
                        return val != null ? val.ToString()! : string.Empty;
                    }
                }
                return m.Value;
            });
        }
    }
}
