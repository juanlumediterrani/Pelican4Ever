using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Players;
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
    /// Estado persistido de forma inmediata en <c>Saves/&lt;Partida&gt;/pelican4ever_pairing.json</c>
    /// para que las invitaciones de 3 días y los emparejamientos sobrevivan aunque el proceso se reinicie
    /// antes del guardado diario de las 02:00.
    /// </summary>
    public sealed class PairingSidecarFile
    {
        public Dictionary<string, AuthorizedClient> AuthorizedClients { get; set; } = new(StringComparer.Ordinal);
        public List<PairingInvite> PairingInvites { get; set; } = new();
    }

    /// <summary>
    /// Módulo de seguridad, lista blanca por IP + código de emparejamiento (pairing) y protección contra usurpación de personajes.
    /// </summary>
    public sealed class ClientSecurityModule : IModule
    {
        private const string PairingSidecarFileName = "pelican4ever_pairing.json";
        private const int MaxRecentAttempts = 25;

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
        private static string localPairingPrompt = "Código de pairing (Pelican 4Ever):";
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
            ctx.AddCommand("p4e_pair_revoke", "Revoca una invitación pendiente o un dispositivo emparejado: p4e_pair_revoke <usuario_o_ip_o_id>", OnConsolePairRevoke);

            ApplyHarmonyPatches(ctx.Harmony);
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
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
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            try
            {
                string raw = File.ReadAllText(path);
                var sidecar = JsonSerializer.Deserialize<PairingSidecarFile>(raw);
                if (sidecar == null)
                    return;

                context.WorldState.AuthorizedClients ??= new Dictionary<string, AuthorizedClient>(StringComparer.Ordinal);
                if (sidecar.AuthorizedClients != null)
                {
                    foreach (var (token, client) in sidecar.AuthorizedClients)
                    {
                        if (!string.IsNullOrWhiteSpace(token) && client != null)
                        {
                            context.WorldState.AuthorizedClients[token] = client;
                        }
                    }
                }

                context.WorldState.PairingInvites ??= new List<PairingInvite>();
                if (sidecar.PairingInvites != null)
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    foreach (PairingInvite invite in sidecar.PairingInvites)
                    {
                        if (invite == null || invite.IsExpired(now))
                            continue;

                        bool exists = context.WorldState.PairingInvites.Any(i =>
                            string.Equals(i.Code, invite.Code, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(i.Alias, invite.Alias, StringComparison.OrdinalIgnoreCase));
                        if (!exists)
                        {
                            context.WorldState.PairingInvites.Add(invite);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[ClientSecurity] No se pudo leer {PairingSidecarFileName}: {ex.Message}", LogLevel.Warn);
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
                var sidecar = new PairingSidecarFile
                {
                    AuthorizedClients = new Dictionary<string, AuthorizedClient>(context.WorldState.AuthorizedClients, StringComparer.Ordinal),
                    PairingInvites = new List<PairingInvite>(context.WorldState.PairingInvites)
                };
                string json = JsonSerializer.Serialize(sidecar, new JsonSerializerOptions { WriteIndented = true });
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, overwrite: true);
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
                    ? "Código de pairing (Pelican 4Ever):"
                    : $"Código de pairing para {invite.Alias}:"
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
                    Game1.addHUDMessage(new HUDMessage("Verificando código de emparejamiento...", HUDMessage.newQuest_type));

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
                title: title,
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
                    localPairingPrompt = string.IsNullOrWhiteSpace(payload.PromptMessage)
                        ? "Código de pairing (Pelican 4Ever):"
                        : payload.PromptMessage;
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
                    Game1.addHUDMessage(new HUDMessage(payload.Message, HUDMessage.achievement_type));
                }
                else
                {
                    Game1.playSound("cancel");
                    Game1.addHUDMessage(new HUDMessage(payload.Message, HUDMessage.error_type));
                    if (payload.RemainingAttempts > 0)
                    {
                        localClientAwaitingPairing = true;
                        localPairingPrompt = payload.Message;
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
                        Message = $"{message} Te quedan {remaining} intento(s)."
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
                        Message = "Has agotado los intentos de emparejamiento. Desconectando..."
                    },
                    ModMessages.PairingResult,
                    new[] { context.Manifest.UniqueID },
                    new[] { playerId }
                );

                context.Helper.Events.GameLoop.UpdateTicked += KickNextTick;
                void KickNextTick(object? s, UpdateTickedEventArgs args)
                {
                    context.Helper.Events.GameLoop.UpdateTicked -= KickNextTick;
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
        /// Sincroniza serverFarmer.userID.Value con PlayerRecord.OwnerToken en el host.
        /// Necesario para que sendAvailableFarmhands transmita el token en la red nativa.
        /// </summary>
        public static void SyncServerTokensFromWorldState()
        {
            if (context == null || !Context.IsMainPlayer || Game1.netWorldState?.Value?.farmhandData == null)
                return;

            foreach (var (playerId, playerRecord) in context.WorldState.Players)
            {
                if (!string.IsNullOrWhiteSpace(playerRecord.OwnerToken)
                    && Game1.netWorldState.Value.farmhandData.TryGetValue(playerId, out Farmer? f)
                    && f != null)
                {
                    f.userID.Value = playerRecord.OwnerToken;
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

            var clients = context.WorldState.AuthorizedClients.Values.OrderBy(c => c.Alias, StringComparer.OrdinalIgnoreCase).ToList();
            context.Monitor.Log($"Dispositivos emparejados ({clients.Count}):", LogLevel.Info);
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
            string token = ClientIdentity.CurrentToken;
            if (!string.IsNullOrWhiteSpace(token) && Game1.player != null)
            {
                Game1.player.userID.Value = token;
            }
        }

        public static bool Prefix_Client_GetUserID(ref string __result)
        {
            string token = ClientIdentity.CurrentToken;
            if (!string.IsNullOrWhiteSpace(token))
            {
                __result = token;
                return false;
            }
            return true;
        }

        public static bool Prefix_LidgrenClient_GetUserID(ref string __result) => Prefix_Client_GetUserID(ref __result);

        public static bool Prefix_SendAvailableFarmhands(
            string userId,
            string connectionId)
        {
            SyncServerTokensFromWorldState();

            if (context != null && Context.IsMainPlayer && context.Config.RequirePairingWhitelist)
            {
                string remoteIp = ResolveRemoteIpByConnectionId(connectionId);
                LobbyAccessResult access = ClientPairingLogic.EvaluateLobbyAccess(
                    context.WorldState,
                    context.Config.RequirePairingWhitelist,
                    userId,
                    remoteIp,
                    DateTimeOffset.UtcNow);

                if (access == LobbyAccessResult.DeniedNoInvite)
                {
                    RecordBlockedAttempt(remoteIp, userId);
                    DisconnectConnectionById(connectionId, "Servidor protegido por lista blanca. Solicita acceso y código de pairing en Discord.");
                    return false;
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
                string incomingToken = !string.IsNullOrWhiteSpace(farmer.Value.userID.Value)
                    ? farmer.Value.userID.Value
                    : (userId ?? string.Empty);
                string remoteIp = ResolveRemoteIpByConnectionId(connectionId);
                if (!string.IsNullOrEmpty(remoteIp))
                {
                    playerRemoteIps[farmhandId] = remoteIp;
                }

                LobbyAccessResult lobbyAccess = ClientPairingLogic.EvaluateLobbyAccess(
                    context.WorldState,
                    context.Config.RequirePairingWhitelist,
                    incomingToken,
                    remoteIp,
                    DateTimeOffset.UtcNow);

                if (lobbyAccess == LobbyAccessResult.DeniedNoInvite)
                {
                    RecordBlockedAttempt(remoteIp, incomingToken);
                    DisconnectConnectionById(connectionId, "Servidor protegido por lista blanca. Solicita acceso y código de pairing en Discord.");
                    return false;
                }

                var playerRecord = context.WorldState.GetOrCreatePlayer(farmhandId, serverFarmer.Name);

                // Si el peón tiene OwnerToken en WorldState (la fuente de verdad), sincronizar serverFarmer.userID
                if (!string.IsNullOrEmpty(playerRecord.OwnerToken))
                {
                    serverFarmer.userID.Value = playerRecord.OwnerToken;
                }

                if (lobbyAccess == LobbyAccessResult.AllowedPendingPairing)
                {
                    // El cliente entra por invitación de IP pero aún debe introducir el código de pairing:
                    // no puede entrar en un peón ya reclamado por otro jugador, y no se reclama definitivamente hasta validar el código.
                    if (PlayerAuthLogic.IsClaimed(playerRecord) &&
                        !string.Equals(playerRecord.OwnerToken, incomingToken.Trim(), StringComparison.Ordinal))
                    {
                        context.Monitor.Log(
                            $"[ClientSecurity] RECHAZADO: Conexión en emparejamiento {connectionId} ({remoteIp}) intentó usar el peón ya reclamado {farmhandId}.",
                            LogLevel.Warn);
                        __instance.sendAvailableFarmhands(userId, connectionId, sendMessage);
                        return false;
                    }

                    pendingPairingSessions[farmhandId] = new PendingPairingSession
                    {
                        PlayerId = farmhandId,
                        ClientToken = incomingToken.Trim(),
                        RemoteIp = remoteIp,
                        StartedAtUtc = DateTimeOffset.UtcNow,
                        RemainingAttempts = ClientPairingLogic.MaxPairingAttempts
                    };

                    context.Monitor.Log(
                        $"[ClientSecurity] Conexión {connectionId} ({remoteIp}) admitida en fase de emparejamiento para el peón {farmhandId}; esperando código de pairing.",
                        LogLevel.Info);

                    userId = incomingToken;
                    return true;
                }

                pendingPairingSessions.Remove(farmhandId);

                var authResult = PlayerAuthLogic.ValidateOrClaim(playerRecord, incomingToken);
                if (authResult == AuthResult.DeniedMismatch || authResult == AuthResult.DeniedNoToken)
                {
                    context.Monitor.Log(
                        $"[ClientSecurity] RECHAZADO: Conexión {connectionId} intentó entrar al peón {farmhandId} ('{serverFarmer.Name}') " +
                        $"con token '{PlayerAuthLogic.MaskToken(incomingToken)}', pero pertenece a '{PlayerAuthLogic.MaskToken(playerRecord.OwnerToken)}'.",
                        LogLevel.Warn);

                    __instance.sendAvailableFarmhands(userId, connectionId, sendMessage);
                    return false;
                }

                if (authResult == AuthResult.Claimed)
                {
                    serverFarmer.userID.Value = incomingToken;
                    ClientPairingLogic.EnsureExistingClaimsAuthorized(context.WorldState, DateTimeOffset.UtcNow);
                    SavePairingSidecar();
                    context.Monitor.Log(
                        $"[ClientSecurity] Peón {farmhandId} ('{serverFarmer.Name}') reclamado y vinculado al dispositivo {PlayerAuthLogic.MaskToken(incomingToken)}.",
                        LogLevel.Info);
                }

                // Propagar el token al argumento userId
                userId = incomingToken;
            }

            return true;
        }

        public static void Postfix_FarmhandSlot_Ctor(FarmhandMenu.FarmhandSlot __instance, Farmer farmer)
        {
            string myToken = ClientIdentity.CurrentToken;
            if (farmer != null)
            {
                string slotToken = farmer.userID.Value ?? string.Empty;
                bool belongsToAnother = PlayerAuthLogic.BelongsToAnotherClient(slotToken, myToken);

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
