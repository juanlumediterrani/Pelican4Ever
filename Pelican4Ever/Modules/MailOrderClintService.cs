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
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.Tools;

namespace Pelican4Ever.Modules
{
    public sealed partial class MailOrderModule
    {
        private static bool clintUpgradeMailSessionActive;
        private static bool clintGeodeMailSessionActive;
        private static readonly List<MailOrderItemLine> activeGeodeBuffer = new();

        private static void ResetClintSession()
        {
            clintUpgradeMailSessionActive = false;
            clintGeodeMailSessionActive = false;
            activeGeodeBuffer.Clear();
        }

        private static void ApplyClintPatches(Harmony harmony)
        {
            PatchSafe(harmony, "Tool.actionWhenPurchased",
                AccessTools.Method(typeof(Tool), nameof(Tool.actionWhenPurchased), new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_Tool_ActionWhenPurchased)));

            PatchSafe(harmony, "GeodeMenu.update",
                AccessTools.Method(typeof(GeodeMenu), nameof(GeodeMenu.update), new[] { typeof(GameTime) }),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_GeodeMenu_Update)));
        }

        private static void ShowClintMailMenu()
        {
            if (Game1.currentLocation == null)
                return;

            var choices = new List<Response>
            {
                new("Shop", I18n.Get("mailbox.orders.clint.shop")),
                new("Upgrade", I18n.Get("mailbox.orders.clint.upgrade")),
                new("Geodes", I18n.Get("mailbox.orders.clint.geodes")),
                new("Cancel", I18n.Get("dialog.cancel"))
            };

            QuestionDialog.Ask(
                I18n.Get("mailbox.orders.clint.prompt"),
                choices.ToArray(),
                (who, answer) =>
                {
                    if (answer == "Shop")
                    {
                        OpenDeferredShopMenu("Blacksmith", "Clint");
                    }
                    else if (answer == "Upgrade")
                    {
                        OpenClintUpgradeMailMenu();
                    }
                    else if (answer == "Geodes")
                    {
                        activeGeodeBuffer.Clear();
                        clintGeodeMailSessionActive = true;
                        Game1.activeClickableMenu = new GeodeMenu();
                    }
                }
            );
        }

        private static void OpenClintUpgradeMailMenu()
        {
            if (Game1.player.toolBeingUpgraded.Value != null)
            {
                if (Game1.player.daysLeftForToolUpgrade.Value > 0)
                {
                    Game1.drawObjectDialogue(I18n.Get("mailbox.orders.clint.still_forging", new
                    {
                        tool = Game1.player.toolBeingUpgraded.Value.DisplayName,
                        days = Game1.player.daysLeftForToolUpgrade.Value
                    }));
                }
                else
                {
                    Game1.drawObjectDialogue(I18n.Get("mailbox.orders.clint.ready_at_noon", new
                    {
                        tool = Game1.player.toolBeingUpgraded.Value.DisplayName
                    }));
                }
                return;
            }

            clintUpgradeMailSessionActive = true;
            if (!Utility.TryOpenShopMenu("ClintUpgrade", "Clint", playOpenSound: true))
            {
                clintUpgradeMailSessionActive = false;
                Game1.drawObjectDialogue(I18n.Get("mailbox.orders.shop_unavailable"));
                return;
            }

            if (Game1.activeClickableMenu is ShopMenu shopMenu)
                ConfigureMailOrderShopMenu(shopMenu);
        }

        public static bool Prefix_Tool_ActionWhenPurchased(Tool __instance, string shopId, ref bool __result)
        {
            if (!clintUpgradeMailSessionActive || !string.Equals(shopId, "ClintUpgrade", StringComparison.Ordinal))
                return true;

            clintUpgradeMailSessionActive = false;

            if (Game1.player.toolBeingUpgraded.Value != null)
            {
                __result = false;
                return false;
            }

            if (__instance is Axe || __instance is Pickaxe || __instance is Hoe || __instance is WateringCan || __instance is Pan)
            {
                string? requireToolId = StardewValley.Internal.ShopBuilder.GetToolUpgradeData(__instance.GetToolData(), Game1.player)?.RequireToolId;
                if (requireToolId != null)
                {
                    Item? baseTool = Game1.player.Items.GetById(requireToolId).FirstOrDefault();
                    if (baseTool != null)
                    {
                        Game1.player.removeItemFromInventory(baseTool);
                        if (baseTool is Tool other)
                            __instance.UpgradeFrom(other);
                    }
                }

                Game1.player.toolBeingUpgraded.Value = (Tool)__instance.getOne();
                Game1.player.daysLeftForToolUpgrade.Value = 2;
                Game1.playSound("parry");
                Game1.exitActiveMenu();
                Game1.drawObjectDialogue(I18n.Get("mailbox.orders.clint.upgrade_scheduled", new { tool = __instance.DisplayName }));
                __result = true;
                return false;
            }

            if (__instance is GenericTool)
            {
                int idx = __instance.IndexOfMenuItemView;
                if ((uint)(idx - 13) <= 3u)
                {
                    Game1.player.toolBeingUpgraded.Value = (Tool)__instance.getOne();
                    Game1.player.daysLeftForToolUpgrade.Value = 2;
                    Game1.playSound("parry");
                    Game1.exitActiveMenu();
                    Game1.drawObjectDialogue(I18n.Get("mailbox.orders.clint.upgrade_scheduled", new { tool = __instance.DisplayName }));
                    __result = true;
                    return false;
                }
            }

            return true;
        }

        public static void Prefix_GeodeMenu_Update(GeodeMenu __instance, GameTime time)
        {
            if (clintGeodeMailSessionActive && Game1.activeClickableMenu is not GeodeMenu)
            {
                FlushGeodeBuffer();
                return;
            }

            if (!clintGeodeMailSessionActive || __instance.geodeAnimationTimer <= 0)
                return;

            if (__instance.geodeAnimationTimer - time.ElapsedGameTime.Milliseconds <= 0 && __instance.geodeTreasure != null)
            {
                Item treasure = __instance.geodeTreasure;
                if (treasure.QualifiedItemId == "(O)73")
                    Game1.netWorldState.Value.GoldenCoconutCracked = true;

                activeGeodeBuffer.Add(new MailOrderItemLine
                {
                    QualifiedItemId = treasure.QualifiedItemId,
                    DisplayName = treasure.DisplayName,
                    Stack = treasure.Stack,
                    Quality = treasure.Quality,
                    PricePaid = 25
                });

                Game1.addHUDMessage(new HUDMessage(
                    I18n.Get("mailbox.orders.item_queued", new { item = treasure.DisplayName, count = treasure.Stack }),
                    HUDMessage.newQuest_type));

                __instance.geodeTreasure = null;
            }
        }

        private static void FlushGeodeBuffer()
        {
            clintGeodeMailSessionActive = false;
            if (activeGeodeBuffer.Count == 0 || context == null)
            {
                activeGeodeBuffer.Clear();
                return;
            }

            List<MailOrderItemLine> lines = activeGeodeBuffer.ToList();
            activeGeodeBuffer.Clear();

            if (Context.IsMainPlayer)
            {
                EnqueueItemOrderOnHost(Game1.player.UniqueMultiplayerID, "Blacksmith", lines);
            }
            else
            {
                context.Helper.Multiplayer.SendMessage(
                    new PlaceMailItemOrderPayload { ShopId = "Blacksmith", Items = lines },
                    MailOrderMessages.PlaceItemOrderRequest,
                    new[] { context.Manifest.UniqueID },
                    new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );
            }
        }

        private static void TryDeliverCompletedToolUpgrades()
        {
            if (context == null || !Context.IsMainPlayer || RealTimeClockModule.IsNightHold || Game1.timeOfDay < MailOrderScheduleLogic.DefaultDeliveryTimeOfDay)
                return;

            foreach (Farmer farmer in Game1.getAllFarmers())
            {
                if (farmer == null || farmer.toolBeingUpgraded.Value == null || farmer.daysLeftForToolUpgrade.Value > 0)
                    continue;

                Farm? farm = ResolvePlayerResidenceFarm(farmer.UniqueMultiplayerID);
                if (farm == null)
                    continue;

                Tool upgradedTool = (Tool)farmer.toolBeingUpgraded.Value.getOne();
                Point mb = farm.GetMainMailboxPosition();
                Point door = farm.GetMainFarmHouseEntry();
                (int X, int Y)? spot = MailPackagePlacementLogic.FindFreePackageTile(
                    mb.X, mb.Y, door.X, door.Y,
                    (x, y) => IsTileFreeForPackage(farm, x, y),
                    maxRadius: 5);

                if (spot == null)
                    spot = (mb.X + 1, mb.Y);

                var tile = new Vector2(spot.Value.X, spot.Value.Y);
                var chest = new Chest(playerChest: false, tile) { TileLocation = tile };
                chest.giftbox.Value = true;
                chest.giftboxIndex.Value = MailOrderCatalogLogic.GetGiftboxIndex("Blacksmith");
                chest.giftboxIsStarterGift.Value = false;
                chest.Items.Add(upgradedTool);

                chest.modData[MailPackageKeys.IsMailPackage] = "1";
                chest.modData[MailPackageKeys.OwnerId] = farmer.UniqueMultiplayerID.ToString();
                chest.modData[MailPackageKeys.OrderId] = Guid.NewGuid().ToString("N");
                chest.modData[MailPackageKeys.ShopId] = "ClintUpgrade";

                farm.objects[tile] = chest;
                farmer.toolBeingUpgraded.Value = null;
                farmer.hasReceivedToolUpgradeMessageYet = false;

                context.Monitor.Log($"[MailOrder] Herramienta mejorada '{upgradedTool.DisplayName}' entregada en paquete en {farm.NameOrUniqueName} ({spot.Value.X}, {spot.Value.Y}) para {farmer.Name}.", LogLevel.Info);
                NotifyPlayerIfOnline(farmer.UniqueMultiplayerID, I18n.Get("mailbox.orders.clint.tool_arrived", new { tool = upgradedTool.DisplayName }));
            }
        }
    }
}
