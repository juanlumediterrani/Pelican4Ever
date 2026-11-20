using System;
using System.Collections.Generic;
using Pelican4Ever.Core.Admin;
using Xunit;

namespace Pelican4Ever.Tests.Admin;

public class ConcurrencyInstrumentationTests
{
    [Fact]
    public void ComputePercentiles_EmptyReturnsZeros_NonEmptyComputesMedianP95MaxMean()
    {
        var empty = ConcurrencyInstrumentationLogic.ComputePercentiles(Array.Empty<double>());
        Assert.Equal(0d, empty.MedianMs);
        Assert.Equal(0d, empty.P95Ms);
        Assert.Equal(0d, empty.MaxMs);
        Assert.Equal(0, empty.SampleCount);

        var samples = new List<double>();
        for (int i = 1; i <= 100; i++)
            samples.Add(i);

        var stats = ConcurrencyInstrumentationLogic.ComputePercentiles(samples);
        Assert.Equal(100, stats.SampleCount);
        Assert.InRange(stats.MedianMs, 50.0, 51.0);
        Assert.InRange(stats.P95Ms, 94.0, 96.0);
        Assert.Equal(100d, stats.MaxMs);
        Assert.InRange(stats.MeanMs, 50.4, 50.6);
    }

    [Fact]
    public void ComputeOverheadPercent_HandlesZeroDenominatorAndNormalRatio()
    {
        Assert.Equal(0d, ConcurrencyInstrumentationLogic.ComputeOverheadPercent(1.5d, 0d));
        Assert.Equal(10d, ConcurrencyInstrumentationLogic.ComputeOverheadPercent(2d, 20d));
    }

    [Fact]
    public void GetMessageTypeName_MapsKnownStardewWireTypes()
    {
        Assert.Equal("farmerDelta", ConcurrencyInstrumentationLogic.GetMessageTypeName(0));
        Assert.Equal("locationIntroduction", ConcurrencyInstrumentationLogic.GetMessageTypeName(3));
        Assert.Equal("locationDelta", ConcurrencyInstrumentationLogic.GetMessageTypeName(6));
        Assert.Equal("forceKick", ConcurrencyInstrumentationLogic.GetMessageTypeName(23));
        Assert.Equal("type_250", ConcurrencyInstrumentationLogic.GetMessageTypeName(250));
    }

    [Fact]
    public void ConcurrencyWindowBuilder_TracksHostVsRebroadcastPerTypeAndPeerAndFilters()
    {
        var start = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var builder = new ConcurrencyWindowBuilder(start);

        builder.RecordOutgoingMessage(0, 1001L, 120, MessageOriginKind.HostGenerated);
        builder.RecordOutgoingMessage(0, 1001L, 80, MessageOriginKind.ClientRebroadcast);
        builder.RecordOutgoingMessage(6, 1002L, 500, MessageOriginKind.HostGenerated);
        builder.RecordOutgoingMessage(3, 1002L, 2048, MessageOriginKind.HostGenerated);

        builder.RecordRootTraversal(2.5d);
        builder.RecordLocationBroadcast(4.0d, serializedLocationCount: 3, serializedBytes: 1500);
        builder.RecordFilteredResidenceSend(14);
        builder.RecordTickWork(10.0d, 16.6d, 0.2d);
        builder.RecordTickWork(20.0d, 16.8d, 0.4d);
        builder.RecordJoinTiming(1001L, "Alice", 432.5d, start.AddSeconds(3));

        var snapshot = builder.BuildSnapshot(
            windowEndUtc: start.AddSeconds(10),
            extendedConcurrencyEnabled: true,
            instrumentationEnabled: true,
            connectedClientsCount: 2,
            maxConcurrentPlayers: 32,
            farmerDeltaBroadcastPeriod: 3,
            transportBytesUp: 4096,
            transportBytesDown: 1024,
            locationOccupancy: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Town"] = 2 },
            livePeers: new[]
            {
                new PeerLiveTelemetryInput(1001L, "Alice", "Town", 1, 200, 12.5, 25.0),
                new PeerLiveTelemetryInput(1002L, "Bob", "Town", 0, 0, 0, 30.0)
            },
            cpuUsagePercent: 18.5d,
            workingSetBytes: 512_000_000L,
            managedHeapBytes: 128_000_000L);

        Assert.True(snapshot.ExtendedConcurrencyEnabled);
        Assert.True(snapshot.InstrumentationEnabled);
        Assert.Equal(2, snapshot.ConnectedClientsCount);
        Assert.Equal(32, snapshot.MaxConcurrentPlayers);
        Assert.Equal(4, snapshot.PayloadTotalMessages);
        Assert.Equal(2748, snapshot.PayloadTotalBytes);
        Assert.Equal(200, snapshot.Type0FarmerDeltaBytes);
        Assert.Equal(500, snapshot.Type6LocationDeltaBytes);
        Assert.Equal(2048, snapshot.Type3LocationIntroBytes);
        Assert.Equal(1, snapshot.RootSync.UpdateRootsCalls);
        Assert.Equal(3, snapshot.RootSync.LocationsSerializedCount);
        Assert.Equal(14, snapshot.RootSync.ResidenceDeltasFilteredOutCount);
        Assert.Equal(2, snapshot.TickCount);
        Assert.Equal(15.0d, snapshot.TickMeanMs);
        Assert.Equal(2.0d, snapshot.InstrumentationOverheadPercent);
        Assert.Single(snapshot.RecentJoinTimings);
        Assert.Equal("Alice", snapshot.RecentJoinTimings[0].PlayerName);

        builder.Reset(start.AddSeconds(10));
        var afterReset = builder.BuildSnapshot(
            start.AddSeconds(20), false, false, 0, 16, 3, 0, 0);
        Assert.Equal(0, afterReset.PayloadTotalMessages);
        Assert.Equal(0, afterReset.RootSync.ResidenceDeltasFilteredOutCount);
        Assert.Empty(afterReset.RecentJoinTimings);
    }
}
