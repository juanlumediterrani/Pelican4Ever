using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Economy;
using Pelican4Ever.Core.Time;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Objects;
using xTile.Dimensions;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Gestiona los pedidos a tiendas desde el buzón de correo (Épica 22, v0.3.1):
    /// menú normal de cada comercio, cobro en el acto y entrega diferida al día siguiente a las 12:00.
    /// </summary>
    public sealed partial class MailOrderModule : IModule
    {
        private static ModContext? context;

        // Estado local de sesión de pedido en el cliente/jugador local
        private static string? activeShopOrderId;
        private static ShopMenu? activeMailOrderShopMenu;
        private static int lastSellBlockedTick = -100;
        private static readonly List<MailOrderItemLine> activeShopBuffer = new();
        private static int activeShopExpectedDeliveryDay;

        // Caché de pedidos pendientes en clientes (sincronizada desde el Host)
        private static List<PendingMailItemOrder> clientSyncedItemOrders = new();
        private static List<PendingBuildingOrder> clientSyncedBuildingOrders = new();
        private static List<PendingAnimalOrder> clientSyncedAnimalOrders = new();

        public string Name => "MailOrder";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            ctx.Helper.Events.GameLoop.DayStarted += OnDayStarted;
            ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
            ctx.Helper.Events.Display.MenuChanged += OnMenuChanged;
            ctx.Helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;

            if (Context.IsMainPlayer)
            {
                ctx.AddCommand(
                    "p4e_mail_orders",
                    "Lista pedidos por buzón pendientes o fuerza su entrega inmediata (uso: p4e_mail_orders [deliver_now]).",
                    OnConsoleMailOrders
                );
            }

            ApplyHarmonyPatches(ctx.Harmony);
        }

        private void ApplyHarmonyPatches(Harmony harmony)
        {
            PatchSafe(harmony, "GameLocation.performAction",
                AccessTools.Method(typeof(GameLocation), nameof(GameLocation.performAction),
                    new[] { typeof(string[]), typeof(Farmer), typeof(Location) }),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_Location_PerformAction)),
                criticality: PatchCriticality.Experience);

            PatchSafe(harmony, "ShopMenu.tryToPurchaseItem",
                AccessTools.Method(typeof(ShopMenu), "tryToPurchaseItem",
                    new[] { typeof(ISalable), typeof(ISalable), typeof(int), typeof(int), typeof(int) }),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_ShopMenu_TryToPurchaseItem)),
                postfix: new HarmonyMethod(typeof(MailOrderModule), nameof(Postfix_ShopMenu_TryToPurchaseItem)),
                criticality: PatchCriticality.Integrity);

            PatchSafe(harmony, "ShopMenu.highlightItemToSell",
                AccessTools.Method(typeof(ShopMenu), nameof(ShopMenu.highlightItemToSell), new[] { typeof(Item) }),
                postfix: new HarmonyMethod(typeof(MailOrderModule), nameof(Postfix_ShopMenu_HighlightItemToSell)),
                criticality: PatchCriticality.Experience);

            PatchSafe(harmony, "ShopMenu.receiveLeftClick",
                AccessTools.Method(typeof(ShopMenu), nameof(ShopMenu.receiveLeftClick), new[] { typeof(int), typeof(int), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_ShopMenu_ReceiveLeftClick)),
                criticality: PatchCriticality.Experience);

            PatchSafe(harmony, "ShopMenu.receiveRightClick",
                AccessTools.Method(typeof(ShopMenu), nameof(ShopMenu.receiveRightClick), new[] { typeof(int), typeof(int), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(MailOrderModule), nameof(Prefix_ShopMenu_ReceiveRightClick)),
                criticality: PatchCriticality.Experience);

            ApplyPackagePatches(harmony);
            ApplyRobinPatches(harmony);
            ApplyMarniePatches(harmony);
            ApplyClintPatches(harmony);
        }

        private static void PatchSafe(
            Harmony harmony,
            string label,
            System.Reflection.MethodBase? original,
            HarmonyMethod? prefix = null,
            HarmonyMethod? postfix = null,
            PatchCriticality criticality = PatchCriticality.Experience)
        {
            if (context == null)
                return;

            context.Patch($"MailOrder.{label}", "MailOrder", criticality, original, prefix, postfix);
            _ = harmony;
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            ResetLocalSessions();
            if (Context.IsMainPlayer)
            {
                TryDeliverDueOrders();
                BroadcastOrdersSync();
            }
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            ResetLocalSessions();
            clientSyncedItemOrders.Clear();
            clientSyncedBuildingOrders.Clear();
            clientSyncedAnimalOrders.Clear();
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            TryDeliverDueOrders();
            BroadcastOrdersSync();
        }

        private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || context == null)
                return;

            if (activeShopOrderId != null && Game1.activeClickableMenu is not ShopMenu)
            {
                FlushActiveShopBuffer();
            }

            if (Context.IsMainPlayer)
            {
                TryDeliverDueOrders();
                TryDeliverCompletedToolUpgrades();
            }
        }

        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            if (activeMailOrderShopMenu != null && ReferenceEquals(e.OldMenu, activeMailOrderShopMenu) && !ReferenceEquals(e.NewMenu, activeMailOrderShopMenu))
            {
                activeMailOrderShopMenu = null;
            }

            if (activeShopOrderId != null && e.NewMenu is not ShopMenu)
            {
                FlushActiveShopBuffer();
            }

            if (clintUpgradeMailSessionActive
                && e.OldMenu is ShopMenu oldShop
                && string.Equals(oldShop.ShopId, "ClintUpgrade", StringComparison.Ordinal)
                && e.NewMenu is not ShopMenu)
            {
                clintUpgradeMailSessionActive = false;
            }
        }

        private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
        {
            if (Context.IsMainPlayer && Context.IsWorldReady)
            {
                SendOrdersSyncTo(e.Peer.PlayerID);
            }
        }

        private static void ResetLocalSessions()
        {
            activeShopOrderId = null;
            activeMailOrderShopMenu = null;
            lastSellBlockedTick = -100;
            activeShopBuffer.Clear();
            activeShopExpectedDeliveryDay = 0;
            ResetRobinSession();
            ResetMarnieSession();
            ResetClintSession();
        }

        // -------------------------------------------------------------------------
        // Planificación horaria y consultas de estado
        // -------------------------------------------------------------------------

        public static int ComputeDeliveryGameDay()
        {
            int currentDay = Game1.Date?.TotalDays ?? 0;
            if (context == null)
                return currentDay + 1;

            DateTime localNow = ServerTime.LocalNow(context.Config.TimeZoneId).Add(RealTimeClockModule.ManualTimeOffset);
            return MailOrderScheduleLogic.ComputeDeliveryGameDay(
                currentDay,
                localNow.TimeOfDay,
                context.Config.ParsedCalendarDayChangeTime,
                context.Config.ParsedDayStart
            );
        }

        private static IReadOnlyList<PendingMailItemOrder> GetItemOrdersSnapshot() =>
            Context.IsMainPlayer
                ? (context?.WorldState.PendingMailItemOrders ?? (IReadOnlyList<PendingMailItemOrder>)Array.Empty<PendingMailItemOrder>())
                : clientSyncedItemOrders;

        private static IReadOnlyList<PendingBuildingOrder> GetBuildingOrdersSnapshot() =>
            Context.IsMainPlayer
                ? (context?.WorldState.PendingBuildingOrders ?? (IReadOnlyList<PendingBuildingOrder>)Array.Empty<PendingBuildingOrder>())
                : clientSyncedBuildingOrders;

        private static IReadOnlyList<PendingAnimalOrder> GetAnimalOrdersSnapshot() =>
            Context.IsMainPlayer
                ? (context?.WorldState.PendingAnimalOrders ?? (IReadOnlyList<PendingAnimalOrder>)Array.Empty<PendingAnimalOrder>())
                : clientSyncedAnimalOrders;

        private static bool IsSharedOrderTarget(string? targetLocationName, long playerId)
        {
            if (string.IsNullOrWhiteSpace(targetLocationName) || context?.WorldState == null)
                return false;

            DoorAssignment? residence = context.WorldState.FindResidenceOf(playerId);
            if (residence == null || string.IsNullOrWhiteSpace(residence.FarmSlotId))
                return false;

            string? targetSlot = VisitorPermissionLogic.ResidenceSlotOf(targetLocationName);
            return !string.IsNullOrWhiteSpace(targetSlot)
                && string.Equals(targetSlot, residence.FarmSlotId, StringComparison.OrdinalIgnoreCase);
        }

        public static int CountPendingOrdersFor(long playerId)
        {
            int items = GetItemOrdersSnapshot().Count(o => o.PlayerId == playerId);
            int buildings = GetBuildingOrdersSnapshot().Count(o => o.PlayerId == playerId || IsSharedOrderTarget(o.TargetLocationName, playerId));
            int animals = GetAnimalOrdersSnapshot().Count(o => o.PlayerId == playerId || IsSharedOrderTarget(o.TargetLocationName, playerId));
            return items + buildings + animals;
        }

        private static MailOrderPlayerProgress BuildLocalPlayerProgress()
        {
            Farmer p = Game1.player;
            Farmer master = Game1.MasterPlayer;
            bool isDesert = (master?.mailReceived.Contains("ccVault") ?? false)
                || (master?.mailReceived.Contains("JojaVault") ?? false)
                || (p?.mailReceived.Contains("ccVault") ?? false);

            return new MailOrderPlayerProgress(
                TotalDays: Game1.Date?.TotalDays ?? 0,
                HasWillyIntroMail: p?.mailReceived.Contains("spring_2_1") ?? false,
                IsGuildMember: p?.mailReceived.Contains("guildMember") ?? false,
                DeepestMineLevel: p?.deepestMineLevel ?? 0,
                IsDesertUnlocked: isDesert,
                HasRustyKey: p?.hasRustyKey ?? false,
                CanUnderstandDwarves: p?.canUnderstandDwarves ?? false
            );
        }

        // -------------------------------------------------------------------------
        // Buzón interior (residencias privadas y Granja Host)
        // -------------------------------------------------------------------------

        public static bool Prefix_Location_PerformAction(GameLocation __instance, string[] action, Farmer who, Location tileLocation, ref bool __result)
        {
            if (context == null || who == null || !who.IsLocalPlayer || action == null || action.Length == 0)
                return true;

            if (!string.Equals(action[0], "Mailbox", StringComparison.Ordinal))
                return true;

            string locName = __instance.NameOrUniqueName;
            bool isHostMainFarm = string.Equals(locName, HostPlazaLayout.HostFarm, StringComparison.OrdinalIgnoreCase);
            bool isPrivateFarm = Core.World.LocationCullingLogic.IsPrivateFarmOrAnnex(locName);
            if (!isHostMainFarm && !isPrivateFarm)
                return true;

            long myId = who.UniqueMultiplayerID;
            if (isPrivateFarm)
            {
                DoorAssignment? assignment = context.WorldState.Doors.Values.FirstOrDefault(d =>
                    string.Equals(d.FarmSlotId, locName, StringComparison.OrdinalIgnoreCase));
                if (assignment != null && !assignment.IsResident(myId))
                {
                    string ownerName = ResolvePlayerName(assignment.OwnerId);
                    Game1.drawObjectDialogue(I18n.Get("mailbox.restricted", new { owner = ownerName }));
                    __result = true;
                    return false;
                }
            }
            else if (isHostMainFarm)
            {
                // En la granja principal, los buzones de los portales ya los maneja TownMailboxModule;
                // si es el buzón de la casa del Host, solo el Host abre el menú de buzón.
                if (!Context.IsMainPlayer)
                {
                    Game1.drawObjectDialogue(Game1.content.LoadString("Strings\\Locations:Farm_OtherPlayerMailbox"));
                    __result = true;
                    return false;
                }
            }

            ShowInteriorMailboxMenu(__instance);
            __result = true;
            return false;
        }

        private static void ShowInteriorMailboxMenu(GameLocation location)
        {
            long myId = Game1.player.UniqueMultiplayerID;
            int pendingCount = CountPendingOrdersFor(myId);

            var choices = new List<Response>
            {
                new("Mailbox", Game1.mailbox.Count > 0
                    ? I18n.Get("mailbox.menu.check_mail", new { count = Game1.mailbox.Count })
                    : I18n.Get("mailbox.menu.check_mail_empty")),
                new("ShopOrders", pendingCount > 0
                    ? I18n.Get("mailbox.menu.shop_orders_pending", new { count = pendingCount })
                    : I18n.Get("mailbox.menu.shop_orders"))
            };

            if (pendingCount > 0)
            {
                choices.Add(new Response("PendingStatus", I18n.Get("mailbox.menu.view_pending", new { count = pendingCount })));
            }

            choices.Add(new Response("Cancel", I18n.Get("dialog.cancel")));

            QuestionDialog.Ask(
                I18n.Get("mailbox.interior_prompt"),
                choices.ToArray(),
                (who, answer) =>
                {
                    if (answer == "Mailbox")
                    {
                        location.mailbox();
                    }
                    else if (answer == "ShopOrders")
                    {
                        OpenShopCatalogMenu();
                    }
                    else if (answer == "PendingStatus")
                    {
                        ShowPendingOrdersDialog();
                    }
                }
            );
        }

        // -------------------------------------------------------------------------
        // Catálogo de tiendas y consulta de pedidos en camino
        // -------------------------------------------------------------------------

        public static void OpenShopCatalogMenu()
        {
            if (Game1.currentLocation == null)
                return;

            IReadOnlyList<MailOrderShopDescriptor> shops = MailOrderCatalogLogic.GetAvailableShops(BuildLocalPlayerProgress());
            var options = shops
                .Select(s => new KeyValuePair<string, string>(s.ShopId, I18n.Get(s.TranslationKey)))
                .ToList();

            Game1.currentLocation.ShowPagedResponses(
                I18n.Get("mailbox.orders.catalog_prompt"),
                options,
                chosenShopId =>
                {
                    if (string.IsNullOrEmpty(chosenShopId))
                        return;
                    OpenSelectedShop(chosenShopId);
                },
                auto_select_single_choice: false
            );
        }

        private static void OpenSelectedShop(string shopId)
        {
            switch (shopId)
            {
                case "Carpenter":
                    ShowRobinMailMenu();
                    break;
                case "Blacksmith":
                    ShowClintMailMenu();
                    break;
                case "AnimalShop":
                    ShowMarnieMailMenu();
                    break;
                default:
                    MailOrderShopDescriptor? desc = MailOrderCatalogLogic.FindShop(shopId);
                    if (desc != null)
                        OpenDeferredShopMenu(desc.ShopId, desc.OwnerNpcName);
                    break;
            }
        }

        public static void ShowPendingOrdersDialog()
        {
            long myId = Game1.player.UniqueMultiplayerID;
            var lines = new List<string>();

            foreach (PendingMailItemOrder itemOrder in GetItemOrdersSnapshot().Where(o => o.PlayerId == myId))
            {
                string shopName = MailOrderCatalogLogic.FindShop(itemOrder.ShopId) is { } s ? I18n.Get(s.TranslationKey) : itemOrder.ShopId;
                int totalUnits = itemOrder.Items.Sum(i => i.Stack);
                string dateText = FormatGameDay(itemOrder.DeliveryGameDay);
                lines.Add(I18n.Get("mailbox.orders.pending_item_line", new { shop = shopName, count = totalUnits, date = dateText }));
            }

            foreach (PendingBuildingOrder bOrder in GetBuildingOrdersSnapshot().Where(o => o.PlayerId == myId || IsSharedOrderTarget(o.TargetLocationName, myId)))
            {
                string dateText = FormatGameDay(bOrder.DeliveryGameDay);
                string label = bOrder.IsHouseUpgrade
                    ? I18n.Get("mailbox.orders.pending_house_upgrade", new { level = bOrder.HouseUpgradeLevel, date = dateText })
                    : I18n.Get("mailbox.orders.pending_building_line", new { building = bOrder.BuildingDisplayName, date = dateText });
                lines.Add(label);
            }

            foreach (PendingAnimalOrder aOrder in GetAnimalOrdersSnapshot().Where(o => o.PlayerId == myId || IsSharedOrderTarget(o.TargetLocationName, myId)))
            {
                string dateText = FormatGameDay(aOrder.DeliveryGameDay);
                lines.Add(I18n.Get("mailbox.orders.pending_animal_line", new { name = aOrder.AnimalName, type = aOrder.AnimalType, date = dateText }));
            }

            if (lines.Count == 0)
            {
                Game1.drawObjectDialogue(I18n.Get("mailbox.orders.none_pending"));
                return;
            }

            Game1.drawObjectDialogue(string.Join("^", lines));
        }

        private static string FormatGameDay(int totalDays)
        {
            var date = new WorldDate();
            date.TotalDays = Math.Max(0, totalDays);
            return $"{Utility.getSeasonNameFromNumber((int)date.Season)} {date.DayOfMonth}";
        }

        // -------------------------------------------------------------------------
        // Apertura de ShopMenu diferido (TASK-2203)
        // -------------------------------------------------------------------------

        private static void OpenDeferredShopMenu(string shopId, string ownerName)
        {
            activeShopBuffer.Clear();
            activeShopOrderId = shopId;
            activeShopExpectedDeliveryDay = ComputeDeliveryGameDay();

            if (!Utility.TryOpenShopMenu(shopId, ownerName, playOpenSound: true))
            {
                activeShopOrderId = null;
                Game1.drawObjectDialogue(I18n.Get("mailbox.orders.shop_unavailable"));
                return;
            }

            if (Game1.activeClickableMenu is ShopMenu shopMenu)
            {
                ConfigureMailOrderShopMenu(shopMenu);

                if (shopMenu.portraitTexture == null && !string.IsNullOrWhiteSpace(ownerName))
                {
                    try
                    {
                        string assetName = $"Portraits\\{ownerName}";
                        if (Game1.content.DoesAssetExist<Texture2D>(assetName))
                            shopMenu.portraitTexture = Game1.content.Load<Texture2D>(assetName);
                    }
                    catch
                    {
                        // Sin retrato personalizado si no existe asset
                    }
                }
            }
        }

        internal static void ConfigureMailOrderShopMenu(ShopMenu shopMenu)
        {
            activeMailOrderShopMenu = shopMenu;
            shopMenu.categoriesToSellHere.Clear();
            shopMenu.tagsToSellHere.Clear();
        }

        internal static bool IsMailOrderShopMenu(ShopMenu? menu)
        {
            if (menu == null)
                return false;

            if (activeMailOrderShopMenu != null)
                return ReferenceEquals(menu, activeMailOrderShopMenu);

            return (activeShopOrderId != null && string.Equals(menu.ShopId, activeShopOrderId, StringComparison.Ordinal))
                || (clintUpgradeMailSessionActive && string.Equals(menu.ShopId, "ClintUpgrade", StringComparison.Ordinal));
        }

        public static void Postfix_ShopMenu_HighlightItemToSell(ShopMenu __instance, ref bool __result)
        {
            if (__result && IsMailOrderShopMenu(__instance) && __instance.heldItem == null)
                __result = false;
        }

        public static bool Prefix_ShopMenu_ReceiveLeftClick(ShopMenu __instance, int x, int y)
        {
            return !TryHandleBlockedMailOrderSellClick(__instance, x, y);
        }

        public static bool Prefix_ShopMenu_ReceiveRightClick(ShopMenu __instance, int x, int y)
        {
            return !TryHandleBlockedMailOrderSellClick(__instance, x, y);
        }

        private static bool TryHandleBlockedMailOrderSellClick(ShopMenu menu, int x, int y)
        {
            if (!IsMailOrderShopMenu(menu))
                return false;

            if (menu.safetyTimer > 0 || menu.heldItem != null || menu.readOnly || menu.inventory == null)
                return false;

            if (menu.inventory.getItemAt(x, y) == null)
                return false;

            NotifyMailOrderSellBlocked(menu);
            return true;
        }

        private static void NotifyMailOrderSellBlocked(ShopMenu menu)
        {
            string message = I18n.Get("mailbox.orders.cannot_sell");
            int currentTick = Game1.ticks;
            if (currentTick - lastSellBlockedTick >= 15)
            {
                Game1.playSound("cancel");
                lastSellBlockedTick = currentTick;
            }

            if (!string.IsNullOrEmpty(menu.potraitPersonDialogue))
                menu.potraitPersonDialogue = Game1.parseText(message, Game1.dialogueFont, 304);

            bool alreadyVisible = Game1.hudMessages.Any(m =>
                m != null && string.Equals(m.message, message, StringComparison.Ordinal));
            if (!alreadyVisible)
                Game1.addHUDMessage(new HUDMessage(message, HUDMessage.error_type));
        }

        private static int prePurchaseMoney;

        public static void Prefix_ShopMenu_TryToPurchaseItem(ShopMenu __instance)
        {
            if (activeShopOrderId == null || !string.Equals(__instance.ShopId, activeShopOrderId, StringComparison.Ordinal))
                return;

            prePurchaseMoney = Game1.player.Money;
        }

        public static void Postfix_ShopMenu_TryToPurchaseItem(ShopMenu __instance, ISalable item)
        {
            if (activeShopOrderId == null || !string.Equals(__instance.ShopId, activeShopOrderId, StringComparison.Ordinal))
                return;

            // Si el jugador compró una receta o una acción inmediata sin heldItem físico, no encolar paquete
            if (item != null && item.IsRecipe)
                return;

            if (__instance.heldItem is Item purchasedItem)
            {
                int paid = Math.Max(0, prePurchaseMoney - Game1.player.Money);
                activeShopBuffer.Add(new MailOrderItemLine
                {
                    QualifiedItemId = purchasedItem.QualifiedItemId,
                    DisplayName = purchasedItem.DisplayName,
                    Stack = purchasedItem.Stack,
                    Quality = purchasedItem.Quality,
                    PricePaid = paid
                });

                __instance.heldItem = null;
                Game1.addHUDMessage(new HUDMessage(
                    I18n.Get("mailbox.orders.item_queued", new { item = purchasedItem.DisplayName, count = purchasedItem.Stack }),
                    HUDMessage.newQuest_type));
            }
        }

        private static void FlushActiveShopBuffer()
        {
            string? shopId = activeShopOrderId;
            activeShopOrderId = null;
            activeMailOrderShopMenu = null;

            if (string.IsNullOrEmpty(shopId) || activeShopBuffer.Count == 0 || context == null)
            {
                activeShopBuffer.Clear();
                return;
            }

            List<MailOrderItemLine> lines = activeShopBuffer.ToList();
            activeShopBuffer.Clear();

            if (Context.IsMainPlayer)
            {
                EnqueueItemOrderOnHost(Game1.player.UniqueMultiplayerID, shopId, lines);
            }
            else
            {
                context.Helper.Multiplayer.SendMessage(
                    new PlaceMailItemOrderPayload { ShopId = shopId, Items = lines },
                    MailOrderMessages.PlaceItemOrderRequest,
                    new[] { context.Manifest.UniqueID },
                    new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );
            }
        }

        private static void EnqueueItemOrderOnHost(long playerId, string shopId, List<MailOrderItemLine> lines)
        {
            if (context == null || !Context.IsMainPlayer || lines.Count == 0)
                return;

            int deliveryDay = ComputeDeliveryGameDay();
            PendingMailItemOrder? existing = context.WorldState.PendingMailItemOrders.FirstOrDefault(o =>
                o.PlayerId == playerId
                && string.Equals(o.ShopId, shopId, StringComparison.OrdinalIgnoreCase)
                && o.DeliveryGameDay == deliveryDay);

            if (existing != null)
            {
                existing.Items.AddRange(lines);
            }
            else
            {
                context.WorldState.PendingMailItemOrders.Add(new PendingMailItemOrder
                {
                    PlayerId = playerId,
                    ShopId = shopId,
                    GiftboxIndex = MailOrderCatalogLogic.GetGiftboxIndex(shopId),
                    CreatedAtGameDay = Game1.Date?.TotalDays ?? 0,
                    DeliveryGameDay = deliveryDay,
                    DeliveryTimeOfDay = MailOrderScheduleLogic.DefaultDeliveryTimeOfDay,
                    Items = lines
                });
            }

            context.Monitor.Log($"[MailOrder] Pedido de {lines.Count} línea(s) en '{shopId}' registrado para jugador {playerId} (entrega día {deliveryDay} a las 12:00).", LogLevel.Info);
            BroadcastOrdersSync();
        }

        // -------------------------------------------------------------------------
        // Red y sincronización Host <-> Clientes
        // -------------------------------------------------------------------------

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || e.FromModID != context.Manifest.UniqueID)
                return;

            switch (e.Type)
            {
                case MailOrderMessages.SyncOrders:
                    if (!Context.IsMainPlayer && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
                    {
                        var sync = e.ReadAs<MailOrdersSyncPayload>();
                        clientSyncedItemOrders = sync.ItemOrders ?? new List<PendingMailItemOrder>();
                        clientSyncedBuildingOrders = sync.BuildingOrders ?? new List<PendingBuildingOrder>();
                        clientSyncedAnimalOrders = sync.AnimalOrders ?? new List<PendingAnimalOrder>();
                    }
                    break;

                case MailOrderMessages.PlaceItemOrderRequest:
                    if (Context.IsMainPlayer)
                    {
                        var req = e.ReadAs<PlaceMailItemOrderPayload>();
                        EnqueueItemOrderOnHost(e.FromPlayerID, req.ShopId, req.Items);
                    }
                    break;

                case MailOrderMessages.PlaceBuildingOrderRequest:
                    if (Context.IsMainPlayer)
                    {
                        var req = e.ReadAs<PlaceBuildingOrderPayload>();
                        HandlePlaceBuildingOrderOnHost(e.FromPlayerID, req.Order);
                    }
                    break;

                case MailOrderMessages.PlaceAnimalOrderRequest:
                    if (Context.IsMainPlayer)
                    {
                        var req = e.ReadAs<PlaceAnimalOrderPayload>();
                        HandlePlaceAnimalOrderOnHost(e.FromPlayerID, req.Order);
                    }
                    break;

                case MailOrderMessages.ClaimPackageRequest:
                    if (Context.IsMainPlayer)
                    {
                        var req = e.ReadAs<ClaimMailPackagePayload>();
                        HandleClaimPackageOnHost(e.FromPlayerID, req);
                    }
                    break;

                case MailOrderMessages.ClaimPackageResult:
                    if (!Context.IsMainPlayer && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
                    {
                        HandleClaimPackageResultOnClient(e.ReadAs<ClaimMailPackageResultPayload>());
                    }
                    break;

                case MailOrderMessages.OrderResult:
                    if (!Context.IsMainPlayer && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
                    {
                        var res = e.ReadAs<MailOrderResultPayload>();
                        if (!string.IsNullOrWhiteSpace(res.Message))
                        {
                            Game1.addHUDMessage(new HUDMessage(res.Message, res.Accepted ? HUDMessage.newQuest_type : HUDMessage.error_type));
                        }
                    }
                    break;
            }
        }

        private static void BroadcastOrdersSync()
        {
            if (context == null || !Context.IsMainPlayer)
                return;

            var payload = new MailOrdersSyncPayload
            {
                ItemOrders = context.WorldState.PendingMailItemOrders,
                BuildingOrders = context.WorldState.PendingBuildingOrders,
                AnimalOrders = context.WorldState.PendingAnimalOrders
            };

            context.Helper.Multiplayer.SendMessage(
                payload,
                MailOrderMessages.SyncOrders,
                new[] { context.Manifest.UniqueID }
            );
        }

        private static void SendOrdersSyncTo(long playerId)
        {
            if (context == null || !Context.IsMainPlayer)
                return;

            var payload = new MailOrdersSyncPayload
            {
                ItemOrders = context.WorldState.PendingMailItemOrders,
                BuildingOrders = context.WorldState.PendingBuildingOrders,
                AnimalOrders = context.WorldState.PendingAnimalOrders
            };

            context.Helper.Multiplayer.SendMessage(
                payload,
                MailOrderMessages.SyncOrders,
                new[] { context.Manifest.UniqueID },
                new[] { playerId }
            );
        }

        private static string ResolvePlayerName(long playerId)
        {
            if (context != null && context.WorldState.Players.TryGetValue(playerId, out var rec) && !string.IsNullOrWhiteSpace(rec.PlayerName))
                return rec.PlayerName;

            Farmer? f = Game1.GetPlayer(playerId);
            return !string.IsNullOrWhiteSpace(f?.Name) ? f.Name : playerId.ToString();
        }

        // -------------------------------------------------------------------------
        // Comando de diagnóstico
        // -------------------------------------------------------------------------

        private void OnConsoleMailOrders(string command, string[] args)
        {
            if (context == null || !Context.IsMainPlayer)
                return;

            if (args.Length > 0 && string.Equals(args[0], "deliver_now", StringComparison.OrdinalIgnoreCase))
            {
                int delivered = ForceDeliverAllNow();
                context.Monitor.Log($"[MailOrder] Entrega forzada completada: {delivered} pedido(s) materializado(s).", LogLevel.Info);
                return;
            }

            int items = context.WorldState.PendingMailItemOrders.Count;
            int buildings = context.WorldState.PendingBuildingOrders.Count;
            int animals = context.WorldState.PendingAnimalOrders.Count;
            context.Monitor.Log($"[MailOrder] Pedidos pendientes: {items} paquetes, {buildings} construcciones, {animals} animales.", LogLevel.Info);

            foreach (var o in context.WorldState.PendingMailItemOrders)
                context.Monitor.Log($"  - Paquete {o.OrderId} ({o.ShopId}) jugador={o.PlayerId} día={o.DeliveryGameDay} líneas={o.Items.Count}", LogLevel.Info);
            foreach (var b in context.WorldState.PendingBuildingOrders)
                context.Monitor.Log($"  - Obra {b.OrderId} ({b.BuildingType}) jugador={b.PlayerId} loc={b.TargetLocationName} día={b.DeliveryGameDay}", LogLevel.Info);
            foreach (var a in context.WorldState.PendingAnimalOrders)
                context.Monitor.Log($"  - Animal {a.OrderId} ({a.AnimalType} '{a.AnimalName}') jugador={a.PlayerId} loc={a.TargetLocationName} día={a.DeliveryGameDay}", LogLevel.Info);
        }
    }
}
