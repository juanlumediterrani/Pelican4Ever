using System;
using System.Collections.Generic;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests.World;

public class ResidenceInterestReplicationTests
{
    [Fact]
    public void TryResolveResidenceSlotId_ResolvesFarmSubLocationsAndAnnexes_IgnoresPublicMaps()
    {
        Assert.Equal("Pelican4Ever_Farm_05", LocationCullingLogic.TryResolveResidenceSlotId("Pelican4Ever_Farm_05"));
        Assert.Equal("Pelican4Ever_Farm_05", LocationCullingLogic.TryResolveResidenceSlotId("Pelican4Ever_Farm_05_Cave"));
        Assert.Equal("Pelican4Ever_Farm_05", LocationCullingLogic.TryResolveResidenceSlotId("Pelican4Ever_Farm_05_Home"));
        Assert.Equal("Pelican4Ever_Farm_05", LocationCullingLogic.TryResolveResidenceSlotId("Barn42", rootOrParentLocationName: "Pelican4Ever_Farm_05"));
        Assert.Equal("Pelican4Ever_Farm_05", LocationCullingLogic.TryResolveResidenceSlotId("Pelican4Ever_Annex_05_North"));

        Assert.Null(LocationCullingLogic.TryResolveResidenceSlotId("Town"));
        Assert.Null(LocationCullingLogic.TryResolveResidenceSlotId(HostPlazaLayout.BridgeName(1)));
        Assert.False(LocationCullingLogic.IsPrivateResidenceOrDependent("Town"));
        Assert.True(LocationCullingLogic.IsPrivateResidenceOrDependent("Pelican4Ever_Farm_12_Greenhouse"));
    }

    [Fact]
    public void FilterInterestedPeers_IncludesOwnerTenantOccupantsAndAuthorizedEntrantsOnly()
    {
        long owner = 1001L;
        long tenant = 1002L;
        long occupant = 1003L;
        long reservedVisitor = 1004L;
        long uninterested1 = 1005L;
        long uninterested2 = 1006L;

        var candidates = new[] { owner, tenant, occupant, reservedVisitor, uninterested1, uninterested2 };
        var occupants = new HashSet<long> { occupant };
        var reserved = new HashSet<long> { reservedVisitor };

        // Host broadcast (no excluded sender)
        var recipients = LocationCullingLogic.FilterInterestedPeers(
            candidates,
            excludeSenderPeerId: null,
            ownerId: owner,
            tenantId: tenant,
            occupantsInRootTree: occupants,
            peersWithAuthorizedEntryToRootTree: reserved);

        Assert.Equal(new[] { owner, tenant, occupant, reservedVisitor }, recipients);

        // Client delta rebroadcast from occupant excludes sender (occupant) and uninterested peers
        var rebroadcastRecipients = LocationCullingLogic.FilterInterestedPeers(
            candidates,
            excludeSenderPeerId: occupant,
            ownerId: owner,
            tenantId: tenant,
            occupantsInRootTree: occupants,
            peersWithAuthorizedEntryToRootTree: reserved);

        Assert.Equal(new[] { owner, tenant, reservedVisitor }, rebroadcastRecipients);
    }

    [Fact]
    public void ResidenceRootSyncTracker_RequiresFullSnapshotBeforeDeltasWhenStaleOrNewlyInterested()
    {
        var tracker = new ResidenceRootSyncTracker();
        const string root = "Pelican4Ever_Farm_03";
        long owner = 1L;
        long visitor = 2L;

        // Before initial full snapshot, both need full snapshot
        Assert.True(tracker.NeedsFullSnapshotBeforeDeltas(owner, root));
        Assert.True(tracker.NeedsFullSnapshotBeforeDeltas(visitor, root));

        // Owner gets full snapshot; visitor does not
        tracker.RecordFullSync(owner, root);
        Assert.False(tracker.NeedsFullSnapshotBeforeDeltas(owner, root));
        Assert.True(tracker.NeedsFullSnapshotBeforeDeltas(visitor, root));

        // Deltas go out while visitor is away
        tracker.RecordDeltaBroadcast(root, new[] { owner });
        Assert.False(tracker.NeedsFullSnapshotBeforeDeltas(owner, root));
        Assert.True(tracker.NeedsFullSnapshotBeforeDeltas(visitor, root));

        // Visitor enters -> receives full sync -> no longer needs full snapshot for subsequent deltas
        tracker.RecordFullSync(visitor, root);
        Assert.False(tracker.NeedsFullSnapshotBeforeDeltas(visitor, root));
        tracker.RecordDeltaBroadcast(root, new[] { owner, visitor });
        Assert.False(tracker.NeedsFullSnapshotBeforeDeltas(visitor, root));

        // Visitor leaves and loses interest -> invalidated -> later return forces full catchup snapshot
        tracker.InvalidateRootForAbsentPeers(root, new[] { owner });
        Assert.False(tracker.NeedsFullSnapshotBeforeDeltas(owner, root));
        Assert.True(tracker.NeedsFullSnapshotBeforeDeltas(visitor, root));

        tracker.OnPeerDisconnected(owner);
        Assert.True(tracker.NeedsFullSnapshotBeforeDeltas(owner, root));
    }
}
