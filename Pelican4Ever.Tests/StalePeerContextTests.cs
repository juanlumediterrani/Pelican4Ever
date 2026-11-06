using System.Collections.Generic;
using Pelican4Ever.Core.Networking;
using Xunit;

namespace Pelican4Ever.Tests;

public class StalePeerContextTests
{
    [Fact]
    public void ReconnectionRemovesOnlyOfflinePlayersOldContext()
    {
        var other = new object();
        var peers = new Dictionary<long, object> { [10] = new(), [20] = other };
        Assert.True(StalePeerContext.Remove(peers, 10, isOnline: false));
        Assert.False(peers.ContainsKey(10));
        Assert.Same(other, peers[20]);
        // The normal handshake can now register the fresh context.
        var fresh = new object();
        peers.Add(10, fresh);
        Assert.Same(fresh, peers[10]);
    }

    [Fact]
    public void DuplicateContextDoesNotReplaceOnlinePlayersMetadata()
    {
        var original = new object();
        var peers = new Dictionary<long, object> { [10] = original };
        Assert.False(StalePeerContext.Remove(peers, 10, isOnline: true));
        Assert.Same(original, peers[10]);
    }

    [Fact]
    public void FirstConnectionNeedsNoRepair()
    {
        var peers = new Dictionary<long, object>();
        Assert.False(StalePeerContext.Remove(peers, 10, isOnline: false));
        Assert.Empty(peers);
    }
}
