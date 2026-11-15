using Pelican4Ever.Core.Economy;
using Xunit;

namespace Pelican4Ever.Tests;

public class SkillCheckpointBarrierTests
{
    [Fact]
    public void RequiresEveryAcknowledgementAndMatchingReplicatedExperience()
    {
        var acks = new Dictionary<long, int[]?> { [1] = null, [2] = new[] { 0, 301, 0, 0, 0 } };
        var host = new Dictionary<long, int[]> { [1] = new[] { 1, 0, 0, 0, 0 }, [2] = new[] { 0, 300, 0, 0, 0 } };
        Assert.False(SkillCheckpointBarrier.IsReady(acks, host));
        acks[1] = new[] { 1, 0, 0, 0, 0 };
        Assert.False(SkillCheckpointBarrier.IsReady(acks, host));
        host[2][1] = 301;
        Assert.True(SkillCheckpointBarrier.IsReady(acks, host));
        host.Remove(1);
        Assert.False(SkillCheckpointBarrier.IsReady(acks, host));
        host[3] = new[] { 1, 0, 0, 0, 0 };
        Assert.False(SkillCheckpointBarrier.IsReady(acks, host));
    }
}
