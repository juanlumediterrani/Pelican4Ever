using System.Collections.Generic;
using Pelican4Ever.Core.Festivals;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class FestivalScoreAndCeremonyTests
    {
        [Fact]
        public void RecordScore_AddsNewScore_AndKeepsHighest()
        {
            var list = new List<FestivalScoreEntry>();

            FestivalScoreLogic.RecordScore(list, "spring13", 13, 100L, "Alice", 8);
            Assert.Single(list);
            Assert.Equal(8, list[0].Score);

            // Re-intento con menor puntuación: conserva la mayor
            FestivalScoreLogic.RecordScore(list, "spring13", 13, 100L, "Alice", 5);
            Assert.Single(list);
            Assert.Equal(8, list[0].Score);

            // Re-intento con mayor puntuación: se actualiza
            FestivalScoreLogic.RecordScore(list, "spring13", 13, 100L, "Alice", 12);
            Assert.Single(list);
            Assert.Equal(12, list[0].Score);
        }

        [Fact]
        public void DetermineWinner_ReturnsHighestScorePlayer()
        {
            var list = new List<FestivalScoreEntry>();
            FestivalScoreLogic.RecordScore(list, "spring13", 13, 100L, "Alice", 9);
            FestivalScoreLogic.RecordScore(list, "spring13", 13, 200L, "Bob", 14);
            FestivalScoreLogic.RecordScore(list, "spring13", 13, 300L, "Charlie", 11);

            var winner = FestivalScoreLogic.DetermineWinner(list, "spring13", 13);
            Assert.NotNull(winner);
            Assert.Equal(200L, winner.PlayerId);
            Assert.Equal("Bob", winner.PlayerName);
            Assert.Equal(14, winner.Score);
        }

        [Fact]
        public void FormatLeaderboard_FormatsRankedList()
        {
            var list = new List<FestivalScoreEntry>();
            FestivalScoreLogic.RecordScore(list, "spring13", 13, 100L, "Alice", 9);
            FestivalScoreLogic.RecordScore(list, "spring13", 13, 200L, "Bob", 14);

            var leaderboard = FestivalScoreLogic.FormatLeaderboard(list, "spring13", 13);
            Assert.Equal(2, leaderboard.Count);
            Assert.Contains("1. Bob: 14 pts", leaderboard[0]);
            Assert.Contains("2. Alice: 9 pts", leaderboard[1]);
        }

        [Theory]
        [InlineData(2045, 2100, 15, true)]
        [InlineData(2050, 2100, 15, true)]
        [InlineData(2030, 2100, 15, false)] // Too early
        [InlineData(2100, 2100, 15, false)] // Window ended, ceremony starts
        public void CeremonyLogic_IsWarningWindow_DetectsWindow(int time, int ceremonyTime, int lead, bool expected)
        {
            bool inWindow = CeremonyLogic.IsWarningWindow(time, ceremonyTime, lead);
            Assert.Equal(expected, inWindow);
        }

        [Theory]
        [InlineData("Beach", "Beach", true)]
        [InlineData("beach", "Beach", true)]
        [InlineData("Town", "Beach", false)]
        [InlineData("Farm", "Beach", false)]
        [InlineData(null, "Beach", false)]
        public void CeremonyLogic_IsPlayerInCeremonyLocation_ChecksMatch(string? playerLoc, string? ceremonyLoc, bool expected)
        {
            bool match = CeremonyLogic.IsPlayerInCeremonyLocation(playerLoc, ceremonyLoc);
            Assert.Equal(expected, match);
        }

        [Theory]
        [InlineData("festival_spring13", "spring13")]
        [InlineData("FESTIVAL_WINTER8", "winter8")]
        [InlineData("spring13", "spring13")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void FestivalScoreLogic_NormalizeFestivalId_NormalizesPrefixAndCase(string? input, string expected)
        {
            Assert.Equal(expected, FestivalScoreLogic.NormalizeFestivalId(input));
        }

        [Fact]
        public void BuildLeaderboardLetterText_WithNoEntries_ShowsEmptyStandingsMessage()
        {
            var text = FestivalScoreLogic.BuildLeaderboardLetterText(new List<FestivalScoreEntry>(), "spring13", 13, "Fiesta del Huevo");
            Assert.Contains("=== Tablón Oficial: Fiesta del Huevo ===", text);
            Assert.Contains("Todavía no hay marcas registradas", text);
        }

        [Fact]
        public void BuildLeaderboardLetterText_WithScores_ShowsRankedStandingsWithMedals()
        {
            var list = new List<FestivalScoreEntry>();
            FestivalScoreLogic.RecordScore(list, "spring13", 13, 100L, "Alice", 9);
            FestivalScoreLogic.RecordScore(list, "spring13", 13, 200L, "Bob", 14);

            var text = FestivalScoreLogic.BuildLeaderboardLetterText(list, "spring13", 13, "Fiesta del Huevo");
            Assert.Contains("=== Clasificación Oficial: Fiesta del Huevo ===", text);
            Assert.Contains("1. [1er Puesto] Bob — 14 pts", text);
            Assert.Contains("2. [2º Puesto] Alice — 9 pts", text);
            Assert.Contains("23:50", text);
        }

        [Theory]
        [InlineData("summer", 11, true, "Beach", "Luau")]
        [InlineData("summer", 28, true, "Beach", "Danza de las Medusas Lunares")]
        [InlineData("spring", 13, false, null, "Ceremonia Comunitaria")]
        [InlineData("fall", 16, false, null, "Ceremonia Comunitaria")]
        [InlineData("winter", 8, false, null, "Ceremonia Comunitaria")]
        public void CeremonyLogic_FestivalDetectionAndDetails(string season, int day, bool isCeremony, string? location, string name)
        {
            Assert.Equal(isCeremony, CeremonyLogic.IsCeremonyFestival(season, day));
            Assert.Equal(location, CeremonyLogic.GetCeremonyLocation(season, day));
            Assert.Equal(name, CeremonyLogic.GetCeremonyFestivalName(season, day));
        }
    }
}
