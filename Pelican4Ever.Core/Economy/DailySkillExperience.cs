using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.Economy
{
    /// <summary>Estado por jugador; el día y las fracciones se guardan junto al mundo del host.</summary>
    public sealed class DailySkillExperience
    {
        public int GameDay { get; set; } = -1;
        public Dictionary<string, SkillExperienceProgress> Skills { get; set; } = new();

        public void EnsureDay(int day)
        {
            if (GameDay == day && Skills != null) return;
            GameDay = day;
            Skills = new();
        }

        public int Apply(int day, string skill, int amount, decimal multiplier, int cap, decimal overflow)
        {
            EnsureDay(day);
            Skills.TryGetValue(skill, out SkillExperienceProgress? progress);
            var result = SkillExperienceCurve.Apply(progress?.EarnedToday ?? 0, amount, multiplier, cap, overflow, progress?.Remainder ?? 0);
            Skills[skill] = new SkillExperienceProgress
            {
                EarnedToday = result.EarnedToday, Remainder = result.Remainder,
                RawXpToday = checked((progress?.RawXpToday ?? 0) + amount),
                GrantedXpToday = checked((progress?.GrantedXpToday ?? 0) + result.GrantedXp)
            };
            return result.GrantedXp;
        }

        /// <summary>Acepta solo avances válidos del día actual; informes antiguos no pueden rebajar el contador.</summary>
        public void MergeReport(DailySkillExperience report, int day)
        {
            EnsureDay(day);
            if (report == null || report.GameDay != day || report.Skills == null) return;
            foreach (var pair in report.Skills)
            {
                if (pair.Key is not ("Farming" or "Fishing" or "Foraging" or "Mining" or "Combat") || pair.Value == null
                    || pair.Value.EarnedToday < 0 || pair.Value.EarnedToday > int.MaxValue
                    || pair.Value.Remainder < 0 || pair.Value.Remainder >= 1
                    || pair.Value.RawXpToday < 0 || pair.Value.GrantedXpToday < 0)
                    continue;
                if (Skills.TryGetValue(pair.Key, out var current) && current != null
                    && (pair.Value.EarnedToday < current.EarnedToday || pair.Value.RawXpToday < current.RawXpToday
                        || pair.Value.GrantedXpToday < current.GrantedXpToday
                        || (pair.Value.EarnedToday == current.EarnedToday && pair.Value.RawXpToday == current.RawXpToday)))
                    continue;
                Skills[pair.Key] = new SkillExperienceProgress
                {
                    EarnedToday = pair.Value.EarnedToday, Remainder = pair.Value.Remainder,
                    RawXpToday = pair.Value.RawXpToday, GrantedXpToday = pair.Value.GrantedXpToday
                };
            }
        }
    }

    public sealed class SkillExperienceProgress
    {
        public decimal EarnedToday { get; set; }
        public decimal Remainder { get; set; }
        public long RawXpToday { get; set; }
        public long GrantedXpToday { get; set; }
    }
}
