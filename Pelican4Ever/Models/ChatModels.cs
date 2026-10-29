namespace Pelican4Ever.Models
{
    public static class ChatMessages
    {
        public const string LocalChat = "LocalChat";
    }

    public class LocalChatMessagePayload
    {
        public long SenderId { get; set; }
        public string SenderName { get; set; } = string.Empty;
        public string LocationName { get; set; } = string.Empty;
        public float TileX { get; set; }
        public float TileY { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
