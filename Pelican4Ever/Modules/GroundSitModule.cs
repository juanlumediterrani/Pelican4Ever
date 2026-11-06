using System;
using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Sentarse en el suelo con una tecla (<c>GroundSitKey</c>). La pose usa fotogramas del sprite del propio juego y se
    /// difunde a los demás jugadores; el host se la reenvía a quien entra después. Sentado en el suelo cuenta como sentado
    /// para el descanso de "Bien comido" (<see cref="EconomyModule"/>, ADR-0006).
    /// </summary>
    public sealed class GroundSitModule : IModule
    {
        public const string MsgGroundSitState = "GroundSitState";

        private static ModContext? context;
        private static SButton sitKey = SButton.OemPeriod;

        // Jugador local sentado en el suelo (null = de pie), con la salud y el lugar al sentarse para detectar golpes y viajes.
        private static GroundSitState? localState;
        private static int healthWhenSat;
        private static string? locationWhenSat;

        // Jugadores remotos sentados. En el host incluye también al propio host, para reenviarlo a quien entra después.
        private static readonly Dictionary<long, GroundSitState> sitters = new();

        /// <summary>True si el jugador local está sentado en el suelo.</summary>
        public static bool IsLocalSittingOnGround => localState != null;

        public string Name => "GroundSit";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            if (Enum.TryParse(ctx.Config.GroundSitKey, ignoreCase: true, out SButton parsed))
                sitKey = parsed;
            else
                ctx.Monitor.Log($"[GroundSit] GroundSitKey '{ctx.Config.GroundSitKey}' no es una tecla válida de SMAPI; se usa {sitKey}.", LogLevel.Warn);

            ctx.Helper.Events.Input.ButtonPressed += OnButtonPressed;
            ctx.Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += (_, _) =>
            {
                localState = null;
                sitters.Clear();
            };
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            ctx.Helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
            ctx.Helper.Events.Multiplayer.PeerDisconnected += (_, e) => sitters.Remove(e.Peer.PlayerID);
        }

        private static void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || context == null || Game1.player == null)
                return;

            // El chat usa la misma tecla al escribir: no sentarse ni levantarse mientras está abierto.
            if (Game1.chatBox?.isActive() == true)
                return;

            if (localState != null)
            {
                if (e.Button == sitKey || e.Button.IsActionButton() || e.Button.IsUseToolButton())
                {
                    StandUp();
                    if (e.Button == sitKey)
                        context.Helper.Input.Suppress(e.Button);
                }
                return;
            }

            if (e.Button != sitKey)
                return;

            if (!GroundSitLogic.CanSit(Context.IsPlayerFree && Game1.player.CanMove, CurrentConditions(Game1.player)))
                return;

            context.Helper.Input.Suppress(e.Button);
            SitDown();
        }

        private static void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || Game1.player == null)
                return;

            Farmer player = Game1.player;
            if (localState != null)
            {
                bool tookDamage = player.health < healthWhenSat;
                bool changedLocation = !string.Equals(player.currentLocation?.NameOrUniqueName, locationWhenSat, StringComparison.Ordinal);
                bool isMoving = player.movementDirections.Count > 0 || player.isMoving();

                if (GroundSitLogic.ShouldStandUp(CurrentConditions(player), isMoving, tookDamage, changedLocation))
                {
                    StandUp();
                }
                else
                {
                    // La salud puede subir sentado (buff del Spa): solo cuenta como golpe bajar de la más alta alcanzada.
                    healthWhenSat = Math.Max(healthWhenSat, player.health);
                    player.showFrame(localState.Frame, localState.Flipped);
                }
            }

            if (sitters.Count == 0)
                return;

            foreach (Farmer farmer in Game1.getOnlineFarmers())
            {
                if (farmer.UniqueMultiplayerID != player.UniqueMultiplayerID
                    && sitters.TryGetValue(farmer.UniqueMultiplayerID, out GroundSitState? state))
                {
                    farmer.showFrame(state.Frame, state.Flipped);
                }
            }
        }

        private static GroundSitConditions CurrentConditions(Farmer player) => new(
            Swimming: player.swimming.Value,
            RidingHorse: player.isRidingHorse(),
            Eating: player.isEating,
            UsingTool: player.UsingTool,
            InFurnitureSeat: player.IsSitting(),
            EventUp: Game1.eventUp);

        private static void SitDown()
        {
            Farmer player = Game1.player;
            player.Halt();
            player.FarmerSprite.StopAnimation();

            localState = GroundSitLogic.SittingState(player.UniqueMultiplayerID, player.FacingDirection);
            healthWhenSat = player.health;
            locationWhenSat = player.currentLocation?.NameOrUniqueName;
            player.showFrame(localState.Frame, localState.Flipped);
            Game1.playSound("pickUpItem");

            Broadcast(localState);
        }

        private static void StandUp()
        {
            if (localState == null)
                return;

            Farmer player = Game1.player;
            localState = null;
            ReleaseSitFrame(player);

            Broadcast(new GroundSitState
            {
                PlayerId = player.UniqueMultiplayerID,
                IsSitting = false,
                Direction = player.FacingDirection
            });
        }

        private static void Broadcast(GroundSitState state)
        {
            if (context == null)
                return;

            if (Context.IsMainPlayer)
                Remember(state);

            if (Context.IsMultiplayer)
                context.Helper.Multiplayer.SendMessage(state, MsgGroundSitState, new[] { context.Manifest.UniqueID });
        }

        private static void Remember(GroundSitState state)
        {
            if (state.IsSitting)
                sitters[state.PlayerId] = state;
            else
                sitters.Remove(state.PlayerId);
        }

        private static void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || e.FromModID != context.Manifest.UniqueID || e.Type != MsgGroundSitState)
                return;

            GroundSitState? state = e.ReadAs<GroundSitState>();
            if (state == null || Game1.player == null || state.PlayerId == Game1.player.UniqueMultiplayerID)
                return;

            // Solo el propio jugador anuncia su estado; el host puede reenviarlo a quien acaba de entrar.
            long hostId = Game1.MasterPlayer?.UniqueMultiplayerID ?? 0;
            if (state.PlayerId != e.FromPlayerID && e.FromPlayerID != hostId)
                return;

            bool wasSitting = sitters.ContainsKey(state.PlayerId);
            Remember(state);

            if (!state.IsSitting && wasSitting
                && Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == state.PlayerId) is { } farmer)
            {
                ReleaseSitFrame(farmer);
            }
        }

        /// <summary>
        /// Suelta la pose fijada con <c>showFrame</c>, que deja <c>PauseForSingleAnimation</c> activo y bloquea la animación de
        /// andar (y hace que <c>StopAnimation</c> no haga nada). Sin pausa, <c>StopAnimation</c> vuelve al fotograma de pie que
        /// corresponda (con o sin objeto en las manos). Si el jugador ya está usando una herramienta, se deja a su animación.
        /// </summary>
        private static void ReleaseSitFrame(Farmer farmer)
        {
            farmer.FarmerSprite.loop = false;
            farmer.FarmerSprite.PauseForSingleAnimation = false;
            if (!farmer.UsingTool)
                farmer.FarmerSprite.StopAnimation();
        }

        private static void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
        {
            if (context == null || !Context.IsMainPlayer || sitters.Count == 0)
                return;

            foreach (GroundSitState state in sitters.Values.ToList())
            {
                if (state.PlayerId == e.Peer.PlayerID)
                    continue;

                context.Helper.Multiplayer.SendMessage(
                    state,
                    MsgGroundSitState,
                    new[] { context.Manifest.UniqueID },
                    new[] { e.Peer.PlayerID });
            }
        }
    }
}
