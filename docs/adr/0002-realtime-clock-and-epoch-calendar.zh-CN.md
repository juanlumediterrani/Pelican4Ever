# ADR-0002：1:1 时钟（`0600–2550`）与 `NightHold`，以及由 `CalendarEpoch` 推导的日历

* **状态：** 已接受
* **日期：** 2026-10-29

## 背景

在 `Game1.performTenMinuteClockUpdate()`（第 5896 行）中，引擎将 `timeOfDay = Math.Min(timeOfDay, 2600)`，并在 `Game1.Update()`（第 6452 行）中每帧计算 `if (timeOfDay >= 2600 || player.stamina <= -15f)` 以强制执行 `player.startToPassOut()`。此外，`Farmer.passOutFromTired` 并不区分晕倒是由时钟引起还是由体力耗尽引起。

另一方面，如果服务器日历只是在每次日切时简单地加一天，那么服务器在换日期间的任何崩溃都会让游戏中的星期几与真实世界永远错位。

## 决策

1. **时钟与日程对齐到真实的 `06:00`（`DayStartTime = "06:00"`）：** 在真实的 `06:00 AM`，时钟从 `0600` 启动，并按 1:1 每 10 分钟真实时间推进，直到 `01:50 AM`（`2550`）。
2. **深夜时段（`NightHoldStartTime = "01:50"`）：** 在真实的 `01:50 AM` 与 `06:00 AM` 之间，`GameClock.Read` 返回 `TimeOfDay = 2550, IsNightHold = true`。引擎不会到达 `2600`；02:00 的换日不会改变 06:00 的光照开始时间或 Pelican 的日程。
3. **基于纪元的永久日历（`ServerCalendar`）：** 游戏日期（`TotalDays`）按 `(fechaRealTrasCalendarDayChangeTime - CalendarEpoch).Days` 计算，换日时刻固定在 02:00，且纪元为一个星期一。在午夜到 02:00 之间仍沿用前一天。如果服务器曾处于关机状态，`ServerCalendar.PlanCatchUp` 会检测出偏差。

## 后果

* **积极面：** 对 `passOutFromTired` 没有任何侵入式补丁；机器每个真实日恰好处理 1440 分钟；即使服务器曾宕机，每个季节的第 1 天始终是星期一。
* **消极面：** 在 `01:50 AM` 与 `06:00 AM` 之间，HUD 上的可视时钟停留在 `01:50 AM`（`2550`）；而日期从 02:00 起就已经是新的一天。
