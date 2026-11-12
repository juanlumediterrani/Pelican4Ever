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
        public void ShouldDeliverLetter_EvaluatesGameDayProgressionCorrectly()
        {
            var record = new PetAdoptionRecord
            {
                State = PetAdoptionState.BowlPurchased,
                PurchaseGameDay = 5
            };

            // Mismo día: no debe enviarse todavía
            Assert.False(PetAdoptionLogic.ShouldDeliverLetter(record, 5));

            // Día siguiente: debe enviarse
            Assert.True(PetAdoptionLogic.ShouldDeliverLetter(record, 6));

            // Días posteriores: sigue debiendo enviarse si aún no se marcó entregada
            Assert.True(PetAdoptionLogic.ShouldDeliverLetter(record, 7));

            // Si ya no está en BowlPurchased (ej. ya entregada o adoptada): false
            record.State = PetAdoptionState.LetterDelivered;
            Assert.False(PetAdoptionLogic.ShouldDeliverLetter(record, 6));

            record.State = PetAdoptionState.Adopted;
            Assert.False(PetAdoptionLogic.ShouldDeliverLetter(record, 6));
        }

        [Fact]
        public void MarkLetterDelivered_TransitionsStateCorrectly()
        {
            var record = new PetAdoptionRecord
            {
                State = PetAdoptionState.BowlPurchased
            };

            bool result = PetAdoptionLogic.MarkLetterDelivered(record);
            Assert.True(result);
            Assert.Equal(PetAdoptionState.LetterDelivered, record.State);

            // Intentar marcar de nuevo sobre estado entregado
            Assert.False(PetAdoptionLogic.MarkLetterDelivered(record));
        }

        [Fact]
        public void CanTriggerAdoptionEvent_ValidConditions_ReturnsTrue()
        {
            var record = new PetAdoptionRecord
            {
                State = PetAdoptionState.LetterDelivered,
                FarmSlotId = "Pelican4Ever_Farm_03"
            };

            long playerId = 1001;
            long slotOwnerId = 1001;
            string map = "Pelican4Ever_Farm_03";
            bool isRaining = false;
            int timeOfDay = 800; // 08:00 AM

            Assert.True(PetAdoptionLogic.CanTriggerAdoptionEvent(record, playerId, slotOwnerId, map, isRaining, timeOfDay));
        }

        [Fact]
        public void CanTriggerAdoptionEvent_EnforcesOwnerAndMapIsolation()
        {
            var record = new PetAdoptionRecord
            {
                State = PetAdoptionState.LetterDelivered,
                FarmSlotId = "Pelican4Ever_Farm_03"
            };

            // Jugador distinto al dueño de la ranura
            Assert.False(PetAdoptionLogic.CanTriggerAdoptionEvent(record, 1002, 1001, "Pelican4Ever_Farm_03", false, 800));

            // Mapa distinto a la residencia del jugador
            Assert.False(PetAdoptionLogic.CanTriggerAdoptionEvent(record, 1001, 1001, "Farm", false, 800));
            Assert.False(PetAdoptionLogic.CanTriggerAdoptionEvent(record, 1001, 1001, "Pelican4Ever_Farm_01", false, 800));

            // Lluvia
            Assert.False(PetAdoptionLogic.CanTriggerAdoptionEvent(record, 1001, 1001, "Pelican4Ever_Farm_03", true, 800));

            // Fuera de horario (de noche)
            Assert.False(PetAdoptionLogic.CanTriggerAdoptionEvent(record, 1001, 1001, "Pelican4Ever_Farm_03", false, 2200));

            // Si la carta aún no fue entregada
            record.State = PetAdoptionState.BowlPurchased;
            Assert.False(PetAdoptionLogic.CanTriggerAdoptionEvent(record, 1001, 1001, "Pelican4Ever_Farm_03", false, 800));
        }

        [Fact]
        public void RecordAdoption_Success_SetsAdoptedAndPetName()
        {
            var record = new PetAdoptionRecord
            {
                State = PetAdoptionState.LetterDelivered,
                FarmSlotId = "Pelican4Ever_Farm_02"
            };

            bool result = PetAdoptionLogic.RecordAdoption(record, "Tobi", "Dog", "2");

            Assert.True(result);
            Assert.Equal(PetAdoptionState.Adopted, record.State);
            Assert.Equal("Tobi", record.PetName);
            Assert.Equal("Dog", record.PetType);
            Assert.Equal("2", record.PetBreed);

            // No permite nombres vacíos o solo espacios
            record.State = PetAdoptionState.LetterDelivered;
            Assert.False(PetAdoptionLogic.RecordAdoption(record, ""));
            Assert.False(PetAdoptionLogic.RecordAdoption(record, "   "));
        }

        [Fact]
        public void RecordDeclined_Success_SetsDeclined()
        {
            var record = new PetAdoptionRecord
            {
                State = PetAdoptionState.LetterDelivered,
                FarmSlotId = "Pelican4Ever_Farm_02"
            };

            bool result = PetAdoptionLogic.RecordDeclined(record);

            Assert.True(result);
            Assert.Equal(PetAdoptionState.Declined, record.State);
        }

        [Fact]
        public void PetAdoptionTranslations_ResolveInEnglishAndSpanish()
        {
            var enDict = new Dictionary<string, string>
            {
                ["marnie.pet_letter.title"] = "A Stray Animal Needs a Home",
                ["marnie.pet_letter.body"] = "Dear {{player}}, Marnie here.",
                ["marnie.pet_event.accepted"] = "Take care of {{petName}}!"
            };
            var esDict = new Dictionary<string, string>
            {
                ["marnie.pet_letter.title"] = "Un animalito busca hogar",
                ["marnie.pet_letter.body"] = "Querido/a {{player}}, te escribe Marnie.",
                ["marnie.pet_event.accepted"] = "¡Cuida de {{petName}}!"
            };

            var enProvider = new Pelican4Ever.Core.Localization.DictionaryTranslationProvider(enDict);
            var esProvider = new Pelican4Ever.Core.Localization.DictionaryTranslationProvider(esDict);

            Assert.Equal("A Stray Animal Needs a Home", enProvider.Get("marnie.pet_letter.title"));
            Assert.Equal("Un animalito busca hogar", esProvider.Get("marnie.pet_letter.title"));

            Assert.Equal("Dear Alex, Marnie here.", enProvider.Get("marnie.pet_letter.body", new { player = "Alex" }));
            Assert.Equal("Querido/a Alex, te escribe Marnie.", esProvider.Get("marnie.pet_letter.body", new { player = "Alex" }));

            Assert.Equal("Take care of Luna!", enProvider.Get("marnie.pet_event.accepted", new { petName = "Luna" }));
            Assert.Equal("¡Cuida de Luna!", esProvider.Get("marnie.pet_event.accepted", new { petName = "Luna" }));
        }
    }
}
