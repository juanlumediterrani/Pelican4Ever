using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Chat;
using Pelican4Ever.Framework;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Gestiona canales de chat: proximidad/local (/l), global (/g) y susurros privados (/w) (TASK-502).
    /// </summary>
    public sealed class ChatChannelModule : IModule
    {
        private static ModContext? context;
        private static bool commandsRegistered;

        public string Name => "ChatChannel";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            ctx.Helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
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
                        name => "Enviar mensaje al canal de proximidad local (/l o /local <mensaje>)",
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
                        name => "Enviar mensaje al canal global (/g o /global <mensaje>)",
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
                chat.addInfoMessage("Uso: /l <mensaje> o /local <mensaje>");
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
                chat.addInfoMessage("Uso: /g <mensaje> o /global <mensaje>");
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
        }
    }
}
