using System;
using System.Collections.Generic;
using System.Globalization;

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

        /// <summary>Clave de <c>modData</c> en la entidad <c>Pet</c> con el propietario autoritativo.</summary>
        public const string PetOwnerModDataKey = "Pelican4Ever.PetOwnerId";

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
            string petBreed = "0",
            long ownerPlayerId = 0)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            if (string.IsNullOrWhiteSpace(slotId))
                return false;

            // Si ya tiene una mascota adoptada en este cuenco, no sobrescribir el estado
            if (record.State == PetAdoptionState.Adopted)
                return false;

            record.State = PetAdoptionState.BowlPurchased;
            if (ownerPlayerId != 0)
                record.OwnerPlayerId = ownerPlayerId;
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

            // Los UniqueMultiplayerID de Stardew pueden ser negativos: solo0 significa «desconocido».
            if (playerId == 0 || currentSlotOwnerId == 0 || playerId != currentSlotOwnerId)
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
            string? petBreed = null,
            string? petGuid = null,
            long ownerPlayerId = 0)
        {
            if (record == null || !IsAwaitingAdoption(record))
                return false;

            if (string.IsNullOrWhiteSpace(petName))
                return false;

            record.State = PetAdoptionState.Adopted;
            record.PetName = petName.Trim();

            if (ownerPlayerId != 0)
                record.OwnerPlayerId = ownerPlayerId;

            if (!string.IsNullOrWhiteSpace(petType))
                record.PetType = petType;

            if (!string.IsNullOrWhiteSpace(petBreed))
                record.PetBreed = petBreed;

            if (!string.IsNullOrWhiteSpace(petGuid))
                record.PetGuid = petGuid;

            record.UpdatedAtUtc = DateTimeOffset.UtcNow;
            return true;
        }

        /// <summary>
        /// Rellena <see cref="PetAdoptionRecord.OwnerPlayerId"/> cuando falta en partidas heredadas.
        /// </summary>
        public static bool EnsureOwnerPlayerId(PetAdoptionRecord? record, long ownerPlayerId)
        {
            if (record == null || ownerPlayerId == 0 || record.OwnerPlayerId == ownerPlayerId)
                return false;

            if (record.OwnerPlayerId != 0)
                return false;

            record.OwnerPlayerId = ownerPlayerId;
            return true;
        }

        /// <summary>Formatea el identificador de propietario para almacenarlo en <c>Pet.modData</c>.</summary>
        public static string FormatPetOwnerId(long ownerPlayerId)
            => ownerPlayerId.ToString(CultureInfo.InvariantCulture);

        /// <summary>Lee el propietario persistido desde <c>Pet.modData</c>.</summary>
        public static bool TryParsePetOwnerId(string? raw, out long ownerPlayerId)
        {
            ownerPlayerId = 0;
            return !string.IsNullOrWhiteSpace(raw)
                && long.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ownerPlayerId)
                && ownerPlayerId != 0;
        }

        /// <summary>
        /// Resuelve el propietario efectivo de una mascota priorizando el registro del servidor,
        /// luego su <c>modData</c> replicado y por último el respaldo recibido.
        /// </summary>
        public static long ResolvePetOwnerId(PetAdoptionRecord? record, string? modDataOwnerRaw, long fallbackOwnerId = 0)
        {
            if (record != null && record.OwnerPlayerId != 0)
                return record.OwnerPlayerId;

            if (TryParsePetOwnerId(modDataOwnerRaw, out long parsed))
                return parsed;

            return fallbackOwnerId;
        }

        /// <summary>Indica si solo el propietario (o un dueño aún sin reclamar) puede reclamar/gestionar la mascota.</summary>
        public static bool CanClaimOrManagePet(long petOwnerId, long actingPlayerId)
        {
            if (actingPlayerId == 0)
                return false;

            return petOwnerId == 0 || petOwnerId == actingPlayerId;
        }

        /// <summary>Los visitantes autorizados pueden interactuar (acariciar) sin adquirir la propiedad.</summary>
        public static bool CanVisitorInteractWithPet(long petOwnerId, long actingPlayerId, bool hasLocationAccess)
        {
            if (actingPlayerId == 0)
                return false;

            if (petOwnerId == actingPlayerId)
                return true;

            return hasLocationAccess;
        }

        /// <summary>
        /// Indica si una mascota está fuera de su conjunto válido de mapas (residencia, anexos y casa privada)
        /// y debe reubicarse junto a su cuenco.
        /// </summary>
        public static bool ShouldRelocateMisplacedPet(string? currentLocationName, IEnumerable<string?> allowedLocationNames)
        {
            if (allowedLocationNames == null)
                return false;

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string? name in allowedLocationNames)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    allowed.Add(name);
            }

            if (allowed.Count == 0)
                return false;

            return string.IsNullOrWhiteSpace(currentLocationName) || !allowed.Contains(currentLocationName);
        }

        /// <summary>
        /// Normaliza el GUID de la mascota. Si el registro no lo tiene (partidas antiguas), se genera uno
        /// nuevo y se devuelve para que pueda persistirse.
        /// </summary>
        public static string EnsurePetGuid(PetAdoptionRecord record)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            if (!string.IsNullOrWhiteSpace(record.PetGuid)
                && Guid.TryParse(record.PetGuid, out Guid parsed))
            {
                return parsed.ToString();
            }

            string generated = Guid.NewGuid().ToString();
            record.PetGuid = generated;
            return generated;
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
