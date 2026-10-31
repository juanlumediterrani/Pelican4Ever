using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Festivals;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace Pelican4Ever.Modules
{
    public static class FestivalParticipationMessages
    {
        public const string SignUpRequest = "FestivalSignUpRequest";
        public const string SignUpsSync = "FestivalSignUpsSync";
        public const string SignUpReply = "FestivalSignUpReply";
        public const string StartRun = "FestivalStartRun";
    }

    public class FestivalSignUpPayload
    {
        public string FestivalId { get; set; } = string.Empty;
        public bool Join { get; set; }
    }

    public class FestivalSignUpsPayload
    {
        public string FestivalId { get; set; } = string.Empty;
        public List<long> Participants { get; set; } = new();
    }

    public class FestivalReplyPayload
    {
        public string Message { get; set; } = string.Empty;
    }

    public class FestivalRunPayload
    {
        public string FestivalId { get; set; } = string.Empty;
        public int RunId { get; set; }
        public List<long> Participants { get; set; } = new();
    }

    /// <summary>
    /// Evento principal del festival (búsqueda de huevos, baile, pesca en el hielo...) por inscripción. Vanilla lo
    /// arranca para todos y sus puertas <c>waitForOtherPlayers</c> esperan a todos los jugadores conectados, aunque
    /// estén en su granja. Aquí cada jugador se apunta hablando con Lewis, el host confirma el inicio (hablando con
    /// Lewis o con <c>/evento empezar</c>) y solo los apuntados entran, con puertas que esperan únicamente a ellos.
    /// </summary>
    public sealed class FestivalParticipationModule : IModule
    {
        private static ModContext? context;

        /// <summary>Inscripciones; solo se usan en el host.</summary>
        private static readonly FestivalSignUps SignUps = new();

        /// <summary>Apuntados según el host, para mostrarlos en cada cliente.</summary>
        private static List<long> knownParticipants = new();

        /// <summary>Tanda en la que participa el jugador local, o null.</summary>
        private static FestivalRun? activeRun;
        private static readonly HashSet<string> configuredGates = new();

        /// <summary>Solo se permite forceFestivalContinue cuando lo llama este módulo (bloquea el «festivalEvent» vanilla).</summary>
        private static bool startingRun;

        private static readonly System.Reflection.FieldInfo? FestivalHostField = AccessTools.Field(typeof(Event), "festivalHost");

        public string Name => "FestivalParticipation";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;
            ctx.Helper.Events.Input.ButtonPressed += OnButtonPressed;
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            ctx.Helper.Events.GameLoop.DayStarted += (_, _) => ResetState();
            ctx.Helper.Events.GameLoop.ReturnedToTitle += (_, _) => ResetState();
            ctx.Helper.Events.GameLoop.GameLaunched += (_, _) => RegisterChatCommands();
            ApplyHarmonyPatches(ctx.Harmony);
        }

        private static void ResetState()
        {
            SignUps.Reset();
            knownParticipants = new List<long>();
            activeRun = null;
            configuredGates.Clear();
        }

        private void ApplyHarmonyPatches(Harmony harmony)
        {
            try
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(Event), nameof(Event.forceFestivalContinue)),
                    prefix: new HarmonyMethod(typeof(FestivalParticipationModule), nameof(Prefix_ForceFestivalContinue))
                );
                harmony.Patch(
                    original: AccessTools.Method(typeof(Event.DefaultCommands), nameof(Event.DefaultCommands.WaitForOtherPlayers)),
                    prefix: new HarmonyMethod(typeof(FestivalParticipationModule), nameof(Prefix_WaitForOtherPlayers))
                );

                context?.Monitor.Log("[FestivalParticipationModule] Parches del evento principal por inscripción aplicados.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[FestivalParticipationModule] Error al aplicar parches Harmony: {ex}", LogLevel.Error);
            }
        }

        private static string TodaysFestivalId() => $"festival_{Game1.currentSeason}{Game1.dayOfMonth}";

        /// <summary>Festivales con evento principal por participantes. La Feria (fall16) es el juicio de la granja: vanilla.</summary>
        private static bool HasParticipantMainEvent(Event festival)
        {
            return festival.isFestival && !festival.isSpecificFestival("fall16") && festival.TryGetFestivalDataForYear("mainEvent", out _);
        }

        /// <summary>
        /// Bloquea el arranque vanilla del evento principal (respuesta a Lewis, mensaje «festivalEvent» del host,
        /// confirmación de servidor dedicado): arrastraría a todos los que están en el festival, apuntados o no.
        /// </summary>
        public static bool Prefix_ForceFestivalContinue(Event __instance)
        {
            return !Game1.IsMultiplayer || startingRun || !HasParticipantMainEvent(__instance);
        }

        /// <summary>Durante una tanda, cada puerta del guion usa el identificador de la tanda y espera solo a sus participantes.</summary>
        public static void Prefix_WaitForOtherPlayers(Event @event, string[] args)
        {
            if (activeRun == null || !@event.isFestival || args.Length < 2 || args[1].Contains('#'))
                return;

            args[1] = activeRun.GateId(args[1]);
            if (configuredGates.Add(args[1]))
            {
                List<Farmer> participants = activeRun.Participants
                    .Select(id => Game1.GetPlayer(id))
                    .Where(farmer => farmer != null && farmer.isActive())
                    .ToList()!;
                Game1.netReady.SetLocalRequiredFarmers(args[1], participants);
            }
        }

        /// <summary>El evento del festival terminó para el jugador local (salida o final del evento principal).</summary>
        public static void OnFestivalEventEnded()
        {
            activeRun = null;
            configuredGates.Clear();
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (context == null || !Context.IsWorldReady || !Game1.IsMultiplayer || !e.Button.IsActionButton() || Game1.activeClickableMenu != null)
                return;

            if (Game1.CurrentEvent is not { } festival || !festival.playerControlSequence || festival.eventSwitched || !HasParticipantMainEvent(festival))
                return;

            if (FestivalHostField?.GetValue(festival) is not NPC host)
                return;

            Vector2 grab = e.Cursor.GrabTile;
            Point hostTile = host.TilePoint;
            if ((int)grab.X != hostTile.X || ((int)grab.Y != hostTile.Y && (int)grab.Y != hostTile.Y - 1))
                return;

            context.Helper.Input.Suppress(e.Button);
            ShowHostDialog(festival);
        }

        private static void ShowHostDialog(Event festival)
        {
            long me = Game1.player.UniqueMultiplayerID;
            bool signedUp = knownParticipants.Contains(me);
            int count = knownParticipants.Count;

            var choices = new List<Response>
            {
                signedUp
                    ? new Response("Leave", "Borrarme del evento")
                    : new Response("Join", "Apuntarme al evento")
            };
            if (Context.IsMainPlayer)
                choices.Add(new Response("Start", $"Empezar ahora ({count} apuntados)"));
            choices.Add(new Response("Cancel", "Cancelar"));

            QuestionDialog.Ask(
                $"{festival.FestivalName}: el evento principal empieza cuando el anfitrión lo confirme.\nApuntados: {count}.",
                choices.ToArray(),
                (who, answer) =>
                {
                    switch (answer)
                    {
                        case "Join":
                        case "Leave":
                            RequestSignUp(answer == "Join");
                            break;
                        case "Start":
                            StartRunAsHost();
                            break;
                    }
                }
            );
        }

        private static void RequestSignUp(bool join)
        {
            if (context == null) return;

            if (Context.IsMainPlayer)
            {
                HandleSignUp(Game1.player.UniqueMultiplayerID, TodaysFestivalId(), join);
                return;
            }

            context.Helper.Multiplayer.SendMessage(
                new FestivalSignUpPayload { FestivalId = TodaysFestivalId(), Join = join },
                FestivalParticipationMessages.SignUpRequest,
                new[] { context.Manifest.UniqueID },
                new[] { Game1.MasterPlayer.UniqueMultiplayerID }
            );
        }

        /// <summary>Host: registra la inscripción, responde al jugador y avisa al anfitrión.</summary>
        private static void HandleSignUp(long playerId, string festivalId, bool join)
        {
            if (context == null || !Context.IsMainPlayer) return;

            if (festivalId != TodaysFestivalId())
            {
                Reply(playerId, "Ese festival ya no está en curso.");
                return;
            }

            string name = Game1.GetPlayer(playerId)?.Name ?? "Un vecino";
            switch (SignUps.SetSignedUp(festivalId, playerId, join))
            {
                case SignUpResult.Joined:
                    Reply(playerId, "Apuntado. El evento empezará cuando el anfitrión lo confirme; puedes seguir paseando por el festival.");
                    if (playerId != Game1.player.UniqueMultiplayerID)
                        Game1.chatBox?.addInfoMessage($"[Festival] {name} se ha apuntado ({SignUps.Participants.Count}). Habla con Lewis o escribe /evento empezar.");
                    break;
                case SignUpResult.Left:
                    Reply(playerId, "Te has borrado del evento.");
                    break;
                case SignUpResult.AlreadyPlayed:
                    Reply(playerId, "Ya has participado en el evento de este festival hoy.");
                    break;
            }

            BroadcastSignUps();
        }

        private static void Reply(long playerId, string message)
        {
            if (context == null) return;

            if (playerId == Game1.player.UniqueMultiplayerID)
            {
                Game1.addHUDMessage(new HUDMessage(message, HUDMessage.newQuest_type));
                return;
            }

            context.Helper.Multiplayer.SendMessage(
                new FestivalReplyPayload { Message = message },
                FestivalParticipationMessages.SignUpReply,
                new[] { context.Manifest.UniqueID },
                new[] { playerId }
            );
        }

        private static void BroadcastSignUps()
        {
            if (context == null) return;

            var payload = new FestivalSignUpsPayload { FestivalId = SignUps.FestivalId, Participants = SignUps.Participants.ToList() };
            knownParticipants = payload.Participants;
            context.Helper.Multiplayer.SendMessage(payload, FestivalParticipationMessages.SignUpsSync, new[] { context.Manifest.UniqueID });
        }

        /// <summary>Host: cierra la inscripción y arranca la tanda en los clientes de los apuntados.</summary>
        private static void StartRunAsHost()
        {
            if (context == null || !Context.IsMainPlayer) return;

            FestivalRun? run = SignUps.StartRun(TodaysFestivalId());
            if (run == null)
            {
                Game1.chatBox?.addInfoMessage("[Festival] No hay nadie apuntado al evento.");
                return;
            }

            var payload = new FestivalRunPayload { FestivalId = run.FestivalId, RunId = run.RunId, Participants = run.Participants.ToList() };
            context.Helper.Multiplayer.SendMessage(payload, FestivalParticipationMessages.StartRun, new[] { context.Manifest.UniqueID });
            BroadcastSignUps();
            Game1.chatBox?.addInfoMessage($"[Festival] Empieza el evento con {run.Participants.Count} participantes.");

            JoinRunIfParticipant(payload);
        }

        /// <summary>Si el jugador local está en el festival y apuntado a la tanda, empieza su evento principal.</summary>
        private static void JoinRunIfParticipant(FestivalRunPayload payload)
        {
            if (!payload.Participants.Contains(Game1.player.UniqueMultiplayerID))
                return;

            if (Game1.CurrentEvent is not { } festival || festival.id != payload.FestivalId || festival.eventSwitched)
            {
                Game1.addHUDMessage(new HUDMessage("El evento del festival ha empezado, pero no estabas en el recinto.", HUDMessage.error_type));
                return;
            }

            activeRun = new FestivalRun(payload.FestivalId, payload.RunId, payload.Participants);
            configuredGates.Clear();
            startingRun = true;
            try
            {
                festival.forceFestivalContinue();
            }
            finally
            {
                startingRun = false;
            }
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || e.FromModID != context.Manifest.UniqueID)
                return;

            switch (e.Type)
            {
                case FestivalParticipationMessages.SignUpRequest when Context.IsMainPlayer:
                    if (e.ReadAs<FestivalSignUpPayload>() is { } signUp)
                        HandleSignUp(e.FromPlayerID, signUp.FestivalId, signUp.Join);
                    break;
                case FestivalParticipationMessages.SignUpsSync:
                    if (e.ReadAs<FestivalSignUpsPayload>() is { } sync)
                        knownParticipants = sync.Participants;
                    break;
                case FestivalParticipationMessages.SignUpReply:
                    if (e.ReadAs<FestivalReplyPayload>() is { } reply)
                        Game1.addHUDMessage(new HUDMessage(reply.Message, HUDMessage.newQuest_type));
                    break;
                case FestivalParticipationMessages.StartRun:
                    if (e.ReadAs<FestivalRunPayload>() is { } run)
                        JoinRunIfParticipant(run);
                    break;
            }
        }

        private static void RegisterChatCommands()
        {
            try
            {
                if (!ChatCommands.Exists("evento"))
                {
                    ChatCommands.Register(
                        "evento",
                        HandleEventCommand,
                        name => "Evento principal del festival: /evento (apuntados) o /evento empezar (anfitrión)",
                        Array.Empty<string>(),
                        mainOnly: false,
                        multiplayerOnly: true,
                        cheatsOnly: false
                    );
                }
            }
            catch (Exception ex)
            {
                context?.Monitor.Log($"[FestivalParticipationModule] Error registrando /evento: {ex}", LogLevel.Warn);
            }
        }

        private static void HandleEventCommand(string[] command, ChatBox chat)
        {
            string sub = command.Length > 1 ? command[1].ToLowerInvariant() : string.Empty;
            if (sub is "empezar" or "start")
            {
                if (!Context.IsMainPlayer)
                {
                    chat.addInfoMessage("Solo el anfitrión puede empezar el evento.");
                    return;
                }

                StartRunAsHost();
                return;
            }

            string names = knownParticipants.Count == 0
                ? "nadie"
                : string.Join(", ", knownParticipants.Select(id => Game1.GetPlayer(id)?.Name ?? id.ToString()));
            chat.addInfoMessage($"[Festival] Apuntados al evento: {names}.");
        }
    }
}
