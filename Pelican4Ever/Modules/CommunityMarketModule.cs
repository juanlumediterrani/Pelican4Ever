using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Economy;
using Pelican4Ever.Framework;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Gestiona el Tablón de Mercado Vecinal Asíncrono (TASK-503).
    /// Permite compraventa de productos entre vecinos sin coincidencia horaria.
    /// </summary>
    public sealed partial class CommunityMarketModule : IModule
    {
        private static ModContext? context;

        public string Name => "CommunityMarket";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;
            RegisterDeliveries(ctx);

            ctx.Helper.Events.Input.ButtonPressed += OnButtonPressed;
            ctx.Helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            RegisterMarketCommands();
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            RegisterMarketCommands();
            CheckPendingMarketGold(Game1.player);
        }

        private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
        {
            if (Context.IsMainPlayer && context != null)
            {
                // Sincronizar estado completo del mercado al cliente que entra
                context.Helper.Multiplayer.SendMessage(
                    context.WorldState.MarketListings,
                    MarketMessages.SyncMarketState,
                    new[] { context.Manifest.UniqueID },
                    new[] { e.Peer.PlayerID }
                );

                Farmer? joinedFarmer = Game1.GetPlayer(e.Peer.PlayerID);
                if (joinedFarmer != null)
                {
                    CheckPendingMarketGold(joinedFarmer);
                }
            }
        }

        private static void RegisterMarketCommands()
        {
            try
            {
                if (!ChatCommands.Exists("market"))
                {
                    ChatCommands.Register(
                        "market",
                        (command, chat) => OpenMarketMenu(),
                        name => "Abre el Tablón del Mercado Vecinal (/market o /mercado)",
                        new[] { "mercado" },
                        mainOnly: false,
                        multiplayerOnly: true,
                        cheatsOnly: false
                    );
                }
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[CommunityMarketModule] Error registrando comando /market: {ex}", LogLevel.Warn);
            }
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || context == null)
                return;

            if (!e.Button.IsActionButton())
                return;

            // Interacción con el tablón del mercado vecinal en Town Plaza o Farm Central Plaza
            if (Game1.currentLocation is { } location && Game1.activeClickableMenu == null)
            {
                Vector2 grabTile = e.Cursor.GrabTile;

                // La zona del mercado es invisible: nunca secuestrar el clic de algo que ocupe la casilla
                bool tileOccupied = location.getBuildingAt(grabTile) != null
                    || location.objects.ContainsKey(grabTile)
                    || location.GetFurnitureAt(grabTile) != null;

                if (!tileOccupied && CommunityMarketLogic.IsMarketInteractionTile(location.NameOrUniqueName, (int)grabTile.X, (int)grabTile.Y))
                {
                    OpenMarketMenu();
                    context.Helper.Input.Suppress(e.Button);
                }
            }
        }

        public static void OpenMarketMenu()
        {
            if (Game1.currentLocation == null)
                return;

            Response[] choices = new[]
            {
                new Response("Browse", "Ver ofertas a la venta"),
                new Response("Sell", "Poner objeto en venta (sostenido en mano)"),
                new Response("Manage", "Gestionar mis ofertas activas"),
                new Response("Cancel", "Salir")
            };

            QuestionDialog.Ask(
                "Tablón del Mercado Vecinal de Pelican Town:",
                choices,
                (who, whichAnswer) =>
                {
                    switch (whichAnswer)
                    {
                        case "Browse":
                            ShowBrowseMenu();
                            break;
                        case "Sell":
                            PromptSellHeldItem();
                            break;
                        case "Manage":
                            ShowManageMyListingsMenu();
                            break;
                    }
                }
            );
        }

        private static void ShowBrowseMenu()
        {
            if (context == null || Game1.currentLocation == null) return;

            long myId = Game1.player.UniqueMultiplayerID;
            List<MarketListing> activeListings = context.WorldState.MarketListings
                .Where(l => !l.IsSold && l.SellerId != myId)
                .OrderByDescending(l => l.CreatedAtUtc)
                .Take(5)
                .ToList();

            if (activeListings.Count == 0)
            {
                Game1.drawObjectDialogue("No hay ofertas de vecinos en venta en este momento.");
                return;
            }

            var choices = new List<Response>();
            foreach (MarketListing listing in activeListings)
            {
                string text = $"{listing.ItemDisplayName} (x{listing.Stack}) - {listing.PriceGold}g (De: {listing.SellerName})";
                choices.Add(new Response(listing.ListingId, text));
            }
            choices.Add(new Response("Cancel", "Volver"));

            QuestionDialog.Ask(
                "Ofertas disponibles en el Mercado Vecinal:",
                choices.ToArray(),
                (who, whichAnswer) =>
                {
                    if (whichAnswer == "Cancel")
                        return;

                    MarketListing? selected = activeListings.FirstOrDefault(l => l.ListingId == whichAnswer);
                    if (selected != null)
                    {
                        ConfirmPurchase(selected);
                    }
                }
            );
        }

        private static void ConfirmPurchase(MarketListing listing)
        {
            if (Game1.currentLocation == null || context == null) return;

            Response[] choices = new[]
            {
                new Response("Buy", $"Comprar por {listing.PriceGold}g"),
                new Response("Cancel", "Cancelar")
            };

            QuestionDialog.Ask(
                $"¿Deseas comprar {listing.ItemDisplayName} (x{listing.Stack}) de {listing.SellerName} por {listing.PriceGold}g?",
                choices,
                (who, whichAnswer) =>
                {
                    if (whichAnswer == "Buy")
                    {
                        ExecutePurchase(listing);
                    }
                }
            );
        }

        private static void ExecutePurchase(MarketListing listing)
        {
            if (context == null) return;

            if (Game1.player.Money < listing.PriceGold)
            {
                Game1.drawObjectDialogue($"No tienes suficiente oro. Requiere {listing.PriceGold}g (tienes {Game1.player.Money}g).");
                return;
            }

            Item itemToReceive = ItemRegistry.Create(listing.ItemId, listing.Stack, listing.Quality);
            if (!Game1.player.couldInventoryAcceptThisItem(itemToReceive))
            {
                Game1.drawObjectDialogue("Tu inventario está lleno. Haz espacio antes de comprar.");
                return;
            }

            if (Context.IsMainPlayer)
            {
                HandleBuyListing(Game1.player.UniqueMultiplayerID, listing.ListingId);
            }
            else
            {
                context.Helper.Multiplayer.SendMessage(
                    new BuyListingPayload { ListingId = listing.ListingId },
                    MarketMessages.BuyListingRequest,
                    new[] { context.Manifest.UniqueID },
                    new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );
            }
        }

        private static void PromptSellHeldItem()
        {
            Item? held = Game1.player.CurrentItem;
            if (held == null)
            {
                Game1.drawObjectDialogue("Sostén en tus manos el objeto que deseas poner a la venta.");
                return;
            }

            if (held is Tool && !(held is MeleeWeapon || held is Slingshot))
            {
                Game1.drawObjectDialogue("No puedes vender herramientas esenciales de trabajo.");
                return;
            }

            int suggestedPrice = Math.Max(10, held.salePrice(true) * held.Stack);

            Game1.activeClickableMenu = new NumberSelectionMenu(
                $"Precio en oro para {held.DisplayName} (x{held.Stack}):",
                (number, price, who) =>
                {
                    if (number > 0)
                    {
                        PublishListing(held, number);
                    }
                },
                price: -1,
                minValue: 1,
                maxValue: 999999,
                defaultNumber: suggestedPrice
            );
        }

        private static void PublishListing(Item item, int price)
        {
            if (context == null) return;

            var (valid, error) = CommunityMarketLogic.ValidateNewListing(item.QualifiedItemId, item.Stack, price);
            if (!valid)
            {
                Game1.drawObjectDialogue(error);
                return;
            }

            var payload = new CreateListingPayload
            {
                ItemId = item.QualifiedItemId,
                ItemDisplayName = item.DisplayName,
                Stack = item.Stack,
                Quality = item.Quality,
                PriceGold = price
            };

            // Retirar ítem del inventario del jugador
            Game1.player.removeItemFromInventory(item);

            if (Context.IsMainPlayer)
            {
                HandleCreateListing(Game1.player.UniqueMultiplayerID, Game1.player.Name, payload);
            }
            else
            {
                context.Helper.Multiplayer.SendMessage(
                    payload,
                    MarketMessages.CreateListingRequest,
                    new[] { context.Manifest.UniqueID },
                    new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );
            }

            Game1.addHUDMessage(new HUDMessage($"¡Oferta de {payload.ItemDisplayName} publicada por {price}g!", HUDMessage.newQuest_type));
        }

        private static void ShowManageMyListingsMenu()
        {
            if (context == null || Game1.currentLocation == null) return;

            long myId = Game1.player.UniqueMultiplayerID;
            List<MarketListing> myListings = context.WorldState.MarketListings
                .Where(l => !l.IsSold && l.SellerId == myId)
                .ToList();

            if (myListings.Count == 0)
            {
                Game1.drawObjectDialogue("No tienes ofertas activas en el Mercado Vecinal.");
                return;
            }

            var choices = new List<Response>();
            foreach (MarketListing listing in myListings)
            {
                choices.Add(new Response(listing.ListingId, $"Retirar: {listing.ItemDisplayName} (x{listing.Stack}) [{listing.PriceGold}g]"));
            }
            choices.Add(new Response("Cancel", "Volver"));

            QuestionDialog.Ask(
                "Tus ofertas a la venta en el Mercado:",
                choices.ToArray(),
                (who, whichAnswer) =>
                {
                    if (whichAnswer == "Cancel")
                        return;

                    MarketListing? selected = myListings.FirstOrDefault(l => l.ListingId == whichAnswer);
                    if (selected != null)
                    {
                        CancelListing(selected);
                    }
                }
            );
        }

        private static void CancelListing(MarketListing listing)
        {
            if (context == null) return;

            if (Context.IsMainPlayer)
            {
                HandleCancelListing(Game1.player.UniqueMultiplayerID, listing.ListingId);
            }
            else
            {
                context.Helper.Multiplayer.SendMessage(
                    new CancelListingPayload { ListingId = listing.ListingId },
                    MarketMessages.CancelListingRequest,
                    new[] { context.Manifest.UniqueID },
                    new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );
            }
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || e.FromModID != context.Manifest.UniqueID)
                return;

            if (HandleDeliveryMessage(e))
                return;

            if (Context.IsMainPlayer)
            {
                if (e.Type == MarketMessages.CreateListingRequest)
                {
                    var payload = e.ReadAs<CreateListingPayload>();
                    if (payload != null)
                    {
                        Farmer? seller = Game1.GetPlayer(e.FromPlayerID);
                        string sellerName = seller?.Name ?? "Vecino";
                        HandleCreateListing(e.FromPlayerID, sellerName, payload);
                    }
                }
                else if (e.Type == MarketMessages.BuyListingRequest)
                {
                    var payload = e.ReadAs<BuyListingPayload>();
                    if (payload != null)
                    {
                        HandleBuyListing(e.FromPlayerID, payload.ListingId);
                    }
                }
                else if (e.Type == MarketMessages.CancelListingRequest)
                {
                    var payload = e.ReadAs<CancelListingPayload>();
                    if (payload != null)
                    {
                        HandleCancelListing(e.FromPlayerID, payload.ListingId);
                    }
                }
            }

            if (!Context.IsMainPlayer && Context.IsWorldReady && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID && e.Type == MarketMessages.SyncMarketState)
            {
                var syncedList = e.ReadAs<List<MarketListing>>();
                if (syncedList != null)
                {
                    context.WorldState.MarketListings = syncedList;
                }
            }
        }

        private static void HandleCreateListing(long sellerId, string sellerName, CreateListingPayload payload)
        {
            if (context == null) return;

            var newListing = new MarketListing
            {
                SellerId = sellerId,
                SellerName = sellerName,
                ItemId = payload.ItemId,
                ItemDisplayName = payload.ItemDisplayName,
                Stack = payload.Stack,
                Quality = payload.Quality,
                PriceGold = payload.PriceGold,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                IsSold = false
            };

            context.WorldState.MarketListings.Add(newListing);
            BroadcastMarketSync();
            context.Monitor.Log($"[CommunityMarketModule] Nueva oferta '{newListing.ItemDisplayName}' creada por {sellerName} ({sellerId}) por {newListing.PriceGold}g.", LogLevel.Info);
        }

        private static void HandleBuyListing(long buyerId, string listingId)
        {
            if (context == null) return;

            MarketListing? listing = context.WorldState.MarketListings.FirstOrDefault(l => l.ListingId == listingId);
            if (listing == null || listing.IsSold || HasPendingDelivery(listingId))
                return;

            Farmer? buyer = Game1.GetPlayer(buyerId);
            if (buyer == null || !buyer.isActive()) return;

            var result = CommunityMarketLogic.ValidatePurchase(listing, buyerId, buyer.Money);
            if (result.Status != MarketTransactionStatus.Success)
            {
                context.Monitor.Log($"[CommunityMarketModule] Compra de {listingId} rechazada: {result.Message}", LogLevel.Warn);
                return;
            }

            bool localBuyer = buyerId == Game1.player.UniqueMultiplayerID;
            if (localBuyer && !TryDeliverLocalItem(buyer, ItemRegistry.Create(listing.ItemId, listing.Stack, listing.Quality)))
                return;

            result = CommunityMarketLogic.BuyListing(listing, buyerId, buyer.Name, buyer.Money);
            DailyRolloverModule.SetFarmerMoney(buyer, result.RemainingBuyerGold);
            if (localBuyer)
                CreditMarketSeller(listing, result.SellerProfit);
            else
                QueueDelivery(listing, buyerId, isReturn: false);
            BroadcastMarketSync();
        }

        private static void CreditMarketSeller(MarketListing listing, int sellerProfit)
        {
            if (context == null) return;
            Farmer? seller = Game1.GetPlayer(listing.SellerId);
            if (seller != null && seller.isActive())
            {
                DailyRolloverModule.SetFarmerMoney(seller, seller.Money + sellerProfit);
                if (seller.UniqueMultiplayerID == Game1.player.UniqueMultiplayerID)
                {
                    Game1.addHUDMessage(new HUDMessage($"¡Tu oferta de {listing.ItemDisplayName} se vendió por {sellerProfit}g!", HUDMessage.newQuest_type));
                }
            }
            else
            {
                // Vendedor offline: acumular en PendingMarketGold
                var record = context.WorldState.GetOrCreatePlayer(listing.SellerId, listing.SellerName);
                record.PendingMarketGold += sellerProfit;
                context.Monitor.Log($"[CommunityMarketModule] Vendedor {listing.SellerId} offline: acumulados {sellerProfit}g en PendingMarketGold.", LogLevel.Info);
            }

        }

        private static void HandleCancelListing(long requesterId, string listingId)
        {
            if (context == null) return;

            MarketListing? listing = context.WorldState.MarketListings.FirstOrDefault(l => l.ListingId == listingId);
            if (listing == null || HasPendingDelivery(listingId)) return;

            var result = CommunityMarketLogic.CancelListing(listing, requesterId);
            if (result.Status != MarketTransactionStatus.Success)
                return;

            Farmer? seller = Game1.GetPlayer(requesterId);
            if (seller == null || !seller.isActive())
                return;

            if (requesterId != Game1.player.UniqueMultiplayerID)
            {
                QueueDelivery(listing, requesterId, isReturn: true);
                return;
            }

            Item returnedItem = ItemRegistry.Create(listing.ItemId, listing.Stack, listing.Quality);
            if (!TryDeliverLocalItem(seller, returnedItem))
                return;

            context.WorldState.MarketListings.Remove(listing);
            if (seller.UniqueMultiplayerID == Game1.player.UniqueMultiplayerID)
            {
                Game1.addHUDMessage(new HUDMessage($"Oferta retirada. Se devolvió {listing.ItemDisplayName}.", HUDMessage.newQuest_type));
            }

            BroadcastMarketSync();
        }

        /// <summary>Entrega en el proceso propietario; restaura el inventario si la operación queda incompleta.</summary>
        private static bool TryDeliverLocalItem(Farmer farmer, Item item)
        {
            if (!farmer.IsLocalPlayer)
                throw new InvalidOperationException("La entrega debe ejecutarse en el cliente propietario del granjero.");
            if (!farmer.couldInventoryAcceptThisItem(item))
            {
                context?.Monitor.Log($"[CommunityMarketModule] Entrega rechazada para {farmer.Name}: inventario lleno; la oferta y el oro no cambian.", LogLevel.Warn);
                return false;
            }

            Item?[] previous = farmer.Items.ToArray();
            int[] stacks = previous.Select(i => i?.Stack ?? 0).ToArray();
            try
            {
                if (farmer.addItemToInventory(item) == null)
                    return true;
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[CommunityMarketModule] No se pudo entregar el objeto a {farmer.Name}: {ex}", LogLevel.Error);
            }

            // No cobrar ni cerrar la oferta si una entrega inesperadamente incompleta alteró varias pilas.
            for (int i = 0; i < previous.Length; i++)
            {
                if (previous[i] is Item original)
                    original.Stack = stacks[i];
                farmer.Items[i] = previous[i];
            }
            context?.Monitor.Log($"[CommunityMarketModule] Entrega incompleta a {farmer.Name}; inventario restaurado y oferta conservada.", LogLevel.Error);
            return false;
        }

        private static void CheckPendingMarketGold(Farmer player)
        {
            if (context == null) return;

            if (context.WorldState.Players.TryGetValue(player.UniqueMultiplayerID, out var record) && record.PendingMarketGold > 0)
            {
                int pending = record.PendingMarketGold;
                DailyRolloverModule.SetFarmerMoney(player, player.Money + pending);
                if (player.UniqueMultiplayerID == Game1.player.UniqueMultiplayerID)
                {
                    Game1.addHUDMessage(new HUDMessage($"Has recibido {pending}g por ventas en el Mercado Vecinal mientras estabas fuera.", HUDMessage.newQuest_type));
                }
                record.PendingMarketGold = 0;
                context.Monitor.Log($"[CommunityMarketModule] Cobrados {pending}g pendientes para jugador {player.Name} ({player.UniqueMultiplayerID}).", LogLevel.Info);
            }
        }

        private static void BroadcastMarketSync()
        {
            if (context == null) return;

            context.Helper.Multiplayer.SendMessage(
                context.WorldState.MarketListings,
                MarketMessages.SyncMarketState,
                new[] { context.Manifest.UniqueID }
            );
        }
    }
}
