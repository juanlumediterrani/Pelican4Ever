using System;
using HarmonyLib;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Botín de las cajas de suministros de las residencias de playa. <c>BreakableContainer.releaseContents</c> solo
    /// suelta su objeto exclusivo <c>(O)920</c> si la partida es de playa (<c>Game1.whichFarm == 6</c>); en otra partida da
    /// una Geoda omni. Corre en todos los lados porque la caja se rompe en el juego de quien la golpea: la playa se
    /// reconoce por la ruta de mapa de la ubicación, que está sincronizada.
    /// </summary>
    public sealed class FarmTypeLootModule : IModule
    {
        private static ModContext? context;

        public string Name => "FarmTypeLoot";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;
            try
            {
                ctx.Harmony.Patch(
                    original: AccessTools.Method(typeof(BreakableContainer), nameof(BreakableContainer.releaseContents)),
                    prefix: new HarmonyMethod(typeof(FarmTypeLootModule), nameof(Prefix_ReleaseContents)),
                    finalizer: new HarmonyMethod(typeof(FarmTypeLootModule), nameof(Finalizer_ReleaseContents))
                );
            }
            catch (Exception ex)
            {
                ctx.Monitor.Log($"[FarmTypeLoot] Error al aplicar parches Harmony: {ex}", LogLevel.Error);
            }
        }

        /// <summary>Durante el reparto del botín, la caja de una residencia de playa se trata como de partida de playa.</summary>
        public static void Prefix_ReleaseContents(BreakableContainer __instance, out int? __state)
        {
            __state = null;
            if (Game1.whichFarm != 6 && __instance.Location is Farm farm && FarmTypeVanillaLogic.IsBeachMap(farm.mapPath.Value))
            {
                __state = Game1.whichFarm;
                Game1.whichFarm = 6;
            }
        }

        /// <summary>Restaura el tipo de granja global aunque el reparto lance una excepción.</summary>
        public static Exception? Finalizer_ReleaseContents(Exception? __exception, int? __state)
        {
            if (__state.HasValue)
                Game1.whichFarm = __state.Value;
            return __exception;
        }
    }
}
