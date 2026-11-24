# ADR-0013 · Fail-Closed Startup, Host Health, and Backup Contract with Putre

🌐 **Languages:** [Español](0013-fail-closed-health-and-backup-contract.md) | English | [中文](0013-fail-closed-health-and-backup-contract.zh-CN.md)

- **Status:** Proposed
- **Date:** 2026-11-16
- **Traceability note:** Originally drafted as ADR-0010 (2026-11-16) and renumbered to ADR-0013 to avoid colliding with [ADR-0010](0010-scalable-residences-admission-queue.md) and preserve the ADR-0011 slot on `feat/v0.6-extended-concurrency`.
- **Scope:** `Pelican 4Ever` (host mod) and its sibling repository `Putre` (`putre-ops` / Hermes), which operates the production VM.
- **Related:** [ADR-0001](0001-native-daily-rollover.en.md) (rollover and SHA-256 backup), [ADR-0004](0004-host-authority-client-mod-and-satiety.en.md) (host authority), [ADR-0007](0007-dispositivo-identidad-y-acceso-seguro.en.md) (per-device identity). Backlog: Epic 25 in [backlog.md](../../backlog.md) and Putre backlog (`Putre/backlog.md`).

## Context

An audit on 2026-11-16 uncovered four silent failures in 24/7 operation:

1. **Harmony patches fail open.** There are roughly 84 patches. The three helpers that apply them (`ClientSecurityModule.TryPatch`, `FarmSlotModule.PatchSafe`, and `MailOrderModule.PatchSafe`) log the error and continue. If `GameServer.checkFarmhandRequest` fails to patch, the server accepts farmhands without the ADR-0007 device challenge. The same applies to `VisitorGuardModule`, package ownership, and parcel isolation. A Stardew update (1.6.16+) or SMAPI update is enough to trigger this, and `manifest.json` does not pin a game version.
2. **A hang goes undetected.** The systemd units use `Restart=always/on-failure`, which only triggers if the process exits. If the game thread freezes (an infinite loop, deadlock, or a save that never finishes), the process stays alive and nobody notices. Putre only checks health inside the `01:50` maintenance window.
3. **Backups have no formal contract.** `SaveBackupService` verifies SHA-256 while copying, but writes no manifest. `BackupRetentionDays = 0` (no rotation) is the default. `backup.saves_to_s3` exists in Putre, but is not scheduled. `saves.restore` returns `unavailable` until the format is agreed with Pelican.
4. **Scale has never been measured.** The design targets 8–16 players on a 2-vCPU VM using CPU rendering, while live tests use at most Host + 2 farmhands.

## Decision

### 1. Patch Criticality and Fail-Closed Startup

Every Harmony patch is declared in a single registry (`PatchRegistry`) with an explicit criticality:

| Criticality | Examples | On failure |
| --- | --- | --- |
| `Security` | `checkFarmhandRequest`, `sendPlayerIntroduction`, `FarmhandSlot`, `VisitorGuardModule`, `MailOrderModule` package opening, `ClientVersionGuardModule` | **Lockdown** |
| `Integrity` | Native rollover, `SaveGame`, checkpoints, 1:1 clock/`NightHold`, `findStructure`, `ServerWorldState` persistence | **Lockdown** |
| `Experience` | Minimaps, cosmetics, NITV, chat, menus | **Degraded** startup with warning |

The lockdown decision is pure domain logic in Core (`PatchHealthPolicy.Decide`) covered by unit tests. In **Lockdown**:

- **The save is not loaded and the multiplayer server does not open.** This does not rely on any patch: `ModEntry` enforces it before `AutoHostSaveName`. Thus, an authentication patch failure never leaves an open door.
- The host API stays up so Putre can observe the state and alert the owner.
- Lockdown is cleared only by fixing the root failure or with an explicit owner exception: `LockdownOverride` lists accepted patch names, and the override emits a `host.lockdown.overridden` event. There is no global "start anyway" switch.

### 2. Verified Game Version List

`ServerConfig.VerifiedGameVersions` (default `["1.6.15"]`) and `MinimumSmapiVersion` (default `4.1.0`). A game version outside the list triggers **Lockdown** with reason `UnverifiedGameVersion`, unless `AllowUnverifiedGameVersion = true`. That flag exists for isolated test labs and must not be enabled in production. On the VM, the game is installed via `steamcmd` at a pinned version, never auto-updated. Putre checks the list before `release.apply` and `pelican.runtime_update`.

### 3. Health Observable Outside the Game Thread

