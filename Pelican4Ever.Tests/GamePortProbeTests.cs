using System;
using System.Net;
using System.Net.Sockets;
using Pelican4Ever.Core.Server;
using Xunit;

namespace Pelican4Ever.Tests;

public class GamePortProbeTests
{
    /// <summary>Puerto de prueba: nunca es el 24642 que el juego fija para alojar.</summary>
    private const int TestPort = 38642;

    [Fact]
    public void IsAvailable_FreePort_ReturnsTrue() =>
        Assert.True(GamePortProbe.IsAvailable(TestPort));

    [Fact]
    public void IsAvailable_BoundPort_ReturnsFalse()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, TestPort));

        Assert.False(GamePortProbe.IsAvailable(TestPort));
    }

    [Fact]
    public void FindOccupant_BoundPort_ReportsThisProcess()
    {
        if (!OperatingSystem.IsLinux())
            return; // /proc solo existe en Linux

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, TestPort));

        PortOccupant? occupant = GamePortProbe.FindOccupant(TestPort);

        Assert.NotNull(occupant);
        Assert.Equal(Environment.ProcessId, occupant!.Value.Pid);
    }
}
