using System;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class ClientPairingLogicTests
    {
        private static readonly DateTimeOffset BaseTime = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);

        [Fact]
        public void CreateInvite_SetsThreeDayExpirationAndReplacesExistingForSameAliasOnly()
        {
            var state = new ServerWorldState();

            var invite1 = ClientPairingLogic.CreateInvite(state, "Dextrure", "83.44.12.34", TimeSpan.FromHours(72), BaseTime);
            Assert.Equal("Dextrure", invite1.Alias);
            Assert.Equal("83.44.12.34", invite1.AllowedIp);
            Assert.Equal(6, invite1.Code.Length);
            Assert.Equal(BaseTime.AddHours(72), invite1.ExpiresAtUtc);
            Assert.Single(state.PairingInvites);

            // Renovar con nueva IP para el mismo usuario reemplaza la invitación anterior
            var invite2 = ClientPairingLogic.CreateInvite(state, "dextrure", "83.44.12.99", TimeSpan.FromHours(72), BaseTime.AddHours(1));
            Assert.Single(state.PairingInvites);
            Assert.Equal("83.44.12.99", invite2.AllowedIp);
        }

        [Fact]
        public void CreateInvite_AllowsMultipleInvitesForSameIp_DifferentAlias()
        {
            var state = new ServerWorldState();

            ClientPairingLogic.CreateInvite(state, "Jugador1", "83.44.12.34", TimeSpan.FromHours(72), BaseTime, "AAA111");
            ClientPairingLogic.CreateInvite(state, "Jugador2", "83.44.12.34", TimeSpan.FromHours(72), BaseTime, "BBB222");

            // Mismo hogar/NAT: dos invitaciones coexisten para la misma IP
            Assert.Equal(2, state.PairingInvites.Count);
            Assert.Equal(2, ClientPairingLogic.FindActiveInvitesForIp(state, "83.44.12.34", BaseTime).Count);
        }

        [Fact]
        public void EvaluateLobbyAccess_RejectsUnknownIp_AllowsInvitedIp_AndAllowsPairedTokenFromAnyIp()
        {
            var state = new ServerWorldState();
            var invite = ClientPairingLogic.CreateInvite(state, "Dextrure", "83.44.12.34", TimeSpan.FromHours(72), BaseTime, "K7M9P2");

            // 1. IP desconocida sin invitación ni token emparejado -> Denegado
            Assert.Equal(
                LobbyAccessResult.DeniedNoInvite,
                ClientPairingLogic.EvaluateLobbyAccess(state, requireWhitelist: true, "tok_stranger", "91.22.33.44", BaseTime));

            // 2. IP invitada dentro de los 3 días -> Admitido en fase de emparejamiento
            Assert.Equal(
                LobbyAccessResult.AllowedPendingPairing,
                ClientPairingLogic.EvaluateLobbyAccess(state, requireWhitelist: true, "tok_dextrure", "83.44.12.34", BaseTime.AddDays(2)));

            // 3.Pasados los 3 días sin canjear -> Caducada y denegado
            Assert.Equal(
                LobbyAccessResult.DeniedNoInvite,
                ClientPairingLogic.EvaluateLobbyAccess(state, requireWhitelist: true, "tok_dextrure", "83.44.12.34", BaseTime.AddDays(3).AddMinutes(1)));
        }

        [Fact]
        public void TryRedeemInvite_ValidatesIpAndCode_ConsumesSingleUseCode_AndAuthorizesClient()
        {
            var state = new ServerWorldState();
            ClientPairingLogic.CreateInvite(state, "Dextrure", "83.44.12.34", TimeSpan.FromHours(72), BaseTime, "K7M9P2");

            // Código erróneo desde la IP correcta -> InvalidCode y la invitación sigue viva
            var badCode = ClientPairingLogic.TryRedeemInvite(
                state, 1001L, "Dex", "tok_dex", "83.44.12.34", "XXXXXX", BaseTime.AddHours(2), out _, out _);
            Assert.Equal(PairingRedeemResult.InvalidCode, badCode);
            Assert.Single(state.PairingInvites);

            // Código correcto pero desde otra IP -> IpMismatch
            var badIp = ClientPairingLogic.TryRedeemInvite(
                state, 1001L, "Dex", "tok_dex", "99.99.99.99", "K7M9P2", BaseTime.AddHours(2), out _, out _);
            Assert.Equal(PairingRedeemResult.IpMismatch, badIp);
            Assert.Single(state.PairingInvites);

            // Código e IP correctos -> Success, invitación consumida y cliente emparejado
            var ok = ClientPairingLogic.TryRedeemInvite(
                state, 1001L, "Dex", "tok_dex", "::ffff:83.44.12.34", "k7m9p2", BaseTime.AddHours(2), out var auth, out _);
            Assert.Equal(PairingRedeemResult.Success, ok);
            Assert.NotNull(auth);
            Assert.Empty(state.PairingInvites);
            Assert.Equal("tok_dex", state.Players[1001L].OwnerToken);

            // Una vez emparejado su dispositivo, puede reconectar incluso si su IP doméstica cambia días después
            Assert.Equal(
                LobbyAccessResult.AllowedPaired,
                ClientPairingLogic.EvaluateLobbyAccess(state, requireWhitelist: true, "tok_dex", "190.10.20.30", BaseTime.AddDays(10)));
            Assert.Equal("190.10.20.30", state.AuthorizedClients["tok_dex"].LastSeenIp);
        }

        [Fact]
        public void EnsureExistingClaimsAuthorized_PreservesPreExistingClaimedPlayers()
        {
            var state = new ServerWorldState();
            var existing = state.GetOrCreatePlayer(555L, "mTx");
            existing.OwnerToken = "token_juanlu_123";

            int imported = ClientPairingLogic.EnsureExistingClaimsAuthorized(state, BaseTime);

            Assert.Equal(1, imported);
            Assert.Equal(
                LobbyAccessResult.AllowedPaired,
                ClientPairingLogic.EvaluateLobbyAccess(state, requireWhitelist: true, "token_juanlu_123", "77.88.99.11", BaseTime));
        }

        [Fact]
        public void Revoke_RemovesPendingInviteOrAuthorizedClientAndClearsClaim()
        {
            var state = new ServerWorldState();
            ClientPairingLogic.CreateInvite(state, "Tester1", "81.1.1.1", TimeSpan.FromHours(72), BaseTime, "ABC234");
            ClientPairingLogic.TryRedeemInvite(state, 2002L, "Tester1", "tok_t1", "81.1.1.1", "ABC234", BaseTime, out _, out _);

            Assert.True(ClientPairingLogic.Revoke(state, "Tester1", out var affected, out _));
            Assert.Contains(2002L, affected);
            Assert.Null(state.Players[2002L].OwnerToken);
            Assert.Equal(
                LobbyAccessResult.DeniedNoInvite,
                ClientPairingLogic.EvaluateLobbyAccess(state, requireWhitelist: true, "tok_t1", "81.1.1.1", BaseTime));
        }
    }
}
