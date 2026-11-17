# Pelican 4Ever — General System Specification (GSS)

🌐 **Languages:** [Español](design.md) | English | [中文](design.zh-CN.md)

> **Product name:** *Pelican 4Ever* (`SV-RTMP`, `UniqueID: Pelican4Ever.SVRTMP`)
> **Document:** General System Specification (GSS). What the product is, who it serves, how its mechanics behave in the persistent world, and which functional and non-functional requirements it fulfills.
> Technical implementation details live in [`architecture.en.md`](architecture.en.md), architectural decisions in [`docs/adr/README.en.md`](docs/adr/README.en.md), development roadmap in [`backlog.md`](backlog.md), agent-executable live tests in [`live-agent-tests-backlog.md`](live-agent-tests-backlog.md), and human playtest tasks in [`test-users-tasks.md`](test-users-tasks.md).
> **Version:** 0.5-draft · **Date:** 2026-11-16

---

## 1. Purpose, Scope, and Normative Glossary

**Pelican 4Ever** transforms **Stardew Valley 1.6.15+** (on **SMAPI 4.1+**) from a synchronous co-op game of 14-minute days into a **persistent real-time world (1:1 scale)** operated 24/7 for communities of **4 to 16 players**.

### 1.1 Core Concept and Scale

* **Experience pillar:** A virtual "second home". Each neighbor logs in for 30 to 90 minutes a day to tend their private residence (reachable from the Central Plaza or the bridge chain), run passive industry, spend their daily energy and satiety budget on fishing, mining, or creature catching, place town orders from their mailbox, and meet others in the plaza, town, or evening/night festivals.
* **Declarative scale:** Configurable from **4 to 16 plots** via `FarmSlotCount` (`4, 8, 12, 16`), supporting up to **2 residents per plot** (Owner + 1 Tenant) and up to **2 thematic annexes** (`40×32`) per residence.
* **Trust and authority model:** Private/community server with a mandatory modified client (`RequireClientMod = true`). The Host is the single source of truth (`ServerWorldState`), and clients authenticate device identity via asymmetric cryptography (ECDSA P-256).

### 1.2 Out of Scope

* Vanilla clients without SMAPI or the required modpack (`RequiredClientMods`).
* Cryptographic anti-cheat against arbitrary modification of a client's local DLL beyond modpack checks, device signatures, and Host authoritative invariants.
* Storing server secrets or Discord/API tokens inside client configuration files.
* Hot in-place save restores without stopping the game process (restores always run offline via `Putre` / `saves.restore`, [ADR-0010](docs/adr/0010-fail-closed-health-and-backup-contract.en.md)).
* Official production support for `FarmSlotCount > 8` until the agent load test benchmark (`LIVE-2503` / `PUTRE-401`) is completed and accepted.

### 1.3 Definitions and Glossary

| Term | Definition |
| :--- | :--- |
| **`NightHold`** | Real-time window between `01:50` and `06:00` where `Game1.timeOfDay` remains frozen at `2550`, preventing forced pass-outs at `2600` while machines continue processing. |
| **`CalendarEpoch`** | Configured real-world Monday used as Spring Day 1, Year 1 to deterministically compute game year, season, and day and align 28-real-day seasons with calendar weeks. |
| **Native Rollover** | Automated daily transition at `02:00 AM` server time via `Game1.NewDay` + `SaveGame.Save`, preceded by a warning (`01:50`), SHA-256 verified backup, and orderly peer disconnect. |
| **Bed-Logout** | Asynchronous sleep mechanic where a player enters their bed (*"Descansar y Desconectar"*) setting `RestedOnGameDay == today` to avoid the one-off rollover penalty without pausing or shutting down the world for others. |
| **Intraday Checkpoint** | Periodic live save of the world and connected farmhands (`CheckpointIntervalMinutes`) bounding progress loss on a crash between `06:00` and `02:00`, rotated by `CheckpointRetention`. |
| **Central Plaza & Bridge Maps** | Declarative residential topology ([ADR-0005](docs/adr/0005-host-plaza-and-bridge-maps.en.md)): 4 portals on the Host Farm (`Farm`) chained westward to up to 3 bridge maps (`Pelican4Ever_Bridge_1..3`, `35×30`), each holding 4 portals and individual mailboxes. |
| **Residence Annex** | Purchasable `40×32` private single-entrance expansion composed from vanilla tilesets ([ADR-0009](docs/adr/0009-annex-vanilla-composition.md)), up to `MaxAnnexesPerResidence = 2` per plot. |
| **Co-Residency (`Owner` / `Tenant`)** | Shared residence of one Owner (`OwnerId`) and up to 1 Tenant (`TenantId`) sharing buildings and animals while keeping wallets, mail, packages, XP, and mine progression individual. |
| **Deferred Mailbox Order** | 24/7 shop order placed from any residence/portal mailbox with immediate payment and physical delivery (owner-locked package, scaffolding, or animal) at `12:00` real time on the target day. |
| **`Lockdown` Mode (*Fail-Closed*)** | Proposed state ([ADR-0010](docs/adr/0010-fail-closed-health-and-backup-contract.en.md)) where `ModEntry` blocks loading the save and refuses incoming connections if a `Security`/`Integrity` patch fails or the game version is unverified. |

