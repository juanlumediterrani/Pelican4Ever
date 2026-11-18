using System;
using System.Collections.Generic;
using HarmonyLib;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Config;
using Pelican4Ever.Framework;
using Pelican4Ever.Modules;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Pelican4Ever
{
    /// <summary>
    /// Punto de entrada SMAPI. El mismo DLL se instala en el servidor y en todos los clientes;
    /// cada módulo declara si su lógica es de host, de cliente o de ambos (ver ADR-0004).
    /// </summary>
    public class ModEntry : Mod
    {
        private ModContext context = null!;
        private SystemdWatchdogService? watchdog;
        private bool lockdownKickLogged;
        private bool initialHealthEventsEmitted;

        public override void Entry(IModHelper helper)
        {
            I18n.Init(helper.Translation);

            ServerConfig config = helper.ReadConfig<ServerConfig>();
            IReadOnlyList<string> errors = config.Validate();
            if (errors.Count > 0)
            {
                foreach (string error in errors)
                    Monitor.Log($"config.json: {error}", LogLevel.Error);
                Monitor.Log("Configuración inválida: Pelican 4Ever queda DESACTIVADO hasta corregir config.json.", LogLevel.Error);
                return;
            }

            context = new ModContext(helper, new CapturingMonitor(Monitor), ModManifest, config, new Harmony(ModManifest.UniqueID));

            Diagnostics.Configure(helper.DirectoryPath);

            // Orden de registro = orden de dependencia. Cada iteración del backlog añade aquí sus módulos.
            RegisterModules(
                new PersistenceModule(),
                new ClientVersionGuardModule(),
                new RolloverLockModule(),
                new ClientSecurityModule(),
                new IdleKickModule(),
                new StatusCommandModule(),
                new DebugGiveModule(),
                new FarmTestCommandsModule(),
                new RealTimeClockModule(),
                new BedLogoutModule(),
                new DailyRolloverModule(),
                new CheckpointModule(),
                new CheckpointSpikeModule(),
                new FarmSlotModule(),
                new AnnexReviewModule(),
                new FarmTypeModule(),
                new FarmTypeLootModule(),
                new VisitorGuardModule(),
                new BombRestrictionsModule(),
                new TownMailboxModule(),
                new MailOrderModule(),
                new ChatChannelModule(),
                new CommunityMarketModule(),
                new EconomyModule(),
                new FurnaceUnlockModule(),
                new RecipeFallbackModule(),
                new HostEventRelayModule(),
                new SkillExperienceModule(),
                new SkullCavernDescentModule(),
                new MineLadderModule(),
                new FishingRebalanceModule(),
                new DailyGoldTelemetryModule(),
                new GroundSitModule(),
                new WorldLifeModule(),
                new FestivalMinigameModule(),
                new FestivalAccessModule(),
                new FestivalParticipationModule(),
                new AgentCosmeticsModule(),
                new ProbationTelemetryModule(),
                new AdminApiModule(),
                new AutoHostModule(),
                new HeadlessRenderModule(),
                new HeadlessEventGuardModule(),
                new ClientPetAdoptionModule(),
                new ModToggleModule()
            );

            EvaluateAndReportHealth(saveLoaded: false, emitEvents: true);
            context.SyncInitialActivationState();

            context.RawHelper.Events.GameLoop.GameLaunched += OnGameLaunched;
            context.RawHelper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            context.RawHelper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            context.RawHelper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            context.RawHelper.Events.Multiplayer.PeerConnected += OnPeerConnectedLockdownGuard;
            context.RawHelper.Events.Multiplayer.PeerContextReceived += OnPeerContextReceivedLockdownGuard;

            watchdog = new SystemdWatchdogService(context.HealthTracker, () => context.Config.HangThresholdSeconds, Monitor);
            watchdog.Start();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => watchdog?.Dispose();

            if (!context.IsModEnabled)
                Monitor.Log("Pelican 4Ever inicia DESACTIVADO internamente por configuración (config.json -> Enabled=false).", LogLevel.Info);

            Monitor.Log($"Cargado (cambio de fecha {config.CalendarDayChangeTime}, inicio de reloj/horarios {config.DayStartTime}, Madrugada desde {config.NightHoldStartTime}, época {config.CalendarEpoch}, zona '{config.TimeZoneId}').", LogLevel.Info);
        }

        /// <summary>API para otros mods (por ejemplo, el puente de Stardew Voyager). Ver <see cref="PelicanApi"/>.</summary>
        public override object? GetApi() => context == null || !context.IsModEnabled ? null : new PelicanApi();

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            context.SyncInitialActivationState();
            EvaluateAndReportHealth(saveLoaded: Context.IsWorldReady, emitEvents: false);
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            bool worldReady = Context.IsWorldReady;
            context.HealthTracker.RecordTick(worldReady);

            if (context.IsLockdown && worldReady && Context.IsMainPlayer)
            {
                if (!lockdownKickLogged)
                {
                    lockdownKickLogged = true;
                    Monitor.Log("[Lockdown] Se cierra la sesión de partida porque el servidor está en estado LOCKDOWN.", LogLevel.Error);
                }
                Game1.ExitToTitle();
            }
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            lockdownKickLogged = false;
            context.HealthTracker.RecordTick(true);
            EvaluateAndReportHealth(saveLoaded: true, emitEvents: false);

            if (context.IsLockdown && Context.IsMainPlayer)
            {
                Monitor.Log("[Lockdown] Partida abortada al cargar: el servidor está en estado LOCKDOWN.", LogLevel.Error);
                Game1.ExitToTitle();
            }
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            lockdownKickLogged = false;
            context.HealthTracker.RecordTick(false);
            EvaluateAndReportHealth(saveLoaded: false, emitEvents: false);
        }

        private void OnPeerConnectedLockdownGuard(object? sender, PeerConnectedEventArgs e)
        {
            if (context.IsLockdown && Context.IsMainPlayer && !e.Peer.IsHost)
            {
                Monitor.Log($"[Lockdown] Rechazando conexión de par {e.Peer.PlayerID}: servidor en LOCKDOWN.", LogLevel.Warn);
                Game1.server?.kick(e.Peer.PlayerID);
            }
        }

        private void OnPeerContextReceivedLockdownGuard(object? sender, PeerContextReceivedEventArgs e)
        {
            if (context.IsLockdown && Context.IsMainPlayer && !e.Peer.IsHost)
            {
                Monitor.Log($"[Lockdown] Rechazando contexto de par {e.Peer.PlayerID}: servidor en LOCKDOWN.", LogLevel.Warn);
                Game1.server?.kick(e.Peer.PlayerID);
            }
        }

        private void EvaluateAndReportHealth(bool saveLoaded, bool emitEvents)
        {
            string gameVersion = Game1.version;
            string smapiVersion = Constants.ApiVersion.ToString();
            PatchHealthDecision decision = context.EvaluateHealth(gameVersion, smapiVersion, saveLoaded);

            if (!emitEvents && initialHealthEventsEmitted)
                return;
            initialHealthEventsEmitted = true;

            if (decision.OverriddenPatchNames.Count > 0)
            {
                string joined = string.Join(", ", decision.OverriddenPatchNames);
                Monitor.Log($"[LockdownOverride] Se han exceptuado parches críticos por configuración: {joined}.", LogLevel.Warn);
                AdminApiModule.EventLog.Append(
                    HostEventTypes.HostLockdownOverridden,
                    0,
                    600,
                    $"LockdownOverride activo para parches: {joined}.",
                    new Dictionary<string, object?> { ["patches"] = decision.OverriddenPatchNames });
            }

            if (decision.IsLockdown)
            {
                string reasons = string.Join("; ", decision.LockdownReasons);
                Monitor.Log($"[LOCKDOWN] Servidor bloqueado (fail-closed). Motivos: {reasons}", LogLevel.Error);
                AdminApiModule.EventLog.Append(
                    HostEventTypes.HostLockdown,
                    0,
                    600,
                    $"Servidor en LOCKDOWN: {reasons}",
                    new Dictionary<string, object?> { ["reasons"] = decision.LockdownReasons });
            }
            else if (decision.Status == HostHealthStatus.Degraded)
            {
                Monitor.Log($"[Salud] Servidor arrancado en estado DEGRADED ({decision.FailedPatches.Count} parches con fallo no bloqueante o exceptuados).", LogLevel.Warn);
                AdminApiModule.EventLog.Append(
                    HostEventTypes.HostDegraded,
                    0,
                    600,
                    $"Servidor en estado Degraded ({decision.FailedPatches.Count} fallo(s)).",
                    new Dictionary<string, object?>
                    {
                        ["failedCount"] = decision.FailedPatches.Count,
                        ["overridden"] = decision.OverriddenPatchNames
                    });
            }
        }

        private void RegisterModules(params IModule[] modules)
        {
            foreach (IModule module in modules)
            {
                if (!string.Equals(module.Name, "ModToggle", StringComparison.OrdinalIgnoreCase) &&
                    !context.Config.IsModuleEnabled(module.Name))
                {
                    Monitor.Log($"Módulo '{module.Name}' desactivado en config.json.", LogLevel.Info);
                    continue;
                }

                try
                {
                    module.Register(context);
                    Monitor.Log($"Módulo '{module.Name}' ({module.Side}) registrado.", LogLevel.Trace);
                }
                catch (Exception ex)
                {
                    context.PatchRegistry.RecordModuleFailure(module.Name, ex);
                    Monitor.Log($"Módulo '{module.Name}' falló al registrarse y queda desactivado:\n{ex}", LogLevel.Error);
                }
            }
        }
    }
}
