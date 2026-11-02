namespace Pelican4Ever.Core.Players
{
    /// <summary>
    /// Información enviada por el cliente al anfitrión cuando el jugador decide dormir en su cama.
    /// Registra el día, ubicación exacta de la cama y coordenadas para el despertar seguro.
    /// </summary>
    public class BedLogoutPayload
    {
        public int GameDay { get; set; }
        public string LocationName { get; set; } = string.Empty;
        public int BedTileX { get; set; }
        public int BedTileY { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
    }
}
