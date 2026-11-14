# ADR-0008: Skill and Economy Rebalance for the 1:1 Clock

* **Status:** Proposed (the values are starting points not yet calibrated in game; it becomes *Accepted* when TASK-2107 is closed)
* **Date:** 2026-11-14
* **Context:** Epic 21. Complements [ADR-0002](0002-realtime-clock-and-epoch-calendar.en.md) (1:1 clock) and [ADR-0006](0006-session-energy-satiety-wellfed-spa.en.md) (energy per session).

## Context

A Stardew Valley day lasts 14 real minutes (120 slots of 7 s). In *Pelican 4Ever* the day lasts 24 hours, a session lasts between 30 and 90 minutes and nobody passes out at 02:00 (`NightHold`). Per calendar day, the player has several times the action time of vanilla.

That surplus does not inflate every activity equally. It depends on what limited each one in vanilla:

| Activity | Effective brake under 1:1 | Result |
| --- | --- | --- |
| Crops | Calendar: growth days are real days | Same as vanilla per day; the slowest in real time |
| Animals | Calendar: 5 Farming XP per petting or product, per animal and day | Same as crops |
| Forage gathering | Calendar (daily respawn) and sharing among 4–16 players | Already diluted |
| *Nature in the Valley* creatures | No known brake: each catch gives `3 + 4^rarity` or `11 + 4^rarity` Foraging XP | Inflated |
| Fishing | Energy: `8 − 0.1 × level` per cast | One bar plus Satiety is about 50 casts; several sessions a day multiply it |
| Mining | Energy: `2 × (power + 1) − 0.1 × level` per swing; the descent no longer has a clock | Inflated in depth |
| Combat | None: the sword costs no energy and food heals even with full Satiety | The most inflated |
| Skull Cavern | None: its only limit in vanilla was the clock | Unbounded iridium |

Formulas checked against the decompiled 1.6.15 code (not the wiki):

