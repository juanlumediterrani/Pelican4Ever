using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Config;
using Pelican4Ever.Core.World;
using StardewModdingAPI;

namespace Pelican4Ever.Framework
{
    /// <summary>
    /// Contexto compartido por todos los módulos. Sustituye a las propiedades estáticas sueltas de <c>ModEntry</c>.
    /// Los parches Harmony (que deben ser estáticos) acceden a él mediante <see cref="Current"/>.
    /// </summary>
    public sealed class ModContext
    {
        /// <summary>Contexto activo. Solo para parches Harmony; el resto del código debe recibirlo por parámetro.</summary>
        public static ModContext Current { get; private set; } = null!;

        public IModHelper Helper { get; }

        /// <summary>Helper sin filtrar para el módulo de activación/desactivación del mod y comandos de diagnóstico.</summary>
        public IModHelper RawHelper { get; }

        public IMonitor Monitor { get; }

        public IManifest Manifest { get; }

        public ServerConfig Config { get; }

        public Harmony Harmony { get; }

        /// <summary>Indica si Pelican 4Ever está activo actualmente en esta sesión de cliente.</summary>
        public bool IsModEnabled { get; private set; }

        /// <summary>Estado autoritativo del servidor. Solo es significativo en el host tras cargar la partida.</summary>
        public ServerWorldState WorldState { get; set; } = new();

        /// <summary>Registro centralizado de parches Harmony y su criticidad (TASK-2501).</summary>
        public PatchRegistry PatchRegistry { get; } = new();

        /// <summary>Instantánea atómica de salud observable fuera del hilo del juego (TASK-2503).</summary>
        public HostHealthTracker HealthTracker { get; } = new();

        /// <summary>Última evaluación de salud/criticidad (arranca en Starting hasta evaluar módulos y versiones).</summary>
        public PatchHealthDecision HealthDecision { get; private set; } = new() { Status = HostHealthStatus.Starting };

        /// <summary>Indica si el servidor debe bloquear la carga de partida y conexiones entrantes (TASK-2502).</summary>
        public bool IsLockdown => HealthDecision.IsLockdown;

        private readonly HarmonyPatchToggle patchToggle = new();

        private readonly Dictionary<string, Action<string, string[]>> commands = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Registra un comando de consola de SMAPI y recuerda su manejador para poder ejecutarlo a distancia por la API de
        /// anfitrión (<see cref="TryRunRemote"/>), ya que con la consola de SMAPI desactivada (servidor sin terminal) no hay
        /// otra forma de invocarlo. Misma firma que <c>Helper.ConsoleCommands.Add</c>.
        /// </summary>
        public void AddCommand(string name, string documentation, Action<string, string[]> callback)
        {
            Helper.ConsoleCommands.Add(name, documentation, callback);
            commands[name] = callback;
        }

        /// <summary>Si hay un comando registrado con ese nombre.</summary>
        public bool HasCommand(string name) => commands.ContainsKey(name);

        /// <summary>
        /// Ejecuta un comando registrado con <see cref="AddCommand"/> y recoge lo que escribe en el registro. Debe llamarse
        /// desde el hilo del juego. Una excepción del comando no se propaga: se devuelve como fallo.
        /// </summary>
        public bool TryRunRemote(string name, string[] args, out IReadOnlyList<string> output, out string? error)
        {
            output = Array.Empty<string>();
            error = null;
            if (!commands.TryGetValue(name, out Action<string, string[]>? callback))
            {
                error = $"El comando '{name}' no está registrado en este servidor.";
                return false;
            }

            using (RemoteCommandOutput.Begin())
            {
                try
                {
                    callback(name, args);
                }
                catch (Exception ex)
                {
                    Monitor.Log($"[RemoteCommand] {name} falló: {ex}", LogLevel.Error);
                    error = $"{ex.GetType().Name}: {ex.Message}";
                }

                output = RemoteCommandOutput.Snapshot();
            }

            return error == null;
        }

