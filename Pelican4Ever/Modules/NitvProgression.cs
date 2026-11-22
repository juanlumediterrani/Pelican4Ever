using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Framework;
using StardewValley;
using StardewValley.Delegates;
using StardewValley.GameData.Shops;
using StardewValley.Menus;
using StardewValley.Mods;

namespace Pelican4Ever.Modules
{
    internal static class NitvProgression
    {
        private const string DonationPrefix = "NatureInTheValley/Donated/";
        private const string BaseRewardMailPrefix = "InsecReward.";
        private const string BaseRewardPrefix = "Pelican4Ever/NITV/BaseReward/";
        private const string PackRewardPrefix = "Pelican4Ever/NITV/Reward/";
        private const string RewardMarker = "Pelican4Ever/NITV/PendingReward";

        public static void Apply(ModContext context, Type entryType)
        {
            context.PatchRegistry.TryExecute(context.Monitor, "FarmSlot.NiTV.PlayerProgression",
                "FarmSlot", PatchCriticality.Experience, () => ApplyPatches(context.Harmony, entryType));
        }

        internal static void ApplyPatches(Harmony harmony, Type entryType)
        {
            Type GetMenu(string name) => entryType.Assembly.GetType($"NatureInTheValley.{name}", throwOnError: true)!;
            Type donationMenu = GetMenu("CreatureDonationMenu");
            Type mobileMenu = GetMenu("MobileCreatureDonationMenu");
            Type terrariumMenu = GetMenu("CreatureTerrariumMenu");
            var tryGetDonation = AccessTools.Method(entryType, "TryGetInsectariumDonated");

            // All readers and writers must agree on the storage location, including the console commands.
            var patches = new (MethodBase? Target, string Patch, HarmonyPatchType Kind)[]
            {
                (tryGetDonation, nameof(UsePlayerDonations), HarmonyPatchType.Transpiler),
                (AccessTools.Method(entryType, "IsInsectariumDonated"), nameof(UsePlayerDonations), HarmonyPatchType.Transpiler),
                (AccessTools.Method(entryType, "setUpInsectarium"), nameof(UsePlayerDonations), HarmonyPatchType.Transpiler),
                (AccessTools.Method(entryType, "CommandTwo"), nameof(UsePlayerDonations), HarmonyPatchType.Transpiler),
                (AccessTools.Method(entryType, "CommandThree"), nameof(UsePlayerDonations), HarmonyPatchType.Transpiler),
                (AccessTools.Method(donationMenu, "CheckDonated"), nameof(UsePlayerDonations), HarmonyPatchType.Transpiler),
                (AccessTools.DeclaredMethod(donationMenu, "receiveLeftClick"), nameof(UsePlayerDonations), HarmonyPatchType.Transpiler),
                (AccessTools.Method(mobileMenu, "CheckDonated"), nameof(UsePlayerDonations), HarmonyPatchType.Transpiler),
                (AccessTools.Method(mobileMenu, "Donated"), nameof(UsePlayerDonations), HarmonyPatchType.Transpiler),
                (AccessTools.Method(terrariumMenu, "CheckDonated"), nameof(UsePlayerDonations), HarmonyPatchType.Transpiler),
                (AccessTools.Method(entryType, "DonatedTotalGSQ"), nameof(UseQueryPlayer), HarmonyPatchType.Transpiler),
                (AccessTools.Method(entryType, "DonatedSpecificGSQ"), nameof(UseQueryPlayer), HarmonyPatchType.Transpiler),
                (AccessTools.Method(entryType, "getRewardsForPlayer"), nameof(UsePlayerRewards), HarmonyPatchType.Transpiler),
                (AccessTools.Method(entryType, "OnRewardCollected"), nameof(RecordPackReward), HarmonyPatchType.Prefix),
                (AccessTools.Constructor(mobileMenu, Type.EmptyTypes), nameof(UseLocalDonationStock), HarmonyPatchType.Postfix)
            };
            if (patches.Any(patch => patch.Target == null))
                throw new MissingMethodException("No se encontraron todos los métodos del Insectario de NITV.");

            var attempted = new List<(MethodBase Target, MethodInfo Patch)>();
            try
            {
                foreach (var (target, patchName, kind) in patches)
                {
                    var patch = AccessTools.Method(typeof(NitvProgression), patchName);
                    var method = new HarmonyMethod(patch);
                    attempted.Add((target!, patch));
                    harmony.Patch(target!, prefix: kind == HarmonyPatchType.Prefix ? method : null,
                        postfix: kind == HarmonyPatchType.Postfix ? method : null,
                        transpiler: kind == HarmonyPatchType.Transpiler ? method : null);
                }
            }
            catch
            {
                foreach (var (target, patch) in attempted.AsEnumerable().Reverse())
                    harmony.Unpatch(target, patch);
                throw;
            }
        }

        private static IEnumerable<CodeInstruction> UsePlayerDonations(
            IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var codes = instructions.ToList();
            var getLocationData = AccessTools.PropertyGetter(typeof(GameLocation), nameof(GameLocation.modData));
            var getPlayerData = AccessTools.Method(typeof(NitvProgression), nameof(GetPlayerDonationData));
            int expected = original.Name is "setUpInsectarium" or "CheckDonated" ? 1 : 2;
            var getters = codes.Where(code => code.Calls(getLocationData)).ToList();
            if (getters.Count != expected)
                throw new InvalidOperationException($"Cambió el acceso a donaciones en {original.DeclaringType?.Name}.{original.Name}.");

            foreach (CodeInstruction code in getters)
            {
                code.opcode = OpCodes.Call;
                code.operand = getPlayerData;
            }
            return codes;
        }