---

## 2. Design Principles

1. **1:1 clock with no punitive night pass-out:** One real day equals one game day; night owls can play during `NightHold` (`2550`) without losing their day to the clock or blocking the `02:00 AM` rollover.
2. **Strict Host authority with verified clients:** All shared or sensitive persistent state lives in `ServerWorldState` on the Host. Clients provide local UI and cryptographic device identity.
3. **Individual progression inside a community world:** Community Center, Museum, Joja, mine floors, and the NITV Insectarium are shared; gold, `Shipping Bin`, satiety, skills, mail, packages, and NPC relationships remain individual (even between co-residents).
4. **30–90 minute session cadence:** Offline energy recovery, 4-hour continuous satiety drain, seated "Well-Fed" recovery, and selective throttling of calendar-unbounded activities ([ADR-0008](docs/adr/0008-skill-economy-rebalance.en.md)) favor frequent sessions over endless grinding.
5. **Fail-closed, agent-observable 24/7 operation:** The server must never fail silently when patches break or the game thread hangs; unsafe states must lock closed or expose health off the game thread so `Putre` can alert or restart automatically.

---

## 3. User Profiles

| Profile | Core Need | Friction in Vanilla / Unhardened Hosting | *Pelican 4Ever* Response |
| :--- | :--- | :--- | :--- |
| **Pelican 4Ever Neighbor** | Play 30–90 min/day on a private farm and socialize in town regardless of when a human host is online. | Host must be playing; 14-min days force everyone to sleep together. | 24/7 continuous 1:1 world, private portal & mailbox, async rest, and async market/orders. |
| **Co-Resident / Tenant** | Share a farm, buildings, and livestock with a friend without losing personal gold, progression, or character slot. | Vanilla ties each client to a fixed `Cabin` and mixes or blocks foreign farms. | Exterior mailbox tenancy workflow, shared buildings & `AnimalPage`, isolated personal progression. |
| **Night-Owl Player** | Build, shop, or fish past midnight without closed stores or forced `02:00` collapse. | Forced pass-out at `02:00` and early shop closing times. | `NightHold` stretch at `2550` and 24/7 mailbox orders delivered at `12:00`. |
| **Probation Candidate (`#pelican-gate`)** | Join a private server securely even when home IP addresses change. | Brittle IP allowlisting or slow manual onboarding. | ECDSA P-256 identity (`P4E\x02`), short-fingerprint ticket, and Putre's 2-slot autonomous probation. |
| **Agent Host / Guardian (`Dr4co` / `Putre`)** | Operate 24/7, broadcast announcements, monitor health, and avoid data loss without free shell access. | Hung processes stay alive; broken patches fail open; no restore contract. | `/api/v1/host/*` (with proposed off-thread `/health`), `AgentHost`, systemd watchdog, and `p4e-backup.json` contract. |
| **Server Admin / Owner** | Calibrate economy, approve critical changes, and restore verified backups confidently. | No daily economy telemetry or guaranteed off-site rotation/restore. | `DailyEconomy` telemetry, S3 sync + rotation via Putre, and single-use Level 2 approvals. |

