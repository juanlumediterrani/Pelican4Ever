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
    /// Antes de cargar comprueba que el puerto UDP 24642 esté libre (lo fija el juego y solo admite una instancia
    /// alojando). También cierra el proceso tras un reinicio controlado si <c>RestartExitsProcess</c> lo pide.
    /// </summary>
    public sealed class AutoHostModule : IModule
    {
        private static ModContext? context;

        /// <summary>Un intento por visita a la pantalla de título: si la carga falla, no se reintenta en bucle.</summary>
        private static bool attemptPending = true;

        private static int titleTicks;

        /// <summary>Para loguear la ocupación del puerto una sola vez por visita a la pantalla de título.</summary>
        private static bool portOccupationLogged;

        /// <summary>Quién tiene el puerto UDP de juego, detectado al primer fallo de disponibilidad.</summary>
        private static PortOccupant? portOccupant;

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
            ResetTitleAttempt();
            attemptPending = true;
            if (exitProcessOnTitle)
            {
                context?.Monitor.Log("[AutoHost] Reinicio controlado: cerrando el juego para que el supervisor lo relance.", LogLevel.Warn);
                Game1.quit = true;
            }
        }

        private static void ResetTitleAttempt()
        {
            titleTicks = 0;
            portOccupationLogged = false;
            portOccupant = null;
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (context == null || !attemptPending || exitProcessOnTitle)
                return;

            if (Context.IsWorldReady || Game1.activeClickableMenu is not TitleMenu || TitleMenu.subMenu != null)
            {
                ResetTitleAttempt();
                return;
            }

            if (++titleTicks < AutoHostLogic.TitleSettleSeconds * 60)
                return;

            // El juego fija el puerto UDP 24642 al alojar: cargar con el puerto ocupado (otra instancia
            // o el propio proceso tras un stopServer fallido) acaba en "Address already in use" y en un
            // bucle de NullReferenceException al volver al título. Comprobar antes de cargar.
            int waitedSeconds = (titleTicks - AutoHostLogic.TitleSettleSeconds * 60) / 60;
            PortGateDecision gate = AutoHostLogic.DecidePortGate(GamePortProbe.IsAvailable(), waitedSeconds);
            if (gate == PortGateDecision.KeepWaiting)
            {
                if (!portOccupationLogged)
                {
                    portOccupationLogged = true;
                    portOccupant = GamePortProbe.FindOccupant();
                    context.Monitor.Log(
                        $"[AutoHost] El puerto UDP {GamePortProbe.GameHostPort} está ocupado por {portOccupant?.ToString() ?? "un proceso no identificable"}; esperando hasta {AutoHostLogic.PortWaitSeconds} s a que se libere antes de cargar.",
                        LogLevel.Warn);
                }
                return;
            }

            if (gate == PortGateDecision.GiveUp)
            {
                attemptPending = false;
                portOccupationLogged = true;
                portOccupant ??= GamePortProbe.FindOccupant();
                context.Monitor.Log(
                    $"[AutoHost] El puerto UDP {GamePortProbe.GameHostPort} sigue ocupado por {portOccupant?.ToString() ?? "un proceso no identificable"} tras {AutoHostLogic.PortWaitSeconds} s; no se carga la partida. Revisa 'ss -lunp | grep {GamePortProbe.GameHostPort}'.",
                    LogLevel.Error);
                if (portOccupant != null && portOccupant.Value.Pid == Environment.ProcessId && context.Config.RestartExitsProcess)
                {
                    context.Monitor.Log("[AutoHost] El puerto lo mantiene este mismo proceso (stopServer falló al volver al título); se cierra para que el supervisor lo relance limpio.", LogLevel.Error);
                    Game1.quit = true;
                }
                return;
            }

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
