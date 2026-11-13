# `ARCHITECTURE.md` — Technical Architecture Document (*Pelican 4Ever*)

## 1. Tech Stack and Topology

* **Target platform:** Stardew Valley **1.6.15** (64-bit) on Linux (`x86_64`), run with **SMAPI 4.1+** (tested on SMAPI 4.5.2) and **Harmony 2.x** (`Lib.Harmony`).
* **Dedicated Server (Native 1.6 + Container):**
  * Stardew Valley 1.6.15 includes native automated host support (`Game1.HasDedicatedHost`, `Game1.IsDedicatedHost`, `Farmer.IsDedicatedPlayer` and the `StardewValley.Network.Dedicated.DedicatedServer` class enabled through `player.team.hasDedicatedHost.Value = true`).
  * The deployment relies on **JunimoServer** (or a Linux Docker container with `xvfb` + `noVNC`), combining the native `DedicatedServer` bot with the continuous-clock rules of `Pelican 4Ever` (preventing `DedicatedServer.Tick()` from pausing the world when `onlineIds.Count == 0`).
* **Client-Server Topology and Trust:**
  * The **same mod package (`Pelican4Ever.dll` + `Pelican4Ever.Core.dll`) is required on the Host and the Clients** (`ClientVersionGuardModule` kicks out in `PeerContextReceived` any client without SMAPI or with a different `Major.Minor` version).
  * **Host (`Context.IsMainPlayer`) = Single Source of Truth** for the clock, the calendar, the portal/residence assignment and the player registry (`ServerWorldState`).
  * **Client (`Context.IsLocalPlayer`) = UI Runner and Local Player** for the energy cut when eating (`Farmer.doneEating`), local blocking of tools on other farms, portal and bed dialogues. All shared-state mutations are requested to the Host through `Helper.Multiplayer.SendMessage`.

---

## 2. Solution Structure (.NET)

The solution is split into three projects to fully separate the pure domain logic from the `Stardew Valley.dll` dependencies and to be able to run unit tests in milliseconds:

```text
Pelican4Ever.sln
├── local.props.example                  # Plantilla de ruta local a Stardew Valley (copiar a local.props)
├── Pelican4Ever.Core/                   # Lógica pura (.NET 6, CERO referencias a Stardew/SMAPI/Harmony)
│   ├── Config/
│   │   └── ServerConfig.cs              # Configuración + validador determinista Validate()
│   ├── Time/
│   │   ├── GameClock.cs                 # Conversión hora real -> timeOfDay (0600..2550) + NightHold
│   │   ├── ServerCalendar.cs            # Calendario perpetuo derivado de CalendarEpoch y CatchUpPlan
│   │   └── ServerTime.cs                # Resolución de TimeZoneInfo IANA y reloj UTC -> Local
│   ├── Players/
│   │   ├── PlayerRecord.cs              # Registro autoritativo por jugador (días en TotalDays)
│   │   └── SleepPolicy.cs               # Derivación de SleepState, evaluación de Rollover y cargo médico
│   ├── Economy/
│   │   └── SatietyBudget.cs             # Presupuesto diario de Saciedad (comida) y tope del Spa
│   └── World/
│       ├── ServerWorldState.cs          # Estado persistido en el Host + migración de esquema (v2)
│       ├── HostPlazaLayout.cs           # Plaza Central y cadena de puentes escalables (TASK-1001)
│       ├── DoorCatalogValidator.cs      # Validación de geometría y portales en la plaza (TASK-1002)
│       ├── SpatialWarpRouter.cs         # Enrutamiento espacial, llegadas y retornos (TASK-1102)
│       ├── TerrainTileClassifier.cs     # Clasificación de terreno y autotiling de caminos (TASK-1103)
│       ├── ResidenceExits.cs            # Puntos de salida por plantilla de granja (TASK-1104)
│       ├── PlazaCatalogFormatter.cs     # Formateo puro de /residencias para chat y consola (TASK-1304)
│       └── PlazaTopologyVerifier.cs     # Verificación topológica, warps y áreas de despeje (TASK-1401/1403)
├── Pelican4Ever/                        # Mod SMAPI (.NET 6, referencia a Pelican4Ever.Core)
│   ├── manifest.json
│   ├── ModEntry.cs                      # Validación de config y registro aislado de módulos (IModule)
│   ├── Framework/
│   │   ├── IModule.cs                   # Contrato IModule { Name, Side, Register(ModContext) }
│   │   ├── ModContext.cs                # Contenedor inyectado (Helper, Monitor, Config, Harmony, WorldState)
│   │   └── MapCloneHelper.cs            # Recorte determinista de puentes (35×30) y anexos
│   ├── Models/
│   │   └── ModDataKeys.cs               # Claves de GameLocation.modData (vista replicada de solo lectura)
│   └── Modules/
│       ├── PersistenceModule.cs         # Carga, migración y guardado de ServerWorldState en el Host
│       ├── ClientVersionGuardModule.cs  # Verificación de versión del mod al conectar un par
│       ├── StatusCommandModule.cs       # Comando de diagnóstico p4e_status
│       ├── RealTimeClockModule.cs       # (Iteración 1) Sincronización 1:1, NightHold y bloqueo de pausa
│       ├── DailyRolloverModule.cs       # (Iteración 1) Avisos, disparo de NewDay nativo y SleepPolicy
│       ├── BedLogoutModule.cs           # (Iteración 1) Intercepción de Sleep_Yes y mensaje al Host
│       ├── FarmSlotModule.cs            # (Iteración 2) Registro en Data/Locations, Cabins, plaza y portales
│       ├── EconomyModule.cs             # (Iteración 3) SatietyBudget, Spa, precios y minas 12h
│       ├── GroundSitModule.cs           # Sentarse en el suelo (tecla GroundSitKey), pose sincronizada entre jugadores
│       └── WorldLifeModule.cs           # (Iteración 4) Micro-wandering de NPCs y festivales
└── Pelican4Ever.Tests/                  # Suite xUnit (.NET 8) sobre Pelican4Ever.Core (1.300+ tests)
```

