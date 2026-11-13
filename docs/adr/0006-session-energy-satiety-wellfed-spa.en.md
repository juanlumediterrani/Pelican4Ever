# ADR-0006: Energy per Session — Satiety with Continuous Drain, Seated "Well-Fed" and the Spa Buff

* **Status:** Accepted (replaces point 3 of [ADR-0004](0004-host-authority-client-mod-and-satiety.en.md))
* **Date:** 2026-11-06

## Context

ADR-0004 limited food energy to a **daily** budget (`+50%` of the bar per real day), with 2 "Well-Fed" buffs of `+1 energy/minute` and a Spa of `1 point/30 s` capped at `60` per day. Sitting to rest (GAP-07) gave `1 point/15 s` with no conditions and no cap, that is, unlimited energy for whoever stayed seated.

The gameplay goal is:

* A session lasts between **30 min and 1 h 30 min**. If the player wants to keep playing, they sit down to rest.
* The **sweet spot** is connecting **30–45 min several times a day**.
* The **intensive** player gets a silver bridge: they can keep playing, but at a more relaxed pace.

Also, the farmhands' Satiety only existed in the client's local copy: the host did not know about it and trampled it when syncing, so it was reset on reconnect.

## Decision

1. **Satiety with capacity and continuous drain:** food gives instantaneous energy until filling a capacity of `DailySatietyFraction × MaxStamina` (`135` on a bar of 270). Satiety drains linearly over `SatietyDrainHours = 4` real hours, connected or not, and it is not reset on the day change. The 4 h match the offline recovery (`OfflineRegenFullBarHours = 4`): whoever comes back after 4 h arrives rested and hungry.
2. **"Well-Fed" enables seated rest:** the "Well-Fed" effect is granted when having at least **1/4 of the Satiety bar** (`WellFedSatietyThresholdFraction = 0.25`, that is, ~34 points on a bar of 135) through any food (fish, crops, forage or cooked dishes). While Satiety stays at or above 1/4 (by filling it or while it drains progressively over 4 hours), sitting on a bench, a chair or in the tavern, or on the ground with the `GroundSitKey` key (`GroundSitModule`), recovers `1 point` of energy every `SittingRegenSeconds = 5` s (`12/min`). Without "Well-Fed", sitting recovers nothing. Cooked dishes additionally grant a guaranteed window of `WellFedDurationMinutes = 60` min. "Well-Fed" does not give passive regeneration of its own, it enables active seated rest instead.
3. **Spa buff:** staying still for `SpaStillSeconds = 30` s inside the Spa water or the active pool, **once a day**, grants a passive recovery buff of `SpaBuffTotalPoints = 120` energy points **and** `120` health spread over `SpaBuffDurationMinutes = 120` min (`1 point/min` of each), whether you stay in the water or not. The blocking of the game's native pool recovery is kept.
4. **Host authority:** the client sends the host a `SatietyReport` message when eating or activating the Spa (satiety, daily counters and remaining seconds of each buff). The host clamps it, seals it with its own clock and stores it in `ServerWorldState`. While the player has changed their consumption in the session, their local copy takes priority over the host's residence sync.

## Consequences

* **Positive:** short, repeated sessions are the most efficient way to play. The intensive player can keep going, but has to stop to eat and sit, and after two dishes and the Spa only satiety is left (about 34 points per hour). Sitting stops being an unlimited energy source. Satiety, "Well-Fed" and the Spa are kept on disconnect.
* **Negative:** a session that chains food, "Well-Fed" and Spa receives more energy than with the daily budget of ADR-0004. Without "Well-Fed", sitting no longer recovers anything, and players have to be told. The Spa buff advances in real time even if the player disconnects.
