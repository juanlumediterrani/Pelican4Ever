using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Ajustes de dibujo para los cuerpos cosméticos de los agentes (p. ej. Dr4co, pack de Fashion Sense).
    /// Desplaza la herramienta en uso para que salga de las garras del cuerpo y no de su cara.
    /// No depende de Fashion Sense: solo lee el id del cuerpo en <c>Farmer.modData</c>, que se sincroniza,
    /// así que cada cliente con el mod dibuja bien también las herramientas de los demás.
    /// </summary>
    public sealed class AgentCosmeticsModule : IModule
    {
        private static ModContext? context;

        public string Name => "AgentCosmetics";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;
            try
            {
                ctx.Harmony.Patch(
                    original: AccessTools.Method(typeof(Game1), nameof(Game1.drawTool), new[] { typeof(Farmer), typeof(int) }),
                    prefix: new HarmonyMethod(typeof(AgentCosmeticsModule), nameof(Prefix_DrawTool)),
                    finalizer: new HarmonyMethod(typeof(AgentCosmeticsModule), nameof(Finalizer_DrawTool))
                );
                ctx.Monitor.Log("[AgentCosmeticsModule] Parche de herramienta para cuerpos de agente aplicado.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                ctx.Monitor.Log($"[AgentCosmeticsModule] Error al aplicar parches Harmony: {ex}", LogLevel.Error);
            }
        }

        /// <summary>Suma el desplazamiento a <c>armOffset</c> solo durante el dibujo de la herramienta.</summary>
        public static void Prefix_DrawTool(Farmer f, out Vector2 __state)
        {
            __state = Vector2.Zero;
            if (f == null)
                return;

            f.modData.TryGetValue(AgentToolOffset.BodyModDataKey, out string? bodyId);
            (int x, int y) = AgentToolOffset.Get(bodyId, f.FacingDirection);
            if (x == 0 && y == 0)
                return;

            __state = new Vector2(x, y);
            f.armOffset += __state;
        }

        /// <summary>Deshace el desplazamiento aunque el dibujo lance una excepción.</summary>
        public static Exception? Finalizer_DrawTool(Farmer f, Vector2 __state, Exception? __exception)
        {
            if (f != null && __state != Vector2.Zero)
                f.armOffset -= __state;
            return __exception;
        }
    }
}
