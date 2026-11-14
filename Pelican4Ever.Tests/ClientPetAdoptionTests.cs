using System;
using Pelican4Ever.Core.Pets;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class ClientPetAdoptionTests
    {
        [Fact]
        public void RecordPurchase_ValidInputs_SetsStateToBowlPurchased()
        {
            var record = new PetAdoptionRecord();
            bool result = PetAdoptionLogic.RecordPurchase(record, "Pelican4Ever_Farm_02", 53, 7, 10, "Cat", "1");

            Assert.True(result);
            Assert.Equal(PetAdoptionState.BowlPurchased, record.State);
            Assert.Equal("Pelican4Ever_Farm_02", record.FarmSlotId);
            Assert.Equal(53, record.BowlTileX);
            Assert.Equal(7, record.BowlTileY);
            Assert.Equal(10, record.PurchaseGameDay);
            Assert.Equal("Cat", record.PetType);
            Assert.Equal("1", record.PetBreed);
        }

        [Fact]
        public void RecordPurchase_AlreadyAdopted_ReturnsFalseAndPreservesState()
        {
            var record = new PetAdoptionRecord
            {
                State = PetAdoptionState.Adopted,
                PetName = "Misi",
                FarmSlotId = "Pelican4Ever_Farm_01"
            };

            bool result = PetAdoptionLogic.RecordPurchase(record, "Pelican4Ever_Farm_01", 53, 7, 15);

            Assert.False(result);
            Assert.Equal(PetAdoptionState.Adopted, record.State);
            Assert.Equal("Misi", record.PetName);
        }

        [Fact]
        public void RecordPurchase_InvalidSlot_ReturnsFalse()
        {
            var record = new PetAdoptionRecord();
            Assert.False(PetAdoptionLogic.RecordPurchase(record, "", 53, 7, 10));
            Assert.False(PetAdoptionLogic.RecordPurchase(record, "   ", 53, 7, 10));
            Assert.Equal(PetAdoptionState.None, record.State);
        }

        [Fact]
        public void HasWokenSincePurchase_RequiresANewDayAfterPurchase()
        {
            var record = new PetAdoptionRecord
            {
                State = PetAdoptionState.BowlPurchased,
                PurchaseGameDay = 5
            };

            // Mismo día: todavía no ha dormido desde la compra
            Assert.False(PetAdoptionLogic.HasWokenSincePurchase(record, 5));

            // Siguiente despertar: Marnie debe acudir
            Assert.True(PetAdoptionLogic.HasWokenSincePurchase(record, 6));

            // Despertares posteriores: sigue pendiente si aún no se resolvió
            Assert.True(PetAdoptionLogic.HasWokenSincePurchase(record, 7));

            // Compatibilidad con partidas antiguas que ya tenían la carta entregada
            record.State = PetAdoptionState.LetterDelivered;
            Assert.True(PetAdoptionLogic.HasWokenSincePurchase(record, 6));

            // Estados ya resueltos no vuelven a disparar la visita
            record.State = PetAdoptionState.Adopted;
            Assert.False(PetAdoptionLogic.HasWokenSincePurchase(record, 6));

            record.State = PetAdoptionState.Declined;
            Assert.False(PetAdoptionLogic.HasWokenSincePurchase(record, 6));

            // Sin día de compra válido no se puede determinar el despertar en el flujo nuevo
            record.State = PetAdoptionState.BowlPurchased;
            record.PurchaseGameDay = -1;
            Assert.False(PetAdoptionLogic.HasWokenSincePurchase(record, 6));

            // Los registros heredados con carta entregada pero sin día de compra sí pueden completarse
            record.State = PetAdoptionState.LetterDelivered;
            Assert.True(PetAdoptionLogic.HasWokenSincePurchase(record, 6));
        }

        [Fact]
        public void CanTriggerAdoptionEvent_ValidConditions_ReturnsTrue()
        {
            var record = new PetAdoptionRecord
            {
                State = PetAdoptionState.BowlPurchased,
                FarmSlotId = "Pelican4Ever_Farm_03"
            };

            long playerId = 1001;
            long slotOwnerId = 1001;
            string map = "Pelican4Ever_Farm_03";
            int timeOfDay = 800; // 08:00 AM

            Assert.True(PetAdoptionLogic.CanTriggerAdoptionEvent(record, playerId, slotOwnerId, map, timeOfDay));

            // El flujo antiguo (carta ya entregada) también puede completarse
            record.State = PetAdoptionState.LetterDelivered;
            Assert.True(PetAdoptionLogic.CanTriggerAdoptionEvent(record, playerId, slotOwnerId, map, timeOfDay));
        }

        [Fact]
        public void CanTriggerAdoptionEvent_AcceptsNegativePlayerIds()
        {
            // Stardew genera UniqueMultiplayerID negativos para algunos jugadores: solo 0 es «desconocido».
            var record = new PetAdoptionRecord
            {
                State = PetAdoptionState.BowlPurchased,
                FarmSlotId = "Pelican4Ever_Farm_02"
            };

            long playerId = -1349064639806311618;

            Assert.True(PetAdoptionLogic.CanTriggerAdoptionEvent(record, playerId, playerId, "Pelican4Ever_Farm_02", 920));

            // Un propietario desconocido (0) sigue bloqueando la visita
            Assert.False(PetAdoptionLogic.CanTriggerAdoptionEvent(record, playerId, 0, "Pelican4Ever_Farm_02", 920));
            Assert.False(PetAdoptionLogic.CanTriggerAdoptionEvent(record, 0, 0, "Pelican4Ever_Farm_02", 920));
        }

        [Fact]
        public void CanTriggerAdoptionEvent_EnforcesOwnerAndMapIsolation()
        {
            var record = new PetAdoptionRecord
            {
                State = PetAdoptionState.BowlPurchased,
                FarmSlotId = "Pelican4Ever_Farm_03"
            };

            // Jugador distinto al dueño de la ranura
            Assert.False(PetAdoptionLogic.CanTriggerAdoptionEvent(record, 1002, 1001, "Pelican4Ever_Farm_03", 800));

            // Mapa distinto a la residencia del jugador
            Assert.False(PetAdoptionLogic.CanTriggerAdoptionEvent(record, 1001, 1001, "Farm", 800));
            Assert.False(PetAdoptionLogic.CanTriggerAdoptionEvent(record, 1001, 1001, "Pelican4Ever_Farm_01", 800));

            // Fuera de horario (de noche)
            Assert.False(PetAdoptionLogic.CanTriggerAdoptionEvent(record, 1001, 1001, "Pelican4Ever_Farm_03", 2200));

            // Un estado ya resuelto no dispara la visita
            record.State = PetAdoptionState.Adopted;
            Assert.False(PetAdoptionLogic.CanTriggerAdoptionEvent(record, 1001, 1001, "Pelican4Ever_Farm_03", 800));

            record.State = PetAdoptionState.Declined;
            Assert.False(PetAdoptionLogic.CanTriggerAdoptionEvent(record, 1001, 1001, "Pelican4Ever_Farm_03", 800));
        }

        [Fact]
        public void RecordAdoption_Success_SetsAdoptedAndPetName()
        {
            var record = new PetAdoptionRecord
            {
                State = PetAdoptionState.BowlPurchased,
                FarmSlotId = "Pelican4Ever_Farm_02"
            };

            bool result = PetAdoptionLogic.RecordAdoption(record, "Tobi", "Dog", "2");

            Assert.True(result);
            Assert.Equal(PetAdoptionState.Adopted, record.State);
            Assert.Equal("Tobi", record.PetName);
            Assert.Equal("Dog", record.PetType);
            Assert.Equal("2", record.PetBreed);

            // Compatibilidad con registros antiguos que venían de la carta
            record.State = PetAdoptionState.LetterDelivered;
            Assert.True(PetAdoptionLogic.RecordAdoption(record, "Luna"));

            // No permite nombres vacíos o solo espacios
            record.State = PetAdoptionState.BowlPurchased;
            Assert.False(PetAdoptionLogic.RecordAdoption(record, ""));
            Assert.False(PetAdoptionLogic.RecordAdoption(record, "   "));
        }

        [Fact]
        public void RecordAdoption_StoresPetGuid()
        {
            var record = new PetAdoptionRecord
            {
                State = PetAdoptionState.BowlPurchased,
                FarmSlotId = "Pelican4Ever_Farm_02"
            };

            string guid = Guid.NewGuid().ToString();
            Assert.True(PetAdoptionLogic.RecordAdoption(record, "Tobi", "Dog", "2", guid));
            Assert.Equal(guid, record.PetGuid);
        }

        [Fact]
        public void EnsurePetGuid_IsStableAndRegeneratesInvalidValues()
        {
            var record = new PetAdoptionRecord { State = PetAdoptionState.Adopted };

            string first = PetAdoptionLogic.EnsurePetGuid(record);
            Assert.True(Guid.TryParse(first, out _));
            Assert.Equal(first, PetAdoptionLogic.EnsurePetGuid(record));

            record.PetGuid = "no-es-un-guid";
            string regenerated = PetAdoptionLogic.EnsurePetGuid(record);
            Assert.True(Guid.TryParse(regenerated, out _));
            Assert.NotEqual("no-es-un-guid", regenerated);
        }

        [Fact]
        public void RecordDeclined_Success_SetsDeclined()
        {
            var record = new PetAdoptionRecord
            {
                State = PetAdoptionState.BowlPurchased,
                FarmSlotId = "Pelican4Ever_Farm_02"
            };

            bool result = PetAdoptionLogic.RecordDeclined(record);

            Assert.True(result);
            Assert.Equal(PetAdoptionState.Declined, record.State);

            // El flujo antiguo ya no puede declinarse dos veces
            Assert.False(PetAdoptionLogic.RecordDeclined(record));
        }

        [Fact]
        public void PetAdoptionTranslations_ResolveInEnglishAndSpanish()
        {
            var enDict = new Dictionary<string, string>
            {
                ["marnie.pet_event.intro"] = "Hello, {{player}}! Look who I brought along.",
                ["marnie.pet_event.accepted"] = "Take care of {{petName}}!"
            };
            var esDict = new Dictionary<string, string>
            {
                ["marnie.pet_event.intro"] = "¡Hola, {{player}}! Mira a quién te he traído.",
                ["marnie.pet_event.accepted"] = "¡Cuida de {{petName}}!"
            };

            var enProvider = new Pelican4Ever.Core.Localization.DictionaryTranslationProvider(enDict);
            var esProvider = new Pelican4Ever.Core.Localization.DictionaryTranslationProvider(esDict);

            Assert.Equal("Hello, Alex! Look who I brought along.", enProvider.Get("marnie.pet_event.intro", new { player = "Alex" }));
            Assert.Equal("¡Hola, Alex! Mira a quién te he traído.", esProvider.Get("marnie.pet_event.intro", new { player = "Alex" }));

            Assert.Equal("Take care of Luna!", enProvider.Get("marnie.pet_event.accepted", new { petName = "Luna" }));
            Assert.Equal("¡Cuida de Luna!", esProvider.Get("marnie.pet_event.accepted", new { petName = "Luna" }));
        }

        [Fact]
        public void PetOwnership_RecordAndClaimValidation_EnforceOwnerAuthority()
        {
            var record = new PetAdoptionRecord();
            Assert.True(PetAdoptionLogic.RecordPurchase(record, "Pelican4Ever_Farm_02", 14, 18, 5, "Dog", "1", ownerPlayerId: 2020));
            Assert.Equal(2020L, record.OwnerPlayerId);

            Assert.True(PetAdoptionLogic.RecordAdoption(record, "Boby", "Dog", "1", Guid.NewGuid().ToString(), ownerPlayerId: 2020));
            Assert.Equal(2020L, record.OwnerPlayerId);

            Assert.True(PetAdoptionLogic.CanClaimOrManagePet(record.OwnerPlayerId, 2020L));
            Assert.False(PetAdoptionLogic.CanClaimOrManagePet(record.OwnerPlayerId, 3030L));
            Assert.False(PetAdoptionLogic.CanClaimOrManagePet(record.OwnerPlayerId, 0L));

            // Visitante autorizado puede acariciar sin apropiarse de la mascota
            Assert.True(PetAdoptionLogic.CanVisitorInteractWithPet(record.OwnerPlayerId, 3030L, hasLocationAccess: true));
            Assert.False(PetAdoptionLogic.CanVisitorInteractWithPet(record.OwnerPlayerId, 3030L, hasLocationAccess: false));
            Assert.True(PetAdoptionLogic.CanVisitorInteractWithPet(record.OwnerPlayerId, 2020L, hasLocationAccess: false));
        }

        [Fact]
        public void PetOwnership_ModDataAndLegacyBackfill_ResolveAndDetectMisplacedPets()
        {
            var legacy = new PetAdoptionRecord
            {
                State = PetAdoptionState.Adopted,
                FarmSlotId = "Pelican4Ever_Farm_03",
                PetName = "Nube"
            };

            Assert.True(PetAdoptionLogic.EnsureOwnerPlayerId(legacy, -9001L));
            Assert.Equal(-9001L, legacy.OwnerPlayerId);
            Assert.False(PetAdoptionLogic.EnsureOwnerPlayerId(legacy, 9999L));

            string serialized = PetAdoptionLogic.FormatPetOwnerId(-9001L);
            Assert.True(PetAdoptionLogic.TryParsePetOwnerId(serialized, out long parsed));
            Assert.Equal(-9001L, parsed);

            var allowed = new[] { "Pelican4Ever_Farm_03", "Pelican4Ever_Farm_03_Home", "Pelican4Ever_Annex_03_North" };
            Assert.False(PetAdoptionLogic.ShouldRelocateMisplacedPet("Pelican4Ever_Farm_03", allowed));
            Assert.False(PetAdoptionLogic.ShouldRelocateMisplacedPet("Pelican4Ever_Annex_03_North", allowed));
            Assert.True(PetAdoptionLogic.ShouldRelocateMisplacedPet("Farm", allowed));
            Assert.True(PetAdoptionLogic.ShouldRelocateMisplacedPet(null, allowed));
        }
    }
}
