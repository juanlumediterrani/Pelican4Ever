## 1. Project Overview

* **Name:** *Pelican 4Ever* (`SV-RTMP`)
* **Core concept:** Turn Stardew Valley from a cooperative game of synchronous sessions and quick turns into a **persistent real-time world (1:1 scale)** hosted on a dedicated 24/7 server.
* **Scale and trust model:** Designed for communities of **4 to 16 players** (scalable through the declarative setting `FarmSlotCount = 4..16`). It runs under a **friends-server model with a mandatory modified client** (`RequireClientMod = true`): both the server and the clients install `Pelican 4Ever`.
* **Experience pillar:** A virtual "second home". The player joins for between 30 and 90 minutes a day to tend their private farm (connected to its portal in the Central Plaza or in the bridge chain), manage their passive industry, spend their daily energy + satiety budget in the mines or fishing, and meet in the plaza, in the town or in evening/night festivals with the rest of the community.

---

## 2. Time System and Calendar

### 2.1 Real-Time Clock (1:1) and "Night Hold" (`NightHold`)

The Stardew Valley engine encodes the clock in integer format (`0600` to `2600`) and forces the pass-out as soon as it reaches `2600` (`Game1.Update` / `performTenMinuteClockUpdate`). To avoid forced pass-outs without rewriting the engine's internal constants:

* **Start of the day (real `06:00` = game `0600`):** The game day begins at **06:00 AM** in the server's time zone (e.g. `Europe/Madrid`).
* **1:1 advance (real `06:00` to `01:50` → game `0600` to `2550`):** Every 10 real minutes the clock advances 10 game minutes (`600` → `2400` at midnight → `2550` at `01:50 AM`).
* **Night hold stretch (real `01:50` to `06:00` → frozen at `2550`):** From real `01:50 AM` onwards, the clock enters the **`NightHold`** state (frozen at `2550`, it never reaches `2600`). Night-owl players can keep fishing, mining or decorating without suffering a pass-out from the clock; when the `02:00 AM` rollover arrives, the engine processes the remaining night hold minutes (`2550 → 0600`) on furnaces and machines.
* **Continuous ambient lighting:** Smoothly interpolated on every 10 real minute stretch (`MillisecondsIntoSlot` over `600.000 ms`).

### 2.2 Perpetual Calendar Based on an Epoch (`CalendarEpoch`)

* **Season length:** The native month of **28 real days** is kept (exactly 4 weeks; 112 real days per game year).
* **Perpetual sync by epoch:** The game date (`Year`, `Season`, `Day`) is **computed mathematically** from a configured real Monday (`CalendarEpoch`, e.g. `2026-11-02` = Spring 1, Year 1).
  * Day 1 of each season is always a real **Monday** and Day 7 is always a real **Sunday**.
  * If the server crashes during the night, on startup it compares `Game1.Date.TotalDays` with the date derived from the real calendar and applies the recovery plan (`CatchUpPlan`: runs up to `MaxCatchUpRollovers` in a row or jumps to the current date).

### 2.3 The Native Daily "Rollover" (`02:00 AM`)

Every day at **02:00 AM (server time)** the daily transition runs, relying on the **native engine flow (`Game1.NewDay` + `SaveGame.Save`)**:

1. **Advance warning:** A warning is shown in the HUD to the connected players **10 minutes** before the cut (`01:50`, `RolloverWarningMinutes`).
2. **Safe native transition:** If there are players connected at `02:00 AM`, they see the native end-of-day and save screen (10–25 seconds). Forcing the native flow guarantees that all the Stardew Valley 1.6 routines run seamlessly (crop growth, personal `Shipping Bins`, mail, weather, luck, animals, friendship, `netWorldState` and XML serialization).
3. **Rest settlement and backup:** Before closing the peers a **SHA-256 signed backup** is generated; when the day change completes, the sleep policy of every registered player is evaluated. If the backup or the orderly disconnection is not verified, the day **does not** advance.
4. **Dawn at `06:00 AM`:** The game clock and the NPC schedules start at real `06:00 AM`; the night hold date change does not bring the daytime routines forward.

---

## 3. Asynchronous Sleep Mechanic ("Bed-Logout")

The authoritative state of each player lives only on the server (`ServerWorldState.Players[UID]`). The `SleepState` states are **derived** from `LastActiveGameDay`, `RestedOnGameDay` and `LastOutcome`:

