using System.Collections;

namespace Pelican4Ever.Core.Networking;

/// <summary>Removes metadata left behind by an offline or disconnecting connection before a new handshake.</summary>
public static class StalePeerContext
{
    public static bool IsActivelyOnline(bool inOnlineFarmers, bool isDisconnecting, bool hasActiveTransportConnection)
    {
        return inOnlineFarmers && !isDisconnecting && hasActiveTransportConnection;
    }

    public static bool Remove(IDictionary peers, long playerId, bool isOnline)
    {
        if (isOnline || !peers.Contains(playerId))
            return false;

        peers.Remove(playerId);
        return true;
    }

    public static bool RemoveIfStale(
        IDictionary peers,
        long playerId,
        bool inOnlineFarmers,
        bool isDisconnecting,
        bool hasActiveTransportConnection)
    {
        return Remove(peers, playerId, IsActivelyOnline(inOnlineFarmers, isDisconnecting, hasActiveTransportConnection));
    }
}
