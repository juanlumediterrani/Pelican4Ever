using System;
using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Servidor desatendido sin dibujado (<c>HeadlessRendering</c>). Sin GPU, el host dibuja por CPU (llvmpipe) y ocupa
    /// núcleos enteros aunque nadie mire su pantalla. La simulación y la red van por <c>Update</c>; cada cliente dibuja lo
    /// suyo, así que el host puede saltarse <c>Draw</c> con <c>Game.SuppressDraw()</c> sin cambiar la frecuencia del juego
    /// (a diferencia de bajar los FPS, que frena a los NPC: Pelican-4Ever#4).
    ///
    /// Se dibuja igualmente cuando el juego lo necesita o alguien lo pide:
    /// <list type="bullet">
    /// <item>en el título y la carga (antes de que el mundo esté listo);</item>
    /// <item>con <c>SaveGameMenu</c> abierto: su <c>update</c> no empieza a guardar hasta que <c>draw</c> pone
    /// <c>hasDrawn</c>; sin esto el guardado nocturno nunca arrancaría;</item>
    /// <item>mientras haya un préstamo de dibujado (API HTTP, consola o API de mod para Stardew Voyager).</item>
    /// </list>
    /// Las capturas de <c>takeMapScreenshot</c> (las del puente de Voyager) dibujan en su propio render target y no
    /// necesitan el bucle de dibujado.
    /// </summary>
    public sealed class HeadlessRenderModule : IModule
    {
        public const string Command = "p4e_render";

        /// <summary>Préstamos vigentes. Seguro entre hilos (API HTTP, consola, hilo del juego, otros mods).</summary>
        public static HostRenderLeaseRegistry Leases { get; } = new();

        private static ModContext? context;
        private static HostRenderReason lastReason = HostRenderReason.WorldNotReady;
        private static bool initialized;
        private static bool leaseActive;

        // Fecha del juego vista en el último tick: los sucesos pueden registrarse desde otros hilos y Game1 no es seguro fuera
        // del hilo del juego.
        private static int lastGameDay;
        private static int lastGameTime;

        public string Name => "HeadlessRender";

        public ModuleSide Side => ModuleSide.Host;

        public void Register(ModContext ctx)
        {
            context = ctx;
            ctx.Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            ctx.AddCommand(
                Command,
                "Dibujado del host en el servidor desatendido. Uso: p4e_render [status] | <minutos 1-120> | off",
                OnCommand);
        }

        /// <summary>Estado actual para la API y la consola.</summary>
        public static HostRenderStatusDto GetStatus(string message = "")
        {
            DateTime now = DateTime.UtcNow;
            return new HostRenderStatusDto
            {
                HeadlessRenderingEnabled = context?.Config.HeadlessRendering ?? false,
                Drawing = HostRenderPolicy.ShouldDraw(lastReason),
                Reason = lastReason.ToString(),
                Leases = Leases.Active(now)
                    .Select(l => new HostRenderLeaseDto { Requester = l.Requester, ExpiresAtUtc = l.ExpiresAtUtc })
                    .ToList(),
                Message = message
            };
        }

        /// <summary>Pide (minutos &gt; 0) o retira (minutos = 0) un préstamo de dibujado. Seguro desde cualquier hilo.</summary>
        public static HostRenderStatusDto RequestRendering(string requester, int minutes)
        {
            if (!HostRenderLeaseRegistry.IsValidRequester(requester))
                return GetStatus("Solicitante no válido: 1-40 caracteres [A-Za-z0-9._-].");

            if (minutes <= 0)
            {
                bool existed = Leases.Release(requester);
                leaseActive = Leases.HasActive(DateTime.UtcNow);
                if (existed)
                    LogLease(HostEventTypes.RenderLeaseReleased, $"Préstamo de dibujado retirado: {requester}", requester, null);
                return GetStatus(existed ? $"Préstamo de '{requester}' retirado." : $"'{requester}' no tenía préstamo.");
            }

            HostRenderLease lease = Leases.Grant(requester, minutes, DateTime.UtcNow);
            leaseActive = true;
            LogLease(HostEventTypes.RenderLeaseGranted, $"Préstamo de dibujado para {requester} hasta {lease.ExpiresAtUtc:HH:mm} UTC", requester, lease.ExpiresAtUtc);
            return GetStatus($"Dibujado prestado a '{requester}' hasta {lease.ExpiresAtUtc:O}.");
        }

        private static void LogLease(string type, string summary, string requester, DateTime? expiresAtUtc)
        {
            context?.Monitor.Log($"[HeadlessRender] {summary}.", LogLevel.Info);
            AdminApiModule.EventLog.Append(
                type,
                lastGameDay,
                lastGameTime,
                summary,
                new Dictionary<string, object?> { ["requester"] = requester, ["expiresAtUtc"] = expiresAtUtc });
        }

        private static void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (context == null || !Context.IsMainPlayer && Context.IsWorldReady)
                return;

            if (Context.IsWorldReady)
            {
                lastGameDay = Game1.Date.TotalDays;
                lastGameTime = Game1.timeOfDay;
            }

            // Comprobar préstamos cada segundo basta (caducan por minutos) y evita bloquear el registro 60 veces por segundo.
            if (e.IsMultipleOf(60))
                leaseActive = Leases.HasActive(DateTime.UtcNow);

            HostRenderReason reason = HostRenderPolicy.Decide(
                headlessEnabled: context.Config.HeadlessRendering,
                unattended: !string.IsNullOrEmpty(context.Config.AutoHostSaveName),
                worldReady: Context.IsWorldReady,
                drawRequiredMenuOpen: Game1.activeClickableMenu is SaveGameMenu,
                hasActiveLease: leaseActive);

            if (!initialized || reason != lastReason)
            {
                bool wasDrawing = HostRenderPolicy.ShouldDraw(lastReason);
                bool drawing = HostRenderPolicy.ShouldDraw(reason);
                if (!initialized || wasDrawing != drawing)
                    context.Monitor.Log($"[HeadlessRender] {(drawing ? "Dibujando" : "Sin dibujar")} ({reason}).", LogLevel.Info);
                lastReason = reason;
                initialized = true;
            }

            // SuppressDraw solo vale para el siguiente Draw: hay que pedirlo en cada tick. GameRunner es el Game de MonoGame
            // (Game1 es una instancia de pantalla dentro de él).
            if (!HostRenderPolicy.ShouldDraw(reason))
                GameRunner.instance?.SuppressDraw();
        }

        private void OnCommand(string command, string[] args)
        {
            if (context == null)
                return;

            string arg = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "status";
            HostRenderStatusDto status;
            if (arg is "status" or "estado")
                status = GetStatus();
            else if (arg is "off" or "0")
                status = RequestRendering("console", 0);
            else if (int.TryParse(arg, out int minutes) && minutes > 0)
                status = RequestRendering("console", minutes);
            else
            {
                context.Monitor.Log($"Uso: {Command} [status] | <minutos 1-{HostRenderLeaseRegistry.MaxMinutes}> | off", LogLevel.Warn);
                return;
            }

            string leases = status.Leases.Count == 0
                ? "ninguno"
                : string.Join(", ", status.Leases.Select(l => $"{l.Requester} hasta {l.ExpiresAtUtc:HH:mm} UTC"));
            context.Monitor.Log(
                $"[HeadlessRender] {(status.Drawing ? "Dibujando" : "Sin dibujar")} ({status.Reason}); HeadlessRendering={status.HeadlessRenderingEnabled}; préstamos: {leases}."
                + (string.IsNullOrEmpty(status.Message) ? string.Empty : $" {status.Message}"),
                LogLevel.Info);
        }
    }
}
