using System;
using System.Collections.Generic;
using HarmonyLib;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Config;
using Pelican4Ever.Core.Security;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Network;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Proporciona un botón en el menú principal para activar/desactivar Pelican 4Ever internamente sin quitar
    /// archivos, así como avisos al cargar una partida local con el mod activo o al conectar a un servidor Pelican
    /// con el mod desactivado. Usa siempre <see cref="ModContext.RawHelper"/> y una instancia Harmony dedicada.
    /// </summary>
    public sealed class ModToggleModule : IModule
    {
        private sealed record ModalOption(string Label, Action OnSelect, Color AccentColor);

        private sealed class ActivationModal
        {
            public string Title { get; }
            public string Body { get; }
            public IReadOnlyList<ModalOption> Options { get; }
            public List<Rectangle> OptionBounds { get; } = new();
            public Rectangle BoxBounds { get; set; }

            public ActivationModal(string title, string body, IReadOnlyList<ModalOption> options)
            {
                Title = title;
                Body = body;
                Options = options;
            }
        }

        private const int ToggleButtonX = 72;
        private const int ToggleButtonY = 16;
        private const int ToggleButtonHeight = 44;

        private static ModContext? context;
        private static ActivationModal? activeModal;
        private static bool bypassSaveLoadPrompt;
        private static string? pendingServerPromptAddress;
        private static readonly Func<TitleMenu, bool>? shouldAllowTitleInteraction = CreateShouldAllowInteractionDelegate();

        private Harmony? toggleHarmony;

        public string Name => "ModToggle";

        public ModuleSide Side => ModuleSide.Client;

        public void Register(ModContext modContext)
        {
            context = modContext;
            toggleHarmony = new Harmony($"{modContext.Manifest.UniqueID}.Toggle");

            ApplyTogglePatches(toggleHarmony);

            IModEvents events = modContext.RawHelper.Events;
            events.Display.RenderedActiveMenu += OnRenderedActiveMenu;
            events.Display.MenuChanged += OnMenuChanged;
            events.GameLoop.UpdateTicked += OnUpdateTicked;
            events.Multiplayer.PeerContextReceived += OnPeerContextReceived;
        }

        private static void ApplyTogglePatches(Harmony harmony)
        {
            if (context is not ModContext ctx)
                return;

            void RegisterTogglePatch(string name, System.Reflection.MethodBase? original, HarmonyMethod prefix, bool optional = false)
            {
                ctx.PatchRegistry.Patch(
                    harmony,
                    ctx.Monitor,
                    name,
                    "ModToggle",
                    PatchCriticality.Experience,
                    original,
                    prefix: prefix,
                    optional: optional);
            }

            RegisterTogglePatch("TitleMenu.receiveLeftClick",
                AccessTools.Method(typeof(TitleMenu), nameof(TitleMenu.receiveLeftClick), new[] { typeof(int), typeof(int), typeof(bool) }),
                new HarmonyMethod(typeof(ModToggleModule), nameof(Prefix_TitleMenuReceiveLeftClick)));

            RegisterTogglePatch("TitleMenu.receiveRightClick",
                AccessTools.Method(typeof(TitleMenu), nameof(TitleMenu.receiveRightClick), new[] { typeof(int), typeof(int), typeof(bool) }),
                new HarmonyMethod(typeof(ModToggleModule), nameof(Prefix_BlockTitleMenuWhileModalOpen)));

            RegisterTogglePatch("TitleMenu.receiveKeyPress",
                AccessTools.Method(typeof(TitleMenu), nameof(TitleMenu.receiveKeyPress), new[] { typeof(Keys) }),
                new HarmonyMethod(typeof(ModToggleModule), nameof(Prefix_TitleMenuReceiveKeyPress)));

            RegisterTogglePatch("TitleMenu.receiveGamePadButton",
                AccessTools.Method(typeof(TitleMenu), nameof(TitleMenu.receiveGamePadButton), new[] { typeof(Buttons) }),
                new HarmonyMethod(typeof(ModToggleModule), nameof(Prefix_TitleMenuReceiveGamePadButton)));

            RegisterTogglePatch("TitleMenu.performHoverAction",
                AccessTools.Method(typeof(TitleMenu), nameof(TitleMenu.performHoverAction), new[] { typeof(int), typeof(int) }),
                new HarmonyMethod(typeof(ModToggleModule), nameof(Prefix_BlockTitleMenuWhileModalOpen)));

            RegisterTogglePatch("LoadGameMenu.receiveLeftClick",
                AccessTools.Method(typeof(LoadGameMenu), nameof(LoadGameMenu.receiveLeftClick), new[] { typeof(int), typeof(int), typeof(bool) }),
                new HarmonyMethod(typeof(ModToggleModule), nameof(Prefix_LoadGameMenuReceiveLeftClick)));

            RegisterTogglePatch("SaveFileSlot.Activate",
                AccessTools.Method(typeof(LoadGameMenu.SaveFileSlot), nameof(LoadGameMenu.SaveFileSlot.Activate)),
                new HarmonyMethod(typeof(ModToggleModule), nameof(Prefix_SaveFileSlotActivate)));

            Type? hostFileSlotType = AccessTools.Inner(typeof(CoopMenu), "HostFileSlot");
            System.Reflection.MethodInfo? hostActivate = hostFileSlotType != null
                ? AccessTools.Method(hostFileSlotType, nameof(LoadGameMenu.SaveFileSlot.Activate))
                : null;
            RegisterTogglePatch("HostFileSlot.Activate",
                hostActivate,
                new HarmonyMethod(typeof(ModToggleModule), nameof(Prefix_SaveFileSlotActivate)),
                optional: true);

            var handshakePrefix = new HarmonyMethod(typeof(ModToggleModule), nameof(Prefix_LidgrenReceiveHandshake))
            {
                priority = Priority.High
            };
            RegisterTogglePatch("ModToggle.LidgrenClient.receiveHandshake",
                AccessTools.Method(typeof(LidgrenClient), "receiveHandshake", new[] { typeof(NetIncomingMessage) }),
                handshakePrefix);
        }

        private static Func<TitleMenu, bool>? CreateShouldAllowInteractionDelegate()
        {
            System.Reflection.MethodInfo? method = AccessTools.Method(typeof(TitleMenu), "ShouldAllowInteraction");
            return method != null
                ? (Func<TitleMenu, bool>)Delegate.CreateDelegate(typeof(Func<TitleMenu, bool>), method)
                : null;
        }

        private static bool CanShowTitleToggle()
        {
            if (context == null || activeModal != null)
                return false;

            if (!string.IsNullOrEmpty(context.Config.AutoHostSaveName))
                return false;

            return Game1.activeClickableMenu is TitleMenu titleMenu &&
                   TitleMenu.subMenu == null &&
                   !titleMenu.isTransitioningButtons &&
                   (shouldAllowTitleInteraction == null || shouldAllowTitleInteraction(titleMenu));
        }

        private static Rectangle GetToggleButtonBounds(out string label)
        {
            bool enabled = context?.IsModEnabled ?? true;
            label = I18n.Get(enabled ? "mod_toggle.button_on" : "mod_toggle.button_off");
            Vector2 textSize = Game1.smallFont.MeasureString(label);
            int width = Math.Max(140, (int)Math.Ceiling(textSize.X) + 48);
            return new Rectangle(ToggleButtonX, ToggleButtonY, width, ToggleButtonHeight);
        }

        private static void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            if (e.NewMenu is not TitleMenu)
            {
                activeModal = null;
                pendingServerPromptAddress = null;
            }
        }

        private static void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (pendingServerPromptAddress == null)
                return;

            string address = pendingServerPromptAddress;
            pendingServerPromptAddress = null;

            if (context == null || context.IsModEnabled)
                return;

            OpenServerConnectModal(address);
        }

        private static void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
        {
            if (context == null || Game1.activeClickableMenu is not TitleMenu activeTitleMenu)
                return;

            if (activeModal != null)
            {
                DrawModal(e.SpriteBatch, activeModal);
                activeTitleMenu.drawMouse(e.SpriteBatch);
                return;
            }

            if (!CanShowTitleToggle())
                return;

            Rectangle bounds = GetToggleButtonBounds(out string label);
            int mouseX = Game1.getMouseX();
            int mouseY = Game1.getMouseY();
            bool hovered = bounds.Contains(mouseX, mouseY);
            bool enabled = context.IsModEnabled;

            IClickableMenu.drawTextureBox(
                e.SpriteBatch,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                hovered ? Color.Wheat : Color.White);

            var indicatorRect = new Rectangle(bounds.X + 14, bounds.Y + (bounds.Height - 14) / 2, 14, 14);
            e.SpriteBatch.Draw(Game1.staminaRect, indicatorRect, enabled ? Color.LimeGreen : Color.IndianRed);

            Vector2 textSize = Game1.smallFont.MeasureString(label);
            var textPos = new Vector2(bounds.X + 34, bounds.Y + (bounds.Height - textSize.Y) / 2f + 1f);
            Utility.drawTextWithShadow(e.SpriteBatch, label, Game1.smallFont, textPos, Game1.textColor);

            if (hovered)
            {
                string tooltip = I18n.Get(enabled ? "mod_toggle.tooltip_on" : "mod_toggle.tooltip_off");
                IClickableMenu.drawHoverText(e.SpriteBatch, tooltip, Game1.smallFont);
            }

            activeTitleMenu.drawMouse(e.SpriteBatch);
        }

        private static void DrawModal(SpriteBatch b, ActivationModal modal)
        {
            int viewportWidth = Game1.uiViewport.Width;
            int viewportHeight = Game1.uiViewport.Height;

            b.Draw(Game1.staminaRect, new Rectangle(0, 0, viewportWidth, viewportHeight), Color.Black * 0.65f);

            int boxWidth = Math.Min(680, Math.Max(420, viewportWidth - 64));
            int innerWidth = boxWidth - 64;

            string wrappedBody = Game1.parseText(modal.Body, Game1.smallFont, innerWidth);
            Vector2 titleSize = Game1.dialogueFont.MeasureString(modal.Title);
            Vector2 bodySize = Game1.smallFont.MeasureString(wrappedBody);

            const int paddingTop = 28;
            const int paddingBottom = 28;
            const int optionHeight = 52;
            const int optionSpacing = 10;

            int optionsTotalHeight = modal.Options.Count * optionHeight + Math.Max(0, modal.Options.Count - 1) * optionSpacing;
            int boxHeight = paddingTop
                + (int)Math.Ceiling(titleSize.Y)
                + 14
                + (int)Math.Ceiling(bodySize.Y)
                + 24
                + optionsTotalHeight
                + paddingBottom;

            int boxX = (viewportWidth - boxWidth) / 2;
            int boxY = (viewportHeight - boxHeight) / 2;
            modal.BoxBounds = new Rectangle(boxX, boxY, boxWidth, boxHeight);

            IClickableMenu.drawTextureBox(b, boxX, boxY, boxWidth, boxHeight, Color.White);

            int cursorY = boxY + paddingTop;
            int titleX = boxX + (boxWidth - (int)Math.Ceiling(titleSize.X)) / 2;
            Utility.drawTextWithShadow(b, modal.Title, Game1.dialogueFont, new Vector2(titleX, cursorY), Game1.textColor);

            cursorY += (int)Math.Ceiling(titleSize.Y) + 14;
            Utility.drawTextWithShadow(b, wrappedBody, Game1.smallFont, new Vector2(boxX + 32, cursorY), Game1.textColor);

            cursorY += (int)Math.Ceiling(bodySize.Y) + 24;

            modal.OptionBounds.Clear();
            int mouseX = Game1.getMouseX();
            int mouseY = Game1.getMouseY();

            foreach (ModalOption option in modal.Options)
            {
                var optionRect = new Rectangle(boxX + 32, cursorY, innerWidth, optionHeight);
                modal.OptionBounds.Add(optionRect);

                bool hovered = optionRect.Contains(mouseX, mouseY);
                IClickableMenu.drawTextureBox(
                    b,
                    optionRect.X,
                    optionRect.Y,
                    optionRect.Width,
                    optionRect.Height,
                    hovered ? Color.Wheat : Color.White);

                var accentRect = new Rectangle(optionRect.X + 14, optionRect.Y + (optionRect.Height - 14) / 2, 14, 14);
                b.Draw(Game1.staminaRect, accentRect, option.AccentColor);

                Vector2 optSize = Game1.smallFont.MeasureString(option.Label);
                var optPos = new Vector2(
                    optionRect.X + Math.Max(36, (optionRect.Width - optSize.X) / 2f),
                    optionRect.Y + (optionRect.Height - optSize.Y) / 2f + 1f);
                Utility.drawTextWithShadow(b, option.Label, Game1.smallFont, optPos, Game1.textColor);

                cursorY += optionHeight + optionSpacing;
            }
        }

        private static bool Prefix_TitleMenuReceiveLeftClick(int x, int y)
        {
            if (activeModal != null)
            {
                for (int i = 0; i < activeModal.OptionBounds.Count && i < activeModal.Options.Count; i++)
                {
                    if (!activeModal.OptionBounds[i].Contains(x, y))
                        continue;

                    Action callback = activeModal.Options[i].OnSelect;
                    callback();
                    return false;
                }

                return false;
            }

            if (CanShowTitleToggle() && context != null)
            {
                Rectangle bounds = GetToggleButtonBounds(out _);
                if (bounds.Contains(x, y))
                {
                    context.SetModEnabled(!context.IsModEnabled);
                    Game1.playSound("drumkit6");
                    return false;
                }
            }

            return true;
        }

        private static bool Prefix_BlockTitleMenuWhileModalOpen()
        {
            return activeModal == null;
        }

        private static bool Prefix_TitleMenuReceiveKeyPress(Keys key)
        {
            if (activeModal == null)
                return true;

            if (key == Keys.Escape)
            {
                CloseModalWithCancelSound();
            }

            return false;
        }

        private static bool Prefix_TitleMenuReceiveGamePadButton(Buttons button)
        {
            if (activeModal == null)
                return true;

            if (button == Buttons.B)
            {
                CloseModalWithCancelSound();
            }

            return false;
        }

        private static bool Prefix_LoadGameMenuReceiveLeftClick(LoadGameMenu __instance, int x, int y)
        {
            if (context == null || activeModal != null || bypassSaveLoadPrompt)
                return true;

            if (__instance.timerToLoad > 0 || __instance.loading || __instance.deleting || __instance.deleteConfirmationScreen)
                return true;

            for (int i = 0; i < __instance.slotButtons.Count; i++)
            {
                if (!__instance.slotButtons[i].containsPoint(x, y))
                    continue;

                if (i < __instance.deleteButtons.Count &&
                    __instance.deleteButtons[i].containsPoint(x, y))
                {
                    return true;
                }

                int slotIndex = __instance.currentItemIndex + i;
                if (slotIndex < 0 || slotIndex >= __instance.MenuSlots.Count)
                    return true;

                LoadGameMenu.MenuSlot clickedSlot = __instance.MenuSlots[slotIndex];
                if (TryInterceptSaveSlot(clickedSlot))
                    return false;

                return true;
            }

            return true;
        }

        private static bool Prefix_SaveFileSlotActivate(LoadGameMenu.SaveFileSlot __instance)
        {
            if (context == null || activeModal != null || bypassSaveLoadPrompt)
                return true;

            return !TryInterceptSaveSlot(__instance);
        }

        private static bool TryInterceptSaveSlot(LoadGameMenu.MenuSlot slot)
        {
            if (context == null)
                return false;

            bool isFarmhand = slot is FarmhandMenu.FarmhandSlot;
            if (slot is not LoadGameMenu.SaveFileSlot saveSlot || isFarmhand)
                return false;

            if (saveSlot.versionComparison < 0)
                return false;

            SaveLoadPromptDecision decision = ModActivationLogic.DecideOnSaveLoad(
                context.IsModEnabled,
                context.Config.AutoHostSaveName,
                isFarmhandSlot: false);

            if (decision != SaveLoadPromptDecision.PromptToDisableBeforeLoad)
                return false;

            Game1.playSound("smallSelect");
            OpenSaveLoadWarningModal(saveSlot);
            return true;
        }

        private static bool Prefix_LidgrenReceiveHandshake(LidgrenClient __instance, NetIncomingMessage msg)
        {
            if (context == null || context.IsModEnabled || msg == null)
                return true;

            long savedPos = msg.Position;
            byte[]? remainingBytes = null;
            long remainingBits = msg.LengthBits - savedPos;
            if (remainingBits == DeviceAuthWire.DiscoveryChallengePayloadLength * 8L)
                remainingBytes = msg.ReadBytes(DeviceAuthWire.DiscoveryChallengePayloadLength);

            msg.Position = savedPos;

            bool serverHasPelican = ModActivationLogic.HasPelicanDiscoveryChallenge(remainingBytes);
            if (ModActivationLogic.DecideOnServerConnect(context.IsModEnabled, serverHasPelican) != ServerConnectPromptDecision.PromptToEnableBeforeConnect)
                return true;

            string address = !string.IsNullOrWhiteSpace(__instance.address)
                ? __instance.address
                : (msg.SenderEndPoint?.Address?.ToString() ?? "localhost");

            __instance.disconnect(false);
            pendingServerPromptAddress = address;
            return false;
        }

        private static void OnPeerContextReceived(object? sender, PeerContextReceivedEventArgs e)
        {
            if (context == null || context.IsModEnabled || Context.IsMainPlayer || !e.Peer.IsHost)
                return;

            if (e.Peer.GetMod(context.Manifest.UniqueID) == null)
                return;

            if (ModActivationLogic.DecideOnServerConnect(context.IsModEnabled, serverHasPelican: true) != ServerConnectPromptDecision.PromptToEnableBeforeConnect)
                return;

            string address = Game1.client is LidgrenClient lidgren && !string.IsNullOrWhiteSpace(lidgren.address)
                ? lidgren.address
                : string.Empty;

            if (Game1.client != null)
            {
                Game1.client.disconnect(false);
                Game1.client = null;
            }
            else if (TitleMenu.subMenu is FarmhandMenu)
            {
                // FarmhandMenu desconectará su cliente al reemplazarse por CoopMenu.
            }

            pendingServerPromptAddress = address;
        }

        private static void OpenSaveLoadWarningModal(LoadGameMenu.SaveFileSlot saveSlot)
        {
            var options = new List<ModalOption>
            {
                new(
                    I18n.Get("mod_toggle.load_warning.disable_and_load"),
                    () =>
                    {
                        context?.SetModEnabled(false);
                        Game1.playSound("smallSelect");
                        ActivateSlotWithBypass(saveSlot);
                    },
                    Color.IndianRed),
                new(
                    I18n.Get("mod_toggle.load_warning.keep_and_load"),
                    () =>
                    {
                        Game1.playSound("smallSelect");
                        ActivateSlotWithBypass(saveSlot);
                    },
                    Color.LimeGreen),
                new(
                    I18n.Get("dialog.cancel"),
                    CloseModalWithCancelSound,
                    Color.Gray)
            };

            activeModal = new ActivationModal(
                I18n.Get("mod_toggle.load_warning.title"),
                I18n.Get("mod_toggle.load_warning.body"),
                options);
        }

        private static void OpenServerConnectModal(string address)
        {
            if (Game1.activeClickableMenu is TitleMenu)
            {
                if (TitleMenu.subMenu is FarmhandMenu)
                    TitleMenu.subMenu = new CoopMenu(false);
                else if (TitleMenu.subMenu == null)
                    TitleMenu.subMenu = new CoopMenu(false);
            }

            Game1.playSound("smallSelect");

            var options = new List<ModalOption>
            {
                new(
                    I18n.Get("mod_toggle.server_warning.enable_and_connect"),
                    () =>
                    {
                        activeModal = null;
                        context?.SetModEnabled(true);
                        Game1.playSound("coin");

                        if (!string.IsNullOrWhiteSpace(address) &&
                            context != null &&
                            Game1.activeClickableMenu is TitleMenu)
                        {
                            Multiplayer multiplayer = context.RawHelper.Reflection.GetField<Multiplayer>(typeof(Game1), "multiplayer").GetValue();
                            Client client = multiplayer.InitClient(new LidgrenClient(address));
                            TitleMenu.subMenu = new FarmhandMenu(client);
                        }
                    },
                    Color.LimeGreen),
                new(
                    I18n.Get("dialog.cancel"),
                    CloseModalWithCancelSound,
                    Color.Gray)
            };

            activeModal = new ActivationModal(
                I18n.Get("mod_toggle.server_warning.title"),
                I18n.Get("mod_toggle.server_warning.body"),
                options);
        }

        private static void ActivateSlotWithBypass(LoadGameMenu.SaveFileSlot saveSlot)
        {
            activeModal = null;
            bypassSaveLoadPrompt = true;
            try
            {
                saveSlot.Activate();
            }
            finally
            {
                bypassSaveLoadPrompt = false;
            }
        }

        private static void CloseModalWithCancelSound()
        {
            activeModal = null;
            Game1.playSound("cancel");
        }
    }
}