---

## 4. World Subsystem Specification

### 4.1. Time System, Calendar, and Rollover ([ADR-0001](docs/adr/0001-native-daily-rollover.en.md), [ADR-0002](docs/adr/0002-realtime-clock-and-epoch-calendar.en.md))

* **1:1 clock (`06:00`–`01:50` → `0600`–`2550`):** Every 10 real minutes the clock advances 10 game minutes (`600,000 ms` per slot), with smooth ambient lighting interpolation and single-player/empty-server pause prevention.
* **Night hold (`NightHold`, `01:50`–`06:00`):** The clock freezes at `2550` (never reaching `2600`), allowing nocturnal gameplay without clock pass-outs while furnaces and machines process the remaining minutes at rollover.
* **Perpetual calendar (`CalendarEpoch`):** 28-real-day seasons (112 real days per year); Day 1 of every season is always a real Monday and Day 7 a real Sunday. After downtime, `CatchUpPlan` executes up to `MaxCatchUpRollovers` (default `3`) or jumps to the real date.
* **Native rollover (`02:00 AM`):** Warns connected players (`RolloverWarningMinutes`, default `10` min), generates a SHA-256 verified backup, disconnects peers cleanly, and runs `Game1.NewDay` + `SaveGame.Save`. If backup or orderly disconnect fails, the day does not advance. Dawn lighting and NPC schedules begin at `06:00 AM`.

### 4.2. Asynchronous Sleep (*Bed-Logout*) and Intraday Checkpoints

* **Derived states (`SleepPolicy`):** Each player's state (`Awake`, `Rested`, `OfflineSafe`, `OfflinePenalized`) derives from `LastActiveGameDay`, `RestedOnGameDay`, and `LastOutcome` in `ServerWorldState.Players[UID]`.
  * Sleeping in bed (*"Descansar y Desconectar"*) marks `Rested`, restores 100% Energy on wake-up, and transitions to `OfflineSafe` with zero penalty.
  * Remaining `Awake` at rollover applies a one-off penalty (`PenaltyGoldPercent = 0.1` capped at `PenaltyGoldCap = 1000`, `PenaltyEnergyFraction = 0.5`) and moves to `OfflinePenalized` (subsequent offline days are untouched).
  * NPC friendship decay is blocked while in `OfflineSafe` or `OfflinePenalized`.
* **Intraday checkpoints (Epic 11):** Configurable periodic saves (`CheckpointIntervalMinutes`, `CheckpointCloneFarmhands = true`, optional gzip compression) bounding crash losses between `06:00` and `02:00`, skipped during rollovers/festivals and rotated by `CheckpointRetention` (default `12`).

### 4.3. Energy, 1:1 Economy, Mines, and Progression ([ADR-0006](docs/adr/0006-session-energy-satiety-wellfed-spa.en.md), [ADR-0008](docs/adr/0008-skill-economy-rebalance.en.md))

