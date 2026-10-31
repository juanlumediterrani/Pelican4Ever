using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
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
        private static bool ceremonyTriggeredToday;
        private static int lastWarningDay = -1;

        public string Name => "FestivalMinigame";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            ctx.Helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            ctx.Helper.Events.GameLoop.TimeChanged += OnTimeChanged;
            ctx.Helper.Events.Input.ButtonPressed += OnButtonPressed;
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            ctx.Helper.Events.Multiplayer.PeerConnected += OnPeerConnected;

            ApplyHarmonyPatches(ctx.Harmony);
        }

        private void ApplyHarmonyPatches(Harmony harmony)
        {
            try
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(Event), nameof(Event.EndPlayerControlSequence)),
                    prefix: new HarmonyMethod(typeof(FestivalMinigameModule), nameof(Prefix_EndPlayerControlSequence))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(Event.DefaultCommands), nameof(Event.DefaultCommands.AwardFestivalPrize)),
                    prefix: new HarmonyMethod(typeof(FestivalMinigameModule), nameof(Prefix_AwardFestivalPrize))
                );

                context?.Monitor.Log("[FestivalMinigameModule] Parches de minijuegos festivos aplicados.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[FestivalMinigameModule] Error al aplicar parches Harmony: {ex}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Captura la puntuación al finalizar un minijuego competitivo (búsqueda de huevos o pesca en el hielo)
        /// y la envía al Host para su consolidación comunitaria (TASK-701).
        /// </summary>
        public static void Prefix_EndPlayerControlSequence(Event __instance)
        {
            if (!Game1.IsMultiplayer || !__instance.isFestival)
                return;

            string? seq = __instance.playerControlSequenceID;
            if (seq is "eggHunt" or "iceFishing")
            {
                int score = Game1.player.festivalScore;
                string festivalId = FestivalScoreLogic.NormalizeFestivalId($"{Game1.currentSeason}{Game1.dayOfMonth}");
                SubmitScore(festivalId, score);
                context?.Monitor.Log($"[FestivalMinigameModule] Minijuego '{seq}' finalizado con {score} puntos. Puntuación registrada para '{festivalId}'.", LogLevel.Info);
            }
        }

        /// <summary>
        /// Suprime la entrega de premios en cada tanda individual en festivales competitivos con clasificación diaria.
        /// El premio oficial se otorga al primer puesto de la jornada a las 23:50 (TASK-701).
        /// </summary>
        public static bool Prefix_AwardFestivalPrize(Event @event, string[] args)
        {
            if (!Game1.IsMultiplayer || !@event.isFestival)
                return true;

            string id = @event.id;
            if (id is "festival_spring13" or "festival_winter8")
            {
                @event.CurrentCommand += 2;
                context?.Monitor.Log($"[FestivalMinigameModule] Premio de tanda suprimido en {id}; se otorgará al campeón diario a las 23:50.", LogLevel.Trace);
                return false;
            }

            return true;
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (context == null || !Context.IsWorldReady || !e.Button.IsActionButton() || Game1.activeClickableMenu != null)
                return;

            if (Game1.CurrentEvent is not { } festival || !festival.isFestival)
                return;

            Vector2 target = e.Cursor.GrabTile;
            string? action = Game1.currentLocation?.doesTileHaveProperty((int)target.X, (int)target.Y, "Action", "Buildings");
            if (action != null && action.StartsWith("Billboard", StringComparison.OrdinalIgnoreCase))
            {
                context.Helper.Input.Suppress(e.Button);
                ShowFestivalLeaderboardLetter();
            }
        }

        public static void ShowFestivalLeaderboardLetter()
        {
            if (context == null) return;

            string seasonDay = FestivalScoreLogic.NormalizeFestivalId($"{Game1.currentSeason}{Game1.dayOfMonth}");
            int today = Game1.Date.TotalDays;
            string festName = Game1.CurrentEvent?.FestivalName ?? $"Festival de {seasonDay}";

            string content = FestivalScoreLogic.BuildLeaderboardLetterText(
                context.WorldState.FestivalScores,
                seasonDay,
                today,
                festName
            );

            Game1.activeClickableMenu = new LetterViewerMenu(content);
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
            string seasonDay = FestivalScoreLogic.NormalizeFestivalId($"{Game1.currentSeason}{Game1.dayOfMonth}");

            var scores = context.WorldState.FestivalScores
                .Where(s => s.GameDay == today || FestivalScoreLogic.NormalizeFestivalId(s.FestivalId) == seasonDay)
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
                ceremonyTriggeredToday = false;
                lastWarningDay = today;
            }

            string season = Game1.currentSeason;
            int day = Game1.dayOfMonth;

            // Ceremonias comunitarias en vivo (Luau, Medusas) (TASK-702): solo Host
            if (Context.IsMainPlayer && CeremonyLogic.IsCeremonyFestival(season, day))
            {
                string ceremonyName = CeremonyLogic.GetCeremonyFestivalName(season, day);
                string ceremonyLoc = CeremonyLogic.GetCeremonyLocation(season, day) ?? "Beach";

                // 1. Preaviso único a las 20:45 emitido por el host
                if (!warningAnnouncedToday && CeremonyLogic.IsWarningWindow(e.NewTime, CeremonyLogic.DefaultCeremonyTime, CeremonyLogic.WarningLeadTimeMinutes))
                {
                    warningAnnouncedToday = true;
                    Game1.chatBox?.addMessage($"[Aviso Festivo] La ceremonia de {ceremonyName} dará comienzo en 15 minutos (21:00) en la playa.", new Color(255, 215, 0));
                }

                // 2. Arranque automático sincronizado a las 21:00 con los vecinos presentes en el mapa
                if (!ceremonyTriggeredToday && CeremonyLogic.IsCeremonyTime(e.NewTime, CeremonyLogic.DefaultCeremonyTime))
                {
                    ceremonyTriggeredToday = true;
                    var attendees = Game1.getOnlineFarmers()
                        .Where(f => f.isActive() && CeremonyLogic.IsPlayerInCeremonyLocation(f.currentLocation?.Name, ceremonyLoc))
                        .Select(f => f.UniqueMultiplayerID)
                        .ToList();

                    if (attendees.Count > 0)
                    {
                        string festId = $"festival_{season}{day}";
                        bool started = FestivalParticipationModule.StartCeremonyRun(festId, attendees);
                        if (started)
                        {
                            Game1.chatBox?.addMessage($"[Ceremonia] ¡Comienza la ceremonia de {ceremonyName} en la playa con {attendees.Count} vecinos presentes!", new Color(255, 215, 0));
                        }
                    }
                    else
                    {
                        Game1.chatBox?.addInfoMessage($"[Ceremonia] La ceremonia de {ceremonyName} estaba programada a las 21:00, pero no había vecinos presentes en la playa.");
                    }
                }
            }

            // Consolidación de ganadores del festival y entrega de premios oficiales a las 23:50 (TASK-701)
            if (Context.IsMainPlayer && e.NewTime >= 2350 && Utility.isFestivalDay(Game1.dayOfMonth, Game1.season))
            {
                ResolveDailyFestivalAwards(today);
            }
        }

        private void ResolveDailyFestivalAwards(int today)
        {
            if (context == null) return;

            string seasonDay = FestivalScoreLogic.NormalizeFestivalId($"{Game1.currentSeason}{Game1.dayOfMonth}");
            var winner = FestivalScoreLogic.DetermineWinner(context.WorldState.FestivalScores, seasonDay, today);

            if (winner != null && !winner.RewardAwarded)
            {
                winner.RewardAwarded = true;
                Game1.chatBox?.addMessage($"[Festivales] ¡{winner.PlayerName} ha ganado el minijuego festivo de hoy con {winner.Score} puntos!", new Color(255, 215, 0));

                Farmer? recipient = Game1.GetPlayer(winner.PlayerId);
                if (recipient != null)
                {
                    AwardWinnerPrize(recipient, seasonDay);
                }

                BroadcastFestivalSync();
            }
        }

        private static void AwardWinnerPrize(Farmer recipient, string seasonDay)
        {
            List<Item> prizes = new();
            string norm = FestivalScoreLogic.NormalizeFestivalId(seasonDay);

            if (norm == "spring13")
            {
                if (recipient.mailReceived.Add("Egg Festival"))
                    prizes.Add(ItemRegistry.Create("(H)4")); // Sombrero de Paja
                else
                    prizes.Add(ItemRegistry.Create("(O)PrizeTicket"));
            }
            else if (norm == "winter8")
            {
                if (recipient.mailReceived.Add("Ice Festival"))
                {
                    prizes.Add(ItemRegistry.Create("(H)17")); // Gorra de Marinero
                    prizes.Add(ItemRegistry.Create("(O)687")); // Hilandero vestido
                    prizes.Add(ItemRegistry.Create("(O)691")); // Anzuelo con púa
                    prizes.Add(ItemRegistry.Create("(O)703")); // Imán
                }
                else
                {
                    prizes.Add(ItemRegistry.Create("(O)PrizeTicket"));
                }
            }
            else
            {
                prizes.Add(ItemRegistry.Create("(O)PrizeTicket"));
            }

            foreach (Item prize in prizes)
            {
                if (recipient.isActive())
                {
                    recipient.addItemByMenuIfNecessary(prize);
                }
                else
                {
                    if (!recipient.addItemToInventoryBool(prize))
                    {
                        recipient.mailForTomorrow.Add($"%item {prize.QualifiedItemId} %%[Pelican 4Ever] ¡Premio por ganar el minijuego del festival!");
                    }
                }
            }

            if (recipient.isActive() && recipient.UniqueMultiplayerID == Game1.player.UniqueMultiplayerID)
            {
                Game1.addHUDMessage(new HUDMessage("¡Has recibido el premio oficial por ganar el minijuego festivo de hoy!", HUDMessage.newQuest_type));
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
                    string normId = FestivalScoreLogic.NormalizeFestivalId(payload.FestivalId);

                    FestivalScoreLogic.RecordScore(
                        context.WorldState.FestivalScores,
                        normId,
                        today,
                        e.FromPlayerID,
                        name,
                        payload.Score
                    );

                    BroadcastFestivalSync();
                    context.Monitor.Log($"[FestivalMinigameModule] Puntuación registrada: {name} ({payload.Score} pts) en {normId}.", LogLevel.Info);
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
            string normId = FestivalScoreLogic.NormalizeFestivalId(festivalId);

            if (Context.IsMainPlayer)
            {
                FestivalScoreLogic.RecordScore(
                    context.WorldState.FestivalScores,
                    normId,
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
                    new SubmitFestivalScorePayload { FestivalId = normId, Score = score },
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
