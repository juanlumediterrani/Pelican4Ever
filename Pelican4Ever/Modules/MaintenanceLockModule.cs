using System;
using Pelican4Ever.Core.Time;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Modo mantenimiento (solo host): entre el cambio de fecha (02:00) y el final de la ventana
    /// (<c>MaintenanceWindowMinutes</c>, 25 por defecto) rechaza a todo peer que intente entrar.
    /// Así el guardado, la copia, el avance de día y la actualización del runtime no compiten con reconexiones.
    /// </summary>
    public sealed class MaintenanceLockModule : Framework.IModule
    {
        private Framework.ModContext context = null!;

        public string Name => "MaintenanceLock";

        public Framework.ModuleSide Side => Framework.ModuleSide.Host;

        public void Register(Framework.ModContext context)
        {
            this.context = context;
            context.Helper.Events.Multiplayer.PeerContextReceived += OnPeerContextReceived;
            context.Helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
        }

        private void OnPeerContextReceived(object? sender, PeerContextReceivedEventArgs e) => RejectIfInWindow(e.Peer);

        // Red de seguridad: si el contexto no llegó a tiempo, se corta al conectarse.
        private void OnPeerConnected(object? sender, PeerConnectedEventArgs e) => RejectIfInWindow(e.Peer);

        private void RejectIfInWindow(IMultiplayerPeer peer)
        {
            if (!Context.IsMainPlayer || peer.IsHost)
                return;

            var cfg = context.Config;
            TimeSpan now = ServerTime.LocalNow(cfg.TimeZoneId).TimeOfDay;
            if (!MaintenanceWindow.IsActive(now, cfg.ParsedCalendarDayChangeTime, cfg.MaintenanceWindowMinutes))
                return;

            TimeSpan end = MaintenanceWindow.EndsAt(cfg.ParsedCalendarDayChangeTime, cfg.MaintenanceWindowMinutes);
            context.Monitor.Log($"[MaintenanceLock] Servidor en mantenimiento hasta las {end:hh\\:mm}; se rechaza la conexión del jugador {peer.PlayerID}.", LogLevel.Warn);

            // Se difiere un tick para no expulsar en mitad del handshake.
            long playerId = peer.PlayerID;
            context.Helper.Events.GameLoop.UpdateTicked += KickNextTick;

            void KickNextTick(object? s, UpdateTickedEventArgs args)
            {
                context.Helper.Events.GameLoop.UpdateTicked -= KickNextTick;
                Game1.server?.kick(playerId);
            }
        }
    }
}
