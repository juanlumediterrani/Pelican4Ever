using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Permite al jugador meterse en la cama, seleccionar "Descansar y Desconectar",
    /// resolver subidas de nivel/profesiones pendientes, registrar su estado Rested en el Host
    /// y salir al menú sin bloquear al resto (TASK-102 / ADR-0004).
    /// En modo <c>AgentHost</c> (TASK-804), el anfitrión registra su descanso sin desconectarse.
    /// </summary>
    public sealed class BedLogoutModule : IModule
    {
        private static ModContext? context;
        private static int pendingOfflineStaminaGain = 0;
        private static int pendingOfflineHealthGain = 0;
        public const string BedLogoutMessageType = "BedLogoutRequest";
        private const string OfflineStaminaMessageType = "OfflineStaminaRestored";
        public const string OfflineRecoveryMessageType = "OfflineRecoveryRestored";

        private static BedLogoutPayload? pendingBedLogoutPayload;
        private static bool isBedLogoutPendingAfterLevels;
        private static bool isHostRestPendingAfterLevels;
        private static bool hasCheckedLoginLevelUps;
        private static Point? lastPresentedLevel;

        public string Name => "BedLogout";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            // El host recibe solicitudes de descanso y el cliente recibe las ganancias offline.
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            ctx.Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            ctx.Helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            ctx.Helper.Events.GameLoop.DayStarted += OnDayStarted;

            // En el Host gestionamos conexiones y desconexiones
            if (Context.IsMainPlayer)
            {
                ctx.Helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
                ctx.Helper.Events.Multiplayer.PeerDisconnected += OnPeerDisconnected;
                ctx.Helper.Events.GameLoop.SaveLoaded += OnHostSaveLoaded;
            }

            ApplyHarmonyPatches(ctx.Harmony);
        }

        private void OnDayStarted(object? sender, StardewModdingAPI.Events.DayStartedEventArgs e)
        {
            hasCheckedLoginLevelUps = false;
            lastPresentedLevel = null;
        }

        private void OnReturnedToTitle(object? sender, StardewModdingAPI.Events.ReturnedToTitleEventArgs e)
        {
            pendingOfflineStaminaGain = 0;
            pendingOfflineHealthGain = 0;
            pendingBedLogoutPayload = null;
            isBedLogoutPendingAfterLevels = false;
            isHostRestPendingAfterLevels = false;
            hasCheckedLoginLevelUps = false;
            lastPresentedLevel = null;
        }

        private void OnUpdateTicked(object? sender, StardewModdingAPI.Events.UpdateTickedEventArgs e)
        {
            if ((pendingOfflineStaminaGain > 0 || pendingOfflineHealthGain > 0) && Context.IsWorldReady && Game1.player != null)
            {
                int staminaGained = pendingOfflineStaminaGain;
                int healthGained = pendingOfflineHealthGain;
                pendingOfflineStaminaGain = 0;
                pendingOfflineHealthGain = 0;
                ApplyOfflineRecoveryToLocalPlayer(staminaGained, healthGained);
            }

            if (!Context.IsWorldReady || Game1.player == null)
                return;

            // En modo anfitrión agente (headless), no mostramos menús interactivos
            if (Context.IsMainPlayer && context?.Config.AgentHost == true)
                return;

            // 1. Proceso de subida de nivel durante BedLogout (en la cama para cliente)
            if (isBedLogoutPendingAfterLevels)
            {
                if (Game1.activeClickableMenu == null)
                {
                    if (Game1.player.newLevels.Count > 0)
                    {
                        Point next = Game1.player.newLevels[0];
                        if (lastPresentedLevel.HasValue && lastPresentedLevel.Value.Equals(next))
                        {
                            Game1.player.newLevels.RemoveAt(0);
                            if (Game1.player.newLevels.Count == 0)
                            {
                                isBedLogoutPendingAfterLevels = false;
                                lastPresentedLevel = null;
                                if (pendingBedLogoutPayload != null)
                                {
                                    var payload = pendingBedLogoutPayload;
                                    pendingBedLogoutPayload = null;
                                    ExecuteClientBedLogout(payload);
                                }
                                return;
                            }
                            next = Game1.player.newLevels[0];
                        }

                        lastPresentedLevel = next;
                        context?.Monitor.Log($"[BedLogoutModule] Mostrando menú de nivel (cama) para {Game1.player.Name}: habilidad {next.X}, nivel {next.Y}.", LogLevel.Info);
                        Game1.activeClickableMenu = new LevelUpMenu(next.X, next.Y);
                    }
                    else
                    {
                        isBedLogoutPendingAfterLevels = false;
                        lastPresentedLevel = null;
                        if (pendingBedLogoutPayload != null)
                        {
                            var payload = pendingBedLogoutPayload;
                            pendingBedLogoutPayload = null;
                            ExecuteClientBedLogout(payload);
                        }
                    }
                }
                return;
            }

            // 1b. Proceso de subida de nivel durante descanso del Host (jugador humano)
            if (isHostRestPendingAfterLevels)
            {
                if (Game1.activeClickableMenu == null)
                {
                    if (Game1.player.newLevels.Count > 0)
                    {
                        Point next = Game1.player.newLevels[0];
                        if (lastPresentedLevel.HasValue && lastPresentedLevel.Value.Equals(next))
                        {
                            Game1.player.newLevels.RemoveAt(0);
                            if (Game1.player.newLevels.Count == 0)
                            {
                                isHostRestPendingAfterLevels = false;
                                lastPresentedLevel = null;
                                ExecuteHostRest();
                                return;
                            }
                            next = Game1.player.newLevels[0];
                        }

                        lastPresentedLevel = next;
                        context?.Monitor.Log($"[BedLogoutModule] Mostrando menú de nivel (cama host) para {Game1.player.Name}: habilidad {next.X}, nivel {next.Y}.", LogLevel.Info);
                        Game1.activeClickableMenu = new LevelUpMenu(next.X, next.Y);
                    }
                    else
                    {
                        isHostRestPendingAfterLevels = false;
                        lastPresentedLevel = null;
                        ExecuteHostRest();
                    }
                }
                return;
            }

            // 2. Al entrar a la partida o tras un rollover diario, comprobar si quedaron niveles o profesiones pendientes
            if (!hasCheckedLoginLevelUps)
            {
                if (!Game1.fadeToBlack && !Game1.globalFade && Game1.activeClickableMenu == null && !Game1.eventUp)
                {
                    LevelUpMenu.AddMissedProfessionChoices(Game1.player);
                    if (Game1.player.newLevels.Count > 0)
                    {
                        Point next = Game1.player.newLevels[0];
                        if (lastPresentedLevel.HasValue && lastPresentedLevel.Value.Equals(next))
                        {
                            Game1.player.newLevels.RemoveAt(0);
                            if (Game1.player.newLevels.Count == 0)
                            {
                                hasCheckedLoginLevelUps = true;
                                lastPresentedLevel = null;
                                return;
                            }
                            next = Game1.player.newLevels[0];
                        }

                        lastPresentedLevel = next;
                        context?.Monitor.Log($"[BedLogoutModule] Mostrando menú de nivel pendiente (inicio sesión) para {Game1.player.Name}: habilidad {next.X}, nivel {next.Y}.", LogLevel.Info);
                        Game1.activeClickableMenu = new LevelUpMenu(next.X, next.Y);
                    }
                    else
                    {
                        hasCheckedLoginLevelUps = true;
                        lastPresentedLevel = null;
                    }
                }
            }
        }

        private static void ApplyOfflineRecoveryToLocalPlayer(int staminaGained, int healthGained)
        {
            if (Game1.player == null || (staminaGained <= 0 && healthGained <= 0))
                return;

            int appliedStamina = 0;
            if (staminaGained > 0)
            {
                float oldStamina = Game1.player.Stamina;
                Game1.player.Stamina = OfflineStaminaRegenLogic.ApplyGain(Game1.player.Stamina, Game1.player.MaxStamina, staminaGained);
                appliedStamina = Math.Max(0, (int)Math.Round(Game1.player.Stamina - oldStamina));
                if (Game1.player.Stamina > 0 && Game1.player.exhausted.Value)
                {
                    Game1.player.exhausted.Value = false;
                }

                context?.Monitor.Log($"[BedLogoutModule] Recuperación de energía offline aplicada al jugador local: +{appliedStamina} ({oldStamina:F0} -> {Game1.player.Stamina:F0}/{Game1.player.MaxStamina}).", LogLevel.Info);
            }

            int appliedHealth = 0;
            if (healthGained > 0)
            {
                int oldHealth = Game1.player.health;
                Game1.player.health = OfflineStaminaRegenLogic.ApplyHealthGain(Game1.player.health, Game1.player.maxHealth, healthGained);
                appliedHealth = Math.Max(0, Game1.player.health - oldHealth);
                context?.Monitor.Log($"[BedLogoutModule] Recuperación de salud offline aplicada al jugador local: +{appliedHealth} ({oldHealth} -> {Game1.player.health}/{Game1.player.maxHealth}).", LogLevel.Info);
            }

            if (appliedStamina > 0 && appliedHealth > 0)
            {
                Game1.addHUDMessage(new HUDMessage(
                    I18n.Get("bed_logout.offline_rest_both", new { energy = appliedStamina, health = appliedHealth }),
                    HUDMessage.newQuest_type));
            }
            else if (appliedStamina > 0)
            {
                Game1.addHUDMessage(new HUDMessage(
                    I18n.Get("bed_logout.offline_rest", new { gained = appliedStamina }),
                    HUDMessage.newQuest_type));
            }
            else if (appliedHealth > 0)
            {
                Game1.addHUDMessage(new HUDMessage(
                    I18n.Get("bed_logout.offline_rest_health", new { health = appliedHealth }),
                    HUDMessage.newQuest_type));
            }
        }

        private void OnHostSaveLoaded(object? sender, StardewModdingAPI.Events.SaveLoadedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null || context.Config.OfflineRegenFullBarHours <= 0)
                return;

            if (context.Config.AgentHost || Game1.player == null)
                return;

            long hostId = Game1.player.UniqueMultiplayerID;
            if (!context.WorldState.Players.TryGetValue(hostId, out var hostRecord))
                return;

            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            var recovery = OfflineStaminaRegenLogic.CalculateRecoveryGains(
                hostRecord, nowUtc, Game1.player.MaxStamina, Game1.player.maxHealth, context.Config.OfflineRegenFullBarHours);
            hostRecord.OfflineStaminaAnchorUtc = DateTimeOffset.MinValue;
            hostRecord.OfflineHealthAnchorUtc = DateTimeOffset.MinValue;
            hostRecord.LastSeenUtc = nowUtc;

            ApplyOfflineRecoveryToLocalPlayer(recovery.Stamina, recovery.Health);
        }

        private void OnPeerConnected(object? sender, StardewModdingAPI.Events.PeerConnectedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null)
                return;

            long playerId = e.Peer.PlayerID;
            Farmer? farmer = Game1.GetPlayer(playerId, onlyOnline: true);
            if (farmer == null)
                return;

            var player = context.WorldState.GetOrCreatePlayer(playerId, farmer.Name);

            DateTime localNow = Core.Time.ServerTime.LocalNow(context.Config.TimeZoneId);
            TimeSpan dayStart = context.Config.ParsedCalendarDayChangeTime;
            TimeSpan graceStart = dayStart - TimeSpan.FromMinutes(context.Config.EarlyMorningGraceMinutes);
            bool inGraceWindow = localNow.TimeOfDay >= graceStart && localNow.TimeOfDay < dayStart;

            if (!inGraceWindow)
            {
                player.ActiveDayPriorToGrace = Game1.Date.TotalDays;
            }

            // Aplicar recuperación gradual de estamina y salud durante su tiempo desconectado (GAP-07 / Offline Regen)
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            SendOfflineRecovery(player, farmer, nowUtc);

            player.CurrentSessionStartUtc = nowUtc;
            player.LastActiveGameDay = Game1.Date.TotalDays;
            player.LastSeenUtc = nowUtc;
            player.OfflineStaminaAnchorUtc = DateTimeOffset.MinValue;
            player.OfflineHealthAnchorUtc = DateTimeOffset.MinValue;
            player.EnsureDailyCounters(Game1.Date.TotalDays);
            context.Monitor.Log($"[BedLogoutModule] Jugador {e.Peer.PlayerID} conectado. Marcado activo para día {Game1.Date.TotalDays} (MadrugadorEnGracia: {inGraceWindow}).", LogLevel.Trace);

            AdminApiModule.EventLog.Append(
                HostEventTypes.PlayerConnected,
                Game1.Date.TotalDays,
                Game1.timeOfDay,
                $"Jugador conectado: {(string.IsNullOrWhiteSpace(player.PlayerName) ? e.Peer.PlayerID.ToString() : player.PlayerName)}",
                new Dictionary<string, object?>
                {
                    ["playerId"] = e.Peer.PlayerID,
                    ["playerName"] = player.PlayerName
                });
        }

        private static void SendOfflineRecovery(PlayerRecord player, Farmer farmer, DateTimeOffset nowUtc)
        {
            if (context == null || context.Config.OfflineRegenFullBarHours <= 0)
                return;

            var recovery = OfflineStaminaRegenLogic.CalculateRecoveryGains(
                player, nowUtc, farmer.MaxStamina, farmer.maxHealth, context.Config.OfflineRegenFullBarHours);
            string[] modIds = { context.Manifest.UniqueID };
            long[] playerIds = { farmer.UniqueMultiplayerID };

            // El granjero local aplica la energía sobre su barra y sincroniza su NetFloat con el host.
            // El mensaje original también permite que los clientes 0.5.0 reciban su recuperación.
            if (recovery.Stamina > 0)
            {
                context.Helper.Multiplayer.SendMessage(recovery.Stamina, OfflineStaminaMessageType, modIds, playerIds);
            }

            int restoredHealth = OfflineStaminaRegenLogic.ApplyHealthGain(farmer.health, farmer.maxHealth, recovery.Health);
            int healthGained = restoredHealth - farmer.health;
            if (healthGained > 0)
            {
                farmer.health = restoredHealth;
                context.Helper.Multiplayer.SendMessage(new OfflineRecoveryPayload { HealthGained = healthGained }, OfflineRecoveryMessageType, modIds, playerIds);
            }

            if (recovery.Stamina > 0 || healthGained > 0)
            {
                context.Monitor.Log($"[BedLogoutModule] Recuperación offline enviada a {player.PlayerName} ({farmer.UniqueMultiplayerID}): hasta +{recovery.Stamina} energía y +{healthGained} salud.", LogLevel.Trace);
            }
        }

        private void OnPeerDisconnected(object? sender, StardewModdingAPI.Events.PeerDisconnectedEventArgs e)
        {
            if (!Context.IsMainPlayer || context == null)
                return;

            string playerName = Game1.GetPlayer(e.Peer.PlayerID)?.Name
                ?? (context.WorldState.Players.TryGetValue(e.Peer.PlayerID, out var rec) ? rec.PlayerName : string.Empty);

            if (context.WorldState.Players.TryGetValue(e.Peer.PlayerID, out var record))
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                record.LastSeenUtc = now;
                record.OfflineStaminaAnchorUtc = now;
                record.OfflineHealthAnchorUtc = now;
                record.CurrentSessionStartUtc = DateTimeOffset.MinValue;
            }

            AdminApiModule.EventLog.Append(
                HostEventTypes.PlayerDisconnected,
                Game1.Date.TotalDays,
                Game1.timeOfDay,
                $"Jugador desconectado: {(string.IsNullOrWhiteSpace(playerName) ? e.Peer.PlayerID.ToString() : playerName)}",
                new Dictionary<string, object?>
                {
                    ["playerId"] = e.Peer.PlayerID,
                    ["playerName"] = playerName
                });
        }

        private void OnModMessageReceived(object? sender, StardewModdingAPI.Events.ModMessageReceivedEventArgs e)
        {
            if (context == null || e.FromModID != context.Manifest.UniqueID)
                return;

            // En el Host: registrar descanso en cama
            if (Context.IsMainPlayer && e.Type == BedLogoutMessageType)
            {
                long playerId = e.FromPlayerID;
                BedLogoutPayload? payload = null;
                try
                {
                    payload = e.ReadAs<BedLogoutPayload>();
                }
                catch
                {
                    // Compatibilidad hacia atrás si un cliente envía un formato previo
                }

                int restDay = payload?.GameDay > 0 ? payload.GameDay : Game1.Date.TotalDays;
                var record = context.WorldState.GetOrCreatePlayer(playerId, string.Empty);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                record.RestedOnGameDay = restDay;
                record.LastActiveGameDay = restDay;
                record.OfflineStaminaAnchorUtc = now;
                record.OfflineHealthAnchorUtc = now;

                Farmer? sleepingFarmer = Game1.GetPlayer(playerId);
                if (sleepingFarmer != null && payload != null && !string.IsNullOrEmpty(payload.LocationName))
                {
                    sleepingFarmer.lastSleepLocation.Value = payload.LocationName;
                    sleepingFarmer.lastSleepPoint.Value = new Point(payload.BedTileX, payload.BedTileY);
                    sleepingFarmer.disconnectLocation.Value = payload.LocationName;
                    sleepingFarmer.disconnectPosition.Value = new Vector2(payload.PositionX, payload.PositionY);
                    sleepingFarmer.disconnectDay.Value = restDay;
                }

                try
                {
                    context.Helper.Data.WriteSaveData(ServerWorldState.SaveDataKey, context.WorldState);
                }
                catch (Exception ex)
                {
                    context.Monitor.Log($"[BedLogoutModule] Error al guardar ServerWorldState tras descanso del jugador {playerId}: {ex}", LogLevel.Warn);
                }
                context.Monitor.Log($"[BedLogoutModule] Descanso registrado para jugador {playerId} en día {restDay} (loc: {payload?.LocationName ?? "n/a"}).", LogLevel.Info);
                return;
            }

            if (Context.IsMainPlayer || context.Helper.Multiplayer.GetConnectedPlayer(e.FromPlayerID)?.IsHost != true)
                return;

            // Aplicar en UpdateTicked, cuando la carga del granjero local haya terminado.
            if (e.Type == OfflineRecoveryMessageType)
            {
                var recovery = e.ReadAs<OfflineRecoveryPayload>();
                if (recovery != null)
                {
                    pendingOfflineStaminaGain += Math.Max(0, recovery.StaminaGained);
                    pendingOfflineHealthGain += Math.Max(0, recovery.HealthGained);
                }
            }
            else if (e.Type == OfflineStaminaMessageType)
            {
                pendingOfflineStaminaGain += Math.Max(0, e.ReadAs<int>());
            }
        }

        private void ApplyHarmonyPatches(Harmony harmony)
        {
            _ = harmony;
            if (context == null)
                return;

            context.Patch(
                "GameLocation.startSleep",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(GameLocation), "startSleep"),
                prefix: new HarmonyMethod(typeof(BedLogoutModule), nameof(Prefix_StartSleep)));

            // LevelUpMenu vanilla recarga vida y energía al 100% al elegir profesión o cerrar el menú,
            // asumiendo un cierre nocturno; fuera de showingEndOfNightStuff debemos preservarlas.
            context.Patch(
                "LevelUpMenu.getImmediateProfessionPerk",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(LevelUpMenu), nameof(LevelUpMenu.getImmediateProfessionPerk), new[] { typeof(int) }),
                prefix: new HarmonyMethod(typeof(BedLogoutModule), nameof(Prefix_LevelUpPerk)),
                postfix: new HarmonyMethod(typeof(BedLogoutModule), nameof(Postfix_LevelUpPerk)));

            context.Patch(
                "LevelUpMenu.getLevelPerk",
                Name,
                PatchCriticality.Integrity,
                AccessTools.Method(typeof(LevelUpMenu), nameof(LevelUpMenu.getLevelPerk), new[] { typeof(int), typeof(int) }),
                prefix: new HarmonyMethod(typeof(BedLogoutModule), nameof(Prefix_LevelUpPerk)),
                postfix: new HarmonyMethod(typeof(BedLogoutModule), nameof(Postfix_LevelUpPerk)));
        }

        public static void Prefix_LevelUpPerk(out LevelUpResourceLogic.ResourceSnapshot? __state)
        {
            if (context == null
                || !context.IsModEnabled
                || !Context.IsWorldReady
                || Game1.player == null
                || !LevelUpResourceLogic.ShouldPreserveResources(Game1.showingEndOfNightStuff))
            {
                __state = null;
                return;
            }

            __state = new LevelUpResourceLogic.ResourceSnapshot(
                Game1.player.health,
                Game1.player.maxHealth,
                Game1.player.stamina);
        }

        public static void Postfix_LevelUpPerk(LevelUpResourceLogic.ResourceSnapshot? __state)
        {
            if (__state is not { } before || Game1.player == null)
                return;

            Game1.player.health = LevelUpResourceLogic.ResolveHealth(before, Game1.player.maxHealth);
            Game1.player.stamina = LevelUpResourceLogic.ResolveStamina(before, Game1.player.MaxStamina);
        }

        /// <summary>
        /// Registra el descanso del jugador anfitrión (desde la cama o vía <c>POST /api/v1/host/rest</c>) (TASK-804).
        /// </summary>
        public static HostRestResult RestHostPlayer(bool triggeredFromApi = false)
        {
            if (context == null || !Context.IsMainPlayer)
            {
                return new HostRestResult(false, -1, false, "Solo el anfitrión con una partida cargada puede registrar su descanso.");
            }

            int today = Game1.Date.TotalDays;
            var record = context.WorldState.GetOrCreatePlayer(Game1.player.UniqueMultiplayerID, Game1.player.Name);
            bool stayOnline = triggeredFromApi || context.Config.AgentHost;

            HostRestResult result = AgentHostLogic.ApplyRest(
                record,
                today,
                isHostPlayer: true,
                agentHostEnabled: stayOnline);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            record.OfflineStaminaAnchorUtc = now;
            record.OfflineHealthAnchorUtc = now;

            AdminApiModule.EventLog.Append(
                HostEventTypes.HostRested,
                today,
                Game1.timeOfDay,
                $"Descanso del anfitrión registrado para el día {today} (sin desconexión: {!result.ShouldDisconnectSession}).",
                new Dictionary<string, object?>
                {
                    ["playerId"] = Game1.player.UniqueMultiplayerID,
                    ["playerName"] = Game1.player.Name,
                    ["gameDay"] = today,
                    ["agentHost"] = context.Config.AgentHost,
                    ["triggeredFromApi"] = triggeredFromApi
                });

            context.Monitor.Log($"[BedLogoutModule] {result.Message} (Día {today}).", LogLevel.Info);
            return result;
        }

        public static bool Prefix_StartSleep(GameLocation __instance)
        {
            // Si estamos en un servidor con Pelican 4Ever activo
            if (Game1.IsMultiplayer && context != null)
            {
                var bedSpot = (__instance as FarmHouse)?.GetPlayerBedSpot() ?? Game1.player.TilePoint;
                if (bedSpot == Point.Zero)
                {
                    bedSpot = new Point(ResidenceInteriorLogic.BedPlacement.TargetX, ResidenceInteriorLogic.BedPlacement.TargetY);
                }
                string locationName = __instance.NameOrUniqueName;

                Game1.player.lastSleepLocation.Value = locationName;
                Game1.player.lastSleepPoint.Value = bedSpot;
                Game1.player.disconnectLocation.Value = locationName;
                Game1.player.disconnectPosition.Value = Game1.player.Position;
                Game1.player.disconnectDay.Value = Game1.Date.TotalDays;

                // Si es el propio Host quien duerme en la cama (TASK-804)
                if (Context.IsMainPlayer)
                {
                    if (context.Config.AgentHost)
                    {
                        ExecuteHostRest();
                        return false;
                    }

                    LevelUpMenu.AddMissedProfessionChoices(Game1.player);
                    if (Game1.player.newLevels.Count > 0)
                    {
                        isHostRestPendingAfterLevels = true;
                        Point p = Game1.player.newLevels[0];
                        lastPresentedLevel = p;
                        context.Monitor.Log($"[BedLogoutModule] Mostrando menú de nivel (cama host) para {Game1.player.Name}: habilidad {p.X}, nivel {p.Y}.", LogLevel.Info);
                        Game1.activeClickableMenu = new LevelUpMenu(p.X, p.Y);
                        return false;
                    }

                    ExecuteHostRest();
                    return false; // Cancela el startSleep() nativo
                }

                var payload = new BedLogoutPayload
                {
                    GameDay = Game1.Date.TotalDays,
                    LocationName = locationName,
                    BedTileX = bedSpot.X,
                    BedTileY = bedSpot.Y,
                    PositionX = Game1.player.Position.X,
                    PositionY = Game1.player.Position.Y
                };

                // Comprobar si hay niveles o profesiones pendientes antes de desconectar al cliente
                LevelUpMenu.AddMissedProfessionChoices(Game1.player);
                if (Game1.player.newLevels.Count > 0)
                {
                    pendingBedLogoutPayload = payload;
                    isBedLogoutPendingAfterLevels = true;
                    Point p = Game1.player.newLevels[0];
                    lastPresentedLevel = p;
                    context.Monitor.Log($"[BedLogoutModule] Mostrando menú de nivel (cama) para {Game1.player.Name}: habilidad {p.X}, nivel {p.Y}.", LogLevel.Info);
                    Game1.activeClickableMenu = new LevelUpMenu(p.X, p.Y);
                    return false;
                }

                ExecuteClientBedLogout(payload);
                return false; // Cancela el startSleep() nativo que pausaría/cerraría la sesión para todos
            }

            return true;
        }

        private static void ExecuteHostRest()
        {
            HostRestResult hostRest = RestHostPlayer(triggeredFromApi: false);
            Game1.addHUDMessage(new HUDMessage(hostRest.Message, HUDMessage.newQuest_type));

            if (hostRest.ShouldDisconnectSession)
            {
                DelayedAction.functionAfterDelay(() =>
                {
                    Game1.ExitToTitle();
                }, 1500);
            }
        }

        private static void ExecuteClientBedLogout(BedLogoutPayload payload)
        {
            if (context == null) return;

            // Cliente granjero: enviar mensaje al Host para registrar el descanso y salir al título
            context.Helper.Multiplayer.SendMessage(
                message: payload,
                messageType: BedLogoutMessageType,
                modIDs: new[] { context.Manifest.UniqueID },
                playerIDs: new[] { Game1.MasterPlayer.UniqueMultiplayerID }
            );

            Game1.addHUDMessage(new HUDMessage(I18n.Get("bed_logout.disconnecting"), HUDMessage.newQuest_type));

            // Salir al menú principal limpiamente tras 1.5s
            DelayedAction.functionAfterDelay(() =>
            {
                Game1.ExitToTitle();
            }, 1500);
        }

        public sealed class OfflineRecoveryPayload
        {
            public int StaminaGained { get; set; }
            public int HealthGained { get; set; }
        }
    }
}
