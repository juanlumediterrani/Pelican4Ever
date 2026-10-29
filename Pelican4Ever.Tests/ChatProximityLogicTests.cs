using Pelican4Ever.Core.Chat;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class ChatProximityLogicTests
    {
        [Theory]
        [InlineData("Town", 50, 50, "Town", 50, 50, 30, true)]
        [InlineData("Town", 50, 50, "Town", 70, 50, 30, true)] // dist 20 <= 30
        [InlineData("Town", 50, 50, "Town", 81, 50, 30, false)] // dist 31 > 30
        [InlineData("Town", 50, 50, "Farm", 50, 50, 30, false)] // Different locations
        [InlineData(null, 50, 50, "Town", 50, 50, 30, false)]
        [InlineData("Town", 50, 50, "", 50, 50, 30, false)]
        public void IsWithinProximity_CalculatesCorrectly(
            string? loc1, double x1, double y1,
            string? loc2, double x2, double y2,
            double maxDist, bool expected)
        {
            bool result = ChatProximityLogic.IsWithinProximity(loc1, x1, y1, loc2, x2, y2, maxDist);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void FormatLocalMessage_FormatsAsExpected()
        {
            string formatted = ChatProximityLogic.FormatLocalMessage("Alice", "Hola a todos");
            Assert.Equal("[Local] Alice: Hola a todos", formatted);
        }

        [Theory]
        [InlineData("Hola mundo", ChatChannelType.Global, null, "Hola mundo")]
        [InlineData("/g Saludos globales", ChatChannelType.Global, null, "Saludos globales")]
        [InlineData("/global Saludos globales", ChatChannelType.Global, null, "Saludos globales")]
        [InlineData("/l Hola vecinos", ChatChannelType.Local, null, "Hola vecinos")]
        [InlineData("/local Hola vecinos", ChatChannelType.Local, null, "Hola vecinos")]
        [InlineData("/w Bob Hola Bob secreto", ChatChannelType.Whisper, "Bob", "Hola Bob secreto")]
        [InlineData("/whisper Alice Secret", ChatChannelType.Whisper, "Alice", "Secret")]
        [InlineData("/dm Charlie Private", ChatChannelType.Whisper, "Charlie", "Private")]
        [InlineData("/help", ChatChannelType.Command, null, "/help")]
        [InlineData("/pause", ChatChannelType.Command, null, "/pause")]
        public void ParseInput_ParsesCommandsCorrectly(
            string input,
            ChatChannelType expectedChannel,
            string? expectedTarget,
            string expectedMessage)
        {
            var parsed = ChatProximityLogic.ParseInput(input);
            Assert.Equal(expectedChannel, parsed.Channel);
            Assert.Equal(expectedTarget, parsed.Target);
            Assert.Equal(expectedMessage, parsed.Message);
        }

        [Fact]
        public void ParseInput_EmptyOrWhitespace_ReturnsEmptyGlobal()
        {
            var parsed = ChatProximityLogic.ParseInput("   ");
            Assert.Equal(ChatChannelType.Global, parsed.Channel);
            Assert.Empty(parsed.Message);
        }
    }
}
