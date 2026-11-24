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
        private static GeodeMenu? activeGeodeMailMenu;
        private static readonly List<MailOrderItemLine> activeGeodeBuffer = new();
        private static readonly Dictionary<long, DateTimeOffset> lastToolUpgradeClearNoticeUtc = new();
        private static readonly TimeSpan ToolUpgradeClearNoticeCooldown = TimeSpan.FromSeconds(5);

        private static void ResetClintSession()
        {
            clintUpgradeMailSessionActive = false;
            clintGeodeMailSessionActive = false;
            activeGeodeMailMenu = null;
            activeGeodeBuffer.Clear();
            lastToolUpgradeClearNoticeUtc.Clear();
        }

        private static bool IsActiveMailGeodeMenu(GeodeMenu? menu)
        {
            return clintGeodeMailSessionActive
                && menu != null
                && ReferenceEquals(menu, activeGeodeMailMenu);
        }

        private static void ApplyClintPatches(Harmony harmony)
        {
            PatchSafe(harmony, "Tool.actionWhenPurchased",
                AccessTools.Method(typeof(Tool), nameof(Tool.actionWhenPurchased), new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_Tool_ActionWhenPurchased)),
                postfix: new HarmonyMethod(typeof(MailOrderModule), nameof(Postfix_Tool_ActionWhenPurchased)));

            PatchSafe(harmony, "GeodeMenu.receiveLeftClick",
                AccessTools.Method(typeof(GeodeMenu), nameof(GeodeMenu.receiveLeftClick), new[] { typeof(int), typeof(int), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_GeodeMenu_ReceiveLeftClick)));

            PatchSafe(harmony, "GeodeMenu.update",
                AccessTools.Method(typeof(GeodeMenu), nameof(GeodeMenu.update), new[] { typeof(GameTime) }),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_GeodeMenu_Update)));

            PatchSafe(harmony, "GeodeMenu.emergencyShutDown",
                AccessTools.Method(typeof(GeodeMenu), nameof(GeodeMenu.emergencyShutDown), Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_GeodeMenu_EmergencyShutDown)));
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
                        FlushGeodeBuffer();
                        var geodeMenu = new GeodeMenu();
                        activeGeodeMailMenu = geodeMenu;
                        clintGeodeMailSessionActive = true;
                        Game1.activeClickableMenu = geodeMenu;
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

        public static void Postfix_Tool_ActionWhenPurchased(string shopId, bool __result)
        {
            if (!__result || !string.Equals(shopId, "ClintUpgrade", StringComparison.Ordinal))
                return;

            Tool? queued = Game1.player?.toolBeingUpgraded.Value;
            if (queued == null || context == null)
                return;

            NotifyHostToolUpgradeStarted(queued.QualifiedItemId);
        }

        private static void NotifyHostToolUpgradeStarted(string qualifiedToolId)
        {
            if (context == null || string.IsNullOrWhiteSpace(qualifiedToolId))
                return;

            if (Context.IsMainPlayer)
            {
                long hostId = Game1.player.UniqueMultiplayerID;
                context.WorldState.PendingToolUpgradeClears.Remove(hostId);
                lastToolUpgradeClearNoticeUtc.Remove(hostId);
                return;
            }

            context.Helper.Multiplayer.SendMessage(
                new ToolUpgradeStartedPayload { QualifiedToolId = qualifiedToolId },
                MailOrderMessages.ToolUpgradeStarted,
                new[] { context.Manifest.UniqueID },
                new[] { Game1.MasterPlayer.UniqueMultiplayerID }
            );
        }

        public static bool Prefix_GeodeMenu_ReceiveLeftClick(GeodeMenu __instance, int x, int y)
        {
            if (!IsActiveMailGeodeMenu(__instance) || __instance.waitingForServerResponse)
                return true;

            if (!__instance.geodeSpot.containsPoint(x, y))
                return true;

            Item? held = __instance.heldItem;
            if (held == null || !Utility.IsGeode(held) || Game1.player.Money < 25 || __instance.geodeAnimationTimer > 0)
                return true;

            int freeSpots = Game1.player.freeSpotsInInventory();
            if (!GeodeMailSessionLogic.ShouldBypassVanillaInventorySpaceCheck(isMailGeodeSession: true, freeSpots, held.Stack))
                return true;

            // El tesoro va al paquete del día siguiente, así que omitimos la guarda vanilla de inventario lleno.
            if (held.QualifiedItemId == "(O)791" && !Game1.netWorldState.Value.GoldenCoconutCracked)
            {
                __instance.waitingForServerResponse = true;
                Game1.player.team.goldenCoconutMutex.RequestLock(
                    () =>
                    {
                        __instance.waitingForServerResponse = false;
                        __instance.geodeTreasureOverride = ItemRegistry.Create("(O)73");
                        __instance.startGeodeCrack();
                    },
                    () =>
                    {
                        __instance.waitingForServerResponse = false;
                        __instance.startGeodeCrack();
                    });
            }
            else
            {
                __instance.startGeodeCrack();
            }

            return false;
        }

        public static void Prefix_GeodeMenu_Update(GeodeMenu __instance, GameTime time)
        {
            if (!IsActiveMailGeodeMenu(__instance))
            {
                if (GeodeMailSessionLogic.ShouldEndGeodeMailSession(
                        clintGeodeMailSessionActive,
                        IsActiveMailGeodeMenu(Game1.activeClickableMenu as GeodeMenu)))
                {
                    FlushGeodeBuffer();
                }
                return;
            }

            if (__instance.geodeAnimationTimer <= 0)
                return;

            if (__instance.geodeAnimationTimer - time.ElapsedGameTime.Milliseconds <= 0 && __instance.geodeTreasure != null)
            {
                CaptureAndEnqueueGeodeTreasure(__instance);
            }
        }

        public static void Prefix_GeodeMenu_EmergencyShutDown(GeodeMenu __instance)
        {
            if (!IsActiveMailGeodeMenu(__instance))
                return;

            if (__instance.geodeTreasure != null)
            {
                CaptureAndEnqueueGeodeTreasure(__instance);
            }
            else if (__instance.geodeSpot?.item != null)
            {
                // Si el menú se cierra a la fuerza antes de generar el tesoro (p. ej. rollover de las 02:00), devolver la geoda en curso.
                Item inFlight = __instance.geodeSpot.item;
                __instance.geodeSpot.item = null;
                __instance.geodeAnimationTimer = 0;
                if (!Game1.player.addItemToInventoryBool(inFlight))
                    Game1.createItemDebris(inFlight, Game1.player.getStandingPosition(), Game1.player.FacingDirection, Game1.currentLocation);
            }

            FlushGeodeBuffer();
        }

        private static void CaptureAndEnqueueGeodeTreasure(GeodeMenu menu)
        {
            Item? treasure = menu.geodeTreasure;
            if (treasure == null)
                return;

            if (treasure.QualifiedItemId == "(O)73")
                Game1.netWorldState.Value.GoldenCoconutCracked = true;

            var line = new MailOrderItemLine
            {
                QualifiedItemId = treasure.QualifiedItemId,
                DisplayName = treasure.DisplayName,
                Stack = treasure.Stack,
                Quality = treasure.Quality,
                PricePaid = 25
            };

            // Enviar de inmediato al Host para que no se pierda ninguna geoda procesada si hay desconexión o rollover.
            if (context != null)
            {
                SubmitGeodeOrderLines(new List<MailOrderItemLine> { line });
            }
            else
            {
                activeGeodeBuffer.Add(line);
            }

            Game1.addHUDMessage(new HUDMessage(
                I18n.Get("mailbox.orders.item_queued", new { item = treasure.DisplayName, count = treasure.Stack }),
                HUDMessage.newQuest_type));

            menu.geodeTreasure = null;
        }

        private static void FlushGeodeBuffer()
        {
            clintGeodeMailSessionActive = false;
            activeGeodeMailMenu = null;
            if (activeGeodeBuffer.Count == 0 || context == null)
            {
                activeGeodeBuffer.Clear();
                return;
            }

            List<MailOrderItemLine> lines = activeGeodeBuffer.ToList();
            activeGeodeBuffer.Clear();
            SubmitGeodeOrderLines(lines);
        }

        private static void SubmitGeodeOrderLines(List<MailOrderItemLine> lines)
        {
            if (context == null || lines.Count == 0)
                return;

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

                DeliverCompletedToolUpgradeForFarmerOnHost(farmer);
            }
        }

        internal static int ReconcileOrphanedToolUpgradesForRolloverOnHost()
        {
            if (context == null || !Context.IsMainPlayer)
                return 0;

            int delivered = 0;
            foreach (Farmer farmer in Game1.getAllFarmers())
            {
                if (farmer == null || farmer.isUnclaimedFarmhand || farmer.toolBeingUpgraded.Value == null)
                    continue;

                // daysLeftForToolUpgrade == 0 acaba de madurar hoy y saldrá a las 12:00;
                // < 0 indica que ya venció su mediodía anterior sin entregarse.
                if (farmer.daysLeftForToolUpgrade.Value < 0)
                {
                    if (DeliverCompletedToolUpgradeForFarmerOnHost(farmer))
                        delivered++;
                }
            }

            if (context.WorldState.PendingToolUpgradeClears.Count > 0)
            {
                foreach (PendingToolUpgradeClear pending in context.WorldState.PendingToolUpgradeClears.Values.ToList())
                {
                    Farmer? farmer = Game1.GetPlayer(pending.PlayerId);
                    if (farmer == null || farmer.toolBeingUpgraded.Value == null)
                    {
                        context.WorldState.PendingToolUpgradeClears.Remove(pending.PlayerId);
                    }
                }
            }

            if (delivered > 0)
                PersistMailOrderState();

            return delivered;
        }

        private static bool DeliverCompletedToolUpgradeForFarmerOnHost(Farmer farmer)
        {
            if (context == null || farmer.toolBeingUpgraded.Value == null)
                return false;

            long farmerId = farmer.UniqueMultiplayerID;
            string qualifiedToolId = farmer.toolBeingUpgraded.Value.QualifiedItemId;

            if (!ToolUpgradeDeliveryLogic.ShouldDeliverToolUpgrade(
                    farmerId,
                    qualifiedToolId,
                    farmer.daysLeftForToolUpgrade.Value,
                    context.WorldState.PendingToolUpgradeClears))
            {
                ClearHostFarmerToolUpgrade(farmer, qualifiedToolId);
                SendToolUpgradeDeliveredToClient(farmerId, qualifiedToolId);
                return false;
            }

            Farm? farm = ResolvePlayerResidenceFarm(farmerId);
            if (farm == null)
                return false;

            if (HasUnclaimedToolUpgradePackage(farm, farmerId, qualifiedToolId))
            {
                ClearHostFarmerToolUpgrade(farmer, qualifiedToolId);
                RecordPendingClientClearAndNotify(farmerId, qualifiedToolId);
                return false;
            }

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
            chest.modData[MailPackageKeys.OwnerId] = farmerId.ToString();
            chest.modData[MailPackageKeys.OrderId] = Guid.NewGuid().ToString("N");
            chest.modData[MailPackageKeys.ShopId] = "ClintUpgrade";

            farm.objects[tile] = chest;
            ClearHostFarmerToolUpgrade(farmer, qualifiedToolId);
            RecordPendingClientClearAndNotify(farmerId, qualifiedToolId);

            context.Monitor.Log($"[MailOrder] Herramienta mejorada '{upgradedTool.DisplayName}' entregada en paquete en {farm.NameOrUniqueName} ({spot.Value.X}, {spot.Value.Y}) para {farmer.Name}.", LogLevel.Info);
            NotifyPlayerIfOnline(farmerId, I18n.Get("mailbox.orders.clint.tool_arrived", new { tool = upgradedTool.DisplayName }));
            return true;
        }

        private static bool HasUnclaimedToolUpgradePackage(Farm farm, long ownerId, string qualifiedToolId)
        {
            string expectedOwner = ownerId.ToString();
            foreach (StardewValley.Object obj in farm.objects.Values)
            {
                if (obj is not Chest chest || !IsMailPackage(chest, out _))
                    continue;

                if (!chest.modData.TryGetValue(MailPackageKeys.ShopId, out string? shopId)
                    || !string.Equals(shopId, "ClintUpgrade", StringComparison.Ordinal))
                    continue;

                if (!chest.modData.TryGetValue(MailPackageKeys.OwnerId, out string? pkgOwner)
                    || !string.Equals(pkgOwner, expectedOwner, StringComparison.Ordinal))
                    continue;

                if (chest.Items.Any(i => i != null && string.Equals(i.QualifiedItemId, qualifiedToolId, StringComparison.Ordinal)))
                    return true;
            }

            return false;
        }

        private static void RecordPendingClientClearAndNotify(long farmerId, string qualifiedToolId)
        {
            if (context == null || farmerId == Game1.player.UniqueMultiplayerID)
                return;

            context.WorldState.PendingToolUpgradeClears[farmerId] = new PendingToolUpgradeClear
            {
                PlayerId = farmerId,
                QualifiedToolId = qualifiedToolId,
                DeliveredAtGameDay = Game1.Date?.TotalDays ?? 0
            };

            SendToolUpgradeDeliveredToClient(farmerId, qualifiedToolId, force: true);
        }

        private static void ClearHostFarmerToolUpgrade(Farmer? farmer, string? expectedQualifiedToolId = null)
        {
            if (farmer == null)
                return;

            ClearSingleFarmerRecordIfMatched(farmer, expectedQualifiedToolId);

            long farmerId = farmer.UniqueMultiplayerID;
            if (Game1.netWorldState?.Value?.farmhandData != null
                && Game1.netWorldState.Value.farmhandData.TryGetValue(farmerId, out Farmer? stored)
                && stored != null
                && !ReferenceEquals(stored, farmer))
            {
                ClearSingleFarmerRecordIfMatched(stored, expectedQualifiedToolId);
            }
        }

        private static void ClearSingleFarmerRecordIfMatched(Farmer target, string? expectedQualifiedToolId)
        {
            Tool? current = target.toolBeingUpgraded.Value;
            if (current == null || target.daysLeftForToolUpgrade.Value > 0)
                return;

            if (!string.IsNullOrWhiteSpace(expectedQualifiedToolId)
                && !string.Equals(current.QualifiedItemId, expectedQualifiedToolId, StringComparison.Ordinal))
                return;

            target.toolBeingUpgraded.Value = null;
            target.daysLeftForToolUpgrade.Value = 0;
            target.hasReceivedToolUpgradeMessageYet = false;
        }

        private static void ReconcilePendingToolUpgradeClearsOnHost()
        {
            if (context == null || !Context.IsMainPlayer || context.WorldState.PendingToolUpgradeClears.Count == 0)
                return;

            foreach (PendingToolUpgradeClear pending in context.WorldState.PendingToolUpgradeClears.Values.ToList())
            {
                if (pending.PlayerId == Game1.player.UniqueMultiplayerID)
                {
                    context.WorldState.PendingToolUpgradeClears.Remove(pending.PlayerId);
                    continue;
                }

                if (Game1.otherFarmers.TryGetValue(pending.PlayerId, out Farmer? live))
                    ClearSingleFarmerRecordIfMatched(live, pending.QualifiedToolId);

                if (Game1.netWorldState?.Value?.farmhandData != null
                    && Game1.netWorldState.Value.farmhandData.TryGetValue(pending.PlayerId, out Farmer? stored))
                {
                    ClearSingleFarmerRecordIfMatched(stored, pending.QualifiedToolId);
                }
            }
        }

        private static void SendToolUpgradeDeliveredToClient(long playerId, string qualifiedToolId, bool force = false, bool knownOnline = false)
        {
            if (context == null || !Context.IsMainPlayer || playerId == Game1.player.UniqueMultiplayerID)
                return;

            bool isOnline = knownOnline || Game1.getOnlineFarmers().Any(f => f != null && f.UniqueMultiplayerID == playerId);
            if (!isOnline)
                return;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (!force
                && lastToolUpgradeClearNoticeUtc.TryGetValue(playerId, out DateTimeOffset lastSent)
                && now - lastSent < ToolUpgradeClearNoticeCooldown)
            {
                return;
            }

            lastToolUpgradeClearNoticeUtc[playerId] = now;
            context.Helper.Multiplayer.SendMessage(
                new ToolUpgradeDeliveredPayload { QualifiedToolId = qualifiedToolId },
                MailOrderMessages.ToolUpgradeDelivered,
                new[] { context.Manifest.UniqueID },
                new[] { playerId }
            );
        }

        private static void HandleToolUpgradeStartedOnHost(long playerId, ToolUpgradeStartedPayload payload)
        {
            if (context == null || !Context.IsMainPlayer)
                return;

            context.WorldState.PendingToolUpgradeClears.Remove(playerId);
            lastToolUpgradeClearNoticeUtc.Remove(playerId);
            _ = payload;
        }

        private static void HandleToolUpgradeDeliveredOnClient(ToolUpgradeDeliveredPayload payload)
        {
            if (context == null || Context.IsMainPlayer || Game1.player == null)
                return;

            string? localToolId = Game1.player.toolBeingUpgraded.Value?.QualifiedItemId;
            if (ToolUpgradeDeliveryLogic.ShouldClearClientLocalTool(
                    localToolId,
                    Game1.player.daysLeftForToolUpgrade.Value,
                    payload.QualifiedToolId))
            {
                Game1.player.toolBeingUpgraded.Value = null;
                Game1.player.daysLeftForToolUpgrade.Value = 0;
                Game1.player.hasReceivedToolUpgradeMessageYet = false;
            }

            string? remainingToolId = Game1.player.toolBeingUpgraded.Value?.QualifiedItemId;
            bool cleared = string.IsNullOrEmpty(remainingToolId)
                || !string.Equals(remainingToolId, payload.QualifiedToolId, StringComparison.Ordinal);

            context.Helper.Multiplayer.SendMessage(
                new ToolUpgradeClearAckPayload
                {
                    QualifiedToolId = payload.QualifiedToolId,
                    Cleared = cleared
                },
                MailOrderMessages.ToolUpgradeClearAck,
                new[] { context.Manifest.UniqueID },
                new[] { Game1.MasterPlayer.UniqueMultiplayerID }
            );
        }

        private static void HandleToolUpgradeClearAckOnHost(long playerId, ToolUpgradeClearAckPayload payload)
        {
            if (context == null || !Context.IsMainPlayer || !payload.Cleared)
                return;

            if (context.WorldState.PendingToolUpgradeClears.TryGetValue(playerId, out PendingToolUpgradeClear? pending)
                && string.Equals(pending.QualifiedToolId, payload.QualifiedToolId, StringComparison.Ordinal))
            {
                context.WorldState.PendingToolUpgradeClears.Remove(playerId);
                lastToolUpgradeClearNoticeUtc.Remove(playerId);
            }
        }
    }
}
