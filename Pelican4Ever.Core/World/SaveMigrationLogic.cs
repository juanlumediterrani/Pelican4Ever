using System;
using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Core.Players;

namespace Pelican4Ever.Core.World
{
    /// <summary>Modo de migración de salidas de residencias entre origin/main y refactor/residence-entrances.</summary>
    public enum MigrationMode
    {
        /// <summary>Conserva la identidad del hueco de granja (Pelican4Ever_Farm_XX) y reasigna la puerta correspondiente en el nuevo catálogo.</summary>
        SlotPreserving,

        /// <summary>Conserva la posición geográfica previa en la Granja Host (reubicando la parcela al nuevo portal físico equivalente).</summary>
        GeoPreserving,

        /// <summary>Mapeo personalizado explícito proporcionado por el usuario.</summary>
        Custom
    }

    /// <summary>Informe detallado de una operación de migración de partida guardada.</summary>
    public sealed record SaveMigrationReport(
        MigrationMode Mode,
        IReadOnlyDictionary<string, string> RemappedDoors,
        IReadOnlyDictionary<long, string> RemappedPlayers,
        IReadOnlyList<string> ReleasedDoors,
        int ObsoleteWarpsPurged,
        bool PetBowlRelocated,
        int ObstructedTilesCleared
    );

    /// <summary>
    /// Lógica pura para migrar estados de partida (<see cref="ServerWorldState"/>) y configuraciones de mapa
    /// desde la arquitectura previa de 16 puertas en Granja Host (<c>origin/main</c>) a la arquitectura
    /// de Plaza Central y Puentes de <c>refactor/residence-entrances</c>.
    /// </summary>
    public static class SaveMigrationLogic
    {
        /// <summary>Posición canónica y validada del cuenco del perro (Pet Bowl) fuera del cercado.</summary>
        public static readonly (int X, int Y) CanonicalPetBowlLocation = (53, 7);

        /// <summary>
        /// Mapeo geográfico de puertas origin/main -> refactor/residence-entrances.
        /// Preserva la posición física del portal en la Granja Host para los 4 portales activos
        /// y redistribuye ordenadamente las salidas oeste y secundarias hacia los puentes 1, 2 y 3.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> GeoDoorMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Granja Host: 4 portales activos en las mismas coordenadas
            ["Farm_Portal_05"] = "Farm_Portal_01", // (18, 1)  Norte Oeste
            ["Farm_Portal_08"] = "Farm_Portal_02", // (62, 1)  Norte Este
            ["Farm_Portal_09"] = "Farm_Portal_03", // (18, 63) Sur Oeste
            ["Farm_Portal_12"] = "Farm_Portal_04", // (62, 63) Sur Este

            // Antiguo Borde Oeste -> Puente 1 (accesible directamente por el nuevo paso oeste)
            ["Farm_Portal_01"] = "Farm_Portal_05", // (1, 16) -> Puente 1 NO (8, 1)
            ["Farm_Portal_02"] = "Farm_Portal_06", // (1, 26) -> Puente 1 NE (25, 1)
            ["Farm_Portal_03"] = "Farm_Portal_07", // (1, 36) -> Puente 1 SO (8, 28)
            ["Farm_Portal_04"] = "Farm_Portal_08", // (1, 46) -> Puente 1 SE (25, 28)

            // Antiguo Norte / Sur interior -> Puente 2
            ["Farm_Portal_06"] = "Farm_Portal_09", // (28, 1)  -> Puente 2 NO (8, 1)
            ["Farm_Portal_07"] = "Farm_Portal_10", // (52, 1)  -> Puente 2 NE (25, 1)
            ["Farm_Portal_10"] = "Farm_Portal_11", // (28, 63) -> Puente 2 SO (8, 28)
            ["Farm_Portal_11"] = "Farm_Portal_12", // (52, 63) -> Puente 2 SE (25, 28)

            // Antiguos rincones de la Granja Host -> Puente 3
            ["Farm_Portal_13"] = "Farm_Portal_13", // (8, 1)   -> Puente 3 NO (8, 1)
            ["Farm_Portal_14"] = "Farm_Portal_14", // (72, 1)  -> Puente 3 NE (25, 1)
            ["Farm_Portal_15"] = "Farm_Portal_15", // (8, 63)  -> Puente 3 SO (8, 28)
            ["Farm_Portal_16"] = "Farm_Portal_16"  // (47, 63) -> Puente 3 SE (25, 28)
        };

