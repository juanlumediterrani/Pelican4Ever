using System;
using System.Collections.Generic;
using Pelican4Ever.Core.Economy;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;

namespace Pelican4Ever.Modules
{
    /// <summary>Aplica el coste de descenso al jugador local, con los valores del servidor.</summary>
    public sealed class SkullCavernDescentModule : IModule
    {
        private const string Request = "SkullDescentRulesRequest";
        private const string Response = "SkullDescentRules";
        private readonly List<(int From, int To)> pending = new();
        private ModContext context = null!;
        private Rules? rules;

        public string Name => "SkullCavernDescent";
        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;
            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += OnSecond;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += (_, _) => { rules = null; pending.Clear(); };
            ctx.Helper.Events.Player.Warped += OnWarped;
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnMessage;
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            if (Context.IsMainPlayer)
            {
                rules = CurrentRules();
                ApplyPending();
            }
            else
                RequestRules();
        }

        private void OnSecond(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (Context.IsWorldReady && !Context.IsMainPlayer && rules == null)
                RequestRules();
        }

        private void RequestRules()
        {
            if (Game1.MasterPlayer != null)
                context.Helper.Multiplayer.SendMessage(string.Empty, Request, new[] { context.Manifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        }

        private Rules CurrentRules() => new()
        {
            BaseCost = context.Config.SkullCavernDescentBaseCost,
            DepthDivisor = context.Config.SkullCavernDescentDepthDivisor
        };

        private void OnMessage(object? sender, ModMessageReceivedEventArgs e)
        {
            if (!Context.IsWorldReady || e.FromModID != context.Manifest.UniqueID) return;
            if (Context.IsMainPlayer && e.Type == Request && Game1.GetPlayer(e.FromPlayerID) != null)
                context.Helper.Multiplayer.SendMessage(CurrentRules(), Response, new[] { context.Manifest.UniqueID }, new[] { e.FromPlayerID });
            else if (!Context.IsMainPlayer && e.Type == Response && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID && rules == null)
            {
                Rules received = e.ReadAs<Rules>();
                if (received == null || !double.IsFinite(received.BaseCost) || received.BaseCost < 0
                    || !double.IsFinite(received.DepthDivisor) || received.DepthDivisor < 0) return;
                rules = received;
                ApplyPending();
            }
        }

        private void ApplyPending()
        {
            foreach (var transition in pending) Charge(transition.From, transition.To);
            pending.Clear();
        }

        private void OnWarped(object? sender, WarpedEventArgs e)
        {
            if (!e.IsLocalPlayer || e.OldLocation is not MineShaft oldMine || e.NewLocation is not MineShaft newMine
                || oldMine.getMineArea() != MineShaft.desertArea || newMine.getMineArea() != MineShaft.desertArea
                || newMine.mineLevel <= oldMine.mineLevel) return;

            int from = oldMine.mineLevel - 120;
            int to = newMine.mineLevel - 120;
            if (rules == null) pending.Add((from, to));
            else Charge(from, to);
        }

        private void Charge(int from, int to)
        {
            if (rules == null || Game1.player == null) return;
            double cost = SkullCavernDescent.Cost(from, to, rules.BaseCost, rules.DepthDivisor);
            if (cost <= 0) return;
            float previous = Game1.player.Stamina;
            Game1.player.Stamina = (float)Math.Max(0, previous - cost);
            Game1.player.checkForExhaustion(previous);
        }

        public sealed class Rules
        {
            public double BaseCost { get; set; }
            public double DepthDivisor { get; set; }
        }
    }
}