        private static ModDataDictionary GetPlayerDonationData(GameLocation _) => Game1.player.modData;

        private static bool TryGetPlayerDonation(string creature, out string? value, Farmer who) =>
            who.modData.TryGetValue(DonationPrefix + creature, out value);

        private static bool TryGetQueryDonation(string creature, out string? value, GameStateQueryContext context) =>
            TryGetPlayerDonation(creature, out value, context.Player ?? Game1.player);

        private static IEnumerable<CodeInstruction> UseQueryPlayer(
            IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var codes = instructions.ToList();
            var tryGetDonation = AccessTools.Method(original.DeclaringType, "TryGetInsectariumDonated");
            var call = codes.Single(code => code.Calls(tryGetDonation));
            ReplaceCall(codes, call, nameof(TryGetQueryDonation), new CodeInstruction(OpCodes.Ldarg_1));
            return codes;
        }

        private static IEnumerable<CodeInstruction> UsePlayerRewards(
            IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var codes = instructions.ToList();
            var tryGetDonation = AccessTools.Method(original.DeclaringType, "TryGetInsectariumDonated");
            var donationCalls = codes.Where(code => code.Calls(tryGetDonation)).ToList();
            if (donationCalls.Count != 2)
                throw new InvalidOperationException("Cambió la comprobación de donaciones para las recompensas de NITV.");

            foreach (CodeInstruction call in donationCalls)
                ReplaceCall(codes, call, nameof(TryGetPlayerDonation), new CodeInstruction(OpCodes.Ldarg_0));

            // In NITV 1.3.4 the first mail check and ItemRegistry.Create belong to the pack-reward loop.
            // Use the dictionary entry's key so two milestones can reward the same item independently.
            int itemId = codes.FindIndex(code => code.operand is MethodInfo method
                && method.Name == "get_ItemId" && method.DeclaringType?.FullName == "NatureInTheValley.RewardModel");
            var hasMail = AccessTools.Method(typeof(Farmer), nameof(Farmer.hasOrWillReceiveMail), new[] { typeof(string) });
            int claimCheck = codes.FindIndex(code => code.Calls(hasMail));
            int createItem = codes.FindIndex(code => code.operand is MethodInfo method
                && method.DeclaringType == typeof(ItemRegistry) && method.Name == nameof(ItemRegistry.Create)
                && !method.IsGenericMethod);
            if (itemId < 2 || claimCheck != itemId + 2 || createItem <= claimCheck
                || codes[itemId - 2].opcode != OpCodes.Ldloca_S
                || codes[itemId - 1].operand is not MethodInfo getValue || getValue.Name != "get_Value")
                throw new InvalidOperationException("Cambió la lectura de recompensas de los packs de NITV.");

            var rewardEntry = codes[itemId - 2];
            var getKey = AccessTools.PropertyGetter(getValue.DeclaringType, "Key");
            var createCall = codes[createItem];
            ReplaceCall(codes, codes[claimCheck], nameof(HasPackReward),
                new CodeInstruction(rewardEntry.opcode, rewardEntry.operand), new CodeInstruction(OpCodes.Call, getKey));
            ReplaceCall(codes, createCall, nameof(CreatePackReward),
                new CodeInstruction(rewardEntry.opcode, rewardEntry.operand), new CodeInstruction(OpCodes.Call, getKey));
            return codes;
        }

        private static void ReplaceCall(List<CodeInstruction> codes, CodeInstruction call, string replacement,
            params CodeInstruction[] args)
        {
            int index = codes.IndexOf(call);
            args[0].labels.AddRange(call.labels);
            args[0].blocks.AddRange(call.blocks);
            call.labels.Clear();
            call.blocks.Clear();
            codes.InsertRange(index, args);
            call.opcode = OpCodes.Call;
            call.operand = AccessTools.Method(typeof(NitvProgression), replacement);
        }

        private static bool HasPackReward(Farmer who, string legacyMail, string key)
        {
            if (who.modData.ContainsKey(PackRewardPrefix + key))
                return true;

            // Old NITV builds recorded all rewards by item ID, without the originating milestone.
            string itemId = ItemRegistry.QualifyItemId(legacyMail[BaseRewardMailPrefix.Length..]);
            return !who.modData.ContainsKey(BaseRewardPrefix + itemId)
                && (who.hasOrWillReceiveMail(legacyMail) || who.hasOrWillReceiveMail(BaseRewardMailPrefix + itemId));
        }

        private static Item CreatePackReward(string itemId, int count, int quality, bool allowNull, string key)
        {
            Item reward = ItemRegistry.Create(itemId, count, quality, allowNull);
            reward.modData[RewardMarker] = key;
            return reward;
        }

        private static bool RecordPackReward(Item item, Farmer who)
        {
            if (item == null)
                return true;

            if (!item.modData.TryGetValue(RewardMarker, out string key))
            {
                if (!who.hasOrWillReceiveMail(BaseRewardMailPrefix + item.QualifiedItemId))
                    who.modData[BaseRewardPrefix + item.QualifiedItemId] = "true";
                return true;
            }

            who.modData[PackRewardPrefix + key] = "true";
            item.modData.Remove(RewardMarker);
            return false;
        }

        private static void UseLocalDonationStock(ShopMenu __instance)
        {
            foreach (ItemStockInformation stock in __instance.itemPriceAndStock.Values)
                stock.LimitedStockMode = LimitedStockMode.None;
        }
    }
}
