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

        /// <summary>
        /// En vanilla (<c>Game1._newDayAfterFade</c>), los cofres por encima del piso 120 (Caverna Calavera)
        /// se limpian al cambiar de día, mientras que los de las minas regulares (&lt;= 120) son de un solo uso.
        /// </summary>
        public static bool ShouldResetMineChestOnNewDay(int mineLevel) => mineLevel > 120;
    }
}
