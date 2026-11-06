using System;
using System.Globalization;

namespace Pelican4Ever.Core.World
{
    /// <summary>Destino de un comando de prueba: una residencia, uno de sus anexos o la granja principal.</summary>
    public readonly record struct FarmTestTarget(string? ResidenceSlotId, AnnexSide? AnnexSide, bool IsHostFarm)
    {
        /// <summary>Nombre de la ubicación: <c>Farm</c>, <c>Pelican4Ever_Farm_NN</c> o <c>Pelican4Ever_Annex_NN_Lado</c>.</summary>
        public string LocationName => IsHostFarm
            ? "Farm"
            : AnnexSide is { } side ? ServerWorldState.BuildAnnexId(ResidenceSlotId!, side) : ResidenceSlotId!;
    }

    /// <summary>Punto de llegada de <c>p4e_farm_tp</c>.</summary>
    public enum FarmTestEntry { House, North, South, East, Cave }

    /// <summary>Qué genera <c>p4e_farm_spawn</c>.</summary>
    public enum FarmTestSpawn { Forage, Ores, Crate, Monsters }

    /// <summary>Clima que fija <c>p4e_weather</c>.</summary>
    public enum FarmTestWeather { Sun, Rain, Storm }

    /// <summary>Análisis de argumentos de los comandos de prueba de residencias (TASK-607).</summary>
    public static class FarmTestCommandLogic
    {
        /// <summary>Índices de <c>Farmer.experiencePoints</c>.</summary>
        public static readonly string[] SkillNames = { "farming", "fishing", "foraging", "mining", "combat", "luck" };

        /// <summary>
        /// Interpreta <c>host</c>, <c>03</c>, <c>3</c>, <c>Pelican4Ever_Farm_03</c>, <c>03/North</c> o <c>Pelican4Ever_Annex_03_North</c>.
        /// </summary>
        public static bool TryParseTarget(string? text, out FarmTestTarget target)
        {
            target = default;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string value = text.Trim();
            if (value.Equals("host", StringComparison.OrdinalIgnoreCase) || value.Equals("Farm", StringComparison.OrdinalIgnoreCase))
            {
                target = new FarmTestTarget(null, null, true);
                return true;
            }

            string? sidePart = null;
            if (value.StartsWith(LocationCullingLogic.AnnexPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string rest = value.Substring(LocationCullingLogic.AnnexPrefix.Length);
                int underscore = rest.LastIndexOf('_');
                if (underscore <= 0)
                    return false;
                value = rest.Substring(0, underscore);
                sidePart = rest.Substring(underscore + 1);
            }
            else
            {
                int slash = value.IndexOf('/');
                if (slash >= 0)
                {
                    sidePart = value.Substring(slash + 1);
                    value = value.Substring(0, slash);
                }
            }

            if (value.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase))
                value = value.Substring(LocationCullingLogic.FarmPrefix.Length);

            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < 1 || number > 99)
                return false;

            AnnexSide? side = null;
            if (sidePart != null)
            {
                if (!Enum.TryParse(sidePart, ignoreCase: true, out AnnexSide parsed) || !Enum.IsDefined(typeof(AnnexSide), parsed))
                    return false;
                side = parsed;
            }

            target = new FarmTestTarget($"{LocationCullingLogic.FarmPrefix}{number:00}", side, false);
            return true;
        }

        /// <summary>Plantilla de residencia por id, sin distinguir mayúsculas; acepta "Meadowlands" como Pradera.</summary>
        public static bool TryParseFarmType(string? text, out string templateId)
        {
            templateId = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string value = text.Trim();
            if (FarmGrassLogic.IsMeadowlands(value))
                value = "Ranching";

            foreach (string id in FarmTemplateCatalog.Templates.Keys)
            {
                if (string.Equals(id, value, StringComparison.OrdinalIgnoreCase))
                {
                    templateId = id;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Índice de habilidad (0 agricultura … 5 suerte) por su nombre en inglés.</summary>
        public static bool TryParseSkill(string? text, out int skillIndex)
        {
            skillIndex = string.IsNullOrWhiteSpace(text)
                ? -1
                : Array.FindIndex(SkillNames, n => n.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase));
            return skillIndex >= 0;
        }

        /// <summary>Experiencia mínima de cada nivel (réplica de <c>Farmer.getBaseExperienceForLevel</c>; nivel 0 = 0).</summary>
        public static int ExperienceForLevel(int level) => level switch
        {
            <= 0 => 0,
            1 => 100,
            2 => 380,
            3 => 770,
            4 => 1300,
            5 => 2150,
            6 => 3300,
            7 => 4800,
            8 => 6900,
            9 => 10000,
            _ => 15000,
        };

        /// <summary>Lee un entero acotado; si falta el texto devuelve <paramref name="fallback"/>.</summary>
        public static bool TryParseCount(string? text, int fallback, int min, int max, out int count)
        {
            count = fallback;
            if (string.IsNullOrWhiteSpace(text))
                return true;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out count) && count >= min && count <= max;
        }

        public static bool TryParseEntry(string? text, out FarmTestEntry entry) => TryParseEnum(text, out entry);

        public static bool TryParseSpawn(string? text, out FarmTestSpawn spawn) => TryParseEnum(text, out spawn);

        public static bool TryParseWeather(string? text, out FarmTestWeather weather) => TryParseEnum(text, out weather);

        /// <summary>Nombre del clima vanilla (<c>Game1.weather_*</c>).</summary>
        public static string VanillaWeatherId(FarmTestWeather weather) => weather switch
        {
            FarmTestWeather.Rain => "Rain",
            FarmTestWeather.Storm => "Storm",
            _ => "Sun",
        };

        private static bool TryParseEnum<T>(string? text, out T value) where T : struct, Enum
        {
            value = default;
            return !string.IsNullOrWhiteSpace(text)
                && !int.TryParse(text, out _)
                && Enum.TryParse(text.Trim(), ignoreCase: true, out value)
                && Enum.IsDefined(typeof(T), value);
        }
    }
}
