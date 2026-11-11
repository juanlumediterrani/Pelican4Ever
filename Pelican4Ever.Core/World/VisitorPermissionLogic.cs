using System;
using Pelican4Ever.Core.Localization;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Permisos de los visitantes en una residencia. La política de acceso (<see cref="AccessPolicy"/>) decide
    /// quién entra; <see cref="DoorAssignment.VisitorsCanEdit"/> decide si quien entra sin ser el dueño puede
    /// cambiar algo: usar herramientas de trabajo, colocar o sembrar, cosechar, abrir cofres y usar máquinas.
    /// </summary>
    public static class VisitorPermissionLogic
    {
        private static readonly string[] SubLocationSuffixes =
        {
            "_Cave",
            ResidenceUpgradeLogic.CellarSuffix,
            ResidenceUpgradeLogic.GreenhouseSuffix,
            ResidenceUpgradeLogic.CabinHomeSuffix
        };

        /// <summary>
        /// Hueco de residencia al que pertenece la ubicación (granja, cueva, sótano, invernadero, casa o anexo),
        /// p. ej. "Pelican4Ever_Annex_03_East" → "Pelican4Ever_Farm_03". Null si no es de ninguna residencia.
        /// </summary>
        public static string? ResidenceSlotOf(string? locationName)
        {
            if (string.IsNullOrWhiteSpace(locationName))
                return null;

            if (locationName.StartsWith(LocationCullingLogic.AnnexPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string rest = locationName.Substring(LocationCullingLogic.AnnexPrefix.Length);
                int underscore = rest.IndexOf('_');
                string number = underscore >= 0 ? rest.Substring(0, underscore) : rest;
                return number.Length > 0 ? LocationCullingLogic.FarmPrefix + number : null;
            }

            if (!locationName.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase))
                return null;

            foreach (string suffix in SubLocationSuffixes)
            {
                if (locationName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return locationName.Substring(0, locationName.Length - suffix.Length);
            }
            return locationName;
        }

        /// <summary>
        /// Indica si el jugador puede entrar: es el dueño, la residencia está abierta o, con
        /// <see cref="AccessPolicy.Friends"/>, el dueño lo tiene en su lista de amigos.
        /// </summary>
        public static bool CanEnter(DoorAssignment? assignment, long playerId)
        {
            if (assignment == null || assignment.OwnerId == 0 || assignment.OwnerId == playerId)
                return true;
            return assignment.Policy switch
            {
                AccessPolicy.Public => true,
                AccessPolicy.Friends => assignment.Friends.Contains(playerId),
                _ => false
            };
        }

        /// <summary>Indica si el jugador puede modificar la residencia: es el dueño o el dueño permite editar a los visitantes.</summary>
        public static bool CanEdit(DoorAssignment? assignment, long playerId)
        {
            return assignment == null
                || assignment.OwnerId == 0
                || assignment.OwnerId == playerId
                || assignment.VisitorsCanEdit;
        }

        /// <summary>
        /// Indica si la ubicación forma parte de la Plaza Central común (<c>Farm</c> o <c>Pelican4Ever_Bridge_*</c>),
        /// donde solo el anfitrión puede usar herramientas de trabajo, colocar objetos o abrir cofres/máquinas.
        /// </summary>
        public static bool IsProtectedPublicHub(string? locationName)
        {
            return HostPlazaLayout.IsPlazaLocation(locationName);
        }

        /// <summary>Texto del estado de acceso para los diálogos.</summary>
        public static string Describe(AccessPolicy policy, bool visitorsCanEdit, ITranslationProvider? translations = null)
        {
            var provider = translations ?? CoreI18n.Current;
            string access = policy switch
            {
                AccessPolicy.Public => provider.Get("residence.permission.open"),
                AccessPolicy.Friends => provider.Get("residence.permission.friends_only"),
                _ => provider.Get("residence.permission.closed")
            };
            if (policy == AccessPolicy.Locked)
                return access;
            return visitorsCanEdit
                ? provider.Get("residence.permission.visitors_can_edit", new { access })
                : provider.Get("residence.permission.visitors_view_only", new { access });
        }
    }
}
