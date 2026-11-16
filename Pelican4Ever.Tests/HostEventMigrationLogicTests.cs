using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public sealed class HostEventMigrationLogicTests
    {
        [Fact]
        public void EvaluateMigration_OnDay5PlusWithoutCcDoorUnlock_UnlocksImmediatelyEvenInEveningOrRain()
        {
            var input = new HostEventMigrationInput
            {
                HostEventMigrationVersion = 0,
                DaysPlayed = 5,
                TimeOfDay = 1700,
                IsWeatherSunny = false,
                IsJojaMember = false,
                HasCcDoorUnlock = false
            };

            HostEventMigrationPlan plan = HostEventMigrationLogic.EvaluateMigration(input);

            Assert.True(plan.UnlockCommunityCenter);
            Assert.False(plan.UnlockRustyKey);
            Assert.True(plan.StampMigrationVersion);
            Assert.True(plan.HasAnyWorldChange);
        }

        [Fact]
        public void EvaluateMigration_OnDay4WithoutCcScroll_DoesNotUnlockEarlyAndStampsVersion()
        {
            var input = new HostEventMigrationInput
            {
                HostEventMigrationVersion = 0,
                DaysPlayed = 4,
                TimeOfDay = 1700,
                IsWeatherSunny = true,
                IsJojaMember = false,
                HasCcDoorUnlock = false
            };

            HostEventMigrationPlan plan = HostEventMigrationLogic.EvaluateMigration(input);

            Assert.False(plan.UnlockCommunityCenter);
            Assert.True(plan.StampMigrationVersion);
            Assert.False(plan.HasAnyWorldChange);
        }

        [Fact]
        public void EvaluateMigration_ForJojaMember_DoesNotUnlockCommunityCenter()
        {
            var input = new HostEventMigrationInput
            {
                HostEventMigrationVersion = 0,
                DaysPlayed = 15,
                TimeOfDay = 1800,
                IsWeatherSunny = true,
                IsJojaMember = true,
                HasCcDoorUnlock = false,
                HasCcIsCompleteMail = true,
                AnyPlayerSeenJojaCeremony = false
            };

            HostEventMigrationPlan plan = HostEventMigrationLogic.EvaluateMigration(input);

            Assert.False(plan.UnlockCommunityCenter);
            Assert.False(plan.MarkCcCeremonySeen);
            Assert.True(plan.MarkJojaCeremonySeen);
        }

        [Fact]
        public void EvaluateMigration_WhenMuseum60OrClientSaw295672_UnlocksRustyKey()
        {
            var byTrigger = new HostEventMigrationInput
            {
                HostEventMigrationVersion = 0,
                DaysPlayed = 20,
                AnyPlayerSeenMuseumRustyTrigger = true,
                MuseumPiecesCount = 45,
                HasRustyKey = false
            };
            var byCount = new HostEventMigrationInput
            {
                HostEventMigrationVersion = 1,
                DaysPlayed = 20,
                AnyPlayerSeenMuseumRustyTrigger = false,
                MuseumPiecesCount = 60,
                HasRustyKey = false
            };

            Assert.True(HostEventMigrationLogic.EvaluateMigration(byTrigger).UnlockRustyKey);
            Assert.True(HostEventMigrationLogic.EvaluateMigration(byCount).UnlockRustyKey);
        }

        [Fact]
        public void EvaluateMigration_WhenCcIsCompleteWithoutCeremonyEvent_RepairsEventSeen()
        {
            var input = new HostEventMigrationInput
            {
                HostEventMigrationVersion = 0,
                DaysPlayed = 40,
                HasCcDoorUnlock = true,
                HasCcIsCompleteMail = true,
                AnyPlayerSeenCcCeremony = false
            };

            HostEventMigrationPlan plan = HostEventMigrationLogic.EvaluateMigration(input);

            Assert.False(plan.UnlockCommunityCenter);
            Assert.True(plan.MarkCcCeremonySeen);
        }

        [Theory]
        [InlineData(4u, 1800, false, false, false, false, false)]
        [InlineData(5u, 900, true, false, false, false, false)]
        [InlineData(5u, 1310, true, false, false, false, true)]
        [InlineData(5u, 900, false, false, false, false, true)]
        [InlineData(6u, 800, true, false, false, false, true)]
        [InlineData(5u, 900, true, true, false, false, false)]
        [InlineData(5u, 900, true, false, true, false, false)]
        [InlineData(3u, 900, true, false, false, true, true)]
        public void ShouldAutoUnlockCommunityCenter_RespectsDay5SunnyMorningWindowAndFallbacks(
            uint daysPlayed,
            int timeOfDay,
            bool isSunny,
            bool isJoja,
            bool hasCcDoorUnlock,
            bool anyScrollOrText,
            bool expected)
        {
            Assert.Equal(
                expected,
                HostEventMigrationLogic.ShouldAutoUnlockCommunityCenter(
                    daysPlayed,
                    timeOfDay,
                    isSunny,
                    isJoja,
                    hasCcDoorUnlock,
                    anyScrollOrText));
        }

        [Theory]
        [InlineData(false, false, false, false, true)]
        [InlineData(true, false, false, false, false)]
        [InlineData(false, true, false, false, false)]
        [InlineData(false, false, true, false, false)]
        [InlineData(false, false, false, true, false)]
        public void ShouldGrantRatProblemQuest_IsIdempotent(
            bool alreadySawCcEvent,
            bool hasSeenJunimoNote,
            bool canReadJunimoText,
            bool hasQuest26,
            bool expected)
        {
            Assert.Equal(
                expected,
                HostEventMigrationLogic.ShouldGrantRatProblemQuest(
                    alreadySawCcEvent,
                    hasSeenJunimoNote,
                    canReadJunimoText,
                    hasQuest26));
        }

        [Fact]
        public void ServerWorldState_MigrateFromV8ToV9_PreservesPendingHostEventMigrationVersion()
        {
            var state = new ServerWorldState { SchemaVersion = 8 };

            bool changed = state.Migrate();

            Assert.True(changed);
            Assert.Equal(ServerWorldState.CurrentSchemaVersion, state.SchemaVersion);
            Assert.Equal(0, state.HostEventMigrationVersion);
        }
    }
}
