using System;
using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Core.Economy;
using Pelican4Ever.Framework;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Pelican4Ever.Modules
{
    public sealed partial class CommunityMarketModule
    {
        // SMAPI entrega los mensajes de forma fiable. No repetir ofertas dentro de una conexión:
        // una negativa antigua no debe poder cancelar una entrega posterior ya aceptada.
        private static readonly HashSet<string> sentDeliveries = new();

        private static void RegisterDeliveries(ModContext ctx)
        {
            ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += (_, _) => CompleteObservedDeliveries();
            ctx.Helper.Events.GameLoop.ReturnedToTitle += (_, _) => sentDeliveries.Clear();
            ctx.Helper.Events.Multiplayer.PeerConnected += (_, e) =>
            {
                foreach (PendingMarketDelivery delivery in ctx.WorldState.PendingMarketDeliveries.Values.Where(d => d.PlayerId == e.Peer.PlayerID))
                    sentDeliveries.Remove(delivery.DeliveryId);
            };
            ctx.Helper.Events.Multiplayer.PeerDisconnected += (_, e) =>
            {
                foreach (PendingMarketDelivery delivery in ctx.WorldState.PendingMarketDeliveries.Values.Where(d => d.PlayerId == e.Peer.PlayerID))
                    sentDeliveries.Remove(delivery.DeliveryId);
            };
            ctx.Helper.Events.GameLoop.SaveLoaded += (_, _) =>
            {
                sentDeliveries.Clear();
                if (!Context.IsMainPlayer)
                    ctx.Helper.Multiplayer.SendMessage(new object(), MarketMessages.RequestDeliveries,
                        new[] { ctx.Manifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
            };
        }

        private static bool HasPendingDelivery(string listingId) => context != null
            && context.WorldState.PendingMarketDeliveries.Values.Any(d => d.ListingId == listingId);

        private static void QueueDelivery(MarketListing listing, long playerId, bool isReturn)
        {
            if (context == null) return;
            var delivery = new PendingMarketDelivery
            {
                ListingId = listing.ListingId, PlayerId = playerId,
                ItemId = listing.ItemId, Stack = listing.Stack, Quality = listing.Quality,
                ReservedGold = isReturn ? 0 : listing.PriceGold, IsReturn = isReturn
            };
            context.WorldState.PendingMarketDeliveries.Add(delivery.DeliveryId, delivery);
            SendPendingDeliveries(playerId);
        }

        private static void SendPendingDeliveries(long playerId)
        {
            if (context == null || !Context.IsMainPlayer) return;
            foreach (PendingMarketDelivery delivery in context.WorldState.PendingMarketDeliveries.Values.Where(d => d.PlayerId == playerId))
            {
                if (!sentDeliveries.Add(delivery.DeliveryId)) continue;
                context.Helper.Multiplayer.SendMessage(delivery, MarketMessages.Delivery,
                    new[] { context.Manifest.UniqueID }, new[] { playerId });
            }
        }

        private static bool HandleDeliveryMessage(ModMessageReceivedEventArgs e)
        {
            if (context == null) return false;
            if (e.Type == MarketMessages.RequestDeliveries)
            {
                if (Context.IsMainPlayer && Context.IsWorldReady)
                {
                    // PeerConnected puede llegar antes de SaveLoaded en el cliente: sincronizar ya listo.
                    context.Helper.Multiplayer.SendMessage(context.WorldState.MarketListings, MarketMessages.SyncMarketState,
                        new[] { context.Manifest.UniqueID }, new[] { e.FromPlayerID });
                    SendPendingDeliveries(e.FromPlayerID);
                }
                return true;
            }
            if (e.Type == MarketMessages.Delivery)
            {
                if (!Context.IsMainPlayer && Context.IsWorldReady && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
                    ReceiveDelivery(e.ReadAs<PendingMarketDelivery>());
                return true;
            }
            if (e.Type == MarketMessages.DeliveryAcknowledgement)
            {
                if (Context.IsMainPlayer && Context.IsWorldReady)
                {
                    MarketDeliveryAcknowledgement reply = e.ReadAs<MarketDeliveryAcknowledgement>();
                    if (!reply.Accepted && context.WorldState.PendingMarketDeliveries.TryGetValue(reply.DeliveryId, out PendingMarketDelivery? delivery)
                        && delivery.PlayerId == e.FromPlayerID)
                        RejectDelivery(delivery);
                }
                return true;
            }
            return false;
        }

        private static void ReceiveDelivery(PendingMarketDelivery delivery)
        {
            if (context == null || !Guid.TryParseExact(delivery.DeliveryId, "N", out _)) return;
            string key = MarketDeliveryPolicy.ReceiptKey(delivery.DeliveryId);
            Game1.player.modData.TryGetValue(key, out string? receipt);
            bool received = receipt == "1";
            Item item = ItemRegistry.Create(delivery.ItemId, delivery.Stack, delivery.Quality);
            MarketDeliveryDecision decision = MarketDeliveryPolicy.Decide(delivery.PlayerId, Game1.player.UniqueMultiplayerID,
                received, Game1.player.couldInventoryAcceptThisItem(item), wasRejected: receipt == "0");
            if (decision == MarketDeliveryDecision.WrongRecipient) return;
            bool accepted = decision == MarketDeliveryDecision.AlreadyReceived;
            if (decision == MarketDeliveryDecision.Receive && TryDeliverLocalItem(Game1.player, item))
            {
                // Inventario y recibo pertenecen al mismo NetFarmerRoot y llegan juntos al host.
                Game1.player.modData[key] = "1";
                accepted = true;
            }
            // Una oferta rechazada nunca debe aplicarse después por un mensaje antiguo o un reintento.
            if (!accepted)
                Game1.player.modData[key] = "0";
            context.Helper.Multiplayer.SendMessage(new MarketDeliveryAcknowledgement
            {
                DeliveryId = delivery.DeliveryId, Accepted = accepted
            }, MarketMessages.DeliveryAcknowledgement, new[] { context.Manifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
            if (!accepted)
                Game1.addHUDMessage(new HUDMessage(I18n.Get("market.delivery_inventory_full"), HUDMessage.error_type));
        }

        private static void CompleteObservedDeliveries()
        {
            if (context == null || !Context.IsWorldReady || !Context.IsMainPlayer) return;
            foreach (PendingMarketDelivery delivery in context.WorldState.PendingMarketDeliveries.Values.ToArray())
            {
                Farmer? recipient = Game1.GetPlayer(delivery.PlayerId);
                // Un ACK puede adelantarse al delta de inventario. Esperar al recibo sincronizado evita
                // confirmar una compra cuyo inventario aún no aparecería en el siguiente checkpoint.
                if (recipient == null || !recipient.modData.TryGetValue(MarketDeliveryPolicy.ReceiptKey(delivery.DeliveryId), out string? receipt)) continue;
                if (receipt == "0")
                {
                    RejectDelivery(delivery);
                    continue;
                }
                if (receipt != "1") continue;
                MarketListing? listing = context.WorldState.MarketListings.FirstOrDefault(l => l.ListingId == delivery.ListingId);
                if (listing == null) continue;
                if (delivery.IsReturn)
                    context.WorldState.MarketListings.Remove(listing);
                else
                    CreditMarketSeller(listing, delivery.ReservedGold);
                context.WorldState.PendingMarketDeliveries.Remove(delivery.DeliveryId);
                sentDeliveries.Remove(delivery.DeliveryId);
                context.Monitor.Log($"[CommunityMarketModule] Entrega {delivery.DeliveryId} confirmada para {recipient.Name} (x{delivery.Stack}).", LogLevel.Info);
                BroadcastMarketSync();
            }
        }

        private static void RejectDelivery(PendingMarketDelivery delivery)
        {
            if (context == null) return;
            MarketListing? listing = context.WorldState.MarketListings.FirstOrDefault(l => l.ListingId == delivery.ListingId);
            if (listing == null) return;
            if (!delivery.IsReturn)
            {
                if (!MarketDeliveryPolicy.ReopenUndeliveredPurchase(listing, delivery.PlayerId)) return;
                Farmer? buyer = Game1.GetPlayer(delivery.PlayerId);
                if (buyer != null && buyer.isActive())
                    DailyRolloverModule.SetFarmerMoney(buyer, buyer.Money + delivery.ReservedGold);
                else
                    context.WorldState.GetOrCreatePlayer(delivery.PlayerId, string.Empty).PendingMarketGold += delivery.ReservedGold;
            }
            context.WorldState.PendingMarketDeliveries.Remove(delivery.DeliveryId);
            sentDeliveries.Remove(delivery.DeliveryId);
            context.Monitor.Log($"[CommunityMarketModule] Entrega rechazada para {delivery.PlayerId}: oferta conservada y reserva devuelta.", LogLevel.Warn);
            BroadcastMarketSync();
        }
    }
}
