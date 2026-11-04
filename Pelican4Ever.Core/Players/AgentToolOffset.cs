namespace Pelican4Ever.Core.Players
{
    /// <summary>
    /// Desplazamiento con el que se dibujan las herramientas (hacha, pico, etc.) cuando el jugador lleva
    /// un cuerpo de agente de Fashion Sense más bajo que el granjero vanilla.
    /// El juego dibuja la herramienta a la altura de las manos de un granjero de 32 px
    /// (<c>Game1.drawTool</c>: posición + <c>armOffset</c>) y Fashion Sense no permite moverla.
    /// Solo cambia el dibujo: la casilla objetivo de la herramienta no varía.
    /// </summary>
    public static class AgentToolOffset
    {
        /// <summary>Clave de <c>Farmer.modData</c> donde Fashion Sense guarda el cuerpo activo.</summary>
        public const string BodyModDataKey = "FashionSense.CustomBody.Id";

        /// <summary>Cuerpo de Dr4co (pack <c>agents/Dr4co.FashionSense</c>).</summary>
        public const string Dr4coBodyId = "Dr4co.Stardew.FashionSenseBody/Player/Dr4co";

        /// <summary>
        /// Desplazamiento en píxeles del mundo (4 por píxel de arte) según la dirección
        /// (0 arriba, 1 derecha, 2 abajo, 3 izquierda). (0, 0) si el cuerpo no es de un agente conocido.
        /// </summary>
        public static (int X, int Y) Get(string? bodyId, int facingDirection)
        {
            if (bodyId != Dr4coBodyId)
                return (0, 0);

            // Dr4co mide ~26 px y apoya los pies en la casilla: sus garras quedan ~4 px de arte más abajo
            // que las manos vanilla y, de perfil, más atrás que la punta del hocico.
            return facingDirection switch
            {
                0 => (0, 16),
                1 => (-8, 16),
                2 => (4, 16),
                3 => (16, 16),   // la fila izquierda del sprite va 2 px más hacia la cola
                _ => (0, 0)
            };
        }
    }
}
