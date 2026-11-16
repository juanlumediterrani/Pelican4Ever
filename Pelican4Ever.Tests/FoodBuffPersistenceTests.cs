using System;
using System.Collections.Generic;
using System.Text.Json;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests;

public class FoodBuffPersistenceTests
{
    [Fact]
    public void UpsertFoodBuff_ReplacesSameId_KeepsDistinctIds_AndPrunesExpired()
    {
        DateTimeOffset t0 = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
        var player = new PlayerRecord();

        player.UpsertFoodBuff(new PersistedFoodBuff
        {
            Id = "food",
            Source = "Spicy Eel",
            DisplaySource = "Anguila picante",
            TotalDurationMs = 4 * 3600 * 1000,
            ExpiresAtUtc = t0.AddHours(4),
            Effects = new FoodBuffEffectsSnapshot { LuckLevel = 1, Speed = 1 }
        }, t0);

        player.UpsertFoodBuff(new PersistedFoodBuff
        {
            Id = "drink",
            Source = "Triple Shot Espresso",
            TotalDurationMs = 2 * 3600 * 1000,
            ExpiresAtUtc = t0.AddHours(2),
            Effects = new FoodBuffEffectsSnapshot { Speed = 1 }
        }, t0);

        // 3 horas después: "drink" ya expiró, "food" sigue vigente y se sustituye por otro plato
        DateTimeOffset tLater = t0.AddHours(3);
        player.UpsertFoodBuff(new PersistedFoodBuff
        {
            Id = "food",
            Source = "Lucky Lunch",
            TotalDurationMs = 5 * 3600 * 1000,
            ExpiresAtUtc = tLater.AddHours(5),
            Effects = new FoodBuffEffectsSnapshot { LuckLevel = 3 }
        }, tLater);

        Assert.Single(player.ActiveFoodBuffs);
        Assert.Equal("food", player.ActiveFoodBuffs[0].Id);
        Assert.Equal("Lucky Lunch", player.ActiveFoodBuffs[0].Source);
        Assert.Equal(3f, player.ActiveFoodBuffs[0].Effects.LuckLevel);
    }

    [Fact]
    public void ToFoodBuffsPayload_And_ApplyFoodBuffsSnapshot_RespectOfflineElapsedTimeAcrossClockSkew()
    {
        DateTimeOffset hostEatTime = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        var hostRecord = new PlayerRecord();
        hostRecord.UpsertFoodBuff(new PersistedFoodBuff
        {
            Id = "food",
            Source = "Seafoam Pudding",
            TotalDurationMs = 5 * 3600 * 1000,
            ExpiresAtUtc = hostEatTime.AddHours(5),
            Effects = new FoodBuffEffectsSnapshot { FishingLevel = 4 }
        }, hostEatTime);
        hostRecord.UpsertFoodBuff(new PersistedFoodBuff
        {
            Id = "drink",
            Source = "Coffee",
            TotalDurationMs = 2 * 3600 * 1000,
            ExpiresAtUtc = hostEatTime.AddHours(2),
            Effects = new FoodBuffEffectsSnapshot { Speed = 1 }
        }, hostEatTime);

        // El jugador reconecta 3 horas después: el café (2h) caducó, el pudín (5h) conserva 2h restantes
        DateTimeOffset hostReconnectTime = hostEatTime.AddHours(3);
        FoodBuffsPayload payload = hostRecord.ToFoodBuffsPayload(hostReconnectTime);

        Assert.Single(payload.Buffs);
        Assert.Equal("food", payload.Buffs[0].Id);
        Assert.Equal(2 * 3600 * 1000, payload.Buffs[0].RemainingMilliseconds);

        // El reloj del cliente va adelantado 15 minutos respecto al host, pero usa la duración relativa
        DateTimeOffset skewedClientClock = hostReconnectTime.AddMinutes(15);
        var clientRecord = new PlayerRecord();
        clientRecord.ApplyFoodBuffsSnapshot(payload, skewedClientClock);

        Assert.Single(clientRecord.ActiveFoodBuffs);
        Assert.Equal(2 * 3600 * 1000, clientRecord.ActiveFoodBuffs[0].RemainingMilliseconds(skewedClientClock));
        Assert.Equal(skewedClientClock.AddHours(2), clientRecord.ActiveFoodBuffs[0].ExpiresAtUtc);
    }

    [Fact]
    public void ApplyFoodBuffsReport_FiltersDebuffs_ClampsDuration_SanitizesNaN_AndUsesHostClock()
    {
        DateTimeOffset hostNow = new DateTimeOffset(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);
        var record = new PlayerRecord();

        var report = new FoodBuffsPayload
        {
            Buffs = new List<FoodBuffReportEntry>
            {
                new FoodBuffReportEntry
                {
                    Id = "17", // Tipsy (debuff) -> rechazado
                    RemainingMilliseconds = 60_000
                },
                new FoodBuffReportEntry
                {
                    Id = "custom_bad_debuff",
                    RemainingMilliseconds = 60_000
                },
                new FoodBuffReportEntry
                {
                    Id = "custom_mod_debuff",
                    RemainingMilliseconds = 60_000
                },
                new FoodBuffReportEntry
                {
                    Id = "food",
                    RemainingMilliseconds = 0 // expirado
                },
                new FoodBuffReportEntry
                {
                    Id = "28", // Squid Ink Ravioli
                    Source = "Squid Ink Ravioli",
                    IconTextureName = "TileSheets\\BuffsIcons",
                    IconSheetIndex = 28,
                    RemainingMilliseconds = int.MaxValue, // por encima del tope
                    Effects = new FoodBuffEffectsSnapshot
                    {
                        MiningLevel = 1f,
                        Speed = float.NaN,
                        AttackMultiplier = float.PositiveInfinity
                    }
                }
            }
        };

        record.ApplyFoodBuffsReport(report, hostNow, id => id == "custom_mod_debuff");

        Assert.Single(record.ActiveFoodBuffs);
        PersistedFoodBuff saved = record.ActiveFoodBuffs[0];
        Assert.Equal("28", saved.Id);
        Assert.Equal(FoodBuffPersistenceLogic.MaxFoodBuffDurationMs, saved.RemainingMilliseconds(hostNow));
        Assert.Equal(hostNow.AddMilliseconds(FoodBuffPersistenceLogic.MaxFoodBuffDurationMs), saved.ExpiresAtUtc);
        Assert.Equal(1f, saved.Effects.MiningLevel);
        Assert.Equal(0f, saved.Effects.Speed);
        Assert.Equal(0f, saved.Effects.AttackMultiplier);
    }

