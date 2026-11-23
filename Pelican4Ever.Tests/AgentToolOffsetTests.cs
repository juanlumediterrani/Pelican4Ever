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
        public void GetForPlayer_HandlesNativeSkinsAndLegacyFashionSense()
        {
            Assert.Equal((0, 0), AgentToolOffset.GetForPlayer("putre", null, 2));
            Assert.Equal((-4, 12), AgentToolOffset.GetForPlayer("dr4co", null, 1));
            Assert.Equal((4, 12), AgentToolOffset.GetForPlayer("dr4co", null, 3));
            Assert.Equal((-8, 16), AgentToolOffset.GetForPlayer(null, AgentToolOffset.Dr4coBodyId, 1));
        }
    }
}
