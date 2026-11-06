using System;

namespace Pelican4Ever.Core.Economy
{
    /// <summary>
    /// Reparto uniforme de una recuperación pasiva (p. ej. el buff del Spa: 120 puntos en 120 minutos) en ticks de un segundo.
    /// </summary>
    public static class PassiveRegenLogic
    {
        /// <summary>
        /// Suma a <paramref name="carry"/> la fracción de punto que corresponde a un segundo y devuelve los puntos enteros
        /// que hay que conceder ahora; la parte decimal queda acumulada para el siguiente tick.
        /// </summary>
        public static int TickOneSecond(ref double carry, int totalPoints, int durationMinutes)
        {
            if (totalPoints <= 0 || durationMinutes <= 0)
            {
                carry = 0;
                return 0;
            }

            carry += totalPoints / (durationMinutes * 60.0);
            int whole = (int)Math.Floor(carry + 1e-9);
            carry = Math.Max(0, carry - whole);
            return whole;
        }
    }
}
