using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Festivals;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace Pelican4Ever.Modules
{
    public static class FestivalMessages
    {
        public const string SubmitScore = "SubmitFestivalScore";
        public const string SyncScores = "SyncFestivalScores";
    }

    public class SubmitFestivalScorePayload
    {
        public string FestivalId { get; set; } = string.Empty;
        public int Score { get; set; }
    }

    /// <summary>
    /// Gestiona minijuegos de festivales asíncronos y ceremonias programadas en vivo (TASK-701 y TASK-702).
    /// </summary>
    public sealed class FestivalMinigameModule : IModule
    {
        private static ModContext? context;
        private static bool warningAnnouncedToday;
        private static int lastWarningDay = -1;

        public string Name => "FestivalMinigame";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            ctx.Helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            ctx.Helper.Events.GameLoop.TimeChanged += OnTimeChanged;
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            ctx.Helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            RegisterFestivalCommands();
        }

        private static void RegisterFestivalCommands()
        {
            try
            {
                if (!ChatCommands.Exists("festival"))
                {
                    ChatCommands.Register(
                        "festival",
                        HandleFestivalLeaderboardCommand,
                        name => "Muestra la tabla de puntuaciones del festival (/festival)",
                        new[] { "fest", "leaderboard" },
                        mainOnly: false,
                        multiplayerOnly: true,
                        cheatsOnly: false
                    );
                }
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[FestivalMinigameModule] Error registrando comando /festival: {ex}", LogLevel.Warn);
            }
        }

        private static void HandleFestivalLeaderboardCommand(string[] command, ChatBox chat)
        {
            if (context == null) return;

            int today = Game1.Date.TotalDays;
            string seasonDay = $"{Game1.currentSeason}{Game1.dayOfMonth}";

            var scores = context.WorldState.FestivalScores
                .Where(s => s.GameDay == today || s.FestivalId == seasonDay)
                .ToList();

            if (scores.Count == 0)
            {
                chat.addInfoMessage("No hay puntuaciones registradas en el festival de hoy todavía.");
                return;
            }

            chat.addMessage($"=== Clasificación de {seasonDay} ===", new Color(255, 215, 0));
            var formatted = FestivalScoreLogic.FormatLeaderboard(scores, seasonDay, today);
            foreach (string entry in formatted)
            {
                chat.addMessage(entry, Color.White);
            }
        }

        private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
        {
            if (context == null || !Context.IsWorldReady)
                return;

            int today = Game1.Date.TotalDays;
            if (lastWarningDay != today)
            {
                warningAnnouncedToday = false;
                lastWarningDay = today;
            }

            // Preaviso de ceremonia en vivo (TASK-702): a las 20:45 si es día de festival
            if (!warningAnnouncedToday && Utility.isFestivalDay(Game1.dayOfMonth, Game1.season))
            {
                if (CeremonyLogic.IsWarningWindow(e.NewTime, CeremonyLogic.DefaultCeremonyTime, CeremonyLogic.WarningLeadTimeMinutes))
                {
                    warningAnnouncedToday = true;
                    Game1.chatBox?.addMessage("[Aviso Festivo] La ceremonia comunitaria dará comienzo en breve (21:00) en el recinto festivo.", new Color(255, 215, 0));
                }
            }

            // Consolidación de ganadores del festival a las 23:50 (TASK-701)
            if (Context.IsMainPlayer && e.NewTime >= 2350 && Utility.isFestivalDay(Game1.dayOfMonth, Game1.season))
            {
                ResolveDailyFestivalAwards(today);
            }
        }

        private void ResolveDailyFestivalAwards(int today)
        {
            if (context == null) return;

            string seasonDay = $"{Game1.currentSeason}{Game1.dayOfMonth}";
            var winner = FestivalScoreLogic.DetermineWinner(context.WorldState.FestivalScores, seasonDay, today);

            if (winner != null && !winner.RewardAwarded)
            {
                winner.RewardAwarded = true;
                Game1.chatBox?.addMessage($"[Festivales] ¡{winner.PlayerName} ha ganado el minijuego festivo de hoy con {winner.Score} puntos!", new Color(255, 215, 0));

                Farmer? recipient = Game1.GetPlayer(winner.PlayerId);
                if (recipient != null && recipient.isActive())
                {
                    Item prize = ItemRegistry.Create("(O)PrizeTicket", 1);
                    recipient.addItemByMenuIfNecessary(prize);
                    if (recipient.UniqueMultiplayerID == Game1.player.UniqueMultiplayerID)
                    {
                        Game1.addHUDMessage(new HUDMessage("¡Has recibido un Ticket de Premio por ganar el minijuego festivo!", HUDMessage.newQuest_type));
                    }
                }

                BroadcastFestivalSync();
            }
        }

        private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
        {
            if (Context.IsMainPlayer && context != null)
            {
                context.Helper.Multiplayer.SendMessage(
                    context.WorldState.FestivalScores,
                    FestivalMessages.SyncScores,
                    new[] { context.Manifest.UniqueID },
                    new[] { e.Peer.PlayerID }
                );
            }
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || e.FromModID != context.Manifest.UniqueID)
                return;

            if (Context.IsMainPlayer && e.Type == FestivalMessages.SubmitScore)
            {
                var payload = e.ReadAs<SubmitFestivalScorePayload>();
                if (payload != null)
                {
                    Farmer? player = Game1.GetPlayer(e.FromPlayerID);
                    string name = player?.Name ?? "Vecino";
                    int today = Game1.Date.TotalDays;

                    FestivalScoreLogic.RecordScore(
                        context.WorldState.FestivalScores,
                        payload.FestivalId,
                        today,
                        e.FromPlayerID,
                        name,
                        payload.Score
                    );

                    BroadcastFestivalSync();
                    context.Monitor.Log($"[FestivalMinigameModule] Puntuación registrada: {name} ({payload.Score} pts) en {payload.FestivalId}.", LogLevel.Info);
                }
            }

            if (e.Type == FestivalMessages.SyncScores)
            {
                var synced = e.ReadAs<List<FestivalScoreEntry>>();
                if (synced != null)
                {
                    context.WorldState.FestivalScores = synced;
                }
            }
        }

        public static void SubmitScore(string festivalId, int score)
        {
            if (context == null) return;

            int today = Game1.Date.TotalDays;
            if (Context.IsMainPlayer)
            {
                FestivalScoreLogic.RecordScore(
                    context.WorldState.FestivalScores,
                    festivalId,
                    today,
                    Game1.player.UniqueMultiplayerID,
                    Game1.player.Name,
                    score
                );
                BroadcastFestivalSync();
            }
            else
            {
                context.Helper.Multiplayer.SendMessage(
                    new SubmitFestivalScorePayload { FestivalId = festivalId, Score = score },
                    FestivalMessages.SubmitScore,
                    new[] { context.Manifest.UniqueID },
                    new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );
            }
        }

        private static void BroadcastFestivalSync()
        {
            if (context == null) return;

            context.Helper.Multiplayer.SendMessage(
                context.WorldState.FestivalScores,
                FestivalMessages.SyncScores,
                new[] { context.Manifest.UniqueID }
            );
        }
    }
}
