using System;

namespace Pelican4Ever.Core.World
{
    public static class MineProgressLogic
    {
        /// <summary>El progreso del ascensor pertenece al jugador; una misión activa puede limitarlo.</summary>
        public static int ElevatorLevel(int personalDepth, int orderDepth)
        {
            int level = Math.Max(0, Math.Min(personalDepth, 120));
            return orderDepth >= 0 ? Math.Min(level, orderDepth) : level;
        }
    }
}
