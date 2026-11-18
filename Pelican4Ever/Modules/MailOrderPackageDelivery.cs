using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Economy;
using Pelican4Ever.Core.Time;
using Pelican4Ever.Core.World;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.Tools;
using xTile.Dimensions;

namespace Pelican4Ever.Modules
{
    public sealed partial class MailOrderModule
    {
        private static void ApplyPackagePatches(Harmony harmony)
        {
            PatchSafe(harmony, "Chest.checkForAction",
                AccessTools.Method(typeof(Chest), nameof(Chest.checkForAction), new[] { typeof(Farmer), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_Chest_CheckForAction)),
                criticality: Pelican4Ever.Core.Admin.PatchCriticality.Security);

            PatchSafe(harmony, "Chest.performToolAction",
                AccessTools.Method(typeof(Chest), nameof(Chest.performToolAction), new[] { typeof(Tool) }),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_Chest_PerformToolAction)),
                criticality: Pelican4Ever.Core.Admin.PatchCriticality.Security);

            PatchSafe(harmony, "Object.onExplosion",
                AccessTools.Method(typeof(StardewValley.Object), nameof(StardewValley.Object.onExplosion), new[] { typeof(Farmer) }),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_Chest_OnExplosion)),
                criticality: Pelican4Ever.Core.Admin.PatchCriticality.Security);
        }

        private static bool IsMailPackage(Chest? chest, out long ownerId)
        {
            ownerId = 0;
            if (chest == null || !chest.modData.TryGetValue(MailPackageKeys.IsMailPackage, out string? flag) || flag != "1")
                return false;

            if (chest.modData.TryGetValue(MailPackageKeys.OwnerId, out string? rawOwner))
                long.TryParse(rawOwner, out ownerId);

            return true;
        }

        public static bool Prefix_Chest_PerformToolAction(Chest __instance, ref bool __result)
        {
            if (!IsMailPackage(__instance, out _))
                return true;

            __result = false;
            return false;
        }

        public static bool Prefix_Chest_OnExplosion(StardewValley.Object __instance, ref bool __result)
        {
            if (__instance is not Chest chest || !IsMailPackage(chest, out _))
                return true;

            __result = false;
            return false;
        }

        public static bool Prefix_Chest_CheckForAction(Chest __instance, Farmer who, bool justCheckingForActivity, ref bool __result)
        {
            if (!IsMailPackage(__instance, out long ownerId))
                return true;

            if (justCheckingForActivity)
            {
                __result = true;
                return false;
            }

            if (who == null || !who.IsLocalPlayer)
                return false;

            if (who.UniqueMultiplayerID != ownerId)
            {
                string ownerName = ResolvePlayerName(ownerId);
                Game1.drawObjectDialogue(I18n.Get("mailbox.orders.package_not_yours", new { owner = ownerName }));
                __result = true;
                return false;
            }

            if (__instance.Location == null)
            {
                __result = false;
                return false;
            }

            if (Context.IsMainPlayer)
            {
                OpenMailPackageOnHost(__instance, who);
            }
            else
            {
                // Verificar que al menos el primer objeto quepa en el inventario antes de reclamar al Host
                Item? first = __instance.Items.FirstOrDefault(i => i != null);
                if (first != null && !who.couldInventoryAcceptThisItem(first))
                {
                    Game1.addHUDMessage(new HUDMessage(I18n.Get("mailbox.orders.inventory_full"), HUDMessage.error_type));
                    __result = true;
                    return false;
                }

                __instance.modData.TryGetValue(MailPackageKeys.OrderId, out string? orderId);
                context?.Helper.Multiplayer.SendMessage(
                    new ClaimMailPackagePayload
                    {
                        LocationName = __instance.Location.NameOrUniqueName,
                        TileX = (int)__instance.TileLocation.X,
                        TileY = (int)__instance.TileLocation.Y,
                        OrderId = orderId ?? string.Empty
                    },
                    MailOrderMessages.ClaimPackageRequest,
                    new[] { context.Manifest.UniqueID },
                    new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );
            }

            __result = true;
            return false;
        }

        private static void OpenMailPackageOnHost(Chest chest, Farmer who)
        {
            GameLocation? location = chest.Location;
            if (location == null)
                return;

            var remaining = new List<Item>();
            int deliveredCount = 0;

            foreach (Item? item in chest.Items)
            {
                if (item == null)
                    continue;

                if (who.couldInventoryAcceptThisItem(item) && who.addItemToInventoryBool(item))
                {
                    deliveredCount++;
                    if (item is GenericTool tool)
                        tool.actionWhenClaimed();
                }
                else
                {
                    remaining.Add(item);
                }
            }

            if (deliveredCount == 0 && remaining.Count > 0)
            {
                Game1.addHUDMessage(new HUDMessage(I18n.Get("mailbox.orders.inventory_full"), HUDMessage.error_type));
                return;
            }

            who.Halt();
            location.playSound("Ship");

            if (remaining.Count == 0)
            {
                chest.Items.Clear();
                PlayPackageOpenVisualAndRemove(location, chest);
            }
            else
            {
                chest.Items.Clear();
                foreach (Item rem in remaining)
                    chest.Items.Add(rem);
                Game1.addHUDMessage(new HUDMessage(I18n.Get("mailbox.orders.package_partial"), HUDMessage.error_type));
            }
        }

        private static void HandleClaimPackageOnHost(long requesterId, ClaimMailPackagePayload req)
        {
            if (context == null || !Context.IsMainPlayer)
                return;

            GameLocation? location = Game1.getLocationFromName(req.LocationName);
            var tile = new Vector2(req.TileX, req.TileY);
            if (location == null || !location.objects.TryGetValue(tile, out StardewValley.Object? obj) || obj is not Chest chest)
                return;

            if (!IsMailPackage(chest, out long ownerId) || ownerId != requesterId)
                return;

            var lines = new List<MailOrderItemLine>();
            foreach (Item? item in chest.Items)
            {
                if (item == null)
                    continue;

                lines.Add(new MailOrderItemLine
                {
                    QualifiedItemId = item.QualifiedItemId,
                    DisplayName = item.DisplayName,
                    Stack = item.Stack,
                    Quality = item.Quality
                });
            }

            chest.Items.Clear();
            PlayPackageOpenVisualAndRemove(location, chest);

            context.Helper.Multiplayer.SendMessage(
                new ClaimMailPackageResultPayload
                {
                    Success = true,
                    LocationName = req.LocationName,
                    TileX = req.TileX,
                    TileY = req.TileY,
                    OrderId = req.OrderId,
                    Items = lines
                },
                MailOrderMessages.ClaimPackageResult,
                new[] { context.Manifest.UniqueID },
                new[] { requesterId }
            );
        }

        private static void HandleClaimPackageResultOnClient(ClaimMailPackageResultPayload res)
        {
            if (!res.Success)
                return;

            Game1.player.Halt();
            Game1.currentLocation?.playSound("Ship");

            var overflow = new List<Item>();
            foreach (MailOrderItemLine line in res.Items)
            {
                Item item = ItemRegistry.Create(line.QualifiedItemId, line.Stack, line.Quality);
                if (!Game1.player.addItemToInventoryBool(item))
                {
                    overflow.Add(item);
                }
                else if (item is GenericTool tool)
                {
                    tool.actionWhenClaimed();
                }
            }

            if (overflow.Count > 0)
            {
                Game1.player.addItemsByMenuIfNecessary(overflow);
            }
        }

        private static void PlayPackageOpenVisualAndRemove(GameLocation location, Chest chest)
        {
            Vector2 tile = chest.TileLocation;
            location.playSound("woodWhack");
            var sprite = new TemporaryAnimatedSprite(
                "LooseSprites\\Giftbox",
                new Microsoft.Xna.Framework.Rectangle(0, chest.giftboxIndex.Value * 32, 16, 32),
                80f,
                11,
                1,
                tile * 64f - new Vector2(0f, 52f),
                flicker: false,
                flipped: false,
                tile.Y / 10000f,
                0f,
                Color.White,
                4f,
                0f,
                0f,
                0f)
            {
                destroyable = false,
                holdLastFrame = true
            };

            Game1.Multiplayer.broadcastSprites(location, sprite);
            location.removeObject(tile, showDestroyedObject: false);
        }

        // -------------------------------------------------------------------------
        // Materialización de pedidos a mediodía (12:00 / 1200)
        // -------------------------------------------------------------------------

        private static void TryDeliverDueOrders()
        {
            if (context == null || !Context.IsMainPlayer || !Context.IsWorldReady)
                return;

            int currentDay = Game1.Date?.TotalDays ?? 0;
            int currentTime = Game1.timeOfDay;
            bool isNightHold = RealTimeClockModule.IsNightHold;
            bool changed = false;

            foreach (PendingMailItemOrder order in context.WorldState.PendingMailItemOrders.ToList())
            {
                if (!MailOrderScheduleLogic.IsReadyToDeliver(order.DeliveryGameDay, order.DeliveryTimeOfDay, currentDay, currentTime, isNightHold))
                    continue;

                if (DeliverItemOrderOnHost(order))
                {
                    context.WorldState.PendingMailItemOrders.Remove(order);
                    changed = true;
                }
            }

            foreach (PendingBuildingOrder bOrder in context.WorldState.PendingBuildingOrders.ToList())
            {
                if (!MailOrderScheduleLogic.IsReadyToDeliver(bOrder.DeliveryGameDay, bOrder.DeliveryTimeOfDay, currentDay, currentTime, isNightHold))
                    continue;

                if (DeliverBuildingOrderOnHost(bOrder))
                {
                    context.WorldState.PendingBuildingOrders.Remove(bOrder);
                    changed = true;
                }
            }

            foreach (PendingAnimalOrder aOrder in context.WorldState.PendingAnimalOrders.ToList())
            {
                if (!MailOrderScheduleLogic.IsReadyToDeliver(aOrder.DeliveryGameDay, aOrder.DeliveryTimeOfDay, currentDay, currentTime, isNightHold))
                    continue;

                if (DeliverAnimalOrderOnHost(aOrder))
                {
                    context.WorldState.PendingAnimalOrders.Remove(aOrder);
                    changed = true;
                }
            }

            if (changed)
            {
                BroadcastOrdersSync();
            }
        }

        private static int ForceDeliverAllNow()
        {
            if (context == null || !Context.IsMainPlayer)
                return 0;

            int count = 0;

            foreach (PendingMailItemOrder order in context.WorldState.PendingMailItemOrders.ToList())
            {
                if (DeliverItemOrderOnHost(order))
                {
                    context.WorldState.PendingMailItemOrders.Remove(order);
                    count++;
                }
            }

            foreach (PendingBuildingOrder bOrder in context.WorldState.PendingBuildingOrders.ToList())
            {
                if (DeliverBuildingOrderOnHost(bOrder))
                {
                    context.WorldState.PendingBuildingOrders.Remove(bOrder);
                    count++;
                }
            }

            foreach (PendingAnimalOrder aOrder in context.WorldState.PendingAnimalOrders.ToList())
            {
                if (DeliverAnimalOrderOnHost(aOrder))
                {
                    context.WorldState.PendingAnimalOrders.Remove(aOrder);
                    count++;
                }
            }

            if (count > 0)
                BroadcastOrdersSync();

            return count;
        }

        private static Farm? ResolvePlayerResidenceFarm(long playerId)
        {
            if (context == null)
                return null;

            string? slotId = context.WorldState.ResolvePlayerResidenceSlot(playerId);
            if (!string.IsNullOrWhiteSpace(slotId) && Game1.getLocationFromName(slotId) is Farm privateFarm)
                return privateFarm;

            if (playerId == Game1.MasterPlayer.UniqueMultiplayerID)
                return Game1.getFarm();

            return null;
        }

        private static bool DeliverItemOrderOnHost(PendingMailItemOrder order)
        {
            if (context == null || order.Items.Count == 0)
                return true;

            Farm? farm = ResolvePlayerResidenceFarm(order.PlayerId);
            if (farm == null)
                return false;

            Point mb = farm.GetMainMailboxPosition();
            Point door = farm.GetMainFarmHouseEntry();

            (int X, int Y)? spot = MailPackagePlacementLogic.FindFreePackageTile(
                mb.X, mb.Y, door.X, door.Y,
                (x, y) => IsTileFreeForPackage(farm, x, y),
                maxRadius: 5);

            if (spot == null)
            {
                // Respaldo de emergencia junto al buzón si todo el entorno tuviera maleza/objetos
                spot = (mb.X + 1, mb.Y);
            }

            var tile = new Vector2(spot.Value.X, spot.Value.Y);
            var chest = new Chest(playerChest: false, tile)
            {
                TileLocation = tile
            };
            chest.giftbox.Value = true;
            chest.giftboxIndex.Value = Math.Clamp(order.GiftboxIndex, 0, 3);
            chest.giftboxIsStarterGift.Value = false;

            foreach (MailOrderItemLine line in order.Items)
            {
                Item item = ItemRegistry.Create(line.QualifiedItemId, Math.Max(1, line.Stack), line.Quality);
                chest.Items.Add(item);
            }

            chest.modData[MailPackageKeys.IsMailPackage] = "1";
            chest.modData[MailPackageKeys.OwnerId] = order.PlayerId.ToString();
            chest.modData[MailPackageKeys.OrderId] = order.OrderId;
            chest.modData[MailPackageKeys.ShopId] = order.ShopId;

            farm.objects[tile] = chest;
            context.Monitor.Log($"[MailOrder] Paquete {order.OrderId} ({order.ShopId}) entregado en {farm.NameOrUniqueName} ({spot.Value.X}, {spot.Value.Y}) para {order.PlayerId}.", LogLevel.Info);

            NotifyPlayerIfOnline(order.PlayerId, I18n.Get("mailbox.orders.package_arrived"));
            return true;
        }

        private static bool IsTileFreeForPackage(Farm farm, int x, int y)
        {
            if (!farm.isTileOnMap(x, y))
                return false;

            var tile = new Vector2(x, y);
            if (farm.objects.ContainsKey(tile) || farm.terrainFeatures.ContainsKey(tile))
                return false;

            if (farm.getBuildingAt(tile) != null)
                return false;

            if (!farm.isTilePassable(new Location(x, y), Game1.viewport))
                return false;

            if (farm.warps.Any(w => w.X == x && w.Y == y))
                return false;

            return true;
        }

        private static void NotifyPlayerIfOnline(long playerId, string message)
        {
            if (context == null || string.IsNullOrWhiteSpace(message))
                return;

            if (playerId == Game1.player.UniqueMultiplayerID)
            {
                Game1.addHUDMessage(new HUDMessage(message, HUDMessage.newQuest_type));
                return;
            }

            Farmer? target = Game1.GetPlayer(playerId);
            if (target != null && target.isActive())
            {
                context.Helper.Multiplayer.SendMessage(
                    new MailOrderResultPayload { Accepted = true, OrderKind = "Delivery", Message = message },
                    MailOrderMessages.OrderResult,
                    new[] { context.Manifest.UniqueID },
                    new[] { playerId }
                );
            }
        }
    }
}
