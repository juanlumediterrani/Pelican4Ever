using System.Collections.Generic;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;

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
        [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
        public PortalEdge Edge { get; set; }
        public string DefaultSlotId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public DoorSpec ToDoorSpec() => new(
            DoorId,
            LocationName,
            TownTileX,
            TownTileY,
            Edge,
            DefaultSlotId,
            MailboxTileX,
            MailboxTileY
        );
    }

    public static class ModMessages
    {
        public const string ClaimDoorRequest = "ClaimDoorRequest";
        public const string ChangePolicyRequest = "ChangePolicyRequest";
        public const string BuyAnnexRequest = "BuyAnnexRequest";
        public const string UnlockGreenhouseRequest = "UnlockGreenhouseRequest";
        public const string SyncDoorsState = "SyncDoorsState";
        public const string RequestAnnexLocation = "RequestAnnexLocation";
        public const string SyncAnnexLocation = "SyncAnnexLocation";
        public const string RequestDoorsSync = "RequestDoorsSync";
        public const string PairingChallenge = "PairingChallenge";
        public const string PairingSubmit = "PairingSubmit";
        public const string PairingResult = "PairingResult";
    }

    public class PairingChallengePayload
    {
        public string Alias { get; set; } = string.Empty;
        public int RemainingAttempts { get; set; } = 3;
        public string PromptMessage { get; set; } = "Introduce tu código de pairing:";
    }

    public class PairingSubmitPayload
    {
        public string Code { get; set; } = string.Empty;
        public string ClientToken { get; set; } = string.Empty;
    }

    public class PairingResultPayload
    {
        public bool Ok { get; set; }
        public int RemainingAttempts { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class UnlockGreenhousePayload
    {
        public string FarmSlotId { get; set; } = string.Empty;
    }

    public class ClaimDoorPayload
    {
        public string DoorId { get; set; } = string.Empty;
        public string FarmType { get; set; } = "Standard";
    }

    public class BuyAnnexPayload
    {
        public string ResidenceSlotId { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public string TemplateId { get; set; } = string.Empty;
    }

    public class ChangePolicyPayload
    {
        public string FarmSlotId { get; set; } = string.Empty;
        public string Policy { get; set; } = string.Empty;

        /// <summary>Nuevo permiso de edición de las visitas; null lo deja como está.</summary>
        public bool? VisitorsCanEdit { get; set; }

        /// <summary>Jugador que se añade o quita de la lista de amigos; null no la cambia.</summary>
        public long? FriendId { get; set; }

        /// <summary>true añade a <see cref="FriendId"/>, false lo quita.</summary>
        public bool FriendAdd { get; set; }
    }

    public class SyncDoorsPayload
    {
        public Dictionary<string, DoorAssignment> Doors { get; set; } = new();
        public Dictionary<string, AnnexAssignment> Annexes { get; set; } = new();
        public Dictionary<long, PlayerRecord> Players { get; set; } = new();

        /// <summary>FarmSlotCount del host, para que el catálogo de los clientes muestre los mismos huecos.</summary>
        public int FarmSlotCount { get; set; }
    }
}