---

## 3. Data Model and Single Source of Truth

### 3.1. Authoritative State on the Host (`ServerWorldState`)

To avoid race conditions on `Farmer.modData` (whose `NetRoot` belongs to each `Farmhand`'s client), all the persistent player information lives in `ServerWorldState` (serialized in the master save through `Helper.Data.WriteSaveData("Pelican4Ever.ServerWorldState", state)`):

* **`Players` (`Dictionary<long, PlayerRecord>`):**
  * `LastActiveGameDay`, `RestedOnGameDay` (expressed in `Game1.Date.TotalDays`, never in `dayOfMonth`).
  * `LastOutcome` (`Untouched` | `Rested` | `Penalized`), from which `SleepPolicy.GetState` derives `Awake`, `Rested`, `OfflineSafe` or `OfflinePenalized`.
  * Satiety with continuous drain (`Satiety`, `LastSatietyUpdateUtc`), end of the running buffs (`WellFedUntilUtc`, `SpaBuffUntilUtc`) and daily counters with auto-reset per game day (`SatietyGameDay`, `WellFedBuffsUsed`, `SpaBuffsUsed`). The client reports them to the host with the `SatietyReport` message (ADR-0006).
* **`Doors` (`Dictionary<string, DoorAssignment>`):** Links each town `DoorId` with its `FarmSlotId` (`Pelican4Ever_Farm_01..04`), its `OwnerId`, its `AccessPolicy` (`Locked`, `Friends`, `Public`) and its `Friends` set.

### 3.2. Replicated View in Locations (`GameLocation.modData`)

Only the locations (controlled by the Host) expose metadata in `location.modData` as a read-only view for the UI and the local tool blocking on the clients:
* `Pelican4Ever.OwnerId` (`long`)
* `Pelican4Ever.AccessPolicy` (`Locked` | `Friends` | `Public`)

---

## 4. Technical Specification of the Subsystems (Verified against Stardew Valley 1.6.15)

### 4.1. Subsystem 1: 1:1 Clock, `NightHold` and Native Rollover

#### A. How the clock works in Stardew Valley 1.6.15 (Decompiled)
1. In `Game1.UpdateGameClock(GameTime time)` (lines 6050–6090), the Host accumulates `gameTimeInterval += time.ElapsedGameTime.Milliseconds` when `shouldTimePass() && !IsClient`.
2. When `gameTimeInterval > realMilliSecondsPerGameTenMinutes + ExtraMillisecondsPerInGameMinute * 10` (`7000 ms` by default), it invokes `Game1.performTenMinuteClockUpdate()`.
3. In `performTenMinuteClockUpdate()` (lines 5882–6024), the Host adds `timeOfDay += 10`, normalizes the minutes (`% 100 >= 60`), applies `timeOfDay = Math.Min(timeOfDay, 2600)`, runs `location.performTenMinuteUpdate(timeOfDay)`, `MineShaft.UpdateMines10Minutes(timeOfDay)` and propagates the state to the clients through `netWorldState.Value.UpdateFromGame1()`.
4. When a client receives `NetWorldState` in `Multiplayer.receiveWorldState` (line 506) and detects `timeOfDay != Game1.timeOfDay`, it runs its own `Game1.performTenMinuteClockUpdate()` locally.
5. In `Game1.Update` (line 6452): `if (timeOfDay >= 2600 || player.stamina <= -15f)` triggers `player.startToPassOut()`.

#### B. Design of the Time Patch (`RealTimeClockModule`)
* **No pass-out from the schedule thanks to `NightHold`:**
  * `GameClock.Read` maps `06:00 → 0600` up to `01:50 → 2550`. Between real `01:50` and `06:00` it returns `TimeOfDay = 2550, IsNightHold = true`.
  * Since `Game1.timeOfDay` **never reaches `2600`**, the `timeOfDay >= 2600` condition of `Game1.Update` (line 6452) is never triggered by the clock, while the pass-out from exhaustion (`player.stamina <= -15f`) and from health (`health <= 0`) keeps working 100% natively without touching `Farmer.passOutFromTired`.
* **1:1 sync driven by the Host:**
  * `Game1.realMilliSecondsPerGameTenMinutes = 600_000` (10 real minutes) is set so that the ambient light interpolation of `UpdateGameClock` (lines 6059 and 6065) uses the real divisor and `gameTimeInterval = clockReading.MillisecondsIntoSlot`.
  * In `UpdateTicked` (Host), the free accumulator is locked and `GameClock.TicksToReach(Game1.timeOfDay, clockReading.TimeOfDay)` is computed: when the real clock crosses a 10 minute multiple (and we are not in `IsNightHold`), the Host invokes `Game1.performTenMinuteClockUpdate()` exactly the number of steps needed. The clients receive the change through `NetWorldState` and run their tick automatically.
* **Continuous time with no pauses:**
  * `Game1.shouldTimePass` is patched (and in dedicated server mode `DedicatedServer.Tick` is prevented from setting `Game1.netWorldState.Value.IsPaused = true` when there are no connected players, and from having individual menus/events pause `IsTimePaused`).

#### C. Native Daily Rollover at `02:00 AM` (`DailyRolloverModule`)
* At real `02:00:00` (`CalendarDayChangeTime`), the Host syncs the target date with `ServerCalendar` and forces the native daily transition (`Game1.NewDay(0f)` / automatic sleep `ReadyCheck`), letting `_newDayAfterFade()` run the whole day-change pipeline and `SaveGame.Save()`.
* At real `06:00` (`DayStartTime`) the game clock and the NPC schedules start: the night hold date change does not bring the daytime routines forward.
* Before closing, the players are warned (`RolloverWarningMinutes`, 10 by default), a SHA-256 signed backup is generated and the peers are closed in an orderly way; if the backup or the disconnection is not verified, the day **does not** advance.
* On `DayStarted`:
  * It evaluates `SleepPolicy.Evaluate` over each `PlayerRecord` of the day that has just closed.
  * It applies `SleepPolicy.WakeUpEnergy` and `SleepPolicy.MedicalFee` to the matching `Farmer` (using separate wallets `useSeparateWallets`).
  * It verifies with `ServerCalendar.PlanCatchUp` whether the save date matches the real date derived from `CalendarEpoch`.

---

### 4.2. Subsystem 2: Central Plaza, Declarative Bridges and Private Residences (`FarmSlotModule`)

1. **Declarative registration in `Data/Locations` (no orphan locations on load):**
   * In `SaveGame.loadDataToLocations`, Stardew Valley 1.6 creates the locations from `Data/Locations` (`CreateOnLoad`).
   * Through `Helper.Events.Content.AssetRequested` on `"Data/Locations"`, the mod injects declaratively:
     - The private residences `Pelican4Ever_Farm_01` .. `Pelican4Ever_Farm_16` (according to `FarmSlotCount`), with `CreateOnLoad = new CreateLocationData { MapPath = template.MapAssetKey, Type = "StardewValley.Farm, Stardew Valley", AlwaysActive = true }`.
     - The active bridge maps `Pelican4Ever_Bridge_1..3` computed deterministically according to `FarmSlotCount` (`ceil((FarmSlotCount - 4) / 4)`): type `GameLocation`, outdoors (`Nature.NIV.Outside = "true"`), never falling back to `Game1.locations.Add` or to volatile in-memory bridges.
   * Engine note (verified in `GameLocation.cs` line 2459): `CanBuildHere()` requires `Game1.multiplayer.isAlwaysActiveLocation(this)` to be `true`. For the server's farmhands `AlwaysActive = true` is kept, which guarantees full compatibility with Robin, buildings and animals at a negligible network cost.

2. **Deterministic generation of the bridge maps (`MapCloneHelper.CreateBridgeMap`):**
   * Each bridge map `Pelican4Ever_Bridge_N` (35×30) is generated by cropping the central visible area `(10, 0, 35, 30)` of `Maps/BusStop`.
   * The generator clears the broken bus and the central bushes, seals the perimeter against the void except for the portal openings and active passages, places fences on the old road and carves the East and West passages by writing the continuous declarative warps in `map.Properties["Warp"]`.

3. **Pure domain architecture (`Pelican4Ever.Core/World/`):**
   * **`HostPlazaLayout`:** Defines the topology and dimensions of the plaza (`Farm` of 80×65 and bridges `Pelican4Ever_Bridge_1..3` of 35×30), the bridge count according to `FarmSlotCount` and the chained East/West passages with their bidirectional arrival coordinates.
   * **`DoorCatalogValidator`:** Validates the catalog of 16 residence portals in `doors.json` (explicit `PortalEdge`, plaza membership, margins and adjacent mailbox).
   * **`SpatialWarpRouter`:** Computes deterministically the arrival tiles and orientation at the residence according to the portal's cardinal edge (`North`, `South`, `West`, `East`) and the farm template, the tiles to return to the plaza in front of the opening, and the trigger tiles (`IsPortalTriggerTile`).
   * **`TerrainTileClassifier`:** Classifies the terrain for the carving and procedural autotiling of `stepping_stones` paths, respecting tree canopies and firm ground.
   * **`ResidenceExits`:** Determines the exit points of each residence template towards the outside.
   * **`PlazaCatalogFormatter`:** Formats the residence lists and plot states cleanly for the chat and console `/residencias` commands.
   * **`PlazaTopologyVerifier`:** Provides pure utilities for computing the 3×4 interior clearance areas (`GetInteriorClearanceTiles`), serializing and parsing `Warp` strings (`BuildPassageWarpString`, `ParseWarpProperty`), safely resolving the return to the plaza (`ResolvePlazaReturnTarget`), and verifying topological connectivity with BFS (`IsConnected`).

4. **Linking residences, portals and mailboxes:**
   * Each residence `Pelican4Ever_Farm_0X` hosts the `Cabin` associated with the owning `Farmhand` and publishes `OwnerId` and `AccessPolicy` in `location.modData`.
   * The 16 residence portals are distributed between the Host Farm (4 portals) and the bridges (4 portals per bridge).
   * When walking to a portal at the edge of the map, the mod opens the founding dialogue if the plot is free, or smoothly teleports the player to the private residence if it is occupied and allowed.
   * Each portal has its own adjacent mailbox (`TownMailboxModule`), allowing personal mail to be handled when entering or leaving the residence without interference.

---

### 4.3. Subsystem 3: Energy, Satiety, Prices and 12h Mines (`EconomyModule`)

1. **Satiety in `Farmer.doneEating` (lines 8851–8945, ADR-0006):**
   * In `Farmer.doneEating()`, the engine adds `Stamina = Math.Min(MaxStamina, Stamina + @object.staminaRecoveredOnConsumption())` and `health = Math.Min(maxHealth, health + @object.healthRecoveredOnConsumption())` and applies the `GetFoodOrDrinkBuffs()`.
   * The `Prefix` stores `float prevStamina = __instance.Stamina`.
   * The `Postfix` applies the continuous drain first (`PlayerRecord.UpdateSatietyDecay`), computes `int requestedGain = (int)Math.Round(__instance.Stamina - prevStamina)`, queries `SatietyBudget.AllowedGain(requestedGain, record.Satiety, capacity)` and sets `__instance.Stamina = prevStamina + allowedGain` (respecting special cases such as `Stardrop (O)434`). With **any food** (not only cooked dishes) and if satiety reaches the `WellFedSatietyThresholdFraction` threshold (1/4) and there are slots left in `MaxDailyWellFedBuffs`, it activates "Well-Fed" (`PlayerRecord.TryActivateWellFed`), which allows recovering energy while sitting (`SittingRegenLogic`, `1 point / 5 s`) for 60 min.
   * In `OneSecondUpdateTicked`, 30 s still in the water activate the Spa buff once a day (`PlayerRecord.TryActivateSpaBuff`), which deals `120` energy and `120` health over 120 min (`PassiveRegenLogic`).
2. **Mine persistence in 12h shifts (`06:00` and `18:00`):**
   * In `MineShaft.UpdateMines10Minutes` (line 4788), the engine calls on every 10 minute tick `MineShaft.clearInactiveMines()` (private static, line 4704), which unloads every floor where there are no players. On the day change (`Game1.cs` line 7925) it calls `MineShaft.clearActiveMines()`.
   * A `Prefix` is applied to `MineShaft.clearInactiveMines` that returns `false` during the normal shift, and at real `06:00` and `18:00` the Host invokes the cleanup on the floors without active players (notifying the new mining shift through the HUD).

---

### 4.4. Subsystem 4: Town Life and Festivals (`WorldLifeModule` — Post-MVP)

* **NPC micro-wandering (`NpcMicroWanderController`):** On the Host, when an `NPC` finishes its scheduled `PathFindController`, it stores its anchor tile and every 45–90 real seconds it walks (`speed = 1`) to a free tile within a radius of $\pm 2$ that is neither a `Warp` nor colliding.
* **Evening/night festivals (`FestivalInstanceManager`):**
  * Rescheduling of dates by editing `Data/Festivals/FestivalDates` in `AssetRequested` + `InvalidateCache`.
  * In `Data/Festivals/<season><day>`, the second token of `conditions` is rewritten to `"1800 2350"` to move the access window to real `18:00–23:50`, and the `ReadyCheckDialog("festivalStart")` block of `Game1.warpFarmer` (line 9978) is intercepted to allow normal transit through `Town` for those who choose not to enter the event.

---

## 5. Architecture Decision Records (ADRs)

The key architectural decisions and their discarded alternatives are documented in [`docs/adr/`](docs/adr/):

* **ADR-0001:** Daily rollover through the native `NewDay` + `Save` pipeline at `02:00 AM` (dawn at `06:00 AM`).
* **ADR-0002:** 1:1 clock (`0600–2550`) with a freeze in the night hold (`NightHold`) and a calendar derived from `CalendarEpoch`.
* **ADR-0003:** Declarative farm slots (`Data/Locations`) with the Standard template for the MVP.
* **ADR-0004:** Authority on the Host (`ServerWorldState`), mandatory mod on clients and isolated modular registry (`IModule`).
* **ADR-0005:** Central Plaza on the Host Farm and declarative bridge maps (`Data/Locations`) for scaling up to 16 private residences.
* **ADR-0006:** Energy per session: Satiety with a continuous drain, "Well-Fed" enabling seated rest and the daily Spa buff.
* **ADR-0007:** Asymmetric identity per device (ECDSA P-256) and the `P4E\x02` challenge in the Lidgren handshake.
