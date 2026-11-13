# ADR-0005: Central Plaza on the Host Farm and Declarative Bridge Maps

* **Status:** Accepted
* **Date:** 2026-11-03

## Context

The 16 residences are spread between the Host Farm (`Farm`, 80×65) and three chained bridge maps (`Pelican4Ever_Bridge_1..3`, 35×30), as proposed by the `refactor/residence-entrances` branch:

* **Host Farm:** five of its own exits, which are four portals (north-west, north-east, south-west and south-east) and one passage to the west (`x=0`, `y=26..27`) towards bridge 1.
* **Each bridge:** four portals (two to the north and two to the south). Its west edge leads to the next bridge and its east edge to the previous one or to the Host Farm.

The initial prototype implementations created the bridges hot with `Game1.locations.Add` and relied on unsynchronized dynamic copies. This had serious limitations:

1. The bridges were not persisted in `SaveGame.loadDataToLocations` and each client generated its own disconnected copy.
2. By relying on in-memory copies of `Maps/BusStop`, any map reload reinserted vanilla warps towards bus stops.
3. The geometry relied on empirical thresholds desynchronized between subsystems.
4. Several routines only recognized the name `"Farm"`.

## Decision

1. **Declarative bridges.** Each bridge `Pelican4Ever_Bridge_N` has its formal entry in `Data/Locations` (`CreateOnLoad`, type `GameLocation`, outdoor with `Nature.NIV.Outside = "true"`) and its own map `Maps/Pelican4Ever_Bridge_N`. It is generated deterministically in `AssetRequested` (`MapCloneHelper.CreateBridgeMap`). Its number of instances depends on `FarmSlotCount`: $\lceil(\text{FarmSlotCount} - 4) / 4\rceil$, between 0 and 3 bridges (to support 4 to 16 plots).
2. **Clean and deterministic bridge map.** When generating it:
   * The `Warp` property and the vanilla tile actions of the template are removed (bus, ticket machine, sign).
   * The portal openings and the east and west passages are carved.
   * The inter-map transit warps are written declaratively in the map's `Warp` property (`PlazaTopologyVerifier.BuildPassageWarpString`), guaranteeing persistence on any reload.
3. **Explicit geometry in Core.**
   * `HostPlazaLayout`: Describes each plaza location (width, height, north/south path depth and chained passages).
   * `DoorCatalogValidator`: Validates the integrity of the 16-portal catalog in `doors.json`.
   * `SpatialWarpRouter`: Routes arrivals and returns one-to-one according to the cardinal edge (`PortalEdge`).
   * `PlazaTopologyVerifier`: Provides pure utilities for warps formatting/parsing, 3×4 interior clearance areas and deterministic return resolution with a safe fallback.
4. **Plaza generalization.** All portal mechanics, collisions, welcome messages and `/residencias` commands operate on the generalized concept of "plaza location" (`HostPlazaLayout.IsPlazaLocation`).

## Empirical Findings on Real XNB Maps

During implementation and verification with the real Stardew Valley 1.6 files (`Content/Maps/Farm.xnb` and `Content/Maps/BusStop.xnb`) the following exact parameters were determined:

1. **`ViewportClamp` in `BusStop`:** The `Maps/BusStop.xnb` file has a physical size of `65×30`, but includes the native Stardew Valley property `ViewportClamp = "10 0 35 30"`. This proved that the useful and canonical area of the map is exactly the `35×30` rectangle with origin at `(10, 0)`, discarding the vanilla empty columns at both ends.
2. **North path depth (`NorthPathDepth`):**
   * In `Farm`, the northern rocky cliff descends to `y = 7..9`, requiring `NorthPathDepth = 10` to reach the walkable plain.
   * In the bridges (`BusStop`), the upper strip of trees and fences is narrower, requiring `NorthPathDepth = 5` to connect with the central yard.
3. **West passage on the Host Farm:** The west corridor towards Bridge 1 is located at `x = 0..7, y = 26..27`. The static cherry tree is cleared in `AlwaysFront` (`x = 1..6, y = 23..27`) to avoid floating canopies visually blocking the passage when crossing to the bridge.
4. **Reuse of the vanilla road (y = 22..25):** In the bridges (`BusStop`), the natural dirt/asphalt roadway crosses openly and continuously from east to west in the rows `y = 22..25`. These rows are used as natural east/west passages between bridges and towards the Host Farm (`ArrivalY = 24`), preserving the natural trees and avoiding artificial holes at `y = 13..15`. The wooden fences are only placed as a finish on the closed terminal edges (e.g. the west edge of Bridge 3).

## Consequences and Verification

* **Persistence and network:** The bridges and portals serialize cleanly in the save and synchronize identically between host and clients.
* **Resilience on downscaling:** If the administrator reduces `FarmSlotCount` (e.g. from 16 to 4), the Host Farm closes its west passage and any return from an inactive bridge is safely redirected to the Host Farm (`64, 16`) without trapping any player.
* **Automated verification (`TASK-1401` and `TASK-1403`):**
  * Verified through a suite of 793 unit tests without external dependencies in `Pelican4Ever.Tests`.
  * Verified topologically through a **BFS (flood-fill)** traversal over the real Stardew Valley 1.6 `.xnb` assets (`Maps/Farm.xnb` and `Maps/BusStop.xnb`), confirming 100% walkable connectivity in all portals (01..16), return tiles, 3×4 clearance areas and east/west link passages (3857 walkable tiles in Farm, 695 in Bridge 1, 695 in Bridge 2 and 684 in Bridge 3).
