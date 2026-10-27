namespace Pelican4Ever.Config
{
    public class ServerConfig
    {
        /// <summary>
        /// Daily rollover time in HH:mm format (24h). Default is 05:30.
        /// </summary>
        public string RolloverTime { get; set; } = "05:30";

        /// <summary>
        /// Real times of the day (HH:mm) when mine levels reset (12-hour shifts).
        /// </summary>
        public string[] MineResetTimes { get; set; } = new[] { "05:30", "17:30" };

        /// <summary>
        /// Multiplier applied to raw crop selling prices.
        /// </summary>
        public float CropPriceMultiplier { get; set; } = 2.2f;

        /// <summary>
        /// Multiplier applied to artisan goods and processed items.
        /// </summary>
        public float ArtisanPriceMultiplier { get; set; } = 1.8f;

        /// <summary>
        /// Maximum daily satiety buff stacks per player (daily dishes eaten).
        /// </summary>
        public int MaxDailySatietyStacks { get; set; } = 2;

        /// <summary>
        /// Amount of stamina regenerated per minute while satiety is active.
        /// </summary>
        public int SatietyRegenPerMinute { get; set; } = 1;

        /// <summary>
        /// Duration of satiety buff in real-time minutes.
        /// </summary>
        public int SatietyDurationMinutes { get; set; } = 60;

        /// <summary>
        /// Real seconds required in the Spa to restore 1 point of stamina.
        /// </summary>
        public int SpaRegenSeconds { get; set; } = 30;

        /// <summary>
        /// Whether the host instance runs as a dedicated headless server (hiding and freezing host farmer).
        /// </summary>
        public bool DedicatedServerMode { get; set; } = true;

        /// <summary>
        /// Timezone ID for server scheduling. If empty or invalid, system local time is used.
        /// </summary>
        public string TimeZoneId { get; set; } = "";
    }
}
