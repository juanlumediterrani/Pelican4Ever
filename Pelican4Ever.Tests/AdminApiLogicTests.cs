using Pelican4Ever.Core.Admin;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class AdminApiLogicTests
    {
        [Theory]
        [InlineData("Bearer secret_token_123", "secret_token_123", true)]
        [InlineData("bearer secret_token_123", "secret_token_123", true)]
        [InlineData("secret_token_123", "secret_token_123", true)]
        [InlineData("Bearer wrong_token", "secret_token_123", false)]
        [InlineData("", "secret_token_123", false)]
        [InlineData(null, "secret_token_123", false)]
        [InlineData("Bearer secret_token_123", "", false)]
        public void IsAuthorized_ValidatesBearerTokenCorrectly(string? authHeader, string expectedToken, bool expectedValid)
        {
            bool valid = AdminApiLogic.IsAuthorized(authHeader, expectedToken);
            Assert.Equal(expectedValid, valid);
        }

        [Fact]
        public void GetDashboardHtml_ContainsExpectedElements()
        {
            string html = AdminApiLogic.GetDashboardHtml("test_token");
            Assert.Contains("Pelican 4Ever Dashboard", html);
            Assert.Contains("/api/status", html);
            Assert.Contains("/api/rollover", html);
            Assert.Contains("/api/mines/reset", html);
            Assert.Contains("test_token", html);
        }
    }
}
