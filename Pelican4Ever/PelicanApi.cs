using System;
using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Framework;
using Pelican4Ever.Modules;
using StardewModdingAPI;
using StardewValley;

namespace Pelican4Ever
{
    /// <summary>
    /// API de mod de Pelican 4Ever (<c>Helper.ModRegistry.GetApi&lt;IPelicanApi&gt;("Pelican4Ever.SVRTMP")</c>). El mod que la
    /// consuma declara su propia interfaz con los métodos que use; SMAPI hace el puente.
    ///
    /// Pensada para Stardew Voyager y PelicanChat: permite consultar renderizado, estado de activación,
    /// hora del servidor y publicar eventos de chat en <see cref="AdminApiModule.EventLog"/>.
    /// </summary>
    public sealed class PelicanApi
    {
        /// <summary>Indica si Pelican 4Ever está activo actualmente.</summary>
        public bool IsModEnabled() => ModContext.Current?.IsModEnabled ?? false;

        /// <summary>Devuelve la hora actual del servidor en formato HH:mm para sellos de chat.</summary>
        public string GetServerTimeFormatted()
        {
            if (!Context.IsWorldReady)
                return string.Empty;

            int tod = Game1.timeOfDay;
            int hours = (tod / 100) % 24;
            int mins = tod % 100;
            return $"{hours:D2}:{mins:D2}";
        }

        /// <summary>Publica un mensaje de chat saliente en el EventLog del host (consumido por Discord/Putre).</summary>
        public void PublishChatEvent(string channel, string senderName, string message)
        {
            if (!Context.IsMainPlayer || string.IsNullOrWhiteSpace(message))
                return;

            int today = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;
            int time = Context.IsWorldReady ? Game1.timeOfDay : 600;
            string safeChannel = string.IsNullOrWhiteSpace(channel) ? "global" : channel.Trim().ToLowerInvariant();
            string safeSender = string.IsNullOrWhiteSpace(senderName) ? "Farmer" : senderName.Trim();

            AdminApiModule.EventLog.Append(
                HostEventTypes.ChatMessage,
                today,
                time,
                $"[{safeChannel}] {safeSender}: {message}",
                new Dictionary<string, object?>
                {
                    ["channel"] = safeChannel,
                    ["senderName"] = safeSender,
                    ["message"] = message
                });
        }

        /// <summary>Pide que el host dibuje durante <paramref name="minutes"/> minutos (1-120). Devuelve la caducidad (UTC) o null si no se concede.</summary>
        public DateTime? RequestRendering(string requester, int minutes)
        {
            if (minutes <= 0)
                return null;
            var status = HeadlessRenderModule.RequestRendering(requester, minutes);
            return status.Leases.FirstOrDefault(l => string.Equals(l.Requester, requester, StringComparison.OrdinalIgnoreCase))?.ExpiresAtUtc;
        }

        /// <summary>Retira el préstamo de dibujado de <paramref name="requester"/>.</summary>
        public void ReleaseRendering(string requester) => HeadlessRenderModule.RequestRendering(requester, 0);

        /// <summary>Si el host está dibujando ahora.</summary>
        public bool IsHostDrawing() => HeadlessRenderModule.GetStatus().Drawing;

        /// <summary>
        /// Registra un callback síncrono invocado antes de <c>SaveGame.Save()</c> en checkpoints y rollovers
        /// donde SMAPI no dispara <c>GameLoop.Saving</c> (TASK-2601 / CP-604).
        /// </summary>
        public void RegisterPreSaveHook(Action callback) => PersistenceModule.PreSaveHooks.Register(callback);

        /// <summary>Retira un callback previamente registrado con <see cref="RegisterPreSaveHook"/>.</summary>
        public void UnregisterPreSaveHook(Action callback) => PersistenceModule.PreSaveHooks.Unregister(callback);
    }
}
