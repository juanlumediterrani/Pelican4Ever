using System;

namespace Pelican4Ever.Core.World;

/// <summary>Pure confirmation gate; construction must not start before an explicit Yes.</summary>
public static class ResidenceClaimConfirmation
{
    public static bool ShouldCreate(string? answer) =>
        string.Equals(answer, "Yes", StringComparison.Ordinal);
}
