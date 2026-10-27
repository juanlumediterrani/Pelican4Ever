using System;
using StardewValley;

namespace Pelican4Ever.Models
{
    public class PlayerPersistentData
    {
        public long UniqueMultiplayerID { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public string SleepState { get; set; } = SleepStates.Awake;
        public string? AssignedFarmId { get; set; }
        public string? FarmMapType { get; set; }
        public int SatietyStacks { get; set; }
        public int LastLoginDay { get; set; }
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        public static PlayerPersistentData FromFarmer(Farmer farmer)
        {
            return new PlayerPersistentData
            {
                UniqueMultiplayerID = farmer.UniqueMultiplayerID,
                PlayerName = farmer.Name,
                SleepState = farmer.GetSleepState(),
                AssignedFarmId = farmer.GetAssignedFarmId(),
                FarmMapType = farmer.modData.TryGetValue(ModDataKeys.FarmMapType, out string? mapType) ? mapType : null,
                SatietyStacks = farmer.GetSatietyStacks(),
                LastLoginDay = farmer.modData.TryGetValue(ModDataKeys.LastLoginDay, out string? loginDayStr) && int.TryParse(loginDayStr, out int loginDay) ? loginDay : Game1.dayOfMonth,
                LastSeenUtc = DateTime.UtcNow
            };
        }

        public void ApplyToFarmer(Farmer farmer)
        {
            farmer.SetSleepState(SleepState);
            if (!string.IsNullOrWhiteSpace(AssignedFarmId))
                farmer.SetAssignedFarmId(AssignedFarmId);
            if (!string.IsNullOrWhiteSpace(FarmMapType))
                farmer.modData[ModDataKeys.FarmMapType] = FarmMapType;
            farmer.SetSatietyStacks(SatietyStacks);
            farmer.modData[ModDataKeys.LastLoginDay] = LastLoginDay.ToString();
        }
    }
}
