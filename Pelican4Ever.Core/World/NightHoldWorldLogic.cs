using System;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Reglas y cálculos puros para la simulación ambiental durante la Madrugada (NightHold).
    /// Desacopladas de SMAPI y Stardew Valley para pruebas unitarias directas.
    /// </summary>
    public static class NightHoldWorldLogic
    {
        public const int MinBubbleLifetimeMinutes = 60;
        public const int MaxBubbleLifetimeMinutes = 180;

        /// <summary>
        /// Determina si una burbuja de pesca debe expirar según los minutos transcurridos y una tirada aleatoria.
        /// Replica la fórmula nativa de Stardew Valley: expiración a partir de 60 minutos con probabilidad 0.1 + (minutos / 1800).
        /// Tras 180 minutos reales (3 horas) expira de forma garantizada.
        /// </summary>
        public static bool ShouldBubbleExpire(int minutesElapsed, double roll)
        {
            if (minutesElapsed < MinBubbleLifetimeMinutes)
                return false;

            if (minutesElapsed >= MaxBubbleLifetimeMinutes)
                return true;

            double threshold = 0.1 + ((double)minutesElapsed / 1800.0);
            return roll < threshold;
        }

        /// <summary>
        /// Determina si debe intentarse el spawn de una burbuja de pesca (50 % de probabilidad nativa).
        /// </summary>
        public static bool ShouldSpawnBubble(double roll) => roll < 0.5;

        /// <summary>
        /// Determina si debe generarse un punto de bateo de cobre (50 % de probabilidad nativa).
        /// </summary>
        public static bool ShouldSpawnOrePan(double roll) => roll < 0.5;

        /// <summary>
        /// Determina si un punto de bateo de cobre existente debe disolverse (10 % de probabilidad nativa).
        /// </summary>
        public static bool ShouldOrePanExpire(double roll) => roll < 0.1;

        /// <summary>
        /// Determina si debe spawnear un monstruo nocturno según la suerte del equipo y una tirada aleatoria.
        /// Replica la fórmula nativa de Farm.performTenMinuteUpdate: roll &lt; (0.25 - averageLuck / 2.0).
        /// </summary>
        public static bool ShouldSpawnNightMonster(double averageDailyLuck, double roll)
        {
            double threshold = 0.25 - (averageDailyLuck / 2.0);
            return roll < threshold;
        }

        /// <summary>
        /// Determina si el monstruo generado es volador (25 % de probabilidad nativa).
        /// </summary>
        public static bool IsFlyingMonster(double roll) => roll < 0.25;
    }
}
