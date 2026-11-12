using System.Text.Json;
using Pelican4Ever.Core.Players;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class AgentToolOffsetTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("SomeOther.Pack/Player/Body")]
        public void OtherBodies_AreNotMoved(string? bodyId)
        {
            for (int dir = 0; dir < 4; dir++)
                Assert.Equal((0, 0), AgentToolOffset.Get(bodyId, dir));
        }

        [Fact]
        public void Dr4co_LowersTheToolInEveryDirection_AndPullsItBackOnTheSides()
        {
            for (int dir = 0; dir < 4; dir++)
                Assert.True(AgentToolOffset.Get(AgentToolOffset.Dr4coBodyId, dir).Y > 0);

            (int rightX, int rightY) = AgentToolOffset.Get(AgentToolOffset.Dr4coBodyId, 1);
            (int leftX, int leftY) = AgentToolOffset.Get(AgentToolOffset.Dr4coBodyId, 3);
            Assert.True(rightX < 0);   // facing right: towards the body
            Assert.True(leftX > 0);    // facing left: towards the body
            Assert.Equal(rightY, leftY);
        }

        [Fact]
        public void UnknownDirection_IsNotMoved()
        {
            Assert.Equal((0, 0), AgentToolOffset.Get(AgentToolOffset.Dr4coBodyId, 7));
        }

        [Fact]
        public void Dr4coBodyId_MatchesTheFashionSensePack()
        {
            string root = FindRepoRoot();
            string pack = Path.Combine(root, "agents", "Dr4co.FashionSense");
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(pack, "manifest.json")));
            using JsonDocument body = JsonDocument.Parse(File.ReadAllText(Path.Combine(pack, "Bodies", "Dr4co", "body.json")));

            string expected = $"{manifest.RootElement.GetProperty("UniqueID").GetString()}/Player/{body.RootElement.GetProperty("Name").GetString()}";
            Assert.Equal(AgentToolOffset.Dr4coBodyId, expected);
        }

        private static string FindRepoRoot()
        {
            for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "agents", "Dr4co.FashionSense")))
                    return dir.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate the Dr4co Fashion Sense pack.");
        }
    }
}