* **Session energy + Satiety:** Base bar of `270` (+ *Stardrops*); full offline recovery over `OfflineRegenFullBarHours = 4`. Eating restores instant Energy up to a Satiety cap of 50% max energy (`DailySatietyFraction = 0.5`), draining continuously over `SatietyDrainHours = 4` real hours.
* **"Well-Fed" and Spa:** Up to `MaxDailyWellFedBuffs = 2` times per day, reaching `WellFedSatietyThresholdFraction = 0.25` activates "Well-Fed" for `60 min`, regenerating `1` Energy every `5 s` while seated or sitting on the ground (`GroundSitKey`). Standing still in the Spa for `30 s` activates once per day `120` Energy and `120` Health over `120 min`.
* **1:1 rebalance ([ADR-0008](docs/adr/0008-skill-economy-rebalance.en.md), *proposed pending `TASK-2107`*):** Daily full-XP cap (`SkillDailyFullXp = 300` for Fishing, Foraging, Mining, Combat; overflow at `SkillOverflowXpMultiplier = 0.25`), `2.5×` slower fish bites and `1.5×` longer fishing minigame, reduced mine ladder spawns (`MineStoneLadderChanceMultiplier = 0.5`, `MineMonsterLadderChance = 0.08`), and Skull Cavern per-floor energy cost (`SkullCavernDescentBaseCost = 1.0`, `SkullCavernDescentDepthDivisor = 25.0`). Crops, 1:1 machines (furnace ~30 min, preserves jar ~2.8 days, keg ~6.9 days), and configurable price multipliers (`CropPriceMultiplier`, `ArtisanPriceMultiplier`, `FishPriceMultiplier`, `MineralPriceMultiplier`, default `1.0x`) complete the balance.
* **Persistent mines (12 h shifts):** Occupied floors stay active; empty floors unload after `MineFloorCooldownMinutes = 45` and unoccupied floors reset at `06:00` and `18:00` real time (`MineResetTimes`).
* **Shared vs. individual progression:**

| Scope | Rule in *Pelican 4Ever* |
| :--- | :--- |
| **Economy & Shipping Bins** | **Individual** (`team.useSeparateWallets = true`, `personalShippingBin`), including between co-residents. |
| **Plot, House, Buildings & Animals** | **Individual per plot** (`Pelican4Ever_Farm_01..16`), shared solely between that plot's `OwnerId` and `TenantId`. |
| **Community Center / Museum / Joja / Insectarium (NITV)** | **Shared (community)** across the whole server. |
| **Mine Elevator, Mail, Packages & XP** | **Individual** per player. Mine floors are shared; mine reward chests are personal. |
| **NPC Relationships** | **Individual** per player, with no decay during `Offline_*`. |

### 4.4. Central Plaza, Residences, Annexes, Co-Residency, and Mailbox

* **Declarative topology ([ADR-0003](docs/adr/0003-declarative-farm-slots.en.md), [ADR-0005](docs/adr/0005-host-plaza-and-bridge-maps.en.md)):** Up to 16 residences (`Pelican4Ever_Farm_01..16`) with `AlwaysActive = true` across 8 official farm types, linked via the Central Plaza (`Farm`, 4 portals) and bridge chain `Pelican4Ever_Bridge_1..3` (`35×30`, 4 portals each), with spatial warp alignment (`SpatialWarpRouter`) and dynamic client culling on vacant slots.
* **40×32 annexes ([ADR-0009](docs/adr/0009-annex-vanilla-composition.md)):** Up to `MaxAnnexesPerResidence = 2` thematic annexes (`FirstAnnexPriceMultiplier = 0.4`, `SecondAnnexPriceMultiplier = 1.0`) with individual private caves (`PrivateFarmCaves`).
* **Visits & permissions (`VisitorGuardModule`):** Owners set `Locked`, `Friends`, or `Public` plus `VisitorsCanEdit`. Visitors without edit permissions cannot use destructive tools, open chests/packages, or place/remove objects or terrariums, while permitted to water (per policy), fish, use slingshots, or swing NITV bug nets.
* **Co-residency (Epic 23):** Each plot supports up to 1 Tenant (`TenantId`) requested and managed via the exterior mailbox. The tenant shares the house (`_Home` with 2 beds and separate wake-up spots), upgrades, cellar, greenhouse, annexes, and building/animal ownership (`Robin`, `Wizard`, `Marnie`, `AnimalPage`), with independent pet ownership, safe eviction evacuation, and automatic ownership transfer on owner release.
* **12:00 deferred mailbox orders (Epic 22):** 24/7 town shop ordering from residence/portal mailboxes with immediate payment and delivery at real `12:00` on the target day (blocked during `NightHold`) via owner-locked crates, scaffolding, or animal arrivals.

### 4.5. Town Life, Pets, Chat, and Mod Intersections

