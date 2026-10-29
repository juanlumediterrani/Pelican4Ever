using System;
using System.Linq;
using HarmonyLib;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Permite al jugador meterse en la cama, seleccionar "Descansar y Desconectar",
    /// registrar su estado Rested en el Host y salir al menú sin bloquear al resto (TASK-102 / ADR-0004).
    /// </summary>
    public sealed class BedLogoutModule : IModule
    {
        private static ModContext? context;
        public const string BedLogoutMessageType = "BedLogoutRequest";

        public string Name => "BedLogout";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            // En el Host escuchamos mensajes de clientes pidiendo desconexión tras descansar en la cama
            if (Context.IsMainPlayer)
            {
                ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
                ctx.Helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
            }

            ApplyHarmonyPatches(ctx.Harmony);
        }

        private void OnPeerConnected(object? sender, StardewModdingAPI.Events.PeerConnectedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null)
                return;

            // Al conectar un jugador, marcamos su actividad en el día actual
            var player = context.WorldState.GetOrCreatePlayer(e.Peer.PlayerID, string.Empty);
            player.LastActiveGameDay = Game1.Date.TotalDays;
            player.EnsureDailyCounters(Game1.Date.TotalDays);
            context.Monitor.Log($"[BedLogoutModule] Jugador {e.Peer.PlayerID} conectado. Marcado activo para día {Game1.Date.TotalDays}.", LogLevel.Trace);
        }

        private void OnModMessageReceived(object? sender, StardewModdingAPI.Events.ModMessageReceivedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null)
                return;

            if (e.FromModID == context.Manifest.UniqueID && e.Type == BedLogoutMessageType)
            {
                long playerId = e.FromPlayerID;
                var record = context.WorldState.GetOrCreatePlayer(playerId, string.Empty);
                record.RestedOnGameDay = Game1.Date.TotalDays;
                record.LastActiveGameDay = Game1.Date.TotalDays;
                context.Monitor.Log($"[BedLogoutModule] Descanso registrado para jugador {playerId} en día {Game1.Date.TotalDays}.", LogLevel.Info);
            }
        }

        private void ApplyHarmonyPatches(Harmony harmony)
        {
            try
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(GameLocation), nameof(GameLocation.answerDialogueAction)),
                    prefix: new HarmonyMethod(typeof(BedLogoutModule), nameof(Prefix_AnswerDialogueAction))
                );

                context?.Monitor.Log("[BedLogoutModule] Harmony patch en GameLocation.answerDialogueAction aplicado.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[BedLogoutModule] Error al aplicar parches Harmony: {ex}", LogLevel.Error);
            }
        }

        public static bool Prefix_AnswerDialogueAction(string questionAndAnswer, ref bool __result)
        {
            if (questionAndAnswer == "Sleep_Yes")
            {
                // Si estamos en un servidor con Pelican 4Ever activo
                if (Game1.IsMultiplayer && context != null)
                {
                    // Enviar mensaje al Host para registrar el descanso
                    context.Helper.Multiplayer.SendMessage(
                        message: Game1.Date.TotalDays,
                        messageType: BedLogoutMessageType,
                        modIDs: new[] { context.Manifest.UniqueID },
                        playerIDs: new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                    );

                    // Si es el propio Host quien duerme, registrarlo directamente
                    if (Context.IsMainPlayer)
                    {
                        var record = context.WorldState.GetOrCreatePlayer(Game1.player.UniqueMultiplayerID, Game1.player.Name);
                        record.RestedOnGameDay = Game1.Date.TotalDays;
                        record.LastActiveGameDay = Game1.Date.TotalDays;
                    }

                    Game1.addHUDMessage(new HUDMessage("Descanso registrado. Desconectando de forma segura...", HUDMessage.newQuest_type));

                    // Salir al menú principal limpiamente tras 1.5s
                    DelayedAction.functionAfterDelay(() =>
                    {
                        Game1.ExitToTitle();
                    }, 1500);

                    __result = true;
                    return false; // Cancela el startSleep() nativo que pausaría/cerraría la sesión para todos
                }
            }

            return true;
        }
    }
}
