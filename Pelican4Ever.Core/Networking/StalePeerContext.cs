using System.Collections;

namespace Pelican4Ever.Core.Networking;

/// <summary>Removes metadata left behind by an offline connection before a new handshake.</summary>
public static class StalePeerContext
{
    public static bool Remove(IDictionary peers, long playerId, bool isOnline)
    {
        if (isOnline || !peers.Contains(playerId))
            return false;

        peers.Remove(playerId);
        return true;
    }
}
