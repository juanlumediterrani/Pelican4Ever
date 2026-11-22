using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Chat;
using Pelican4Ever.Framework;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Network;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Gestiona canales de chat: proximidad/local (/l), global (/g) y susurros privados (/w) (TASK-502).
    /// </summary>
    public sealed class ChatChannelModule : IModule
    {
        private static ModContext? context;
        private static bool commandsRegistered;
        private static readonly FieldInfo? MultiplayerField = AccessTools.Field(typeof(Game1), "multiplayer");

        public string Name => "ChatChannel";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            ctx.Helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;

            ApplyPrivacyPatches(ctx);
        }

        private void ApplyPrivacyPatches(ModContext ctx)
        {
            ctx.Patch(
                "LidgrenServer.getUserName",
                Name,
                PatchCriticality.Security,
                AccessTools.Method(typeof(LidgrenServer), nameof(LidgrenServer.getUserName), new[] { typeof(long) }),
                postfix: new HarmonyMethod(typeof(ChatChannelModule), nameof(Postfix_LidgrenServer_GetUserName)));

            ctx.Patch(
                "ChatBox.formattedUserNameLong",
                Name,
                PatchCriticality.Security,
                AccessTools.Method(typeof(ChatBox), nameof(ChatBox.formattedUserNameLong), new[] { typeof(Farmer) }),
                postfix: new HarmonyMethod(typeof(ChatChannelModule), nameof(Postfix_ChatBox_FormattedUserNameLong)));
        }

        private static void Postfix_LidgrenServer_GetUserName(ref string? __result)
        {
            if (context == null || !context.IsModEnabled || !context.Config.HideClientIpInChat)
                return;

            if (!string.IsNullOrEmpty(__result))
                __result = string.Empty;
        }

        private static void Postfix_ChatBox_FormattedUserNameLong(Farmer? farmer, ref string __result)
        {
            if (farmer == null || context == null || !context.IsModEnabled || !context.Config.HideClientIpInChat)
                return;

            var multiplayer = MultiplayerField?.GetValue(null) as Multiplayer;
            string? userName = multiplayer?.getUserName(farmer.UniqueMultiplayerID);
            if (ChatIdentityPrivacyLogic.IsIpAddressLabel(userName))
            {
                __result = ChatBox.formattedUserName(farmer);
            }
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            RegisterChatCommands();
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            RegisterChatCommands();
        }

        private static void RegisterChatCommands()
        {
            if (commandsRegistered)
                return;

            try
            {
                // Alias /w para susurros privados nativos (/message)
                if (!ChatCommands.Exists("w"))
                {
                    ChatCommands.RegisterAlias("w", "message");
                }

                // Comando /l y /local para chat de proximidad
                if (!ChatCommands.Exists("local"))
                {
                    ChatCommands.Register(
                        "local",
                        HandleLocalChatCommand,
                        name => I18n.Get("chat.local_help"),
                        new[] { "l" },
                        mainOnly: false,
                        multiplayerOnly: true,
                        cheatsOnly: false
                    );
                }

                // Comando /g y /global para chat global
                if (!ChatCommands.Exists("global"))
                {
                    ChatCommands.Register(
                        "global",
                        HandleGlobalChatCommand,
                        name => I18n.Get("chat.global_help"),
                        new[] { "g" },
                        mainOnly: false,
                        multiplayerOnly: true,
                        cheatsOnly: false
                    );
                }

                commandsRegistered = true;
                context?.Monitor.Log("[ChatChannelModule] Comandos /l, /g y /w registrados en ChatCommands.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[ChatChannelModule] Error registrando comandos de chat: {ex}", LogLevel.Warn);
            }
        }

        private static void HandleLocalChatCommand(string[] command, ChatBox chat)
        {
            if (command.Length <= 1)
            {
                chat.addInfoMessage(I18n.Get("chat.local_usage"));
                return;
            }

            string text = string.Join(" ", command.Skip(1));
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (context == null) return;

            string locationName = Game1.currentLocation?.NameOrUniqueName ?? string.Empty;
            float tileX = Game1.player.TilePoint.X;
            float tileY = Game1.player.TilePoint.Y;

            var payload = new LocalChatMessagePayload
            {
                SenderId = Game1.player.UniqueMultiplayerID,
                SenderName = Game1.player.Name,
                LocationName = locationName,
                TileX = tileX,
                TileY = tileY,
                Message = text
            };

            // Transmitir a todos los clientes mediante el bus de mensajes SMAPI
            context.Helper.Multiplayer.SendMessage(
                payload,
                ChatMessages.LocalChat,
                new[] { context.Manifest.UniqueID }
            );

            // Mostrar inmediatamente en la interfaz del emisor (color verde claro de proximidad)
            string localFormatted = ChatProximityLogic.FormatLocalMessage(payload.SenderName, payload.Message);
            Game1.chatBox?.addMessage(localFormatted, new Color(130, 230, 130));
        }

        private static void HandleGlobalChatCommand(string[] command, ChatBox chat)
        {
            if (command.Length <= 1)
            {
                chat.addInfoMessage(I18n.Get("chat.global_usage"));
                return;
            }

            string text = string.Join(" ", command.Skip(1));
            if (string.IsNullOrWhiteSpace(text))
                return;

            // Transmitir al chat global estándar a través de ChatBox
            chat.textBoxEnter(text);
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || e.FromModID != context.Manifest.UniqueID)
                return;

            if (e.Type == ChatMessages.LocalChat)
            {
                var payload = e.ReadAs<LocalChatMessagePayload>();
                if (payload == null)
                    return;

                // Evitar duplicar el mensaje en el emisor que ya lo pintó localmente
                if (payload.SenderId == Game1.player.UniqueMultiplayerID)
                    return;

                string myLoc = Game1.currentLocation?.NameOrUniqueName ?? string.Empty;
                float myX = Game1.player.TilePoint.X;
                float myY = Game1.player.TilePoint.Y;

                bool inRange = ChatProximityLogic.IsWithinProximity(
                    payload.LocationName,
                    payload.TileX,
                    payload.TileY,
                    myLoc,
                    myX,
                    myY,
                    ChatProximityLogic.DefaultProximityRadius
                );

                if (inRange)
                {
                    string localFormatted = ChatProximityLogic.FormatLocalMessage(payload.SenderName, payload.Message);
                    Game1.chatBox?.addMessage(localFormatted, new Color(130, 230, 130));
                }
            }
            else if (e.Type == ChatMessages.ServerBroadcast)
            {
                // Si PelicanChat está cargado, su ChatTransportModule gestiona ServerBroadcast con sanitización y pestañas
                if (context.Helper.ModRegistry.IsLoaded("Pelican4Ever.Chat"))
                    return;

                var broadcast = e.ReadAs<ServerBroadcastPayload>();
                if (broadcast == null || string.IsNullOrWhiteSpace(broadcast.Message))
                    return;

                string text = string.Equals(broadcast.Source, "discord", StringComparison.OrdinalIgnoreCase)
                    ? $"[Discord] {broadcast.Author}: {broadcast.Message}"
                    : broadcast.Message;

                Game1.chatBox?.addMessage(text, new Color(broadcast.ColorR, broadcast.ColorG, broadcast.ColorB));
            }
        }
    }
}
