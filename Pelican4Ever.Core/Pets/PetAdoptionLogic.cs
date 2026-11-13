using System;

namespace Pelican4Ever.Core.Pets
{
    /// <summary>
    /// Lógica de dominio pura para el flujo de adopción de mascotas de clientes (Épica 20).
    /// Desacoplada del motor de Stardew Valley y SMAPI para facilitar tests unitarios deterministas.
    /// </summary>
    public static class PetAdoptionLogic
    {
        /// <summary>
        /// Clave histórica de la carta de Marnie. Se conserva para poder leer partidas antiguas,
        /// pero el flujo actual ya no envía correo: Marnie se presenta en la puerta tras despertar.
        /// </summary>
        public const string MarnieMailKey = "Pelican4Ever_MarniePetLetter";

        /// <summary>
        /// Registra la adquisición de un cuenco para mascotas por parte de un jugador cliente.
        /// </summary>
        public static bool RecordPurchase(
            PetAdoptionRecord record,
            string slotId,
            int bowlX,
            int bowlY,
            int currentGameDay,
            string petType = "Dog",
            string petBreed = "0")
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            if (string.IsNullOrWhiteSpace(slotId))
                return false;

            // Si ya tiene una mascota adoptada en este cuenco, no sobrescribir el estado
            if (record.State == PetAdoptionState.Adopted)
                return false;

            record.State = PetAdoptionState.BowlPurchased;
            record.FarmSlotId = slotId;
            record.BowlTileX = bowlX;
            record.BowlTileY = bowlY;
            record.PurchaseGameDay = currentGameDay;
            record.PetType = string.IsNullOrWhiteSpace(petType) ? "Dog" : petType;
            record.PetBreed = string.IsNullOrWhiteSpace(petBreed) ? "0" : petBreed;
            record.UpdatedAtUtc = DateTimeOffset.UtcNow;
            return true;
        }

        /// <summary>
        /// Determina si ya ha pasado al menos un "despertar" (un día nuevo) desde la compra del cuenco.
        /// La visita de Marnie no depende de una carta ni de que el día siguiente sea exactamente el inmediato:
        /// basta con que el jugador haya dormido una vez desde la compra.
        /// </summary>
        public static bool HasWokenSincePurchase(PetAdoptionRecord record, int currentGameDay)
        {
            if (record == null)
                return false;

            if (!IsAwaitingAdoption(record))
                return false;

            if (record.PurchaseGameDay < 0)
            {
                // Partidas antiguas sin día de compra: si la carta ya se entregó, la visita puede completarse.
                return record.State == PetAdoptionState.LetterDelivered;
            }

            return currentGameDay > record.PurchaseGameDay;
        }

        /// <summary>
        /// Indica si el registro sigue pendiente de la visita de Marnie (compra reciente o flujo antiguo de carta).
        /// </summary>
        public static bool IsAwaitingAdoption(PetAdoptionRecord record)
        {
            return record != null
                && (record.State == PetAdoptionState.BowlPurchased || record.State == PetAdoptionState.LetterDelivered);
        }

        /// <summary>
        /// Evalúa si se cumplen las condiciones contextuales para activar la cinemática de adopción.
        /// Debe ocurrir en el mapa propio del jugador y durante el día. La visita ya no espera a que
        /// el tiempo sea soleado: Marnie acude igualmente el día en que el jugador despierta.
        /// </summary>
        public static bool CanTriggerAdoptionEvent(
            PetAdoptionRecord record,
            long playerId,
            long currentSlotOwnerId,
            string currentMapName,
            int timeOfDay)
        {
            if (record == null)
                return false;

            if (!IsAwaitingAdoption(record))
                return false;

            if (playerId <= 0 || currentSlotOwnerId <= 0 || playerId != currentSlotOwnerId)
                return false;

            if (string.IsNullOrWhiteSpace(record.FarmSlotId) || string.IsNullOrWhiteSpace(currentMapName))
                return false;

            if (!string.Equals(record.FarmSlotId, currentMapName, StringComparison.OrdinalIgnoreCase))
                return false;

            // Horario diurno canónico (entre 06:00 y 20:00)
            if (timeOfDay < 600 || timeOfDay > 2000)
                return false;

            return true;
        }

        /// <summary>
        /// Confirma la adopción exitosa de la mascota, registrando su nombre y tipo.
        /// </summary>
        public static bool RecordAdoption(
            PetAdoptionRecord record,
            string petName,
            string? petType = null,
            string? petBreed = null)
        {
            if (record == null || !IsAwaitingAdoption(record))
                return false;

            if (string.IsNullOrWhiteSpace(petName))
                return false;

            record.State = PetAdoptionState.Adopted;
            record.PetName = petName.Trim();

            if (!string.IsNullOrWhiteSpace(petType))
                record.PetType = petType;

            if (!string.IsNullOrWhiteSpace(petBreed))
                record.PetBreed = petBreed;

            record.UpdatedAtUtc = DateTimeOffset.UtcNow;
            return true;
        }

        /// <summary>
        /// Registra que el jugador declinó la adopción durante el evento.
        /// </summary>
        public static bool RecordDeclined(PetAdoptionRecord record)
        {
            if (record == null || !IsAwaitingAdoption(record))
                return false;

            record.State = PetAdoptionState.Declined;
            record.UpdatedAtUtc = DateTimeOffset.UtcNow;
            return true;
        }
    }
}
