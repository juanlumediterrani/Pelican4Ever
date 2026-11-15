using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Core.Admin;
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
    /// rompería las reglas del servidor (ver ADR-0004). Con <c>RequiredClientMods</c> exige además el resto del
    /// modpack en la versión exacta que tiene el host: un cliente sin NITV, Fashion Sense, etc. vería objetos rotos.
    /// </summary>
    public sealed class ClientVersionGuardModule : Framework.IModule
    {
        private Framework.ModContext context = null!;
        private static byte modContextMessageType;

        /// <summary>UniqueID → versión instalada en el host de cada mod de <c>RequiredClientMods</c>.</summary>
        private List<KeyValuePair<string, string>> requiredMods = new();

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
            context.Helper.Events.GameLoop.GameLaunched += (_, _) => ResolveRequiredMods();
        }

        /// <summary>Fija la versión exigida de cada mod del modpack a la que tiene instalada el host.</summary>
        private void ResolveRequiredMods()
        {
            requiredMods = new List<KeyValuePair<string, string>>();
            foreach (string id in context.Config.RequiredClientMods.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                IModInfo? installed = context.Helper.ModRegistry.Get(id);
                if (installed == null)
                {
                    context.Monitor.Log($"[ClientVersionGuard] RequiredClientMods: '{id}' no está instalado en el servidor; no se exige.", LogLevel.Warn);
                    continue;
                }

                requiredMods.Add(new KeyValuePair<string, string>(installed.Manifest.UniqueID, installed.Manifest.Version.ToString()));
            }

            if (requiredMods.Count > 0)
                context.Monitor.Log($"[ClientVersionGuard] Modpack exigido a los clientes: {string.Join(", ", requiredMods.Select(m => $"{m.Key} {m.Value}"))}.", LogLevel.Info);
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
            AdminApiModule.EventLog.Append(
                HostEventTypes.PlayerRejected,
                Game1.Date.TotalDays,
                Game1.timeOfDay,
                $"Jugador {e.Peer.PlayerID} expulsado: {reason}",
                new Dictionary<string, object?> { ["playerId"] = e.Peer.PlayerID, ["reason"] = reason });

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

            if (requiredMods.Count > 0)
            {
                var clientMods = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (IMultiplayerPeerMod m in peer.Mods)
                    clientMods[m.ID] = m.Version.ToString();
                var problems = ClientModRequirements.Check(requiredMods, clientMods);
                if (problems.Count > 0)
                    return ClientModRequirements.DescribeRejection(problems);
            }

            return null;
        }
    }
}
