using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Pelican4Ever.Core.Localization;
using Pelican4Ever.Core.World;

namespace Pelican4Ever.Core.Players
{
    /// <summary>Resultado de evaluar el acceso inicial de una conexión al lobby de peones.</summary>
    public enum LobbyAccessResult
    {
        /// <summary>El cliente ya está emparejado (o la lista blanca está desactivada / es loopback).</summary>
        AllowedPaired,

        /// <summary>El cliente aún no está emparejado, pero su IP tiene una invitación activa pendiente de código.</summary>
        AllowedPendingPairing,

        /// <summary>El cliente no está emparejado y su IP no tiene ninguna invitación activa.</summary>
        DeniedNoInvite
    }

    /// <summary>Resultado de validar y canjear un código de emparejamiento dentro del juego.</summary>
    public enum PairingRedeemResult
    {
        Success,
        InvalidCode,
        IpMismatch,
        ExpiredOrNoInvite,
        MissingToken
    }

    /// <summary>
    /// Invitación temporal de emparejamiento para dar de alta a un nuevo jugador desde una IP concreta.
    /// Por defecto caduca a los 3 días (72 horas) y es de un solo uso.
    /// </summary>
    public sealed class PairingInvite
    {
        public string Alias { get; set; } = string.Empty;
        public string AllowedIp { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset ExpiresAtUtc { get; set; } = DateTimeOffset.UtcNow.AddHours(ClientPairingLogic.DefaultInviteTtlHours);

        public bool IsExpired(DateTimeOffset nowUtc) => nowUtc > ExpiresAtUtc;
    }

    /// <summary>
    /// Cliente emparejado y autorizado en la lista blanca del servidor.
    /// </summary>
    public sealed class AuthorizedClient
    {
        public string ClientToken { get; set; } = string.Empty;
        public string Alias { get; set; } = string.Empty;
        public string PairedIp { get; set; } = string.Empty;
        public string LastSeenIp { get; set; } = string.Empty;
        public long BoundPlayerId { get; set; }
        public DateTimeOffset PairedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Registro en memoria de un intento reciente de conexión para diagnóstico y alta rápida.
    /// </summary>
    public sealed class RecentConnectionAttempt
    {
        public string RemoteIp { get; set; } = string.Empty;
        public string MaskedToken { get; set; } = string.Empty;
        public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
        public string Outcome { get; set; } = string.Empty;
    }

    /// <summary>
    /// Lógica pura de dominio para la lista blanca por IP + código de emparejamiento (pairing) de un solo uso.
    /// </summary>
    public static class ClientPairingLogic
    {
        /// <summary>Caducidad por defecto de una invitación asíncrona: 72 horas (3 días).</summary>
        public const int DefaultInviteTtlHours = 72;

        /// <summary>Longitud del código de emparejamiento.</summary>
        public const int CodeLength = 6;

        /// <summary>Intentos máximos para introducir el código antes de expulsar la conexión.</summary>
        public const int MaxPairingAttempts = 3;

        /// <summary>Minutos máximos que un cliente puede permanecer sin completar el emparejamiento.</summary>
        public const int PairingSessionTimeoutMinutes = 5;

        // Alfabeto sin caracteres ambiguos (sin 0, O, 1, I, L)
        private const string CodeAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

        /// <summary>Normaliza una dirección IP (elimina puerto si viene en formato endpoint y convierte IPv4 mapeada en IPv6).</summary>
        public static string NormalizeIp(string? rawIp)
        {
            if (string.IsNullOrWhiteSpace(rawIp))
                return string.Empty;

            string candidate = rawIp.Trim();

            if (IPEndPoint.TryParse(candidate, out IPEndPoint? ep))
            {
                IPAddress addr = ep.Address.IsIPv4MappedToIPv6 ? ep.Address.MapToIPv4() : ep.Address;
                return addr.ToString();
            }

            if (IPAddress.TryParse(candidate, out IPAddress? parsed))
            {
                return (parsed.IsIPv4MappedToIPv6 ? parsed.MapToIPv4() : parsed).ToString();
            }

            return candidate;
        }

        /// <summary>Indica si la dirección corresponde a loopback local (el propio host o el arnés local).</summary>
        public static bool IsLoopbackIp(string? rawIp)
        {
            if (string.IsNullOrWhiteSpace(rawIp))
                return false;

            string trimmed = rawIp.Trim();
            if (string.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase))
                return true;

