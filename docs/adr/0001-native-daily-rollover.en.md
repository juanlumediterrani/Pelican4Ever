# ADR-0001: Native rollover with the date at 02:00 and dawn at 06:00

* **Status:** Accepted
* **Date:** 2026-11-02 (revision)

## Context

The initial design proposed running a manual loop at `05:30 AM` that incremented `Game1.dayOfMonth` and invoked `location.DayUpdate(dayOfMonth)` "hot" without going through `Game1.NewDay()`.

After decompiling `Stardew Valley.dll` (1.6.15), it was verified that the real day transition (`Game1.NewDay` → `_newDayAfterFade`) runs dozens of coupled subsystems: settlement of `personalShippingBin` per player, mail delivery, luck and weather calculation, quest and `SpecialOrders` progression, friendship updates, animals, fruit trees, `stats.DaysPlayed`, `netWorldState` and the `SaveGame.Save()` coroutine, which requires syncing all connected clients.

## Decision

1. The calendar date and the rollover happen at **02:00 AM**; `DayStartTime` stays at **06:00 AM** for the clock, the light and Pelican's schedules.
2. At 01:50 there is a single warning. At 02:00 `SaveGame.Save` runs, the save is copied to a temporary host backup and its files are verified with SHA-256. If any of those steps fails, nobody is disconnected and the day does not advance.
3. After the backup is verified, the host asks the clients to close individually. If they are still connected after 20 s, the native peer close is sent; the host waits for the confirmed disconnection up to 10 s more. If they are still connected, it aborts the day change instead of entering a blocked ReadyCheck.
4. Only with zero peers is `Game1.NewDay(0f)` invoked. The consequences of the closed day are processed after `DayStarted`; the clock stays in NightHold until 06:00. The peer close still needs a development rehearsal before any rollout.

## Consequences

* **Positive:** The backup of the connected state is taken before closing the clients; the native change is not invoked while a ReadyCheck is left waiting for peers; date and dawn have distinct contracts.
* **Negative:** If the peer close is not confirmed, the sequence fails and the day does not advance; it needs observation/repair and is not retried automatically.
