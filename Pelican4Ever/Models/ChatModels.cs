namespace Pelican4Ever.Models
{
    public static class ChatMessages
    {
        public const string LocalChat = "LocalChat";
        public const string ServerBroadcast = "ServerBroadcast";
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

    public class ServerBroadcastPayload
    {
        public string Source { get; set; } = "system";
        public string Author { get; set; } = string.Empty;
        public string Channel { get; set; } = "system";
        public string Message { get; set; } = string.Empty;
        public byte ColorR { get; set; } = 255;
        public byte ColorG { get; set; } = 215;
        public byte ColorB { get; set; } = 0;
        public bool IsCritical { get; set; }
    }
}
