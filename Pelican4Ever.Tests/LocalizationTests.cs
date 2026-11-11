using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pelican4Ever.Core.Localization;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class LocalizationTests
    {
        private static readonly Regex TokenRegex = new(@"\{\{([a-zA-Z0-9_]+)\}\}", RegexOptions.Compiled);

        private static string FindI18nPath(string fileName)
        {
            // Busca desde la carpeta de ejecución de tests hacia arriba hasta encontrar Pelican4Ever/i18n/
            string current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                string candidate = Path.Combine(current, "Pelican4Ever", "i18n", fileName);
                if (File.Exists(candidate))
                    return candidate;

                string candidateDirect = Path.Combine(current, "..", "..", "..", "..", "Pelican4Ever", "i18n", fileName);
                if (File.Exists(candidateDirect))
                    return Path.GetFullPath(candidateDirect);

                DirectoryInfo? parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }

            throw new FileNotFoundException($"No se pudo encontrar {fileName} para las pruebas de i18n.");
        }

        private static Dictionary<string, string> LoadJson(string fileName)
        {
            string path = FindI18nPath(fileName);
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? throw new InvalidOperationException($"El archivo {fileName} no contiene un objeto JSON válido.");
        }

        [Fact]
        public void DefaultAndEs_HaveIdenticalKeys()
        {
            var en = LoadJson("default.json");
            var es = LoadJson("es.json");

            var missingInEs = en.Keys.Except(es.Keys).ToList();
            var missingInEn = es.Keys.Except(en.Keys).ToList();

            Assert.Empty(missingInEs);
            Assert.Empty(missingInEn);
        }

        [Fact]
        public void DefaultAndEs_HaveMatchingTokens()
        {
            var en = LoadJson("default.json");
            var es = LoadJson("es.json");

            foreach (var kvp in en)
            {
                string key = kvp.Key;
                string enVal = kvp.Value;
                string esVal = es[key];

                var enTokens = TokenRegex.Matches(enVal).Select(m => m.Groups[1].Value).OrderBy(s => s).ToList();
                var esTokens = TokenRegex.Matches(esVal).Select(m => m.Groups[1].Value).OrderBy(s => s).ToList();

                Assert.Equal(enTokens, esTokens);
            }
        }

        [Fact]
        public void TokenInterpolator_ReplacesAnonymousObjectProperties()
        {
            string template = "Hola {{name}}, tienes {{gold}}g.";
            string result = TokenInterpolator.Interpolate(template, new { name = "Robin", gold = 500 });
            Assert.Equal("Hola Robin, tienes 500g.", result);
        }

        [Fact]
        public void TokenInterpolator_ReplacesDictionaryEntries()
        {
            string template = "Hello {{user}}, status: {{status}}.";
            var dict = new Dictionary<string, object?> { ["user"] = "Alex", ["status"] = "Active" };
            string result = TokenInterpolator.Interpolate(template, dict);
            Assert.Equal("Hello Alex, status: Active.", result);
        }

        [Fact]
        public void DictionaryTranslationProvider_ResolvesAndFallsBack()
        {
            var enDict = new Dictionary<string, string>
            {
                ["greeting"] = "Hello {{name}}!",
                ["common.ok"] = "OK"
            };
            var esDict = new Dictionary<string, string>
            {
                ["greeting"] = "¡Hola {{name}}!"
            };

            var enProvider = new DictionaryTranslationProvider(enDict);
            var esProvider = new DictionaryTranslationProvider(esDict, enProvider);

            Assert.Equal("¡Hola Clint!", esProvider.Get("greeting", new { name = "Clint" }));
            Assert.Equal("OK", esProvider.Get("common.ok"));
            Assert.Equal("unknown.key", esProvider.Get("unknown.key"));
        }

        [Fact]
        public void CoreI18n_SwitchingProvider_ChangesResolvedLanguage()
        {
            var original = CoreI18n.Current;
            try
            {
                var en = new DictionaryTranslationProvider(LoadJson("default.json"));
                var es = new DictionaryTranslationProvider(LoadJson("es.json"));

                CoreI18n.Current = en;
                Assert.Equal("closed", CoreI18n.Get("residence.permission.closed"));
                Assert.Equal("Offline rest: +25 energy recovered.", CoreI18n.Get("bed_logout.offline_rest", new { gained = 25 }));

                CoreI18n.Current = es;
                Assert.Equal("cerrada", CoreI18n.Get("residence.permission.closed"));
                Assert.Equal("Descanso offline: +25 energía recuperada.", CoreI18n.Get("bed_logout.offline_rest", new { gained = 25 }));
            }
            finally
            {
                CoreI18n.Current = original;
            }
        }

        [Fact]
        public void Modules_ResolveCorrectlyInBothLanguages()
        {
            var en = new DictionaryTranslationProvider(LoadJson("default.json"));
            var es = new DictionaryTranslationProvider(LoadJson("es.json"));

            // Festivals
            Assert.Equal("Start now (3 signed up)", en.Get("festival.signup.start_choice", new { count = 3 }));
            Assert.Equal("Empezar ahora (3 apuntados)", es.Get("festival.signup.start_choice", new { count = 3 }));

            // Chat
            Assert.Equal("Usage: /l <message> or /local <message>", en.Get("chat.local_usage"));
            Assert.Equal("Uso: /l <mensaje> o /local <mensaje>", es.Get("chat.local_usage"));

            // Clock
            Assert.Equal("[Clock] Reset to real time. Game time: 12:00.", en.Get("clock.reset", new { time = "12:00" }));
            Assert.Equal("[Reloj] Restablecido a hora real. Hora del juego: 12:00.", es.Get("clock.reset", new { time = "12:00" }));

            // Bed Logout & Rollover
            Assert.Equal("Notice: in 10 minutes the farm will be saved and connections closed for day rollover.", en.Get("rollover.warning_10min"));
            Assert.Equal("Aviso: en 10 minutos se guardará la granja y se cerrarán las conexiones para el cambio de fecha.", es.Get("rollover.warning_10min"));

            // Security
            Assert.Equal("Device paired successfully as 'Alex'!", en.Get("security.pairing_success", new { alias = "Alex" }));
            Assert.Equal("¡Dispositivo emparejado correctamente como 'Alex'!", es.Get("security.pairing_success", new { alias = "Alex" }));
        }
    }
}
