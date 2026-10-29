using System;

namespace Pelican4Ever.Core.Players
{
    /// <summary>
    /// Reglas del sueño asíncrono ("Bed-Logout"). Funciones puras sobre <see cref="PlayerRecord"/>.
    /// </summary>
    public static class SleepPolicy
    {
        /// <summary>Estado visible del jugador en el día de juego <paramref name="currentGameDay"/>.</summary>
        public static SleepState GetState(PlayerRecord player, int currentGameDay, bool isOnline)
        {
            if (player.RestedOnGameDay == currentGameDay)
                return SleepState.Rested;

            if (isOnline || player.LastActiveGameDay == currentGameDay)
                return SleepState.Awake;

            return player.LastOutcome == RolloverOutcome.Penalized
                ? SleepState.OfflinePenalized
                : SleepState.OfflineSafe;
        }

        /// <summary>
        /// Evalúa el Rollover que cierra el día de juego <paramref name="endingGameDay"/>.
        /// <list type="bullet">
        /// <item>Descansó ese día → <see cref="RolloverOutcome.Rested"/>.</item>
        /// <item>Jugó ese día (o sigue conectado) sin descansar → <see cref="RolloverOutcome.Penalized"/>.</item>
        /// <item>No jugó ese día → <see cref="RolloverOutcome.Untouched"/> (nunca se penaliza dos veces).</item>
        /// </list>
        /// </summary>
        public static RolloverOutcome Evaluate(PlayerRecord player, int endingGameDay, bool isOnline)
        {
            if (player.RestedOnGameDay == endingGameDay)
                return RolloverOutcome.Rested;

            if (isOnline || player.LastActiveGameDay == endingGameDay)
                return RolloverOutcome.Penalized;

            return RolloverOutcome.Untouched;
        }

        /// <summary>Aplica el resultado al registro (no toca el <c>Farmer</c>; eso lo hace el módulo del juego).</summary>
        public static void Commit(PlayerRecord player, RolloverOutcome outcome)
        {
            if (outcome != RolloverOutcome.Untouched)
                player.LastOutcome = outcome;
        }

        /// <summary>Gasto médico por no dormir: porcentaje del oro con tope.</summary>
        public static int MedicalFee(int gold, float percent, int cap)
        {
            if (gold <= 0)
                return 0;

            return Math.Min(cap, (int)Math.Floor(gold * percent));
        }

        /// <summary>Energía con la que se despierta un jugador según el resultado.</summary>
        public static float WakeUpEnergy(RolloverOutcome outcome, int maxStamina, float penaltyFraction, float currentStamina)
        {
            return outcome switch
            {
                RolloverOutcome.Rested => maxStamina,
                RolloverOutcome.Penalized => (float)Math.Ceiling(maxStamina * penaltyFraction),
                _ => currentStamina
            };
        }
    }
}
