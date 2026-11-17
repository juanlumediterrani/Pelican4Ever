using System;
using System.Collections.Generic;
using HarmonyLib;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Economy;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>Curva de XP local con recuperación del contador y de la configuración del host.</summary>
    public sealed class SkillExperienceModule : IModule
    {
        private const string Request = "SkillExperienceRequest";
        private const string Snapshot = "SkillExperienceSnapshot";
        private const string Report = "SkillExperienceReport";
        private const string CheckpointPrepare = "SkillXpCheckpointPrepare";
        private const string CheckpointAck = "SkillXpCheckpointAck";
        private const string CheckpointRelease = "SkillXpCheckpointRelease";
        private static readonly string[] SkillNames = { "Farming", "Fishing", "Foraging", "Mining", "Combat" };
        private static readonly List<(int Skill, int Amount)> Pending = new();
        private static ModContext? context;
        private static bool ready;
        private static SkillRules? rules;
        private static string? clientCheckpoint;
        private static string? hostCheckpoint;
        private static readonly Dictionary<long, int[]?> CheckpointPeers = new();

        public static bool HasLocalState => ready;
        public string Name => "SkillExperience";
        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;
            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += OnSecond;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += (_, _) => { ready = false; rules = null; Pending.Clear(); clientCheckpoint = null; hostCheckpoint = null; CheckpointPeers.Clear(); };
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnMessage;
            ctx.Patch(
                "Farmer.gainExperience",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(Farmer), nameof(Farmer.gainExperience), new[] { typeof(int), typeof(int) }),
                prefix: new HarmonyMethod(typeof(SkillExperienceModule), nameof(PrefixGainExperience)));
        }

        private static void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            if (context == null) return;
            if (Context.IsMainPlayer)
            {
                rules = SkillRules.FromConfig(context.Config);
                ready = true;
                ReplayPending();
            }
            else
            {
                ready = false;
                rules = null;
                context.Helper.Multiplayer.SendMessage(string.Empty, Request, new[] { context.Manifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
            }
        }

        private static void OnSecond(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (context == null || !Context.IsWorldReady || Context.IsMainPlayer || Game1.MasterPlayer == null) return;
            if (!ready)
                context.Helper.Multiplayer.SendMessage(string.Empty, Request, new[] { context.Manifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        }

        private static void ReplayPending()
        {
            var pending = Pending.ToArray();
            Pending.Clear();
            foreach (var item in pending)
                Game1.player.gainExperience(item.Skill, item.Amount);
        }

        public static bool PrefixGainExperience(Farmer __instance, int __0, ref int __1)
        {
            if (context == null || !Context.IsWorldReady || __instance != Game1.player || __1 <= 0 || __0 < 0 || __0 >= SkillNames.Length)
                return true;
            if (!ready || rules == null || clientCheckpoint != null)
            {
                Pending.Add((__0, __1));
                return false;
            }
            string skill = SkillNames[__0];
            var record = context.WorldState.GetOrCreatePlayer(__instance.UniqueMultiplayerID, __instance.Name);
            record.SkillExperience ??= new DailySkillExperience();
            rules.Caps.TryGetValue(skill, out int cap);
            decimal multiplier = rules.Multipliers.TryGetValue(skill, out decimal configured) ? configured : 1m;
            __1 = record.SkillExperience.Apply(Game1.Date.TotalDays, skill, __1, multiplier, cap, rules.Overflow);
            if (!Context.IsMainPlayer)
                context.Helper.Multiplayer.SendMessage(record.SkillExperience, Report, new[] { context.Manifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
            return true;
        }

        private static void OnMessage(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || !Context.IsWorldReady || e.FromModID != context.Manifest.UniqueID) return;
            if (Context.IsMainPlayer && e.Type == Request)
            {
                Farmer? farmer = Game1.GetPlayer(e.FromPlayerID);
                if (farmer == null) return;
                var record = context.WorldState.GetOrCreatePlayer(e.FromPlayerID, farmer.Name);
                record.SkillExperience ??= new DailySkillExperience();
                record.SkillExperience.EnsureDay(Game1.Date.TotalDays);
                context.Helper.Multiplayer.SendMessage(new SkillSnapshot { State = record.SkillExperience, Rules = SkillRules.FromConfig(context.Config) },
                    Snapshot, new[] { context.Manifest.UniqueID }, new[] { e.FromPlayerID });
            }
            else if (Context.IsMainPlayer && e.Type == Report)
            {
                Farmer? farmer = Game1.GetPlayer(e.FromPlayerID);
                if (farmer == null) return;
                var record = context.WorldState.GetOrCreatePlayer(e.FromPlayerID, farmer.Name);
                record.SkillExperience ??= new DailySkillExperience();
                record.SkillExperience.MergeReport(e.ReadAs<DailySkillExperience>(), Game1.Date.TotalDays);
            }
            else if (!Context.IsMainPlayer && e.Type == CheckpointPrepare && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID && ready)
            {
                string id = e.ReadAs<string>();
                if (string.IsNullOrEmpty(id) || clientCheckpoint != null
                    || (DailyGoldTelemetryModule.IsEnabled && !DailyGoldTelemetryModule.HasLocalState)) return;
                clientCheckpoint = id;
                var record = context.WorldState.GetOrCreatePlayer(Game1.player.UniqueMultiplayerID, Game1.player.Name);
                record.SkillExperience ??= new DailySkillExperience();
                record.SkillExperience.EnsureDay(Game1.Date.TotalDays);
                int[] experience = new int[SkillNames.Length];
                for (int i = 0; i < experience.Length; i++) experience[i] = Game1.player.experiencePoints[i];
                context.Helper.Multiplayer.SendMessage(new CheckpointAcknowledgement { Id = id, State = record.SkillExperience, Gold = record.GoldLedger, Experience = experience },
                    CheckpointAck, new[] { context.Manifest.UniqueID }, new[] { e.FromPlayerID });
            }
            else if (Context.IsMainPlayer && e.Type == CheckpointAck && hostCheckpoint != null && CheckpointPeers.ContainsKey(e.FromPlayerID))
            {
                var ack = e.ReadAs<CheckpointAcknowledgement>();
                Farmer? farmer = Game1.GetPlayer(e.FromPlayerID);
                if (ack == null || ack.Id != hostCheckpoint || ack.State == null || ack.Experience == null
                    || ack.State.GameDay != Game1.Date.TotalDays || ack.Experience.Length != SkillNames.Length || farmer == null
                    || (DailyGoldTelemetryModule.IsEnabled && ack.Gold == null)) return;
                var record = context.WorldState.GetOrCreatePlayer(e.FromPlayerID, farmer.Name);
                record.SkillExperience ??= new DailySkillExperience();
                record.SkillExperience.MergeReport(ack.State, Game1.Date.TotalDays);
                if (ack.Gold != null)
                {
                    record.GoldLedger ??= new DailyGoldLedger();
                    record.GoldLedger.MergeClient(ack.Gold, Game1.Date.TotalDays);
                }
                CheckpointPeers[e.FromPlayerID] = ack.Experience;
            }
            else if (!Context.IsMainPlayer && e.Type == CheckpointRelease && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
            {
                string id = e.ReadAs<string>();
                if (clientCheckpoint == id)
                {
                    clientCheckpoint = null;
                    if (ready) ReplayPending();
                }
            }
            else if (!Context.IsMainPlayer && e.Type == Snapshot && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID && !ready)
            {
                SkillSnapshot snapshot = e.ReadAs<SkillSnapshot>();
                if (snapshot?.State == null || snapshot.Rules?.Caps == null || snapshot.Rules.Multipliers == null) return;
                var record = context.WorldState.GetOrCreatePlayer(Game1.player.UniqueMultiplayerID, Game1.player.Name);
                record.SkillExperience = snapshot.State;
                rules = snapshot.Rules;
                ready = true;
                ReplayPending();
            }
        }

        /// <summary>Congela nuevas concesiones de XP mientras se espera la copia sincronizada de cada farmhand.</summary>
        public static void BeginCheckpoint(IEnumerable<long> peers)
        {
            if (context == null || !Context.IsMainPlayer) return;
            hostCheckpoint = Guid.NewGuid().ToString("N");
            CheckpointPeers.Clear();
            foreach (long id in peers) CheckpointPeers[id] = null;
            if (CheckpointPeers.Count > 0)
                context.Helper.Multiplayer.SendMessage(hostCheckpoint, CheckpointPrepare, new[] { context.Manifest.UniqueID }, new List<long>(CheckpointPeers.Keys).ToArray());
        }

        public static bool CheckpointReady(IEnumerable<long> peers)
        {
            if (context == null) return true; // Módulo desactivado en este host.
            if (hostCheckpoint == null) return false;
            var replicated = new Dictionary<long, int[]>();
            foreach (long id in peers)
            {
                Farmer? farmer = Game1.GetPlayer(id);
                if (farmer == null) return false;
                var values = new int[SkillNames.Length];
                for (int i = 0; i < values.Length; i++) values[i] = farmer.experiencePoints[i];
                replicated[id] = values;
            }
            return SkillCheckpointBarrier.IsReady(CheckpointPeers, replicated);
        }

        public static void EndCheckpoint()
        {
            if (context != null && hostCheckpoint != null && CheckpointPeers.Count > 0)
                context.Helper.Multiplayer.SendMessage(hostCheckpoint, CheckpointRelease, new[] { context.Manifest.UniqueID }, new List<long>(CheckpointPeers.Keys).ToArray());
            hostCheckpoint = null;
            CheckpointPeers.Clear();
        }

        public sealed class CheckpointAcknowledgement
        {
            public string Id { get; set; } = string.Empty;
            public DailySkillExperience State { get; set; } = new();
            public DailyGoldLedger? Gold { get; set; }
            public int[] Experience { get; set; } = Array.Empty<int>();
        }

        public sealed class SkillSnapshot
        {
            public DailySkillExperience State { get; set; } = new();
            public SkillRules Rules { get; set; } = new();
        }

        public sealed class SkillRules
        {
            public Dictionary<string, int> Caps { get; set; } = new();
            public Dictionary<string, decimal> Multipliers { get; set; } = new();
            public decimal Overflow { get; set; }
            public static SkillRules FromConfig(Core.Config.ServerConfig config) => new()
            {
                Caps = new(config.SkillDailyFullXp), Multipliers = new(config.SkillXpMultiplier), Overflow = config.SkillOverflowXpMultiplier
            };
        }
    }
}
