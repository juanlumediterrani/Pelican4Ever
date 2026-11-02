using System;

namespace Pelican4Ever.Core.Admin;

/// <summary>
/// Decides when the "found a residence" prompt may open for the door under the player. The prompt is shown once per
/// visit to a door's trigger tile: cancelling it must not reopen it while the player still stands there. It is armed
/// again only after the player has been seen off that tile (or after <see cref="Reset"/>, e.g. on changing location).
/// </summary>
public sealed class PortalPromptGate
{
    private string? promptedDoorId;

    /// <param name="unclaimedDoorIdUnderPlayer">Id of the unclaimed door whose trigger tile the player stands on, or null.</param>
    public bool ShouldPrompt(string? unclaimedDoorIdUnderPlayer)
    {
        if (string.IsNullOrEmpty(unclaimedDoorIdUnderPlayer))
        {
            promptedDoorId = null;
            return false;
        }

        if (string.Equals(promptedDoorId, unclaimedDoorIdUnderPlayer, StringComparison.Ordinal))
            return false;

        promptedDoorId = unclaimedDoorIdUnderPlayer;
        return true;
    }

    public void Reset() => promptedDoorId = null;
}
