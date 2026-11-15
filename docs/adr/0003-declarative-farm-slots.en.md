# ADR-0003: 4 Declarative Farm Slots (`Data/Locations`) with the Standard Template

* **Status:** Accepted
* **Date:** 2026-10-29

## Context

The original design planned to instantiate locations on demand with `new Farm(mapPath, $"Farm_{player.UniqueMultiplayerID}")` and let each player choose between the game's 8 farm types.

The inspection of `Stardew Valley.dll` revealed three engine restrictions:
1. In `SaveGame.loadDataToLocations`, the engine rebuilds the locations from `Data/Locations` (`CreateOnLoad`) before dumping the XML data. Locations created hot without an entry in `Data/Locations` risk being discarded on reload.
2. The mechanics specific to each farm type (forest forage, hill minerals, wilderness monsters) query the global `Game1.whichFarm` field, not the `Farm` instance.
3. `GameLocation.CanBuildHere()` (line 2459) requires `Game1.multiplayer.isAlwaysActiveLocation(this)` to be `true`, which syncs each farm to all connected clients.

## Decision

1. Limit the MVP to **4 players (`FarmSlotCount = 4`)**, all with the **Standard farm template (`Maps\Farm`)**.
2. Register declaratively in `Data/Locations` (via `Helper.Events.Content.AssetRequested`) the 4 fixed locations `Pelican4Ever_Farm_01` to `Pelican4Ever_Farm_04` with `CreateOnLoad` and `AlwaysActive = true`.
3. When claiming a residence portal in the plaza (initially projected in the town and later consolidated in the Central Plaza and the bridges via ADR-0005), assign the player one of the fixed slots and relocate their `Cabin` inside that slot.

## Consequences

* **Positive:** Saving and reloading 100% compatible with `SaveGame.loadDataToLocations`; Robin allows building without extra patches; for 4 players the `AlwaysActive = true` traffic is negligible.
* **Negative:** In the MVP all players share the same farm map design (Standard) and the default limit is 4 owners.
