using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Pelican4Ever.Core.Networking;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Network;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Exige que cada cliente tenga Pelican 4Ever con la misma versión mayor.menor que el host (solo host).
    /// La lógica de energía, comida, puertas y cama se ejecuta en el cliente, así que un cliente sin el mod
    /// rompería las reglas del servidor (ver ADR-0004).
    /// </summary>
    public sealed class ClientVersionGuardModule : Framework.IModule
    {
        private Framework.ModContext context = null!;
        private static byte modContextMessageType;

        public string Name => "ClientVersionGuard";

        public Framework.ModuleSide Side => Framework.ModuleSide.Host;

        public void Register(Framework.ModContext context)
        {
            this.context = context;
            // SMAPI 4.5.2 drops the NEW context when its peer table still contains an
            // offline connection. Repair only that stale entry before the normal handshake.
            var multiplayerType = AccessTools.TypeByName("StardewModdingAPI.Framework.SMultiplayer");
            var messageType = typeof(IModHelper).Assembly.GetTypes()
                .Single(t => t.IsEnum && t.Name == "MessageType");
            modContextMessageType = Convert.ToByte(Enum.Parse(messageType, "ModContext"));
            context.Harmony.Patch(
                AccessTools.Method(multiplayerType, "OnServerProcessingMessage"),
                prefix: new HarmonyMethod(typeof(ClientVersionGuardModule), nameof(BeforeServerMessage)));
            context.Helper.Events.Multiplayer.PeerContextReceived += OnPeerContextReceived;
        }

        private static void BeforeServerMessage(object __instance, IncomingMessage message)
        {
            if (!Context.IsMainPlayer || message.MessageType != modContextMessageType)
                return;

            var peers = (IDictionary)AccessTools.Property(__instance.GetType(), "Peers").GetValue(__instance)!;
            bool isOnline = Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == message.FarmerID);
            if (StalePeerContext.Remove(peers, message.FarmerID, isOnline))
                Framework.ModContext.Current.Monitor.Log(
                    $"[ClientVersionGuard] Contexto obsoleto eliminado para reconexión de {message.FarmerID}.", LogLevel.Debug);
        }

        private void OnPeerContextReceived(object? sender, PeerContextReceivedEventArgs e)
        {
            if (!Context.IsMainPlayer || !context.Config.RequireClientMod || e.Peer.IsHost)
                return;

            string? reason = GetRejectionReason(e.Peer);
            if (reason is null)
                return;

            context.Monitor.Log($"Expulsando al jugador {e.Peer.PlayerID}: {reason}", LogLevel.Warn);

            // Se difiere un tick para no expulsar en mitad del handshake.
            long playerId = e.Peer.PlayerID;
            context.Helper.Events.GameLoop.UpdateTicked += KickNextTick;

            void KickNextTick(object? s, UpdateTickedEventArgs args)
            {
                context.Helper.Events.GameLoop.UpdateTicked -= KickNextTick;
                Game1.server?.kick(playerId);
            }
        }

        private string? GetRejectionReason(IMultiplayerPeer peer)
        {
            if (!peer.HasSmapi)
                return "no usa SMAPI.";

            IMultiplayerPeerMod? mod = peer.Mods.FirstOrDefault(m => string.Equals(m.ID, context.Manifest.UniqueID, StringComparison.OrdinalIgnoreCase));
            if (mod is null)
                return $"no tiene instalado {context.Manifest.Name}.";

            ISemanticVersion hostVersion = context.Manifest.Version;
            if (mod.Version.MajorVersion != hostVersion.MajorVersion || mod.Version.MinorVersion != hostVersion.MinorVersion)
                return $"versión incompatible ({mod.Version}; el servidor usa {hostVersion}).";

            return null;
        }
    }
}
