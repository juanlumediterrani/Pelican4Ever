using System;
using System.Collections.Generic;
using HarmonyLib;
using Pelican4Ever.Core.Economy;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>Registra créditos brutos sin confundir cartera individual con cartera compartida.</summary>
    public sealed class DailyGoldTelemetryModule : IModule
    {
        private const string Request = "DailyGoldRequest";
        private const string Snapshot = "DailyGoldSnapshot";
        private const string Report = "DailyGoldReport";
        private static ModContext? context;
        private static bool ready;
        private static int? closingGameDay;
        private static readonly List<(int Day, long Amount, bool Shared)> Pending = new();
        // AddIndividualMoney/SetIndividualMoney may mutate Farmer.Money internally. Suppress
        // the nested property hook so one transaction is recorded at the team API boundary.
        private static readonly Dictionary<long, int> ActiveTeamMoneyMutations = new();
        public static bool HasLocalState => ready;
        public static bool IsEnabled => context != null;
        public string Name => "DailyGoldTelemetry";
        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;
            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.GameLoop.DayEnding += OnDayEnding;
            ctx.Helper.Events.GameLoop.DayStarted += OnDayStarted;
            ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += OnSecond;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += (_, _) => { ready = false; closingGameDay = null; Pending.Clear(); ActiveTeamMoneyMutations.Clear(); };
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnMessage;
            ctx.Harmony.Patch(AccessTools.PropertySetter(typeof(Farmer), nameof(Farmer.Money)),
                prefix: new HarmonyMethod(typeof(DailyGoldTelemetryModule), nameof(PrefixMoney)),
                postfix: new HarmonyMethod(typeof(DailyGoldTelemetryModule), nameof(PostfixMoney)));
            foreach (string name in new[] { nameof(FarmerTeam.AddIndividualMoney), nameof(FarmerTeam.SetIndividualMoney) })
                ctx.Harmony.Patch(AccessTools.Method(typeof(FarmerTeam), name),
                    prefix: new HarmonyMethod(typeof(DailyGoldTelemetryModule), nameof(PrefixTeamMoney)),
                    postfix: new HarmonyMethod(typeof(DailyGoldTelemetryModule), nameof(PostfixTeamMoney)));
        }

        private static void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            closingGameDay = null;
            if (Context.IsMainPlayer) { ready = true; ReplayPending(); }
            else RequestState();
        }

        private static void OnDayEnding(object? sender, DayEndingEventArgs e)
        {
            // Fija el día que se cierra mientras _newDayAfterFade incrementa la fecha y abona los envíos.
            closingGameDay = Game1.Date.TotalDays;
        }

        [EventPriority(EventPriority.High)]
        private static void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            // No depender de Game1.newDaySync.hasInstance(): permanece activo hasta cerrar menús matutinos.
            closingGameDay = null;
        }

        private static void OnSecond(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (Context.IsWorldReady && !Context.IsMainPlayer && !ready) RequestState();
        }

        private static void RequestState()
        {
            if (context != null && Game1.MasterPlayer != null)
                context.Helper.Multiplayer.SendMessage(string.Empty, Request, new[] { context.Manifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        }

        public static void PrefixMoney(Farmer __instance, out MoneyMutationState __state)
        {
            long id = __instance.UniqueMultiplayerID;
            __state = new MoneyMutationState(__instance.Money, ActiveTeamMoneyMutations.ContainsKey(id));
        }

        public static void PostfixMoney(Farmer __instance, MoneyMutationState __state)
        {
            if (__state.Suppress || __instance != Game1.player) return;
            Track(__instance, (long)__instance.Money - __state.Money, Context.IsMainPlayer);
        }

        public static void PrefixTeamMoney(Farmer who, out TeamMoneyMutationState __state)
        {
            long? playerId = who?.UniqueMultiplayerID;
            bool outermost = true;
            if (playerId is long id)
            {
                ActiveTeamMoneyMutations.TryGetValue(id, out int depth);
                outermost = depth == 0;
                ActiveTeamMoneyMutations[id] = depth + 1;
            }
            __state = new TeamMoneyMutationState(playerId, who?.Money ?? 0, outermost);
        }

        public static void PostfixTeamMoney(Farmer who, TeamMoneyMutationState __state)
        {
            if (__state.PlayerId is long id && ActiveTeamMoneyMutations.TryGetValue(id, out int depth))
            {
                if (depth <= 1) ActiveTeamMoneyMutations.Remove(id);
                else ActiveTeamMoneyMutations[id] = depth - 1;
            }
            if (__state.Outermost && Context.IsMainPlayer && who != null)
                Track(who, (long)who.Money - __state.Money, hostApplied: true);
        }

        public readonly struct MoneyMutationState
        {
            public int Money { get; }
            public bool Suppress { get; }
            public MoneyMutationState(int money, bool suppress) { Money = money; Suppress = suppress; }
        }

        public readonly struct TeamMoneyMutationState
        {
            public long? PlayerId { get; }
            public int Money { get; }
            public bool Outermost { get; }
            public TeamMoneyMutationState(long? playerId, int money, bool outermost) { PlayerId = playerId; Money = money; Outermost = outermost; }
        }

        private static void Track(Farmer who, long amount, bool hostApplied)
        {
            if (context == null || !Context.IsWorldReady || Game1.player == null || amount <= 0) return;
            bool shared = !Game1.player.team.useSeparateWallets.Value;
            int day = closingGameDay ?? Game1.Date.TotalDays;
            if (!hostApplied && !ready) { Pending.Add((day, amount, shared)); return; }
            var record = context.WorldState.GetOrCreatePlayer(who.UniqueMultiplayerID, who.Name);
            record.GoldLedger ??= new DailyGoldLedger();
            record.GoldLedger.Add(day, amount, shared, hostApplied);
            if (!hostApplied) SendReport(record.GoldLedger);
        }

        private static void ReplayPending()
        {
            if (context == null || Game1.player == null) return;
            var record = context.WorldState.GetOrCreatePlayer(Game1.player.UniqueMultiplayerID, Game1.player.Name);
            record.GoldLedger ??= new DailyGoldLedger();
            foreach (var item in Pending) record.GoldLedger.Add(item.Day, item.Amount, item.Shared, Context.IsMainPlayer);
            if (Pending.Count > 0 && !Context.IsMainPlayer) SendReport(record.GoldLedger);
            Pending.Clear();
        }

        private static void SendReport(DailyGoldLedger ledger)
        {
            if (context != null && Game1.MasterPlayer != null)
                context.Helper.Multiplayer.SendMessage(ledger, Report, new[] { context.Manifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        }

        private static void OnMessage(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || !Context.IsWorldReady || e.FromModID != context.Manifest.UniqueID) return;
            if (Context.IsMainPlayer && (e.Type == Request || e.Type == Report))
            {
                Farmer? farmer = Game1.GetPlayer(e.FromPlayerID);
                if (farmer == null) return;
                var record = context.WorldState.GetOrCreatePlayer(e.FromPlayerID, farmer.Name);
                record.GoldLedger ??= new DailyGoldLedger();
                if (e.Type == Request)
                    context.Helper.Multiplayer.SendMessage(record.GoldLedger, Snapshot, new[] { context.Manifest.UniqueID }, new[] { e.FromPlayerID });
                else record.GoldLedger.MergeClient(e.ReadAs<DailyGoldLedger>(), Game1.Date.TotalDays);
            }
            else if (!Context.IsMainPlayer && e.Type == Snapshot && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID && !ready)
            {
                DailyGoldLedger ledger = e.ReadAs<DailyGoldLedger>();
                if (ledger?.Days == null) return;
                context.WorldState.GetOrCreatePlayer(Game1.player.UniqueMultiplayerID, Game1.player.Name).GoldLedger = ledger;
                ready = true;
                ReplayPending();
            }
        }
    }
}
