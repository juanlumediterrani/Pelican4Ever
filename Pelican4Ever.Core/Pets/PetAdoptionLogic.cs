using System;

namespace Pelican4Ever.Core.Pets
{
    /// <summary>
    /// Lógica de dominio pura para el flujo de adopción de mascotas de clientes (Épica 20).
    /// Desacoplada del motor de Stardew Valley y SMAPI para facilitar tests unitarios deterministas.
    /// </summary>
    public static class PetAdoptionLogic
    {
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
        /// Determina si Marnie debe despachar la carta de aviso al buzón del jugador.
        /// La carta se despacha a partir del día siguiente al de la compra del cuenco.
        /// </summary>
        public static bool ShouldDeliverLetter(PetAdoptionRecord record, int currentGameDay)
        {
            if (record == null)
                return false;

            return record.State == PetAdoptionState.BowlPurchased
                && currentGameDay > record.PurchaseGameDay
                && record.PurchaseGameDay >= 0;
        }

        /// <summary>
        /// Marca la carta de Marnie como entregada al buzón del cliente.
        /// </summary>
        public static bool MarkLetterDelivered(PetAdoptionRecord record)
        {
            if (record == null || record.State != PetAdoptionState.BowlPurchased)
                return false;

            record.State = PetAdoptionState.LetterDelivered;
            record.UpdatedAtUtc = DateTimeOffset.UtcNow;
            return true;
        }

        /// <summary>
        /// Evalúa si se cumplen las condiciones contextuales para activar la cinemática de adopción.
        /// Debe ocurrir en el mapa propio del jugador, con tiempo soleado y durante el día.
        /// </summary>
        public static bool CanTriggerAdoptionEvent(
            PetAdoptionRecord record,
            long playerId,
            long currentSlotOwnerId,
            string currentMapName,
            bool isRaining,
            int timeOfDay)
        {
            if (record == null)
                return false;

            if (record.State != PetAdoptionState.LetterDelivered)
                return false;

            if (playerId <= 0 || currentSlotOwnerId <= 0 || playerId != currentSlotOwnerId)
                return false;

            if (string.IsNullOrWhiteSpace(record.FarmSlotId) || string.IsNullOrWhiteSpace(currentMapName))
                return false;

            if (!string.Equals(record.FarmSlotId, currentMapName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (isRaining)
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
            if (record == null || record.State != PetAdoptionState.LetterDelivered)
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
            if (record == null || record.State != PetAdoptionState.LetterDelivered)
                return false;

            record.State = PetAdoptionState.Declined;
            record.UpdatedAtUtc = DateTimeOffset.UtcNow;
            return true;
        }
    }
}
