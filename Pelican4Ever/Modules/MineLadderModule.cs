using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;

namespace Pelican4Ever.Modules
{
    /// <summary>Ajusta las tiradas concretas de escalera sin interferir en las salidas garantizadas.</summary>
    public sealed class MineLadderModule : IModule
    {
        private const string Request = "MineLadderRulesRequest";
        private const string Response = "MineLadderRules";
        private static ModContext? context;
        private static Rules? rules;
        public string Name => "MineLadders";
        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;
            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += OnSecond;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += (_, _) => rules = null;
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnMessage;
            ctx.Harmony.Patch(AccessTools.Method(typeof(MineShaft), nameof(MineShaft.checkStoneForItems)),
                transpiler: new HarmonyMethod(typeof(MineLadderModule), nameof(TranspileStone)));
            ctx.Harmony.Patch(AccessTools.Method(typeof(MineShaft), nameof(MineShaft.monsterDrop)),
                transpiler: new HarmonyMethod(typeof(MineLadderModule), nameof(TranspileMonster)));
        }

        private static Rules CurrentRules() => new()
        {
            StoneMultiplier = context!.Config.MineStoneLadderChanceMultiplier,
            MonsterChance = context.Config.MineMonsterLadderChance
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
                if (value != null && double.IsFinite(value.StoneMultiplier) && value.StoneMultiplier >= 0
                    && double.IsFinite(value.MonsterChance) && value.MonsterChance is >= 0 and <= 1)
                    rules = value;
            }
        }

        public static double ScaleStoneChance(double chance) => chance * (rules?.StoneMultiplier ?? 1);
        public static double MonsterChance() => rules?.MonsterChance ?? 0.15;

        public static IEnumerable<CodeInstruction> TranspileStone(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            MethodInfo nextDouble = AccessTools.Method(typeof(Random), nameof(Random.NextDouble));
            MethodInfo shouldCreate = AccessTools.Method(typeof(MineShaft), "shouldCreateLadderOnThisLevel");
            var matches = new List<int>();
            for (int i = 0; i + 5 < code.Count; i++)
            {
                if (code[i].Calls(nextDouble) && code[i + 1].IsLdloc() && code[i + 2].opcode.FlowControl == FlowControl.Cond_Branch
                    && code[i + 2].labels.Count == 0 && code.Skip(i + 3).Take(5).Any(c => c.Calls(shouldCreate)))
                    matches.Add(i + 2);
            }
            if (matches.Count != 1)
            {
                context?.Monitor.Log($"[MineLadders] No se ha reconocido la tirada de piedra ({matches.Count} coincidencias); se conserva vanilla.", LogLevel.Error);
                return code;
            }
            code.Insert(matches[0], new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MineLadderModule), nameof(ScaleStoneChance))));
            return code;
        }

        public static IEnumerable<CodeInstruction> TranspileMonster(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            MethodInfo nextDouble = AccessTools.Method(typeof(Random), nameof(Random.NextDouble));
            var matches = new List<int>();
            for (int i = 1; i + 2 < code.Count; i++)
            {
                if (code[i - 1].Calls(nextDouble) && code[i].opcode == OpCodes.Ldc_R8 && code[i].operand is double value
                    && value == 0.15 && code[i + 1].IsLdloc() && code[i + 2].opcode == OpCodes.Add)
                    matches.Add(i);
            }
            if (matches.Count != 1)
            {
                context?.Monitor.Log($"[MineLadders] No se ha reconocido la tirada de monstruo ({matches.Count} coincidencias); se conserva vanilla.", LogLevel.Error);
                return code;
            }
            code[matches[0]].opcode = OpCodes.Call;
            code[matches[0]].operand = AccessTools.Method(typeof(MineLadderModule), nameof(MonsterChance));
            return code;
        }

        public sealed class Rules
        {
            public double StoneMultiplier { get; set; }
            public double MonsterChance { get; set; }
        }
    }
}