| Derived State | Condition | Effect when the day closes at the rollover (`02:00 AM`) |
| --- | --- | --- |
| **`Awake`** | Connected today (`isOnline` or `LastActiveGameDay == today`) and has not used the bed yet (`RestedOnGameDay != today`). | If the `02:00 AM` rollover arrives without having rested (or it is still connected outside the bed), it **takes a one-off penalty** (`RolloverOutcome.Penalized`) and moves to `OfflinePenalized`. |
| **`Rested`** | The player gets into their bed, selects *"Descansar y Desconectar"* (`RestedOnGameDay == today`) and exits to the menu. | No penalty (`RolloverOutcome.Rested`). It wakes up with **100% of its Energy** and moves to `OfflineSafe`. |
| **`OfflineSafe`** | It has not joined on the current day and its last outcome was `Rested` or `Untouched`. | It stays untouched (`RolloverOutcome.Untouched`) even if days or weeks pass without connecting. |
| **`OfflinePenalized`** | It has not joined on the current day and its last outcome was `Penalized`. | The penalty (10% of personal gold up to `1.000g` and **50% of Energy**) is applied **only once**. On later days without connecting it returns `Untouched` and is not penalized again. |

* **Friendship frozen while away:** While a player is in `OfflineSafe` or `OfflinePenalized`, the daily friendship decay for not talking to the NPCs is blocked.

---

## 4. Economy and Balance (Passive vs. Active)

1. **Energy per session + Satiety (`SatietyBudget`, ADR-0006):**
   * **Base bar:** The player starts the day with their energy bar (`270` base + *Stardrops*).
   * **Session rhythm:** a session lasts between 30 min and 1 h 30 min. The sweet spot is connecting for 30–45 min several times a day: after 4 h disconnected, the bar is full (`OfflineRegenFullBarHours = 4`) and Satiety is empty. Whoever wants to keep playing sits down to rest; the intensive player can continue, but at a more relaxed pace.
   * **Satiety (food that makes sense):** Eating food **does restore instantaneous Energy**, plus Health and *Buffs*, until filling a **Satiety capacity** equivalent to **50% of their maximum energy** (`DailySatietyFraction = 0.5`, that is, `135` on a bar of 270). Satiety **drains continuously over 4 real hours** (`SatietyDrainHours = 4`), connected or not, and it is not reset on the day change nor on reconnect. With full Satiety you can keep eating to heal Health or maintain *Buffs*, but without gaining instantaneous Energy.
   * **"Well-Fed" (any food):** Up to **2 times a day** (`MaxDailyWellFedBuffs = 2`), eating any food with satiety above the threshold (`WellFedSatietyThresholdFraction = 1/4`) activates "Well-Fed" for 60 minutes. While it lasts, **sitting** on a bench, a chair or in the tavern, or **on the ground** (`GroundSitKey` key, `.` by default), recovers `1 point of Energy every 5 seconds` (`SittingRegenSeconds = 5`, `12/min`). Without "Well-Fed", sitting recovers nothing.
   * **Spa (once a day):** Staying **30 seconds still** inside the Spa water or the active pool triggers a **passive recovery buff of `120` Energy and `120` Health over 2 hours** (`1 point/min` of each), even if you leave the water afterwards. It can only be activated once a day.

2. **Mine Persistence by Shifts (12 Hours):**
   * The automatic unloading of empty floors (`MineShaft.clearInactiveMines`) is blocked.
   * The floors visited in the Mines, the Skull Cavern and the Volcano stay in memory and **only regenerate twice per real day (`06:00` and `18:00`)**, except for the floors where a player is inside at that exact moment (which are cleared as soon as they leave).

3. **Native 1:1 Industrial Processing:**
   * Because the clock runs 1:1 with real time, Stardew Valley's native `MinutesUntilReady` values already scale naturally without touching each machine: an oven takes **30 real minutes**, a jar **~2.8 real days** and a keg **~6.9 real days**.

4. **Configurable Farming and Crafting Multipliers:**
   * `ServerConfig` includes support for them (`CropPriceMultiplier`, `ArtisanPriceMultiplier`, `1.0x` by default until calibrated with real telemetry from the 4-player group), applicable per category (`Category`).

---

## 5. Central Plaza, Bridges and Private Residences (`Farm Slots`)

* **Flexible scale from 4 to 16 declarative residences:**
  * Instead of instantiating arbitrary maps per UID at runtime, the mod registers up to **16 fixed locations** (`Pelican4Ever_Farm_01` to `Pelican4Ever_Farm_16`) in `Data/Locations` with `CreateOnLoad` using the matching farm templates (`FarmTemplateCatalog`).
