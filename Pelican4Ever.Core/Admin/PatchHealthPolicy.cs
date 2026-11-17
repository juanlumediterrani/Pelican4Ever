using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.Admin
{
    /// <summary>Criticidad operativa de un parche Harmony (ADR-0010, TASK-2501).</summary>
    public enum PatchCriticality
    {
        Security = 0,
        Integrity = 1,
        Experience = 2
    }

    /// <summary>Resultado individual al intentar registrar un parche Harmony.</summary>
    public enum PatchRegistrationStatus
    {
        Applied = 0,
        SkippedOptional = 1,
        Failed = 2
    }

    /// <summary>Estado de salud global del servidor expuesto por <c>/api/v1/host/health</c>.</summary>
    public enum HostHealthStatus
    {
        Starting = 0,
        Ok = 1,
        Degraded = 2,
        Lockdown = 3
    }

    /// <summary>Registro inmutable de un parche Harmony intentado durante el arranque.</summary>
    public sealed record PatchRegistrationEntry(
        string Name,
        string ModuleName,
        PatchCriticality Criticality,
        PatchRegistrationStatus Status,
        string? FailureReason = null);

    /// <summary>Decisión pura de arranque y salud derivada del registro de parches y versiones (TASK-2501 / TASK-2502).</summary>
    public sealed class PatchHealthDecision
    {
        public HostHealthStatus Status { get; init; }
        public bool IsLockdown => Status == HostHealthStatus.Lockdown;
        public IReadOnlyList<string> LockdownReasons { get; init; } = Array.Empty<string>();
        public IReadOnlyList<PatchRegistrationEntry> FailedPatches { get; init; } = Array.Empty<PatchRegistrationEntry>();
        public IReadOnlyList<string> OverriddenPatchNames { get; init; } = Array.Empty<string>();
        public int TotalPatches { get; init; }
    }

    /// <summary>
    /// Política pura sin dependencias de SMAPI/Harmony para decidir si el servidor puede cargar partida y abrir
    /// multijugador (<c>Ok</c>/<c>Degraded</c>) o debe permanecer bloqueado (<c>Lockdown</c>) (ADR-0010).
    /// </summary>
    public static class PatchHealthPolicy
    {
        public static PatchHealthDecision Decide(
            IEnumerable<PatchRegistrationEntry> patches,
            string? gameVersion,
            IEnumerable<string>? verifiedGameVersions,
            bool allowUnverifiedGameVersion,
            string? smapiVersion,
            string? minimumSmapiVersion,
            IEnumerable<string>? lockdownOverride = null,
            IEnumerable<string>? moduleRegistrationFailures = null)
        {
            List<PatchRegistrationEntry> patchList = patches?.ToList() ?? new List<PatchRegistrationEntry>();
            var overrideSet = new HashSet<string>(
                (lockdownOverride ?? Array.Empty<string>())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var lockdownReasons = new List<string>();
            var failedPatches = new List<PatchRegistrationEntry>();
            var overriddenNames = new List<string>();
            bool hasExperienceFailure = false;

            foreach (PatchRegistrationEntry entry in patchList)
            {
                if (entry.Status != PatchRegistrationStatus.Failed)
                    continue;

                failedPatches.Add(entry);
                bool isCritical = entry.Criticality is PatchCriticality.Security or PatchCriticality.Integrity;
                if (isCritical)
                {
                    if (overrideSet.Contains(entry.Name))
                    {
                        overriddenNames.Add(entry.Name);
                    }
                    else
                    {
                        lockdownReasons.Add($"PatchFailed:{entry.Criticality}:{entry.Name}");
                    }
                }
                else
                {
                    hasExperienceFailure = true;
                }
            }

            if (moduleRegistrationFailures != null)
            {
                foreach (string moduleName in moduleRegistrationFailures)
                {
                    if (!string.IsNullOrWhiteSpace(moduleName))
                        lockdownReasons.Add($"ModuleRegisterFailed:{moduleName.Trim()}");
                }
            }

            if (!allowUnverifiedGameVersion && !IsGameVersionVerified(gameVersion, verifiedGameVersions))
            {
                string display = string.IsNullOrWhiteSpace(gameVersion) ? "unknown" : gameVersion.Trim();
                lockdownReasons.Add($"UnverifiedGameVersion:{display}");
            }

            if (!string.IsNullOrWhiteSpace(minimumSmapiVersion) &&
                !IsVersionAtLeast(smapiVersion, minimumSmapiVersion))
            {
                string display = string.IsNullOrWhiteSpace(smapiVersion) ? "unknown" : smapiVersion.Trim();
                lockdownReasons.Add($"UnverifiedSmapiVersion:{display}");
            }

            HostHealthStatus status;
            if (lockdownReasons.Count > 0)
                status = HostHealthStatus.Lockdown;
            else if (hasExperienceFailure || overriddenNames.Count > 0)
                status = HostHealthStatus.Degraded;
            else
                status = HostHealthStatus.Ok;

            return new PatchHealthDecision
            {
                Status = status,
                LockdownReasons = lockdownReasons,
                FailedPatches = failedPatches,
                OverriddenPatchNames = overriddenNames,
                TotalPatches = patchList.Count
            };
        }

        public static bool IsGameVersionVerified(string? actualVersion, IEnumerable<string>? verifiedVersions)
        {
            if (string.IsNullOrWhiteSpace(actualVersion) || verifiedVersions == null)
                return false;

            string actualTrimmed = actualVersion.Trim();
            string actualNormalized = StripPreReleaseAndBuild(actualTrimmed);
            string[] actualParts = actualNormalized.Split('.');

            foreach (string candidate in verifiedVersions)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                string candidateTrimmed = candidate.Trim();
                if (string.Equals(actualTrimmed, candidateTrimmed, StringComparison.OrdinalIgnoreCase))
                    return true;

                // Soporta cadenas con número de compilación (p. ej. "1.6.15.24356" frente a "1.6.15"),
                // sin aceptar una versión mayor/menor/parche distinta como "1.6.16".
                string candidateNormalized = StripPreReleaseAndBuild(candidateTrimmed);
                string[] candidateParts = candidateNormalized.Split('.');
                if (candidateParts.Length == 3 && actualParts.Length >= 3 &&
                    string.Equals(actualParts[0], candidateParts[0], StringComparison.Ordinal) &&
                    string.Equals(actualParts[1], candidateParts[1], StringComparison.Ordinal) &&
                    string.Equals(actualParts[2], candidateParts[2], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsVersionAtLeast(string? actualVersion, string minimumVersion)
        {
            if (!TryParseNumericVersion(actualVersion, out Version? actual) ||
                !TryParseNumericVersion(minimumVersion, out Version? minimum))
            {
                return false;
            }

            return actual! >= minimum!;
        }

        public static bool TryParseNumericVersion(string? raw, out Version? parsed)
        {
            parsed = null;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            string core = StripPreReleaseAndBuild(raw.Trim());
            string[] parts = core.Split('.');
            if (parts.Length is < 2 or > 4)
                return false;

            var nums = new int[4];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out int val) || val < 0)
                    return false;
                nums[i] = val;
            }

            parsed = new Version(nums[0], nums[1], nums[2], nums[3]);
            return true;
        }

        private static string StripPreReleaseAndBuild(string version)
        {
            int dash = version.IndexOf('-');
            int plus = version.IndexOf('+');
            int cut = -1;
            if (dash >= 0 && plus >= 0)
                cut = Math.Min(dash, plus);
            else if (dash >= 0)
                cut = dash;
            else if (plus >= 0)
                cut = plus;

            string trimmed = cut >= 0 ? version.Substring(0, cut) : version;
            int space = trimmed.IndexOf(' ');
            return space >= 0 ? trimmed.Substring(0, space) : trimmed;
        }
    }
}