* **Bite** (`FishingRod.calculateTimeUntilFishingBite`): random between `600` and `30000 − 250 × level` ms, minus `5000`/`10000` per tackle; `×0.75` on the first cast and `×0.5` with bait; minimum `500` ms.
* **Minigame** (`BobberBar.update`): the catch bar starts at `0.3`; it rises `0.002` per tick with the fish inside and drops `0.003` per tick outside (`0.002` with the training rod).
* **Fishing XP** (`FishingRod.pullFishFromWater`): `max(1, (quality + 1) × 3 + difficulty / 3)`, `×2.4` on a perfect catch, `×2.2` with treasure and `×5` for legendary fish.
* **Ladder when breaking a stone** (`MineShaft.checkStoneForItems`): `0.02 + 1 / stonesLeft + luck / 100 + dailyLuck / 5`, plus `0.04` with no enemies; guaranteed when the last stone is broken.
* **Ladder when killing a monster** (`MineShaft.monsterDrop`): `0.15`, plus `0.07` with the dwarf statue.
* **Ore** (`MineShaft.createLitterObject`): `0.029` per generated stone.
* **XP** (`Farmer.gainExperience`): all skill experience goes through this method and is applied on the client that owns the character (the host forwards the farmhands' XP). Levels are reached at `100, 380, 770, 1300, 2150, 3300, 4800, 6900, 10000, 15000` XP.

## Decision

1. **Balance principle.** The calendar rules. What the calendar does not brake, energy brakes. What neither brakes gets a brake of its own. Activities braked by the calendar are left untouched.

2. **Daily experience curve.** A single patch on `Farmer.gainExperience` applies, per skill and game day:
   * The first `SkillDailyFullXp` points of the day count at 100%. Initial value: `300` for Fishing, Foraging, Mining and Combat.
   * Anything beyond that allowance counts at `SkillOverflowXpMultiplier = 0.25`.
   * Farming is **exempt** (it has no allowance). This also covers animals, which give Farming XP.
   * `SkillXpMultiplier` (per skill, `1.0` by default) allows an additional flat cut, applied before the allowance.
   * Fractions accumulate and are not lost: a 1 XP event at 25% counts 1 point every four events.
   * The daily counter per skill is authoritative on the host (`ServerWorldState`), is saved with the game and resets per game day, following the same pattern as Satiety (`SatietyReport`).
   * Mastery experience (total level ≥ 25) receives the same cut, because it is computed in the same method.

3. **Fishing.**
   * `FishingBiteTimeMultiplier = 2.5`: multiplies the bite time once computed, so baits and tackle keep their relative advantage. Fish ponds are exempt.
   * `FishingMinigameDurationMultiplier = 1.5`: divides by `1.5` what the catch bar rises and drops per tick. The minigame lasts longer without changing the share of time the fish must be kept inside.

4. **Mine ladders.**
   * `MineStoneLadderChanceMultiplier = 0.5`: multiplies the ladder chance when breaking a stone. The guaranteed ladder on the last stone is kept.
   * `MineMonsterLadderChance = 0.08` (vanilla `0.15`). The `+0.07` from the dwarf statue is kept.
   * Floors that require killing every monster do not change.
   * Ore density is **not** touched.

5. **Skull Cavern: descending costs energy.** On reaching a floor deeper than the previous one, the player pays `SkullCavernDescentBaseCost + floor / SkullCavernDescentDepthDivisor` energy, with `floor` counted from the Cavern entrance (`mineLevel − 120`). Initial values: `1` and `25`.
   * A player who falls down a shaft or uses a crafted staircase pays for every floor skipped.
   * Going down to floor 100 costs about `300` points.
   * The descent is never blocked: if energy is insufficient it stays at `0` and the player becomes exhausted.
   * The regular Mines and the Volcano do not have this cost.

6. **Prices.** The per-category multipliers of `Object.sellToStorePrice` are extended with `FishPriceMultiplier` (category `-4`) and `MineralPriceMultiplier` (categories `-2`, `-12` and `-15`), both `1.0`. No price changes in this decision: before moving one, the host log records, per player and day, the gold earned and the XP gained per skill.

## Discarded alternatives

* **A flat XP multiplier as the only measure:** it cuts the 30-minute player as much as the 6-hour player. It stays available (`SkillXpMultiplier`), but it is not the main mechanism.
* **Reducing animal XP:** it is already braked by the calendar, like crops.
* **Reducing ore density:** floors are shared and only regenerate at `06:00` and `18:00` or after 45 empty minutes, so ore is already split among players.
* **A hard cap on new floors per day:** it cuts the session short instead of making it more expensive.
* **An energy cost per sword swing:** it changes the feel of combat across the whole game to fix a problem concentrated in the Skull Cavern.

## Consequences

* **Positive:**
  * A player who joins 30 minutes a day progresses at vanilla pace per calendar day; playing more hours still pays, but at a quarter of the rate.
  * A fishing energy bar goes from about 25 to about 45–50 minutes (estimate), so fishing fills a session and leaves room for social life.
  * The Skull Cavern has a per-dive limit again, paid in the mod's own currency.
  * Everything is configurable and each neutral value (`0` for the allowance, `1.0` for the multipliers) restores vanilla behaviour.
* **Negative:**
  * Combat loot stays unbounded outside the Skull Cavern; only its experience is limited.
  * The daily curve is not visible in the vanilla interface: it has to be communicated to players.
  * Floors are shared: a ladder opened by one player serves the next ones, which dampens the effect of point 4 on busy servers.
  * The daily counter adds per-player state that must survive reconnections, checkpoints and rollovers.
* **Not calibrated:** the values `300`, `0.25`, `2.5`, `1.5`, `0.5`, `0.08`, `1` and `25` are estimates. Fishing times and the Cavern cost are computed from the formulas, not measured in game.
