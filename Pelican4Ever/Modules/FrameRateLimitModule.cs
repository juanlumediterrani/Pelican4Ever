using System;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Limita los fotogramas por segundo del host (<c>MaxFramesPerSecond</c>). Sin GPU real el juego dibuja sin tope y
    /// satura la CPU aunque no haya jugadores; el tope fija el paso de tiempo del bucle de MonoGame.
    /// </summary>
    public sealed class FrameRateLimitModule : IModule
    {
        public string Name => "FrameRateLimit";

        public ModuleSide Side => ModuleSide.Host;

        public void Register(ModContext ctx)
        {
            int fps = ctx.Config.MaxFramesPerSecond;
            if (fps == 0)
                return;

            ctx.Helper.Events.GameLoop.GameLaunched += (_, _) => Apply(ctx, fps);
            ctx.Helper.Events.GameLoop.ReturnedToTitle += (_, _) => Apply(ctx, fps);
        }

        private static void Apply(ModContext ctx, int fps)
        {
            Game1.game1.IsFixedTimeStep = true;
            Game1.game1.TargetElapsedTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / fps);
            ctx.Monitor.Log($"[FrameRateLimit] Tope de {fps} fps aplicado al bucle del juego.", LogLevel.Info);
        }
    }
}
