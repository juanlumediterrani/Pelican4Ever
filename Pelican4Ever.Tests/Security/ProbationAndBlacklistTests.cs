using System;
using System.Collections.Generic;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.Security;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests.Security
{
    public class ProbationAndBlacklistTests
    {
        private static readonly DateTimeOffset T0 = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

        private static UnpairedHandshakeObservation MakeObs(string name, DeviceKeyPair key, DateTimeOffset atUtc, string ip = "198.51.100.10")
        {
            return new UnpairedHandshakeObservation
            {
                Fingerprint = key.GetFingerprint(),
                PublicKeyBase64 = Convert.ToBase64String(key.ExportPublicKeySpki()),
                RemoteIp = ip,
                ObservedAtUtc = atUtc
            };
        }

        [Fact]
        public void OpenAndApprove_EnforcesMaxTwoConcurrentSlots_AndGraduationFreesSlot()
        {
            var state = new ServerWorldState();
            using var k1 = DeviceKeyPair.Generate();
            using var k2 = DeviceKeyPair.Generate();
            using var k3 = DeviceKeyPair.Generate();

            Assert.True(ProbationAndBlacklistLogic.OpenTicket(state, "111111111111111111", "AldeanoUno", T0, out _, out _));
            Assert.True(ProbationAndBlacklistLogic.OpenTicket(state, "222222222222222222", "AldeanoDos", T0.AddSeconds(10), out _, out _));
            Assert.Equal(2, ProbationAndBlacklistLogic.CountPutreActiveSlots(state, T0.AddSeconds(20)));

            // El tercer candidato queda bloqueado mientras haya 2 cupos ocupados (tickets u observación)
            Assert.False(ProbationAndBlacklistLogic.OpenTicket(state, "333333333333333333", "AldeanoTres", T0.AddSeconds(30), out _, out string capMsg));
            Assert.Contains("cupo", capMsg, StringComparison.OrdinalIgnoreCase);

            // Aprobar el primero usando su prefijo corto de 12 hex caracteres
            var obs = new[]
            {
                MakeObs("AldeanoUno", k1, T0.AddMinutes(1)),
                MakeObs("AldeanoDos", k2, T0.AddMinutes(1)),
                MakeObs("AldeanoTres", k3, T0.AddMinutes(1))
            };
            string short1 = k1.GetFingerprint()[..12];
            Assert.True(ProbationAndBlacklistLogic.ApproveTicketDevice(state, "111111111111111111", short1, obs, T0.AddMinutes(2), out var d1, out _));
            Assert.NotNull(d1);
            Assert.Equal("putre", d1!.InvitedBy);
            Assert.Equal(ProbationStage.Observing, d1.ProbationStage);
            Assert.Equal("111111111111111111", d1.DiscordUserId);
            Assert.True(ServerDeviceRegistry.IsDeviceAuthorized(state, d1.Fingerprint, out _));

            // 1 observando + 1 ticket => sigue en 2 cupos
            Assert.Equal(2, ProbationAndBlacklistLogic.CountPutreActiveSlots(state, T0.AddMinutes(3)));
            Assert.False(ProbationAndBlacklistLogic.OpenTicket(state, "333333333333333333", "AldeanoTres", T0.AddMinutes(3), out _, out _));

            // Graduar el primero a los 10 min libera 1 cupo (el ticket 2 sigue vigente hasta los 30 min)
            Assert.True(ProbationAndBlacklistLogic.GraduateDevice(state, d1.Alias, T0.AddMinutes(10), out var graduated, out _));
            Assert.Equal(ProbationStage.Graduated, graduated!.ProbationStage);
            Assert.Equal(1, ProbationAndBlacklistLogic.CountPutreActiveSlots(state, T0.AddMinutes(10)));
            Assert.True(ProbationAndBlacklistLogic.OpenTicket(state, "333333333333333333", "AldeanoTres", T0.AddMinutes(10), out _, out _));
        }

        [Fact]
        public void Blacklist_BlocksTicketReopenAndDeviceAuth_AndOnlyRemoveFromBlacklistFreesIt()
        {
            var state = new ServerWorldState();
            using var key = DeviceKeyPair.Generate();
            string fp = key.GetFingerprint();

            Assert.True(ProbationAndBlacklistLogic.OpenTicket(state, "444444444444444444", "Trolero", T0, out _, out _));
            var obs = new[] { MakeObs("Trolero", key, T0.AddMinutes(1), "203.0.113.55") };
            Assert.True(ProbationAndBlacklistLogic.ApproveTicketDevice(state, "444444444444444444", fp[..12], obs, T0.AddMinutes(2), out var dev, out _));
            dev!.BoundPlayerId = 9001;
            var rec = state.GetOrCreatePlayer(9001, "Trolero");
            rec.OwnerToken = fp;

            // Putre revoca -> entra directamente a lista negra y libera su cupo
            Assert.True(ProbationAndBlacklistLogic.AddToBlacklist(state, "444444444444444444", "Flood + VisitorGuard", "putre", T0.AddMinutes(10), out var entry, out var affectedIds, out _));
            Assert.NotNull(entry);
            Assert.Contains(9001L, affectedIds);
            Assert.Equal(DeviceStatus.Blacklisted, state.AuthorizedDevices[fp].Status);
            Assert.Equal(ProbationStage.Blacklisted, state.AuthorizedDevices[fp].ProbationStage);
            Assert.False(ServerDeviceRegistry.IsDeviceAuthorized(state, fp, out _));
            Assert.Equal(0, ProbationAndBlacklistLogic.CountPutreActiveSlots(state, T0.AddMinutes(11)));
            Assert.True(string.IsNullOrEmpty(rec.OwnerToken));

            // No puede volver a pedir ticket ni con otro alias si coincide DiscordUserId
            Assert.False(ProbationAndBlacklistLogic.OpenTicket(state, "444444444444444444", "OtroAlias", T0.AddMinutes(12), out _, out string blockMsg));
            Assert.Contains("lista negra", blockMsg, StringComparison.OrdinalIgnoreCase);
            Assert.True(ProbationAndBlacklistLogic.IsBlacklisted(state, fp, null, out _));

            // Solo RemoveFromBlacklist (propietario) lo retira; el dispositivo queda Revoked (requiere nueva invitación)
            Assert.True(ProbationAndBlacklistLogic.RemoveFromBlacklist(state, fp[..12], out _, out _));
            Assert.False(ProbationAndBlacklistLogic.IsBlacklisted(state, fp, "444444444444444444", out _));
            Assert.False(ServerDeviceRegistry.IsDeviceAuthorized(state, fp, out _));
            Assert.True(ProbationAndBlacklistLogic.OpenTicket(state, "444444444444444444", "OtroAlias", T0.AddMinutes(15), out _, out _));
        }

        [Fact]
        public void ExpiredTicketsAndCancel_FreeSlotsAutomatically()
        {
            var state = new ServerWorldState();
            Assert.True(ProbationAndBlacklistLogic.OpenTicket(state, "555555555555555555", "Uno", T0, out _, out _));
            Assert.True(ProbationAndBlacklistLogic.OpenTicket(state, "666666666666666666", "Dos", T0, out _, out _));
            Assert.Equal(2, ProbationAndBlacklistLogic.CountPutreActiveSlots(state, T0.AddMinutes(1)));

            Assert.True(ProbationAndBlacklistLogic.CancelTicket(state, "Uno", out _));
            Assert.Equal(1, ProbationAndBlacklistLogic.CountPutreActiveSlots(state, T0.AddMinutes(2)));

            // Tras 31 min expira el segundo ticket automáticamente
            Assert.Equal(0, ProbationAndBlacklistLogic.CountPutreActiveSlots(state, T0.AddMinutes(31)));
            Assert.True(ProbationAndBlacklistLogic.OpenTicket(state, "777777777777777777", "Tres", T0.AddMinutes(31), out _, out _));
        }

        [Fact]
        public void TagInjectionDetector_FlagsTagsAndMassMentions_WhileAllowingVanillaEmojis()
        {
            Assert.False(ProbationAndBlacklistLogic.IsSuspiciousChatTagInjection("Hola vecinos [12] vamos a minar"));
            Assert.True(ProbationAndBlacklistLogic.IsSuspiciousChatTagInjection("[color red]soy admin[/color]"));
            Assert.True(ProbationAndBlacklistLogic.IsSuspiciousChatTagInjection("[#ff0000]alerta"));
            Assert.True(ProbationAndBlacklistLogic.IsSuspiciousChatTagInjection("ey @everyone entrar ya"));
            Assert.True(ProbationAndBlacklistLogic.IsSuspiciousChatTagInjection("[Sistema] servidor reiniciando"));
            Assert.True(ProbationAndBlacklistLogic.IsSuspiciousChatTagInjection("texto\u001b[31mrojo"));
        }

        [Fact]
        public void RemoteCommandsAndHostApiCatalog_RegisterProbationAndOwnerOnlyRemove()
        {
            Assert.NotNull(RemoteCommandCatalog.Find("p4e_probation_ticket"));
            Assert.NotNull(RemoteCommandCatalog.Find("p4e_probation_approve"));
            Assert.NotNull(RemoteCommandCatalog.Find("p4e_probation_graduate"));
            Assert.NotNull(RemoteCommandCatalog.Find("p4e_probation_cancel"));
            Assert.NotNull(RemoteCommandCatalog.Find("p4e_probation_blacklist"));

            var removeSpec = RemoteCommandCatalog.Find("p4e_blacklist_remove");
            Assert.NotNull(removeSpec);
            Assert.Equal(RemoteCommandRisk.Sensitive, removeSpec!.Risk);

            Assert.Contains(HostApiCatalog.Routes, r => r.Method == "GET" && r.Path == "/api/v1/host/probation");
            Assert.Equal("player.blacklisted", HostEventTypes.PlayerBlacklisted);
            Assert.Equal("player.blacklist.removed", HostEventTypes.PlayerBlacklistRemoved);
            Assert.Equal("player.probation.graduated", HostEventTypes.PlayerProbationGraduated);
        }
    }
}
