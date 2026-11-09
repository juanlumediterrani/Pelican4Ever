using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Netcode;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Locations;

namespace Pelican4Ever.Modules
{
    /// <summary>Elección del residente y adaptación de la cueva vanilla a cada ubicación privada.</summary>
    internal sealed class PrivateFarmCaves
    {
        private const string ChoiceKey = "Pelican4Ever.CaveChoice";
        private const string MushroomsInstalledKey = "Pelican4Ever.CaveMushroomsInstalled";
        private const string ChooseCaveMessage = "ChoosePrivateFarmCave";
        private const string CaveSuffix = "_Cave";
        private const string EventStrings = "Strings\\StringsFromCSFiles:Event.cs.";

        private readonly ModContext context;
        private readonly PerScreen<FarmCave?> promptedCave = new();

        public PrivateFarmCaves(ModContext context)
        {
            this.context = context;
        }

        public void Register()
        {
            var readChoice = new HarmonyMethod(typeof(PrivateFarmCaves), nameof(Transpiler_ReadChoice));
            foreach (string method in new[] { "resetLocalState", nameof(FarmCave.UpdateWhenCurrentLocation), nameof(FarmCave.DayUpdate) })
                context.Harmony.Patch(AccessTools.DeclaredMethod(typeof(FarmCave), method), transpiler: readChoice);

            context.Harmony.Patch(
                AccessTools.DeclaredMethod(typeof(FarmCave), nameof(FarmCave.setUpMushroomHouse)),
                transpiler: new HarmonyMethod(typeof(PrivateFarmCaves), nameof(Transpiler_PlaceMushroomEquipment)));
            context.Harmony.Patch(
                AccessTools.DeclaredMethod(typeof(FarmCave), nameof(FarmCave.UpdateReadyFlag)),
                prefix: new HarmonyMethod(typeof(PrivateFarmCaves), nameof(Prefix_UpdateReadyFlag)));

            context.Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            context.Helper.Events.GameLoop.ReturnedToTitle += (_, _) => promptedCave.Value = null;
            context.Helper.Events.Player.Warped += (_, e) =>
            {
                if (e.IsLocalPlayer)
                    promptedCave.Value = null;
            };
            context.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!e.IsMultipleOf(4) || !Context.IsPlayerFree || Game1.currentLocation is not FarmCave cave
                || ReferenceEquals(promptedCave.Value, cave) || !cave.modData.TryGetValue(ChoiceKey, out string? choice)
                || choice != nameof(FarmCaveChoice.None)
                || FindResidence(cave)?.OwnerId != Game1.player.UniqueMultiplayerID)
                return;

