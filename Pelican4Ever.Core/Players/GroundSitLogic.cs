namespace Pelican4Ever.Core.Players
{
    /// <summary>Fotograma del sprite base del granjero (del propio juego) y si se dibuja volteado.</summary>
    public readonly record struct FarmerFrame(int Frame, bool Flipped);

    /// <summary>Situación del jugador local que decide si puede sentarse en el suelo o debe levantarse.</summary>
    public readonly record struct GroundSitConditions(
        bool Swimming,
        bool RidingHorse,
        bool Eating,
        bool UsingTool,
        bool InFurnitureSeat,
        bool EventUp)
    {
        /// <summary>True si alguna circunstancia impide estar sentado en el suelo.</summary>
        public bool Blocks => Swimming || RidingHorse || Eating || UsingTool || InFurnitureSeat || EventUp;
    }

    /// <summary>Estado de "sentado en el suelo" que cada cliente difunde para que los demás vean la pose.</summary>
    public class GroundSitState
    {
        public long PlayerId { get; set; }

        public bool IsSitting { get; set; }

        /// <summary>Dirección a la que mira (0 arriba, 1 derecha, 2 abajo, 3 izquierda).</summary>
        public int Direction { get; set; } = 2;

        public int Frame { get; set; }

        public bool Flipped { get; set; }
    }

    /// <summary>
    /// Lógica pura de sentarse en el suelo: pose según la dirección y cuándo se puede sentar o debe levantarse.
    /// Mientras dure "Bien comido", sentado en el suelo recupera energía igual que en un banco o silla (ADR-0006).
    /// </summary>
    public static class GroundSitLogic
    {
        /// <summary>Pose de sentado en el suelo según la dirección a la que mira el jugador.</summary>
        public static FarmerFrame SittingPoseFor(int facingDirection) => facingDirection switch
        {
            0 => new FarmerFrame(62, false),
            1 => new FarmerFrame(58, false),
            3 => new FarmerFrame(58, true),
            _ => new FarmerFrame(29, false)
        };

        /// <summary>True si el jugador puede sentarse ahora: libre (sin menús, chat ni diálogos) y sin nada que lo impida.</summary>
        public static bool CanSit(bool playerFree, GroundSitConditions conditions)
            => playerFree && !conditions.Blocks;

        /// <summary>True si un jugador sentado en el suelo debe levantarse.</summary>
        public static bool ShouldStandUp(GroundSitConditions conditions, bool isMoving, bool tookDamage, bool changedLocation)
            => isMoving || tookDamage || changedLocation || conditions.Blocks;

        /// <summary>Crea el estado a difundir cuando el jugador se sienta mirando en <paramref name="facingDirection"/>.</summary>
        public static GroundSitState SittingState(long playerId, int facingDirection)
        {
            FarmerFrame pose = SittingPoseFor(facingDirection);
            return new GroundSitState
            {
                PlayerId = playerId,
                IsSitting = true,
                Direction = facingDirection,
                Frame = pose.Frame,
                Flipped = pose.Flipped
            };
        }
    }
}
