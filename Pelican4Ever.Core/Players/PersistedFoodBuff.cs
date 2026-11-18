using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.Players
{
    /// <summary>
    /// Atributos aditivos y multiplicativos de un buff de comida o bebida (espejo puro de <c>BuffEffects</c>).
    /// </summary>
    public sealed class FoodBuffEffectsSnapshot
    {
        public float CombatLevel { get; set; }
        public float FarmingLevel { get; set; }
        public float FishingLevel { get; set; }
        public float MiningLevel { get; set; }
        public float LuckLevel { get; set; }
        public float ForagingLevel { get; set; }
        public float MaxStamina { get; set; }
        public float MagneticRadius { get; set; }
        public float Speed { get; set; }
        public float Defense { get; set; }
        public float Attack { get; set; }
        public float Immunity { get; set; }

        public float AttackMultiplier { get; set; }
        public float KnockbackMultiplier { get; set; }
        public float WeaponSpeedMultiplier { get; set; }
        public float CriticalChanceMultiplier { get; set; }
        public float CriticalPowerMultiplier { get; set; }
        public float WeaponPrecisionMultiplier { get; set; }

        public bool HasAnyValue()
        {
            return CombatLevel != 0f
                || FarmingLevel != 0f
                || FishingLevel != 0f
                || MiningLevel != 0f
                || LuckLevel != 0f
                || ForagingLevel != 0f
                || MaxStamina != 0f
                || MagneticRadius != 0f
                || Speed != 0f
                || Defense != 0f
                || Attack != 0f
                || Immunity != 0f
                || AttackMultiplier != 0f
                || KnockbackMultiplier != 0f
                || WeaponSpeedMultiplier != 0f
                || CriticalChanceMultiplier != 0f
                || CriticalPowerMultiplier != 0f
                || WeaponPrecisionMultiplier != 0f;
        }

        public FoodBuffEffectsSnapshot CloneSanitized()
        {
            return new FoodBuffEffectsSnapshot
            {
                CombatLevel = SanitizeFloat(CombatLevel),
                FarmingLevel = SanitizeFloat(FarmingLevel),
                FishingLevel = SanitizeFloat(FishingLevel),
                MiningLevel = SanitizeFloat(MiningLevel),
                LuckLevel = SanitizeFloat(LuckLevel),
                ForagingLevel = SanitizeFloat(ForagingLevel),
                MaxStamina = SanitizeFloat(MaxStamina),
                MagneticRadius = SanitizeFloat(MagneticRadius),
                Speed = SanitizeFloat(Speed),
                Defense = SanitizeFloat(Defense),
                Attack = SanitizeFloat(Attack),
                Immunity = SanitizeFloat(Immunity),
                AttackMultiplier = SanitizeFloat(AttackMultiplier),
                KnockbackMultiplier = SanitizeFloat(KnockbackMultiplier),
                WeaponSpeedMultiplier = SanitizeFloat(WeaponSpeedMultiplier),
                CriticalChanceMultiplier = SanitizeFloat(CriticalChanceMultiplier),
                CriticalPowerMultiplier = SanitizeFloat(CriticalPowerMultiplier),
                WeaponPrecisionMultiplier = SanitizeFloat(WeaponPrecisionMultiplier)
            };
        }

        private static float SanitizeFloat(float value) => float.IsFinite(value) ? value : 0f;
    }

    /// <summary>
    /// Buff de comida o bebida activo persistido en <see cref="PlayerRecord"/> para sobrevivir desconexiones y reinicios
    /// mientras su duración real UTC no haya expirado.
    /// </summary>
    public sealed class PersistedFoodBuff
    {
        public string Id { get; set; } = string.Empty;

        public string? Source { get; set; }

        public string? DisplaySource { get; set; }

        public string? DisplayName { get; set; }

        public string? Description { get; set; }

        public string? IconTextureName { get; set; }

        public int IconSheetIndex { get; set; } = -1;

        public byte GlowR { get; set; } = 255;

        public byte GlowG { get; set; } = 255;

        public byte GlowB { get; set; } = 255;

        public byte GlowA { get; set; } = 255;

        public int TotalDurationMs { get; set; }

        public DateTimeOffset ExpiresAtUtc { get; set; } = DateTimeOffset.MinValue;

        public bool Visible { get; set; } = true;

        public FoodBuffEffectsSnapshot Effects { get; set; } = new();

        public Dictionary<string, string>? CustomFields { get; set; }

        public int RemainingMilliseconds(DateTimeOffset nowUtc)
        {
            if (ExpiresAtUtc <= nowUtc)
                return 0;

            double remainingMs = Math.Ceiling((ExpiresAtUtc - nowUtc).TotalMilliseconds);
            if (!double.IsFinite(remainingMs) || remainingMs <= 0)
                return 0;

            return (int)Math.Min(remainingMs, FoodBuffPersistenceLogic.MaxFoodBuffDurationMs);
        }

        public PersistedFoodBuff Clone()
        {
            return new PersistedFoodBuff
            {
                Id = Id,
                Source = Source,
                DisplaySource = DisplaySource,
                DisplayName = DisplayName,
                Description = Description,
                IconTextureName = IconTextureName,
                IconSheetIndex = IconSheetIndex,
                GlowR = GlowR,
                GlowG = GlowG,
                GlowB = GlowB,
                GlowA = GlowA,
                TotalDurationMs = TotalDurationMs,
                ExpiresAtUtc = ExpiresAtUtc,
                Visible = Visible,
                Effects = Effects?.CloneSanitized() ?? new FoodBuffEffectsSnapshot(),
                CustomFields = CustomFields != null ? new Dictionary<string, string>(CustomFields, StringComparer.Ordinal) : null
            };
        }

        public FoodBuffReportEntry ToReportEntry(DateTimeOffset nowUtc)
        {
            return new FoodBuffReportEntry
            {
                Id = Id,
                Source = Source,
                DisplaySource = DisplaySource,
                DisplayName = DisplayName,
                Description = Description,
                IconTextureName = IconTextureName,
                IconSheetIndex = IconSheetIndex,
                GlowR = GlowR,
                GlowG = GlowG,
                GlowB = GlowB,
                GlowA = GlowA,
                TotalDurationMs = TotalDurationMs,
                RemainingMilliseconds = RemainingMilliseconds(nowUtc),
                Visible = Visible,
                Effects = Effects?.CloneSanitized() ?? new FoodBuffEffectsSnapshot(),
                CustomFields = CustomFields != null ? new Dictionary<string, string>(CustomFields, StringComparer.Ordinal) : null
            };
        }
    }

    /// <summary>
    /// DTO de red para sincronizar buffs de comida activos usando duración restante relativa (evita desfase de relojes).
    /// </summary>
    public sealed class FoodBuffReportEntry
    {
        public string Id { get; set; } = string.Empty;

        public string? Source { get; set; }

        public string? DisplaySource { get; set; }

        public string? DisplayName { get; set; }

        public string? Description { get; set; }

        public string? IconTextureName { get; set; }

        public int IconSheetIndex { get; set; } = -1;

        public byte GlowR { get; set; } = 255;

        public byte GlowG { get; set; } = 255;

        public byte GlowB { get; set; } = 255;

        public byte GlowA { get; set; } = 255;

        public int TotalDurationMs { get; set; }

        public int RemainingMilliseconds { get; set; }

        public bool Visible { get; set; } = true;

        public FoodBuffEffectsSnapshot Effects { get; set; } = new();

        public Dictionary<string, string>? CustomFields { get; set; }

        public PersistedFoodBuff ToPersisted(DateTimeOffset nowUtc, int clampedRemainingMs)
        {
            return new PersistedFoodBuff
            {
                Id = Id,
                Source = Source,
                DisplaySource = DisplaySource,
                DisplayName = DisplayName,
                Description = Description,
                IconTextureName = IconTextureName,
                IconSheetIndex = IconSheetIndex,
                GlowR = GlowR,
                GlowG = GlowG,
                GlowB = GlowB,
                GlowA = GlowA,
                TotalDurationMs = Math.Max(TotalDurationMs, clampedRemainingMs),
                ExpiresAtUtc = nowUtc + TimeSpan.FromMilliseconds(clampedRemainingMs),
                Visible = Visible,
                Effects = Effects?.CloneSanitized() ?? new FoodBuffEffectsSnapshot(),
                CustomFields = CustomFields != null ? new Dictionary<string, string>(CustomFields, StringComparer.Ordinal) : null
            };
        }
    }

    /// <summary>Carga útil de red para informes y snapshots de buffs de comida activos.</summary>
    public sealed class FoodBuffsPayload
    {
        public List<FoodBuffReportEntry> Buffs { get; set; } = new();
    }

    /// <summary>Reglas puras de validación y filtrado para la persistencia de buffs de comida.</summary>
    public static class FoodBuffPersistenceLogic
    {
        /// <summary>Número máximo de buffs activos persistidos por jugador.</summary>
        public const int MaxPersistedFoodBuffs = 16;

        /// <summary>Tope de duración restante aceptado por el host (48 horas reales).</summary>
        public const int MaxFoodBuffDurationMs = 48 * 60 * 60 * 1000;

        /// <summary>
        /// Devuelve true si el identificador corresponde a un buff transitorio de mod externo (p. ej. carga de red
        /// <c>NatCSpeedN</c> de Nature In The Valley) cuya duración en milisegundos reales no debe escalarse ni persistirse.
        /// </summary>
        public static bool IsTransientModBuffId(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;

            return string.Equals(id, "NatCSpeedN", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Devuelve true si el identificador corresponde a un debuff o efecto de combate que nunca debe
        /// escalarse ni persistirse como buff de comida.
        /// </summary>
        public static bool IsDisallowedBuffId(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return true;

            if (IsTransientModBuffId(id))
                return true;

            if (id is "6" or "7" or "12" or "13" or "14" or "17" or "18" or "19" or "20" or "21" or "24" or "25" or "26" or "27")
                return true;

            string lower = id.ToLowerInvariant();
            return lower.Contains("debuff", StringComparison.Ordinal)
                || lower.Contains("slimed", StringComparison.Ordinal)
                || lower.Contains("frozen", StringComparison.Ordinal);
        }

        public static int ClampRemainingMs(int remainingMs)
            => Math.Clamp(remainingMs, 0, MaxFoodBuffDurationMs);

        public static List<PersistedFoodBuff> NormalizeIncoming(
            FoodBuffsPayload? payload,
            DateTimeOffset nowUtc,
            Func<string, bool>? isRejectedBuffId = null)
        {
            var result = new List<PersistedFoodBuff>();
            if (payload?.Buffs == null || payload.Buffs.Count == 0)
                return result;

            var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (FoodBuffReportEntry? entry in payload.Buffs)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Id))
                    continue;

                if (IsDisallowedBuffId(entry.Id) || (isRejectedBuffId != null && isRejectedBuffId(entry.Id)))
                    continue;

                int remainingMs = ClampRemainingMs(entry.RemainingMilliseconds);
                if (remainingMs <= 0)
                    continue;

                PersistedFoodBuff persisted = entry.ToPersisted(nowUtc, remainingMs);
                if (indexById.TryGetValue(persisted.Id, out int existingIndex))
                {
                    result[existingIndex] = persisted;
                }
                else
                {
                    if (result.Count >= MaxPersistedFoodBuffs)
                        continue;

                    indexById[persisted.Id] = result.Count;
                    result.Add(persisted);
                }
            }

            return result;
        }
    }
}
