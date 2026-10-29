using System;
using System.Collections.Generic;
using HarmonyLib;
using Pelican4Ever.Core.Config;
using Pelican4Ever.Framework;
using Pelican4Ever.Modules;
using StardewModdingAPI;

namespace Pelican4Ever
{
    /// <summary>
    /// Punto de entrada SMAPI. El mismo DLL se instala en el servidor y en todos los clientes;
    /// cada módulo declara si su lógica es de host, de cliente o de ambos (ver ADR-0004).
    /// </summary>
    public class ModEntry : Mod
    {
        private ModContext context = null!;

        public override void Entry(IModHelper helper)
        {
            ServerConfig config = helper.ReadConfig<ServerConfig>();
            IReadOnlyList<string> errors = config.Validate();
            if (errors.Count > 0)
            {
                foreach (string error in errors)
                    Monitor.Log($"config.json: {error}", LogLevel.Error);
                Monitor.Log("Configuración inválida: Pelican 4Ever queda DESACTIVADO hasta corregir config.json.", LogLevel.Error);
                return;
            }

            context = new ModContext(helper, Monitor, ModManifest, config, new Harmony(ModManifest.UniqueID));

            // Orden de registro = orden de dependencia. Cada iteración del backlog añade aquí sus módulos.
            RegisterModules(
                new PersistenceModule(),
                new ClientVersionGuardModule(),
                new StatusCommandModule(),
                new RealTimeClockModule(),
                new BedLogoutModule(),
                new DailyRolloverModule(),
                new FarmSlotModule(),
                new TownMailboxModule(),
                new ChatChannelModule(),
                new CommunityMarketModule(),
                new EconomyModule(),
                new WorldLifeModule(),
                new FestivalMinigameModule(),
                new AdminApiModule()
            );

            Monitor.Log($"Cargado (Rollover {config.DayStartTime}, Madrugada desde {config.NightHoldStartTime}, época {config.CalendarEpoch}, zona '{config.TimeZoneId}').", LogLevel.Info);
        }

        private void RegisterModules(params IModule[] modules)
        {
            foreach (IModule module in modules)
            {
                if (!context.Config.IsModuleEnabled(module.Name))
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
                    Monitor.Log($"Módulo '{module.Name}' falló al registrarse y queda desactivado:\n{ex}", LogLevel.Error);
                }
            }
        }
    }
}
