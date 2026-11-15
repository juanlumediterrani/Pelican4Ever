# ADR-0002: 1:1 clock (`0600–2550`) with `NightHold` and a calendar derived from `CalendarEpoch`

* **Status:** Accepted
* **Date:** 2026-10-29

## Context

In `Game1.performTenMinuteClockUpdate()` (line 5896) the engine clamps `timeOfDay = Math.Min(timeOfDay, 2600)`, and in `Game1.Update()` (line 6452) it evaluates on every frame `if (timeOfDay >= 2600 || player.stamina <= -15f)` to force `player.startToPassOut()`. Also, `Farmer.passOutFromTired` does not tell whether the pass-out was caused by the clock or by energy exhaustion.

On the other hand, if the server calendar simply incremented one day on each rollover, any server crash during the date cut would permanently desynchronize the game's weekdays from the real week.

## Decision

1. **Clock and schedules aligned to real `06:00` (`DayStartTime = "06:00"`):** At real `06:00 AM` the clock starts at `0600` and advances 1:1 every 10 real minutes until `01:50 AM` (`2550`).
2. **Night hold stretch (`NightHoldStartTime = "01:50"`):** Between real `01:50 AM` and `06:00 AM`, `GameClock.Read` returns `TimeOfDay = 2550, IsNightHold = true`. The engine never reaches `2600`; the date change at 02:00 does not alter the light start or Pelican's schedules at 06:00.
3. **Perpetual calendar by epoch (`ServerCalendar`):** The game date (`TotalDays`) is computed as `(realDateAfterCalendarDayChangeTime - CalendarEpoch).Days`, with a fixed cut at 02:00 and an epoch that is a Monday. Between midnight and 02:00 it keeps the previous date. If the server was off, `ServerCalendar.PlanCatchUp` detects the drift.

## Consequences

* **Positive:** Zero invasive patches on `passOutFromTired`; machines process exactly 1440 minutes per real day; Day 1 of each season is always Monday even if the server was down.
* **Negative:** Between `01:50 AM` and `06:00 AM` the HUD visual clock stays at `01:50 AM` (`2550`); the date is already the new one since 02:00.
