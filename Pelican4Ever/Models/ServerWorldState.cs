using System;
using System.Collections.Generic;

namespace Pelican4Ever.Models
{
    public class ServerWorldState
    {
        public const string SaveDataKey = "Pelican4Ever.ServerWorldState";

        public int SchemaVersion { get; set; } = 1;

        /// <summary>
        /// Maps town door coordinate/key (e.g. "Town_45_60") to player's UniqueMultiplayerID.
        /// </summary>
        public Dictionary<string, long> DoorAssignments { get; set; } = new();

        /// <summary>
        /// Maps rescheduled festivals: key is original "seasonDay" (e.g. "spring13"), value is "seasonNewDay" (e.g. "spring16").
        /// </summary>
        public Dictionary<string, string> RescheduledFestivals { get; set; } = new();

        /// <summary>
        /// Timestamp of the last executed daily rollover.
        /// </summary>
        public DateTime LastRolloverTimestamp { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Timestamp of the last 12-hour mine shafts reset.
        /// </summary>
        public DateTime LastMineResetTimestamp { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Tracks registered instantiated farm names on the server.
        /// </summary>
        public HashSet<string> RegisteredInstantiatedFarms { get; set; } = new();
    }
}
