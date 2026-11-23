using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.Networking
{
    /// <summary>Versión semántica simple <c>Major.Minor.Patch</c> sin dependencias de SMAPI.</summary>
    public readonly record struct SemVer(int Major, int Minor, int Patch) : IComparable<SemVer>
    {
        public int CompareTo(SemVer other)
        {
            int c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            return Patch.CompareTo(other.Patch);
        }

        public static bool operator <(SemVer left, SemVer right) => left.CompareTo(right) < 0;
        public static bool operator <=(SemVer left, SemVer right) => left.CompareTo(right) <= 0;
        public static bool operator >(SemVer left, SemVer right) => left.CompareTo(right) > 0;
        public static bool operator >=(SemVer left, SemVer right) => left.CompareTo(right) >= 0;

        public override string ToString() => $"{Major}.{Minor}.{Patch}";

        public static bool TryParse(string? raw, out SemVer version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            string trimmed = raw.Trim();
            int suffixIdx = trimmed.IndexOfAny(new[] { '-', '+' });
            if (suffixIdx >= 0)
                trimmed = trimmed.Substring(0, suffixIdx);

            string[] parts = trimmed.Split('.');
            if (parts.Length != 3)
                return false;

            if (!int.TryParse(parts[0], out int major) || major < 0)
                return false;
            if (!int.TryParse(parts[1], out int minor) || minor < 0)
                return false;
            if (!int.TryParse(parts[2], out int patch) || patch < 0)
                return false;

            version = new SemVer(major, minor, patch);
            return true;
        }
    }

    /// <summary>Resultado de evaluar si un cliente puede conectarse a este host según ADR-0012.</summary>
    public sealed record ClientVersionDecision(
        bool IsCompatible,
        SemVer EffectiveMinClientVersion,
        string? RejectionReason);

    /// <summary>Tipo de impacto sobre la compatibilidad cliente ↔ host (ADR-0012).</summary>
    public enum CompatibilityImpactKind
    {
        None = 0,
        MajorOrMinorBump = 1,
        ProtocolOrModelChange = 2,
        WorldDefinitionChange = 3,
        ClientRuleChange = 4,
    }

    /// <summary>Clasificación de impacto de un archivo o condición concreta.</summary>
    public sealed record PathImpact(string Path, CompatibilityImpactKind Kind, string Reason)
    {
        public bool RequiresBump => Kind != CompatibilityImpactKind.None;
    }

    /// <summary>Resultado determinista del cálculo de <see cref="ClientVersionPolicy.MinCompatibleClientVersion"/>.</summary>
    public sealed record MinVersionCalculation(
        SemVer RecommendedMinVersion,
        bool MustBump,
        IReadOnlyList<PathImpact> BlockingImpacts,
        IReadOnlyList<PathImpact> SafeImpacts);

    /// <summary>
    /// Política de compatibilidad de versión cliente ↔ host desacoplada del <c>PatchVersion</c> del host (ADR-0012).
    /// <para>
    /// Un cambio puramente de host (<c>ModuleSide.Host</c>) o un hotfix de interfaz/plataforma cliente no obliga a
    /// expulsar clientes existentes dentro de la misma serie <c>Major.Minor</c>. Solo se eleva
    /// <see cref="MinCompatibleClientVersion"/> cuando cambia el protocolo, los modelos de red, la definición del mundo
    /// (<c>Data/Locations</c>/mapas/modpack) o una regla de juego ejecutada en el cliente.
    /// </para>
    /// </summary>
    public static class ClientVersionPolicy
    {
        /// <summary>
        /// Revisión del protocolo de saludo cliente ↔ host (<c>Pelican4Ever/ClientProtocol</c>).
        /// Cualquier incremento aquí obliga a elevar <see cref="MinCompatibleClientVersion"/> a la release actual.
        /// </summary>
        public const string ProtocolRevision = "1";

        /// <summary>
        /// Versión mínima de <c>Pelican 4Ever</c> exigida al cliente para entrar a este servidor.
        /// <para>
        /// <b>Cálculo v0.5.4 (base 0.5.3):</b> elevada a <c>0.5.4</c> por el commit <c>c444aa6</c>, que absorbió
        /// <c>Pelican4Ever.NITVCompat</c> en <c>Data/Locations</c> (<c>FarmTemplateLogic.cs</c> + <c>FarmSlotModule.cs</c>)
        /// y retiró el content pack hermano de <c>deploy/modpack/</c>. Los commits de <c>dev</c> (<c>fix(android)</c> y
        /// <c>TASK-2701</c>) eran por sí solos retrocompatibles.
        /// </para>
        /// </summary>
        public static readonly SemVer MinCompatibleClientVersion = new(0, 5, 4);

        private static readonly HashSet<string> HostOnlyFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "Pelican4Ever/PelicanApi.cs",
            "Pelican4Ever/Framework/SystemdWatchdogService.cs",
            "Pelican4Ever/Modules/AdminApiModule.cs",
            "Pelican4Ever/Modules/AnnexReviewModule.cs",
            "Pelican4Ever/Modules/AutoHostModule.cs",
            "Pelican4Ever/Modules/CheckpointModule.cs",
            "Pelican4Ever/Modules/CheckpointSpikeModule.cs",
            "Pelican4Ever/Modules/ClientVersionGuardModule.cs",
            "Pelican4Ever/Modules/FarmTypeModule.cs",
            "Pelican4Ever/Modules/HeadlessEventGuardModule.cs",
            "Pelican4Ever/Modules/HeadlessRenderModule.cs",
            "Pelican4Ever/Modules/PersistenceModule.cs",
            "Pelican4Ever/Modules/ProbationTelemetryModule.cs",
            "Pelican4Ever/Modules/RolloverLockModule.cs",
            "Pelican4Ever.Core/Networking/ClientModRequirements.cs",
            "Pelican4Ever.Core/Networking/ClientVersionPolicy.cs",
            "Pelican4Ever.Core/Networking/StalePeerContext.cs",
        };

        private static readonly HashSet<string> ClientUiSafeFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "Pelican4Ever/Framework/AndroidMenuCompat.cs",
            "Pelican4Ever/Modules/ModToggleModule.cs",
        };

        private static readonly string[] NonRuntimePrefixes =
        {
            "docs/",
            "screenshots/",
            "audit/",
            "tools/",
            "scripts/",
            ".github/",
            ".agents/",
            "Pelican4Ever.Tests/",
            "Pelican4Ever/i18n/",
        };

        private static readonly string[] HostOnlyPrefixes =
        {
            "Pelican4Ever.Core/Admin/",
            "Pelican4Ever.Core/Server/",
        };

        private static readonly string[] ProtocolOrModelPrefixes =
        {
            "Pelican4Ever/Models/",
            "Pelican4Ever.Core/Security/",
        };

        private static readonly string[] WorldDefinitionPrefixes =
        {
            "Pelican4Ever.Core/World/",
            "Pelican4Ever/assets/",
            "deploy/modpack/",
        };

        /// <summary>
        /// Evalúa si <paramref name="clientVersion"/> es compatible con un host en <paramref name="hostVersion"/>
        /// con suelo mínimo <paramref name="minClientVersion"/>.
        /// <para>
        /// Invariante fail-closed: si <paramref name="minClientVersion"/> no pertenece al mismo <c>Major.Minor</c>
        /// que <paramref name="hostVersion"/> o es superior al host, se eleva automáticamente a <paramref name="hostVersion"/>.
        /// </para>
        /// </summary>
        public static ClientVersionDecision Evaluate(SemVer hostVersion, SemVer minClientVersion, SemVer clientVersion)
        {
            SemVer effectiveMin = NormalizeFloor(hostVersion, minClientVersion);

            if (clientVersion.Major != hostVersion.Major || clientVersion.Minor != hostVersion.Minor)
            {
                return new ClientVersionDecision(
                    IsCompatible: false,
                    EffectiveMinClientVersion: effectiveMin,
                    RejectionReason: $"versión incompatible ({clientVersion}; el servidor {hostVersion} exige serie {hostVersion.Major}.{hostVersion.Minor}.x >= {effectiveMin}).");
            }

            if (clientVersion < effectiveMin)
            {
                return new ClientVersionDecision(
                    IsCompatible: false,
                    EffectiveMinClientVersion: effectiveMin,
                    RejectionReason: $"versión incompatible ({clientVersion}; el servidor {hostVersion} exige cliente >= {effectiveMin}).");
            }

            return new ClientVersionDecision(
                IsCompatible: true,
                EffectiveMinClientVersion: effectiveMin,
                RejectionReason: null);
        }

        /// <summary>
        /// Comprueba que <paramref name="minClientVersion"/> respeta los invariantes respecto a <paramref name="hostVersion"/>
        /// (mismo <c>Major.Minor</c> y <c>min &lt;= host</c>).
        /// </summary>
        public static bool IsValidFloor(SemVer hostVersion, SemVer minClientVersion) =>
            minClientVersion.Major == hostVersion.Major
            && minClientVersion.Minor == hostVersion.Minor
            && minClientVersion <= hostVersion;

        /// <summary>Normaliza el suelo en modo fail-closed si la configuración en código quedó desfasada.</summary>
        public static SemVer NormalizeFloor(SemVer hostVersion, SemVer minClientVersion) =>
            IsValidFloor(hostVersion, minClientVersion) ? minClientVersion : hostVersion;

        /// <summary>
        /// Clasifica el impacto de compatibilidad de una ruta relativa al repositorio según la matriz normativa de ADR-0012.
        /// </summary>
        public static PathImpact EvaluatePathImpact(string repoRelativePath)
        {
            string normalized = NormalizePath(repoRelativePath);
            if (string.IsNullOrEmpty(normalized))
                return new PathImpact(normalized, CompatibilityImpactKind.None, "ruta vacía");

            if (IsNonRuntimePath(normalized))
                return new PathImpact(normalized, CompatibilityImpactKind.None, "documentación, tests, i18n o tooling sin impacto de protocolo");

            if (HostOnlyFiles.Contains(normalized) || HasAnyPrefix(normalized, HostOnlyPrefixes))
                return new PathImpact(normalized, CompatibilityImpactKind.None, "solo host (ModuleSide.Host / Core Admin/Server)");

            if (ClientUiSafeFiles.Contains(normalized))
                return new PathImpact(normalized, CompatibilityImpactKind.None, "solo UI/plataforma cliente sin cambio de reglas ni protocolo");

            if (string.Equals(normalized, "Pelican4Ever.Core/Networking/AdmissionLogic.cs", StringComparison.OrdinalIgnoreCase)
                || HasAnyPrefix(normalized, ProtocolOrModelPrefixes))
            {
                return new PathImpact(normalized, CompatibilityImpactKind.ProtocolOrModelChange, "protocolo, modelos de red o seguridad compartida");
            }

            if (string.Equals(normalized, "Pelican4Ever/Framework/MapCloneHelper.cs", StringComparison.OrdinalIgnoreCase)
                || HasAnyPrefix(normalized, WorldDefinitionPrefixes))
            {
                return new PathImpact(normalized, CompatibilityImpactKind.WorldDefinitionChange, "definición de mundo, mapas, biomas o modpack");
            }

            if (normalized.StartsWith("Pelican4Ever/", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("Pelican4Ever.Core/", StringComparison.OrdinalIgnoreCase))
            {
                return new PathImpact(normalized, CompatibilityImpactKind.ClientRuleChange, "módulo compartido (Both) o regla en Core — fail-safe exige revisión/subida");
            }

            return new PathImpact(normalized, CompatibilityImpactKind.None, "fuera del runtime del mod");
        }

        /// <summary>
        /// Calcula la próxima <see cref="MinCompatibleClientVersion"/> dadas la versión mínima anterior,
        /// la versión objetivo de la release y las rutas modificadas desde la etiqueta de esa versión mínima.
        /// </summary>
        public static MinVersionCalculation ComputeNextMinVersion(
            SemVer currentMinVersion,
            SemVer targetHostVersion,
            IEnumerable<string> changedPaths,
            bool protocolRevisionChanged = false)
        {
            var blocking = new List<PathImpact>();
            var safe = new List<PathImpact>();

            if (targetHostVersion.Major != currentMinVersion.Major || targetHostVersion.Minor != currentMinVersion.Minor)
            {
                blocking.Add(new PathImpact(
                    "<version>",
                    CompatibilityImpactKind.MajorOrMinorBump,
                    $"cambio de serie {currentMinVersion.Major}.{currentMinVersion.Minor}.x -> {targetHostVersion.Major}.{targetHostVersion.Minor}.x"));
            }

            if (protocolRevisionChanged)
            {
                blocking.Add(new PathImpact(
                    "<protocol>",
                    CompatibilityImpactKind.ProtocolOrModelChange,
                    "incremento de ClientVersionPolicy.ProtocolRevision"));
            }

            foreach (string rawPath in changedPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                PathImpact impact = EvaluatePathImpact(rawPath);
                if (impact.RequiresBump)
                    blocking.Add(impact);
                else
                    safe.Add(impact);
            }

            bool mustBump = blocking.Count > 0;
            SemVer recommended = mustBump
                ? targetHostVersion
                : (currentMinVersion <= targetHostVersion ? currentMinVersion : targetHostVersion);

            return new MinVersionCalculation(recommended, mustBump, blocking, safe);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string p = path.Trim().Replace('\\', '/');
            while (p.StartsWith("./", StringComparison.Ordinal))
                p = p.Substring(2);
            return p;
        }

        private static bool IsNonRuntimePath(string normalized)
        {
            if (HasAnyPrefix(normalized, NonRuntimePrefixes))
                return true;

            if (normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                return true;

            if (normalized.StartsWith("deploy/", StringComparison.OrdinalIgnoreCase)
                && !normalized.StartsWith("deploy/modpack/", StringComparison.OrdinalIgnoreCase))
                return true;

            if (normalized.StartsWith("agents/", StringComparison.OrdinalIgnoreCase))
                return true;

            return normalized.Equals("Pelican4Ever/manifest.json", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("config.example.json", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("docker-compose.yml", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("global.json", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Pelican4Ever.sln", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals(".gitignore", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals(".editorconfig", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals(".env.example", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("local.props.example", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasAnyPrefix(string path, string[] prefixes) =>
            prefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