* **Central Plaza (`Farm`) and Bridge Chain (`Pelican4Ever_Bridge_1..3`):**
  * The residences are not placed as doors in Pelican Town, but in the **Central Plaza** (the Host Farm map `Farm`) and in a **deterministic chain of bridges** (`Pelican4Ever_Bridge_1`, `Pelican4Ever_Bridge_2`, `Pelican4Ever_Bridge_3`) that scales dynamically according to `FarmSlotCount`:
    - With `FarmSlotCount = 4`: 4 plots in the Host Farm (no bridges and no west passage).
    - With `FarmSlotCount = 8`: 4 plots in the Host Farm + 4 on Bridge 1.
    - With `FarmSlotCount = 12`: 4 plots in the Host Farm + 4 per bridge on Bridges 1 and 2.
    - With `FarmSlotCount = 16`: 4 plots in the Host Farm + 4 per bridge on Bridges 1, 2 and 3.
  * Each bridge is a deterministic crop of `35×30` tiles derived from `Maps/BusStop`, with a clean central roadway, a sealed perimeter and continuous east and west passages.
* **Portals with an explicit edge (`PortalEdge`) and walk-in entrance:**
  * Each residence portal faces a cardinal edge (`North` or `South` in `doors.json`), has a 2-tile wide cleared path to the roadway, `stepping_stones` paving and a 3×4 tile interior clearance area.
  * Walking to the portal up to the last two tiles of the edge triggers the founding/claiming menu for a free plot, or the direct entrance to the private residence if it is occupied and allowed.
  * Entrance and exit respect the immersive spatial orientation (`SpatialWarpRouter`): north portals enter through the south of the residence facing north, and south portals enter through the north of the residence facing south.
* **Individual mailboxes:**
  * Each portal has its own adjacent mailbox (`TownMailboxModule` / `TownMailboxLogic`), allowing private mail from the NPCs or from the system to be received right after leaving the residence.
* **Visits and permissions validated by the Host:**
  * The owner configures their residence in **`Locked`**, **`Friends`** or **`Public`** mode.
  * On other farms, the mod blocks destructive tools (pickaxes, axes, hoes on crops, bombs) and chest opening, allowing watering in `Friends`/`Public` mode.

---

## 6. Shared vs. Individual Progress

| Scope | Rule in *Pelican 4Ever* |
| --- | --- |
| **Economy and Shipping Bins** | **Individual** (`team.useSeparateWallets = true`). Each player has their own gold and their own `personalShippingBin`. |
| **Farm, House and Upgrades** | **Individual** per slot (`Pelican4Ever_Farm_01..04`). |
| **Town Hall / Museum / Joja** | **Shared (community)** for the whole server. |
| **Mine Elevator** | **Individual** or shared depending on the group configuration (generated floors are shared during each 12h shift). |
| **NPC relationships** | **Individual** per player, with no decay while the player is in the `Offline_*` state. |

---

## 7. Town Life (NPCs) and Festivals (Post-MVP / Iteration 4)

* **Autonomous NPC behaviour (*Micro-Wandering*):**
  * Villagers halve their route speed and, on reaching their scheduled destination, wander within a safe radius of $\pm 2$ tiles every 45–90 real seconds (avoiding collisions and `Warps`).
* **Festivals in the evening/night window (`18:00 – 23:50`) and reschedulable:**
  * Admin command (`p4e_festival_move <season> <oldDay> <newDay>`) to move festivals to the real weekend through `Data/Festivals/FestivalDates`.
  * Adaptation of the festival time window to the evening/night hours (`1800` to `2350`) without hijacking the traffic through `Town`.

---

## 8. Known Risks and Scope Limits

* **Maximum loss on a host crash:** Since Stardew Valley only serializes the full save on `NewDay`, an abrupt crash before `02:00 AM` can lose the progress of the current day. It is mitigated with a supervisor (`systemd` with `Restart=always` or a container with `restart: unless-stopped`), automatic SHA-256 backups after each rollover, intraday checkpoints (`CheckpointIntervalMinutes`) and the controlled restart (`POST /api/v1/host/restart`), which waits for the next save before closing the process.
* **Out of scope for the MVP:**
  * Cryptographic anti-cheat against clients that modify the local DLL (a private server with a version check in `ClientVersionGuardModule` is assumed).
  * Specific `Game1.whichFarm` rules per player (in the MVP the 4 farms use the Standard template).
