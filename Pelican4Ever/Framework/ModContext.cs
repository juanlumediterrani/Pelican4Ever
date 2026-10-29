using HarmonyLib;
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
