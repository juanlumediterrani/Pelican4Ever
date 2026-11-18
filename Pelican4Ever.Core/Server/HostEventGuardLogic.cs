using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.Server
{
    public enum HostBlockingMenuKind
    {
        None = 0,
        SaveGameMenu = 1,
        DialogueBox = 2,
        ConfirmationDialog = 3,
        NamingMenu = 4,
        LetterViewerMenu = 5,
        Other = 6
    }

    public enum HostEventGuardActionKind
    {
        None = 0,
        AdvanceDialogue = 1,
        AnswerDialogueQuestion = 2,
        CancelConfirmationDialog = 3,
        ExitActiveMenu = 4,
        SkipCurrentEvent = 5,
        ForceExitCurrentEvent = 6
    }

    public sealed record HostEventGuardDecision(HostEventGuardActionKind Action, int ResponseIndex = -1)
    {
        public static readonly HostEventGuardDecision DoNothing = new(HostEventGuardActionKind.None);
    }

    public sealed class HostEventGuardInput
    {
        public bool IsMainPlayer { get; init; }
        public bool IsWorldReady { get; init; }
        public bool IsUnattendedHost { get; init; }
        public bool AutoResolveEnabled { get; init; }
        public bool IsRolloverBusy { get; init; }
        public bool IsFestival { get; init; }
        public bool IsRelayActive { get; init; }
        public bool HasActiveRenderLease { get; init; }
        public bool HasCurrentEvent { get; init; }
        public bool IsEventUp { get; init; }
        public bool IsEventSkippable { get; init; }
        public HostBlockingMenuKind ActiveMenuKind { get; init; }
        public bool IsDialogueQuestion { get; init; }
        public bool IsDialogueTransitioning { get; init; }
        public int DialogueSafetyTimer { get; init; }
        public IReadOnlyList<string> ResponseKeys { get; init; } = Array.Empty<string>();
        public string? LastQuestionKey { get; init; }
        public int StuckTicks { get; init; }
        public int DelayTicks { get; init; }
        public int HardEventTimeoutTicks { get; init; }
    }

    /// <summary>
    /// Política pura para destrabar eventos, cinemáticas y diálogos en un anfitrión desatendido
    /// sin requerir un agente externo (parche 0.5.1).
    /// </summary>
    public static class HostEventGuardLogic
    {
        private static readonly string[] PreferredDeclineKeys =
        {
            "no",
            "leave",
            "cancel",
            "decline",
            "exit",
            "close",
            "never",
            "nothing"
        };

        public static bool IsBlockingState(
            bool hasCurrentEvent,
            bool isEventUp,
            HostBlockingMenuKind menuKind)
        {
            if (menuKind == HostBlockingMenuKind.SaveGameMenu)
                return false;

            return hasCurrentEvent
                || isEventUp
                || menuKind is HostBlockingMenuKind.DialogueBox
                    or HostBlockingMenuKind.ConfirmationDialog
                    or HostBlockingMenuKind.NamingMenu
                    or HostBlockingMenuKind.LetterViewerMenu;
        }

        public static int ChooseDialogueResponseIndex(string? lastQuestionKey, IReadOnlyList<string>? responseKeys)
        {
            if (responseKeys == null || responseKeys.Count == 0)
                return -1;

            // Adopción de mascota en el host: rechazar siempre ("No") para no crear mascotas fantasma ni abrir NamingMenu.
            if (string.Equals(lastQuestionKey?.Trim(), "pet", StringComparison.OrdinalIgnoreCase))
            {
                int noIndex = FindMatchingKeyIndex(responseKeys, "no");
                if (noIndex >= 0)
                    return noIndex;
            }

            foreach (string preferred in PreferredDeclineKeys)
            {
                int idx = FindMatchingKeyIndex(responseKeys, preferred);
                if (idx >= 0)
                    return idx;
            }

            return responseKeys.Count - 1;
        }

        public static HostEventGuardDecision Decide(HostEventGuardInput input)
        {
            if (input == null
                || !input.IsMainPlayer
                || !input.IsWorldReady
                || !input.IsUnattendedHost
                || !input.AutoResolveEnabled
                || input.IsRolloverBusy
                || input.IsFestival
                || input.IsRelayActive
                || input.HasActiveRenderLease
                || input.ActiveMenuKind == HostBlockingMenuKind.SaveGameMenu)
            {
                return HostEventGuardDecision.DoNothing;
            }

            if (!IsBlockingState(input.HasCurrentEvent, input.IsEventUp, input.ActiveMenuKind))
                return HostEventGuardDecision.DoNothing;

            if (input.StuckTicks < Math.Max(0, input.DelayTicks))
                return HostEventGuardDecision.DoNothing;

            if (input.ActiveMenuKind == HostBlockingMenuKind.DialogueBox)
            {
                if (input.IsDialogueTransitioning)
                    return HostEventGuardDecision.DoNothing;

                if (input.IsDialogueQuestion && input.ResponseKeys.Count > 0)
                {
                    int idx = ChooseDialogueResponseIndex(input.LastQuestionKey, input.ResponseKeys);
                    return idx >= 0
                        ? new HostEventGuardDecision(HostEventGuardActionKind.AnswerDialogueQuestion, idx)
                        : HostEventGuardDecision.DoNothing;
                }

                if (input.HasCurrentEvent && input.IsEventSkippable)
                    return new HostEventGuardDecision(HostEventGuardActionKind.SkipCurrentEvent);

                return input.DialogueSafetyTimer <= 0 || input.StuckTicks >= Math.Max(0, input.DelayTicks) + 60
                    ? new HostEventGuardDecision(HostEventGuardActionKind.AdvanceDialogue)
                    : HostEventGuardDecision.DoNothing;
            }

            if (input.ActiveMenuKind == HostBlockingMenuKind.ConfirmationDialog)
                return new HostEventGuardDecision(HostEventGuardActionKind.CancelConfirmationDialog);

            if (input.ActiveMenuKind is HostBlockingMenuKind.NamingMenu or HostBlockingMenuKind.LetterViewerMenu)
                return new HostEventGuardDecision(HostEventGuardActionKind.ExitActiveMenu);

            if (input.HasCurrentEvent || input.IsEventUp)
            {
                if (input.IsEventSkippable)
                    return new HostEventGuardDecision(HostEventGuardActionKind.SkipCurrentEvent);

                if (input.StuckTicks >= Math.Max(input.DelayTicks, input.HardEventTimeoutTicks))
                    return new HostEventGuardDecision(HostEventGuardActionKind.ForceExitCurrentEvent);
            }

            return HostEventGuardDecision.DoNothing;
        }

        private static int FindMatchingKeyIndex(IReadOnlyList<string> responseKeys, string needle)
        {
            for (int i = 0; i < responseKeys.Count; i++)
            {
                string key = responseKeys[i]?.Trim() ?? string.Empty;
                if (string.Equals(key, needle, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            for (int i = 0; i < responseKeys.Count; i++)
            {
                string key = responseKeys[i]?.Trim() ?? string.Empty;
                if (key.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }
    }
}