* **NPCs & Festivals:** *Micro-wandering* ($\pm 2$ tiles every 45–90 s at half speed) and decoupled festivals in the evening window `18:00–23:50` (`FestivalWindowStart = 1800`, `FestivalWindowEnd = 2350`), reschedulable (`p4e_festival_move`), with free entry/exit, heat signups, and automatic ceremonies.
* **Client pets (Epic 20):** Buying a `Pet Bowl` at Robin unlocks Marnie's adoption visit on client wake-up and binds the pet authoritatively.
* **`PelicanChat` & `Nature In The Valley` integration (Epic 24):** Channels `/l`, `/g`, `/w` and Discord bridging via `HostEventLog` + `/api/v1/host/chat`; NITV habitat exposure (`"0"`/`"Outside"`, `"1"`/`"Forest"`, `"2"`/`"Beach"`, `"3"`/`"Underground"`) in `Pelican4Ever.NITVCompat`, exclusion of transient buff `"NatCSpeedN"` from `85.7x` scaling, and visitor net allowance.
* **Cosmetics & `AgentHost`:** Automated host sleep without closing multiplayer and *Fashion Sense* body presets for `Dr4co` and `Putre`.

### 4.6. Security, Identity, and Coordination with `Putre`

* **ECDSA P-256 identity ([ADR-0007](docs/adr/0007-dispositivo-identidad-y-acceso-seguro.en.md)):** IP-independent Lidgren `P4E\x02` handshake, timed invite pairing (`PairingInviteTtlHours = 72`), and `RequiredClientMods` validation.
* **2-slot probation protocol (`ProbationTelemetryModule`):** **Putre** issues temporary tickets in `#pelican-gate` (`MaxConcurrentPutreSlots = 2`), tracks staged behavioral telemetry, and can apply an irrevocable blacklist (only removable by the owner).
* **Host API & Resilience Contract ([ADR-0010](docs/adr/0010-fail-closed-health-and-backup-contract.en.md)):** Loopback REST API (`/api/v1/host/*`) dispatched on the main thread, extended in Epic 25 with off-thread `/api/v1/host/health`, fail-closed startup, `sd_notify` watchdog, and `p4e-backup.json` manifest for S3 sync and Putre `saves.restore`.

---

## 5. Functional Requirements (`RF`)

### 5.1 Time Engine, Calendar, and Rollover (`RF-01`..`RF-08`)

| ID | Requirement | Priority |
| :--- | :--- | :--- |
| `RF-01` | Advance the game clock at 1:1 real time (`600,000 ms` per 10 game minutes from `06:00` to `01:50`) without pausing on menus or empty servers. | High |
| `RF-02` | Smoothly interpolate ambient lighting across each 10-real-minute interval (`MillisecondsIntoSlot`). | Medium |
| `RF-03` | Freeze the clock at `2550` (`NightHold`) between `01:50` and `06:00` real time to prevent clock pass-outs while machines finish processing. | High |
| `RF-04` | Deterministically compute the game date (`28` real days/season) from `CalendarEpoch`, keeping Day 1 a real Monday and Day 7 a real Sunday. | High |
| `RF-05` | Plan recovery on startup after nighttime downtime using `CatchUpPlan` (up to `MaxCatchUpRollovers` or direct jump). | High |
| `RF-06` | Broadcast HUD advance warnings (`RolloverWarningMinutes`, default `10` min before at `01:50`) prior to day turnover. | High |
| `RF-07` | Execute daily rollover at `02:00 AM` through native `NewDay` + `SaveGame.Save`, SHA-256 backup, and orderly peer shutdown; abort day advance if backup fails. | High |
| `RF-08` | Start daylight and daytime NPC schedules at `06:00 AM` real time (`DayStartTime`) without advancing routines during night hold. | High |

### 5.2 Asynchronous Sleep and Intraday Checkpoints (`RF-09`..`RF-13`)