- `GET /api/v1/host/health` is served directly from the HTTP thread using an atomic snapshot. **It does not go through `MainThreadActionQueue`**, so it keeps responding even if the game thread is hung. Normative fields: `Status` (`Ok` | `Degraded` | `Lockdown` | `Starting`), `LastTickUtc`, `SecondsSinceLastTick`, `SaveLoaded`, `Lockdown.Reasons[]`, `Patches.Total`, `Patches.Failed[]` (name and criticality), `GameVersion`, `SmapiVersion`, `ModVersion`, and `UptimeSeconds`.
- New events in `HostEventLog`: `host.lockdown`, `host.degraded`, and `host.lockdown.overridden`.
- **Systemd watchdog:** if `NOTIFY_SOCKET` is set, a background thread sends `WATCHDOG=1` every 15 s **only if** `SecondsSinceLastTick <= HangThresholdSeconds` (120 s by default, above the longest measured save). The unit declares `WatchdogSec=180` and `NotifyAccess=all`. If the game hangs, systemd kills and restarts the process under `Restart=always`. Without `NOTIFY_SOCKET` (local lab or Docker), the thread does nothing.

### 4. Backup and Restore Contract

- Every backup (daily or checkpoint) includes `p4e-backup.json` with:
  - `FormatVersion = 1`;
  - `Kind` (`Daily` | `Checkpoint` | `PreRestore`);
  - `SaveFolderName`, `TotalGameDays`, `GameDate`, `CreatedUtc`, `ModVersion`, and `GameVersion`;
  - `Files[]` with relative path, byte size, and uncompressed `Sha256`;
  - `Compressed`.
  
  `ServerWorldState` travels inside the save (`WriteSaveData`), making each backup a consistent snapshot. Excluded: `config.json` (managed by Putre and IaC) and telemetry under `data/`.
- **Default local retention:** `BackupRetentionDays = 14` and `CheckpointRetention = 12`. Full history lives off-server in S3. Putre syncs via `aws s3 sync` without `--delete` after every `backup.created`, and expiration is governed solely by the S3 bucket lifecycle policy in IaC, never by Putre (deleting backups is Level 3 in its contract).
- **Restore** always runs through `saves.restore` in Putre (Level 2, requiring owner approval) in this order:
  1. Stop the game.
  2. Verify the manifest and all SHA-256 checksums.
  3. Move the current save to `Pelican4Ever_Backups/pre-restore/<UTC>/` (never delete it).
  4. Copy the restored save into place.
  5. Start the game.
  6. Require `health.Status == Ok`, `SaveLoaded`, and `TotalGameDays == manifest.TotalGameDays` (or the day computed by `CatchUpPlan`).
  
  Pelican never performs hot restores.

### 5. Measure Scale Before Promising It

`FarmSlotCount > 8` is not declared production-supported until the agent load test harness (in the live-agent test backlog) measures, with 8, 12, and 16 synthetic farmhands on the reference VM: save size, rollover save duration, `LastCheckpointLongestTickMs`, per-player bandwidth, and NITV creature density (`locationCap × numberOfPlayers`). Measured figures will be added to this ADR upon acceptance.

## Split Across Repositories

| Component | `Pelican 4Ever` | `Putre` |
| --- | --- | --- |
| Patch registry, policy, and lockdown | Implements | Reads `health` and alerts |
| Verified versions | Enforces on startup | Checks before updating; pins `steamcmd` version on the VM |
| `/health` and events | Exposes | Polls every minute; alerts only on incidents and recoveries |
| Systemd watchdog | Sends `WATCHDOG=1` | Declares `WatchdogSec`/`NotifyAccess` in the VM unit |
| Backup manifest | Writes and validates | Verifies, syncs to S3, and restores |
| Load test bench | Provides synthetic farmhands and metrics | Provides reference VM and stores the report |

## Consequences

- **Pros:** a game update or broken patch leaves the server closed and visibly reported rather than silently wide open. A hang becomes an automatic restart plus an alert. Restore becomes a verifiable procedure, enabling `saves.restore` in Putre.
- **Cons:** more visible downtime. After a game update, the server stays closed until the version is verified. A poorly chosen `HangThresholdSeconds` could kill a long save, which is why it must be calibrated with the load test bench. Every new patch must declare its criticality.
- **Normative figures** (index rule 2): `WatchdogSec=180`, heartbeat every 15 s, `HangThresholdSeconds=120`, `BackupRetentionDays=14`, `CheckpointRetention=12`, and `VerifiedGameVersions=["1.6.15"]`.
