using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.Networking
{
    /// <summary>Un mod que el cliente no tiene, o tiene en otra versión.</summary>
    public sealed record ClientModProblem(string UniqueId, string RequiredVersion, string? ClientVersion)
    {
        public bool IsMissing => ClientVersion is null;

        public override string ToString() => IsMissing
            ? $"falta {UniqueId} {RequiredVersion}"
            : $"{UniqueId} {ClientVersion} (el servidor usa {RequiredVersion})";
    }

    /// <summary>
    /// Comprueba que un cliente tiene el modpack del servidor (<c>RequiredClientMods</c>). La versión exigida es la que
    /// tiene instalada el propio host: así no hay que repetirla en config.json y, al actualizar un mod en el servidor,
    /// la exigencia cambia sola. Pura y sin dependencias de SMAPI para poder probarla.
    /// </summary>
    public static class ClientModRequirements
    {
        /// <summary>Longitud máxima razonable de un UniqueID de SMAPI.</summary>
        public const int MaxUniqueIdLength = 100;

        public static bool IsValidUniqueId(string? id) =>
            !string.IsNullOrWhiteSpace(id)
            && id.Length <= MaxUniqueIdLength
            && id.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_');

        /// <summary>
        /// Problemas del cliente frente a los mods exigidos, en el orden de <paramref name="required"/>. Los IDs no
        /// distinguen mayúsculas; las versiones se comparan exactas (como las escribe SMAPI, p. ej. <c>2.9.1</c>).
        /// </summary>
        /// <param name="required">UniqueID → versión instalada en el host.</param>
        /// <param name="client">UniqueID → versión que declara el cliente.</param>
        public static IReadOnlyList<ClientModProblem> Check(
            IEnumerable<KeyValuePair<string, string>> required,
            IReadOnlyDictionary<string, string> client)
        {
            var byId = new Dictionary<string, string>(client, StringComparer.OrdinalIgnoreCase);
            var problems = new List<ClientModProblem>();
            foreach (var (id, version) in required)
            {
                if (!byId.TryGetValue(id, out string? clientVersion))
                    problems.Add(new ClientModProblem(id, version, null));
                else if (!string.Equals(clientVersion.Trim(), version.Trim(), StringComparison.OrdinalIgnoreCase))
                    problems.Add(new ClientModProblem(id, version, clientVersion));
            }

            return problems;
        }

        /// <summary>Motivo de expulsión legible: los primeros problemas y cuántos más hay.</summary>
        public static string DescribeRejection(IReadOnlyList<ClientModProblem> problems, int maxShown = 3)
        {
            if (problems.Count == 0)
                return string.Empty;

            string shown = string.Join("; ", problems.Take(maxShown));
            int rest = problems.Count - maxShown;
            return rest > 0
                ? $"no tiene el modpack del servidor: {shown}; y {rest} más."
                : $"no tiene el modpack del servidor: {shown}.";
        }
    }
}
