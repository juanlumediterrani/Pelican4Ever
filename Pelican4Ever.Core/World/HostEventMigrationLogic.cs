namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Instantánea del estado del mundo necesario para decidir la migración de eventos <c>/H</c>
    /// que pudieron quedar bloqueados antes de la versión 0.4.9.
    /// </summary>
    public sealed class HostEventMigrationInput
    {
        public int HostEventMigrationVersion { get; set; }
        public uint DaysPlayed { get; set; }
        public int TimeOfDay { get; set; }
        public bool IsWeatherSunny { get; set; }
        public bool IsJojaMember { get; set; }
        public bool HasCcDoorUnlock { get; set; }
        public bool AnyPlayerHasCcScrollOrText { get; set; }
        public bool HasCcIsCompleteMail { get; set; }
        public bool AnyPlayerSeenCcCeremony { get; set; }
        public bool AnyPlayerSeenJojaCeremony { get; set; }
        public bool HasRustyKey { get; set; }
        public int MuseumPiecesCount { get; set; }
        public bool AnyPlayerSeenMuseumRustyTrigger { get; set; }
    }

    /// <summary>Acciones idempotentes de reparación derivadas de <see cref="HostEventMigrationLogic"/>.</summary>
    public sealed record HostEventMigrationPlan(
        bool UnlockCommunityCenter,
        bool UnlockRustyKey,
        bool MarkCcCeremonySeen,
        bool MarkJojaCeremonySeen,
        bool StampMigrationVersion)
    {
        public bool HasAnyWorldChange =>
            UnlockCommunityCenter
            || UnlockRustyKey
            || MarkCcCeremonySeen
            || MarkJojaCeremonySeen;
    }

    /// <summary>
    /// Reglas puras para migrar y rescatar el estado de eventos <c>/H</c> cuyos hitos de mundo ya se
    /// superaron mientras estuvieron bloqueados (Centro Cívico <c>Town:611439</c>, Llave Oxidada
    /// <c>Farm:66</c> y consistencia de ceremonias <c>Town:191393</c>/<c>502261</c>).
    /// </summary>
    public static class HostEventMigrationLogic
    {
        /// <summary>Versión de migración de eventos correspondiente a la v0.4.9.</summary>
        public const int CurrentMigrationVersion = 1;

        public const string CcUnlockEventId = "611439";
        public const string CcDoorUnlockMailId = "ccDoorUnlock";
        public const string RatProblemQuestId = "26";
        public const string SeenJunimoNoteMailId = "seenJunimoNote";
        public const string CanReadJunimoTextMailId = "canReadJunimoText";
        public const string JojaMemberMailId = "JojaMember";

        public const string CcCeremonyEventId = "191393";
        public const string JojaCeremonyEventId = "502261";
        public const string CcIsCompleteMailId = "ccIsComplete";

        public const string RustyKeyEventId = "66";
        public const string MuseumRustyTriggerEventId = "295672";
        public const string HasRustyKeyMailId = "HasRustyKey";
        public const int RustyKeyMuseumThreshold = 60;

        /// <summary>Día mínimo jugado (<c>j 4</c> = <c>DaysPlayed &gt; 4</c>, es decir, Día 5+).</summary>
        public const uint CcUnlockMinDaysPlayedExclusive = 4;

        /// <summary>Fin de la ventana horaria del evento <c>Town:611439</c> (13:00).</summary>
        public const int CcUnlockWindowEndTime = 1300;

        /// <summary>
        /// Evalúa la migración única de v0.4.9 sobre una partida cargada.
        /// Si la versión de migración aún es inferior a <see cref="CurrentMigrationVersion"/>,
        /// desbloquea de inmediato cualquier evento cuyo hito ya se alcanzó aunque su ventana horaria
        /// o clima no coincidan en este momento.
        /// </summary>
        public static HostEventMigrationPlan EvaluateMigration(HostEventMigrationInput input)
        {
            bool needsMigration = input.HostEventMigrationVersion < CurrentMigrationVersion;

            bool unlockCc = needsMigration
                ? !input.IsJojaMember
                    && !input.HasCcDoorUnlock
                    && (input.DaysPlayed > CcUnlockMinDaysPlayedExclusive || input.AnyPlayerHasCcScrollOrText)
                : ShouldAutoUnlockCommunityCenter(
                    input.DaysPlayed,
                    input.TimeOfDay,
                    input.IsWeatherSunny,
                    input.IsJojaMember,
                    input.HasCcDoorUnlock,
                    input.AnyPlayerHasCcScrollOrText);

            bool unlockRustyKey = ShouldAutoUnlockRustyKey(
                input.HasRustyKey,
                input.AnyPlayerSeenMuseumRustyTrigger,
                input.MuseumPiecesCount);

            bool markCcCeremony = needsMigration
                && !input.IsJojaMember
                && input.HasCcIsCompleteMail
                && !input.AnyPlayerSeenCcCeremony;

            bool markJojaCeremony = needsMigration
                && input.IsJojaMember
                && input.HasCcIsCompleteMail
                && !input.AnyPlayerSeenJojaCeremony;

            return new HostEventMigrationPlan(
                UnlockCommunityCenter: unlockCc,
                UnlockRustyKey: unlockRustyKey,
                MarkCcCeremonySeen: markCcCeremony,
                MarkJojaCeremonySeen: markJojaCeremony,
                StampMigrationVersion: needsMigration);
        }

        /// <summary>
        /// Red de seguridad continua para reloj en tiempo real 1:1: si ya pasó el Día 5 y nunca se dio
        /// la combinación exacta de 08:00–13:00 + sol + entrada por BusStop (p. ej. si el servidor pasó
        /// de Día 4 a Día 5 y los jugadores solo conectan por la tarde o llueve), desbloquea la puerta.
        /// </summary>
        public static bool ShouldAutoUnlockCommunityCenter(
            uint daysPlayed,
            int timeOfDay,
            bool isWeatherSunny,
            bool isJojaMember,
            bool hasCcDoorUnlock,
            bool anyPlayerHasCcScrollOrText)
        {
            if (isJojaMember || hasCcDoorUnlock)
                return false;

            if (anyPlayerHasCcScrollOrText)
                return true;

            if (daysPlayed <= CcUnlockMinDaysPlayedExclusive)
                return false;

            // En el propio Día 5 damos margen por la mañana soleada para ver la escena normal;
            // si pasó de las 13:00, no hace sol o ya es Día 6+, desbloqueamos sin esperar más.
            return daysPlayed > CcUnlockMinDaysPlayedExclusive + 1
                || timeOfDay > CcUnlockWindowEndTime
                || !isWeatherSunny;
        }

        /// <summary>
        /// Red de seguridad para la Llave Oxidada (<c>Farm:66</c>): la ventana vanilla 06:00–07:00
        /// es de una sola hora real y el trigger <c>295672</c> puede quedar en un cliente.
        /// </summary>
        public static bool ShouldAutoUnlockRustyKey(
            bool hasRustyKey,
            bool anyPlayerSeenMuseumRustyTrigger,
            int museumPiecesCount)
        {
            if (hasRustyKey)
                return false;

            return anyPlayerSeenMuseumRustyTrigger || museumPiecesCount >= RustyKeyMuseumThreshold;
        }

        /// <summary>
        /// Determina si debe entregarse la misión <c>26</c> («Problema de ratas») a un jugador
        /// al sincronizar el desbloqueo por migración o fallback.
        /// </summary>
        public static bool ShouldGrantRatProblemQuest(
            bool alreadySawCcUnlockEvent,
            bool hasSeenJunimoNote,
            bool canReadJunimoText,
            bool hasQuest26)
        {
            return !alreadySawCcUnlockEvent
                && !hasSeenJunimoNote
                && !canReadJunimoText
                && !hasQuest26;
        }
    }
}