| ID | Requirement | Priority |
| :--- | :--- | :--- |
| `RF-09` | Allow each player to rest in their bed (*"Descansar y Desconectar"*) setting `RestedOnGameDay == today` without pausing the world or kicking peers. | High |
| `RF-10` | Derive `SleepState` (`Awake`, `Rested`, `OfflineSafe`, `OfflinePenalized`) solely from `ServerWorldState.Players[UID]` on the Host. | High |
| `RF-11` | Apply the `Awake` penalty (`PenaltyGoldPercent = 0.1` capped at `1,000g`, `PenaltyEnergyFraction = 0.5`) only once and never re-penalize on subsequent offline days (`OfflinePenalized`). | High |
| `RF-12` | Freeze daily NPC friendship decay while a player remains in `OfflineSafe` or `OfflinePenalized`. | Medium |
| `RF-13` | Execute periodic intraday checkpoints (`CheckpointIntervalMinutes`) cloning connected farmhands (`CheckpointCloneFarmhands`), skipped during rollovers or festivals and rotated per `CheckpointRetention`. | High |

### 5.3 Economy, Satiety, and 1:1 Progression (`RF-14`..`RF-20`)

| ID | Requirement | Priority |
| :--- | :--- | :--- |
| `RF-14` | Fully regenerate the base energy bar (`270` + *Stardrops*) offline within `OfflineRegenFullBarHours = 4` hours. | High |
| `RF-15` | Gate instant food energy recovery with continuous Satiety (`DailySatietyFraction = 0.5`, `SatietyDrainHours = 4` real hours) without blocking health healing or buffs. | High |
| `RF-16` | Activate "Well-Fed" up to `MaxDailyWellFedBuffs = 2` times/day above `WellFedSatietyThresholdFraction = 0.25`, restoring `1` Energy every `5 s` when seated on furniture or ground (`GroundSitKey`). | High |
| `RF-17` | Activate the daily Spa buff after `30 s` still in water (`120` Energy and `120` Health over `120 min`). | Medium |
| `RF-18` | Apply configurable 1:1 rebalance rules (proposed ADR-0008): `SkillDailyFullXp` cap, fishing bite/minigame multipliers, mine ladder rates, Skull Cavern floor cost, price multipliers, and daily telemetry (`DailyEconomyTelemetry`). | Medium |
| `RF-19` | Keep occupied mine floors alive and reset unoccupied floors on 12-hour shifts (`06:00` and `18:00`) or after `MineFloorCooldownMinutes = 45`. | High |
| `RF-20` | Keep separate wallets (`useSeparateWallets = true`) and `personalShippingBin` individual while Community Center, Museum, Joja, mine floors, and Insectarium remain shared. | High |

### 5.4 Residences, Bridges, Annexes, Tenants, and Mailbox (`RF-21`..`RF-30`)

| ID | Requirement | Priority |
| :--- | :--- | :--- |
| `RF-21` | Declaratively register 4 to 16 residences (`FarmSlotCount`) in `Data/Locations` across Central Plaza (`Farm`, 4 portals) and bridges `Pelican4Ever_Bridge_1..3` (`35×30`, 4 portals each). | High |
| `RF-22` | Allow choosing among the 8 official farm types when claiming a plot, preserving coherent spatial orientation (`SpatialWarpRouter`) per `North`/`South` edge. | High |
| `RF-23` | Apply dynamic network culling on clients to hide and avoid traffic for vacant residences and annexes. | High |
| `RF-24` | Allow purchasing up to `MaxAnnexesPerResidence = 2` `40×32` annexes per plot (`FirstAnnexPriceMultiplier = 0.4`, `SecondAnnexPriceMultiplier = 1.0`) and individual private cave choice (`PrivateFarmCaves`). | Medium |
| `RF-25` | Enforce residence access policies (`Locked`, `Friends`, `Public`, `VisitorsCanEdit`) on both Host and client, blocking destructive tools and chest opening for visitors. | High |
| `RF-26` | Support co-residency of 1 Owner and up to 1 Tenant via requests, invites, acceptance, leave, and eviction from the exterior portal mailbox. | High |
| `RF-27` | Decouple the technical login `Cabin` from the shared residence interior (`Pelican4Ever_Farm_XX_Home` with 2 beds and separate wake-up spots) to prevent login slot collisions. | High |
| `RF-28` | Share construction (`Robin`, `Wizard`), livestock (`Marnie`, `AnimalPage`), cellar, and annexes between Owner and Tenant while isolating wallets, mail, packages, XP, and mines. | High |
| `RF-29` | Evacuate an evicted tenant if the gate is `Locked`, redirect pending item packages on tenancy termination, and transfer ownership (`OwnerId = TenantId`) if the owner releases the plot. | High |
| `RF-30` | Enable 24/7 mailbox shop orders with upfront payment and `12:00` real-time delivery (blocked during `NightHold`) via owner-locked packages. | High |