    [Fact]
    public void ApplyFoodBuffsReport_CapsMaxBuffCount_AndDeduplicatesById()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 11, 16, 16, 0, 0, TimeSpan.Zero);
        var payload = new FoodBuffsPayload();
        for (int i = 0; i < FoodBuffPersistenceLogic.MaxPersistedFoodBuffs + 5; i++)
        {
            payload.Buffs.Add(new FoodBuffReportEntry
            {
                Id = $"custom_food_{i}",
                RemainingMilliseconds = 10_000 + i
            });
        }
        payload.Buffs.Add(new FoodBuffReportEntry
        {
            Id = "custom_food_0",
            RemainingMilliseconds = 99_000
        });

        var record = new PlayerRecord();
        record.ApplyFoodBuffsReport(payload, now);

        Assert.Equal(FoodBuffPersistenceLogic.MaxPersistedFoodBuffs, record.ActiveFoodBuffs.Count);
        Assert.Equal(99_000, record.ActiveFoodBuffs[0].RemainingMilliseconds(now));
    }

    [Fact]
    public void RemoveAndClearFoodBuffs_And_CopyFoodBuffsFrom_WorkWithoutAliasing()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 11, 16, 18, 0, 0, TimeSpan.Zero);
        var source = new PlayerRecord();
        source.UpsertFoodBuff(new PersistedFoodBuff
        {
            Id = "food",
            ExpiresAtUtc = now.AddHours(3),
            CustomFields = new Dictionary<string, string> { ["k"] = "v" }
        }, now);
        source.UpsertFoodBuff(new PersistedFoodBuff
        {
            Id = "drink",
            ExpiresAtUtc = now.AddHours(1)
        }, now);

        var copy = new PlayerRecord();
        copy.CopyFoodBuffsFrom(source);

        Assert.True(source.RemoveFoodBuff("drink"));
        Assert.False(source.RemoveFoodBuff("drink"));
        Assert.Equal(2, copy.ActiveFoodBuffs.Count);

        copy.ActiveFoodBuffs[0].CustomFields!["k"] = "mutated";
        Assert.Equal("v", source.ActiveFoodBuffs[0].CustomFields!["k"]);

        Assert.True(copy.ClearFoodBuffs());
        Assert.Empty(copy.ActiveFoodBuffs);
        Assert.Single(source.ActiveFoodBuffs);
    }

    [Fact]
    public void ServerWorldState_RoundTripAndLegacyMigration_HandlesActiveFoodBuffs()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 11, 16, 20, 0, 0, TimeSpan.Zero);
        var state = new ServerWorldState();
        PlayerRecord p = state.GetOrCreatePlayer(42L, "Aldo");
        p.UpsertFoodBuff(new PersistedFoodBuff
        {
            Id = "food",
            Source = "Pumpkin Soup",
            DisplaySource = "Sopa de calabaza",
            IconTextureName = "TileSheets\\BuffsIcons",
            IconSheetIndex = 4,
            GlowR = 255,
            GlowG = 200,
            GlowB = 100,
            GlowA = 128,
            TotalDurationMs = 10_800_000,
            ExpiresAtUtc = now.AddHours(3),
            Effects = new FoodBuffEffectsSnapshot { Defense = 2f, LuckLevel = 2f },
            CustomFields = new Dictionary<string, string> { ["mod"] = "test" }
        }, now);

        string json = JsonSerializer.Serialize(state);
        var restored = JsonSerializer.Deserialize<ServerWorldState>(json)!;

        PersistedFoodBuff restoredBuff = Assert.Single(restored.Players[42L].ActiveFoodBuffs);
        Assert.Equal("Pumpkin Soup", restoredBuff.Source);
        Assert.Equal("Sopa de calabaza", restoredBuff.DisplaySource);
        Assert.Equal(2f, restoredBuff.Effects.Defense);
        Assert.Equal(2f, restoredBuff.Effects.LuckLevel);
        Assert.Equal((byte)200, restoredBuff.GlowG);
        Assert.Equal("test", restoredBuff.CustomFields!["mod"]);

        // Legado con ActiveFoodBuffs nulo
        restored.Players[42L].ActiveFoodBuffs = null!;
        Assert.True(restored.Migrate());
        Assert.NotNull(restored.Players[42L].ActiveFoodBuffs);
        Assert.Empty(restored.Players[42L].ActiveFoodBuffs);
    }
}
