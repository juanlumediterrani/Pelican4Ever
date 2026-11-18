using System;
using System.Collections.Generic;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Server;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Desbloquea automáticamente eventos, cinemáticas y diálogos en el anfitrión desatendido
    /// (<c>AutoHostSaveName</c> configurado), evitando que el host quede congelado esperando entrada
    /// manual e impida checkpoints y <c>removeDisconnectedFarmers()</c> (parche 0.5.1).
    /// </summary>
    public sealed class HeadlessEventGuardModule : IModule
    {
        private const int ActionCooldownTicks = 30;
        private const int HardEventTimeoutSeconds = 20;

        private ModContext? context;
        private int stuckTicks;
        private int cooldownTicks;
        private bool episodeLogged;

        public string Name => "HeadlessEventGuard";

        public ModuleSide Side => ModuleSide.Host;

        public void Register(ModContext ctx)
        {
            context = ctx;
            ctx.Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += (_, _) => ResetState();
            ctx.Helper.Events.GameLoop.SaveLoaded += (_, _) => ResetState();
        }

        private void ResetState()
        {
            stuckTicks = 0;
            cooldownTicks = 0;
            episodeLogged = false;
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (context == null || !Context.IsMainPlayer || !Context.IsWorldReady)
                return;

            if (string.IsNullOrEmpty(context.Config.AutoHostSaveName) || !context.Config.AutoResolveHostEvents)
            {
                ResetState();
                return;
            }

            HostBlockingMenuKind menuKind = ClassifyMenu(Game1.activeClickableMenu);
            bool hasCurrentEvent = Game1.CurrentEvent != null;
            bool isEventUp = Game1.eventUp;

            if (!HostEventGuardLogic.IsBlockingState(hasCurrentEvent, isEventUp, menuKind))
            {
                ResetState();
                return;
            }

            if (DailyRolloverModule.IsBusy
                || Game1.isFestival()
                || HostEventRelayModule.IsRelayActive
                || HeadlessRenderModule.Leases.HasActive(DateTime.UtcNow))
            {
                stuckTicks = 0;
                cooldownTicks = 0;
                return;
            }

            stuckTicks++;
            if (cooldownTicks > 0)
            {
                cooldownTicks--;
                return;
            }

            DialogueBox? dialogueBox = Game1.activeClickableMenu as DialogueBox;
            string[] responseKeys = ExtractResponseKeys(dialogueBox);

            int delayTicks = Math.Max(1, context.Config.HostEventAutoResolveDelaySeconds) * 60;
            int hardTimeoutTicks = Math.Max(delayTicks, HardEventTimeoutSeconds * 60);

            var input = new HostEventGuardInput
            {
                IsMainPlayer = true,
                IsWorldReady = true,
                IsUnattendedHost = true,
                AutoResolveEnabled = context.Config.AutoResolveHostEvents,
                IsRolloverBusy = DailyRolloverModule.IsBusy,
                IsFestival = Game1.isFestival(),
                IsRelayActive = HostEventRelayModule.IsRelayActive,
                HasActiveRenderLease = HeadlessRenderModule.Leases.HasActive(DateTime.UtcNow),
                HasCurrentEvent = hasCurrentEvent,
                IsEventUp = isEventUp,
                IsEventSkippable = Game1.CurrentEvent?.skippable ?? false,
                ActiveMenuKind = menuKind,
                IsDialogueQuestion = dialogueBox?.isQuestion ?? false,
                IsDialogueTransitioning = dialogueBox?.transitioning ?? false,
                DialogueSafetyTimer = dialogueBox?.safetyTimer ?? 0,
                ResponseKeys = responseKeys,
                LastQuestionKey = Game1.currentLocation?.lastQuestionKey,
                StuckTicks = stuckTicks,
                DelayTicks = delayTicks,
                HardEventTimeoutTicks = hardTimeoutTicks
            };

            HostEventGuardDecision decision = HostEventGuardLogic.Decide(input);
            if (decision.Action == HostEventGuardActionKind.None)
                return;

            ExecuteDecision(decision, dialogueBox, responseKeys);
            cooldownTicks = ActionCooldownTicks;
        }

        private void ExecuteDecision(
            HostEventGuardDecision decision,
            DialogueBox? dialogueBox,
            IReadOnlyList<string> responseKeys)
        {
            if (context == null)
                return;

            string eventId = Game1.CurrentEvent?.id ?? string.Empty;
            string location = Game1.currentLocation?.NameOrUniqueName ?? string.Empty;
            string detail = decision.Action.ToString();

            switch (decision.Action)
            {
                case HostEventGuardActionKind.AnswerDialogueQuestion when dialogueBox != null:
                {
                    int idx = decision.ResponseIndex;
                    string key = idx >= 0 && idx < responseKeys.Count ? responseKeys[idx] : string.Empty;
                    detail = $"AnswerDialogueQuestion({Game1.currentLocation?.lastQuestionKey ?? "?"}={key})";
                    dialogueBox.safetyTimer = 0;
                    dialogueBox.selectedResponse = idx;
                    int clickX = dialogueBox.x + 16;
                    int clickY = dialogueBox.y + 16;
                    if (dialogueBox.responseCC != null && idx >= 0 && idx < dialogueBox.responseCC.Count)
                    {
                        var bounds = dialogueBox.responseCC[idx].bounds;
                        clickX = bounds.Center.X;
                        clickY = bounds.Center.Y;
                    }
                    dialogueBox.receiveLeftClick(clickX, clickY, playSound: false);
                    break;
                }

                case HostEventGuardActionKind.AdvanceDialogue when dialogueBox != null:
                {
                    detail = "AdvanceDialogue";
                    dialogueBox.safetyTimer = 0;
                    dialogueBox.receiveLeftClick(dialogueBox.x + 16, dialogueBox.y + 16, playSound: false);
                    break;
                }

                case HostEventGuardActionKind.CancelConfirmationDialog when Game1.activeClickableMenu is ConfirmationDialog confirm:
                {
                    detail = "CancelConfirmationDialog";
                    confirm.cancel();
                    break;
                }

                case HostEventGuardActionKind.ExitActiveMenu:
                {
                    detail = $"ExitActiveMenu({Game1.activeClickableMenu?.GetType().Name ?? "null"})";
                    Game1.exitActiveMenu();
                    break;
                }

                case HostEventGuardActionKind.SkipCurrentEvent when Game1.CurrentEvent != null:
                {
                    detail = $"SkipCurrentEvent({eventId})";
                    Game1.CurrentEvent.skipEvent();
                    break;
                }

                case HostEventGuardActionKind.ForceExitCurrentEvent when Game1.CurrentEvent != null:
                {
                    detail = $"ForceExitCurrentEvent({eventId})";
                    Game1.CurrentEvent.exitEvent();
                    break;
                }

                default:
                    return;
            }

            if (!episodeLogged)
            {
                episodeLogged = true;
                context.Monitor.Log(
                    $"[HeadlessEventGuard] Resolviendo bloqueo en host desatendido ({detail}) en '{location}'.",
                    LogLevel.Info);
                AdminApiModule.EventLog.Append(
                    HostEventTypes.HostEventResolved,
                    Game1.Date?.TotalDays ?? 0,
                    Game1.timeOfDay,
                    $"Host desatendido resolvió bloqueo: {detail} ({location})",
                    new Dictionary<string, object?>
                    {
                        ["action"] = detail,
                        ["eventId"] = eventId,
                        ["location"] = location
                    });
            }
        }

        private static HostBlockingMenuKind ClassifyMenu(IClickableMenu? menu) => menu switch
        {
            null => HostBlockingMenuKind.None,
            SaveGameMenu => HostBlockingMenuKind.SaveGameMenu,
            DialogueBox => HostBlockingMenuKind.DialogueBox,
            ConfirmationDialog => HostBlockingMenuKind.ConfirmationDialog,
            NamingMenu => HostBlockingMenuKind.NamingMenu,
            LetterViewerMenu => HostBlockingMenuKind.LetterViewerMenu,
            _ => HostBlockingMenuKind.Other
        };

        private static string[] ExtractResponseKeys(DialogueBox? dialogueBox)
        {
            if (dialogueBox?.responses == null || dialogueBox.responses.Length == 0)
                return Array.Empty<string>();

            var keys = new string[dialogueBox.responses.Length];
            for (int i = 0; i < dialogueBox.responses.Length; i++)
                keys[i] = dialogueBox.responses[i]?.responseKey ?? string.Empty;
            return keys;
        }
    }
}
