using Pelican4Ever.Core.World;
using StardewValley;

namespace Pelican4Ever.Models
{
    /// <summary>
    /// Claves de <c>modData</c> usadas como VISTA REPLICADA de solo lectura para los clientes.
    ///
    /// Regla (ADR-0004): el estado autoritativo vive en <see cref="ServerWorldState"/> en el host.
    /// Solo el host escribe estas claves, y solo sobre objetos que controla (ubicaciones), nunca sobre
    /// <c>Farmer.modData</c> de un farmhand (lo controla su cliente y provocaría condiciones de carrera).
    /// </summary>
    public static class ModDataKeys
    {
        private const string Prefix = "Pelican4Ever.";

        // GameLocation.modData de los huecos de granja (escritas por el host).
        public const string FarmOwnerId = Prefix + "OwnerId";
        public const string AccessPolicy = Prefix + "AccessPolicy";
    }

    public static class LocationModDataExtensions
    {
        public static long? GetFarmOwnerId(this GameLocation location)
        {
            return location.modData.TryGetValue(ModDataKeys.FarmOwnerId, out string? raw) && long.TryParse(raw, out long id)
                ? id
                : null;
        }

        public static AccessPolicy GetAccessPolicy(this GameLocation location)
        {
            return location.modData.TryGetValue(ModDataKeys.AccessPolicy, out string? raw)
                && System.Enum.TryParse(raw, ignoreCase: true, out AccessPolicy policy)
                ? policy
                : AccessPolicy.Locked;
        }

        /// <summary>Publica propietario y política en la ubicación. Solo debe llamarse en el host.</summary>
        public static void PublishOwnership(this GameLocation location, long ownerId, AccessPolicy policy)
        {
            location.modData[ModDataKeys.FarmOwnerId] = ownerId.ToString();
            location.modData[ModDataKeys.AccessPolicy] = policy.ToString();
        }
    }
}
