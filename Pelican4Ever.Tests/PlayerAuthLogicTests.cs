using System;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Players;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class PlayerAuthLogicTests
    {
        private const string TokenAlice = "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6";
        private const string TokenBob = "f9e8d7c6b5a4f3e2d1c0b9a8f7e6d5c4";

        [Fact]
        public void ValidateOrClaim_WhenUnclaimed_AndIncomingTokenProvided_ClaimsAndReturnsClaimed()
        {
            var record = new PlayerRecord
            {
                UniqueMultiplayerID = 1001,
                PlayerName = "Alice",
                OwnerToken = null
            };

            AuthResult result = PlayerAuthLogic.ValidateOrClaim(record, TokenAlice);

            Assert.Equal(AuthResult.Claimed, result);
            Assert.Equal(TokenAlice, record.OwnerToken);
            Assert.True(PlayerAuthLogic.IsClaimed(record));
        }

        [Fact]
        public void ValidateOrClaim_WhenUnclaimed_AndIncomingTokenHasWhitespace_TrimsAndClaims()
        {
            var record = new PlayerRecord { UniqueMultiplayerID = 1001, OwnerToken = null };

            AuthResult result = PlayerAuthLogic.ValidateOrClaim(record, $"   {TokenAlice}   ");

            Assert.Equal(AuthResult.Claimed, result);
            Assert.Equal(TokenAlice, record.OwnerToken);
        }

        [Fact]
        public void ValidateOrClaim_WhenUnclaimed_AndNoIncomingToken_AllowsConnectionWithoutClaim()
        {
            var record = new PlayerRecord { UniqueMultiplayerID = 1001, OwnerToken = null };

            AuthResult result = PlayerAuthLogic.ValidateOrClaim(record, null);

            Assert.Equal(AuthResult.Allowed, result);
            Assert.Null(record.OwnerToken);
            Assert.False(PlayerAuthLogic.IsClaimed(record));
        }

        [Fact]
        public void ValidateOrClaim_WhenClaimed_AndSameTokenProvided_ReturnsAllowed()
        {
            var record = new PlayerRecord
            {
                UniqueMultiplayerID = 1001,
                PlayerName = "Alice",
                OwnerToken = TokenAlice
            };

            AuthResult result = PlayerAuthLogic.ValidateOrClaim(record, TokenAlice);

            Assert.Equal(AuthResult.Allowed, result);
            Assert.Equal(TokenAlice, record.OwnerToken);
        }

        [Fact]
        public void ValidateOrClaim_WhenClaimed_AndDifferentTokenProvided_ReturnsDeniedMismatch()
        {
            var record = new PlayerRecord
            {
                UniqueMultiplayerID = 1001,
                PlayerName = "Alice",
                OwnerToken = TokenAlice
            };

            AuthResult result = PlayerAuthLogic.ValidateOrClaim(record, TokenBob);

            Assert.Equal(AuthResult.DeniedMismatch, result);
            Assert.Equal(TokenAlice, record.OwnerToken); // No cambia el dueño
        }

        [Fact]
        public void ValidateOrClaim_WhenClaimed_AndNoTokenProvided_ReturnsDeniedNoToken()
        {
            var record = new PlayerRecord
            {
                UniqueMultiplayerID = 1001,
                PlayerName = "Alice",
                OwnerToken = TokenAlice
            };

            AuthResult resultNull = PlayerAuthLogic.ValidateOrClaim(record, null);
            AuthResult resultEmpty = PlayerAuthLogic.ValidateOrClaim(record, "   ");

            Assert.Equal(AuthResult.DeniedNoToken, resultNull);
            Assert.Equal(AuthResult.DeniedNoToken, resultEmpty);
        }

        [Fact]
        public void ValidateOrClaim_WhenRecordIsNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => PlayerAuthLogic.ValidateOrClaim(null!, TokenAlice));
        }

        [Fact]
        public void ResetClaim_ClearsOwnerToken()
        {
            var record = new PlayerRecord
            {
                UniqueMultiplayerID = 1001,
                OwnerToken = TokenAlice
            };

            Assert.True(PlayerAuthLogic.IsClaimed(record));

            PlayerAuthLogic.ResetClaim(record);

            Assert.Null(record.OwnerToken);
            Assert.False(PlayerAuthLogic.IsClaimed(record));

            // Ahora Bob puede reclamarlo
            AuthResult bobResult = PlayerAuthLogic.ValidateOrClaim(record, TokenBob);
            Assert.Equal(AuthResult.Claimed, bobResult);
            Assert.Equal(TokenBob, record.OwnerToken);
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("tok_1234", true)]
        public void IsClaimed_EvaluatesCorrectly(string? token, bool expected)
        {
            var record = new PlayerRecord { OwnerToken = token };
            Assert.Equal(expected, PlayerAuthLogic.IsClaimed(record));
        }

        [Theory]
        [InlineData(null, "—")]
        [InlineData("", "—")]
        [InlineData("   ", "—")]
        [InlineData("12345", "12345")]
        [InlineData("12345678", "12345678")]
        [InlineData("a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6", "a1b2...c5d6")]
        public void MaskToken_FormatsCorrectly(string? token, string expected)
        {
            Assert.Equal(expected, PlayerAuthLogic.MaskToken(token));
        }

        [Fact]
        public void HostPlayerDetailDto_Properties_WorkAsExpected()
        {
            var dto = new HostPlayerDetailDto
            {
                PlayerId = 1001,
                Name = "Alice",
                IsClaimed = true,
                OwnerTokenMasked = "a1b2...c5d6"
            };

            Assert.True(dto.IsClaimed);
            Assert.Equal("a1b2...c5d6", dto.OwnerTokenMasked);
        }

        [Theory]
        [InlineData(null, TokenAlice, false)]
        [InlineData("", TokenAlice, false)]
        [InlineData("   ", TokenAlice, false)]
        [InlineData(TokenAlice, TokenAlice, false)]
        [InlineData($"  {TokenAlice}  ", TokenAlice, false)]
        [InlineData(TokenBob, TokenAlice, true)]
        [InlineData(TokenAlice, null, true)]
        [InlineData(TokenAlice, "", true)]
        public void BelongsToAnotherClient_EvaluatesCorrectly(string? slotOwnerToken, string? currentClientToken, bool expected)
        {
            bool actual = PlayerAuthLogic.BelongsToAnotherClient(slotOwnerToken, currentClientToken);
            Assert.Equal(expected, actual);
        }
    }
}
