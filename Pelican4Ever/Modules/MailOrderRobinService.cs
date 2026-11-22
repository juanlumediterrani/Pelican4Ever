using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Economy;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;
using StardewValley.Menus;
using xTile.Dimensions;

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
                postfix: new HarmonyMethod(typeof(MailOrderModule), nameof(Postfix_CarpenterMenu_TryToBuild)),
                criticality: Pelican4Ever.Core.Admin.PatchCriticality.Integrity);

            PatchSafe(harmony, "CarpenterMenu.receiveLeftClick",
                AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.receiveLeftClick), new[] { typeof(int), typeof(int), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_CarpenterMenu_ReceiveLeftClick)),
                postfix: new HarmonyMethod(typeof(MailOrderModule), nameof(Postfix_CarpenterMenu_ReceiveLeftClick)));

            PatchSafe(harmony, "CarpenterMenu.robinConstructionMessage",
                AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.robinConstructionMessage)),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_CarpenterMenu_RobinConstructionMessage)));

            PatchSafe(harmony, "Building.isThereAnythingtoPreventConstruction",
                AccessTools.Method(typeof(Building), nameof(Building.isThereAnythingtoPreventConstruction)),
                postfix: new HarmonyMethod(typeof(MailOrderModule), nameof(Postfix_Building_PreventConstruction)),
                criticality: Pelican4Ever.Core.Admin.PatchCriticality.Integrity);

            PatchSafe(harmony, "GameLocation.carpenters",
                AccessTools.Method(typeof(GameLocation), nameof(GameLocation.carpenters), new[] { typeof(Location) }),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_GameLocation_Carpenters)),
                criticality: PatchCriticality.Experience);
        }

        public static bool HasPendingHouseUpgradeFor(long playerId)
        {
            return GetBuildingOrdersSnapshot().Any(o => o.PlayerId == playerId && o.IsHouseUpgrade);
        }

        public static bool Prefix_GameLocation_Carpenters(GameLocation __instance, Location tileLocation, ref bool __result)
        {
            if (context == null || Game1.player == null || __instance == null)
                return true;

            NPC? robin = null;
            foreach (NPC character in __instance.characters)
            {
                if (character != null && string.Equals(character.Name, "Robin", StringComparison.Ordinal))
                {
                    robin = character;
                    break;
                }
            }

            if (robin == null || Vector2.Distance(robin.Tile, new Vector2(tileLocation.X, tileLocation.Y)) > 3f)
                return true;

            bool hasPendingMailUpgrade = HasPendingHouseUpgradeFor(Game1.player.UniqueMultiplayerID);
            bool isOtherConstruction = Game1.IsThereABuildingUnderConstruction();
            bool allowDuringUpgrade = ResidenceUpgradeLogic.ShouldAllowCarpenterMenuDuringHouseUpgrade(
                context.Config.KeepRobinInCarpentryShop,
                Game1.player.daysUntilHouseUpgrade.Value,
                hasPendingMailUpgrade,
                isOtherConstruction);

            bool blockDoubleMailUpgrade = !context.Config.KeepRobinInCarpentryShop
                && Game1.player.daysUntilHouseUpgrade.Value < 0
                && hasPendingMailUpgrade
                && !isOtherConstruction;

            if (!allowDuringUpgrade && !blockDoubleMailUpgrade)
                return true;

            robin.faceDirection(2);
            var choices = new List<Response>
            {
                new("Shop", Game1.content.LoadString("Strings\\Locations:ScienceHouse_CarpenterMenu_Shop"))
            };

            if (Game1.IsMasterGame && Game1.player.HouseUpgradeLevel >= 3)
            {
                Farmer master = Game1.MasterPlayer;
                if ((master.mailReceived.Contains("ccIsComplete")
                        || master.mailReceived.Contains("JojaMember")
                        || master.hasCompletedCommunityCenter())
                    && Game1.RequireLocation<Town>("Town").daysUntilCommunityUpgrade.Value <= 0
                    && (!master.mailReceived.Contains("pamHouseUpgrade")
                        || !master.mailReceived.Contains("communityUpgradeShortcuts")))
                {
                    choices.Add(new Response("CommunityUpgrade", Game1.content.LoadString("Strings\\Locations:ScienceHouse_CarpenterMenu_CommunityUpgrade")));
                }
            }

            if (Game1.player.HouseUpgradeLevel >= 2)
            {
                string renovateKey = Game1.IsMasterGame
                    ? "Strings\\Locations:ScienceHouse_CarpenterMenu_RenovateHouse"
                    : "Strings\\Locations:ScienceHouse_CarpenterMenu_RenovateCabin";
                choices.Add(new Response("Renovate", Game1.content.LoadString(renovateKey)));
            }

            choices.Add(new Response("Construct", Game1.content.LoadString("Strings\\Locations:ScienceHouse_CarpenterMenu_Construct")));
            choices.Add(new Response("Leave", Game1.content.LoadString("Strings\\Locations:ScienceHouse_CarpenterMenu_Leave")));

            __instance.createQuestionDialogue(
                Game1.content.LoadString("Strings\\Locations:ScienceHouse_CarpenterMenu"),
                choices.ToArray(),
                "carpenter");
            __result = true;
            return false;
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

            var materials = new List<MailOrderItemLine>();
            if (matId != null && matCount > 0)
            {
                materials.Add(new MailOrderItemLine
                {
                    QualifiedItemId = matId,
                    DisplayName = matId,
                    Stack = matCount
                });
            }

            var order = new PendingBuildingOrder
            {
                PlayerId = Game1.player.UniqueMultiplayerID,
                Builder = "Robin",
                IsHouseUpgrade = true,
                HouseUpgradeLevel = level + 1,
                BuildingDisplayName = $"House Upgrade {level + 1}",
                BuildDays = 3,
                GoldPaid = goldCost,
                MaterialsPaid = materials,
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
                GoldPaid = Math.Max(0, __instance.Blueprint?.BuildCost ?? 0),
                MaterialsPaid = CaptureBlueprintMaterials(__instance.Blueprint),
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
                    GoldPaid = Math.Max(0, __instance.Blueprint?.BuildCost ?? 0),
                    MaterialsPaid = CaptureBlueprintMaterials(__instance.Blueprint),
                    CreatedAtGameDay = Game1.Date?.TotalDays ?? 0,
                    DeliveryGameDay = ComputeDeliveryGameDay(),
                    DeliveryTimeOfDay = MailOrderScheduleLogic.DefaultDeliveryTimeOfDay
                };

                SubmitBuildingOrder(order);
            }
        }

        private static List<MailOrderItemLine> CaptureBlueprintMaterials(CarpenterMenu.BlueprintEntry? blueprint)
        {
            var list = new List<MailOrderItemLine>();
            if (blueprint?.BuildMaterials == null)
                return list;

            foreach (var mat in blueprint.BuildMaterials)
            {
                if (mat == null || string.IsNullOrWhiteSpace(mat.ItemId) || mat.Amount <= 0)
                    continue;
                list.Add(new MailOrderItemLine
                {
                    QualifiedItemId = mat.ItemId,
                    DisplayName = mat.ItemId,
                    Stack = mat.Amount
                });
            }

            return list;
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

            if (order.IsHouseUpgrade)
            {
                Farmer? buyer = Game1.GetPlayer(playerId);
                bool alreadyQueued = context.WorldState.PendingBuildingOrders.Any(o => o.PlayerId == playerId && o.IsHouseUpgrade);
                bool alreadyActive = buyer != null && (buyer.daysUntilHouseUpgrade.Value >= 0 || buyer.HouseUpgradeLevel >= order.HouseUpgradeLevel);
                if (alreadyQueued || alreadyActive)
                {
                    RefundOrderPaymentOnHost(playerId, order.GoldPaid, order.MaterialsPaid);
                    NotifyPlayerIfOnline(playerId, I18n.Get("mailbox.orders.house_upgrade_already_pending"));
                    context.Monitor.Log($"[MailOrder] Ampliación de casa duplicada rechazada y reembolsada para {playerId}.", LogLevel.Warn);
                    return;
                }
            }

            context.WorldState.PendingBuildingOrders.Add(order);
            context.Monitor.Log($"[MailOrder] Obra diferida '{order.BuildingType}' registrada para {playerId} en '{order.TargetLocationName}' (entrega día {order.DeliveryGameDay} a las 12:00).", LogLevel.Info);
            PersistMailOrderState();
            BroadcastOrdersSync();
        }

        private static void SendHouseUpgradeStartedToClient(long playerId, int daysUntilUpgrade, int targetLevel)
        {
            if (context == null || !Context.IsMainPlayer || playerId == Game1.player.UniqueMultiplayerID)
                return;

            Farmer? target = Game1.GetPlayer(playerId, onlyOnline: true);
            if (target == null || !target.isActive())
                return;

            context.Helper.Multiplayer.SendMessage(
                new HouseUpgradeStartedPayload
                {
                    DaysUntilHouseUpgrade = daysUntilUpgrade,
                    TargetHouseUpgradeLevel = targetLevel
                },
                MailOrderMessages.HouseUpgradeStarted,
                new[] { context.Manifest.UniqueID },
                new[] { playerId }
            );
        }

        private static void HandleHouseUpgradeStartedOnClient(HouseUpgradeStartedPayload payload)
        {
            if (Context.IsMainPlayer || Game1.player == null)
                return;

            if (payload.DaysUntilHouseUpgrade >= 0
                && Game1.player.HouseUpgradeLevel < payload.TargetHouseUpgradeLevel)
            {
                Game1.player.daysUntilHouseUpgrade.Value = payload.DaysUntilHouseUpgrade;
            }
        }

        private static bool DeliverBuildingOrderOnHost(PendingBuildingOrder order)
        {
            return TryDeliverBuildingOrderInternal(order, out _, out _);
        }

        private static bool TryDeliverBuildingOrderInternal(PendingBuildingOrder order, out bool refunded, out Building? affectedBuilding)
        {
            refunded = false;
            affectedBuilding = null;

            if (context == null)
                return true;

            if (order.IsHouseUpgrade)
            {
                Farmer? farmer = Game1.GetPlayer(order.PlayerId);
                if (farmer == null)
                    return false;

                if (farmer.HouseUpgradeLevel < order.HouseUpgradeLevel && farmer.daysUntilHouseUpgrade.Value < 0)
                {
                    farmer.daysUntilHouseUpgrade.Value = Math.Max(order.BuildDays, 1);
                }

                if (order.PlayerId != Game1.player.UniqueMultiplayerID)
                {
                    SendHouseUpgradeStartedToClient(order.PlayerId, farmer.daysUntilHouseUpgrade.Value, order.HouseUpgradeLevel);
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
                {
                    if (order.GoldPaid > 0 || (order.MaterialsPaid != null && order.MaterialsPaid.Count > 0))
                    {
                        RefundOrderPaymentOnHost(order.PlayerId, order.GoldPaid, order.MaterialsPaid);
                        NotifyPlayerIfOnline(order.PlayerId, I18n.Get("mailbox.orders.building_refunded", new { building = order.BuildingDisplayName }));
                    }
                    refunded = true;
                    return true;
                }

                target.upgradeName.Value = order.BuildingType;
                target.daysUntilUpgrade.Value = Math.Max(order.BuildDays, 1);
                Game1.netWorldState.Value.MarkUnderConstruction(order.Builder, target);
                affectedBuilding = target;
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
                affectedBuilding = constructed;

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

        /// <summary>
        /// Reconcilia durante el rollover los pedidos de correo cuyo día de entrega (<c>DeliveryGameDay &lt; newGameDay</c>)
        /// quedó atrás sin materializarse, acreditando construcciones, mejoras de vivienda, paquetes o reembolsos huérfanos.
        /// </summary>
        internal static (int BuildingsCredited, int ItemsCredited, int AnimalsCredited, int OrphanRefunds) ReconcileOverdueOrdersForRolloverOnHost(int newGameDay)
        {
            if (context == null || !Context.IsMainPlayer)
                return (0, 0, 0, 0);

            int buildingsCredited = 0;
            int itemsCredited = 0;
            int animalsCredited = 0;
            int orphanRefunds = 0;
            bool changed = false;

            foreach (PendingBuildingOrder order in context.WorldState.PendingBuildingOrders.ToList())
            {
                if (!RolloverOrderReconciliationLogic.IsOverdueForRollover(order.DeliveryGameDay, newGameDay))
                    continue;

                if (order.IsHouseUpgrade)
                {
                    Farmer? farmer = Game1.GetPlayer(order.PlayerId);
                    if (farmer == null)
                    {
                        context.WorldState.PendingBuildingOrders.Remove(order);
                        changed = true;
                        context.Monitor.Log($"[MailOrder] Pedido huérfano de ampliación de casa {order.OrderId} retirado: jugador {order.PlayerId} inexistente.", LogLevel.Warn);
                        continue;
                    }

                    DoorAssignment? door = context.WorldState.FindResidenceOf(order.PlayerId);
                    if (!farmer.IsMainPlayer && (door == null || door.OwnerId != order.PlayerId))
                    {
                        RefundOrderPaymentOnHost(order.PlayerId, order.GoldPaid, order.MaterialsPaid);
                        context.WorldState.PendingBuildingOrders.Remove(order);
                        orphanRefunds++;
                        changed = true;
                        context.Monitor.Log($"[MailOrder] Ampliación de casa huérfana {order.OrderId} para {farmer.Name} sin residencia propia: reembolso acreditado.", LogLevel.Warn);
                        continue;
                    }

                    int targetLevel = Math.Clamp(order.HouseUpgradeLevel, 1, 3);
                    if (farmer.HouseUpgradeLevel < targetLevel)
                    {
                        int remaining = RolloverOrderReconciliationLogic.ComputeRemainingBuildDaysAfterMissedDelivery(
                            order.BuildDays,
                            order.DeliveryGameDay,
                            newGameDay);

                        if (remaining > 0)
                        {
                            if (farmer.daysUntilHouseUpgrade.Value < 0 || farmer.daysUntilHouseUpgrade.Value > remaining)
                                farmer.daysUntilHouseUpgrade.Value = remaining;
                            context.Monitor.Log($"[MailOrder] Rollover acreditó ampliación pendiente de {farmer.Name} (nivel {targetLevel}, faltan {remaining} día(s)).", LogLevel.Info);
                        }
                        else
                        {
                            DailyRolloverModule.ApplyFarmerHouseUpgradeDirectOnHost(farmer, targetLevel);
                            context.Monitor.Log($"[MailOrder] Rollover completó ampliación de casa vencida para {farmer.Name} hasta nivel {targetLevel}.", LogLevel.Info);
                        }
                    }

                    context.WorldState.PendingBuildingOrders.Remove(order);
                    buildingsCredited++;
                    changed = true;
                    continue;
                }

                if (TryDeliverBuildingOrderInternal(order, out bool refunded, out Building? building))
                {
                    context.WorldState.PendingBuildingOrders.Remove(order);
                    changed = true;

                    if (refunded)
                    {
                        orphanRefunds++;
                        context.Monitor.Log($"[MailOrder] Mejora huérfana '{order.BuildingType}' ({order.OrderId}) reembolsada en rollover por desaparición del edificio destino.", LogLevel.Warn);
                    }
                    else
                    {
                        if (building != null)
                        {
                            int elapsedNights = Math.Max(0, newGameDay - order.DeliveryGameDay);
                            if (elapsedNights > 0)
                            {
                                if (order.IsUpgrade && building.daysUntilUpgrade.Value > 0)
                                {
                                    int rem = building.daysUntilUpgrade.Value - elapsedNights;
                                    if (rem <= 0)
                                        building.FinishConstruction();
                                    else
                                        building.daysUntilUpgrade.Value = rem;
                                }
                                else if (!order.IsUpgrade && building.daysOfConstructionLeft.Value > 0)
                                {
                                    int rem = building.daysOfConstructionLeft.Value - elapsedNights;
                                    if (rem <= 0)
                                        building.FinishConstruction();
                                    else
                                        building.daysOfConstructionLeft.Value = rem;
                                }

                                Game1.netWorldState.Value.UpdateUnderConstruction();
                            }
                        }

                        buildingsCredited++;
                        context.Monitor.Log($"[MailOrder] Rollover materializó obra vencida '{order.BuildingType}' para {order.PlayerId} en '{order.TargetLocationName}'.", LogLevel.Info);
                    }
                }
                else
                {
                    RefundOrderPaymentOnHost(order.PlayerId, order.GoldPaid, order.MaterialsPaid);
                    context.WorldState.PendingBuildingOrders.Remove(order);
                    orphanRefunds++;
                    changed = true;
                    context.Monitor.Log($"[MailOrder] Obra huérfana '{order.BuildingType}' ({order.OrderId}) imposible de materializar en '{order.TargetLocationName}': reembolso acreditado.", LogLevel.Warn);
                }
            }

            foreach (PendingMailItemOrder itemOrder in context.WorldState.PendingMailItemOrders.ToList())
            {
                if (!RolloverOrderReconciliationLogic.IsOverdueForRollover(itemOrder.DeliveryGameDay, newGameDay))
                    continue;

                if (DeliverItemOrderOnHost(itemOrder))
                {
                    context.WorldState.PendingMailItemOrders.Remove(itemOrder);
                    itemsCredited++;
                    changed = true;
                }
                else
                {
                    int refundGold = itemOrder.Items.Sum(i => Math.Max(0, i.PricePaid));
                    RefundOrderPaymentOnHost(itemOrder.PlayerId, refundGold);
                    context.WorldState.PendingMailItemOrders.Remove(itemOrder);
                    orphanRefunds++;
                    changed = true;
                    context.Monitor.Log($"[MailOrder] Paquete huérfano {itemOrder.OrderId} ({itemOrder.ShopId}) reembolsado en rollover ({refundGold}g) por falta de residencia.", LogLevel.Warn);
                }
            }

            foreach (PendingAnimalOrder animalOrder in context.WorldState.PendingAnimalOrders.ToList())
            {
                if (!RolloverOrderReconciliationLogic.IsOverdueForRollover(animalOrder.DeliveryGameDay, newGameDay))
                    continue;

                if (DeliverAnimalOrderOnHost(animalOrder))
                {
                    context.WorldState.PendingAnimalOrders.Remove(animalOrder);
                    animalsCredited++;
                    changed = true;
                }
            }

            if (changed)
            {
                PersistMailOrderState();
                BroadcastOrdersSync();
            }

            return (buildingsCredited, itemsCredited, animalsCredited, orphanRefunds);
        }
    }
}