            promptedCave.Value = cave;
            QuestionDialog.Ask(
                Game1.content.LoadString(EventStrings + "1223"),
                new[]
                {
                    new Response(nameof(FarmCaveChoice.Mushrooms), Game1.content.LoadString(EventStrings + "1220")),
                    new Response(nameof(FarmCaveChoice.FruitBats), Game1.content.LoadString(EventStrings + "1222"))
                },
                (who, answer) =>
                {
                    if (!ReferenceEquals(Game1.currentLocation, cave) || !Enum.TryParse(answer, out FarmCaveChoice selected))
                        return;

                    var request = new CaveChoiceRequest { CaveName = cave.NameOrUniqueName, Choice = selected };
                    if (Context.IsMainPlayer)
                        ChooseCave(who.UniqueMultiplayerID, request);
                    else
                        context.Helper.Multiplayer.SendMessage(request, ChooseCaveMessage,
                            new[] { context.Manifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
                });
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (Context.IsMainPlayer && Context.IsWorldReady && e.FromModID == context.Manifest.UniqueID
                && e.Type == ChooseCaveMessage && e.ReadAs<CaveChoiceRequest>() is { } request)
                ChooseCave(e.FromPlayerID, request);
        }

        private void ChooseCave(long playerId, CaveChoiceRequest request)
        {
            if (request.Choice is not (FarmCaveChoice.FruitBats or FarmCaveChoice.Mushrooms)
                || string.IsNullOrEmpty(request.CaveName)
                || Game1.getLocationFromName(request.CaveName) is not FarmCave cave
                || FindResidence(cave) is not { } residence || residence.OwnerId != playerId)
                return;

            PlayerRecord player = context.WorldState.GetOrCreatePlayer(playerId, Game1.GetPlayer(playerId)?.Name ?? string.Empty);
            if (player.CaveChoice == FarmCaveChoice.None)
                player.CaveChoice = request.Choice;

            ApplyChoice(context.WorldState, cave, playerId);
            foreach (FarmCave otherCave in Game1.locations.OfType<FarmCave>())
            {
                if (!ReferenceEquals(otherCave, cave) && FindResidence(otherCave) == residence)
                    ApplyChoice(context.WorldState, otherCave, playerId);
            }
        }

        private DoorAssignment? FindResidence(FarmCave cave)
        {
            if (!IsPrivateCave(cave))
                return null;

            string location = cave.NameOrUniqueName[..^CaveSuffix.Length];
            if (location.StartsWith(LocationCullingLogic.AnnexPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var annex = context.WorldState.FindAnnexById(location);
                if (annex == null || !string.Equals(annex.TemplateId, "Combat", StringComparison.OrdinalIgnoreCase))
                    return null;
                location = annex.ResidenceSlotId;
            }

            return context.WorldState.Doors.Values.FirstOrDefault(d =>
                d.OwnerId != 0 && string.Equals(d.FarmSlotId, location, StringComparison.OrdinalIgnoreCase));
        }

        internal static void ApplyChoice(ServerWorldState state, FarmCave cave, long ownerId)
        {
            if (ownerId == 0)
            {
                cave.modData.Remove(ChoiceKey);
                return;
            }

            FarmCaveChoice choice = state.Players.GetValueOrDefault(ownerId)?.CaveChoice ?? FarmCaveChoice.None;
            if (choice == FarmCaveChoice.Mushrooms && !cave.modData.ContainsKey(MushroomsInstalledKey))
            {
                // Una cueva antigua puede tener ya las cajas fijas. Respeta su contenido y el deshidratador original.
                if (!cave.objects.Values.Any(o => o.QualifiedItemId == "(BC)128" && o.Fragility == 2))
                    cave.setUpMushroomHouse();
                cave.modData[MushroomsInstalledKey] = "true";
            }

            cave.modData[ChoiceKey] = choice.ToString();
        }

        private static bool IsPrivateCave(FarmCave cave)
            => LocationCullingLogic.IsPrivateFarmOrAnnex(cave.NameOrUniqueName)
                && cave.NameOrUniqueName.EndsWith(CaveSuffix, StringComparison.OrdinalIgnoreCase);

        private static int GetCaveChoice(FarmCave cave)
        {
            if (!IsPrivateCave(cave))
                return Game1.MasterPlayer.caveChoice.Value;

            return cave.modData.TryGetValue(ChoiceKey, out string? raw) && Enum.TryParse(raw, out FarmCaveChoice choice)
                ? (int)choice
                : (int)FarmCaveChoice.None;
        }

        // Conserva las rutinas vanilla de producción, efectos y sonido leyendo el modo de esta cueva.
        private static IEnumerable<CodeInstruction> Transpiler_ReadChoice(IEnumerable<CodeInstruction> instructions)
        {
            var masterPlayer = AccessTools.PropertyGetter(typeof(Game1), nameof(Game1.MasterPlayer));
            var caveChoice = AccessTools.Field(typeof(Farmer), nameof(Farmer.caveChoice));
            var netValue = AccessTools.PropertyGetter(typeof(NetFieldBase<int, NetInt>), nameof(NetInt.Value));
            var replacement = AccessTools.Method(typeof(PrivateFarmCaves), nameof(GetCaveChoice));

            return new CodeMatcher(instructions)
                .MatchStartForward(new CodeMatch(OpCodes.Call, masterPlayer), new CodeMatch(OpCodes.Ldfld, caveChoice),
                    new CodeMatch(OpCodes.Callvirt, netValue))
                .ThrowIfInvalid("No se encontró la lectura vanilla de caveChoice.")
                .SetAndAdvance(OpCodes.Ldarg_0, null)
                .SetAndAdvance(OpCodes.Call, replacement)
                .SetAndAdvance(OpCodes.Nop, null)
                .InstructionEnumeration();
        }

        private static IEnumerable<CodeInstruction> Transpiler_PlaceMushroomEquipment(IEnumerable<CodeInstruction> instructions)
        {
            var setObject = AccessTools.Method(typeof(GameLocation), nameof(GameLocation.setObject));
            var replacement = AccessTools.Method(typeof(PrivateFarmCaves), nameof(PlaceMushroomEquipment));
            return instructions.MethodReplacer(setObject, replacement);
        }

        private static void PlaceMushroomEquipment(FarmCave cave, Vector2 tile, StardewValley.Object item)
        {
            if (!IsPrivateCave(cave) || !cave.objects.ContainsKey(tile))
                cave.setObject(tile, item);
        }

        // El indicador vanilla pertenece a la granja compartida.
        private static bool Prefix_UpdateReadyFlag(FarmCave __instance) => !IsPrivateCave(__instance);

        private sealed class CaveChoiceRequest
        {
            public string CaveName { get; set; } = string.Empty;
            public FarmCaveChoice Choice { get; set; }
        }
    }
}
