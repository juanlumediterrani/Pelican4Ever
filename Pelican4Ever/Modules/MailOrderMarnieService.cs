using System;
using System.Collections.Generic;
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
        private static bool marnieMailSessionActive;
        private static string? marnieOriginLocationName;
        private static Point marnieOriginTile;
        private static int marnieOriginFacing;

        private static void ResetMarnieSession()
        {
            marnieMailSessionActive = false;
            marnieOriginLocationName = null;
        }

        private static void ApplyMarniePatches(Harmony harmony)
        {
            PatchSafe(harmony, "PurchaseAnimalsMenu.setUpForReturnToShopMenu",
                AccessTools.Method(typeof(PurchaseAnimalsMenu), nameof(PurchaseAnimalsMenu.setUpForReturnToShopMenu)),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_PurchaseAnimals_ReturnToShop)));

            PatchSafe(harmony, "PurchaseAnimalsMenu.setUpForReturnAfterPurchasingAnimal",
                AccessTools.Method(typeof(PurchaseAnimalsMenu), nameof(PurchaseAnimalsMenu.setUpForReturnAfterPurchasingAnimal)),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_PurchaseAnimals_ReturnAfterPurchase)));

            PatchSafe(harmony, "PurchaseAnimalsMenu.marnieAnimalPurchaseMessage",
                AccessTools.Method(typeof(PurchaseAnimalsMenu), nameof(PurchaseAnimalsMenu.marnieAnimalPurchaseMessage)),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_PurchaseAnimals_Message)));

            PatchSafe(harmony, "AnimalHouse.adoptAnimal",
                AccessTools.Method(typeof(AnimalHouse), nameof(AnimalHouse.adoptAnimal)),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_AnimalHouse_AdoptAnimal)));

            PatchSafe(harmony, "AnimalHouse.isFull",
                AccessTools.Method(typeof(AnimalHouse), nameof(AnimalHouse.isFull)),
                postfix: new HarmonyMethod(typeof(MailOrderModule), nameof(Postfix_AnimalHouse_IsFull)));
        }

        private static void ShowMarnieMailMenu()
        {
            if (Game1.currentLocation == null)
                return;

            var choices = new List<Response>
            {
                new("Supplies", I18n.Get("mailbox.orders.marnie.supplies")),
                new("Purchase", I18n.Get("mailbox.orders.marnie.animals")),
                new("Cancel", I18n.Get("dialog.cancel"))
            };

            QuestionDialog.Ask(
                I18n.Get("mailbox.orders.marnie.prompt"),
                choices.ToArray(),
                (who, answer) =>
                {
                    if (answer == "Supplies")
                    {
                        OpenDeferredShopMenu("AnimalShop", "Marnie");
                    }
                    else if (answer == "Purchase")
                    {
                        marnieMailSessionActive = true;
                        marnieOriginLocationName = Game1.currentLocation.NameOrUniqueName;
                        marnieOriginTile = Game1.player.TilePoint;
                        marnieOriginFacing = Game1.player.FacingDirection;
                        Game1.currentLocation.ShowAnimalShopMenu();
                    }
                }
            );
        }

        public static void Postfix_AnimalHouse_IsFull(AnimalHouse __instance, ref bool __result)
        {
            if (__result || !marnieMailSessionActive || __instance == null)
                return;

            Building? building = __instance.ParentBuilding;
            GameLocation? parent = building?.GetParentLocation();
            if (building == null || parent == null)
                return;

            int inTransit = MailPackagePlacementLogic.CountPendingAnimalsInBuilding(
                parent.NameOrUniqueName,
                building.tileX.Value,
                building.tileY.Value,
                GetAnimalOrdersSnapshot());

            if (__instance.animalsThatLiveHere.Count + inTransit >= __instance.animalLimit.Value)
            {
                __result = true;
            }
        }

        public static bool Prefix_AnimalHouse_AdoptAnimal(AnimalHouse __instance, FarmAnimal animal)
        {
            if (!marnieMailSessionActive || Game1.activeClickableMenu is not PurchaseAnimalsMenu menu || animal == null)
                return true;

            Building? building = __instance.ParentBuilding;
            GameLocation? parent = building?.GetParentLocation();
            if (building == null || parent == null)
                return true;

            var order = new PendingAnimalOrder
            {
                PlayerId = Game1.player.UniqueMultiplayerID,
                TargetLocationName = parent.NameOrUniqueName,
                BuildingTileX = building.tileX.Value,
                BuildingTileY = building.tileY.Value,
                AnimalType = animal.type.Value,
                AnimalName = animal.Name,
                AnimalEntityId = animal.myID.Value,
                PricePaid = menu.priceOfAnimal,
                CreatedAtGameDay = Game1.Date?.TotalDays ?? 0,
                DeliveryGameDay = ComputeDeliveryGameDay(),
                DeliveryTimeOfDay = MailOrderScheduleLogic.DefaultDeliveryTimeOfDay
            };

            SubmitAnimalOrder(order);
            return false;
        }

        public static bool Prefix_PurchaseAnimals_ReturnToShop(PurchaseAnimalsMenu __instance)
        {
            if (!marnieMailSessionActive || string.IsNullOrEmpty(marnieOriginLocationName))
                return true;

            __instance.freeze = false;
            if (__instance.upArrow != null && __instance.downArrow != null)
            {
                bool showScroll = PurchaseAnimalsMenu.GetOffScreenRows(__instance.animalsToPurchase.Count) > 0;
                __instance.upArrow.visible = showScroll;
                __instance.downArrow.visible = showScroll;
            }
            if (__instance.doneNamingButton != null)
                __instance.doneNamingButton.visible = false;
            if (__instance.randomButton != null)
                __instance.randomButton.visible = false;

            Game1.displayFarmer = true;
            LocationRequest request = Game1.getLocationRequest(marnieOriginLocationName);
            request.OnWarp += () =>
            {
                __instance.onFarm = false;
                Game1.player.viewingLocation.Value = null;
                if (__instance.okButton != null)
                {
                    __instance.okButton.bounds.X = __instance.xPositionOnScreen + __instance.width + 4;
                    __instance.okButton.bounds.Y = __instance.yPositionOnScreen + __instance.height - 64 - IClickableMenu.borderWidth;
                }
                Game1.displayHUD = true;
                Game1.viewportFreeze = false;
                __instance.namingAnimal = false;
                __instance.textBox.Selected = false;
            };
            Game1.warpFarmer(request, marnieOriginTile.X, marnieOriginTile.Y, marnieOriginFacing);
            return false;
        }

        public static bool Prefix_PurchaseAnimals_ReturnAfterPurchase(PurchaseAnimalsMenu __instance)
        {
            if (!marnieMailSessionActive || string.IsNullOrEmpty(marnieOriginLocationName))
                return true;

            LocationRequest request = Game1.getLocationRequest(marnieOriginLocationName);
            request.OnWarp += () =>
            {
                __instance.onFarm = false;
                Game1.player.viewingLocation.Value = null;
                if (__instance.okButton != null)
                    __instance.okButton.bounds.X = __instance.xPositionOnScreen + __instance.width + 4;
                Game1.displayHUD = true;
                Game1.displayFarmer = true;
                __instance.freeze = false;
                __instance.textBox.Selected = false;
                Game1.viewportFreeze = false;
                __instance.marnieAnimalPurchaseMessage();
            };
            Game1.warpFarmer(request, marnieOriginTile.X, marnieOriginTile.Y, marnieOriginFacing);
            return false;
        }

        public static bool Prefix_PurchaseAnimals_Message(PurchaseAnimalsMenu __instance)
        {
            if (!marnieMailSessionActive)
                return true;

            string animalName = __instance.animalBeingPurchased?.displayName ?? string.Empty;
            marnieMailSessionActive = false;
            marnieOriginLocationName = null;

            __instance.exitThisMenu();
            Game1.player.forceCanMove();
            __instance.freeze = false;
            Game1.drawObjectDialogue(I18n.Get("mailbox.orders.animal_scheduled", new { name = animalName }));
            return false;
        }

        private static void SubmitAnimalOrder(PendingAnimalOrder order)
        {
            if (context == null)
                return;

            if (Context.IsMainPlayer)
            {
                HandlePlaceAnimalOrderOnHost(Game1.player.UniqueMultiplayerID, order);
            }
            else
            {
                context.Helper.Multiplayer.SendMessage(
                    new PlaceAnimalOrderPayload { Order = order },
                    MailOrderMessages.PlaceAnimalOrderRequest,
                    new[] { context.Manifest.UniqueID },
                    new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );
            }
        }

        private static void HandlePlaceAnimalOrderOnHost(long playerId, PendingAnimalOrder order)
        {
            if (context == null || !Context.IsMainPlayer)
                return;

            order.PlayerId = playerId;
            context.WorldState.PendingAnimalOrders.Add(order);
            context.Monitor.Log($"[MailOrder] Animal '{order.AnimalType}' ({order.AnimalName}) programado para {playerId} en '{order.TargetLocationName}' (día {order.DeliveryGameDay} a las 12:00).", LogLevel.Info);
            BroadcastOrdersSync();
        }

        private static bool DeliverAnimalOrderOnHost(PendingAnimalOrder order)
        {
            if (context == null)
                return true;

            GameLocation? location = Game1.getLocationFromName(order.TargetLocationName);
            Building? building = location?.getBuildingAt(new Vector2(order.BuildingTileX, order.BuildingTileY));

            if (building?.GetIndoors() is not AnimalHouse animalHouse || animalHouse.isFull())
            {
                // Si el corral fue demolido o llenado, reembolsar el importe al jugador
                Farmer? buyer = Game1.GetPlayer(order.PlayerId);
                if (buyer != null && buyer.isActive())
                    DailyRolloverModule.SetFarmerMoney(buyer, buyer.Money + order.PricePaid);
                else
                    context.WorldState.GetOrCreatePlayer(order.PlayerId, string.Empty).PendingMarketGold += order.PricePaid;

                NotifyPlayerIfOnline(order.PlayerId, I18n.Get("mailbox.orders.animal_refunded", new { name = order.AnimalName }));
                return true;
            }

            long entityId = order.AnimalEntityId != 0 ? order.AnimalEntityId : Game1.Multiplayer.getNewID();
            var animal = new FarmAnimal(order.AnimalType, entityId, order.PlayerId)
            {
                Name = order.AnimalName,
                displayName = order.AnimalName
            };
            animalHouse.adoptAnimal(animal);

            NotifyPlayerIfOnline(order.PlayerId, I18n.Get("mailbox.orders.animal_arrived", new { name = order.AnimalName }));
            return true;
        }
    }
}
