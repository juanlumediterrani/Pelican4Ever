using System;
using System.IO;
using Pelican4Ever.Core.Server;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Arranque desatendido del servidor persistente: en la pantalla de título carga como anfitrión de cooperativo
    /// la partida de <c>AutoHostSaveName</c>, igual que la ranura «Anfitrión» de CoopMenu (<c>multiplayerMode = 2</c>
    /// y <c>SaveGame.Load</c>); el servidor de red lo abre el juego en el primer tick con la partida cargada.
    /// También cierra el proceso tras un reinicio controlado si <c>RestartExitsProcess</c> lo pide.
    /// </summary>
    public sealed class AutoHostModule : IModule
    {
        private static ModContext? context;

        /// <summary>Un intento por visita a la pantalla de título: si la carga falla, no se reintenta en bucle.</summary>
        private static bool attemptPending = true;

        private static int titleTicks;

        /// <summary>Lo pide el reinicio controlado: al volver al título, cerrar el juego en vez de recargar.</summary>
        private static bool exitProcessOnTitle;

        public string Name => "AutoHost";

        public ModuleSide Side => ModuleSide.Host;

        public void Register(ModContext ctx)
        {
            context = ctx;
            if (string.IsNullOrEmpty(ctx.Config.AutoHostSaveName))
                return;

            ctx.Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            ctx.Monitor.Log($"[AutoHost] Servidor desatendido: cargará '{ctx.Config.AutoHostSaveName}' como anfitrión al llegar al título.", LogLevel.Info);
        }

        /// <summary>
        /// Llamado por el reinicio controlado justo antes de volver al título. Devuelve true si el proceso se cerrará
        /// (para que el supervisor lo relance con el mod actual); false si la carga automática reabrirá la partida.
        /// </summary>
        public static bool PrepareControlledRestart()
        {
            if (context == null)
                return false;

            exitProcessOnTitle = AutoHostLogic.ShouldExitProcessOnRestart(context.Config.RestartExitsProcess, context.Config.AutoHostSaveName);
            return exitProcessOnTitle;
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            titleTicks = 0;
            attemptPending = true;
            if (exitProcessOnTitle)
            {
                context?.Monitor.Log("[AutoHost] Reinicio controlado: cerrando el juego para que el supervisor lo relance.", LogLevel.Warn);
                Game1.quit = true;
            }
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (context == null || !attemptPending || exitProcessOnTitle)
                return;

            if (Context.IsWorldReady || Game1.activeClickableMenu is not TitleMenu || TitleMenu.subMenu != null)
            {
                titleTicks = 0;
                return;
            }

            if (++titleTicks < AutoHostLogic.TitleSettleSeconds * 60)
                return;

            attemptPending = false;
            string saveName = context.Config.AutoHostSaveName;
            AutoHostDecision decision = AutoHostLogic.Decide(saveName, name => Directory.Exists(Path.Combine(Constants.SavesPath, name)));
            if (decision != AutoHostDecision.Load)
            {
                context.Monitor.Log($"[AutoHost] No se carga la partida '{saveName}': {decision}. El servidor queda en la pantalla de título.", LogLevel.Error);
                return;
            }

            try
            {
                context.Monitor.Log($"[AutoHost] Cargando '{saveName}' como anfitrión de cooperativo.", LogLevel.Info);
                Game1.multiplayerMode = 2;
                SaveGame.Load(saveName);
                Game1.exitActiveMenu();
            }
            catch (Exception ex)
            {
                Game1.multiplayerMode = 0;
                context.Monitor.Log($"[AutoHost] Error cargando '{saveName}' como anfitrión: {ex}", LogLevel.Error);
            }
        }
    }
}
