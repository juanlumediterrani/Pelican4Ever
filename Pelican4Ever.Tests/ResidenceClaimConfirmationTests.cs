using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests;

public class ResidenceClaimConfirmationTests
{
    [Theory]
    [InlineData("Yes", true)]
    [InlineData("No", false)]
    [InlineData("Cancel", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ResidenceIsCreatedOnlyAfterExplicitYes(string? answer, bool expected)
    {
        Assert.Equal(expected, ResidenceClaimConfirmation.ShouldCreate(answer));
    }
}