            string norm = NormalizeIp(trimmed);
            return IPAddress.TryParse(norm, out IPAddress? addr) && IPAddress.IsLoopback(addr);
        }

        /// <summary>Valida que una cadena sea una dirección IP pública/privada concreta válida para una invitación.</summary>
        public static bool IsValidInviteIp(string? rawIp, out string normalizedIp, out string? error)
        {
            normalizedIp = string.Empty;
            error = null;

            if (string.IsNullOrWhiteSpace(rawIp))
            {
                error = "La dirección IP no puede estar vacía.";
                return false;
            }

            string trimmed = rawIp.Trim();
            if (!IPAddress.TryParse(trimmed, out IPAddress? addr))
            {
                error = $"'{trimmed}' no es una dirección IPv4 o IPv6 válida.";
                return false;
            }

            if (addr.IsIPv4MappedToIPv6)
                addr = addr.MapToIPv4();

            if (IPAddress.Any.Equals(addr) || IPAddress.IPv6Any.Equals(addr) || IPAddress.Broadcast.Equals(addr))
            {
                error = $"La dirección IP '{addr}' no puede ser un comodín.";
                return false;
            }

            if (addr.AddressFamily != AddressFamily.InterNetwork && addr.AddressFamily != AddressFamily.InterNetworkV6)
            {
                error = $"Familia de dirección no soportada para '{addr}'.";
                return false;
            }

            normalizedIp = addr.ToString();
            return true;
        }

        /// <summary>Genera un código de emparejamiento aleatorio legible de 6 caracteres.</summary>
        public static string GeneratePairingCode(Random? rng = null)
        {
            char[] chars = new char[CodeLength];
            for (int i = 0; i < CodeLength; i++)
            {
                int idx = rng != null
                    ? rng.Next(CodeAlphabet.Length)
                    : RandomNumberGenerator.GetInt32(CodeAlphabet.Length);
                chars[i] = CodeAlphabet[idx];
            }
            return new string(chars);
        }

        /// <summary>
        /// Garantiza que cualquier jugador que ya tuviera un <see cref="PlayerRecord.OwnerToken"/> reclamado en la partida
        /// conste automáticamente como cliente autorizado al cargar la partida (retrocompatibilidad sin perder acceso).
        /// </summary>
        public static int EnsureExistingClaimsAuthorized(ServerWorldState state, DateTimeOffset nowUtc)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            state.AuthorizedClients ??= new Dictionary<string, AuthorizedClient>(StringComparer.Ordinal);
            int added = 0;

            foreach (PlayerRecord player in state.Players.Values)
            {
                if (string.IsNullOrWhiteSpace(player.OwnerToken))
                    continue;

                string token = player.OwnerToken.Trim();
                if (!state.AuthorizedClients.TryGetValue(token, out AuthorizedClient? existing))
                {
                    state.AuthorizedClients[token] = new AuthorizedClient
                    {
                        ClientToken = token,
                        Alias = string.IsNullOrWhiteSpace(player.PlayerName) ? $"Jugador_{player.UniqueMultiplayerID}" : player.PlayerName,
                        BoundPlayerId = player.UniqueMultiplayerID,
                        PairedAtUtc = player.LastSeenUtc != DateTimeOffset.MinValue ? player.LastSeenUtc : nowUtc
                    };
                    added++;
                }
                else if (existing.BoundPlayerId == 0)
                {
                    existing.BoundPlayerId = player.UniqueMultiplayerID;
                }
            }

            return added;
        }

        /// <summary>Elimina las invitaciones de emparejamiento cuya fecha de caducidad haya vencido.</summary>
        public static int PurgeExpiredInvites(ServerWorldState state, DateTimeOffset nowUtc)
        {
            if (state?.PairingInvites == null || state.PairingInvites.Count == 0)
                return 0;

            return state.PairingInvites.RemoveAll(i => i == null || i.IsExpired(nowUtc));
        }

        /// <summary>
        /// Crea o renueva una invitación de emparejamiento para <paramref name="alias"/> e <paramref name="rawIp"/>
        /// con una validez de <paramref name="ttl"/> (por defecto 3 días).
        /// </summary>
        public static PairingInvite CreateInvite(
            ServerWorldState state,
            string alias,
            string rawIp,
            TimeSpan ttl,
            DateTimeOffset nowUtc,
            string? explicitCode = null)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrWhiteSpace(alias))
                throw new ArgumentException("El alias del usuario es obligatorio.", nameof(alias));
            if (!IsValidInviteIp(rawIp, out string normalizedIp, out string? ipError))
                throw new ArgumentException(ipError ?? "IP no válida.", nameof(rawIp));
            if (ttl <= TimeSpan.Zero)
                ttl = TimeSpan.FromHours(DefaultInviteTtlHours);

