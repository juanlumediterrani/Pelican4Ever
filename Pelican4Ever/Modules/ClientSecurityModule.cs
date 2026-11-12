using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.Security;
using Pelican4Ever.Framework;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Network;

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

        // Estado en el cliente cuando el servidor exige introducir el código de pairing al entrar por primera vez
        private static bool localClientAwaitingPairing;
        private static string localPairingPrompt = "security.pairing_code_prompt";
        private static int localPairingReopenDelayTicks;

        /// <summary>True en el cliente local mientras tiene pendiente introducir el código de emparejamiento.</summary>
        public static bool IsLocalClientAwaitingPairing => !Context.IsMainPlayer && localClientAwaitingPairing;

        public string Name => "ClientSecurity";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            // Inicializar o verificar identidad del cliente local
            ClientIdentity.Initialize(ctx.Helper);

            // Eventos de ciclo de vida y multijugador
            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            ctx.Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
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

            ApplyHarmonyPatches(ctx.Harmony);
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            serverChallenges.Clear();
            pendingPairingSessions.Clear();
            playerRemoteIps.Clear();
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
            if (args.Length > 1)
            {
                // Si se especifica peón o nombre, crear respaldo verificable y migrar explícitamente
                string? sidecarPath = GetPairingSidecarPath();
                if (!string.IsNullOrEmpty(sidecarPath))
                {
                    if (DeviceMigrationLogic.CreateVerifiableBackup(sidecarPath, context.WorldState, DateTimeOffset.UtcNow, out VerifiableBackupResult? backup, out string? backupErr))
                    {
                        context.Monitor.Log($"[ClientSecurity] Copia de seguridad verificada: {Path.GetFileName(backup?.BackupPath)} (SHA-256: {backup?.Sha256Checksum[..12]}...).", LogLevel.Info);
                    }
                    else
                    {
                        context.Monitor.Log($"[ClientSecurity] Error creando respaldo verificable previo a migración: {backupErr}", LogLevel.Warn);
                    }
                }

                if (!DeviceMigrationLogic.MigratePlayer(context.WorldState, target, args[1].Trim(), DateTimeOffset.UtcNow, out AuthorizedDevice? dev, out PlayerRecord? rec, out string migMsg))
                {
                    context.Monitor.Log(migMsg, LogLevel.Warn);
                    return;
                }

                SavePairingSidecar();
                PersistenceModule.FlushToSaveData(context, "PairApproveMigrate");
                SyncServerTokensFromWorldState();

                int todayMig = Context.IsWorldReady && Game1.Date != null ? Game1.Date.TotalDays : 0;
                int timeMig = Context.IsWorldReady ? Game1.timeOfDay : 600;
                AdminApiModule.EventLog.Append(
                    HostEventTypes.PlayerPaired,
                    todayMig,
                    timeMig,
                    migMsg,
                    new Dictionary<string, object?>
                    {
                        ["alias"] = dev?.Alias,
                        ["fingerprint"] = dev?.Fingerprint,
                        ["boundPlayerId"] = dev?.BoundPlayerId
                    });

                context.Monitor.Log(migMsg, LogLevel.Info);
                return;
            }

            if (!DeviceEnrollmentLogic.ApproveEnrollment(context.WorldState, target, 0, DateTimeOffset.UtcNow, out AuthorizedDevice? device, out string message))
            {
                context.Monitor.Log(message, LogLevel.Warn);
                return;
            }

            SavePairingSidecar();
            PersistenceModule.FlushToSaveData(context, "PairApprove");

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

        private static bool HasValidEarlyProof(string connectionId, string remoteIp, out string verifiedToken)
        {
            verifiedToken = string.Empty;
            if (context == null || Game1.server is not GameServer gs)
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
                            verifiedToken = verified.Fingerprint;
                            return true;
                        }
                        NetIncomingMessage? hail = connection.RemoteHailMessage;
                        if (hail == null)
                            return false;

                        if (!DeviceAuthWire.TryParseHailPayload(
                                hail.Data,
                                out byte[]? nonce,
                                out _,
                                out byte[]? pubKeySpki,
                                out byte[]? sig) || nonce == null || pubKeySpki == null || sig == null)
                        {
                            return false;
                        }

                        if (!serverChallenges.TryVerifyAndConsume(
                                remoteIp,
                                nonce,
                                pubKeySpki,
                                sig,
                                DateTimeOffset.UtcNow,
                                out string fingerprint,
                                out string failureReason))
                        {
                            context.Monitor.Log($"[ClientSecurity] Verificación de conexión fallida desde {remoteIp}: {failureReason}", LogLevel.Trace);
                            return false;
                        }

                        // Comprobar si la huella digital está autorizada en la lista blanca del servidor
                        bool isAuthorized = ServerDeviceRegistry.IsDeviceAuthorized(context.WorldState, fingerprint, out _);

                        isAuthorized |= context.WorldState.AuthorizedClients != null &&
                            context.WorldState.AuthorizedClients.Values.Any(c =>
                                string.Equals(c.ClientToken, fingerprint, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(c.Alias, fingerprint, StringComparison.OrdinalIgnoreCase));

                        isAuthorized |= context.WorldState.Players.Values.Any(p =>
                            string.Equals(p.OwnerToken, fingerprint, StringComparison.OrdinalIgnoreCase));

                        if (!isAuthorized)
                        {
                            var activeInvite = ClientPairingLogic.FindActiveInviteForIp(context.WorldState, remoteIp, DateTimeOffset.UtcNow);
                            if (activeInvite != null)
                            {
                                context.WorldState.PendingEnrollments ??= new Dictionary<string, DeviceEnrollmentRequest>(StringComparer.OrdinalIgnoreCase);
                                if (!context.WorldState.PendingEnrollments.TryGetValue(fingerprint, out var enroll))
                                {
                                    enroll = new DeviceEnrollmentRequest
                                    {
                                        Fingerprint = fingerprint,
                                        PublicKeyBase64 = Convert.ToBase64String(pubKeySpki),
                                        Alias = activeInvite.Alias,
                                        RemoteIp = ClientPairingLogic.NormalizeIp(remoteIp),
                                        InviteCode = activeInvite.Code,
                                        RequestedAtUtc = DateTimeOffset.UtcNow,
                                        ExpiresAtUtc = activeInvite.ExpiresAtUtc,
                                        Attempts = 1
                                    };
                                    context.WorldState.PendingEnrollments[fingerprint] = enroll;
                                }
                                else
                                {
                                    enroll.Attempts++;
                                    enroll.RemoteIp = ClientPairingLogic.NormalizeIp(remoteIp);
                                }

                                SavePairingSidecar();
                                string shortFp = fingerprint.Length >= 12 ? fingerprint[..12] : fingerprint;
                                context.Monitor.Log($"[ClientSecurity] Solicitud de alta registrada para '{activeInvite.Alias}' (huella: {shortFp}...) desde IP {remoteIp}.", LogLevel.Info);
                                DisconnectConnectionById(connectionId, $"Dispositivo registrado para aprobación. Huella: {shortFp}. Solicita al propietario que apruebe tu acceso en Discord.");
                                return false;
                            }

                            string fpShort = fingerprint.Length >= 12 ? fingerprint[..12] : fingerprint;
                            context.Monitor.Log($"[ClientSecurity] Dispositivo válido con huella {fpShort}... pero no autorizado ni con invitación activa.", LogLevel.Info);
                            DisconnectConnectionById(connectionId, "Servidor protegido por lista blanca. Solicita una invitación al propietario en Discord.");
                            return false;
                        }

                        verifiedToken = fingerprint;
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

        private static string ResolveRemoteIpByPlayerId(long playerId)
        {
            if (playerRemoteIps.TryGetValue(playerId, out string? cached) && !string.IsNullOrEmpty(cached))
                return cached;

            try
            {
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

        private static void RecordBlockedAttempt(string remoteIp, string? token)
        {
            string ip = string.IsNullOrWhiteSpace(remoteIp) ? "desconocida" : remoteIp;
            DateTimeOffset now = DateTimeOffset.UtcNow;

            recentConnectionAttempts.Add(new RecentConnectionAttempt
            {
                RemoteIp = ip,
                MaskedToken = PlayerAuthLogic.MaskToken(token),
                TimestampUtc = now,
                Outcome = "Bloqueado (IP sin invitación ni dispositivo emparejado)"
            });

            while (recentConnectionAttempts.Count > MaxRecentAttempts)
                recentConnectionAttempts.RemoveAt(0);

            if (!lastBlockedLogByIp.TryGetValue(ip, out DateTimeOffset lastLog) || (now - lastLog).TotalSeconds >= 60)
            {
                lastBlockedLogByIp[ip] = now;
                context?.Monitor.Log(
                    $"[ClientSecurity] Conexión bloqueada desde IP {ip} (dispositivo {PlayerAuthLogic.MaskToken(token)}): no está en lista blanca ni tiene código de pairing pendiente.",
                    LogLevel.Warn);
            }
        }

        private void ApplyHarmonyPatches(Harmony harmony)
        {
            int failures = 0;

            void TryPatch(string name, Action apply)
            {
                try
                {
                    apply();
                }
                catch (Exception ex)
                {
                    failures++;
                    context?.Monitor.Log($"[ClientSecurityModule] Error al aplicar el parche '{name}': {ex}", LogLevel.Error);
                }
            }

            // 1. Inyección de token en el cliente al presentarse al servidor
            TryPatch("Client.sendPlayerIntroduction", () => harmony.Patch(
                original: AccessTools.Method(typeof(Client), nameof(Client.sendPlayerIntroduction)),
                prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_Client_SendPlayerIntroduction))
            ));

            // 2. Override de getUserID en las implementaciones concretas de Client (IP directa, Steam y GOG Galaxy).
            var patchedGetUserID = new HashSet<MethodBase>();
            void PatchGetUserID(Type? clientType)
            {
                if (clientType == null) return;
                var method = AccessTools.Method(clientType, "getUserID");
                if (method == null || method.IsAbstract || !patchedGetUserID.Add(method)) return;

                TryPatch($"{clientType.Name}.getUserID", () => harmony.Patch(
                    original: method,
                    prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_Client_GetUserID))
                ));
            }

            PatchGetUserID(typeof(Client));
            PatchGetUserID(typeof(LidgrenClient));
            PatchGetUserID(AccessTools.TypeByName("StardewValley.SDKs.Steam.SteamNetClient"));
            PatchGetUserID(AccessTools.TypeByName("StardewValley.SDKs.GogGalaxy.GalaxyNetClient"));

            TryPatch("LidgrenServer.sendVersionInfo", () => harmony.Patch(
                original: AccessTools.Method(typeof(LidgrenServer), "sendVersionInfo"),
                prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_SendVersionInfo))));
            TryPatch("LidgrenClient.receiveHandshake", () => harmony.Patch(
                original: AccessTools.Method(typeof(LidgrenClient), "receiveHandshake"),
                prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_ReceiveHandshake))));

            // 3. Sincronización proactiva de tokens y filtro de lista blanca por IP antes de enviar la lista de peones
            TryPatch("GameServer.sendAvailableFarmhands", () => harmony.Patch(
                original: AccessTools.Method(typeof(GameServer), nameof(GameServer.sendAvailableFarmhands)),
                prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_SendAvailableFarmhands))
            ));

            // 4. Validación y reclamo autoritativo en el host (GameServer.checkFarmhandRequest)
            TryPatch("GameServer.checkFarmhandRequest", () => harmony.Patch(
                original: AccessTools.Method(typeof(GameServer), nameof(GameServer.checkFarmhandRequest)),
                prefix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Prefix_CheckFarmhandRequest))
            ));

            // 5. Bloqueo visual y control de pertenencia de ranuras en FarmhandMenu
            TryPatch("FarmhandSlot.ctor", () => harmony.Patch(
                original: AccessTools.Constructor(typeof(FarmhandMenu.FarmhandSlot), new[] { typeof(FarmhandMenu), typeof(Farmer) }),
                postfix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Postfix_FarmhandSlot_Ctor))
            ));

            // 6. Etiqueta visual "[De otro jugador]" en las ranuras bloqueadas
            TryPatch("FarmhandSlot.drawSlotName", () => harmony.Patch(
                original: AccessTools.Method(typeof(FarmhandMenu.FarmhandSlot), "drawSlotName"),
                postfix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Postfix_FarmhandSlot_DrawSlotName))
            ));

            // 7. Ordenación inteligente del menú de peones (personajes propios primero)
            TryPatch("FarmhandMenu.ctor", () => harmony.Patch(
                original: AccessTools.Constructor(typeof(FarmhandMenu), new[] { typeof(Client) }),
                postfix: new HarmonyMethod(typeof(ClientSecurityModule), nameof(Postfix_FarmhandMenu_Ctor))
            ));

            if (failures == 0)
                context?.Monitor.Log("[ClientSecurityModule] Parches Harmony de seguridad y posesión aplicados correctamente.", LogLevel.Trace);
        }

        // ─────────────────────────── Parches Harmony ───────────────────────────

        public static void Prefix_Client_SendPlayerIntroduction(Client __instance)
        {
            if (Game1.player == null)
                return;

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
            if (context == null || !Context.IsMainPlayer || !context.Config.RequirePairingWhitelist)
                return true;

            string clientIp = message.SenderEndPoint.Address.ToString();
            var challenge = serverChallenges.Issue(clientIp, DateTimeOffset.UtcNow, string.Empty);
            byte[] challengePayload = DeviceAuthWire.PackDiscoveryChallenge(challenge);

            NetOutgoingMessage response = __instance.server.CreateMessage();
            response.Write(Multiplayer.protocolVersion);
            response.Write("StardewValley");
            response.Write(challengePayload);
            __instance.server.SendDiscoveryResponse(response, message.SenderEndPoint);
            return false;
        }

        public static bool Prefix_ReceiveHandshake(LidgrenClient __instance, NetIncomingMessage msg)
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

        public static bool Prefix_SendAvailableFarmhands(
            string userId,
            string connectionId)
        {
            SyncServerTokensFromWorldState();

            if (context != null && Context.IsMainPlayer && context.Config.RequirePairingWhitelist)
            {
                string remoteIp = ResolveRemoteIpByConnectionId(connectionId);
                bool isLoopback = ClientPairingLogic.IsLoopbackIp(remoteIp);

                if (!isLoopback)
                {
                    if (!HasValidEarlyProof(connectionId, remoteIp, out string verifiedFingerprint))
                    {
                        RecordBlockedAttempt(remoteIp, userId);
                        DisconnectConnectionById(connectionId, "Acceso denegado: debes contar con un cliente actualizado y dispositivo autorizado.");
                        return false;
                    }

                    if (!FarmhandAuthLogic.CanAccessFarmhandList(context.WorldState, true, isLoopback, verifiedFingerprint, out string failureReason))
                    {
                        RecordBlockedAttempt(remoteIp, verifiedFingerprint);
                        DisconnectConnectionById(connectionId, failureReason);
                        return false;
                    }
                }
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

            long farmhandId = farmer.Value.UniqueMultiplayerID;
            if (!Game1.netWorldState.Value.farmhandData.TryGetValue(farmhandId, out Farmer? serverFarmer) || serverFarmer == null)
            {
                context?.Monitor.Log($"[ClientSecurity] El peón {farmhandId} pedido por la conexión {connectionId} ya no existe; se reenvía la lista.", LogLevel.Warn);
                __instance.sendAvailableFarmhands(userId, connectionId, sendMessage);
                return false;
            }

            if (context != null)
            {
                string remoteIp = ResolveRemoteIpByConnectionId(connectionId);
                bool isLoopback = ClientPairingLogic.IsLoopbackIp(remoteIp);

                if (context.Config.RequirePairingWhitelist && !isLoopback)
                {
                    if (!HasValidEarlyProof(connectionId, remoteIp, out string verifiedFingerprint))
                    {
                        context.Monitor.Log("[ClientSecurity] Rechazado intento de selección de ranura sin prueba de posesión de clave válida.", LogLevel.Warn);
                        DisconnectConnectionById(connectionId, "No se ha verificado la identidad del dispositivo.");
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
                    return true;
                }
            }

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
    }
}
