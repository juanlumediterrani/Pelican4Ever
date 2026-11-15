using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Economy;
using Pelican4Ever.Framework;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Menus;

namespace Pelican4Ever.Modules
{
    public sealed partial class MailOrderModule
    {
        private static bool robinMailSessionActive;

        private static void ResetRobinSession()
        {
            robinMailSessionActive = false;
        }

        private static void ApplyRobinPatches(Harmony harmony)
        {
            PatchSafe(harmony, "CarpenterMenu.tryToBuild",
                AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.tryToBuild)),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_CarpenterMenu_TryToBuild)),
                postfix: new HarmonyMethod(typeof(MailOrderModule), nameof(Postfix_CarpenterMenu_TryToBuild)));

            PatchSafe(harmony, "CarpenterMenu.receiveLeftClick",
                AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.receiveLeftClick), new[] { typeof(int), typeof(int), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_CarpenterMenu_ReceiveLeftClick)),
                postfix: new HarmonyMethod(typeof(MailOrderModule), nameof(Postfix_CarpenterMenu_ReceiveLeftClick)));

            PatchSafe(harmony, "CarpenterMenu.robinConstructionMessage",
                AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.robinConstructionMessage)),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_CarpenterMenu_RobinConstructionMessage)));

            PatchSafe(harmony, "Building.isThereAnythingtoPreventConstruction",
                AccessTools.Method(typeof(Building), nameof(Building.isThereAnythingtoPreventConstruction)),
                postfix: new HarmonyMethod(typeof(MailOrderModule), nameof(Postfix_Building_PreventConstruction)));
        }

        private static void ShowRobinMailMenu()
        {
            if (Game1.currentLocation == null)
                return;

            var choices = new List<Response>
            {
                new("Shop", I18n.Get("mailbox.orders.robin.shop")),
                new("Construct", I18n.Get("mailbox.orders.robin.construct"))
            };

            bool hasPendingHouseUpgrade = GetBuildingOrdersSnapshot().Any(o =>
                o.PlayerId == Game1.player.UniqueMultiplayerID && o.IsHouseUpgrade);

            if (Game1.player.HouseUpgradeLevel < 3
                && Game1.player.daysUntilHouseUpgrade.Value < 0
                && !hasPendingHouseUpgrade)
            {
                choices.Add(new Response("UpgradeHouse", I18n.Get("mailbox.orders.robin.upgrade_house")));
            }

            choices.Add(new Response("Cancel", I18n.Get("dialog.cancel")));

            QuestionDialog.Ask(
                I18n.Get("mailbox.orders.robin.prompt"),
                choices.ToArray(),
                (who, answer) =>
                {
                    if (answer == "Shop")
                    {
                        OpenDeferredShopMenu("Carpenter", "Robin");
                    }
                    else if (answer == "Construct")
                    {
                        robinMailSessionActive = true;
                        Game1.currentLocation.ShowConstructOptions("Robin", 0);
                    }
                    else if (answer == "UpgradeHouse")
                    {
                        ShowMailHouseUpgradeOffer();
                    }
                }
            );
        }

        private static void ShowMailHouseUpgradeOffer()
        {
            if (!FarmSlotModule.EnsureLocalPlayerOwnsResidenceForHouseholdAction(isMarriageAction: false))
                return;

            int level = Game1.player.HouseUpgradeLevel;
            string? offerText = level switch
            {
                0 => Game1.content.LoadString("Strings\\Locations:ScienceHouse_Carpenter_UpgradeHouse1"),
                1 => Game1.content.LoadString("Strings\\Locations:ScienceHouse_Carpenter_UpgradeHouse2", "65,000", 100),
                2 => Game1.content.LoadString("Strings\\Locations:ScienceHouse_Carpenter_UpgradeHouse3"),
                _ => null
            };

            if (offerText == null)
                return;

            Response[] yesNo =
            {
                new("Yes", I18n.Get("dialog.yes")),
                new("No", I18n.Get("dialog.no"))
            };

            QuestionDialog.Ask(
                offerText,
                yesNo,
                (who, answer) =>
                {
                    if (answer == "Yes")
                        AcceptMailHouseUpgrade();
                }
            );
        }

        private static void AcceptMailHouseUpgrade()
        {
            if (!FarmSlotModule.EnsureLocalPlayerOwnsResidenceForHouseholdAction(isMarriageAction: false))
                return;

            int level = Game1.player.HouseUpgradeLevel;
            int goldCost = level switch { 0 => 10000, 1 => 65000, 2 => 100000, _ => 0 };
            string? matId = level switch { 0 => "(O)388", 1 => "(O)709", _ => null };
            int matCount = level switch { 0 => 450, 1 => 100, _ => 0 };

            if (goldCost <= 0)
                return;

            if (Game1.player.Money < goldCost)
            {
                Game1.drawObjectDialogue(Game1.content.LoadString("Strings\\UI:NotEnoughMoney3"));
                return;
            }

            if (matId != null && !Game1.player.Items.ContainsId(matId, matCount))
            {
                string key = level == 0
                    ? "Strings\\Locations:ScienceHouse_Carpenter_NotEnoughWood"
                    : "Strings\\Locations:ScienceHouse_Carpenter_NotEnoughHardwood";
                Game1.drawObjectDialogue(Game1.content.LoadString(key, matCount));
                return;
            }

            Game1.player.Money -= goldCost;
            if (matId != null)
                Game1.player.Items.ReduceId(matId, matCount);

            var order = new PendingBuildingOrder
            {
                PlayerId = Game1.player.UniqueMultiplayerID,
                Builder = "Robin",
                IsHouseUpgrade = true,
                HouseUpgradeLevel = level + 1,
                BuildingDisplayName = $"House Upgrade {level + 1}",
                BuildDays = 3,
                CreatedAtGameDay = Game1.Date?.TotalDays ?? 0,
                DeliveryGameDay = ComputeDeliveryGameDay(),
                DeliveryTimeOfDay = MailOrderScheduleLogic.DefaultDeliveryTimeOfDay
            };

            SubmitBuildingOrder(order);
            Game1.drawObjectDialogue(I18n.Get("mailbox.orders.house_upgrade_scheduled"));
        }

        public static void Postfix_Building_PreventConstruction(Building __instance, GameLocation location, Vector2 tile_location, ref string? __result)
        {
            if (__result != null || location == null || __instance == null)
                return;

            if (MailPackagePlacementLogic.DoesFootprintOverlapPendingBuilding(
                location.NameOrUniqueName,
                (int)tile_location.X,
                (int)tile_location.Y,
                __instance.tilesWide.Value,
                __instance.tilesHigh.Value,
                GetBuildingOrdersSnapshot()))
            {
                __result = I18n.Get("mailbox.orders.building_spot_reserved");
            }
        }

        private static int preBuildCount;

        public static bool Prefix_CarpenterMenu_TryToBuild(CarpenterMenu __instance, GameLocation ___TargetLocation, ref bool __result)
        {
            preBuildCount = ___TargetLocation?.buildings.Count ?? 0;
            return true;
        }

        public static void Postfix_CarpenterMenu_TryToBuild(CarpenterMenu __instance, bool __result, GameLocation ___TargetLocation)
        {
            if (!robinMailSessionActive || !__result || ___TargetLocation == null)
                return;

            if (___TargetLocation.buildings.Count <= preBuildCount)
                return;

            Building built = ___TargetLocation.buildings[___TargetLocation.buildings.Count - 1];
            var order = new PendingBuildingOrder
            {
                PlayerId = Game1.player.UniqueMultiplayerID,
                Builder = __instance.Builder ?? "Robin",
                TargetLocationName = ___TargetLocation.NameOrUniqueName,
                BuildingType = built.buildingType.Value,
                BuildingDisplayName = __instance.Blueprint?.DisplayName ?? built.buildingType.Value,
                TileX = built.tileX.Value,
                TileY = built.tileY.Value,
                TilesWide = built.tilesWide.Value,
                TilesHigh = built.tilesHigh.Value,
                SkinId = built.skinId.Value,
                BuildDays = Math.Max(__instance.Blueprint?.BuildDays ?? 2, 1),
                CreatedAtGameDay = Game1.Date?.TotalDays ?? 0,
                DeliveryGameDay = ComputeDeliveryGameDay(),
                DeliveryTimeOfDay = MailOrderScheduleLogic.DefaultDeliveryTimeOfDay
            };

            // Retirar la construcción inmediata: los andamios aparecerán al día siguiente a las 12:00
            ___TargetLocation.buildings.RemoveAt(___TargetLocation.buildings.Count - 1);
            SubmitBuildingOrder(order);
        }

        private static Building? upgradeCandidateBeforeClick;
        private static string? prevUpgradeName;
        private static int prevDaysUntilUpgrade;

        public static bool Prefix_CarpenterMenu_ReceiveLeftClick(CarpenterMenu __instance, GameLocation ___TargetLocation, int x, int y)
        {
            upgradeCandidateBeforeClick = null;
            if (!robinMailSessionActive || !__instance.onFarm || __instance.freeze)
                return true;

            if (__instance.Action == CarpenterMenu.CarpentryAction.Upgrade && ___TargetLocation != null)
            {
                Vector2 tile = new((Game1.viewport.X + Game1.getOldMouseX(ui_scale: false)) / 64, (Game1.viewport.Y + Game1.getOldMouseY(ui_scale: false)) / 64);
                Building? b = ___TargetLocation.getBuildingAt(tile);
                if (b != null)
                {
                    bool alreadyPendingUpgrade = GetBuildingOrdersSnapshot().Any(o =>
                        o.IsUpgrade
                        && string.Equals(o.TargetLocationName, ___TargetLocation.NameOrUniqueName, StringComparison.OrdinalIgnoreCase)
                        && o.TargetBuildingTileX == b.tileX.Value
                        && o.TargetBuildingTileY == b.tileY.Value);
                    if (alreadyPendingUpgrade)
                    {
                        Game1.addHUDMessage(new HUDMessage(I18n.Get("mailbox.orders.building_spot_reserved"), HUDMessage.error_type));
                        return false;
                    }

                    upgradeCandidateBeforeClick = b;
                    prevUpgradeName = b.upgradeName.Value;
                    prevDaysUntilUpgrade = b.daysUntilUpgrade.Value;
                }
            }

            return true;
        }

        public static void Postfix_CarpenterMenu_ReceiveLeftClick(CarpenterMenu __instance, GameLocation ___TargetLocation)
        {
            if (!robinMailSessionActive || upgradeCandidateBeforeClick == null || ___TargetLocation == null)
                return;

            Building b = upgradeCandidateBeforeClick;
            upgradeCandidateBeforeClick = null;

            if (b.daysUntilUpgrade.Value > 0 && b.daysUntilUpgrade.Value != prevDaysUntilUpgrade)
            {
                string upgradeType = b.upgradeName.Value;
                int days = b.daysUntilUpgrade.Value;

                // Revertir andamios inmediatos; el coste ya fue descontado por ConsumeResources()
                b.upgradeName.Value = prevUpgradeName;
                b.daysUntilUpgrade.Value = prevDaysUntilUpgrade;

                var order = new PendingBuildingOrder
                {
                    PlayerId = Game1.player.UniqueMultiplayerID,
                    Builder = __instance.Builder ?? "Robin",
                    TargetLocationName = ___TargetLocation.NameOrUniqueName,
                    BuildingType = upgradeType,
                    BuildingDisplayName = __instance.Blueprint?.DisplayName ?? upgradeType,
                    IsUpgrade = true,
                    TargetBuildingTileX = b.tileX.Value,
                    TargetBuildingTileY = b.tileY.Value,
                    BuildDays = days,
                    CreatedAtGameDay = Game1.Date?.TotalDays ?? 0,
                    DeliveryGameDay = ComputeDeliveryGameDay(),
                    DeliveryTimeOfDay = MailOrderScheduleLogic.DefaultDeliveryTimeOfDay
                };

                SubmitBuildingOrder(order);
            }
        }

        public static bool Prefix_CarpenterMenu_RobinConstructionMessage(CarpenterMenu __instance)
        {
            if (!robinMailSessionActive)
                return true;

            robinMailSessionActive = false;
            __instance.exitThisMenu();
            Game1.player.forceCanMove();
            Game1.drawObjectDialogue(I18n.Get("mailbox.orders.building_scheduled"));
            return false;
        }

        private static void SubmitBuildingOrder(PendingBuildingOrder order)
        {
            if (context == null)
                return;

            if (Context.IsMainPlayer)
            {
                HandlePlaceBuildingOrderOnHost(Game1.player.UniqueMultiplayerID, order);
            }
            else
            {
                context.Helper.Multiplayer.SendMessage(
                    new PlaceBuildingOrderPayload { Order = order },
                    MailOrderMessages.PlaceBuildingOrderRequest,
                    new[] { context.Manifest.UniqueID },
                    new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );
            }
        }

        private static void HandlePlaceBuildingOrderOnHost(long playerId, PendingBuildingOrder order)
        {
            if (context == null || !Context.IsMainPlayer)
                return;

            order.PlayerId = playerId;
            context.WorldState.PendingBuildingOrders.Add(order);
            context.Monitor.Log($"[MailOrder] Obra diferida '{order.BuildingType}' registrada para {playerId} en '{order.TargetLocationName}' (entrega día {order.DeliveryGameDay} a las 12:00).", LogLevel.Info);
            BroadcastOrdersSync();
        }

        private static bool DeliverBuildingOrderOnHost(PendingBuildingOrder order)
        {
            if (context == null)
                return true;

            if (order.IsHouseUpgrade)
            {
                Farmer? farmer = Game1.GetPlayer(order.PlayerId);
                if (farmer == null)
                    return false;

                if (farmer.daysUntilHouseUpgrade.Value < 0)
                {
                    farmer.daysUntilHouseUpgrade.Value = Math.Max(order.BuildDays, 1);
                }

                NotifyPlayerIfOnline(order.PlayerId, I18n.Get("mailbox.orders.house_upgrade_started"));
                return true;
            }

            GameLocation? location = Game1.getLocationFromName(order.TargetLocationName);
            if (location == null)
                return false;

            if (order.IsUpgrade)
            {
                Building? target = location.getBuildingAt(new Vector2(order.TargetBuildingTileX, order.TargetBuildingTileY));
                if (target == null)
                    return true;

                target.upgradeName.Value = order.BuildingType;
                target.daysUntilUpgrade.Value = Math.Max(order.BuildDays, 1);
                Game1.netWorldState.Value.MarkUnderConstruction(order.Builder, target);
                NotifyPlayerIfOnline(order.PlayerId, I18n.Get("mailbox.orders.building_started", new { building = order.BuildingDisplayName }));
                return true;
            }

            Farmer? owner = Game1.GetPlayer(order.PlayerId) ?? Game1.MasterPlayer;
            var tile = new Vector2(order.TileX, order.TileY);

            // Despejar maleza/objetos superficiales de la huella antes de plantar los andamios
            for (int dy = 0; dy < Math.Max(1, order.TilesHigh); dy++)
            {
                for (int dx = 0; dx < Math.Max(1, order.TilesWide); dx++)
                {
                    location.pokeTileForConstruction(new Vector2(order.TileX + dx, order.TileY + dy));
                }
            }

            if (!location.buildStructure(order.BuildingType, tile, owner, out Building? constructed, magicalConstruction: false, skipSafetyChecks: true))
            {
                context.Monitor.Log($"[MailOrder] No se pudo materializar '{order.BuildingType}' en '{order.TargetLocationName}' ({order.TileX},{order.TileY}).", LogLevel.Warn);
                return false;
            }

            if (constructed != null)
            {
                if (!string.IsNullOrEmpty(order.SkinId))
                    constructed.skinId.Value = order.SkinId;

                constructed.daysOfConstructionLeft.Value = Math.Max(order.BuildDays, 1);
                Game1.netWorldState.Value.MarkUnderConstruction(order.Builder, constructed);

                if (string.Equals(order.BuildingType, "Pet Bowl", StringComparison.OrdinalIgnoreCase))
                {
                    var rec = context.WorldState.GetOrCreatePlayer(order.PlayerId, owner.Name);
                    string petType = string.IsNullOrWhiteSpace(owner.whichPetType) ? (owner.catPerson ? "Cat" : "Dog") : owner.whichPetType;
                    Core.Pets.PetAdoptionLogic.RecordPurchase(
                        rec.PetAdoption,
                        order.TargetLocationName,
                        constructed.tileX.Value,
                        constructed.tileY.Value,
                        Game1.Date?.TotalDays ?? 0,
                        petType,
                        owner.whichPetBreed.ToString(),
                        order.PlayerId);
                }
            }

            NotifyPlayerIfOnline(order.PlayerId, I18n.Get("mailbox.orders.building_started", new { building = order.BuildingDisplayName }));
            return true;
        }
    }
}
