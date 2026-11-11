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
        public const string GrantPrize = "FestivalGrantPrize";
        public const string PrizeGranted = "FestivalPrizeGranted";
    }

    public class FestivalPrizePayload
    {
        public string PrizeKey { get; set; } = string.Empty;
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

        /// <summary>Premios recibidos del host que el jugador local aún no ha recogido (se entregan sin menús abiertos).</summary>
        private static readonly Queue<string> localPrizeQueue = new();

        /// <summary><c>Farmer.modData</c> del ganador: premios ya entregados, para no repetirlos si el aviso llega dos veces.</summary>
        private const string GrantedPrizesKey = "Pelican4Ever.FestivalPrizesGranted";

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
            ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += (_, _) => GrantQueuedPrizes();
            ctx.Helper.Events.GameLoop.ReturnedToTitle += (_, _) => localPrizeQueue.Clear();

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
                chat.addInfoMessage(I18n.Get("festival.no_scores"));
                return;
            }

            chat.addMessage(I18n.Get("festival.leaderboard_header", new { date = seasonDay }), new Color(255, 215, 0));
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

                // 1. Preaviso único a las 20:45 emitido por el host
                if (!warningAnnouncedToday && CeremonyLogic.IsWarningWindow(e.NewTime, CeremonyLogic.DefaultCeremonyTime, CeremonyLogic.WarningLeadTimeMinutes))
                {
                    warningAnnouncedToday = true;
                    Game1.chatBox?.addMessage(I18n.Get("festival.ceremony_warning_15m", new { ceremony = ceremonyName }), new Color(255, 215, 0));
                }

                // 2. Arranque automático sincronizado a las 21:00 con los vecinos presentes en el mapa
                if (!ceremonyTriggeredToday && CeremonyLogic.IsCeremonyTime(e.NewTime, CeremonyLogic.DefaultCeremonyTime))
                {
                    ceremonyTriggeredToday = true;
                    // Presentes según el aviso de cada cliente: dentro del festival el jugador está en la ubicación «Temp»
                    IReadOnlyList<long> attendees = FestivalParticipationModule.PlayersInFestival();

                    if (attendees.Count > 0)
                    {
                        string festId = $"festival_{season}{day}";
                        bool started = FestivalParticipationModule.StartCeremonyRun(festId, attendees);
                        if (started)
                        {
                            Game1.chatBox?.addMessage(I18n.Get("festival.ceremony_started", new { ceremony = ceremonyName, count = attendees.Count }), new Color(255, 215, 0));
                        }
                    }
                    else
                    {
                        Game1.chatBox?.addInfoMessage(I18n.Get("festival.ceremony_no_attendees", new { ceremony = ceremonyName }));
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
                Game1.chatBox?.addMessage(I18n.Get("festival.winner_announcement", new { winner = winner.PlayerName, score = winner.Score }), new Color(255, 215, 0));

                QueuePrize(winner.PlayerId, winner.PlayerName, FestivalPrizeLogic.PrizeKey(seasonDay, today));

                BroadcastFestivalSync();
            }
        }

        /// <summary>
        /// Host: deja el premio pendiente en el registro del ganador y se lo envía a su cliente si está conectado.
        /// El inventario y el <c>mailReceived</c> de un jugador remoto los controla su cliente, así que el premio no
        /// se añade desde el host; queda pendiente hasta que el cliente confirma, también si estaba desconectado.
        /// </summary>
        private static void QueuePrize(long playerId, string playerName, string prizeKey)
        {
            if (context == null) return;

            var record = context.WorldState.GetOrCreatePlayer(playerId, playerName);
            record.PendingFestivalPrizes ??= new List<string>();
            if (!record.PendingFestivalPrizes.Contains(prizeKey))
                record.PendingFestivalPrizes.Add(prizeKey);

            DeliverPendingPrizes(playerId);
        }

        private static void DeliverPendingPrizes(long playerId)
        {
            if (context == null || !Context.IsMainPlayer) return;
            if (!context.WorldState.Players.TryGetValue(playerId, out var record) || record.PendingFestivalPrizes is not { Count: > 0 } pending)
                return;

            if (playerId == Game1.player.UniqueMultiplayerID)
            {
                foreach (string key in pending)
                    localPrizeQueue.Enqueue(key);
                return;
            }

            if (!Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == playerId))
                return;

            foreach (string key in pending)
            {
                context.Helper.Multiplayer.SendMessage(
                    new FestivalPrizePayload { PrizeKey = key },
                    FestivalMessages.GrantPrize,
                    new[] { context.Manifest.UniqueID },
                    new[] { playerId }
                );
            }
        }

        /// <summary>Entrega en el jugador local los premios recibidos, cuando el mundo está listo y no hay menús abiertos.</summary>
        private static void GrantQueuedPrizes()
        {
            if (context == null || !Context.IsWorldReady || Game1.activeClickableMenu != null || localPrizeQueue.Count == 0)
                return;

            string key = localPrizeQueue.Dequeue();
            string granted = Game1.player.modData.TryGetValue(GrantedPrizesKey, out string? list) ? list : string.Empty;

            if (!FestivalPrizeLogic.ListContains(granted, key) && FestivalPrizeLogic.FestivalOfKey(key) is { } festivalId)
            {
                string? flag = FestivalPrizeLogic.FirstWinMailFlag(festivalId);
                bool firstWin = flag != null && Game1.player.mailReceived.Add(flag);
                List<Item> prizes = FestivalPrizeLogic.PrizeItems(festivalId, firstWin)
                    .Select(id => ItemRegistry.Create(id))
                    .ToList();

                Game1.player.modData[GrantedPrizesKey] = FestivalPrizeLogic.AddToList(granted, key);
                Game1.player.addItemsByMenuIfNecessary(prizes);
                Game1.addHUDMessage(new HUDMessage(I18n.Get("festival.prize_received"), HUDMessage.newQuest_type));
            }

            ConfirmPrize(key);
        }

        private static void ConfirmPrize(string key)
        {
            if (context == null) return;

            if (Context.IsMainPlayer)
            {
                RemovePendingPrize(Game1.player.UniqueMultiplayerID, key);
                return;
            }

            context.Helper.Multiplayer.SendMessage(
                new FestivalPrizePayload { PrizeKey = key },
                FestivalMessages.PrizeGranted,
                new[] { context.Manifest.UniqueID },
                new[] { Game1.MasterPlayer.UniqueMultiplayerID }
            );
        }

        private static void RemovePendingPrize(long playerId, string key)
        {
            if (context != null && context.WorldState.Players.TryGetValue(playerId, out var record))
                record.PendingFestivalPrizes?.Remove(key);
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

                // Premios ganados mientras estaba desconectado
                DeliverPendingPrizes(e.Peer.PlayerID);
            }
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || e.FromModID != context.Manifest.UniqueID)
                return;

            if (e.Type == FestivalMessages.GrantPrize && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
            {
                if (e.ReadAs<FestivalPrizePayload>() is { } prize && !localPrizeQueue.Contains(prize.PrizeKey))
                    localPrizeQueue.Enqueue(prize.PrizeKey);
                return;
            }

            if (Context.IsMainPlayer && e.Type == FestivalMessages.PrizeGranted)
            {
                if (e.ReadAs<FestivalPrizePayload>() is { } granted)
                    RemovePendingPrize(e.FromPlayerID, granted.PrizeKey);
                return;
            }

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
