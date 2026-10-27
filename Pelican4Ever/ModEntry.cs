using System;
using HarmonyLib;
using Pelican4Ever.Config;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace Pelican4Ever
{
    public class ModEntry : Mod
    {
        public static ModEntry Instance { get; private set; } = null!;
        public static ServerConfig Config { get; private set; } = null!;
        public static IMonitor ModMonitor => Instance.Monitor;
        public static Harmony HarmonyInstance { get; private set; } = null!;
        public static ServerWorldState WorldState { get; private set; } = new();

        public override void Entry(IModHelper helper)
        {
            Instance = this;

            // Load or generate configuration
            Config = helper.ReadConfig<ServerConfig>();
            ModMonitor.Log($"[Pelican 4Ever] Loaded configuration (Rollover: {Config.RolloverTime}, Crops: {Config.CropPriceMultiplier}x, DedicatedMode: {Config.DedicatedServerMode}).", LogLevel.Info);

            // Initialize Harmony
            HarmonyInstance = new Harmony(ModManifest.UniqueID);
            try
            {
                HarmonyInstance.PatchAll();
                ModMonitor.Log($"[Pelican 4Ever] Harmony patches registered successfully.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                ModMonitor.Log($"[Pelican 4Ever] Failed applying Harmony patches: {ex}", LogLevel.Error);
            }

            // Hook events for WorldState persistence
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.Saving += OnSaving;

            // Register management console commands
            helper.ConsoleCommands.Add("p4e_status", "Displays current Pelican 4Ever server status.", OnStatusCheckCommand);
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            WorldState = Helper.Data.ReadSaveData<ServerWorldState>(ServerWorldState.SaveDataKey) ?? new ServerWorldState();
            ModMonitor.Log($"[Pelican 4Ever] Loaded ServerWorldState: {WorldState.DoorAssignments.Count} assigned doors, {WorldState.RescheduledFestivals.Count} rescheduled festivals.", LogLevel.Info);
        }

        private void OnSaving(object? sender, SavingEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            Helper.Data.WriteSaveData(ServerWorldState.SaveDataKey, WorldState);
            ModMonitor.Log($"[Pelican 4Ever] Persisted ServerWorldState to master save.", LogLevel.Trace);
        }

        private void OnStatusCheckCommand(string command, string[] args)
        {
            ModMonitor.Log($"=== Pelican 4Ever ({ModManifest.Version}) ===", LogLevel.Info);
            ModMonitor.Log($"Is Master Game: {Context.IsMainPlayer}", LogLevel.Info);
            ModMonitor.Log($"Rollover Target: {Config.RolloverTime}", LogLevel.Info);
            ModMonitor.Log($"Assigned Doors: {WorldState.DoorAssignments.Count}", LogLevel.Info);
            ModMonitor.Log($"Rescheduled Festivals: {WorldState.RescheduledFestivals.Count}", LogLevel.Info);
        }
    }
}