        public ModContext(IModHelper helper, IMonitor monitor, IManifest manifest, ServerConfig config, Harmony harmony)
        {
            RawHelper = helper;
            Config = config;
            IsModEnabled = ModActivationLogic.IsEffectivelyEnabled(config.Enabled, config.AutoHostSaveName);
            Helper = new GatedModHelper(helper, () => IsModEnabled);
            Monitor = monitor;
            Manifest = manifest;
            Harmony = harmony;
            Current = this;
        }

        /// <summary>
        /// Registra y aplica un parche Harmony a través del <see cref="PatchRegistry"/> centralizado (TASK-2501).
        /// </summary>
        public bool Patch(
            string name,
            string moduleName,
            PatchCriticality criticality,
            MethodBase? original,
            HarmonyMethod? prefix = null,
            HarmonyMethod? postfix = null,
            HarmonyMethod? transpiler = null,
            HarmonyMethod? finalizer = null,
            bool optional = false)
        {
            return PatchRegistry.Patch(
                Harmony,
                Monitor,
                name,
                moduleName,
                criticality,
                original,
                prefix,
                postfix,
                transpiler,
                finalizer,
                optional);
        }

        /// <summary>
        /// Recalcula la decisión de salud (<see cref="PatchHealthPolicy"/>) y actualiza la instantánea atómica de <c>/health</c>.
        /// </summary>
        public PatchHealthDecision EvaluateHealth(string? gameVersion, string? smapiVersion, bool saveLoaded)
        {
            PatchHealthDecision decision = PatchRegistry.Evaluate(Config, gameVersion, smapiVersion);
            HealthDecision = decision;
            HealthTracker.UpdateDecision(
                decision,
                gameVersion ?? string.Empty,
                smapiVersion ?? string.Empty,
                Manifest.Version.ToString(),
                saveLoaded);
            return decision;
        }

        /// <summary>
        /// Captura los parches Harmony aplicados hasta el momento y aplica el estado inicial de activación del mod.
        /// </summary>
        public void SyncInitialActivationState()
        {
            patchToggle.CaptureOwnedPatches(Harmony);
            if (!IsModEnabled)
                ApplyRuntimeActivation(enabled: false, invalidateCaches: true);
        }

        /// <summary>
        /// Activa o desactiva Pelican 4Ever en caliente, guardando la preferencia en config.json si corresponde.
        /// </summary>
        public void SetModEnabled(bool enabled, bool persist = true)
        {
            bool effectiveEnabled = ModActivationLogic.IsEffectivelyEnabled(enabled, Config.AutoHostSaveName);
            bool changed = IsModEnabled != effectiveEnabled;
            IsModEnabled = effectiveEnabled;

            if (persist && Config.Enabled != effectiveEnabled)
            {
                Config.Enabled = effectiveEnabled;
                RawHelper.WriteConfig(Config);
            }

            ApplyRuntimeActivation(effectiveEnabled, invalidateCaches: changed);
        }

        private void ApplyRuntimeActivation(bool enabled, bool invalidateCaches)
        {
            if (enabled)
            {
                patchToggle.Enable(Harmony);
                if (Context.IsMainPlayer && Config.IsModuleEnabled("RealTimeClock"))
                    StardewValley.Game1.realMilliSecondsPerGameTenMinutes = 600_000;
            }
            else
            {
                patchToggle.Disable(Harmony);
                StardewValley.Game1.realMilliSecondsPerGameTenMinutes = 7000;
            }

            if (invalidateCaches && StardewValley.Game1.content != null)
                InvalidateModAssets();
        }

        private void InvalidateModAssets()
        {
            RawHelper.GameContent.InvalidateCache("Data/Locations");
            RawHelper.GameContent.InvalidateCache("Data/Objects");
            RawHelper.GameContent.InvalidateCache("Data/Events/Farm");
            RawHelper.GameContent.InvalidateCache("Characters/Dialogue/Marnie");
            RawHelper.GameContent.InvalidateCache(asset =>
                asset.Name.BaseName.StartsWith("Data/Festivals/", StringComparison.OrdinalIgnoreCase) ||
                asset.Name.BaseName.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase));
        }
    }
}
