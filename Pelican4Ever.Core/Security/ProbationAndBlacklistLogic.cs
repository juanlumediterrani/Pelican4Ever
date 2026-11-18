using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;

namespace Pelican4Ever.Core.Security
{
    /// <summary>Registro permanente en lista negra; solo el propietario puede revocarlo.</summary>
    public sealed class BlacklistEntry
    {
        public string Fingerprint { get; set; } = string.Empty;
        public string DiscordUserId { get; set; } = string.Empty;
        public string Alias { get; set; } = string.Empty;
        public string LastKnownIp { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string BlacklistedBy { get; set; } = "putre";
        public DateTimeOffset BlacklistedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>Ticket temporal abierto por Putre para que un candidato de Discord entregue su huella ECDSA.</summary>
    public sealed class PutreAdmissionTicket
    {
        public string DiscordUserId { get; set; } = string.Empty;
        public string Alias { get; set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset ExpiresAtUtc { get; set; } = DateTimeOffset.UtcNow.Add(ProbationAndBlacklistLogic.DefaultTicketTtl);

        public bool IsExpired(DateTimeOffset nowUtc) => nowUtc > ExpiresAtUtc;
    }

    /// <summary>Intento reciente de conexión no autorizada con firma ECDSA válida (en memoria, sin IP pública en Discord).</summary>
    public sealed class UnpairedHandshakeObservation
    {
        public string Fingerprint { get; set; } = string.Empty;
        public string PublicKeyBase64 { get; set; } = string.Empty;
        public string RemoteIp { get; set; } = string.Empty;
        public DateTimeOffset ObservedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>Métricas de comportamiento acumuladas por el host para descartar uso indebido.</summary>
    public sealed class PlayerBehaviorMetrics
    {
        public int VisitorDeniedCount { get; set; }
        public string LastVisitorDeniedSummary { get; set; } = string.Empty;
        public DateTimeOffset LastVisitorDeniedAtUtc { get; set; } = DateTimeOffset.MinValue;
        public int ChatMessageCount { get; set; }
        public int ChatFloodHits { get; set; }
        public int ChatTagInjectionHits { get; set; }
        public int ConnectCount { get; set; }
        public long TotalOnlineSeconds { get; set; }
        public DateTimeOffset LastConnectedUtc { get; set; } = DateTimeOffset.MinValue;
        public int RolloversPassed { get; set; }
        public int RolloversPenalized { get; set; }
    }

    /// <summary>
    /// Reglas puras para admisión de Putre con límite de 2 invitados en observación simultáneos y lista negra irrevocable por Putre.
    /// </summary>
    public static class ProbationAndBlacklistLogic
    {
        public const int MaxConcurrentPutreSlots = 2;
        public const int MaxRecentUnpairedObservations = 32;
        public static readonly TimeSpan DefaultTicketTtl = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan UnpairedObservationTtl = TimeSpan.FromMinutes(15);

        private static readonly Regex DiscordIdPattern = new(@"^\d{15,22}$", RegexOptions.Compiled);
        private static readonly Regex AliasPattern = new(@"^[A-Za-z0-9_.-]{2,24}$", RegexOptions.Compiled);
        private static readonly Regex HexPrefixPattern = new(@"^[0-9a-fA-F]{8,64}$", RegexOptions.Compiled);

        public static bool IsValidDiscordId(string? value) =>
            !string.IsNullOrWhiteSpace(value) && DiscordIdPattern.IsMatch(value.Trim());

        public static bool IsValidAlias(string? value) =>
            !string.IsNullOrWhiteSpace(value) && AliasPattern.IsMatch(value.Trim());

        public static bool IsValidFingerprintPrefix(string? value) =>
            !string.IsNullOrWhiteSpace(value) && HexPrefixPattern.IsMatch(value.Trim());

        public static int PurgeExpiredTickets(ServerWorldState state, DateTimeOffset nowUtc)
        {
            if (state?.PutreTickets == null || state.PutreTickets.Count == 0)
                return 0;

            return state.PutreTickets.RemoveAll(t => t == null || t.IsExpired(nowUtc));
        }

        public static bool IsBlacklisted(
            ServerWorldState state,
            string? fingerprint,
            string? discordUserId,
            out BlacklistEntry? entry)
        {
            entry = null;
            if (state == null)
                return false;

            string cleanFp = (fingerprint ?? string.Empty).Trim();
            string cleanDiscord = (discordUserId ?? string.Empty).Trim();

            if (state.Blacklist != null)
            {
                foreach (BlacklistEntry candidate in state.Blacklist)
                {
                    if (candidate == null)
                        continue;

                    if (!string.IsNullOrEmpty(cleanFp) &&
                        !string.IsNullOrEmpty(candidate.Fingerprint) &&
                        string.Equals(candidate.Fingerprint, cleanFp, StringComparison.OrdinalIgnoreCase))
                    {
                        entry = candidate;
                        return true;
                    }

                    if (!string.IsNullOrEmpty(cleanDiscord) &&
                        !string.IsNullOrEmpty(candidate.DiscordUserId) &&
                        string.Equals(candidate.DiscordUserId, cleanDiscord, StringComparison.Ordinal))
                    {
                        entry = candidate;
                        return true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(cleanFp) &&
                state.AuthorizedDevices != null &&
                state.AuthorizedDevices.TryGetValue(cleanFp, out AuthorizedDevice? dev) &&
                dev != null &&
                dev.Status == DeviceStatus.Blacklisted)
            {
                entry = new BlacklistEntry
                {
                    Fingerprint = dev.Fingerprint,
                    DiscordUserId = dev.DiscordUserId,
                    Alias = dev.Alias,
                    LastKnownIp = dev.LastSeenIp,
                    Reason = "Dispositivo marcado como Blacklisted",
                    BlacklistedBy = string.IsNullOrWhiteSpace(dev.InvitedBy) ? "owner" : dev.InvitedBy
                };
                return true;
            }

            return false;
        }

        /// <summary>
        /// Cuenta los huecos ocupados por Putre (tickets vigentes + dispositivos activos en observación).
        /// Los dispositivos graduados o en lista negra ya no consumen hueco.
        /// </summary>
        public static int CountPutreActiveSlots(ServerWorldState state, DateTimeOffset nowUtc)
        {
            if (state == null)
                return 0;

            PurgeExpiredTickets(state, nowUtc);
            int tickets = state.PutreTickets?.Count(t => t != null && !t.IsExpired(nowUtc)) ?? 0;
            int observing = state.AuthorizedDevices?.Values.Count(d =>
                d != null &&
                string.Equals(d.InvitedBy, "putre", StringComparison.OrdinalIgnoreCase) &&
                d.Status == DeviceStatus.Active &&
                d.ProbationStage == ProbationStage.Observing) ?? 0;

            return tickets + observing;
        }

        public static bool OpenTicket(
            ServerWorldState state,
            string discordUserId,
            string alias,
            DateTimeOffset nowUtc,
            out PutreAdmissionTicket? ticket,
            out string message)
        {
            ticket = null;
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            string cleanDiscord = (discordUserId ?? string.Empty).Trim();
            string cleanAlias = (alias ?? string.Empty).Trim();

            if (!IsValidDiscordId(cleanDiscord))
            {
                message = "El identificador de Discord no es válido.";
                return false;
            }

            if (!IsValidAlias(cleanAlias))
            {
                message = "El alias debe tener entre 2 y 24 caracteres [A-Za-z0-9_.-].";
                return false;
            }

            if (IsBlacklisted(state, null, cleanDiscord, out BlacklistEntry? bl))
            {
                message = $"Usuario de Discord en lista negra ({bl?.Reason ?? "sin motivo"}). Solo el propietario puede retirarla.";
                return false;
            }

            state.PutreTickets ??= new List<PutreAdmissionTicket>();
            PurgeExpiredTickets(state, nowUtc);

            // Si ya tiene un dispositivo activo invitado por Putre
            AuthorizedDevice? existingDev = state.AuthorizedDevices?.Values.FirstOrDefault(d =>
                d != null &&
                d.Status == DeviceStatus.Active &&
                string.Equals(d.DiscordUserId, cleanDiscord, StringComparison.Ordinal));
            if (existingDev != null)
            {
                message = $"Este usuario de Discord ya tiene el dispositivo '{existingDev.Alias}' autorizado.";
                return false;
            }

            PutreAdmissionTicket? existingTicket = state.PutreTickets.FirstOrDefault(t =>
                t != null && string.Equals(t.DiscordUserId, cleanDiscord, StringComparison.Ordinal));
            if (existingTicket != null)
            {
                existingTicket.Alias = cleanAlias;
                existingTicket.ExpiresAtUtc = nowUtc.Add(DefaultTicketTtl);
                ticket = existingTicket;
                message = $"Ticket actualizado para '{cleanAlias}' (caduca en {(int)DefaultTicketTtl.TotalMinutes} min).";
                return true;
            }

            int used = CountPutreActiveSlots(state, nowUtc);
            if (used >= MaxConcurrentPutreSlots)
            {
                message = $"Cupo lleno: Putre ya gestiona {used}/{MaxConcurrentPutreSlots} invitados simultáneos en admisión u observación.";
                return false;
            }

            ticket = new PutreAdmissionTicket
            {
                DiscordUserId = cleanDiscord,
                Alias = cleanAlias,
                CreatedAtUtc = nowUtc,
                ExpiresAtUtc = nowUtc.Add(DefaultTicketTtl)
            };
            state.PutreTickets.Add(ticket);
            message = $"Ticket abierto para '{cleanAlias}' ({used + 1}/{MaxConcurrentPutreSlots} huecos ocupados).";
            return true;
        }

        public static bool CancelTicket(ServerWorldState state, string target, out string message)
        {
            if (state?.PutreTickets == null || string.IsNullOrWhiteSpace(target))
            {
                message = "No hay ticket pendiente para cancelar.";
                return false;
            }

            string clean = target.Trim();
            int removed = state.PutreTickets.RemoveAll(t =>
                t != null && (
                    string.Equals(t.DiscordUserId, clean, StringComparison.Ordinal) ||
                    string.Equals(t.Alias, clean, StringComparison.OrdinalIgnoreCase)));

            if (removed == 0)
            {
                message = $"No se encontró ningún ticket abierto para '{clean}'.";
                return false;
            }

            message = $"Ticket cancelado para '{clean}'. Hueco liberado.";
            return true;
        }

        public static bool ApproveTicketDevice(
            ServerWorldState state,
            string discordUserId,
            string fingerprintPrefix,
            IReadOnlyCollection<UnpairedHandshakeObservation> observations,
            DateTimeOffset nowUtc,
            out AuthorizedDevice? device,
            out string message)
        {
            device = null;
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            string cleanDiscord = (discordUserId ?? string.Empty).Trim();
            string cleanFp = (fingerprintPrefix ?? string.Empty).Trim().ToLowerInvariant();

            if (!IsValidDiscordId(cleanDiscord))
            {
                message = "El identificador de Discord no es válido.";
                return false;
            }

            if (!IsValidFingerprintPrefix(cleanFp))
            {
                message = "La huella debe tener al menos 8 caracteres hexadecimales (ej. 12 caracteres).";
                return false;
            }

            if (IsBlacklisted(state, null, cleanDiscord, out BlacklistEntry? blDiscord))
            {
                message = $"Usuario de Discord en lista negra ({blDiscord?.Reason ?? "sin motivo"}).";
                return false;
            }

            state.PutreTickets ??= new List<PutreAdmissionTicket>();
            PurgeExpiredTickets(state, nowUtc);

            PutreAdmissionTicket? ticket = state.PutreTickets.FirstOrDefault(t =>
                t != null && string.Equals(t.DiscordUserId, cleanDiscord, StringComparison.Ordinal));
            if (ticket == null)
            {
                message = "No existe un ticket abierto para ese usuario de Discord (o ha caducado).";
                return false;
            }

            // Buscar en observaciones recientes en memoria (con clave ECDSA verificada)
            List<UnpairedHandshakeObservation> matches = (observations ?? Array.Empty<UnpairedHandshakeObservation>())
                .Where(o =>
                    o != null &&
                    (nowUtc - o.ObservedAtUtc) <= UnpairedObservationTtl &&
                    o.Fingerprint.StartsWith(cleanFp, StringComparison.OrdinalIgnoreCase))
                .GroupBy(o => o.Fingerprint, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.ObservedAtUtc).First())
                .ToList();

            if (matches.Count == 0)
            {
                message = $"No se ha visto ninguna conexión reciente verificada con huella '{cleanFp}'. Conéctate primero una vez al servidor.";
                return false;
            }

            if (matches.Count > 1)
            {
                message = $"El prefijo '{cleanFp}' coincide con varios dispositivos; introduce más caracteres de la huella.";
                return false;
            }

            UnpairedHandshakeObservation match = matches[0];
            if (IsBlacklisted(state, match.Fingerprint, cleanDiscord, out BlacklistEntry? blFp))
            {
                state.PutreTickets.RemoveAll(t => t != null && string.Equals(t.DiscordUserId, cleanDiscord, StringComparison.Ordinal));
                message = $"Dispositivo en lista negra ({blFp?.Reason ?? "sin motivo"}). Solo el propietario puede retirarla.";
                return false;
            }

            if (state.AuthorizedDevices != null &&
                state.AuthorizedDevices.TryGetValue(match.Fingerprint, out AuthorizedDevice? existing) &&
                existing != null &&
                existing.Status == DeviceStatus.Active)
            {
                state.PutreTickets.RemoveAll(t => t != null && string.Equals(t.DiscordUserId, cleanDiscord, StringComparison.Ordinal));
                message = $"El dispositivo '{existing.Alias}' ya estaba autorizado.";
                return false;
            }

            // El ticket ya ocupaba 1 slot; al consumirlo y aprobar en Observing, el total de slots no debe exceder MaxConcurrentPutreSlots
            int slotsExcludingThisTicket = CountPutreActiveSlots(state, nowUtc) - 1;
            if (slotsExcludingThisTicket >= MaxConcurrentPutreSlots)
            {
                message = $"Cupo lleno ({MaxConcurrentPutreSlots}/{MaxConcurrentPutreSlots} en observación).";
                return false;
            }

            device = ServerDeviceRegistry.RegisterOrUpdateDevice(
                state,
                match.PublicKeyBase64,
                ticket.Alias,
                boundPlayerId: 0,
                ip: match.RemoteIp,
                nowUtc: nowUtc);

            device.InvitedBy = "putre";
            device.DiscordUserId = cleanDiscord;
            device.ProbationStage = ProbationStage.Observing;
            device.GraduatedAtUtc = null;

            state.PutreTickets.RemoveAll(t => t != null && string.Equals(t.DiscordUserId, cleanDiscord, StringComparison.Ordinal));
            state.PendingEnrollments?.Remove(match.Fingerprint);

            message = $"Dispositivo '{device.Alias}' ({device.Fingerprint[..12]}...) aprobado en observación por Putre.";
            return true;
        }

        public static bool GraduateDevice(
            ServerWorldState state,
            string target,
            DateTimeOffset nowUtc,
            out AuthorizedDevice? device,
            out string message)
        {
            device = null;
            if (state == null || string.IsNullOrWhiteSpace(target))
            {
                message = "Debes indicar huella, alias o Discord ID.";
                return false;
            }

            device = FindAuthorizedDevice(state, target.Trim());
            if (device == null)
            {
                message = $"No se encontró ningún dispositivo activo para '{target}'.";
                return false;
            }

            if (device.Status == DeviceStatus.Blacklisted)
            {
                message = $"El dispositivo '{device.Alias}' está en lista negra y no puede graduarse.";
                return false;
            }

            device.ProbationStage = ProbationStage.Graduated;
            device.GraduatedAtUtc = nowUtc;
            message = $"Dispositivo '{device.Alias}' graduado; supera la observación y libera su hueco.";
            return true;
        }

        public static bool AddToBlacklist(
            ServerWorldState state,
            string target,
            string reason,
            string blacklistedBy,
            DateTimeOffset nowUtc,
            out BlacklistEntry? entry,
            out List<long> affectedPlayerIds,
            out string message)
        {
            entry = null;
            affectedPlayerIds = new List<long>();
            if (state == null || string.IsNullOrWhiteSpace(target))
            {
                message = "Debes especificar huella, alias, Discord ID o ID de jugador.";
                return false;
            }

            string clean = target.Trim();
            string cleanReason = string.IsNullOrWhiteSpace(reason) ? "Uso indebido detectado" : reason.Trim();
            string cleanActor = string.IsNullOrWhiteSpace(blacklistedBy) ? "putre" : blacklistedBy.Trim();

            state.Blacklist ??= new List<BlacklistEntry>();
            AuthorizedDevice? device = FindAuthorizedDevice(state, clean);
            PutreAdmissionTicket? ticket = state.PutreTickets?.FirstOrDefault(t =>
                t != null && (
                    string.Equals(t.DiscordUserId, clean, StringComparison.Ordinal) ||
                    string.Equals(t.Alias, clean, StringComparison.OrdinalIgnoreCase)));

            string fp = device?.Fingerprint ?? string.Empty;
            string discordId = device?.DiscordUserId ?? ticket?.DiscordUserId ?? (IsValidDiscordId(clean) ? clean : string.Empty);
            string alias = device?.Alias ?? ticket?.Alias ?? clean;
            string ip = device?.LastSeenIp ?? string.Empty;

            if (string.IsNullOrEmpty(fp) && DeviceSignatureVerifier.IsValidFingerprint(clean))
            {
                fp = clean.ToLowerInvariant();
            }

            if (device == null && ticket == null && string.IsNullOrEmpty(fp) && string.IsNullOrEmpty(discordId))
            {
                message = $"No se encontró ningún invitado, dispositivo ni ID válido para '{clean}'.";
                return false;
            }

            if (device != null)
            {
                device.Status = DeviceStatus.Blacklisted;
                device.ProbationStage = ProbationStage.Blacklisted;
                if (device.BoundPlayerId != 0 && !affectedPlayerIds.Contains(device.BoundPlayerId))
                    affectedPlayerIds.Add(device.BoundPlayerId);
            }

            if (!string.IsNullOrEmpty(fp) && state.Players != null)
            {
                foreach (PlayerRecord p in state.Players.Values.Where(p =>
                    string.Equals(p.OwnerToken, fp, StringComparison.OrdinalIgnoreCase)))
                {
                    PlayerAuthLogic.ResetClaim(p);
                    if (!affectedPlayerIds.Contains(p.UniqueMultiplayerID))
                        affectedPlayerIds.Add(p.UniqueMultiplayerID);
                }
            }

            if (state.AuthorizedClients != null && !string.IsNullOrEmpty(fp))
            {
                state.AuthorizedClients.Remove(fp);
            }

            state.PutreTickets?.RemoveAll(t =>
                t != null && (
                    (!string.IsNullOrEmpty(discordId) && string.Equals(t.DiscordUserId, discordId, StringComparison.Ordinal)) ||
                    string.Equals(t.Alias, alias, StringComparison.OrdinalIgnoreCase)));

            if (!string.IsNullOrEmpty(fp))
                state.PendingEnrollments?.Remove(fp);

            BlacklistEntry? existing = state.Blacklist.FirstOrDefault(b =>
                b != null && (
                    (!string.IsNullOrEmpty(fp) && string.Equals(b.Fingerprint, fp, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(discordId) && string.Equals(b.DiscordUserId, discordId, StringComparison.Ordinal))));

            if (existing != null)
            {
                if (string.IsNullOrEmpty(existing.Fingerprint) && !string.IsNullOrEmpty(fp)) existing.Fingerprint = fp;
                if (string.IsNullOrEmpty(existing.DiscordUserId) && !string.IsNullOrEmpty(discordId)) existing.DiscordUserId = discordId;
                existing.Reason = cleanReason;
                existing.BlacklistedBy = cleanActor;
                existing.BlacklistedAtUtc = nowUtc;
                entry = existing;
            }
            else
            {
                entry = new BlacklistEntry
                {
                    Fingerprint = fp,
                    DiscordUserId = discordId,
                    Alias = alias,
                    LastKnownIp = ip,
                    Reason = cleanReason,
                    BlacklistedBy = cleanActor,
                    BlacklistedAtUtc = nowUtc
                };
                state.Blacklist.Add(entry);
            }

            string fpDisplay = string.IsNullOrEmpty(fp) ? "-" : (fp.Length >= 12 ? fp[..12] : fp);
            message = $"'{alias}' (huella: {fpDisplay}, discord: {(string.IsNullOrEmpty(discordId) ? "-" : discordId)}) añadido a la LISTA NEGRA: {cleanReason}.";
            return true;
        }

        public static bool RemoveFromBlacklist(
            ServerWorldState state,
            string target,
            out BlacklistEntry? removed,
            out string message)
        {
            removed = null;
            if (state?.Blacklist == null || string.IsNullOrWhiteSpace(target))
            {
                message = "No se encontró ninguna entrada coincidente en la lista negra.";
                return false;
            }

            string clean = target.Trim();
            BlacklistEntry? match = state.Blacklist.FirstOrDefault(b =>
                b != null && (
                    string.Equals(b.DiscordUserId, clean, StringComparison.Ordinal) ||
                    string.Equals(b.Alias, clean, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(b.Fingerprint) && (
                        string.Equals(b.Fingerprint, clean, StringComparison.OrdinalIgnoreCase) ||
                        (clean.Length >= 8 && b.Fingerprint.StartsWith(clean, StringComparison.OrdinalIgnoreCase))))));

            if (match == null)
            {
                message = $"No existe '{clean}' en la lista negra.";
                return false;
            }

            state.Blacklist.RemoveAll(b =>
                b != null && (
                    (!string.IsNullOrEmpty(match.Fingerprint) && string.Equals(b.Fingerprint, match.Fingerprint, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(match.DiscordUserId) && string.Equals(b.DiscordUserId, match.DiscordUserId, StringComparison.Ordinal))));

            if (state.AuthorizedDevices != null && !string.IsNullOrEmpty(match.Fingerprint) &&
                state.AuthorizedDevices.TryGetValue(match.Fingerprint, out AuthorizedDevice? dev) &&
                dev != null && dev.Status == DeviceStatus.Blacklisted)
            {
                dev.Status = DeviceStatus.Revoked;
                dev.ProbationStage = ProbationStage.None;
            }

            removed = match;
            message = $"'{match.Alias}' retirado de la lista negra por el propietario.";
            return true;
        }

        private static readonly Regex SuspiciousChatTagRegex = new(
            @"\[(/?color\b|font\b|img\b|#[0-9a-fA-F]{3,8})|</?color\b|@(?:everyone|here)\b|^\s*\[(?:sistema|system|admin|servidor|server|discord)\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Detecta intentos de inyección de etiquetas de formato o suplantación en mensajes de chat del juego.
        /// Permite emojis numéricos vanilla de Stardew (p. ej. <c>[12]</c>).
        /// </summary>
        public static bool IsSuspiciousChatTagInjection(string? message)
        {
            if (string.IsNullOrEmpty(message))
                return false;

            foreach (char ch in message)
            {
                if (char.IsControl(ch) && ch != '\t' && ch != '\r' && ch != '\n')
                    return true;
            }

            return SuspiciousChatTagRegex.IsMatch(message);
        }

        private static AuthorizedDevice? FindAuthorizedDevice(ServerWorldState state, string clean)
        {
            if (state?.AuthorizedDevices == null)
                return null;

            if (state.AuthorizedDevices.TryGetValue(clean, out AuthorizedDevice? exact))
                return exact;

            bool parsedId = long.TryParse(clean, out long playerId);
            return state.AuthorizedDevices.Values.FirstOrDefault(d =>
                d != null && (
                    string.Equals(d.DiscordUserId, clean, StringComparison.Ordinal) ||
                    string.Equals(d.Alias, clean, StringComparison.OrdinalIgnoreCase) ||
                    (parsedId && d.BoundPlayerId == playerId) ||
                    (clean.Length >= 8 && d.Fingerprint.StartsWith(clean, StringComparison.OrdinalIgnoreCase))));
        }
    }
}