### 5.5 Town Life, Festivals, Pets, and Mod Integration (`RF-31`..`RF-36`)

| ID | Requirement | Priority |
| :--- | :--- | :--- |
| `RF-31` | Simulate autonomous villager *micro-wandering* ($\pm 2$ tiles every 45–90 s at half speed) avoiding collisions and `Warps`. | Medium |
| `RF-32` | Run decoupled festivals in the evening/night window (`1800–2350`), reschedulable (`p4e_festival_move`), with free entry/exit and no clock freeze. | Medium |
| `RF-33` | Manage registration heats in competitive festivals, automatic ceremonies (`20:45` warning / `21:00` start), and deferred prize delivery (`PendingFestivalPrizes`). | Medium |
| `RF-34` | Unlock client pet adoption upon purchasing a `Pet Bowl`, binding the pet to bowl and owner. | Medium |
| `RF-35` | Guarantee *Nature In The Valley* compatibility: `"0"`..`"3"` habitats in `Pelican4Ever.NITVCompat`, `"NatCSpeedN"` exclusion from `85.7x` scaling, and visitor net allowance without modifying terrariums. | Medium |
| `RF-36` | Support multi-channel chat (`/l`, `/g`, `/w`) integrable with `PelicanChat`, escrowed community market, `AgentHost` sleep mode, and `Dr4co`/`Putre` cosmetics. | Medium |

### 5.6 Security, Identity, and Host API (`RF-37`..`RF-42`)

| ID | Requirement | Priority |
| :--- | :--- | :--- |
| `RF-37` | Authenticate clients using asymmetric ECDSA P-256 device keys (`P4E\x02` in Lidgren) independent of IP, with timed invite pairing (`PairingInviteTtlHours = 72`). | High |
| `RF-38` | Enforce mod version and exact `RequiredClientMods` checks during initial handshake (`ClientVersionGuardModule`). | High |
| `RF-39` | Run Putre's 2-slot probation protocol (`MaxConcurrentPutreSlots = 2`), behavioral telemetry (`ProbationTelemetryModule`), and Putre-irrevocable blacklist. | High |
| `RF-40` | Expose the Host REST API (`/api/v1/host/*` and `HostEventLog`), including whitelisted remote commands and safe restart (`p4e_safe_restart`). | High |
| `RF-41` | (*Proposed ADR-0010 / Epic 25*) Register all Harmony patches in `PatchRegistry` (`Security`, `Integrity`, `Experience`) and fail closed into `Lockdown` on critical failures or unverified `VerifiedGameVersions`. | High |
| `RF-42` | (*Proposed ADR-0010 / Epic 25*) Expose `GET /api/v1/host/health` off the game thread, emit `WATCHDOG=1` pulses via `sd_notify`, and generate `p4e-backup.json` manifests per backup. | High |

---

## 6. Non-Functional Requirements and Operational Resilience (`RNF`)

