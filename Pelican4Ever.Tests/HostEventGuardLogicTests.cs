using Pelican4Ever.Core.Server;
using Xunit;

namespace Pelican4Ever.Tests;

public class HostEventGuardLogicTests
{
    [Fact]
    public void HumanHost_NeverInterferes()
    {
        var input = new HostEventGuardInput
        {
            IsMainPlayer = true,
            IsWorldReady = true,
            IsUnattendedHost = false,
            AutoResolveEnabled = true,
            HasCurrentEvent = true,
            IsEventSkippable = true,
            ActiveMenuKind = HostBlockingMenuKind.DialogueBox,
            StuckTicks = 1000,
            DelayTicks = 300,
            HardEventTimeoutTicks = 900
        };

        Assert.Equal(HostEventGuardActionKind.None, HostEventGuardLogic.Decide(input).Action);
    }

    [Fact]
    public void Guards_SkipDuringRolloverFestivalRelayOrRenderLease()
    {
        var baseIn = new HostEventGuardInput
        {
            IsMainPlayer = true,
            IsWorldReady = true,
            IsUnattendedHost = true,
            AutoResolveEnabled = true,
            HasCurrentEvent = true,
            IsEventSkippable = true,
            StuckTicks = 1000,
            DelayTicks = 300,
            HardEventTimeoutTicks = 900
        };

        Assert.Equal(HostEventGuardActionKind.None, HostEventGuardLogic.Decide(new HostEventGuardInput
        {
            IsMainPlayer = baseIn.IsMainPlayer, IsWorldReady = baseIn.IsWorldReady, IsUnattendedHost = true, AutoResolveEnabled = true,
            HasCurrentEvent = true, IsEventSkippable = true, StuckTicks = 1000, DelayTicks = 300, HardEventTimeoutTicks = 900,
            IsRolloverBusy = true
        }).Action);

        Assert.Equal(HostEventGuardActionKind.None, HostEventGuardLogic.Decide(new HostEventGuardInput
        {
            IsMainPlayer = true, IsWorldReady = true, IsUnattendedHost = true, AutoResolveEnabled = true,
            HasCurrentEvent = true, IsEventSkippable = true, StuckTicks = 1000, DelayTicks = 300, HardEventTimeoutTicks = 900,
            IsFestival = true
        }).Action);

        Assert.Equal(HostEventGuardActionKind.None, HostEventGuardLogic.Decide(new HostEventGuardInput
        {
            IsMainPlayer = true, IsWorldReady = true, IsUnattendedHost = true, AutoResolveEnabled = true,
            HasCurrentEvent = true, IsEventSkippable = true, StuckTicks = 1000, DelayTicks = 300, HardEventTimeoutTicks = 900,
            IsRelayActive = true
        }).Action);

        Assert.Equal(HostEventGuardActionKind.None, HostEventGuardLogic.Decide(new HostEventGuardInput
        {
            IsMainPlayer = true, IsWorldReady = true, IsUnattendedHost = true, AutoResolveEnabled = true,
            HasCurrentEvent = true, IsEventSkippable = true, StuckTicks = 1000, DelayTicks = 300, HardEventTimeoutTicks = 900,
            HasActiveRenderLease = true
        }).Action);
    }

    [Fact]
    public void WaitsForGraceDelayBeforeActing()
    {
        var early = new HostEventGuardInput
        {
            IsMainPlayer = true,
            IsWorldReady = true,
            IsUnattendedHost = true,
            AutoResolveEnabled = true,
            HasCurrentEvent = true,
            IsEventSkippable = true,
            StuckTicks = 299,
            DelayTicks = 300,
            HardEventTimeoutTicks = 900
        };

        Assert.Equal(HostEventGuardActionKind.None, HostEventGuardLogic.Decide(early).Action);
    }

    [Fact]
    public void PetAdoptionQuestion_SelectsNo()
    {
        var input = new HostEventGuardInput
        {
            IsMainPlayer = true,
            IsWorldReady = true,
            IsUnattendedHost = true,
            AutoResolveEnabled = true,
            HasCurrentEvent = true,
            IsEventUp = true,
            IsEventSkippable = false,
            ActiveMenuKind = HostBlockingMenuKind.DialogueBox,
            IsDialogueQuestion = true,
            LastQuestionKey = "pet",
            ResponseKeys = new[] { "Yes", "No" },
            StuckTicks = 300,
            DelayTicks = 300,
            HardEventTimeoutTicks = 900
        };

        var decision = HostEventGuardLogic.Decide(input);
        Assert.Equal(HostEventGuardActionKind.AnswerDialogueQuestion, decision.Action);
        Assert.Equal(1, decision.ResponseIndex);
    }

    [Fact]
    public void GenericQuestion_PrefersDeclineOrLastResponse()
    {
        Assert.Equal(0, HostEventGuardLogic.ChooseDialogueResponseIndex("shop", new[] { "Leave", "Buy" }));
        Assert.Equal(1, HostEventGuardLogic.ChooseDialogueResponseIndex("cave", new[] { "Mushrooms", "Bats" }));
    }

