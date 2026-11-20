using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Networking;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.Security;
using Pelican4Ever.Framework;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.SaveSerialization;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Módulo de seguridad, lista blanca por IP + código de emparejamiento (pairing) y protección contra usurpación de personajes.
    /// </summary>
    public sealed class ClientSecurityModule : IModule
    {
        private const string PairingSidecarFileName = "pelican4ever_pairing.json";
        private const int MaxRecentAttempts = 25;
        private static readonly ServerChallengeManager serverChallenges = new();
        private sealed class VerifiedConnection
        {
            public string Fingerprint { get; init; } = string.Empty;
            public byte[] PublicKeySpki { get; init; } = Array.Empty<byte>();
        }
        private static readonly ConditionalWeakTable<NetConnection, VerifiedConnection> verifiedConnections = new();

        private sealed class PendingPairingSession
        {
            public long PlayerId { get; init; }
            public string ClientToken { get; set; } = string.Empty;
            public string RemoteIp { get; set; } = string.Empty;
            public DateTimeOffset StartedAtUtc { get; init; }
            public int RemainingAttempts { get; set; } = ClientPairingLogic.MaxPairingAttempts;
            public DateTimeOffset LastChallengeSentUtc { get; set; } = DateTimeOffset.MinValue;
        }

        private static ModContext? context;
        private static readonly Dictionary<long, PendingPairingSession> pendingPairingSessions = new();
        private static readonly Dictionary<long, string> playerRemoteIps = new();
        private static readonly List<RecentConnectionAttempt> recentConnectionAttempts = new();
        private static readonly Dictionary<string, DateTimeOffset> lastBlockedLogByIp = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<UnpairedHandshakeObservation> recentUnpairedObservations = new();

        // Estado en el cliente cuando el servidor exige introducir el código de pairing al entrar por primera vez
        private static bool localClientAwaitingPairing;
        private static string localPairingPrompt = "security.pairing_code_prompt";
        private static int localPairingReopenDelayTicks;

        /// <summary>True en el cliente local mientras tiene pendiente introducir el código de emparejamiento.</summary>
        public static bool IsLocalClientAwaitingPairing => !Context.IsMainPlayer && localClientAwaitingPairing;

        public string Name => "ClientSecurity";

        public ModuleSide Side => ModuleSide.Both;

        /// <summary>Cola de admisión y reservas de reconexión del servidor (TASK-2402).</summary>
        public static AdmissionLogic Admission { get; } = new();

        private sealed record QueuedConnectionCallbacks(
            GameServer GameServer,
            string UserId,
            Action<OutgoingMessage> SendMessage);

        private static readonly Dictionary<string, QueuedConnectionCallbacks> queuedCallbacks = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, long> connectionToPlayerId = new(StringComparer.Ordinal);
        private static readonly Dictionary<long, string> playerToConnectionId = new();
        private static readonly Dictionary<long, DisconnectClassification> pendingPlayerDisconnectClassifications = new();
        private static readonly Dictionary<string, long> pendingUnclaimedOffers = new(StringComparer.Ordinal);

        public static void MarkPlayerDisconnectClassification(long playerId, DisconnectClassification classification)
        {
            if (playerId == 0)
                return;
            pendingPlayerDisconnectClassifications[playerId] = classification;
        }

        public static void MarkAllActiveDisconnectClassification(DisconnectClassification classification)
        {
            foreach (long playerId in playerToConnectionId.Keys)
            {
                pendingPlayerDisconnectClassifications[playerId] = classification;
            }
        }

        public void Register(ModContext ctx)
        {
            context = ctx;

            // Inicializar o verificar identidad del cliente local
            ClientIdentity.Initialize(ctx.Helper);
            if (!string.IsNullOrEmpty(ClientIdentity.DeviceFingerprint))
            {
                string shortFp = ClientIdentity.DeviceFingerprint.Length >= 12
                    ? ClientIdentity.DeviceFingerprint[..12]
                    : ClientIdentity.DeviceFingerprint;
                ctx.Monitor.Log($"[ClientSecurity] Huella ECDSA del dispositivo: {shortFp} (completa en data/device_key.json).", LogLevel.Info);
            }
            Admission.Configure(
                ctx.Config.MaxConcurrentPlayers,
                ctx.Config.AdmissionQueueCapacity,
                ctx.Config.ReconnectGraceMinutes);

            // Eventos de ciclo de vida y multijugador
            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            ctx.Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            ctx.Helper.Events.Specialized.UnvalidatedUpdateTicked += OnUnvalidatedUpdateTicked;
            ctx.Helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
            ctx.Helper.Events.Multiplayer.PeerDisconnected += OnPeerDisconnected;
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;

            // Comandos de consola de administración en el host
            ctx.AddCommand("p4e_claim_list", "Lista los peones registrados y su estado de posesión/token.", OnConsoleClaimList);
            ctx.AddCommand("p4e_claim_reset", "Libera la posesión de un peón para que pueda ser reclamado de nuevo: p4e_claim_reset <nombre_o_id>", OnConsoleClaimReset);
            ctx.AddCommand("p4e_pair_list", "Lista los dispositivos emparejados, invitaciones activas (3 días) e intentos recientes bloqueados.", OnConsolePairList);
            ctx.AddCommand("p4e_pair_allow", "Prepara un alta por IP y genera un código de pairing válido 3 días: p4e_pair_allow <usuario> <ip>", OnConsolePairAllow);
            ctx.AddCommand("p4e_pair_approve", "Aprueba una solicitud de alta pendiente de un dispositivo: p4e_pair_approve <huella_o_alias> [peon_id]", OnConsolePairApprove);
            ctx.AddCommand("p4e_migrate_player", "Migra explícitamente un peón existente al nuevo dispositivo con respaldo verificable: p4e_migrate_player <huella_o_alias> <peon_o_nombre>", OnConsoleMigratePlayer);
            ctx.AddCommand("p4e_pair_reject", "Rechaza o cancela una solicitud de alta pendiente: p4e_pair_reject <huella_o_alias>", OnConsolePairReject);
            ctx.AddCommand("p4e_pair_revoke", "Revoca una invitación pendiente o un dispositivo emparejado: p4e_pair_revoke <usuario_o_ip_o_id>", OnConsolePairRevoke);
            ctx.AddCommand("p4e_probation_status", "Lista solicitudes Putre, dispositivos en observación (máx. 2 cupos) y lista negra.", OnConsoleProbationStatus);
            ctx.AddCommand("p4e_probation_ticket", "Abre un ticket de admisión Putre temporal (30 min): p4e_probation_ticket <discord_id> <alias>", OnConsoleProbationTicket);
            ctx.AddCommand("p4e_probation_approve", "Aprueba una huella observada para un ticket Putre: p4e_probation_approve <discord_id> <huella>", OnConsoleProbationApprove);
            ctx.AddCommand("p4e_probation_graduate", "Gradúa un dispositivo en observación y libera el cupo de Putre: p4e_probation_graduate <huella_o_alias_o_discord>", OnConsoleProbationGraduate);
            ctx.AddCommand("p4e_probation_cancel", "Cancela un ticket Putre pendiente y libera el cupo: p4e_probation_cancel <discord_id_o_alias>", OnConsoleProbationCancel);
            ctx.AddCommand("p4e_probation_blacklist", "Añade permanentemente a lista negra, revoca acceso y expulsa: p4e_probation_blacklist <objetivo> <motivo...>", OnConsoleProbationBlacklist);
            ctx.AddCommand("p4e_blacklist_remove", "Retira una entrada de la lista negra (solo propietario): p4e_blacklist_remove <huella_o_alias_o_discord>", OnConsoleBlacklistRemove);

            ApplyHarmonyPatches(ctx.Harmony);
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            serverChallenges.Clear();
            pendingPairingSessions.Clear();
            playerRemoteIps.Clear();
            recentUnpairedObservations.Clear();
            queuedCallbacks.Clear();
            connectionToPlayerId.Clear();
            playerToConnectionId.Clear();
            pendingPlayerDisconnectClassifications.Clear();
            pendingUnclaimedOffers.Clear();
            Admission.ClearAll();
            localClientAwaitingPairing = false;
            localPairingReopenDelayTicks = 0;
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            localClientAwaitingPairing = false;
            localPairingReopenDelayTicks = 0;

            if (!Context.IsMainPlayer || context == null)
                return;

            pendingPairingSessions.Clear();
            playerRemoteIps.Clear();
            recentUnpairedObservations.Clear();
            queuedCallbacks.Clear();
            connectionToPlayerId.Clear();
            playerToConnectionId.Clear();
            pendingPlayerDisconnectClassifications.Clear();
            pendingUnclaimedOffers.Clear();
            Admission.ClearAll();
            Admission.Configure(
                context.Config.MaxConcurrentPlayers,
                context.Config.AdmissionQueueCapacity,
                context.Config.ReconnectGraceMinutes);

            LoadAndMergePairingSidecar();
            ClientPairingLogic.EnsureExistingClaimsAuthorized(context.WorldState, DateTimeOffset.UtcNow);
            ClientPairingLogic.PurgeExpiredInvites(context.WorldState, DateTimeOffset.UtcNow);
            SyncServerTokensFromWorldState();
            SavePairingSidecar();
        }

        private static string? GetPairingSidecarPath()
        {
            string? savePath = Constants.CurrentSavePath;
            return string.IsNullOrWhiteSpace(savePath) ? null : Path.Combine(savePath, PairingSidecarFileName);
        }

        private static void LoadAndMergePairingSidecar()
        {
            if (context == null || !Context.IsMainPlayer)
                return;

            string? path = GetPairingSidecarPath();
            if (string.IsNullOrEmpty(path))
                return;

            if (!SidecarPersistence.LoadAndMerge(path, context.WorldState, out string? error) && !string.IsNullOrEmpty(error))
            {
                context.Monitor.Log($"[ClientSecurity] {error}", LogLevel.Warn);
            }
        }

        public static void SavePairingSidecar()
        {
            if (context == null || !Context.IsMainPlayer)
                return;

            string? path = GetPairingSidecarPath();
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                SidecarPersistence.SaveAtomic(path, context.WorldState);
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[ClientSecurity] No se pudo guardar {PairingSidecarFileName}: {ex.Message}", LogLevel.Warn);
            }
        }

        /// <summary>Indica si un peón conectado en el host tiene aún pendiente completar el código de emparejamiento.</summary>
        public static bool IsPlayerPendingPairing(long playerId)
        {
            if (context == null || !Context.IsMainPlayer || !context.Config.RequirePairingWhitelist)
                return false;

            if (pendingPairingSessions.ContainsKey(playerId))
                return true;

            if (context.WorldState.Players.TryGetValue(playerId, out PlayerRecord? rec) && !string.IsNullOrWhiteSpace(rec.OwnerToken))
            {
                return !ClientPairingLogic.IsClientTokenAuthorized(context.WorldState, rec.OwnerToken, out _);
            }

            return false;
        }

        private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null || e.Peer.IsHost)
                return;

            if (pendingPairingSessions.ContainsKey(e.Peer.PlayerID))
            {
                SendPairingChallengeToPlayer(e.Peer.PlayerID);
            }
        }

        private void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null || e.Peer.IsHost)
                return;

            long playerId = e.Peer.PlayerID;
            playerRemoteIps.Remove(playerId);

            if (pendingPairingSessions.Remove(playerId))
            {
                CleanupUnpairedFarmhand(playerId);
                context.Monitor.Log($"[ClientSecurity] El jugador {playerId} se desconectó sin completar el emparejamiento; ranura liberada.", LogLevel.Info);
            }
        }

        private void OnUnvalidatedUpdateTicked(object? sender, UnvalidatedUpdateTickedEventArgs e)
        {
            if (context == null || Context.IsWorldReady)
                return;

            // Mientras espera en FarmhandMenu (cola de admisión en gameMode=0 o aprobación en gameMode=6),
            // Stardew pausa TitleMenu si pierde el foco (Game1._update retorna al tener pauseWhenOutOfFocus && multiplayerMode == 0)
            // y SMAPI detiene UpdateTicked durante gameMode=6. Bombear FarmhandMenu aquí garantiza recepción continua en segundo plano.
            if (Game1.game1 != null
                && !Game1.game1.IsActiveNoOverlay
                && Program.releaseBuild
                && Game1.multiplayerMode == 0
                && (Game1.options == null || Game1.options.pauseWhenOutOfFocus))
            {
                FarmhandMenu? fhMenu = Game1.activeClickableMenu as FarmhandMenu
                    ?? (Game1.activeClickableMenu is TitleMenu ? TitleMenu.subMenu as FarmhandMenu : null);
                if (fhMenu?.client != null && fhMenu.client.connectionStarted)
                {
                    fhMenu.update(Game1.currentGameTime ?? new GameTime());
                }
            }
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (context == null || !Context.IsWorldReady)
                return;

            // Cliente: mantener abierto el diálogo de código de pairing mientras esté pendiente
            if (!Context.IsMainPlayer && localClientAwaitingPairing)
            {
                if (localPairingReopenDelayTicks > 0)
                {
                    localPairingReopenDelayTicks--;
                    return;
                }

                if (!Game1.eventUp && Game1.activeClickableMenu is not NamingMenu)
                {
                    OpenPairingDialogOnClient(localPairingPrompt);
                }
                return;
            }

            // Host: supervisar sesiones de emparejamiento pendientes (reenviar reto y aplicar timeout de 5 min)
            if (Context.IsMainPlayer && pendingPairingSessions.Count > 0 && e.IsMultipleOf(60))
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                foreach (var session in pendingPairingSessions.Values.ToList())
                {
                    if ((now - session.StartedAtUtc).TotalMinutes >= ClientPairingLogic.PairingSessionTimeoutMinutes)
                    {
                        pendingPairingSessions.Remove(session.PlayerId);
                        CleanupUnpairedFarmhand(session.PlayerId);
                        context.Monitor.Log($"[ClientSecurity] Tiempo de emparejamiento agotado para el jugador {session.PlayerId} ({session.RemoteIp}); expulsando.", LogLevel.Warn);
                        Game1.server?.kick(session.PlayerId);
                        continue;
                    }

                    if ((now - session.LastChallengeSentUtc).TotalSeconds >= 4
                        && Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == session.PlayerId))
                    {
                        SendPairingChallengeToPlayer(session.PlayerId);
                    }
                }
            }

            // Host: supervisar cola de admisión y reservas de reconexión cada ~1 segundo (TASK-2402)
            if (Context.IsMainPlayer && e.IsMultipleOf(60))
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                bool removedDead = false;
                foreach (var kv in queuedCallbacks.ToList())
                {
                    if (!kv.Value.GameServer.isConnectionActive(kv.Key))
                    {
                        queuedCallbacks.Remove(kv.Key);
                        Admission.OnDisconnected(kv.Key, DisconnectClassification.VoluntaryExit, now);
                        removedDead = true;
                    }
                }

                var promoted = Admission.PromoteEligible(now);
                if (promoted.Count > 0 || removedDead)
                {
                    ProcessPromotedAndNotifyQueue(promoted);
                }
            }
        }

        public static void SendPairingChallengeToPlayer(long playerId)
        {
            if (context == null || !Context.IsMainPlayer)
                return;

            if (!pendingPairingSessions.TryGetValue(playerId, out PendingPairingSession? session))
                return;

            session.LastChallengeSentUtc = DateTimeOffset.UtcNow;
            var invite = ClientPairingLogic.FindActiveInviteForIp(context.WorldState, session.RemoteIp, DateTimeOffset.UtcNow);
            var payload = new PairingChallengePayload
            {
                Alias = invite?.Alias ?? string.Empty,
                RemainingAttempts = session.RemainingAttempts,
                PromptMessage = string.IsNullOrWhiteSpace(invite?.Alias)
                    ? "security.pairing_code_prompt"
                    : invite.Alias
            };

            context.Helper.Multiplayer.SendMessage(
                payload,
                ModMessages.PairingChallenge,
                new[] { context.Manifest.UniqueID },
                new[] { playerId }
            );
        }

        private static void OpenPairingDialogOnClient(string title)
        {
            if (Context.IsMainPlayer || context == null)
                return;

            Game1.activeClickableMenu = new NamingMenu(
                b: code =>
                {
                    string clean = (code ?? string.Empty).Trim().ToUpperInvariant();
                    if (string.IsNullOrEmpty(clean))
                        return;

                    localPairingReopenDelayTicks = 90; // esperar respuesta del host (~1.5s) antes de reabrir
                    Game1.exitActiveMenu();
                    Game1.addHUDMessage(new HUDMessage(I18n.Get("security.verifying_code"), HUDMessage.newQuest_type));

                    context.Helper.Multiplayer.SendMessage(
                        new PairingSubmitPayload
                        {
                            Code = clean,
                            ClientToken = ClientIdentity.CurrentToken
                        },
                        ModMessages.PairingSubmit,
                        new[] { context.Manifest.UniqueID },
                        Game1.MasterPlayer != null ? new[] { Game1.MasterPlayer.UniqueMultiplayerID } : null
                    );
                },
                title: I18n.Get(title),
                defaultName: string.Empty)
            {
                minLength = 1
            };
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (context == null || e.FromModID != context.Manifest.UniqueID)
                return;

            if (!Context.IsMainPlayer && e.Type == ModMessages.PairingChallenge)
            {
                var payload = e.ReadAs<PairingChallengePayload>();
                if (payload != null)
                {
                    localClientAwaitingPairing = true;
                    localPairingPrompt = string.IsNullOrWhiteSpace(payload.Alias)
                        ? I18n.Get("security.pairing_code_prompt")
                        : I18n.Get("security.pairing_code_prompt_alias", new { alias = payload.Alias });
                    if (Context.IsWorldReady && !Game1.eventUp && Game1.activeClickableMenu is not NamingMenu)
                    {
                        OpenPairingDialogOnClient(localPairingPrompt);
                    }
                }
            }
            else if (!Context.IsMainPlayer && e.Type == ModMessages.PairingResult)
            {
                var payload = e.ReadAs<PairingResultPayload>();
                if (payload == null)
                    return;

                if (payload.Ok)
                {
                    localClientAwaitingPairing = false;
                    localPairingReopenDelayTicks = 0;
                    if (Game1.activeClickableMenu is NamingMenu)
                        Game1.exitActiveMenu();
                    Game1.playSound("reward");
                    Game1.addHUDMessage(new HUDMessage(I18n.Get(payload.Message), HUDMessage.achievement_type));
                }
                else
                {
                    Game1.playSound("cancel");
                    Game1.addHUDMessage(new HUDMessage(I18n.Get(payload.Message), HUDMessage.error_type));
                    if (payload.RemainingAttempts > 0)
                    {
                        localClientAwaitingPairing = true;
                        localPairingPrompt = I18n.Get(payload.Message);
                        localPairingReopenDelayTicks = 0;
                        if (Context.IsWorldReady && !Game1.eventUp)
                            OpenPairingDialogOnClient(localPairingPrompt);
                    }
                }
            }
            else if (Context.IsMainPlayer && e.Type == ModMessages.PairingSubmit)
            {
                var payload = e.ReadAs<PairingSubmitPayload>();
                if (payload != null)
                {
                    HandlePairingSubmitOnHost(e.FromPlayerID, payload);
                }
            }
        }

        private static void HandlePairingSubmitOnHost(long playerId, PairingSubmitPayload payload)
        {
            if (context == null || !Context.IsMainPlayer)
                return;

            pendingPairingSessions.TryGetValue(playerId, out PendingPairingSession? session);
            string remoteIp = session?.RemoteIp ?? ResolveRemoteIpByPlayerId(playerId);
            string clientToken = !string.IsNullOrWhiteSpace(payload.ClientToken)
                ? payload.ClientToken.Trim()
                : (session?.ClientToken ?? string.Empty);
            string playerName = Game1.GetPlayer(playerId)?.Name ?? string.Empty;

            var result = ClientPairingLogic.TryRedeemInvite(
                context.WorldState,
                playerId,
                playerName,
                clientToken,
                remoteIp,
                payload.Code,
                DateTimeOffset.UtcNow,
                out AuthorizedClient? authorized,
                out string message);

            int today = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;
            int time = Context.IsWorldReady ? Game1.timeOfDay : 600;

            if (result == PairingRedeemResult.Success && authorized != null)
            {
                pendingPairingSessions.Remove(playerId);

                if (Game1.netWorldState?.Value?.farmhandData != null
                    && Game1.netWorldState.Value.farmhandData.TryGetValue(playerId, out Farmer? serverFarmer)
                    && serverFarmer != null)
                {
                    serverFarmer.userID.Value = authorized.ClientToken;
                }

                SavePairingSidecar();
                PersistenceModule.FlushToSaveData(context, "ClientPaired");

                string logMsg = $"[ClientSecurity] Jugador {playerId} ('{playerName}') completó el emparejamiento como '{authorized.Alias}' desde IP {authorized.PairedIp}.";
                context.Monitor.Log(logMsg, LogLevel.Info);
                AdminApiModule.EventLog.Append(
                    HostEventTypes.PlayerPaired,
                    today,
                    time,
                    $"Jugador '{playerName}' emparejado como '{authorized.Alias}' ({authorized.PairedIp}).",
                    new Dictionary<string, object?>
                    {
                        ["playerId"] = playerId,
                        ["playerName"] = playerName,
                        ["alias"] = authorized.Alias,
                        ["ip"] = authorized.PairedIp
                    });

                context.Helper.Multiplayer.SendMessage(
                    new PairingResultPayload
                    {
                        Ok = true,
                        RemainingAttempts = ClientPairingLogic.MaxPairingAttempts,
                        Message = message
                    },
                    ModMessages.PairingResult,
                    new[] { context.Manifest.UniqueID },
                    new[] { playerId }
                );

                FarmSlotModule.SendDoorsSyncToPlayer(playerId);
                return;
            }

            int remaining = (session != null ? --session.RemainingAttempts : 0);
            if (remaining > 0)
            {
                context.Monitor.Log($"[ClientSecurity] Código de pairing incorrecto de {playerId} ({remoteIp}). Intentos restantes: {remaining}.", LogLevel.Warn);
                context.Helper.Multiplayer.SendMessage(
                    new PairingResultPayload
                    {
                        Ok = false,
                        RemainingAttempts = remaining,
                        Message = I18n.Get("security.pairing_incorrect_remaining", new { message = I18n.Get(message), remaining })
                    },
                    ModMessages.PairingResult,
                    new[] { context.Manifest.UniqueID },
                    new[] { playerId }
                );
            }
            else
            {
                pendingPairingSessions.Remove(playerId);
                CleanupUnpairedFarmhand(playerId);
                context.Monitor.Log($"[ClientSecurity] Jugador {playerId} ({remoteIp}) agotó sus intentos de emparejamiento; expulsando.", LogLevel.Warn);
                AdminApiModule.EventLog.Append(
                    HostEventTypes.PlayerRejected,
                    today,
                    time,
                    $"Jugador {playerId} ({remoteIp}) expulsado: código de pairing inválido.",
                    new Dictionary<string, object?> { ["playerId"] = playerId, ["ip"] = remoteIp, ["reason"] = "código de pairing inválido" });

                context.Helper.Multiplayer.SendMessage(
                    new PairingResultPayload
                    {
                        Ok = false,
                        RemainingAttempts = 0,
                        Message = "security.pairing_exhausted"
                    },
                    ModMessages.PairingResult,
                    new[] { context.Manifest.UniqueID },
                    new[] { playerId }
                );

                var gameLoop = context.Helper.Events.GameLoop;
                gameLoop.UpdateTicked += KickNextTick;
                void KickNextTick(object? s, UpdateTickedEventArgs args)
                {
                    gameLoop.UpdateTicked -= KickNextTick;
                    Game1.server?.kick(playerId);
                }
            }
        }

        private static void CleanupUnpairedFarmhand(long playerId)
        {
            if (context == null || !Context.IsMainPlayer)
                return;

            if (context.WorldState.Players.TryGetValue(playerId, out PlayerRecord? rec))
            {
                if (!ClientPairingLogic.IsClientTokenAuthorized(context.WorldState, rec.OwnerToken, out _))
                {
                    PlayerAuthLogic.ResetClaim(rec);
                    if (string.IsNullOrEmpty(rec.FarmSlotId) && context.WorldState.FindResidenceOf(playerId) == null)
                    {
                        context.WorldState.Players.Remove(playerId);
                    }
                }
            }

            if (Game1.netWorldState?.Value?.farmhandData != null
                && Game1.netWorldState.Value.farmhandData.TryGetValue(playerId, out Farmer? f)
                && f != null
                && context.WorldState.FindResidenceOf(playerId) == null)
            {
                f.userID.Value = string.Empty;
                f.Name = string.Empty;
                f.isCustomized.Value = false;
            }
        }

        /// <summary>
        /// Sincroniza serverFarmer.userID.Value con el identificador público higienizado del dispositivo.
        /// NUNCA serializa claves privadas, tokens legados secretos ni códigos en la red nativa (TASK-1805).
        /// </summary>
        public static void SyncServerTokensFromWorldState()
        {
            if (context == null || !Context.IsMainPlayer || Game1.netWorldState?.Value?.farmhandData == null)
                return;

            foreach (var (playerId, playerRecord) in context.WorldState.Players)
            {
                if (Game1.netWorldState.Value.farmhandData.TryGetValue(playerId, out Farmer? f) && f != null)
                {
                    f.userID.Value = FarmhandAuthLogic.GetSanitizedFarmhandUserId(context.WorldState, playerId, playerRecord);
                }
            }
        }

        private void OnConsoleClaimList(string command, string[] args)
        {
            if (context == null) return;

            if (!Context.IsMainPlayer)
            {
                context.Monitor.Log("Este comando solo puede ejecutarse en el Host.", LogLevel.Warn);
                return;
            }

            var players = context.WorldState.Players.Values.OrderBy(p => p.UniqueMultiplayerID).ToList();
            context.Monitor.Log($"=== Posesión de peones ({players.Count} registrados) ===", LogLevel.Info);
            foreach (var p in players)
            {
                bool isClaimed = PlayerAuthLogic.IsClaimed(p);
                string claimText = isClaimed
                    ? $"Reclamado [Dispositivo: {PlayerAuthLogic.MaskToken(p.OwnerToken)}]"
                    : "Libre / Sin reclamar";

                context.Monitor.Log($"  [{p.UniqueMultiplayerID}] {p.PlayerName} (Granja: {p.FarmSlotId ?? "ninguna"}): {claimText}", LogLevel.Info);
            }
        }

        private void OnConsoleClaimReset(string command, string[] args)
        {
            if (context == null) return;

            if (!Context.IsMainPlayer)
            {
                context.Monitor.Log("Este comando solo puede ejecutarse en el Host.", LogLevel.Warn);
                return;
            }

            if (args.Length == 0)
            {
                context.Monitor.Log("Uso: p4e_claim_reset <nombre_o_id>", LogLevel.Warn);
                return;
            }

            string target = string.Join(" ", args).Trim();
            PlayerRecord? found = null;

            if (long.TryParse(target, out long targetId))
            {
                context.WorldState.Players.TryGetValue(targetId, out found);
            }

            if (found == null)
            {
                found = context.WorldState.Players.Values.FirstOrDefault(p =>
                    string.Equals(p.PlayerName, target, StringComparison.OrdinalIgnoreCase));
            }

            if (found == null)
            {
                context.Monitor.Log($"No se encontró ningún jugador registrado con '{target}'.", LogLevel.Error);
                return;
            }

            if (!string.IsNullOrWhiteSpace(found.OwnerToken))
            {
                context.WorldState.AuthorizedClients?.Remove(found.OwnerToken.Trim());
            }

            PlayerAuthLogic.ResetClaim(found);

            // También limpiar el NetString en farmhandData si existe
            if (Game1.netWorldState?.Value?.farmhandData != null
                && Game1.netWorldState.Value.farmhandData.TryGetValue(found.UniqueMultiplayerID, out Farmer? f)
                && f != null)
            {
                f.userID.Value = string.Empty;
            }

            SavePairingSidecar();
            PersistenceModule.FlushToSaveData(context, "ClaimReset");
            context.Monitor.Log($"Posesión del peón {found.UniqueMultiplayerID} ({found.PlayerName}) restablecida. Ahora está libre para ser reclamado.", LogLevel.Info);
        }

        private void OnConsolePairList(string command, string[] args)
        {
            if (context == null) return;

            if (!Context.IsMainPlayer)
            {
                context.Monitor.Log("Este comando solo puede ejecutarse en el Host.", LogLevel.Warn);
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            ClientPairingLogic.PurgeExpiredInvites(context.WorldState, now);
            ClientPairingLogic.EnsureExistingClaimsAuthorized(context.WorldState, now);

            string mode = context.Config.RequirePairingWhitelist ? "ACTIVA" : "DESACTIVADA";
            context.Monitor.Log($"=== Lista Blanca y Emparejamiento ({mode}, validez invitaciones: {context.Config.PairingInviteTtlHours}h) ===", LogLevel.Info);

            var devices = context.WorldState.AuthorizedDevices.Values.OrderBy(d => d.Alias, StringComparer.OrdinalIgnoreCase).ToList();
            if (devices.Count > 0)
            {
                context.Monitor.Log($"Dispositivos autorizados (ECDSA P-256) ({devices.Count}):", LogLevel.Info);
                foreach (var d in devices)
                {
                    string ipInfo = string.IsNullOrWhiteSpace(d.LastSeenIp) ? "IP no registrada" : $"IP {d.LastSeenIp}";
                    string shortFp = d.Fingerprint.Length >= 12 ? d.Fingerprint[..12] : d.Fingerprint;
                    context.Monitor.Log($"  - {d.Alias} [Peón: {d.BoundPlayerId}, {ipInfo}, Estado: {d.Status}, Huella: {shortFp}...]", LogLevel.Info);
                }
            }

            var clients = context.WorldState.AuthorizedClients.Values.OrderBy(c => c.Alias, StringComparer.OrdinalIgnoreCase).ToList();
            context.Monitor.Log($"Dispositivos emparejados legacy ({clients.Count}):", LogLevel.Info);
            if (clients.Count == 0)
            {
                context.Monitor.Log("  (ninguno)", LogLevel.Info);
            }
            else
            {
                foreach (var c in clients)
                {
                    string ipInfo = string.IsNullOrWhiteSpace(c.LastSeenIp) ? "IP no registrada" : $"IP {c.LastSeenIp}";
                    context.Monitor.Log($"  - {c.Alias} [Peón: {c.BoundPlayerId}, {ipInfo}, ID: {PlayerAuthLogic.MaskToken(c.ClientToken)}]", LogLevel.Info);
                }
            }

            var invites = context.WorldState.PairingInvites.OrderBy(i => i.ExpiresAtUtc).ToList();
            context.Monitor.Log($"Invitaciones pendientes ({invites.Count}):", LogLevel.Info);
            if (invites.Count == 0)
            {
                context.Monitor.Log("  (ninguna)", LogLevel.Info);
            }
            else
            {
                foreach (var inv in invites)
                {
                    double hoursLeft = Math.Max(0, (inv.ExpiresAtUtc - now).TotalHours);
                    context.Monitor.Log(
                        $"  - Usuario: {inv.Alias} | IP: {inv.AllowedIp} | Código de pairing: {inv.Code} | Caduca en {hoursLeft:F1}h ({inv.ExpiresAtUtc:yyyy-MM-dd HH:mm} UTC)",
                        LogLevel.Info);
                }
            }

            var pending = context.WorldState.PendingEnrollments?.Values.OrderBy(e => e.RequestedAtUtc).ToList();
            context.Monitor.Log($"Solicitudes de alta pendientes ({pending?.Count ?? 0}):", LogLevel.Info);
            if (pending == null || pending.Count == 0)
            {
                context.Monitor.Log("  (ninguna)", LogLevel.Info);
            }
            else
            {
                foreach (var req in pending)
                {
                    context.Monitor.Log(
                        $"  - Usuario: {req.Alias} | Huella: {req.GetShortFingerprint()}... | IP: {req.RemoteIp} | Código: {req.InviteCode} | Intentos: {req.Attempts} | Caduca: {req.ExpiresAtUtc:yyyy-MM-dd HH:mm} UTC",
                        LogLevel.Info);
                }
            }

            var pendingFps = new HashSet<string>(
                pending?.Select(p => p.Fingerprint) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            var recentObs = GetRecentUnpairedObservationsSnapshot(now)
                .Where(o => !pendingFps.Contains(o.Fingerprint) && !ServerDeviceRegistry.IsDeviceAuthorized(context.WorldState, o.Fingerprint, out _))
                .OrderByDescending(o => o.ObservedAtUtc)
                .ToList();
            context.Monitor.Log($"Conexiones ECDSA recientes sin autorizar (últimos 15 min) ({recentObs.Count}):", LogLevel.Info);
            if (recentObs.Count == 0)
            {
                context.Monitor.Log("  (ninguna)", LogLevel.Info);
            }
            else
            {
                foreach (var obs in recentObs)
                {
                    string shortFp = obs.Fingerprint.Length >= 12 ? obs.Fingerprint[..12] : obs.Fingerprint;
                    context.Monitor.Log(
                        $"  - Huella: {shortFp}... | IP: {obs.RemoteIp} | Visto: {obs.ObservedAtUtc:HH:mm:ss} UTC",
                        LogLevel.Info);
                }
            }

            if (recentConnectionAttempts.Count > 0)
            {
                context.Monitor.Log($"Intentos recientes bloqueados ({recentConnectionAttempts.Count}):", LogLevel.Info);
                foreach (var att in recentConnectionAttempts.TakeLast(10))
                {
                    context.Monitor.Log($"  - IP {att.RemoteIp} ({att.TimestampUtc:HH:mm:ss} UTC): {att.Outcome}", LogLevel.Info);
                }
            }
        }

        private void OnConsolePairAllow(string command, string[] args)
        {
            if (context == null) return;

            if (!Context.IsMainPlayer)
            {
                context.Monitor.Log("Este comando solo puede ejecutarse en el Host.", LogLevel.Warn);
                return;
            }

            if (args.Length < 2)
            {
                context.Monitor.Log("Uso: p4e_pair_allow <usuario> <ip>", LogLevel.Warn);
                return;
            }

            string alias = args[0].Trim();
            string rawIp = args[1].Trim();

            if (!ClientPairingLogic.IsValidInviteIp(rawIp, out string normalizedIp, out string? ipError))
            {
                context.Monitor.Log($"Error en p4e_pair_allow: {ipError}", LogLevel.Error);
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            TimeSpan ttl = TimeSpan.FromHours(Math.Max(1, context.Config.PairingInviteTtlHours));
            PairingInvite invite = ClientPairingLogic.CreateInvite(context.WorldState, alias, normalizedIp, ttl, now);

            SavePairingSidecar();
            PersistenceModule.FlushToSaveData(context, "PairAllow");

            int today = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;
            int time = Context.IsWorldReady ? Game1.timeOfDay : 600;
            AdminApiModule.EventLog.Append(
                HostEventTypes.PlayerPairingInvited,
                today,
                time,
                $"Invitación de emparejamiento creada para '{invite.Alias}' ({invite.AllowedIp}).",
                new Dictionary<string, object?>
                {
                    ["alias"] = invite.Alias,
                    ["ip"] = invite.AllowedIp,
                    ["expiresAtUtc"] = invite.ExpiresAtUtc.ToString("O")
                });

            context.Monitor.Log(
                $"Alta preparada para '{invite.Alias}' (IP {invite.AllowedIp}). Código de pairing: {invite.Code} (válido {context.Config.PairingInviteTtlHours}h, hasta {invite.ExpiresAtUtc:yyyy-MM-dd HH:mm} UTC).",
                LogLevel.Info);
        }

        private static void FinishPairApprove(AuthorizedDevice? device, string message, string flushTag)
        {
            if (context == null)
                return;

            SavePairingSidecar();
            PersistenceModule.FlushToSaveData(context, flushTag);

            int today = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;
            int time = Context.IsWorldReady ? Game1.timeOfDay : 600;
            AdminApiModule.EventLog.Append(
                HostEventTypes.PlayerPaired,
                today,
                time,
                message,
                new Dictionary<string, object?>
                {
                    ["alias"] = device?.Alias,
                    ["fingerprint"] = device?.Fingerprint,
                    ["boundPlayerId"] = device?.BoundPlayerId
                });

            context.Monitor.Log(message, LogLevel.Info);
        }

        private void OnConsolePairApprove(string command, string[] args)
        {
            if (context == null) return;

            if (!Context.IsMainPlayer)
            {
                context.Monitor.Log("Este comando solo puede ejecutarse en el Host.", LogLevel.Warn);
                return;
            }

            if (args.Length == 0)
            {
                context.Monitor.Log("Uso: p4e_pair_approve <huella_o_alias> [peon_id_o_nombre]", LogLevel.Warn);
                return;
            }

            string target = args[0].Trim();
            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (args.Length > 1)
            {
                string secondArg = args[1].Trim();
                bool targetPlayerExists = long.TryParse(secondArg, out long parsedPlayerId)
                    ? context.WorldState.Players.ContainsKey(parsedPlayerId)
                    : context.WorldState.Players.Values.Any(p =>
                        p != null && string.Equals(p.PlayerName, secondArg, StringComparison.OrdinalIgnoreCase));

                if (targetPlayerExists)
                {
                    // Si se especifica peón existente, crear respaldo verificable y migrar explícitamente
                    string? sidecarPath = GetPairingSidecarPath();
                    if (!string.IsNullOrEmpty(sidecarPath))
                    {
                        if (DeviceMigrationLogic.CreateVerifiableBackup(sidecarPath, context.WorldState, now, out VerifiableBackupResult? backup, out string? backupErr))
                        {
                            context.Monitor.Log($"[ClientSecurity] Copia de seguridad verificada: {Path.GetFileName(backup?.BackupPath)} (SHA-256: {backup?.Sha256Checksum[..12]}...).", LogLevel.Info);
                        }
                        else
                        {
                            context.Monitor.Log($"[ClientSecurity] Error creando respaldo verificable previo a migración: {backupErr}", LogLevel.Warn);
                        }
                    }

                    if (!DeviceMigrationLogic.MigratePlayer(context.WorldState, target, secondArg, now, out AuthorizedDevice? dev, out PlayerRecord? rec, out string migMsg))
                    {
                        context.Monitor.Log(migMsg, LogLevel.Warn);
                        return;
                    }

                    SyncServerTokensFromWorldState();
                    FinishPairApprove(dev, migMsg, "PairApproveMigrate");
                    return;
                }

                var pendingMatch = DeviceEnrollmentLogic.FindPendingEnrollment(context.WorldState, target);
                if (pendingMatch != null && string.Equals(pendingMatch.Alias, secondArg, StringComparison.OrdinalIgnoreCase))
                {
                    if (!DeviceEnrollmentLogic.ApproveEnrollment(context.WorldState, target, 0, now, out AuthorizedDevice? pendingDev, out string pendingMsg))
                    {
                        context.Monitor.Log(pendingMsg, LogLevel.Warn);
                        return;
                    }
                    FinishPairApprove(pendingDev, pendingMsg, "PairApprove");
                    return;
                }

                var observations = GetRecentUnpairedObservationsSnapshot(now);
                if (DeviceEnrollmentLogic.ApproveObservedDevice(context.WorldState, target, secondArg, observations, now, out AuthorizedDevice? obsDev, out string obsMsg))
                {
                    FinishPairApprove(obsDev, obsMsg, "PairApproveObserved");
                    return;
                }

                context.Monitor.Log(
                    $"No existe ningún peón llamado '{secondArg}' para migrar y no pudo aprobarse '{target}': {obsMsg}",
                    LogLevel.Warn);
                return;
            }

            if (!DeviceEnrollmentLogic.ApproveEnrollment(context.WorldState, target, 0, now, out AuthorizedDevice? device, out string message))
            {
                var observations = GetRecentUnpairedObservationsSnapshot(now);
                if (!DeviceEnrollmentLogic.ApproveObservedDevice(context.WorldState, target, null, observations, now, out device, out message))
                {
                    context.Monitor.Log(message, LogLevel.Warn);
                    return;
                }
            }

            FinishPairApprove(device, message, "PairApprove");
        }

        private void OnConsoleMigratePlayer(string command, string[] args)
        {
            if (context == null) return;

            if (!Context.IsMainPlayer)
            {
                context.Monitor.Log("Este comando solo puede ejecutarse en el Host.", LogLevel.Warn);
                return;
            }

            if (args.Length < 2)
            {
                context.Monitor.Log("Uso: p4e_migrate_player <huella_o_alias> <peon_o_nombre>", LogLevel.Warn);
                return;
            }

            string targetDevice = args[0].Trim();
            string targetPlayer = args[1].Trim();
            string? sidecarPath = GetPairingSidecarPath();
            if (string.IsNullOrEmpty(sidecarPath))
            {
                context.Monitor.Log("[ClientSecurity] No hay una partida activa cargada con ruta en disco para migración.", LogLevel.Warn);
                return;
            }

            // 1. Respaldo verificable con suma SHA-256 antes de cualquier cambio
            if (!DeviceMigrationLogic.CreateVerifiableBackup(sidecarPath, context.WorldState, DateTimeOffset.UtcNow, out VerifiableBackupResult? backup, out string? backupError))
            {
                context.Monitor.Log($"[ClientSecurity] Error crítico al crear la copia de seguridad verificable previa a la migración: {backupError}", LogLevel.Error);
                return;
            }

            context.Monitor.Log($"[ClientSecurity] Copia de seguridad verificada: {Path.GetFileName(backup?.BackupPath)} (SHA-256: {backup?.Sha256Checksum[..12]}...).", LogLevel.Info);

            // 2. Ejecutar la migración atómica y purgar credenciales legadas
            if (!DeviceMigrationLogic.MigratePlayer(context.WorldState, targetDevice, targetPlayer, DateTimeOffset.UtcNow, out AuthorizedDevice? dev, out PlayerRecord? rec, out string message))
            {
                context.Monitor.Log(message, LogLevel.Warn);
                return;
            }

            SavePairingSidecar();
            PersistenceModule.FlushToSaveData(context, "PlayerMigrated");
            SyncServerTokensFromWorldState();

            int today = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;
            int time = Context.IsWorldReady ? Game1.timeOfDay : 600;
            AdminApiModule.EventLog.Append(
                HostEventTypes.PlayerPaired,
                today,
                time,
                message,
                new Dictionary<string, object?>
                {
                    ["alias"] = dev?.Alias,
                    ["fingerprint"] = dev?.Fingerprint,
                    ["boundPlayerId"] = dev?.BoundPlayerId,
                    ["backup"] = backup?.BackupPath
                });

            context.Monitor.Log(message, LogLevel.Info);
        }

        private void OnConsolePairReject(string command, string[] args)
        {
            if (context == null) return;

            if (!Context.IsMainPlayer)
            {
                context.Monitor.Log("Este comando solo puede ejecutarse en el Host.", LogLevel.Warn);
                return;
            }

            if (args.Length == 0)
            {
                context.Monitor.Log("Uso: p4e_pair_reject <huella_o_alias>", LogLevel.Warn);
                return;
            }

            string target = args[0].Trim();
            if (!DeviceEnrollmentLogic.RejectOrCancelEnrollment(context.WorldState, target, out string message))
            {
                context.Monitor.Log(message, LogLevel.Warn);
                return;
            }

            SavePairingSidecar();
            PersistenceModule.FlushToSaveData(context, "PairReject");
            context.Monitor.Log(message, LogLevel.Info);
        }

        private void OnConsolePairRevoke(string command, string[] args)
        {
            if (context == null) return;

            if (!Context.IsMainPlayer)
            {
                context.Monitor.Log("Este comando solo puede ejecutarse en el Host.", LogLevel.Warn);
                return;
            }

            if (args.Length == 0)
            {
                context.Monitor.Log("Uso: p4e_pair_revoke <usuario_o_ip_o_id>", LogLevel.Warn);
                return;
            }

            string target = string.Join(" ", args).Trim();
            if (!ClientPairingLogic.Revoke(context.WorldState, target, out List<long> affectedPlayerIds, out string summary))
            {
                context.Monitor.Log(summary, LogLevel.Warn);
                return;
            }

            foreach (long playerId in affectedPlayerIds)
            {
                pendingPairingSessions.Remove(playerId);
                if (Game1.netWorldState?.Value?.farmhandData != null
                    && Game1.netWorldState.Value.farmhandData.TryGetValue(playerId, out Farmer? f)
                    && f != null)
                {
                    f.userID.Value = string.Empty;
                }

                if (Game1.getOnlineFarmers().Any(p => p.UniqueMultiplayerID == playerId && p.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID))
                {
                    Game1.server?.kick(playerId);
                }
            }

            SavePairingSidecar();
            PersistenceModule.FlushToSaveData(context, "PairRevoke");

            int today = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;
            int time = Context.IsWorldReady ? Game1.timeOfDay : 600;
            AdminApiModule.EventLog.Append(
                HostEventTypes.PlayerPairingRevoked,
                today,
                time,
                summary,
                new Dictionary<string, object?> { ["target"] = target, ["affectedPlayers"] = affectedPlayerIds.Count });

            context.Monitor.Log(summary, LogLevel.Info);
        }

        private static void FailCommand(string message)
        {
            context?.Monitor.Log(message, LogLevel.Warn);
            if (RemoteCommandOutput.IsCapturing)
                throw new InvalidOperationException(message);
        }

        private static void RecordUnpairedObservation(string fingerprint, byte[] pubKeySpki, string remoteIp, DateTimeOffset nowUtc)
        {
            recentUnpairedObservations.RemoveAll(o =>
                o == null ||
                (nowUtc - o.ObservedAtUtc) > ProbationAndBlacklistLogic.UnpairedObservationTtl ||
                string.Equals(o.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));

            recentUnpairedObservations.Add(new UnpairedHandshakeObservation
            {
                Fingerprint = fingerprint,
                PublicKeyBase64 = Convert.ToBase64String(pubKeySpki),
                RemoteIp = remoteIp,
                ObservedAtUtc = nowUtc
            });

            while (recentUnpairedObservations.Count > ProbationAndBlacklistLogic.MaxRecentUnpairedObservations)
                recentUnpairedObservations.RemoveAt(0);
        }

        private static List<UnpairedHandshakeObservation> GetRecentUnpairedObservationsSnapshot(DateTimeOffset nowUtc)
        {
            recentUnpairedObservations.RemoveAll(o =>
                o == null || (nowUtc - o.ObservedAtUtc) > ProbationAndBlacklistLogic.UnpairedObservationTtl);
            return recentUnpairedObservations.ToList();
        }

        private static void EvictBlacklistedPlayers(string? fingerprint, IEnumerable<long> affectedPlayerIds)
        {
            HashSet<long> targetIds = new(affectedPlayerIds ?? Array.Empty<long>());

            if (Game1.netWorldState?.Value?.farmhandData != null)
            {
                var fhData = Game1.netWorldState.Value.farmhandData;
                foreach (long fhId in fhData.Keys.ToList())
                {
                    if (!fhData.TryGetValue(fhId, out Farmer? fh) || fh == null)
                        continue;

                    bool matchesId = targetIds.Contains(fhId);
                    bool matchesFp = !string.IsNullOrEmpty(fingerprint) &&
                        string.Equals(fh.userID.Value, fingerprint, StringComparison.OrdinalIgnoreCase);

                    if (matchesId || matchesFp)
                    {
                        fh.userID.Value = string.Empty;
                        targetIds.Add(fhId);
                    }
                }
            }

            foreach (long playerId in targetIds)
                pendingPairingSessions.Remove(playerId);

            foreach (Farmer online in Game1.getOnlineFarmers())
            {
                if (online.UniqueMultiplayerID == Game1.player?.UniqueMultiplayerID)
                    continue;

                bool matchesOnline = targetIds.Contains(online.UniqueMultiplayerID) ||
                    (!string.IsNullOrEmpty(fingerprint) &&
                     string.Equals(online.userID.Value, fingerprint, StringComparison.OrdinalIgnoreCase));

                if (matchesOnline)
                    Game1.server?.kick(online.UniqueMultiplayerID);
            }
        }

        private void OnConsoleProbationStatus(string command, string[] args)
        {
            if (context == null || !Context.IsMainPlayer)
            {
                FailCommand("Este comando solo está disponible en el servidor anfitrión.");
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            ProbationAndBlacklistLogic.PurgeExpiredTickets(context.WorldState, now);
            int used = ProbationAndBlacklistLogic.CountPutreActiveSlots(context.WorldState, now);
            int max = ProbationAndBlacklistLogic.MaxConcurrentPutreSlots;

            context.Monitor.Log($"Estado Putre — Cupos en uso: {used}/{max} (libres: {Math.Max(0, max - used)}).", LogLevel.Info);

            var tickets = context.WorldState.PutreTickets ?? new List<PutreAdmissionTicket>();
            context.Monitor.Log($"Tickets pendientes ({tickets.Count}):", LogLevel.Info);
            foreach (var t in tickets)
            {
                context.Monitor.Log($"  - {t.Alias} (Discord {t.DiscordUserId}), expira {t.ExpiresAtUtc:yyyy-MM-ddTHH:mm:ssZ}", LogLevel.Info);
            }

            var observing = (context.WorldState.AuthorizedDevices?.Values ?? Enumerable.Empty<AuthorizedDevice>())
                .Where(d => d != null && string.Equals(d.InvitedBy, "putre", StringComparison.OrdinalIgnoreCase) && d.ProbationStage == ProbationStage.Observing)
                .OrderBy(d => d.CreatedAtUtc)
                .ToList();
            context.Monitor.Log($"Dispositivos en observación ({observing.Count}):", LogLevel.Info);
            foreach (var d in observing)
            {
                string shortFp = d.Fingerprint.Length >= 12 ? d.Fingerprint[..12] : d.Fingerprint;
                context.Monitor.Log($"  - {d.Alias} (huella {shortFp}, Discord {d.DiscordUserId}, peón {d.BoundPlayerId}, alta {d.CreatedAtUtc:yyyy-MM-ddTHH:mm:ssZ})", LogLevel.Info);
            }

            var bl = context.WorldState.Blacklist ?? new List<BlacklistEntry>();
            context.Monitor.Log($"Lista negra ({bl.Count}):", LogLevel.Info);
            foreach (var b in bl)
            {
                string shortFp = b.Fingerprint.Length >= 12 ? b.Fingerprint[..12] : b.Fingerprint;
                context.Monitor.Log($"  - {b.Alias} (huella {shortFp}, Discord {b.DiscordUserId}, por {b.BlacklistedBy}): {b.Reason}", LogLevel.Info);
            }
        }

        private void OnConsoleProbationTicket(string command, string[] args)
        {
            if (context == null || !Context.IsMainPlayer)
            {
                FailCommand("Este comando solo está disponible en el servidor anfitrión.");
                return;
            }

            if (args.Length < 2)
            {
                FailCommand("Uso: p4e_probation_ticket <discord_id> <alias>");
                return;
            }

            if (!ProbationAndBlacklistLogic.OpenTicket(
                    context.WorldState,
                    args[0],
                    args[1],
                    DateTimeOffset.UtcNow,
                    out _,
                    out string message))
            {
                FailCommand(message);
                return;
            }

            SavePairingSidecar();
            PersistenceModule.FlushToSaveData(context, "ProbationTicket");
            context.Monitor.Log(message, LogLevel.Info);
        }

        private void OnConsoleProbationApprove(string command, string[] args)
        {
            if (context == null || !Context.IsMainPlayer)
            {
                FailCommand("Este comando solo está disponible en el servidor anfitrión.");
                return;
            }

            if (args.Length < 2)
            {
                FailCommand("Uso: p4e_probation_approve <discord_id> <huella>");
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            var observations = GetRecentUnpairedObservationsSnapshot(now);
            if (!ProbationAndBlacklistLogic.ApproveTicketDevice(
                    context.WorldState,
                    args[0],
                    args[1],
                    observations,
                    now,
                    out AuthorizedDevice? device,
                    out string message))
            {
                FailCommand(message);
                return;
            }

            SavePairingSidecar();
            PersistenceModule.FlushToSaveData(context, "ProbationApprove");

            int today = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;
            int time = Context.IsWorldReady ? Game1.timeOfDay : 600;
            AdminApiModule.EventLog.Append(
                HostEventTypes.PlayerPaired,
                today,
                time,
                message,
                new Dictionary<string, object?>
                {
                    ["alias"] = device?.Alias,
                    ["fingerprint"] = device?.Fingerprint,
                    ["discordUserId"] = device?.DiscordUserId,
                    ["invitedBy"] = device?.InvitedBy,
                    ["probationStage"] = device?.ProbationStage.ToString()
                });

            context.Monitor.Log(message, LogLevel.Info);
        }

        private void OnConsoleProbationGraduate(string command, string[] args)
        {
            if (context == null || !Context.IsMainPlayer)
            {
                FailCommand("Este comando solo está disponible en el servidor anfitrión.");
                return;
            }

            if (args.Length < 1)
            {
                FailCommand("Uso: p4e_probation_graduate <huella_o_alias_o_discord>");
                return;
            }

            string target = string.Join(" ", args).Trim();
            if (!ProbationAndBlacklistLogic.GraduateDevice(
                    context.WorldState,
                    target,
                    DateTimeOffset.UtcNow,
                    out AuthorizedDevice? device,
                    out string message))
            {
                FailCommand(message);
                return;
            }

            SavePairingSidecar();
            PersistenceModule.FlushToSaveData(context, "ProbationGraduate");

            int today = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;
            int time = Context.IsWorldReady ? Game1.timeOfDay : 600;
            AdminApiModule.EventLog.Append(
                HostEventTypes.PlayerProbationGraduated,
                today,
                time,
                message,
                new Dictionary<string, object?>
                {
                    ["alias"] = device?.Alias,
                    ["fingerprint"] = device?.Fingerprint,
                    ["discordUserId"] = device?.DiscordUserId,
                    ["boundPlayerId"] = device?.BoundPlayerId
                });

            context.Monitor.Log(message, LogLevel.Info);
        }

        private void OnConsoleProbationCancel(string command, string[] args)
        {
            if (context == null || !Context.IsMainPlayer)
            {
                FailCommand("Este comando solo está disponible en el servidor anfitrión.");
                return;
            }

            if (args.Length < 1)
            {
                FailCommand("Uso: p4e_probation_cancel <discord_id_o_alias>");
                return;
            }

            string target = string.Join(" ", args).Trim();
            if (!ProbationAndBlacklistLogic.CancelTicket(context.WorldState, target, out string message))
            {
                FailCommand(message);
                return;
            }

            SavePairingSidecar();
            PersistenceModule.FlushToSaveData(context, "ProbationCancel");
            context.Monitor.Log(message, LogLevel.Info);
        }

        private void OnConsoleProbationBlacklist(string command, string[] args)
        {
            if (context == null || !Context.IsMainPlayer)
            {
                FailCommand("Este comando solo está disponible en el servidor anfitrión.");
                return;
            }

            if (args.Length < 2)
            {
                FailCommand("Uso: p4e_probation_blacklist <objetivo> <motivo...>");
                return;
            }

            string target = args[0];
            string reason = string.Join(" ", args.Skip(1)).Trim();
            if (!ProbationAndBlacklistLogic.AddToBlacklist(
                    context.WorldState,
                    target,
                    reason,
                    "putre",
                    DateTimeOffset.UtcNow,
                    out BlacklistEntry? entry,
                    out List<long> affectedIds,
                    out string message))
            {
                FailCommand(message);
                return;
            }

            EvictBlacklistedPlayers(entry?.Fingerprint, affectedIds);
            SavePairingSidecar();
            PersistenceModule.FlushToSaveData(context, "ProbationBlacklist");

            int today = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;
            int time = Context.IsWorldReady ? Game1.timeOfDay : 600;
            AdminApiModule.EventLog.Append(
                HostEventTypes.PlayerBlacklisted,
                today,
                time,
                message,
                new Dictionary<string, object?>
                {
                    ["alias"] = entry?.Alias,
                    ["fingerprint"] = entry?.Fingerprint,
                    ["discordUserId"] = entry?.DiscordUserId,
                    ["reason"] = entry?.Reason,
                    ["blacklistedBy"] = entry?.BlacklistedBy,
                    ["affectedPlayers"] = affectedIds.Count
                });

            context.Monitor.Log(message, LogLevel.Info);
        }

        private void OnConsoleBlacklistRemove(string command, string[] args)
        {
            if (context == null || !Context.IsMainPlayer)
            {
                FailCommand("Este comando solo está disponible en el servidor anfitrión.");
                return;
            }

            if (args.Length < 1)
            {
                FailCommand("Uso: p4e_blacklist_remove <huella_o_alias_o_discord>");
                return;
            }

            string target = string.Join(" ", args).Trim();
            if (!ProbationAndBlacklistLogic.RemoveFromBlacklist(
                    context.WorldState,
                    target,
                    out BlacklistEntry? removed,
                    out string message))
            {
                FailCommand(message);
                return;
            }

            SavePairingSidecar();
            PersistenceModule.FlushToSaveData(context, "BlacklistRemove");

            int today = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;
            int time = Context.IsWorldReady ? Game1.timeOfDay : 600;
            AdminApiModule.EventLog.Append(
                HostEventTypes.PlayerBlacklistRemoved,
                today,
                time,
                message,
                new Dictionary<string, object?>
                {
                    ["alias"] = removed?.Alias,
                    ["fingerprint"] = removed?.Fingerprint,
                    ["discordUserId"] = removed?.DiscordUserId
                });

            context.Monitor.Log(message, LogLevel.Info);
        }

        // ─────────────────────────── Resolución de IP e Inspección Lidgren ───────────────────────────

        private static string ResolveRemoteIpByConnectionId(string? connectionId)
        {
            if (string.IsNullOrWhiteSpace(connectionId) || Game1.server is not GameServer gs)
                return string.Empty;

            try
            {
                var serversField = AccessTools.Field(typeof(GameServer), "servers");
                if (serversField?.GetValue(gs) is not IEnumerable servers)
                    return string.Empty;

                foreach (object? srv in servers)
                {
                    if (srv is not LidgrenServer ls)
                        continue;

                    var netServer = AccessTools.Field(typeof(LidgrenServer), "server")?.GetValue(ls);
                    if (netServer == null)
                        continue;

                    var connsProp = AccessTools.Property(netServer.GetType(), "Connections");
                    if (connsProp?.GetValue(netServer) is not IEnumerable conns)
                        continue;

                    var getConnIdMethod = AccessTools.Method(typeof(LidgrenServer), "getConnectionId");
                    foreach (object? conn in conns)
                    {
                        if (conn == null)
                            continue;

                        string? id = getConnIdMethod?.Invoke(ls, new[] { conn }) as string;
                        if (string.Equals(id, connectionId, StringComparison.Ordinal))
                        {
                            var ep = AccessTools.Property(conn.GetType(), "RemoteEndPoint")?.GetValue(conn) as IPEndPoint;
                            if (ep != null)
                                return ClientPairingLogic.NormalizeIp(ep.Address.ToString());
                        }
                    }
                }
            }
            catch
            {
                // Ignorar fallos de reflexión en red
            }

            return string.Empty;
        }

        private static bool TryVerifyConnectionHail(
            string connectionId,
            string remoteIp,
            out string fingerprint,
            out byte[] pubKeySpki)
        {
            fingerprint = string.Empty;
            pubKeySpki = Array.Empty<byte>();
            if (context == null || string.IsNullOrWhiteSpace(connectionId) || Game1.server is not GameServer gs)
                return false;

            try
            {
                if (AccessTools.Field(typeof(GameServer), "servers")?.GetValue(gs) is not IEnumerable servers)
                    return false;

                foreach (object? server in servers)
                {
                    if (server is not LidgrenServer lidgren || lidgren.server == null)
                        continue;

                    foreach (NetConnection connection in lidgren.server.Connections)
                    {
                        if (!string.Equals(lidgren.getConnectionId(connection), connectionId, StringComparison.Ordinal))
                            continue;

                        if (verifiedConnections.TryGetValue(connection, out VerifiedConnection? verified))
                        {
                            fingerprint = verified.Fingerprint;
                            pubKeySpki = verified.PublicKeySpki;
                            return true;
                        }

                        NetIncomingMessage? hail = connection.RemoteHailMessage;
                        if (hail == null)
                            return false;

                        if (!DeviceAuthWire.TryParseHailPayload(
                                hail.Data,
                                out byte[]? nonce,
                                out _,
                                out byte[]? parsedPubKey,
                                out byte[]? sig) || nonce == null || parsedPubKey == null || sig == null)
                        {
                            return false;
                        }

                        if (!serverChallenges.TryVerifyAndConsume(
                                remoteIp,
                                nonce,
                                parsedPubKey,
                                sig,
                                DateTimeOffset.UtcNow,
                                out string verifiedFp,
                                out string failureReason))
                        {
                            context.Monitor.Log($"[ClientSecurity] Verificación de conexión fallida desde {remoteIp}: {failureReason}", LogLevel.Trace);
                            return false;
                        }

                        fingerprint = verifiedFp;
                        pubKeySpki = parsedPubKey;
                        verifiedConnections.Add(connection, new VerifiedConnection { Fingerprint = fingerprint, PublicKeySpki = pubKeySpki });
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[ClientSecurity] No se pudo verificar el desafío de conexión: {ex.GetType().Name}.", LogLevel.Warn);
            }

            return false;
        }

        private static bool HasValidEarlyProof(string connectionId, string remoteIp, out string verifiedToken) =>
            HasValidEarlyProof(connectionId, remoteIp, out verifiedToken, out _);

        private static bool HasValidEarlyProof(string connectionId, string remoteIp, out string verifiedToken, out bool alreadyHandled)
        {
            verifiedToken = string.Empty;
            alreadyHandled = false;
            if (context == null)
                return false;

            try
            {
                if (!TryVerifyConnectionHail(connectionId, remoteIp, out string fingerprint, out byte[] pubKeySpki))
                    return false;

                string fpShortLog = fingerprint.Length >= 12 ? fingerprint[..12] : fingerprint;
                if (ProbationAndBlacklistLogic.IsBlacklisted(context.WorldState, fingerprint, null, out BlacklistEntry? blEntry))
                {
                    context.Monitor.Log($"[ClientSecurity] Dispositivo bloqueado por lista negra ({fpShortLog}...) desde IP {remoteIp}: {blEntry?.Reason}", LogLevel.Warn);
                    alreadyHandled = true;
                    RecordConnectionAttempt(remoteIp, fingerprint, $"Bloqueado (lista negra: {fpShortLog})", logWarn: false);
                    DisconnectConnectionById(connectionId, "Access permanently denied (blacklisted). Only the server owner can revoke it.");
                    return false;
                }

                // Comprobar si la huella digital está autorizada en la lista blanca del servidor
                bool isAuthorized = ServerDeviceRegistry.IsDeviceAuthorized(context.WorldState, fingerprint, out AuthorizedDevice? matchedDevice);

                isAuthorized |= context.WorldState.AuthorizedClients != null &&
                    context.WorldState.AuthorizedClients.Values.Any(c =>
                        string.Equals(c.ClientToken, fingerprint, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(c.Alias, fingerprint, StringComparison.OrdinalIgnoreCase));

                isAuthorized |= context.WorldState.Players.Values.Any(p =>
                    string.Equals(p.OwnerToken, fingerprint, StringComparison.OrdinalIgnoreCase));

                if (!isAuthorized)
                {
                    DateTimeOffset obsNow = DateTimeOffset.UtcNow;
                    RecordUnpairedObservation(fingerprint, pubKeySpki, remoteIp, obsNow);

                    context.WorldState.PendingEnrollments ??= new Dictionary<string, DeviceEnrollmentRequest>(StringComparer.OrdinalIgnoreCase);
                    if (context.WorldState.PendingEnrollments.TryGetValue(fingerprint, out var existingEnroll))
                    {
                        existingEnroll.Attempts++;
                        existingEnroll.RemoteIp = ClientPairingLogic.NormalizeIp(remoteIp);
                        SavePairingSidecar();
                        string existingShort = fingerprint.Length >= 12 ? fingerprint[..12] : fingerprint;
                        context.Monitor.Log($"[ClientSecurity] Solicitud de alta pendiente reiterada para '{existingEnroll.Alias}' (huella: {existingShort}...) desde IP {remoteIp}.", LogLevel.Info);
                        alreadyHandled = true;
                        RecordConnectionAttempt(remoteIp, fingerprint, $"Solicitud pendiente: {existingEnroll.Alias} ({existingShort})", logWarn: false);
                        DisconnectConnectionById(connectionId, $"Dispositivo registrado para aprobación. Huella: {existingShort}. Solicita al propietario que apruebe tu acceso en Discord.");
                        return false;
                    }

                    var activeInvite = ClientPairingLogic.FindUnclaimedInviteForIp(context.WorldState, remoteIp, obsNow, fingerprint);
                    if (activeInvite != null)
                    {
                        var enroll = new DeviceEnrollmentRequest
                        {
                            Fingerprint = fingerprint,
                            PublicKeyBase64 = Convert.ToBase64String(pubKeySpki),
                            Alias = activeInvite.Alias,
                            RemoteIp = ClientPairingLogic.NormalizeIp(remoteIp),
                            InviteCode = activeInvite.Code,
                            RequestedAtUtc = obsNow,
                            ExpiresAtUtc = activeInvite.ExpiresAtUtc,
                            Attempts = 1
                        };
                        context.WorldState.PendingEnrollments[fingerprint] = enroll;

                        SavePairingSidecar();
                        string shortFp = fingerprint.Length >= 12 ? fingerprint[..12] : fingerprint;
                        context.Monitor.Log($"[ClientSecurity] Solicitud de alta registrada para '{activeInvite.Alias}' (huella: {shortFp}...) desde IP {remoteIp}.", LogLevel.Info);
                        alreadyHandled = true;
                        RecordConnectionAttempt(remoteIp, fingerprint, $"Solicitud pendiente: {activeInvite.Alias} ({shortFp})", logWarn: false);
                        DisconnectConnectionById(connectionId, $"Dispositivo registrado para aprobación. Huella: {shortFp}. Solicita al propietario que apruebe tu acceso en Discord.");
                        return false;
                    }

                    string fpShort = fingerprint.Length >= 12 ? fingerprint[..12] : fingerprint;
                    if (ClientPairingLogic.FindActiveInviteForIp(context.WorldState, remoteIp, obsNow) != null)
                    {
                        context.Monitor.Log($"[ClientSecurity] Dispositivo válido con huella {fpShort}... pero todas las invitaciones para IP {remoteIp} están reservadas por otras solicitudes pendientes.", LogLevel.Info);
                        alreadyHandled = true;
                        RecordConnectionAttempt(remoteIp, fingerprint, $"Invitaciones de IP reservadas ({fpShort})", logWarn: false);
                        DisconnectConnectionById(connectionId, "Todas las invitaciones para tu IP están reservadas por otras solicitudes pendientes. Pide al propietario que apruebe o cree otra invitación en Discord.");
                        return false;
                    }

                    ProbationAndBlacklistLogic.PurgeExpiredTickets(context.WorldState, obsNow);
                    if ((context.WorldState.PutreTickets?.Count ?? 0) > 0)
                    {
                        context.Monitor.Log($"[ClientSecurity] Dispositivo con solicitud Putre observada: huella {fpShort}... desde IP {remoteIp}.", LogLevel.Info);
                        alreadyHandled = true;
                        RecordConnectionAttempt(remoteIp, fingerprint, $"Observado para #pelican-gate ({fpShort})", logWarn: false);
                        DisconnectConnectionById(connectionId, $"Active admission request. Your fingerprint is: {fpShort} — Paste it in Discord (#pelican-gate).");
                        return false;
                    }

                    context.Monitor.Log($"[ClientSecurity] Dispositivo válido con huella {fpShort}... pero no autorizado ni con invitación activa.", LogLevel.Info);
                    alreadyHandled = true;
                    RecordConnectionAttempt(remoteIp, fingerprint, $"Bloqueado (huella {fpShort} sin invitación)", logWarn: false);
                    DisconnectConnectionById(connectionId, $"Servidor protegido por lista blanca. Huella: {fpShort}. Solicita una invitación al propietario o usa #pelican-gate en Discord.");
                    return false;
                }

                if (matchedDevice != null)
                {
                    matchedDevice.LastSeenUtc = DateTimeOffset.UtcNow;
                    string normIp = ClientPairingLogic.NormalizeIp(remoteIp);
                    if (!string.IsNullOrEmpty(normIp))
                        matchedDevice.LastSeenIp = normIp;
                }

                verifiedToken = fingerprint;
                return true;
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[ClientSecurity] No se pudo verificar el desafío de conexión: {ex.GetType().Name}.", LogLevel.Warn);
            }
            return false;
        }

        private static string ResolveRemoteIpByPlayerId(long playerId)
        {
            if (playerRemoteIps.TryGetValue(playerId, out string? cached) && !string.IsNullOrEmpty(cached))
                return cached;

            try
            {
                if (Game1.server is GameServer gs &&
                    AccessTools.Field(typeof(GameServer), "servers")?.GetValue(gs) is IEnumerable servers)
                {
                    foreach (object? obj in servers)
                    {
                        if (obj is Server srv)
                        {
                            string? userId = srv.getUserId(playerId);
                            string normId = ClientPairingLogic.NormalizeIp(userId);
                            if (!string.IsNullOrEmpty(normId))
                            {
                                playerRemoteIps[playerId] = normId;
                                return normId;
                            }
                        }
                    }
                }

                string? raw = Game1.server?.getUserName(playerId);
                string norm = ClientPairingLogic.NormalizeIp(raw);
                if (!string.IsNullOrEmpty(norm))
                {
                    playerRemoteIps[playerId] = norm;
                    return norm;
                }
            }
            catch
            {
                // Ignorar
            }

            return string.Empty;
        }

        private static void DisconnectConnectionById(string? connectionId, string reason)
        {
            if (string.IsNullOrWhiteSpace(connectionId) || Game1.server is not GameServer gs)
                return;

            try
            {
                var serversField = AccessTools.Field(typeof(GameServer), "servers");
                if (serversField?.GetValue(gs) is not IEnumerable servers)
                    return;

                foreach (object? srv in servers)
                {
                    if (srv is not LidgrenServer ls)
                        continue;

                    var netServer = AccessTools.Field(typeof(LidgrenServer), "server")?.GetValue(ls);
                    if (netServer == null)
                        continue;

                    var connsProp = AccessTools.Property(netServer.GetType(), "Connections");
                    if (connsProp?.GetValue(netServer) is not IEnumerable conns)
                        continue;

                    var getConnIdMethod = AccessTools.Method(typeof(LidgrenServer), "getConnectionId");
                    foreach (object? conn in conns)
                    {
                        if (conn == null)
                            continue;

                        string? id = getConnIdMethod?.Invoke(ls, new[] { conn }) as string;
                        if (string.Equals(id, connectionId, StringComparison.Ordinal))
                        {
                            AccessTools.Method(conn.GetType(), "Disconnect", new[] { typeof(string) })?.Invoke(conn, new object[] { reason });
                            return;
                        }
                    }
                }
            }
            catch
            {
                // Ignorar
            }
        }

        private static void RecordBlockedAttempt(string remoteIp, string? token) =>
            RecordConnectionAttempt(remoteIp, token, "Bloqueado (IP sin invitación ni dispositivo emparejado)", logWarn: true);

        private static void RecordConnectionAttempt(string remoteIp, string? token, string outcome, bool logWarn)
        {
            string ip = string.IsNullOrWhiteSpace(remoteIp) ? "desconocida" : remoteIp;
            DateTimeOffset now = DateTimeOffset.UtcNow;

            recentConnectionAttempts.Add(new RecentConnectionAttempt
            {
                RemoteIp = ip,
                MaskedToken = PlayerAuthLogic.MaskToken(token),
                TimestampUtc = now,
                Outcome = outcome
            });

            while (recentConnectionAttempts.Count > MaxRecentAttempts)
                recentConnectionAttempts.RemoveAt(0);

            if (logWarn && (!lastBlockedLogByIp.TryGetValue(ip, out DateTimeOffset lastLog) || (now - lastLog).TotalSeconds >= 60))
            {
                lastBlockedLogByIp[ip] = now;
                context?.Monitor.Log(
                    $"[ClientSecurity] Conexión bloqueada desde IP {ip} (dispositivo {PlayerAuthLogic.MaskToken(token)}): no está en lista blanca ni tiene código de pairing pendiente.",
                    LogLevel.Warn);
            }
        }

        private void ApplyHarmonyPatches(Harmony harmony)
        {
            _ = harmony;
            if (context is not ModContext ctx)
                return;

            bool allOk = true;

            // 1. Inyección de token en el cliente al presentarse al servidor
            allOk &= ctx.Patch(
                "Client.sendPlayerIntroduction",
                Name,
                PatchCriticality.Security,
                AccessTools.Method(typeof(Client), nameof(Client.sendPlayerIntroduction)),
                prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_Client_SendPlayerIntroduction)),
                finalizer: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Finalizer_Client_SendPlayerIntroduction)));

            // 2. Override de getUserID en las implementaciones concretas de Client (IP directa, Steam y GOG Galaxy).
            var patchedGetUserID = new HashSet<MethodBase>();
            void PatchGetUserID(Type? clientType, bool optional)
            {
                if (clientType == null)
                {
                    if (optional)
                        ctx.PatchRegistry.RecordSkippedOptional("SDKClient.getUserID", Name, PatchCriticality.Security, "SDK opcional no cargado.");
                    return;
                }

                var method = AccessTools.Method(clientType, "getUserID");
                if (method == null || method.IsAbstract || !patchedGetUserID.Add(method))
                    return;

                allOk &= ctx.Patch(
                    $"{clientType.Name}.getUserID",
                    Name,
                    PatchCriticality.Security,
                    method,
                    prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_Client_GetUserID)),
                    optional: optional);
            }

            PatchGetUserID(typeof(Client), optional: false);
            PatchGetUserID(typeof(LidgrenClient), optional: false);
            PatchGetUserID(AccessTools.TypeByName("StardewValley.SDKs.Steam.SteamNetClient"), optional: true);
            PatchGetUserID(AccessTools.TypeByName("StardewValley.SDKs.GogGalaxy.GalaxyNetClient"), optional: true);

            allOk &= ctx.Patch(
                "LidgrenServer.sendVersionInfo",
                Name,
                PatchCriticality.Security,
                AccessTools.Method(typeof(LidgrenServer), "sendVersionInfo"),
                prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_SendVersionInfo)));
            allOk &= ctx.Patch(
                "LidgrenServer.initialize",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(LidgrenServer), nameof(LidgrenServer.initialize)),
                transpiler: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Transpile_LidgrenServer_Initialize)));
            allOk &= ctx.Patch(
                "Server.onDisconnect",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(Server), nameof(Server.onDisconnect)),
                postfix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Postfix_Server_OnDisconnect)));
            allOk &= ctx.Patch(
                "LidgrenServer.kick",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(LidgrenServer), nameof(LidgrenServer.kick)),
                prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_LidgrenServer_Kick)));
            allOk &= ctx.Patch(
                "Multiplayer.processIncomingMessage",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(Multiplayer), nameof(Multiplayer.processIncomingMessage)),
                prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_Multiplayer_ProcessIncomingMessage)));
            allOk &= ctx.Patch(
                "Client.processIncomingMessage",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(Client), "processIncomingMessage", new[] { typeof(IncomingMessage) }),
                prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_Client_ProcessIncomingMessage)));
            allOk &= ctx.Patch(
                "LidgrenClient.receiveHandshake",
                Name,
                PatchCriticality.Security,
                AccessTools.Method(typeof(LidgrenClient), "receiveHandshake"),
                prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_ReceiveHandshake)));

            // 3. Sincronización proactiva de tokens y filtro de lista blanca por IP antes de enviar la lista de peones
            allOk &= ctx.Patch(
                "GameServer.sendAvailableFarmhands",
                Name,
                PatchCriticality.Security,
                AccessTools.Method(typeof(GameServer), nameof(GameServer.sendAvailableFarmhands)),
                prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_SendAvailableFarmhands)));

            // 4. Validación y reclamo autoritativo en el host (GameServer.checkFarmhandRequest)
            allOk &= ctx.Patch(
                "GameServer.checkFarmhandRequest",
                Name,
                PatchCriticality.Security,
                AccessTools.Method(typeof(GameServer), nameof(GameServer.checkFarmhandRequest)),
                prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_CheckFarmhandRequest)));

            // 5. Bloqueo visual y control de pertenencia de ranuras en FarmhandMenu
            allOk &= ctx.Patch(
                "FarmhandSlot.ctor",
                Name,
                PatchCriticality.Security,
                AccessTools.Constructor(typeof(FarmhandMenu.FarmhandSlot), new[] { typeof(FarmhandMenu), typeof(Farmer) }),
                postfix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Postfix_FarmhandSlot_Ctor)));

            // 6. Etiqueta visual "[De otro jugador]" en las ranuras bloqueadas
            allOk &= ctx.Patch(
                "FarmhandSlot.drawSlotName",
                Name,
                PatchCriticality.Experience,
                AccessTools.Method(typeof(FarmhandMenu.FarmhandSlot), "drawSlotName"),
                postfix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Postfix_FarmhandSlot_DrawSlotName)));

            // 7. Ordenación inteligente del menú de peones (personajes propios primero)
            allOk &= ctx.Patch(
                "FarmhandMenu.ctor",
                Name,
                PatchCriticality.Experience,
                AccessTools.Constructor(typeof(FarmhandMenu), new[] { typeof(Client) }),
                postfix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Postfix_FarmhandMenu_Ctor)));

            // 8. Mostrar el motivo real y huella ECDSA en cliente cuando el servidor rechaza la conexión
            allOk &= ctx.Patch(
                "LidgrenClient.clientRemotelyDisconnected",
                Name,
                PatchCriticality.Experience,
                AccessTools.Method(typeof(LidgrenClient), "clientRemotelyDisconnected", new[] { typeof(NetConnectionStatus), typeof(string) }),
                postfix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Postfix_LidgrenClient_ClientRemotelyDisconnected)));
            allOk &= ctx.Patch(
                "FarmhandMenu.getStatusText",
                Name,
                PatchCriticality.Experience,
                AccessTools.Method(typeof(FarmhandMenu), "getStatusText", Type.EmptyTypes),
                postfix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Postfix_FarmhandMenu_GetStatusText)));
            allOk &= ctx.Patch(
                "LoadGameMenu.drawStatusText",
                Name,
                PatchCriticality.Experience,
                AccessTools.Method(typeof(LoadGameMenu), "drawStatusText", new[] { typeof(SpriteBatch) }),
                prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_LoadGameMenu_DrawStatusText)));

            if (allOk)
                ctx.Monitor.Log("[ClientSecurityModule] Parches Harmony de seguridad y posesión aplicados correctamente.", LogLevel.Trace);
        }

        // ─────────────────────────── Parches Harmony ───────────────────────────

        public static void Prefix_Client_SendPlayerIntroduction(Client __instance)
        {
            if (Game1.player == null)
                return;

            Game1.player.modData[ClientVersionGuardModule.ProtocolKey] = ClientVersionGuardModule.ProtocolRevision;

            // Si el peón seleccionado ya tiene asignado nuestro token de cliente legado, lo preservamos
            // para permitir el juego transparente con servidores que aún no han completado la migración de ranura.
            if (!string.IsNullOrEmpty(ClientIdentity.CurrentToken) &&
                string.Equals(Game1.player.userID.Value, ClientIdentity.CurrentToken, StringComparison.OrdinalIgnoreCase))
            {
                Game1.player.userID.Value = ClientIdentity.CurrentToken;
                return;
            }

            string publicId = !string.IsNullOrEmpty(ClientIdentity.DeviceFingerprint)
                ? ClientIdentity.DeviceFingerprint
                : ClientIdentity.CurrentToken;
            if (!string.IsNullOrWhiteSpace(publicId))
            {
                Game1.player.userID.Value = publicId;
            }
        }

        public static void Finalizer_Client_SendPlayerIntroduction()
        {
            Game1.player?.modData.Remove(ClientVersionGuardModule.ProtocolKey);
        }

        public static bool Prefix_Client_GetUserID(ref string __result)
        {
            if (Game1.player != null &&
                !string.IsNullOrEmpty(ClientIdentity.CurrentToken) &&
                string.Equals(Game1.player.userID.Value, ClientIdentity.CurrentToken, StringComparison.OrdinalIgnoreCase))
            {
                __result = ClientIdentity.CurrentToken;
                return false;
            }

            string publicId = !string.IsNullOrEmpty(ClientIdentity.DeviceFingerprint)
                ? ClientIdentity.DeviceFingerprint
                : ClientIdentity.CurrentToken;
            if (!string.IsNullOrWhiteSpace(publicId))
            {
                __result = publicId;
                return false;
            }
            return true;
        }

        public static bool Prefix_LidgrenClient_GetUserID(ref string __result) => Prefix_Client_GetUserID(ref __result);

        public static bool Prefix_SendVersionInfo(LidgrenServer __instance, NetIncomingMessage message)
        {
            try
            {
                if (context == null || !Context.IsMainPlayer)
                    return true;

                string clientIp = message?.SenderEndPoint?.Address?.ToString() ?? string.Empty;
                System.Net.IPEndPoint? remoteEndPoint = message?.SenderEndPoint;
                if (string.IsNullOrEmpty(clientIp) || __instance?.server == null || remoteEndPoint == null)
                    return true;

                var challenge = serverChallenges.Issue(clientIp, DateTimeOffset.UtcNow, string.Empty);
                byte[] challengePayload = DeviceAuthWire.PackDiscoveryChallenge(challenge);

                NetOutgoingMessage response = __instance.server.CreateMessage();
                response.Write(Multiplayer.protocolVersion);
                response.Write("StardewValley");
                response.Write(challengePayload);
                __instance.server.SendDiscoveryResponse(response, remoteEndPoint);
                return false;
            }
            catch (Exception ex)
            {
                // Fail-open hacia el handshake vanilla para no romper el descubrimiento en LAN/local.
                LogHandshakeFailureOnce(ex);
                return true;
            }
        }

        private static long lastHandshakeErrorMs;

        private static void LogHandshakeFailureOnce(Exception ex)
        {
            try
            {
                long now = Environment.TickCount64;
                if (now - lastHandshakeErrorMs < 10_000)
                    return;
                lastHandshakeErrorMs = now;
                context?.Monitor.Log($"[ClientSecurity] Fallo inesperado en sendVersionInfo (fail-open a vanilla): {ex.GetType().Name}: {ex.Message}", LogLevel.Warn);
            }
            catch
            {
                // Nunca romper el juego desde el propio manejador de errores.
            }
        }

        public static bool Prefix_ReceiveHandshake(LidgrenClient __instance, NetIncomingMessage msg)
        {
            try
            {
                return Prefix_ReceiveHandshakeInner(__instance, msg);
            }
            catch (Exception ex)
            {
                // Fail-open hacia el handshake vanilla ante paquetes malformados o rarezas del runtime.
                LogHandshakeFailureOnce(ex);
                return true;
            }
        }

        private static bool Prefix_ReceiveHandshakeInner(LidgrenClient __instance, NetIncomingMessage msg)
        {
            int remaining = msg.LengthBytes - (int)(msg.Position / 8);
            if (remaining != DeviceAuthWire.DiscoveryChallengePayloadLength)
                return true;

            byte[] rawPayload = msg.ReadBytes(DeviceAuthWire.DiscoveryChallengePayloadLength);

            if (!DeviceAuthWire.TryParseDiscoveryChallenge(rawPayload, string.Empty, out DeviceAuthChallenge? challenge) || challenge == null)
                return true;

            DeviceKeyPair? keyPair = ClientIdentity.DeviceKey;
            if (keyPair == null && context != null)
            {
                keyPair = ClientIdentity.InitializeDeviceKey(context.Helper);
            }

            if (keyPair == null)
                return true;

            byte[] signBytes = challenge.BuildSignBytes();
            byte[] signature = keyPair.Sign(signBytes);
            byte[] hailPayload = DeviceAuthWire.PackHailPayload(challenge.Nonce, challenge.TimestampUtc, keyPair.ExportPublicKeySpki(), signature);

            NetOutgoingMessage hail = __instance.client.CreateMessage();
            hail.Write(hailPayload);
            __instance.client.Connect(msg.SenderEndPoint.Address.ToString(), msg.SenderEndPoint.Port, hail);
            return false;
        }

        public static int GetDesiredMaximumConnections(int vanillaMax)
        {
            if (context == null)
                return vanillaMax;
            return Math.Max(1, context.Config.MaxConcurrentPlayers + context.Config.AdmissionQueueCapacity);
        }

        public static IEnumerable<CodeInstruction> Transpile_LidgrenServer_Initialize(IEnumerable<CodeInstruction> instructions)
        {
            var setMaxConn = AccessTools.PropertySetter(typeof(NetPeerConfiguration), nameof(NetPeerConfiguration.MaximumConnections));
            var helper = AccessTools.Method(typeof(ClientSecurityModule), nameof(GetDesiredMaximumConnections));
            foreach (var instr in instructions)
            {
                if (instr.Calls(setMaxConn))
                {
                    yield return new CodeInstruction(OpCodes.Call, helper);
                }
                yield return instr;
            }
        }

        public static void Prefix_Multiplayer_ProcessIncomingMessage(IncomingMessage msg)
        {
            if (msg == null || !Context.IsMainPlayer)
                return;

            if (msg.MessageType == Multiplayer.disconnecting && msg.FarmerID != 0)
            {
                MarkPlayerDisconnectClassification(msg.FarmerID, DisconnectClassification.VoluntaryExit);
            }
        }

        public static void Prefix_LidgrenServer_Kick(long disconnectee)
        {
            if (Context.IsMainPlayer && disconnectee != 0 && !pendingPlayerDisconnectClassifications.ContainsKey(disconnectee))
            {
                pendingPlayerDisconnectClassifications[disconnectee] = DisconnectClassification.AdminKick;
            }
        }

        public static void Postfix_Server_OnDisconnect(string connectionId)
        {
            if (!Context.IsMainPlayer || context == null || string.IsNullOrWhiteSpace(connectionId))
                return;

            var classification = DisconnectClassification.NetworkDrop;
            if (connectionToPlayerId.Remove(connectionId, out long playerId))
            {
                playerToConnectionId.Remove(playerId);
                if (pendingPlayerDisconnectClassifications.Remove(playerId, out var explicitClass))
                {
                    classification = explicitClass;
                }
            }

            queuedCallbacks.Remove(connectionId);
            pendingUnclaimedOffers.Remove(connectionId);
            var promoted = Admission.OnDisconnected(connectionId, classification, DateTimeOffset.UtcNow);
            context.Monitor.Log($"[Admission] OnDisconnected conn={connectionId} class={classification} promoted={promoted.Count} remainingQueue={queuedCallbacks.Count}", LogLevel.Trace);
            if (promoted.Count > 0 || queuedCallbacks.Count > 0)
            {
                ProcessPromotedAndNotifyQueue(promoted);
            }
        }

        private const string QueueMessageWirePrefix = "Strings\\UI:P4E_Queue:";

        private static string FormatQueuePositionMessage(int position, int capacity)
        {
            string fallback = $"Servidor lleno. Tu posición en la cola de espera: {position}/{capacity}.";
            string textStr = fallback;
            if (context != null)
            {
                var text = context.Helper.Translation.Get("admission.queue_position", new { position, capacity });
                if (text.HasValue())
                    textStr = text.ToString();
            }
            return $"{QueueMessageWirePrefix}{position}/{capacity}:{textStr}";
        }

        public static bool Prefix_Client_ProcessIncomingMessage(Client __instance, IncomingMessage message)
        {
            if (__instance == null || message == null || message.MessageType != Multiplayer.connectionMessage)
                return true;

            // Mientras el cliente espera en cola (o espera a que el host termine de cargar), el juego ya recibió
            // respuesta del servidor; marcamos handshake completado para evitar el timeout fijo de 45s en Client.connect().
            AccessTools.Field(typeof(Client), "hasHandshaked")?.SetValue(__instance, true);
            AccessTools.Field(typeof(Client), "timeoutTime")?.SetValue(__instance, null);

            string raw = message.Reader.ReadString();
            if (raw.StartsWith("Strings\\UI:P4E_Queue:", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("Strings/UI:P4E_Queue:", StringComparison.OrdinalIgnoreCase))
            {
                string rest = raw.Substring(QueueMessageWirePrefix.Length);
                int colonIdx = rest.IndexOf(':');
                string counts = colonIdx >= 0 ? rest.Substring(0, colonIdx) : rest;
                string fallback = colonIdx >= 0 ? rest.Substring(colonIdx + 1) : raw;

                int slashIdx = counts.IndexOf('/');
                if (slashIdx > 0 &&
                    int.TryParse(counts.Substring(0, slashIdx), out int pos) &&
                    int.TryParse(counts.Substring(slashIdx + 1), out int cap) &&
                    context != null)
                {
                    var text = context.Helper.Translation.Get("admission.queue_position", new { position = pos, capacity = cap });
                    __instance.connectionMessage = text.HasValue() ? text.ToString() : fallback;
                }
                else
                {
                    __instance.connectionMessage = fallback;
                }
                return false;
            }

            try
            {
                __instance.connectionMessage = Game1.content.LoadString(raw);
            }
            catch
            {
                __instance.connectionMessage = raw;
            }
            return false;
        }

        private static string GetAdmissionQueueFullMessage()
        {
            string fallback = "El servidor y la cola de espera están llenos. Inténtalo más tarde.";
            if (context == null)
                return fallback;
            var text = context.Helper.Translation.Get("admission.queue_full");
            return text.HasValue() ? text.ToString() : fallback;
        }

        private static void ProcessPromotedAndNotifyQueue(IReadOnlyList<QueuedConnectionInfo> promoted)
        {
            foreach (var item in promoted)
            {
                bool hasCb = queuedCallbacks.Remove(item.ConnectionId, out var cb);
                bool active = hasCb && cb != null && cb.GameServer.isConnectionActive(item.ConnectionId);
                context?.Monitor.Log($"[Admission] Promote conn={item.ConnectionId} hasCb={hasCb} isActive={active}", LogLevel.Trace);
                if (active && cb != null)
                {
                    cb.GameServer.sendAvailableFarmhands(cb.UserId, item.ConnectionId, cb.SendMessage);
                }
            }

            if (context == null)
                return;

            var remaining = Admission.GetQueueSnapshot();
            foreach (var entry in remaining)
            {
                if (queuedCallbacks.TryGetValue(entry.ConnectionId, out var cb) && cb.GameServer.isConnectionActive(entry.ConnectionId))
                {
                    cb.SendMessage(new OutgoingMessage(
                        Multiplayer.connectionMessage,
                        Game1.player,
                        FormatQueuePositionMessage(entry.Position, context.Config.AdmissionQueueCapacity)));
                }
            }
        }

        public static bool Prefix_SendAvailableFarmhands(
            GameServer __instance,
            string userId,
            string connectionId,
            Action<OutgoingMessage> sendMessage)
        {
            SyncServerTokensFromWorldState();

            string verifiedFingerprint = string.Empty;
            bool requireWhitelistForConn = false;

            if (context != null && Context.IsMainPlayer)
            {
                string remoteIp = ResolveRemoteIpByConnectionId(connectionId);
                bool isLoopback = ClientPairingLogic.IsLoopbackIp(remoteIp);
                requireWhitelistForConn = context.Config.RequirePairingWhitelist && !isLoopback;

                if (requireWhitelistForConn)
                {
                    if (!HasValidEarlyProof(connectionId, remoteIp, out verifiedFingerprint, out bool alreadyHandled))
                    {
                        if (!alreadyHandled)
                        {
                            RecordBlockedAttempt(remoteIp, userId);
                            DisconnectConnectionById(connectionId, "Acceso denegado: debes contar con un cliente actualizado y dispositivo autorizado.");
                        }
                        return false;
                    }

                    if (!FarmhandAuthLogic.CanAccessFarmhandList(context.WorldState, true, isLoopback, verifiedFingerprint, out string failureReason))
                    {
                        RecordBlockedAttempt(remoteIp, verifiedFingerprint);
                        DisconnectConnectionById(connectionId, failureReason);
                        return false;
                    }
                }
                else
                {
                    TryVerifyConnectionHail(connectionId, remoteIp, out verifiedFingerprint, out _);
                }
            }

            string callerIdentity = !string.IsNullOrEmpty(verifiedFingerprint)
                ? verifiedFingerprint
                : (!string.IsNullOrWhiteSpace(userId) ? userId.Trim() : string.Empty);

            // TASK-2402: Cola de admisión y reservas de reconexión
            if (context != null && Context.IsMainPlayer)
            {
                string identityKey = !string.IsNullOrEmpty(callerIdentity) ? callerIdentity : $"conn:{connectionId}";
                var decision = Admission.TryRequestAdmission(connectionId, identityKey, DateTimeOffset.UtcNow);
                if (decision.Outcome == AdmissionOutcome.Rejected)
                {
                    DisconnectConnectionById(connectionId, GetAdmissionQueueFullMessage());
                    return false;
                }

                if (decision.Outcome == AdmissionOutcome.Queued)
                {
                    queuedCallbacks[connectionId] = new QueuedConnectionCallbacks(__instance, userId, sendMessage);
                    context.Monitor.Log($"[Admission] Queued conn={connectionId} pos={decision.QueuePosition}/{context.Config.AdmissionQueueCapacity}", LogLevel.Trace);
                    sendMessage(new OutgoingMessage(
                        Multiplayer.connectionMessage,
                        Game1.player,
                        FormatQueuePositionMessage(decision.QueuePosition, context.Config.AdmissionQueueCapacity)));
                    return false;
                }

                queuedCallbacks.Remove(connectionId);
            }

            // TASK-2404: Cuando el juego está disponible, filtrar la lista de peones para enviar solo
            // el peón ligado al dispositivo o un único peón sin reclamar (nunca exponer los de otros ni saturar el saludo).
            if (context != null && Context.IsMainPlayer && __instance.isGameAvailable() && Game1.netWorldState?.Value?.farmhandData != null)
            {
                var multiplayer = context.Helper.Reflection.GetField<Multiplayer>(typeof(Game1), "multiplayer").GetValue();
                var availableRefs = new Dictionary<long, NetRef<Farmer>>();
                var candidates = new List<(long PlayerId, bool IsUnclaimed)>();
                var farmhandUserIds = new Dictionary<long, string?>();
                foreach (NetRef<Farmer> netRef in Game1.netWorldState.Value.farmhandData.FieldDict.Values)
                {
                    Farmer f = netRef.Value;
                    if (f == null)
                        continue;
                    if ((!f.isActive() || multiplayer.isDisconnecting(f.UniqueMultiplayerID)) && __instance.IsFarmhandAvailable(f))
                    {
                        long id = f.UniqueMultiplayerID;
                        availableRefs[id] = netRef;
                        candidates.Add((id, string.IsNullOrWhiteSpace(f.userID.Value)));
                        farmhandUserIds[id] = f.userID.Value;
                    }
                }

                var reservedOtherOffers = new HashSet<long>();
                foreach (var kv in pendingUnclaimedOffers)
                {
                    if (!string.Equals(kv.Key, connectionId, StringComparison.Ordinal))
                        reservedOtherOffers.Add(kv.Value);
                }

                IReadOnlyList<long> filteredIds = FarmhandAuthLogic.FilterAvailableFarmhandIds(
                    context.WorldState,
                    callerIdentity,
                    candidates,
                    requireWhitelist: requireWhitelistForConn,
                    farmhandUserIds: farmhandUserIds,
                    reservedUnclaimedIds: reservedOtherOffers);

                if (filteredIds.Count == 1
                    && availableRefs.TryGetValue(filteredIds[0], out NetRef<Farmer>? offeredRef)
                    && string.IsNullOrWhiteSpace(offeredRef.Value?.userID.Value))
                {
                    pendingUnclaimedOffers[connectionId] = filteredIds[0];
                }
                else
                {
                    pendingUnclaimedOffers.Remove(connectionId);
                }

                using var memoryStream = new MemoryStream();
                using var binaryWriter = new BinaryWriter(memoryStream);
                binaryWriter.Write(Game1.year);
                binaryWriter.Write(Game1.seasonIndex);
                binaryWriter.Write(Game1.dayOfMonth);
                binaryWriter.Write((byte)filteredIds.Count);
                foreach (long id in filteredIds)
                {
                    if (!availableRefs.TryGetValue(id, out NetRef<Farmer>? item))
                        continue;
                    try
                    {
                        item.Serializer = SaveSerializer.GetSerializer(typeof(Farmer));
                        item.WriteFull(binaryWriter);
                    }
                    finally
                    {
                        item.Serializer = null;
                    }
                }

                memoryStream.Seek(0L, SeekOrigin.Begin);
                context.Monitor.Log($"[Admission] Sending {filteredIds.Count} filtered farmhand(s) ({memoryStream.Length} B) to conn={connectionId}.", LogLevel.Trace);
                sendMessage(new OutgoingMessage(9, Game1.player, memoryStream.ToArray()));
                return false;
            }

            return true;
        }

        public static bool Prefix_CheckFarmhandRequest(
            GameServer __instance,
            ref string userId,
            string connectionId,
            NetFarmerRoot farmer,
            Action<OutgoingMessage> sendMessage)
        {
            if (farmer?.Value == null)
            {
                __instance.sendAvailableFarmhands(userId, connectionId, sendMessage);
                return false;
            }

            bool compatible = farmer.Value.modData.TryGetValue(ClientVersionGuardModule.ProtocolKey, out string? protocol)
                && protocol == ClientVersionGuardModule.ProtocolRevision;
            farmer.Value.modData.Remove(ClientVersionGuardModule.ProtocolKey);
            if (context?.Config.RequireClientMod == true && !compatible)
            {
                context.Monitor.Log("[ClientVersionGuard] Cliente rechazado: actualiza las DLL del mod y reinicia Stardew.", LogLevel.Warn);
                DisconnectConnectionById(connectionId, "Se requiere actualizar Pelican 4Ever a la versión del servidor.");
                return false;
            }

            long farmhandId = farmer.Value.UniqueMultiplayerID;
            if (!Game1.netWorldState.Value.farmhandData.TryGetValue(farmhandId, out Farmer? serverFarmer) || serverFarmer == null)
            {
                context?.Monitor.Log($"[ClientSecurity] El peón {farmhandId} pedido por la conexión {connectionId} ya no existe; se reenvía la lista.", LogLevel.Warn);
                __instance.sendAvailableFarmhands(userId, connectionId, sendMessage);
                return false;
            }

            pendingUnclaimedOffers.Remove(connectionId);

            if (context != null)
            {
                string remoteIp = ResolveRemoteIpByConnectionId(connectionId);
                bool isLoopback = ClientPairingLogic.IsLoopbackIp(remoteIp);

                if (context.Config.RequirePairingWhitelist && !isLoopback)
                {
                    if (!HasValidEarlyProof(connectionId, remoteIp, out string verifiedFingerprint, out bool alreadyHandled))
                    {
                        if (!alreadyHandled)
                        {
                            context.Monitor.Log("[ClientSecurity] Rechazado intento de selección de ranura sin prueba de posesión de clave válida.", LogLevel.Warn);
                            DisconnectConnectionById(connectionId, "No se ha verificado la identidad del dispositivo.");
                        }
                        return false;
                    }

                    if (!string.IsNullOrEmpty(remoteIp))
                    {
                        playerRemoteIps[farmhandId] = remoteIp;
                    }

                    var slotAuth = FarmhandAuthLogic.AuthorizeSlotSelection(
                        context.WorldState,
                        farmhandId,
                        verifiedFingerprint,
                        out string slotMessage);

                    if (slotAuth == SlotSelectionResult.DeniedBelongsToAnother ||
                        slotAuth == SlotSelectionResult.DeniedAlreadyHasBoundPlayer ||
                        slotAuth == SlotSelectionResult.DeniedDeviceNotAuthorized ||
                        slotAuth == SlotSelectionResult.DeniedNoIdentity)
                    {
                        context.Monitor.Log($"[ClientSecurity] RECHAZADO: Conexión {connectionId} ({verifiedFingerprint[..12]}...) no puede acceder a ranura {farmhandId}: {slotMessage}", LogLevel.Warn);
                        __instance.sendAvailableFarmhands(userId, connectionId, sendMessage);
                        return false;
                    }

                    var playerRecord = context.WorldState.GetOrCreatePlayer(farmhandId, serverFarmer.Name);

                    if (slotAuth == SlotSelectionResult.AllowedClaimedNew)
                    {
                        if (context.WorldState.AuthorizedDevices.TryGetValue(verifiedFingerprint, out AuthorizedDevice? dev) && dev != null)
                        {
                            dev.BoundPlayerId = farmhandId;
                        }
                        playerRecord.OwnerToken = verifiedFingerprint;
                        serverFarmer.userID.Value = verifiedFingerprint;
                        SavePairingSidecar();
                        PersistenceModule.FlushToSaveData(context, "SlotClaimed");
                        context.Monitor.Log($"[ClientSecurity] Peón libre {farmhandId} ('{serverFarmer.Name}') reclamado por el dispositivo {verifiedFingerprint[..12]}...", LogLevel.Info);
                    }

                    userId = verifiedFingerprint;
                    connectionToPlayerId[connectionId] = farmhandId;
                    playerToConnectionId[farmhandId] = connectionId;
                    return true;
                }

                if (TryVerifyConnectionHail(connectionId, remoteIp, out string verifiedFp, out _))
                {
                    if (!string.IsNullOrEmpty(remoteIp))
                    {
                        playerRemoteIps[farmhandId] = remoteIp;
                    }

                    var playerRecord = context.WorldState.GetOrCreatePlayer(farmhandId, serverFarmer.Name);
                    if (string.IsNullOrWhiteSpace(playerRecord.OwnerToken))
                    {
                        playerRecord.OwnerToken = verifiedFp;
                        if (string.IsNullOrWhiteSpace(serverFarmer.userID.Value))
                            serverFarmer.userID.Value = verifiedFp;
                        if (context.WorldState.AuthorizedDevices.TryGetValue(verifiedFp, out AuthorizedDevice? dev) && dev != null && dev.BoundPlayerId == 0)
                            dev.BoundPlayerId = farmhandId;
                        SavePairingSidecar();
                        PersistenceModule.FlushToSaveData(context, "SlotClaimed");
                    }

                    userId = verifiedFp;
                }
            }

            connectionToPlayerId[connectionId] = farmhandId;
            playerToConnectionId[farmhandId] = connectionId;
            return true;
        }

        public static void Postfix_FarmhandSlot_Ctor(FarmhandMenu.FarmhandSlot __instance, Farmer farmer)
        {
            if (farmer != null)
            {
                string slotIdentifier = farmer.userID.Value ?? string.Empty;
                bool belongsToAnother = PlayerAuthLogic.BelongsToAnotherClient(
                    slotIdentifier,
                    ClientIdentity.DeviceFingerprint,
                    ClientIdentity.CurrentToken);

                // Sobrescribe el valor calculado por el juego nativo (que comparó contra GetUserID() de Steam/GOG)
                AccessTools.Field(typeof(FarmhandMenu.FarmhandSlot), "_belongsToAnotherPlayer")?.SetValue(__instance, belongsToAnother);
            }
        }

        public static void Postfix_FarmhandSlot_DrawSlotName(FarmhandMenu.FarmhandSlot __instance, SpriteBatch b, int i)
        {
            if (__instance.BelongsToAnotherPlayer())
            {
                var menu = AccessTools.Field(typeof(FarmhandMenu.FarmhandSlot), "menu")?.GetValue(__instance) as FarmhandMenu;
                if (menu != null && i < menu.slotButtons.Count)
                {
                    var btn = menu.slotButtons[i];
                    Vector2 pos = new Vector2(btn.bounds.X + 128 + 36, btn.bounds.Y + 68);
                    Utility.drawTextWithShadow(b, "[De otro jugador]", Game1.smallFont, pos, Color.IndianRed);
                }
            }
        }

        public static void Postfix_FarmhandMenu_Ctor(FarmhandMenu __instance)
        {
            if (__instance.MenuSlots == null || __instance.MenuSlots.Count <= 1)
                return;

            __instance.MenuSlots.Sort((a, b) =>
            {
                int Score(LoadGameMenu.MenuSlot slot)
                {
                    if (slot is FarmhandMenu.FarmhandSlot fs)
                    {
                        if (fs.BelongsToAnotherPlayer())
                            return 2; // Partidas de otros al final
                        if (fs.Farmer.isCustomized.Value)
                            return 0; // Mis personajes primero
                        return 1; // Hueco libre nuevo en medio
                    }
                    return 1;
                }

                return Score(a).CompareTo(Score(b));
            });
        }

        public static void Postfix_LidgrenClient_ClientRemotelyDisconnected(LidgrenClient __instance, NetConnectionStatus status, string message)
        {
            _ = status;
            if (__instance == null || string.IsNullOrWhiteSpace(message))
                return;
            if (string.Equals(message, Multiplayer.kicked, StringComparison.Ordinal) ||
                string.Equals(message, "Timed out", StringComparison.OrdinalIgnoreCase))
                return;

            if (!string.Equals(__instance.connectionMessage, message, StringComparison.Ordinal))
            {
                __instance.connectionMessage = message;
                context?.Monitor.Log($"[ClientSecurity] Desconectado por el servidor: {message}", LogLevel.Alert);
            }
        }

        public static void Postfix_FarmhandMenu_GetStatusText(FarmhandMenu __instance, ref string __result)
        {
            if (__instance?.client != null && __instance.client.timedOut && !string.IsNullOrWhiteSpace(__instance.client.connectionMessage))
            {
                __result = __instance.client.connectionMessage;
            }
        }

        public static bool Prefix_LoadGameMenu_DrawStatusText(LoadGameMenu __instance, SpriteBatch b)
        {
            if (__instance is FarmhandMenu fh && fh.client != null && fh.client.timedOut && !string.IsNullOrWhiteSpace(fh.client.connectionMessage))
            {
                string wrapped = Game1.parseText(fh.client.connectionMessage, Game1.dialogueFont, Math.Max(320, __instance.width - 96));
                Vector2 size = Game1.dialogueFont.MeasureString(wrapped);
                Vector2 pos = new Vector2(
                    __instance.xPositionOnScreen + (__instance.width - size.X) / 2f,
                    __instance.yPositionOnScreen + (__instance.height - size.Y) / 2f);
                Utility.drawTextWithShadow(b, wrapped, Game1.dialogueFont, pos, Game1.textColor);
                return false;
            }
            return true;
        }
    }
}