| ID | Requirement | Metric / Criterion |
| :--- | :--- | :--- |
| `RNF-01` | **Pure Domain Separation** | All pure time, calendar, sleep, economy, topology, security, and policy logic lives in `Pelican4Ever.Core` (`net6.0`) with **0** references to `StardewValley`, `SMAPI`, `MonoGame`, or `Harmony`. |
| `RNF-02` | **Automated Unit Test Suite** | Pure `Pelican4Ever.Core` logic is verified by `xUnit` tests in `Pelican4Ever.Tests` (`net8.0`) run via `dotnet test` (1,684 passing tests). |
| `RNF-03` | **Build Quality** | `TreatWarningsAsErrors = true` and `Nullable = enable` enforced in `Pelican4Ever.Core` and `Pelican4Ever` (`0` warnings permitted). |
| `RNF-04` | **Internationalization (`i18n`)** | Full support in English (`i18n/default.json`), Spanish (`i18n/es.json`), and Simplified Chinese (`i18n/zh-CN.json`), with fallback provider and no raw keys shown. |
| `RNF-05` | **Isolated Modular Architecture** | All SMAPI modules implement `IModule` and register cleanly per side (`Host` / `Client`) and toggle setting (`ModToggleModule`). |
| `RNF-06` | **Minimal Network Surface & Zero Client Secrets** | Only `24642/udp` is exposed publicly; the REST API (`AdminApiPort = 8085` default, loopback) is never externally bound unless explicitly configured, and clients store zero server/Discord tokens. |
| `RNF-07` | **Rollover & Checkpoint Integrity** | Every daily backup and checkpoint verifies SHA-256 per file; `RolloverDayGuard` prevents double penalty application and no day advances if backup or orderly disconnect fails. |
| `RNF-08` | **Headless Operation & Compute Budget** | Dedicated host supports suppressed rendering (`HeadlessRenderModule` on `Xvfb`), targeting a production VM of `2 vCPU`, `LP_NUM_THREADS=1`, `Nice=5`, and `MemoryMax=4G`. |
| `RNF-09` | **Fail-Closed Startup (Proposed ADR-0010)** | Any `Security`/`Integrity` patch failure or version outside `VerifiedGameVersions` (`["1.6.15"]`) and `MinimumSmapiVersion` (`4.1.0`) blocks loading `AutoHostSaveName` and connections without relying on Harmony patches, unless listed in `LockdownOverride`. |
| `RNF-10` | **Lock-Free Observable Health (Proposed ADR-0010)** | `GET /api/v1/host/health` responds directly from the HTTP thread in `< 50 ms` bypassing `MainThreadActionQueue`, exposing `Status`, `SecondsSinceLastTick`, `SaveLoaded`, `Lockdown.Reasons[]`, and `Patches.Failed[]`. |
| `RNF-11` | **Hang Detection via Watchdog (Proposed ADR-0010)** | If `NOTIFY_SOCKET` is set, sends `WATCHDOG=1` every `15 s` only while `SecondsSinceLastTick <= HangThresholdSeconds` (`120 s`); `systemd` sets `WatchdogSec=180` and `NotifyAccess=all`. |
| `RNF-12` | **Negative Monitor Vigilance in Putre (Proposed ADR-0010)** | `Putre` polls `/api/v1/host/health` every `60 s`, stays silent during nominal `Ok` states, and sends `owner.alert` only on transitions to `Lockdown`, `Degraded`, hang, crash, or backup failure and recovery. |
| `RNF-13` | **Backup Contract & S3 Retention (Proposed ADR-0010)** | Every backup writes `p4e-backup.json` (`FormatVersion = 1`) with default local retention `BackupRetentionDays = 14` and `CheckpointRetention = 12`; Putre runs `aws s3 sync` without `--delete` after `backup.created` and IaC manages S3 expiration. |
| `RNF-14` | **Verified Offline Restore (Proposed ADR-0010)** | `saves.restore` (Level 2 in Putre) stops the game, verifies `p4e-backup.json` and SHA-256 checksums, moves prior save data to `Pelican4Ever_Backups/pre-restore/<UTC>/` without deleting, copies target data, and requires `health.Status == "Ok"`, `SaveLoaded == true`, and matching `TotalGameDays`. |
| `RNF-15` | **Empirical Scale Validation (Proposed ADR-0010)** | `FarmSlotCount > 8` is not declared production-supported until measuring save size, `SaveGame.Save()` duration, `LastCheckpointLongestTickMs`, bandwidth, and NITV density across 8, 12, and 16 synthetic farmhands (`LIVE-2503` / `PUTRE-401`). |
