using System;

namespace Pelican4Ever.Core.Pets
{
    /// <summary>
    /// Estados del ciclo de vida de la adopción de mascota de un jugador cliente (Épica 20).
    /// </summary>
    public enum PetAdoptionState
    {
        /// <summary>No ha adquirido cuenco de mascota.</summary>
        None = 0,

        /// <summary>El cuenco para la mascota ha sido adquirido en la carpintería.</summary>
        BowlPurchased = 1,

        /// <summary>Marnie ha enviado la carta al buzón del jugador avisando de su visita.</summary>
        LetterDelivered = 2,

        /// <summary>Mascota adoptada y vinculada autoritativamente a su cuenco en el mapa.</summary>
        Adopted = 3,

        /// <summary>El jugador declinó la adopción durante el evento.</summary>
        Declined = 4
    }

    /// <summary>
    /// Registro de adopción y cuenco de mascota asociado a un jugador cliente.
    /// </summary>
    public class PetAdoptionRecord
    {
        /// <summary>Estado actual del flujo de adopción.</summary>
        public PetAdoptionState State { get; set; } = PetAdoptionState.None;

        /// <summary>Identificador de mapa de la parcela residencial donde se ubica el cuenco (p. ej. "Pelican4Ever_Farm_02").</summary>
        public string? FarmSlotId { get; set; }

        /// <summary>Coordenada X del cuenco en el mapa de residencia.</summary>
        public int BowlTileX { get; set; }

        /// <summary>Coordenada Y del cuenco en el mapa de residencia.</summary>
        public int BowlTileY { get; set; }

        /// <summary>Día de juego (TotalDays) en el que se realizó la compra del cuenco.</summary>
        public int PurchaseGameDay { get; set; } = -1;

        /// <summary>Nombre asignado a la mascota adoptada.</summary>
        public string? PetName { get; set; }

        /// <summary>Tipo de mascota: "Dog" o "Cat".</summary>
        public string? PetType { get; set; } = "Dog";

        /// <summary>Raza/variante cosmética de la mascota (ej. "0", "1", "2").</summary>
        public string? PetBreed { get; set; } = "0";

        /// <summary>Fecha UTC de la última actualización de estado.</summary>
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
