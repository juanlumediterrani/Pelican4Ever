using System;
using Pelican4Ever.Core.World;

namespace Pelican4Ever.Core.Players
{
    /// <summary>Motivo por el que se bloquea una mejora de casa, compra del colgante o propuesta de matrimonio.</summary>
    public enum HouseholdRestrictionReason
    {
        None,
        NoResidence,
        NotOwner
    }

    /// <summary>Reglas puras de residencia, bodas offline, amistad con cónyuge, divorcio e hijos.</summary>
    public static class HouseholdRolloverLogic
    {
        /// <summary>Puntos diarios perdidos por no hablar con el cónyuge o compañero de piso.</summary>
        public const int SpouseDailyNeglectPenalty = -20;

        /// <summary>Comprueba si el jugador es propietario de su residencia para ampliar casa o casarse.</summary>
        public static HouseholdRestrictionReason CanUpgradeHouseOrMarry(long playerId, DoorAssignment? residence)
        {
            if (playerId == 0 || residence == null)
                return HouseholdRestrictionReason.NoResidence;

            return residence.OwnerId == playerId
                ? HouseholdRestrictionReason.None
                : HouseholdRestrictionReason.NotOwner;
        }

        /// <summary>
        /// Calcula el delta diario de amistad por inactividad: únicamente penaliza (-20) al cónyuge
        /// o compañero de piso si no se habló con él ese día (decisión 3: sin caída general ni dating).
        /// </summary>
        public static int EvaluateDailySpouseFriendshipDelta(bool isSpouseOrRoommate, bool talkedToToday)
        {
            return isSpouseOrRoommate && !talkedToToday
                ? SpouseDailyNeglectPenalty
                : 0;
        }

        /// <summary>
        /// Determina si el rollover del servidor debe completar de forma autoritativa la boda de un
        /// jugador desconectado (Opción 4A) y dejar la escena pendiente en <see cref="PlayerRecord.PendingWeddingSpouse"/>.
        /// </summary>
        public static bool ShouldCompleteOfflineWedding(
            bool isEngaged,
            bool hasSpouseName,
            int countdownToWedding,
            bool canHaveWeddingToday)
        {
            return isEngaged && hasSpouseName && countdownToWedding < 1 && canHaveWeddingToday;
        }

        /// <summary>
        /// Detecta si un NPC tiene su mapa por defecto apuntando a una cabaña privada (<c>_Home</c>)
        /// sin cónyuge vivo o con la cabaña desaparecida, evitando el fallo vanilla que solo revisaba "cabin"/"FarmHouse".
        /// </summary>
        public static bool IsStrandedResidenceNpc(
            bool isResidenceCabinMap,
            bool hasLiveSpouse,
            bool isDefaultMapMissing)
        {
            return isResidenceCabinMap && (!hasLiveSpouse || isDefaultMapMissing);
        }

        /// <summary>
        /// Calcula la casilla exterior del porche asociada a la casa de la residencia para tareas del cónyuge
        /// y salida de boda, evitando devolver <c>(0, 0)</c> cuando el edificio exterior es <c>Farmhouse</c>.
        /// </summary>
        public static (int X, int Y) ComputeResidencePorchSpot(
            int mainFarmHouseEntryX,
            int mainFarmHouseEntryY,
            int buildingTileX = -1,
            int buildingTileY = -1,
            int buildingTilesHigh = -1,
            bool isCabinBuilding = false)
        {
            if (isCabinBuilding && buildingTileX >= 0 && buildingTileY >= 0 && buildingTilesHigh > 0)
                return (buildingTileX + 1, buildingTileY + buildingTilesHigh - 1);

            return (mainFarmHouseEntryX + 2, mainFarmHouseEntryY);
        }

        /// <summary>
        /// Evalúa si procede ofrecer al cliente la pregunta nocturna de embarazo/adopción diferida (5 % diario).
        /// </summary>
        public static bool ShouldOfferPregnancyQuestion(
            bool isMarriedOrRoommates,
            bool hasSpouseNpc,
            bool spouseCanGetPregnant,
            bool spouseWantsChildren,
            bool hasNextBirthingDate,
            int currentGameDay,
            int lastPromptGameDay,
            double randomRoll)
        {
            if (!isMarriedOrRoommates || !hasSpouseNpc || !spouseCanGetPregnant || !spouseWantsChildren)
                return false;

            if (hasNextBirthingDate)
                return false;

            if (lastPromptGameDay >= currentGameDay)
                return false;

            return randomRoll < 0.05;
        }
    }
}
