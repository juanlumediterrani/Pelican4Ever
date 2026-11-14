using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Economy;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>Publica una muestra duradera e idempotente por partida, día y jugador.</summary>
    public static class DailyEconomyTelemetry
    {
        public static void Record(ModContext context, PlayerRecord record, int day)
        {
            DailySkillExperience? xp = record.SkillExperience;
            DailyGoldEntry? gold = null;
            record.GoldLedger?.Days?.TryGetValue(day, out gold);
            bool hasXp = xp?.GameDay == day && xp.Skills != null && xp.Skills.Count > 0;
            if (record.LastActiveGameDay != day && !hasXp && gold == null) return;

            var skills = new Dictionary<string, object?>();
            if (hasXp)
                foreach (var pair in xp!.Skills!)
                    if (pair.Value != null)
                        skills[pair.Key] = new { before = pair.Value.RawXpToday, after = pair.Value.GrantedXpToday };
            var data = new Dictionary<string, object?>
            {
                ["playerId"] = record.UniqueMultiplayerID,
                ["playerName"] = record.PlayerName,
                ["closedGameDay"] = day,
                ["individualGrossCredits"] = (gold?.ClientIndividual ?? 0) + (gold?.HostIndividual ?? 0),
                ["sharedWalletGrossCredits"] = (gold?.ClientShared ?? 0) + (gold?.HostShared ?? 0),
                ["skills"] = skills
            };

            string directory = Path.Combine(context.Helper.DirectoryPath, "data", "economy-telemetry", Game1.uniqueIDForThisGame.ToString());
            string destination = Path.Combine(directory, $"{day}_{record.UniqueMultiplayerID}.json");
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(temporary, JsonSerializer.Serialize(data));
                try { File.Move(temporary, destination); }
                catch (IOException) when (File.Exists(destination)) { File.Delete(temporary); return; }
                AdminApiModule.EventLog.Append(HostEventTypes.DailyEconomy, day, Game1.timeOfDay,
                    $"Economía diaria de {record.PlayerName} ({record.UniqueMultiplayerID}).", data);
                context.Monitor.Log($"[DailyEconomy] Día {day}, jugador {record.UniqueMultiplayerID}: {JsonSerializer.Serialize(data)}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
                context.Monitor.Log($"[DailyEconomy] No se pudo registrar el día {day} del jugador {record.UniqueMultiplayerID}: {ex}", LogLevel.Error);
            }
        }
    }
}
