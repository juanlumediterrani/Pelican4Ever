namespace Pelican4Ever.Models
{
    public class DoorModel
    {
        public string DoorId { get; set; } = string.Empty;
        public string LocationName { get; set; } = "Town";
        public int TownTileX { get; set; }
        public int TownTileY { get; set; }
        public int? MailboxTileX { get; set; }
        public int? MailboxTileY { get; set; }
        public string DefaultSlotId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public static class ModMessages
    {
        public const string ClaimDoorRequest = "ClaimDoorRequest";
        public const string ChangePolicyRequest = "ChangePolicyRequest";
    }

    public class ClaimDoorPayload
    {
        public string DoorId { get; set; } = string.Empty;
        public string FarmType { get; set; } = "Standard";
    }

    public class ChangePolicyPayload
    {
        public string FarmSlotId { get; set; } = string.Empty;
        public string Policy { get; set; } = string.Empty;
    }
}
