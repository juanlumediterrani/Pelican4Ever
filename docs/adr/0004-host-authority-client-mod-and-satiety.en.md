# ADR-0004: Authority on the Host, Mandatory Mod on Clients and Satiety Budget

* **Status:** Accepted. Point 3 (daily satiety budget) is replaced by [ADR-0006](0006-session-energy-satiety-wellfed-spa.en.md).
* **Date:** 2026-10-29

## Context

In the Stardew Valley networking layer, each `Farmhand` owns its own `Farmer` (`NetRoot<Farmer>`) and runs `Farmer.doneEating`, tool use and dialogues on its client. If the Host writes directly to the `farmer.modData` of a connected client, race conditions with the client replication appear.

Also, removing instantaneous energy from food completely left the kitchen useless for active players who want to do more than drain the base bar of 270 energy.

## Decision

1. **Mandatory mod on Host and Clients (`ClientVersionGuardModule`):** The Host verifies in `PeerContextReceived` that the client has `Pelican 4Ever` with the same `Major.Minor` version and kicks out on the next tick whoever does not meet the requirement.
2. **Single source of truth on the Host (`ServerWorldState`):** The state of the players (`PlayerRecord`) and of the doors (`DoorAssignment`) is stored only in `ServerWorldState` on the Host. `GameLocation.modData` is used only as a replicated read-only view for the clients.
3. **Daily satiety budget (`SatietyBudget`):** Food still returns instantaneous energy on top of health and buffs, but limited to a daily budget (`DailySatietyFraction = 0.5`, that is, +50% of the max bar per real day), complemented by up to 2 daily passive regeneration buffs on cooked dishes and a daily cap at the Spa (`SpaDailyCap = 60`).
4. **Isolated modular registry (`IModule`):** All pure logic lives in `Pelican4Ever.Core` (with no game references and tested with xUnit) and each SMAPI/Harmony subsystem implements `IModule` with error isolation on registration.

## Consequences

* **Positive:** No race conditions in `Farmer.modData`; the kitchen keeps its strategic value within a controlled daily cap; all business logic is tested with `dotnet test` without opening the game.
* **Negative:** All players must install the mod on their client to join the server.