        /// <summary>
        /// Mapeo por preservación de identidad de hueco (slot) origin/main -> refactor/residence-entrances.
        /// Cada parcela mantiene su FarmSlotId ("Pelican4Ever_Farm_XX") y se conecta al DoorId correspondiente.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> SlotDoorMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Farm_Portal_01"] = "Farm_Portal_01",
            ["Farm_Portal_02"] = "Farm_Portal_02",
            ["Farm_Portal_03"] = "Farm_Portal_03",
            ["Farm_Portal_04"] = "Farm_Portal_04",
            ["Farm_Portal_05"] = "Farm_Portal_05",
            ["Farm_Portal_06"] = "Farm_Portal_06",
            ["Farm_Portal_07"] = "Farm_Portal_07",
            ["Farm_Portal_08"] = "Farm_Portal_08",
            ["Farm_Portal_09"] = "Farm_Portal_09",
            ["Farm_Portal_10"] = "Farm_Portal_10",
            ["Farm_Portal_11"] = "Farm_Portal_11",
            ["Farm_Portal_12"] = "Farm_Portal_12",
            ["Farm_Portal_13"] = "Farm_Portal_13",
            ["Farm_Portal_14"] = "Farm_Portal_14",
            ["Farm_Portal_15"] = "Farm_Portal_15",
            ["Farm_Portal_16"] = "Farm_Portal_16"
        };

        /// <summary>
        /// Obtiene el diccionario de mapeo de puertas según el modo especificado.
        /// </summary>
        public static IReadOnlyDictionary<string, string> ResolveDoorMapping(
            MigrationMode mode,
            IReadOnlyDictionary<string, string>? customMap = null)
        {
            return mode switch
            {
                MigrationMode.GeoPreserving => GeoDoorMapping,
                MigrationMode.SlotPreserving => SlotDoorMapping,
                MigrationMode.Custom when customMap != null => customMap,
                _ => SlotDoorMapping
            };
        }

        /// <summary>
        /// Determina si una coordenada de Warp en la Granja Host ("Farm") pertenece a una salida obsoleta
        /// de origin/main que debe ser eliminada del mapa de runtime y del XML de guardado.
        /// </summary>
        public static bool IsObsoleteFarmWarp(int x, int y, string? targetLocation)
        {
            if (string.IsNullOrWhiteSpace(targetLocation)) return false;

            // Si es un warp de residencia privada en la Granja Host
            if (targetLocation.StartsWith(LocationCullingLogic.FarmPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // Solo las 4 aperturas activas son válidas en la Granja Host
                bool isNw = (x == 18 || x == 19) && (y == 0 || y == 1);
                bool isNe = (x == 62 || x == 63) && (y == 0 || y == 1);
                bool isSw = (x == 18 || x == 19) && (y == 63 || y == 64);
                bool isSe = (x == 62 || x == 63) && (y == 63 || y == 64);

                return !(isNw || isNe || isSw || isSe);
            }

            // Antiguos warps en el borde oeste hacia parcelas obsoletas
            if (x <= 1 && (y == 16 || y == 17 || y == 26 || y == 27 || y == 36 || y == 37 || y == 46 || y == 47))
            {
                // La fila 26..27 ahora pertenece al paso hacia Pelican4Ever_Bridge_1, cualquier otro destino es obsoleto
                if ((y == 26 || y == 27) && string.Equals(targetLocation, HostPlazaLayout.BridgeName(1), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Comprueba si una coordenada de la Granja Host está dentro del corredor del paso oeste hacia los puentes.
        /// </summary>
        public static bool IsInWestCorridor(int x, int y)
        {
            return x >= 0 && x <= 7 && y >= 25 && y <= 28;
        }

        /// <summary>
        /// Comprueba si una coordenada de la Granja Host se sitúa en los accesos despejados de los portales activos.
        /// </summary>
        public static bool IsInActivePortalClearance(int x, int y)
        {
            bool isNw = x >= 17 && x <= 20 && y >= 0 && y <= 12;
            bool isNe = x >= 61 && x <= 64 && y >= 0 && y <= 12;
            bool isSw = x >= 17 && x <= 20 && y >= 58 && y <= 64;
            bool isSe = x >= 61 && x <= 64 && y >= 58 && y <= 64;
            return isNw || isNe || isSw || isSe;
        }

        /// <summary>
        /// Migra un objeto <see cref="ServerWorldState"/> al nuevo catálogo de puertas y asignaciones.
        /// </summary>
        public static SaveMigrationReport MigrateServerWorldState(
            ServerWorldState state,
            MigrationMode mode,
            IReadOnlyDictionary<string, string> catalogSlots,
            IReadOnlyDictionary<string, string>? customMap = null)
        {
            IReadOnlyDictionary<string, string> mapping = ResolveDoorMapping(mode, customMap);
            var remappedDoors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var remappedPlayers = new Dictionary<long, string>();

            var newDoors = new Dictionary<string, DoorAssignment>(StringComparer.OrdinalIgnoreCase);
            var oldToNewSlot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, DoorAssignment> entry in state.Doors)
            {
                string oldDoorId = entry.Key;
                DoorAssignment oldAssignment = entry.Value;

                string newDoorId = mapping.TryGetValue(oldDoorId, out string? targetDoorId)
                    ? targetDoorId
                    : oldDoorId;

                string newSlotId = catalogSlots.TryGetValue(newDoorId, out string? slot)
                    ? slot
                    : oldAssignment.FarmSlotId;

                var newAssignment = new DoorAssignment
                {
                    DoorId = newDoorId,
                    FarmSlotId = newSlotId,
                    FarmType = oldAssignment.FarmType,
                    OwnerId = oldAssignment.OwnerId,
                    Policy = oldAssignment.Policy,
                    Friends = new HashSet<long>(oldAssignment.Friends)
                };

                newDoors[newDoorId] = newAssignment;
                remappedDoors[oldDoorId] = newDoorId;
                oldToNewSlot[oldAssignment.FarmSlotId] = newSlotId;

                if (oldAssignment.OwnerId != 0)
                {
                    remappedPlayers[oldAssignment.OwnerId] = newSlotId;
                }
            }

            state.Doors = newDoors;

            // Actualizar referencias en Players
            foreach (PlayerRecord player in state.Players.Values)
            {
                if (!string.IsNullOrEmpty(player.FarmSlotId) && oldToNewSlot.TryGetValue(player.FarmSlotId, out string? updatedSlot))
                {
                    player.FarmSlotId = updatedSlot;
                    remappedPlayers[player.UniqueMultiplayerID] = updatedSlot;
                }
                else
                {
                    player.FarmSlotId = state.FindResidenceOf(player.UniqueMultiplayerID)?.FarmSlotId;
                }
            }

            // Actualizar referencias en Annexes
            if (state.Annexes != null)
            {
                var newAnnexes = new Dictionary<string, AnnexAssignment>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, AnnexAssignment> annexEntry in state.Annexes)
                {
                    AnnexAssignment annex = annexEntry.Value;
                    string currentSlot = annex.ResidenceSlotId ?? string.Empty;
                    string targetSlot = oldToNewSlot.TryGetValue(currentSlot, out string? s) && !string.IsNullOrEmpty(s) ? s : currentSlot;

                    string newAnnexId = ServerWorldState.BuildAnnexId(targetSlot, annex.Side);
                    annex.AnnexId = newAnnexId;
                    annex.ResidenceSlotId = targetSlot;

                    newAnnexes[newAnnexId] = annex;
                }
                state.Annexes = newAnnexes;
            }

            // Normalizar asignaciones para asegurar integridad canónica
            List<string> released = state.NormalizeDoorAssignments(catalogSlots);

            return new SaveMigrationReport(
                Mode: mode,
                RemappedDoors: remappedDoors,
                RemappedPlayers: remappedPlayers,
                ReleasedDoors: released,
                ObsoleteWarpsPurged: 0,
                PetBowlRelocated: false,
                ObstructedTilesCleared: 0
            );
        }
    }
}
