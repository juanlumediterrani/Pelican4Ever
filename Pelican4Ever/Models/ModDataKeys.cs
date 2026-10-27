using System;
using StardewValley;

namespace Pelican4Ever.Models
{
    public static class ModDataKeys
    {
        // Claves en Farmer.modData (sincronizadas por red automáticamente)
        public const string SleepState = "Pelican4Ever.SleepState";
        public const string AssignedFarmId = "Pelican4Ever.FarmLocation";
        public const string FarmMapType = "Pelican4Ever.FarmMapType";
        public const string SatietyStacks = "Pelican4Ever.SatietyStacks";
        public const string LastLoginDay = "Pelican4Ever.LastLoginDay";

        // Claves en GameLocation.modData (para control de acceso a granjas)
        public const string FarmOwnerId = "Pelican4Ever.OwnerUniqueMultiplayerID";
        public const string DoorAccessPolicy = "Pelican4Ever.AccessPolicy";
    }

    public static class SleepStates
    {
        public const string Awake = "Awake";
        public const string Rested = "Rested";
        public const string OfflineSafe = "Offline_Safe";
        public const string OfflinePenalized = "Offline_Penalized";
    }

    public static class DoorAccessPolicies
    {
        public const string Public = "Public";
        public const string Friends = "Friends";
        public const string Locked = "Locked";
    }

    public static class ModDataExtensions
    {
        public static string GetSleepState(this Farmer farmer)
        {
            if (farmer.modData.TryGetValue(ModDataKeys.SleepState, out string? value) && !string.IsNullOrWhiteSpace(value))
                return value;
            return SleepStates.Awake;
        }

        public static void SetSleepState(this Farmer farmer, string state)
        {
            farmer.modData[ModDataKeys.SleepState] = state;
        }

        public static string? GetAssignedFarmId(this Farmer farmer)
        {
            return farmer.modData.TryGetValue(ModDataKeys.AssignedFarmId, out string? value) ? value : null;
        }

        public static void SetAssignedFarmId(this Farmer farmer, string farmId)
        {
            farmer.modData[ModDataKeys.AssignedFarmId] = farmId;
        }

        public static int GetSatietyStacks(this Farmer farmer)
        {
            if (farmer.modData.TryGetValue(ModDataKeys.SatietyStacks, out string? raw) && int.TryParse(raw, out int stacks))
                return stacks;
            return 0;
        }

        public static void SetSatietyStacks(this Farmer farmer, int stacks)
        {
            farmer.modData[ModDataKeys.SatietyStacks] = stacks.ToString();
        }

        public static long? GetFarmOwnerId(this GameLocation location)
        {
            if (location.modData.TryGetValue(ModDataKeys.FarmOwnerId, out string? raw) && long.TryParse(raw, out long ownerId))
                return ownerId;
            return null;
        }

        public static void SetFarmOwnerId(this GameLocation location, long ownerId)
        {
            location.modData[ModDataKeys.FarmOwnerId] = ownerId.ToString();
        }

        public static string GetDoorAccessPolicy(this GameLocation location)
        {
            if (location.modData.TryGetValue(ModDataKeys.DoorAccessPolicy, out string? policy) && !string.IsNullOrWhiteSpace(policy))
                return policy;
            return DoorAccessPolicies.Locked;
        }

        public static void SetDoorAccessPolicy(this GameLocation location, string policy)
        {
            location.modData[ModDataKeys.DoorAccessPolicy] = policy;
        }
    }
}
