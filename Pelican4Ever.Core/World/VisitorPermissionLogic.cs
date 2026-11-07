using System;

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

        /// <summary>Indica si el jugador puede modificar la residencia: es el dueño o el dueño permite editar a los visitantes.</summary>
        public static bool CanEdit(DoorAssignment? assignment, long playerId)
        {
            return assignment == null
                || assignment.OwnerId == 0
                || assignment.OwnerId == playerId
                || assignment.VisitorsCanEdit;
        }

        /// <summary>Texto del estado de acceso para los diálogos.</summary>
        public static string Describe(AccessPolicy policy, bool visitorsCanEdit)
        {
            string access = policy switch
            {
                AccessPolicy.Public => "abierta",
                AccessPolicy.Friends => "solo amigos",
                _ => "cerrada"
            };
            if (policy == AccessPolicy.Locked)
                return access;
            return visitorsCanEdit ? $"{access}, visitas pueden editar" : $"{access}, visitas solo miran";
        }
    }
}
