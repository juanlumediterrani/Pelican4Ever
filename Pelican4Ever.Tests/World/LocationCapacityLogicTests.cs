using System;
using System.Collections.Generic;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests.World;

public class LocationCapacityLogicTests
{
    [Fact]
    public void DisabledFeature_AllowsEntryWithoutReservingSlots()
    {
        var logic = new LocationCapacityLogic();
        logic.Configure(extendedConcurrency: false, defaultLocationCapacity: 2);

        var decision = logic.TryRequestEntry(101L, "Town", attemptId: 1, currentLiveOccupantsInTarget: 20, DateTimeOffset.UtcNow);

        Assert.True(decision.Allowed);
        Assert.Equal(LocationEntryExemption.FeatureDisabled, decision.Exemption);
        Assert.False(decision.ReservedSlot);
    }

    [Fact]
    public void VoluntaryEntry_ReservesSlotsUpToCapacity_AndRejectsExcess_UsingOverrides()
    {
        var logic = new LocationCapacityLogic();
        logic.Configure(
            extendedConcurrency: true,
            defaultLocationCapacity: 16,
            overrides: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Saloon"] = 2
            },
            reservationTtl: TimeSpan.FromSeconds(8));

        var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

        // 1 live occupant in Saloon + 1 reservation reaches capacity 2.
        var d1 = logic.TryRequestEntry(101L, "Saloon", attemptId: 1, currentLiveOccupantsInTarget: 1, now, LocationEntryOrigin.Voluntary, "Town");
        Assert.True(d1.Allowed);
        Assert.True(d1.ReservedSlot);
        Assert.Equal(2, d1.OccupiedCount);
        Assert.Equal(1, d1.ActiveReservations);

        // Idempotent retry for same (player, attemptId) returns the same granted reservation cleanly.
        var d1Retry = logic.TryRequestEntry(101L, "Saloon", attemptId: 1, currentLiveOccupantsInTarget: 1, now.AddMilliseconds(200), LocationEntryOrigin.Voluntary, "Town");
        Assert.True(d1Retry.Allowed);
        Assert.True(d1Retry.ReservedSlot);
        Assert.Equal(1, d1Retry.ActiveReservations);

        // Second visitor while 101's reservation is still pending gets rejected pre-fade.
        var d2 = logic.TryRequestEntry(102L, "Saloon", attemptId: 2, currentLiveOccupantsInTarget: 1, now.AddSeconds(1), LocationEntryOrigin.Voluntary, "Town");
        Assert.False(d2.Allowed);
        Assert.False(d2.ReservedSlot);
        Assert.Equal(2, d2.OccupiedCount);
        Assert.Equal(2, d2.Capacity);

        // Single arrival consumption frees the pending reservation once live occupants already reflects arrival (or left).
        Assert.True(logic.ConfirmArrival(101L, "Saloon", attemptId: 1, now.AddSeconds(2)));
        Assert.False(logic.ConfirmArrival(101L, "Saloon", attemptId: 1, now.AddSeconds(2)));

        // Cancel & disconnect also release slots, and TTL expiry frees stalled attempts.
        var d3 = logic.TryRequestEntry(103L, "Saloon", attemptId: 3, currentLiveOccupantsInTarget: 1, now.AddSeconds(3), LocationEntryOrigin.Voluntary, "Town");
        Assert.True(d3.Allowed);
        Assert.True(logic.CancelReservation(103L, 3));

        var d4 = logic.TryRequestEntry(104L, "Saloon", attemptId: 4, currentLiveOccupantsInTarget: 1, now.AddSeconds(4), LocationEntryOrigin.Voluntary, "Town");
        Assert.True(d4.Allowed);
        // Still reserved at +10s (<4+8s) so 105 fails
        var d5Early = logic.TryRequestEntry(105L, "Saloon", attemptId: 5, currentLiveOccupantsInTarget: 1, now.AddSeconds(10), LocationEntryOrigin.Voluntary, "Town");
        Assert.False(d5Early.Allowed);
        // Expired at +12s (>=4+8s) so 105 succeeds
        var d5AfterTtl = logic.TryRequestEntry(105L, "Saloon", attemptId: 5, currentLiveOccupantsInTarget: 1, now.AddSeconds(12), LocationEntryOrigin.Voluntary, "Town");
        Assert.True(d5AfterTtl.Allowed);
    }

    [Fact]
    public void Exemptions_AllowForcedTransitions_OwnResidences_AndRedirectAcrossFullBridgeToOwnHome()
    {
        string bridge1 = HostPlazaLayout.BridgeName(1);
        var logic = new LocationCapacityLogic();
        logic.Configure(
            extendedConcurrency: true,
            defaultLocationCapacity: 2,
            overrides: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Pelican4Ever_Farm_05"] = 1,
                [bridge1] = 1
            });

        var now = DateTimeOffset.UtcNow;

        // 1. Forced system transitions bypass full location caps.
        foreach (var origin in new[]
        {
            LocationEntryOrigin.Passout,
            LocationEntryOrigin.DayRolloverWake,
            LocationEntryOrigin.ResidenceEviction,
            LocationEntryOrigin.EventOrFestivalEnd,
            LocationEntryOrigin.AdminTeleport
        })
        {
            var forced = logic.TryRequestEntry(201L, "Town", attemptId: 10, currentLiveOccupantsInTarget: 16, now, origin, "Hospital");
            Assert.True(forced.Allowed);
            Assert.Equal(LocationEntryExemption.ForcedSystemTransition, forced.Exemption);
            Assert.False(forced.ReservedSlot);
        }

        // 2. Owner/tenant always allowed into own residence and its annexes even when cap reached by visitors.
        var ownRes = logic.TryRequestEntry(
            202L,
            "Pelican4Ever_Annex_05_North",
            attemptId: 11,
            currentLiveOccupantsInTarget: 5,
            now,
            LocationEntryOrigin.Voluntary,
            currentLocationName: bridge1,
            playerResidenceSlotId: "Pelican4Ever_Farm_05",
            playerAnnexIds: new[] { "Pelican4Ever_Annex_05_North" });
        Assert.True(ownRes.Allowed);
        Assert.Equal(LocationEntryExemption.OwnResidenceOrDependency, ownRes.Exemption);
        Assert.False(ownRes.ReservedSlot);

        // 3. Returning resident crossing a full transit bridge toward own slot is redirected home.
        var bridgeRedirect = logic.TryRequestEntry(
            203L,
            bridge1,
            attemptId: 12,
            currentLiveOccupantsInTarget: 5,
            now,
            LocationEntryOrigin.Voluntary,
            currentLocationName: "Farm",
            playerResidenceSlotId: "Pelican4Ever_Farm_05",
            farmSlotCount: 16);
        Assert.True(bridgeRedirect.Allowed);
        Assert.Equal(LocationEntryExemption.TransitBridgeToOwnResidence, bridgeRedirect.Exemption);
        Assert.Equal("Pelican4Ever_Farm_05", bridgeRedirect.EffectiveTargetLocationName);
        Assert.False(bridgeRedirect.ReservedSlot);

        // 4. Leaving own residence toward a full bridge is NOT treated as returning home -> denied cleanly.
        var bridgeExitDenied = logic.TryRequestEntry(
            203L,
            bridge1,
            attemptId: 13,
            currentLiveOccupantsInTarget: 5,
            now,
            LocationEntryOrigin.Voluntary,
            currentLocationName: "Pelican4Ever_Farm_05",
            playerResidenceSlotId: "Pelican4Ever_Farm_05",
            farmSlotCount: 16);
        Assert.False(bridgeExitDenied.Allowed);
    }
}
