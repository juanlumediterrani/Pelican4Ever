using System;

namespace Pelican4Ever.Framework
{
    /// <summary>Dónde se ejecuta la lógica de un módulo. El mismo DLL se instala en host y clientes.</summary>
    [Flags]
    public enum ModuleSide
    {
        /// <summary>Lógica autoritativa: solo en el host (<c>Context.IsMainPlayer</c>).</summary>
        Host = 1,

        /// <summary>Lógica sobre el jugador local o la interfaz: en cada cliente (y en el host si juega).</summary>
        Client = 2,

        Both = Host | Client
    }

    /// <summary>
    /// Subsistema independiente del mod. Cada módulo se registra por separado y con su propio try/catch,
    /// de modo que un parche roto tras una actualización del juego no tumba el resto del mod.
    /// </summary>
    public interface IModule
    {
        /// <summary>Nombre estable; se usa como clave en <c>config.json → Modules</c>.</summary>
        string Name { get; }

        ModuleSide Side { get; }

        /// <summary>Suscribe eventos SMAPI, comandos y parches Harmony. Se llama una vez desde <c>Entry</c>.</summary>
        void Register(ModContext context);
    }
}
