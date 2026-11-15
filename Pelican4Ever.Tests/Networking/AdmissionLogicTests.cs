using System;
using Pelican4Ever.Core.Networking;
using Xunit;

namespace Pelican4Ever.Tests.Networking
{
    public class AdmissionLogicTests
    {
        private static readonly DateTimeOffset T0 = new(2026, 11, 15, 18, 0, 0, TimeSpan.Zero);

        [Fact]
        public void TryRequestAdmission_AdmitsUpToMax_ThenQueuesInFifoOrder()
        {
            var logic = new AdmissionLogic(maxConcurrentPlayers: 2, queueCapacity: 2, reconnectGraceMinutes: 5);

            Assert.Equal(AdmissionOutcome.Admitted, logic.TryRequestAdmission("c1", "fp1", T0).Outcome);
            Assert.Equal(AdmissionOutcome.Admitted, logic.TryRequestAdmission("c2", "fp2", T0).Outcome);

            var q1 = logic.TryRequestAdmission("c3", "fp3", T0);
            Assert.Equal(AdmissionOutcome.Queued, q1.Outcome);
            Assert.Equal(1, q1.QueuePosition);

            var q2 = logic.TryRequestAdmission("c4", "fp4", T0);
            Assert.Equal(AdmissionOutcome.Queued, q2.Outcome);
            Assert.Equal(2, q2.QueuePosition);

            var full = logic.TryRequestAdmission("c5", "fp5", T0);
            Assert.Equal(AdmissionOutcome.Rejected, full.Outcome);
            Assert.Equal(AdmissionRejectReason.QueueFull, full.RejectReason);
        }

        [Fact]
        public void OnDisconnected_VoluntaryExit_PromotesOldestQueuedImmediately()
        {
            var logic = new AdmissionLogic(maxConcurrentPlayers: 2, queueCapacity: 4, reconnectGraceMinutes: 5);
            logic.TryRequestAdmission("c1", "fp1", T0);
            logic.TryRequestAdmission("c2", "fp2", T0);
            logic.TryRequestAdmission("c3", "fp3", T0);
            logic.TryRequestAdmission("c4", "fp4", T0);

            var promoted = logic.OnDisconnected("c1", DisconnectClassification.VoluntaryExit, T0.AddSeconds(10));
            Assert.Single(promoted);
            Assert.Equal("c3", promoted[0].ConnectionId);
            Assert.Equal(2, logic.ActiveCount);
            Assert.Equal(1, logic.QueueCount);

            var snapshot = logic.GetQueueSnapshot();
            Assert.Single(snapshot);
            Assert.Equal("c4", snapshot[0].ConnectionId);
            Assert.Equal(1, snapshot[0].Position);
        }

        [Fact]
        public void OnDisconnected_QueuedClientLeaving_RemovesFromQueue()
        {
            var logic = new AdmissionLogic(maxConcurrentPlayers: 1, queueCapacity: 3, reconnectGraceMinutes: 5);
            logic.TryRequestAdmission("c1", "fp1", T0);
            logic.TryRequestAdmission("c2", "fp2", T0);
            logic.TryRequestAdmission("c3", "fp3", T0);

            var promoted = logic.OnDisconnected("c2", DisconnectClassification.VoluntaryExit, T0.AddSeconds(5));
            Assert.Empty(promoted);
            var snapshot = logic.GetQueueSnapshot();
            Assert.Single(snapshot);
            Assert.Equal("c3", snapshot[0].ConnectionId);
            Assert.Equal(1, snapshot[0].Position);
        }

        [Fact]
        public void NetworkDrop_ReservesSlotForGracePeriod_AndReclaimsWithoutQueue()
        {
            var logic = new AdmissionLogic(maxConcurrentPlayers: 2, queueCapacity: 2, reconnectGraceMinutes: 5);
            logic.TryRequestAdmission("c1", "fp1", T0);
            logic.TryRequestAdmission("c2", "fp2", T0);
            logic.TryRequestAdmission("c3", "fp3", T0); // queued pos 1

            // c1 suffers a network drop -> slot reserved, c3 NOT promoted yet
            var promoted = logic.OnDisconnected("c1", DisconnectClassification.NetworkDrop, T0.AddMinutes(1));
            Assert.Empty(promoted);
            Assert.Equal(2, logic.GetOccupiedSlotsCount(T0.AddMinutes(2)));
            Assert.Single(logic.GetReservationsSnapshot(T0.AddMinutes(2)));

            // fp1 reconnects at +3 min (within 5 min grace) -> admitted directly!
            var reconnect = logic.TryRequestAdmission("c1_new", "fp1", T0.AddMinutes(4));
            Assert.Equal(AdmissionOutcome.Admitted, reconnect.Outcome);
            Assert.True(reconnect.UsedReconnectReservation);
            Assert.Empty(logic.GetReservationsSnapshot(T0.AddMinutes(4)));
            Assert.Equal(1, logic.QueueCount); // c3 still queued
        }

        [Fact]
        public void NetworkDrop_ExpiredReservation_PromotesQueuedPlayer()
        {
            var logic = new AdmissionLogic(maxConcurrentPlayers: 1, queueCapacity: 2, reconnectGraceMinutes: 5);
            logic.TryRequestAdmission("c1", "fp1", T0);
            logic.TryRequestAdmission("c2", "fp2", T0);

            logic.OnDisconnected("c1", DisconnectClassification.NetworkDrop, T0);
            Assert.Empty(logic.PromoteEligible(T0.AddMinutes(4)));

            // After 5 minutes, reservation expires and c2 is promoted
            var promoted = logic.PromoteEligible(T0.AddMinutes(5));
            Assert.Single(promoted);
            Assert.Equal("c2", promoted[0].ConnectionId);

            // fp1 returning after expiry is now queued/rejected because c2 took the slot
            var late = logic.TryRequestAdmission("c1_late", "fp1", T0.AddMinutes(6));
            Assert.Equal(AdmissionOutcome.Queued, late.Outcome);
        }

        [Theory]
        [InlineData(DisconnectClassification.VoluntaryExit)]
        [InlineData(DisconnectClassification.IdleKick)]
        [InlineData(DisconnectClassification.AdminKick)]
        [InlineData(DisconnectClassification.DayRollover)]
        public void NonDropDisconnects_DoNotReserveSlot(DisconnectClassification classification)
        {
            var logic = new AdmissionLogic(maxConcurrentPlayers: 1, queueCapacity: 2, reconnectGraceMinutes: 5);
            logic.TryRequestAdmission("c1", "fp1", T0);
            logic.TryRequestAdmission("c2", "fp2", T0);

            var promoted = logic.OnDisconnected("c1", classification, T0.AddSeconds(10));
            Assert.Single(promoted);
            Assert.Equal("c2", promoted[0].ConnectionId);
            Assert.Empty(logic.GetReservationsSnapshot(T0.AddSeconds(10)));
        }
    }
}