            state.PairingInvites ??= new List<PairingInvite>();
            PurgeExpiredInvites(state, nowUtc);

            string cleanAlias = alias.Trim();

            // Si ya había una invitación pendiente para ese mismo alias o la misma IP, se reemplaza por la nueva
            state.PairingInvites.RemoveAll(i =>
                string.Equals(i.Alias, cleanAlias, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(i.AllowedIp, normalizedIp, StringComparison.OrdinalIgnoreCase));

            string code = string.IsNullOrWhiteSpace(explicitCode)
                ? GeneratePairingCode()
                : explicitCode.Trim().ToUpperInvariant();

            // Evitar colisión de código con otra invitación activa
            int guard = 0;
            while (string.IsNullOrWhiteSpace(explicitCode)
                   && state.PairingInvites.Any(i => string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase))
                   && ++guard < 20)
            {
                code = GeneratePairingCode();
            }

            var invite = new PairingInvite
            {
                Alias = cleanAlias,
                AllowedIp = normalizedIp,
                Code = code,
                CreatedAtUtc = nowUtc,
                ExpiresAtUtc = nowUtc.Add(ttl)
            };

            state.PairingInvites.Add(invite);
            return invite;
        }

        /// <summary>Comprueba si un token de cliente ya está emparejado y autorizado en el servidor.</summary>
        public static bool IsClientTokenAuthorized(
            ServerWorldState state,
            string? clientToken,
            out AuthorizedClient? authorized)
        {
            authorized = null;
            if (state == null || string.IsNullOrWhiteSpace(clientToken))
                return false;

            string trimmed = clientToken.Trim();
            if (state.AuthorizedClients != null && state.AuthorizedClients.TryGetValue(trimmed, out authorized) && authorized != null)
                return true;

            if (state.AuthorizedDevices != null && state.AuthorizedDevices.TryGetValue(trimmed, out var dev) && dev != null && dev.Status == Security.DeviceStatus.Active)
            {
                authorized = new AuthorizedClient
                {
                    ClientToken = dev.Fingerprint,
                    Alias = dev.Alias,
                    BoundPlayerId = dev.BoundPlayerId,
                    LastSeenIp = dev.LastSeenIp,
                    PairedAtUtc = dev.CreatedAtUtc
                };
                return true;
            }

            // Respaldo: jugador ya reclamado en Players
            PlayerRecord? claimed = state.Players?.Values.FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(p.OwnerToken) &&
                string.Equals(p.OwnerToken.Trim(), trimmed, StringComparison.Ordinal));

            if (claimed != null)
            {
                authorized = new AuthorizedClient
                {
                    ClientToken = trimmed,
                    Alias = claimed.PlayerName,
                    BoundPlayerId = claimed.UniqueMultiplayerID
                };
                state.AuthorizedClients ??= new Dictionary<string, AuthorizedClient>(StringComparer.Ordinal);
                state.AuthorizedClients[trimmed] = authorized;
                return true;
            }

            return false;
        }

        /// <summary>Busca una invitación activa (no caducada) para la dirección IP indicada.</summary>
        public static PairingInvite? FindActiveInviteForIp(ServerWorldState state, string? remoteIp, DateTimeOffset nowUtc)
        {
            if (state?.PairingInvites == null || string.IsNullOrWhiteSpace(remoteIp))
                return null;

            string normIp = NormalizeIp(remoteIp);
            if (string.IsNullOrEmpty(normIp))
                return null;

            return state.PairingInvites.FirstOrDefault(i =>
                i != null &&
                !i.IsExpired(nowUtc) &&
                string.Equals(NormalizeIp(i.AllowedIp), normIp, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Evalúa si una conexión entrante puede ver/seleccionar peones en el lobby.
        /// </summary>
        public static LobbyAccessResult EvaluateLobbyAccess(
            ServerWorldState state,
            bool requireWhitelist,
            string? clientToken,
            string? remoteIp,
            DateTimeOffset nowUtc)
        {
            if (!requireWhitelist || IsLoopbackIp(remoteIp))
                return LobbyAccessResult.AllowedPaired;

            if (state == null)
                return LobbyAccessResult.DeniedNoInvite;

            PurgeExpiredInvites(state, nowUtc);

            if (IsClientTokenAuthorized(state, clientToken, out AuthorizedClient? authorized) && authorized != null)
            {
                string normIp = NormalizeIp(remoteIp);
                if (!string.IsNullOrEmpty(normIp))
                    authorized.LastSeenIp = normIp;
                return LobbyAccessResult.AllowedPaired;
            }

            if (FindActiveInviteForIp(state, remoteIp, nowUtc) != null)
                return LobbyAccessResult.AllowedPendingPairing;

            return LobbyAccessResult.DeniedNoInvite;
        }

        /// <summary>
        /// Valida y canjea un código de emparejamiento enviado por un cliente al entrar por primera vez.
        /// Si es válido, registra el dispositivo en <see cref="ServerWorldState.AuthorizedClients"/>, vincula el peón
        /// y consume la invitación (un solo uso).
        /// </summary>
        public static PairingRedeemResult TryRedeemInvite(
            ServerWorldState state,
            long playerId,
            string playerName,
            string? clientToken,
            string? remoteIp,
            string? submittedCode,
            DateTimeOffset nowUtc,
            out AuthorizedClient? authorized,
            out string message)
        {
            authorized = null;
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (string.IsNullOrWhiteSpace(clientToken))
            {
                message = CoreI18n.Get("security.pairing_invalid_device");
                return PairingRedeemResult.MissingToken;
            }

            string trimmedToken = clientToken.Trim();
            string normIp = NormalizeIp(remoteIp);
            string cleanCode = (submittedCode ?? string.Empty).Trim().ToUpperInvariant();

            PurgeExpiredInvites(state, nowUtc);

            if (string.IsNullOrEmpty(cleanCode))
            {
                message = CoreI18n.Get("security.pairing_code_empty");
                return PairingRedeemResult.InvalidCode;
            }

            PairingInvite? inviteByCode = state.PairingInvites?.FirstOrDefault(i =>
                i != null &&
                !i.IsExpired(nowUtc) &&
                string.Equals(i.Code, cleanCode, StringComparison.OrdinalIgnoreCase));

            if (inviteByCode == null)
            {
                PairingInvite? inviteForIp = FindActiveInviteForIp(state, normIp, nowUtc);
                if (inviteForIp == null)
                {
                    message = CoreI18n.Get("security.pairing_no_active_invite");
                    return PairingRedeemResult.ExpiredOrNoInvite;
                }

                message = CoreI18n.Get("security.pairing_incorrect_code");
                return PairingRedeemResult.InvalidCode;
            }

            // Verificar que la IP de conexión coincide con la IP autorizada en la invitación (salvo loopback en pruebas locales)
            if (!IsLoopbackIp(normIp) &&
                !string.IsNullOrEmpty(normIp) &&
                !string.Equals(NormalizeIp(inviteByCode.AllowedIp), normIp, StringComparison.OrdinalIgnoreCase))
            {
                message = CoreI18n.Get("security.pairing_ip_mismatch");
                return PairingRedeemResult.IpMismatch;
            }

            // Consumir invitación de un solo uso
            state.PairingInvites!.Remove(inviteByCode);

            state.AuthorizedClients ??= new Dictionary<string, AuthorizedClient>(StringComparer.Ordinal);
            authorized = new AuthorizedClient
            {
                ClientToken = trimmedToken,
                Alias = inviteByCode.Alias,
                PairedIp = string.IsNullOrEmpty(normIp) ? inviteByCode.AllowedIp : normIp,
                LastSeenIp = string.IsNullOrEmpty(normIp) ? inviteByCode.AllowedIp : normIp,
                BoundPlayerId = playerId,
                PairedAtUtc = nowUtc
            };
            state.AuthorizedClients[trimmedToken] = authorized;

            PlayerRecord record = state.GetOrCreatePlayer(playerId, string.IsNullOrWhiteSpace(playerName) ? inviteByCode.Alias : playerName);
            record.OwnerToken = trimmedToken;

            message = CoreI18n.Get("security.pairing_success", new { alias = inviteByCode.Alias });
            return PairingRedeemResult.Success;
        }

        /// <summary>
        /// Revoca invitaciones pendientes y/o dispositivos autorizados que coincidan con el alias, IP, código,
        /// nombre de jugador o ID de jugador indicados.
        /// </summary>
        public static bool Revoke(
            ServerWorldState state,
            string? target,
            out List<long> affectedPlayerIds,
            out string summary)
        {
            affectedPlayerIds = new List<long>();
            if (state == null || string.IsNullOrWhiteSpace(target))
            {
                summary = "Debes especificar un usuario, IP, código o ID a revocar.";
                return false;
            }

            string clean = target.Trim();
            string normIp = NormalizeIp(clean);
            int removedInvites = 0;
            int removedClients = 0;

            if (state.PairingInvites != null)
            {
                removedInvites = state.PairingInvites.RemoveAll(i =>
                    i != null && (
                        string.Equals(i.Alias, clean, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(i.Code, clean, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(normIp) && string.Equals(NormalizeIp(i.AllowedIp), normIp, StringComparison.OrdinalIgnoreCase))));
            }

            bool parsedId = long.TryParse(clean, out long targetPlayerId);

            if (state.AuthorizedDevices != null)
            {
                foreach (var kvp in state.AuthorizedDevices.ToList())
                {
                    var d = kvp.Value;
                    bool match =
                        string.Equals(d.Alias, clean, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(d.Fingerprint, clean, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(normIp) && string.Equals(NormalizeIp(d.LastSeenIp), normIp, StringComparison.OrdinalIgnoreCase)) ||
                        (parsedId && d.BoundPlayerId == targetPlayerId);

                    if (match)
                    {
                        d.Status = Security.DeviceStatus.Revoked;
                        removedClients++;
                        if (d.BoundPlayerId != 0 && !affectedPlayerIds.Contains(d.BoundPlayerId))
                            affectedPlayerIds.Add(d.BoundPlayerId);
                    }
                }
            }

            if (state.AuthorizedClients != null)
            {
                foreach (var kvp in state.AuthorizedClients.ToList())
                {
                    AuthorizedClient c = kvp.Value;
                    bool match =
                        string.Equals(c.Alias, clean, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(c.ClientToken, clean, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(normIp) && (
                            string.Equals(NormalizeIp(c.PairedIp), normIp, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(NormalizeIp(c.LastSeenIp), normIp, StringComparison.OrdinalIgnoreCase))) ||
                        (parsedId && c.BoundPlayerId == targetPlayerId);

                    if (!match && c.BoundPlayerId != 0 && state.Players.TryGetValue(c.BoundPlayerId, out PlayerRecord? boundRec))
                    {
                        match = string.Equals(boundRec.PlayerName, clean, StringComparison.OrdinalIgnoreCase);
                    }

                    if (match)
                    {
                        state.AuthorizedClients.Remove(kvp.Key);
                        removedClients++;
                        if (c.BoundPlayerId != 0 && !affectedPlayerIds.Contains(c.BoundPlayerId))
                            affectedPlayerIds.Add(c.BoundPlayerId);

                        foreach (PlayerRecord p in state.Players.Values.Where(p => string.Equals(p.OwnerToken, kvp.Key, StringComparison.Ordinal)))
                        {
                            PlayerAuthLogic.ResetClaim(p);
                            if (!affectedPlayerIds.Contains(p.UniqueMultiplayerID))
                                affectedPlayerIds.Add(p.UniqueMultiplayerID);
                        }
                    }
                }
            }

            foreach (PlayerRecord p in state.Players.Values)
            {
                if (!string.IsNullOrWhiteSpace(p.OwnerToken) &&
                    ((parsedId && p.UniqueMultiplayerID == targetPlayerId) ||
                     string.Equals(p.PlayerName, clean, StringComparison.OrdinalIgnoreCase)))
                {
                    if (state.AuthorizedClients != null && state.AuthorizedClients.Remove(p.OwnerToken.Trim()))
                        removedClients++;
                    PlayerAuthLogic.ResetClaim(p);
                    if (!affectedPlayerIds.Contains(p.UniqueMultiplayerID))
                        affectedPlayerIds.Add(p.UniqueMultiplayerID);
                }
            }

            if (removedInvites == 0 && removedClients == 0 && affectedPlayerIds.Count == 0)
            {
                summary = $"No se encontró ninguna invitación ni dispositivo emparejado para '{clean}'.";
                return false;
            }

            summary = $"Revocado '{clean}': {removedInvites} invitación(es) pendiente(s) y {removedClients} dispositivo(s) emparejado(s) eliminados.";
            return true;
        }
    }
}
