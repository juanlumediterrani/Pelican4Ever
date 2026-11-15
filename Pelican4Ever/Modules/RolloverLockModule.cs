using System;
using System.Collections.Generic;
using Pelican4Ever.Core.Admin;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Bloqueo de conexiones durante el Rollover diario (solo host): mientras la secuencia diaria no
    /// está en reposo (guardado pre-cierre, cierre ordenado de peers, avance de día y guardado
    /// posterior) rechaza a todo peer que intente entrar, para que una reconexión no compita con el
    /// guardado ni con el cambio de día. Es un bloqueo por evento (estado real del Rollover), no por
    /// reloj: el mantenimiento del runtime lo gestiona el supervisor con el servicio apagado
    /// (<c>RestartExitsProcess</c>), que mientras tanto no acepta conexiones de todos modos.
    /// </summary>
    public sealed class RolloverLockModule : Framework.IModule
    {
        /// <summary>Margen para fusionar el contexto recibido y la conexión del mismo intento de peer.</summary>
        private static readonly TimeSpan DuplicateRejectGrace = TimeSpan.FromSeconds(15);

        private Framework.ModContext context = null!;
        private readonly Dictionary<long, DateTimeOffset> lastRejectedUtc = new();

        public string Name => "RolloverLock";

        public Framework.ModuleSide Side => Framework.ModuleSide.Host;

        public void Register(Framework.ModContext context)
        {
            this.context = context;
            context.Helper.Events.Multiplayer.PeerContextReceived += OnPeerContextReceived;
            // Red de seguridad: si el contexto no llegó a tiempo, se corta al conectarse.
            context.Helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
        }

        private void OnPeerContextReceived(object? sender, PeerContextReceivedEventArgs e) => RejectIfRolloverBusy(e.Peer);

        private void OnPeerConnected(object? sender, PeerConnectedEventArgs e) => RejectIfRolloverBusy(e.Peer);

        private void RejectIfRolloverBusy(IMultiplayerPeer peer)
        {
            if (!Context.IsMainPlayer || !Context.IsWorldReady || peer.IsHost)
                return;
            if (!DailyRolloverModule.IsBusy)
                return;

            // PeerContextReceived y PeerConnected llegan para el mismo intento: un solo aviso y una sola expulsión.
            DateTimeOffset now = DateTimeOffset.UtcNow;
            PruneRejectHistory(now);
            if (lastRejectedUtc.TryGetValue(peer.PlayerID, out DateTimeOffset previous)
                && now - previous < DuplicateRejectGrace)
                return;
            lastRejectedUtc[peer.PlayerID] = now;

            context.Monitor.Log($"[RolloverLock] Rollover en curso (guardado/cambio de día); se rechaza la conexión del jugador {peer.PlayerID}.", LogLevel.Warn);
            AdminApiModule.EventLog.Append(
                HostEventTypes.PlayerRejected,
                Game1.Date.TotalDays,
                Game1.timeOfDay,
                $"Jugador {peer.PlayerID} rechazado: Rollover en curso",
                new Dictionary<string, object?> { ["playerId"] = peer.PlayerID, ["reason"] = "rollover_in_progress" });

            // Se difiere un tick para no expulsar en mitad del handshake.
            long playerId = peer.PlayerID;
            context.Helper.Events.GameLoop.UpdateTicked += KickNextTick;

            void KickNextTick(object? s, UpdateTickedEventArgs args)
            {
                context.Helper.Events.GameLoop.UpdateTicked -= KickNextTick;
                Game1.server?.kick(playerId);
            }
        }

        private void PruneRejectHistory(DateTimeOffset now)
        {
            List<long>? stale = null;
            foreach (KeyValuePair<long, DateTimeOffset> entry in lastRejectedUtc)
            {
                if (now - entry.Value >= TimeSpan.FromMinutes(2))
                    (stale ??= new List<long>()).Add(entry.Key);
            }
            if (stale == null)
                return;
            foreach (long playerId in stale)
                lastRejectedUtc.Remove(playerId);
        }
    }
}
