using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Config;
using Xunit;

namespace Pelican4Ever.Tests;

public class HeadlessRenderTests
{
    private static HostRenderReason Decide(
        bool enabled = true, bool unattended = true, bool worldReady = true, bool saveMenu = false, bool lease = false) =>
        HostRenderPolicy.Decide(enabled, unattended, worldReady, saveMenu, lease);

    [Fact]
    public void UnattendedServerWithLoadedWorld_DoesNotDraw()
    {
        Assert.Equal(HostRenderReason.Suppressed, Decide());
        Assert.False(HostRenderPolicy.ShouldDraw(Decide()));
    }

    [Fact]
    public void DisabledOption_AlwaysDraws()
    {
        Assert.Equal(HostRenderReason.Disabled, Decide(enabled: false));
        Assert.True(HostRenderPolicy.ShouldDraw(Decide(enabled: false)));
    }

    [Fact]
    public void HumanHost_WithoutAutoHost_AlwaysDraws()
    {
        // Sin AutoHostSaveName hay una persona mirando la pantalla del host: nunca se deja de dibujar.
        Assert.Equal(HostRenderReason.Attended, Decide(unattended: false));
    }

    [Fact]
    public void TitleAndLoading_Draw()
    {
        Assert.Equal(HostRenderReason.WorldNotReady, Decide(worldReady: false));
    }

    [Fact]
    public void SaveGameMenu_ForcesDrawing_EvenWithoutLease()
    {
        // SaveGameMenu.update no empieza a guardar hasta que draw() pone hasDrawn: sin esto el guardado nocturno no arranca.
        Assert.Equal(HostRenderReason.DrawRequiredMenu, Decide(saveMenu: true));
        Assert.True(HostRenderPolicy.ShouldDraw(Decide(saveMenu: true)));
    }

    [Fact]
    public void ActiveLease_Draws()
    {
        Assert.Equal(HostRenderReason.Lease, Decide(lease: true));
    }

    [Fact]
    public void Lease_IsRenewedPerRequester_AndExpires()
    {
        var registry = new HostRenderLeaseRegistry();
        var now = new DateTime(2026, 11, 9, 4, 0, 0, DateTimeKind.Utc);

        registry.Grant("voyager", 10, now);
        registry.Grant("voyager", 30, now.AddMinutes(5));
        registry.Grant("putre", 5, now);

        var active = registry.Active(now.AddMinutes(6));
        Assert.Single(active);                                   // el de putre caducó a los 5 min
        Assert.Equal("voyager", active[0].Requester);
        Assert.Equal(now.AddMinutes(35), active[0].ExpiresAtUtc); // renovado, no acumulado

        Assert.False(registry.HasActive(now.AddMinutes(36)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(500, HostRenderLeaseRegistry.MaxMinutes)]
    public void Lease_DurationIsClamped(int requested, int expected)
    {
        var registry = new HostRenderLeaseRegistry();
        var now = new DateTime(2026, 11, 9, 4, 0, 0, DateTimeKind.Utc);

        HostRenderLease lease = registry.Grant("api", requested, now);

        Assert.Equal(now.AddMinutes(expected), lease.ExpiresAtUtc);
    }

    [Fact]
    public void Lease_Release_RemovesOnlyThatRequester()
    {
        var registry = new HostRenderLeaseRegistry();
        var now = DateTime.UtcNow;
        registry.Grant("voyager", 10, now);
        registry.Grant("putre", 10, now);

        Assert.True(registry.Release("VOYAGER"));                // sin distinguir mayúsculas
        Assert.False(registry.Release("voyager"));
        Assert.Equal("putre", Assert.Single(registry.Active(now)).Requester);
    }

    [Theory]
    [InlineData("")]
    [InlineData("con espacio")]
    [InlineData("../etc")]
    [InlineData("a-very-long-requester-name-over-forty-characters")]
    public void Lease_RejectsInvalidRequesters(string requester)
    {
        Assert.False(HostRenderLeaseRegistry.IsValidRequester(requester));
        Assert.Throws<ArgumentException>(() => new HostRenderLeaseRegistry().Grant(requester, 5, DateTime.UtcNow));
    }

    [Fact]
    public void Config_HeadlessRenderingIsOnByDefault_AndValid()
    {
        var config = new ServerConfig();

        Assert.True(config.HeadlessRendering);
        Assert.Empty(config.Validate());
    }
}
