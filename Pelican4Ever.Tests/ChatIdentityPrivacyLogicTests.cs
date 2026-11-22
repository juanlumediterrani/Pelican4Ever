using Pelican4Ever.Core.Chat;
using Xunit;

namespace Pelican4Ever.Tests;

public class ChatIdentityPrivacyLogicTests
{
    [Theory]
    [InlineData("192.168.1.50", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("13.61.223.199", true)]
    [InlineData(" 10.0.0.5 ", true)]
    [InlineData("192.168.1.50:24642", true)]
    [InlineData("::1", true)]
    [InlineData("[::1]:24642", true)]
    [InlineData("::ffff:192.168.1.50", true)]
    [InlineData("Dr4co", false)]
    [InlineData("Player 1", false)]
    [InlineData("12345", false)]
    [InlineData("1.2.3", false)]
    [InlineData("foo.bar.baz.qux", false)]
    [InlineData("Player:1", false)]
    [InlineData("?", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IsIpAddressLabel_DetectsOnlyIpEndpoints(string? input, bool expected)
    {
        Assert.Equal(expected, ChatIdentityPrivacyLogic.IsIpAddressLabel(input));
    }

    [Fact]
    public void SanitizeChatUserName_HidesIpWhenEnabled_AndPreservesPlatformNames()
    {
        Assert.Equal(string.Empty, ChatIdentityPrivacyLogic.SanitizeChatUserName("192.168.1.50", hideClientIpInChat: true));
        Assert.Equal(string.Empty, ChatIdentityPrivacyLogic.SanitizeChatUserName("::1", hideClientIpInChat: true));
        Assert.Equal("Dr4co", ChatIdentityPrivacyLogic.SanitizeChatUserName("Dr4co", hideClientIpInChat: true));
        Assert.Equal("192.168.1.50", ChatIdentityPrivacyLogic.SanitizeChatUserName("192.168.1.50", hideClientIpInChat: false));
        Assert.Equal(string.Empty, ChatIdentityPrivacyLogic.SanitizeChatUserName(null, hideClientIpInChat: true));
    }
}