    [Fact]
    public void NonQuestionDialogue_AdvancesWhenReady()
    {
        var input = new HostEventGuardInput
        {
            IsMainPlayer = true,
            IsWorldReady = true,
            IsUnattendedHost = true,
            AutoResolveEnabled = true,
            ActiveMenuKind = HostBlockingMenuKind.DialogueBox,
            IsDialogueQuestion = false,
            DialogueSafetyTimer = 0,
            StuckTicks = 300,
            DelayTicks = 300,
            HardEventTimeoutTicks = 900
        };

        Assert.Equal(HostEventGuardActionKind.AdvanceDialogue, HostEventGuardLogic.Decide(input).Action);
    }

    [Fact]
    public void SkippableEvent_SkipsWhenNoQuestionOpen()
    {
        var input = new HostEventGuardInput
        {
            IsMainPlayer = true,
            IsWorldReady = true,
            IsUnattendedHost = true,
            AutoResolveEnabled = true,
            HasCurrentEvent = true,
            IsEventUp = true,
            IsEventSkippable = true,
            ActiveMenuKind = HostBlockingMenuKind.DialogueBox,
            IsDialogueQuestion = false,
            StuckTicks = 300,
            DelayTicks = 300,
            HardEventTimeoutTicks = 900
        };

        Assert.Equal(HostEventGuardActionKind.SkipCurrentEvent, HostEventGuardLogic.Decide(input).Action);
    }

    [Fact]
    public void NonSkippableEvent_WithoutMenu_ForceExitsAfterHardTimeout()
    {
        var beforeHardTimeout = new HostEventGuardInput
        {
            IsMainPlayer = true,
            IsWorldReady = true,
            IsUnattendedHost = true,
            AutoResolveEnabled = true,
            HasCurrentEvent = true,
            IsEventUp = true,
            IsEventSkippable = false,
            StuckTicks = 500,
            DelayTicks = 300,
            HardEventTimeoutTicks = 900
        };
        Assert.Equal(HostEventGuardActionKind.None, HostEventGuardLogic.Decide(beforeHardTimeout).Action);

        var afterHardTimeout = new HostEventGuardInput
        {
            IsMainPlayer = true,
            IsWorldReady = true,
            IsUnattendedHost = true,
            AutoResolveEnabled = true,
            HasCurrentEvent = true,
            IsEventUp = true,
            IsEventSkippable = false,
            StuckTicks = 900,
            DelayTicks = 300,
            HardEventTimeoutTicks = 900
        };
        Assert.Equal(HostEventGuardActionKind.ForceExitCurrentEvent, HostEventGuardLogic.Decide(afterHardTimeout).Action);
    }

    [Fact]
    public void ConfirmationNamingAndLetterMenus_AreDismissed()
    {
        Assert.Equal(HostEventGuardActionKind.CancelConfirmationDialog, HostEventGuardLogic.Decide(new HostEventGuardInput
        {
            IsMainPlayer = true, IsWorldReady = true, IsUnattendedHost = true, AutoResolveEnabled = true,
            ActiveMenuKind = HostBlockingMenuKind.ConfirmationDialog,
            StuckTicks = 300, DelayTicks = 300, HardEventTimeoutTicks = 900
        }).Action);

        Assert.Equal(HostEventGuardActionKind.ExitActiveMenu, HostEventGuardLogic.Decide(new HostEventGuardInput
        {
            IsMainPlayer = true, IsWorldReady = true, IsUnattendedHost = true, AutoResolveEnabled = true,
            ActiveMenuKind = HostBlockingMenuKind.NamingMenu,
            StuckTicks = 300, DelayTicks = 300, HardEventTimeoutTicks = 900
        }).Action);

        Assert.Equal(HostEventGuardActionKind.ExitActiveMenu, HostEventGuardLogic.Decide(new HostEventGuardInput
        {
            IsMainPlayer = true, IsWorldReady = true, IsUnattendedHost = true, AutoResolveEnabled = true,
            ActiveMenuKind = HostBlockingMenuKind.LetterViewerMenu,
            StuckTicks = 300, DelayTicks = 300, HardEventTimeoutTicks = 900
        }).Action);
    }

    [Fact]
    public void ExternalModDialogueAndProrrogaLetter_AreNeverDismissed_CP605()
    {
        Assert.True(HostEventGuardLogic.IsExternalControlledMenu(true, null));
        Assert.True(HostEventGuardLogic.IsExternalControlledMenu(false, "CorazonPelican.Prorroga.comp-1"));
        Assert.False(HostEventGuardLogic.IsExternalControlledMenu(false, "Robin_Mail"));

        Assert.Equal(HostEventGuardActionKind.None, HostEventGuardLogic.Decide(new HostEventGuardInput
        {
            IsMainPlayer = true, IsWorldReady = true, IsUnattendedHost = true, AutoResolveEnabled = true,
            ActiveMenuKind = HostBlockingMenuKind.DialogueBox,
            IsExternalModDialogueOrLetter = true,
            StuckTicks = 2000, DelayTicks = 300, HardEventTimeoutTicks = 900
        }).Action);

        Assert.Equal(HostEventGuardActionKind.None, HostEventGuardLogic.Decide(new HostEventGuardInput
        {
            IsMainPlayer = true, IsWorldReady = true, IsUnattendedHost = true, AutoResolveEnabled = true,
            ActiveMenuKind = HostBlockingMenuKind.LetterViewerMenu,
            IsExternalModDialogueOrLetter = true,
            StuckTicks = 2000, DelayTicks = 300, HardEventTimeoutTicks = 900
        }).Action);
    }
}
