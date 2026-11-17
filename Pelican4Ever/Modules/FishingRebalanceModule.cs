using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Menus;
using StardewValley.Tools;

namespace Pelican4Ever.Modules
{
    public sealed class FishingRebalanceModule : IModule
    {
        private const string Request = "FishingRulesRequest";
        private const string Response = "FishingRules";
        private static ModContext? context;
        private static Rules? rules;
        public string Name => "FishingRebalance";
        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;
            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += OnSecond;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += (_, _) => rules = null;
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnMessage;
            ctx.Patch(
                "FishingRod.calculateTimeUntilFishingBite",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(FishingRod), "calculateTimeUntilFishingBite"),
                postfix: new HarmonyMethod(typeof(FishingRebalanceModule), nameof(PostfixBiteTime)));
            ctx.Patch(
                "BobberBar.update",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(BobberBar), nameof(BobberBar.update)),
                transpiler: new HarmonyMethod(typeof(FishingRebalanceModule), nameof(TranspileBobberUpdate)));
        }

        private static Rules CurrentRules() => new()
        {
            BiteMultiplier = context!.Config.FishingBiteTimeMultiplier,
            DurationMultiplier = context.Config.FishingMinigameDurationMultiplier
        };

        private static void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            if (Context.IsMainPlayer) rules = CurrentRules();
            else RequestRules();
        }

        private static void OnSecond(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (Context.IsWorldReady && !Context.IsMainPlayer && rules == null) RequestRules();
        }

        private static void RequestRules()
        {
            if (context != null && Game1.MasterPlayer != null)
                context.Helper.Multiplayer.SendMessage(string.Empty, Request, new[] { context.Manifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        }

        private static void OnMessage(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || !Context.IsWorldReady || e.FromModID != context.Manifest.UniqueID) return;
            if (Context.IsMainPlayer && e.Type == Request && Game1.GetPlayer(e.FromPlayerID) != null)
                context.Helper.Multiplayer.SendMessage(CurrentRules(), Response, new[] { context.Manifest.UniqueID }, new[] { e.FromPlayerID });
            else if (!Context.IsMainPlayer && e.Type == Response && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID && rules == null)
            {
                Rules value = e.ReadAs<Rules>();
                if (value != null && double.IsFinite(value.BiteMultiplier) && value.BiteMultiplier > 0
                    && double.IsFinite(value.DurationMultiplier) && value.DurationMultiplier > 0)
                    rules = value;
            }
        }

        private static bool IsFestivalContest() => Game1.isFestival() || Game1.currentMinigame != null;

        public static void PostfixBiteTime(Vector2 __0, ref float __result)
        {
            if (rules == null || IsFestivalContest() || Game1.currentLocation == null) return;
            if (Game1.currentLocation.isTileBuildingFishable((int)__0.X, (int)__0.Y)
                && Game1.currentLocation.getBuildingAt(__0) is FishPond pond && pond.currentOccupants.Value > 0) return;
            __result = (float)Math.Min(float.MaxValue, __result * rules.BiteMultiplier);
        }

        public static float ScaleProgressDelta(float delta) => rules == null || IsFestivalContest()
            ? delta : (float)(delta / rules.DurationMultiplier);

        public static IEnumerable<CodeInstruction> TranspileBobberUpdate(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            FieldInfo field = AccessTools.Field(typeof(BobberBar), nameof(BobberBar.distanceFromCatching));
            var matches = new List<int>();
            for (int i = 1; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Stfld && Equals(code[i].operand, field)
                    && (code[i - 1].opcode == OpCodes.Add || code[i - 1].opcode == OpCodes.Sub)
                    && code[i - 1].labels.Count == 0)
                    matches.Add(i - 1);
            }
            if (matches.Count != 3)
            {
                context?.Monitor.Log($"[FishingRebalance] No se reconocieron las tres variaciones de captura ({matches.Count}); se conserva vanilla.", LogLevel.Error);
                return code;
            }
            for (int i = matches.Count - 1; i >= 0; i--)
                code.Insert(matches[i], new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FishingRebalanceModule), nameof(ScaleProgressDelta))));
            return code;
        }

        public sealed class Rules
        {
            public double BiteMultiplier { get; set; }
            public double DurationMultiplier { get; set; }
        }
    }
}
