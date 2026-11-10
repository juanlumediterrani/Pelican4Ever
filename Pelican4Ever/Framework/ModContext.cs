using System;
using System.Collections.Generic;
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

        public IMonitor Monitor { get; }

        public IManifest Manifest { get; }

        public ServerConfig Config { get; }

        public Harmony Harmony { get; }

        /// <summary>Estado autoritativo del servidor. Solo es significativo en el host tras cargar la partida.</summary>
        public ServerWorldState WorldState { get; set; } = new();

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
            Helper = helper;
            Monitor = monitor;
            Manifest = manifest;
            Config = config;
            Harmony = harmony;
            Current = this;
        }
    }
}
