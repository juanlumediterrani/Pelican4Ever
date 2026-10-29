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
    }
}
