# Host API v1 (`Pelican 4Ever` — `TASK-803` and `TASK-804`)

Local HTTP contract to operate a Pelican 4Ever server from outside the game. It lets you supervise
the clock in real time, check the neighbours' status, coordinate festival heats and ceremonies,
register the host's daily rest without disconnecting the session, and schedule controlled
restarts.

**It is optional.** Pelican 4Ever works the same without it: a human host has `/evento empezar`,
Lewis, the console commands and the automatic rollover. The API does not start until you configure
your own `AdminApiToken` and it only listens locally. A possible client is the **Stardew Voyager** /
**OpenClaw** agent host, which also needs `AgentHost: true` to rest without disconnecting; neither
this mod depends on Voyager nor Voyager on this mod.

---

## 1. Security, Network and Main Thread Execution

1. **Listens only on loopback by default:**
   - By default (`AdminApiBindAllInterfaces: false` in `config.json`), the server listens only on
     `http://127.0.0.1:{AdminApiPort}/` and `http://localhost:{AdminApiPort}/`.
   - The `Access-Control-Allow-Origin: *` header is not sent.
2. **Startup rejected with the default token:**
   - If `AdminApiToken` is empty or keeps the default value `pelican4ever_secret_admin_token`,
     `AdminApiModule` **does not start** the `HttpListener` and logs an error in the SMAPI log.
3. **Authentication required on every route:**
   - Every `/api/*` or `/api/v1/host/*` route requires `Authorization: Bearer <AdminApiToken>`
     (or `?token=<AdminApiToken>`).
   - Without a valid token, the server answers `401 Unauthorized`.
4. **Dispatch on the game's main thread (`UpdateTicked`):**
   - Every world query or mutating action queued from an HTTP thread runs on the Stardew Valley
     main thread through `MainThreadActionQueue.Drain()` during the `GameLoop.UpdateTicked` event.

---

## 2. Routes of the Versioned Space `/api/v1/host/`

### `GET /api/v1/host/clock`

Returns the in-game and real time, whether the server is in the night hold window (`IsNightHold`),
the moment of the next Rollover, the outcome and backup of the last Rollover, and the served save.

**`200 OK` response (`HostClockDto`):**
```json
{
  "RealTimeUtc": "2026-10-31T15:00:00.0000000Z",
  "LocalTime": "2026-10-31T16:00:00",
  "TimeZoneId": "Europe/Madrid",
  "GameTimeOfDay": 1600,
  "TotalGameDays": 12,
  "Year": 1,
  "Season": "spring",
  "DayOfMonth": 13,
  "IsNightHold": false,
  "NextRolloverLocalTime": "2026-11-01T02:00:00",
  "SecondsUntilNextRollover": 36000,
  "LastRolloverGameDay": 11,
  "LastRolloverOutcomeSummary": "rested=3, penalized=0, untouched=1",
  "LastBackupFileName": "PelicanValley_20261031_020000_000",
  "LastBackupCreatedUtc": "2026-10-31T01:00:00.0000000Z",
  "SaveFolderName": "PelicanValley_452974881",
  "IsMultiplayerServer": true
}
```

`SaveFolderName` is the `Saves/` folder of the loaded save and `IsMultiplayerServer` tells whether
it is open as a cooperative server; an unattended supervisor uses them to check that the server
serves the authorised save (guide: [despliegue/host-vps.md](despliegue/host-vps.md)).

### `GET /api/v1/host/players`

Returns the connected and registered players, their location and current coordinates, their
assigned residence, whether they are inside today's festival, and their full `PlayerRecord` (rest
state and satiety).

**`200 OK` response (`HostPlayersResponseDto`):**
```json
{
  "TotalOnline": 2,
  "TotalRegistered": 4,
  "Players": [
    {
      "PlayerId": 1234567890,
      "Name": "VoyagerHost",
      "IsOnline": true,
      "IsHost": true,
      "LocationName": "Town",
      "TileX": 28,
      "TileY": 67,
      "ResidenceSlotId": "Pelican4Ever_Farm_01",
      "ResidenceDoorId": "Town_Door_01",
      "ResidenceFarmType": "Standard",
      "InFestival": false,
      "SleepState": "Rested",
      "LastActiveGameDay": 12,
      "RestedOnGameDay": 12,
      "LastOutcome": "Rested",
      "SatietyUsed": 40,
      "WellFedBuffsUsed": 1,
      "WellFedActive": true,
      "SpaBuffsUsed": 0,
      "SpaBuffActive": false,
      "SatietyGameDay": 12,
      "PendingMarketGold": 0,
      "IsClaimed": true,
      "OwnerTokenMasked": "a1b2...c5d6"
    }
  ]
}
```

### `GET /api/v1/host/festival`

Returns the state of today's festival, its time window, location, whether it has a main event or a
live ceremony, the players present in the grounds, those signed up for the next heat, the heats
already run and who has taken part today.

**`200 OK` response (`HostFestivalDto`):**
```json
{
  "IsFestivalToday": true,
  "FestivalId": "festival_spring13",
  "FestivalName": "Festival del Huevo",
  "Season": "spring",
  "DayOfMonth": 13,
  "LocationName": "Town",
  "WindowStart": 1800,
  "WindowEnd": 2350,
  "IsWindowOpen": true,
  "HasMainEvent": true,
  "IsCeremonyFestival": false,
  "PresentPlayerIds": [1234567890, 987654321],
  "SignedUpPlayerIds": [987654321],
  "PlayedPlayerIds": [],
  "RunsCount": 0
}
```

### `GET /api/v1/host/map?location=Town`

Returns a `HostMapDto` with `LocationName`, `Width`, `Height`, `Tiles`, `Players`, `Warps`,
`AvailableMaps`, `Season` and `GameDay`. It only queries existing locations, including building
interiors with their unique names. If the requested location is not found, it shows Town. With no
world loaded it returns dimensions 0.

`Tiles` is a `byte[]` serialized by System.Text.Json as **base64** (not as a JSON array). You have
to decode it before reading the index `y * Width + x`. For example, the cells `[1, 3, 8, 11]` arrive
as `"AQMICw=="`. The values are: 0 unknown, 1 floor, 2 blocked, 3 water, 4 building, 5 path, 6 grass,
7 tree, 8 exit, 9 mine, 10 NPC and 11 player. It is a supervision classification, not a navigation
mesh.

`Players` contains markers with `PlayerId`, `Name`, `TileX`, `TileY`, `IsHost`; `Warps` contains
`TileX`, `TileY`, `TargetMap`, `TargetX`, `TargetY`. Every world query runs on the main thread and
requires the same token as the other routes.

### `POST /api/v1/host/festival/start`

Equivalent to `/evento empezar` in `FestivalParticipationModule`. It closes the current sign-up
and starts the next heat of the main event only for the signed-up players.

**`200 OK` response (`HostActionResultDto`):**
```json
{
  "Ok": true,
  "Message": "[Festival] Empieza el evento (tanda #1) con 2 participantes.",
  "ExecutedOnThreadId": 1,
  "EventId": 14
}
```

### `POST /api/v1/host/festival/ceremony`

Fires the live community ceremony (`TASK-702`, Luau or Moon Jellyfish Dance) with all the
neighbours present in the festival grounds.

**`200 OK` response (`HostActionResultDto`):**
```json
{
  "Ok": true,
  "Message": "Ceremonia iniciada para 3 vecino(s) en festival_summer11.",
  "ExecutedOnThreadId": 1,
  "EventId": 15
}
```

### `POST /api/v1/host/announce`

Sends a global notice to the connected players via chat, HUD or both channels, and logs it in the
event log.

**Request body (`HostAnnounceRequestDto`):**
```json
{
  "message": "La búsqueda de huevos comenzará en 5 minutos.",
  "channel": "both"
}
```

**`200 OK` response (`HostActionResultDto`):**
```json
{
  "Ok": true,
  "Message": "Anuncio emitido (both).",
  "ExecutedOnThreadId": 1,
  "EventId": 16
}
```

### `POST /api/v1/host/rest`

Registers the host player's daily rest (`RestedOnGameDay` and `LastActiveGameDay`) without calling
`Game1.ExitToTitle()` nor disconnecting the session (`TASK-804`).

**`200 OK` response (`HostActionResultDto`):**
```json
{
  "Ok": true,
  "Message": "Descanso registrado. Modo anfitrión agente activo: el servidor permanece en línea.",
  "ExecutedOnThreadId": 1,
  "EventId": 17
}
```

### `POST /api/v1/host/restart`

Schedules or cancels a controlled restart of the server (`TASK-804`), equivalent to the console
command `p4e_safe_restart`.
- Warns the connected players with a configurable advance notice (`warningMinutes`).
- Computes and reports the unsaved daily progress at risk (`ProgressAtRisk`), since Stardew Valley
  only saves when the day changes.
- By default (`immediate: false`), it waits until just after the next save/Rollover
  (`WaitingForNextSave: true`).
- If `immediate: true` is requested for a major cause, it runs the restart after the given warning
  minutes.

**Request body (`HostRestartRequestDto`):**
```json
{
  "reason": "Actualización de mantenimiento",
  "immediate": false,
  "warningMinutes": 5,
  "cancel": false
}
```

**`200 OK` response (`SafeRestartStatusDto`):**
```json
{
  "IsPending": true,
  "Mode": "after_next_save",
  "Reason": "Actualización de mantenimiento",
  "Immediate": false,
  "WaitingForNextSave": true,
  "WarningMinutes": 5,
  "RequestedAtUtc": "2026-10-31T15:10:00.0000000Z",
  "ExecuteNotBeforeUtc": null,
  "WarningAnnouncement": "[Reinicio Controlado] El servidor se reiniciará justo después del próximo guardado/Rollover (preaviso de 5 min)...",
  "ProgressAtRisk": {
    "CurrentGameDay": 12,
    "CurrentGameTime": 1430,
    "GameMinutesSinceMorning": 510,
    "OnlinePlayersCount": 2,
    "UnrestedActivePlayersCount": 1,
    "WouldLoseUnsavedDayProgress": true,
    "Summary": "Progreso no guardado del día 12 (8h 30m desde las 06:00, 2 jugador(es) conectado(s), 1 sin descansar hoy). Stardew Valley solo guarda al cambiar de día (Rollover)."
  },
  "EventId": 57
}
```

### `GET /api/v1/host/events?since=N`

Returns the append-only list of events with `Id > N` in strictly increasing order and without
duplicates. If `LastEventId` is lower than the requested cursor, the log has been reset and the
client must query again from 0.
It includes festival sign-ups (`festival.signup`, `festival.signup.left`), heats started and
finished (`festival.run.started`, `festival.run.ended`, `festival.ceremony.started`), rollover
started, completed and stalled at the day change (`rollover.started`, `rollover.completed`,
`rollover.stalled`: the mod had to launch the day-change fade again; it carries `nudges` and
`secondsSinceAdvance`), backup created (`backup.created`), intraday checkpoints
(`checkpoint.created` with `backupName`, `bytes`, `totalMs`, `longestTickMs`, `slow` and
`nextIntervalMinutes`; `checkpoint.skipped` with `reason` when it was due and could not be made;
`checkpoint.failed` with `error`), host rest (`host.rested`), announcements (`host.announcement`),
controlled restarts (`host.restart.scheduled`, `host.restart.cancelled`, `host.restart.executed`),
render leases (`host.render.lease.granted`, `host.render.lease.released`) and player
connection/disconnection (`player.connected`, `player.disconnected`), mod rejections
(`player.rejected`, with the reason), pairing (`player.paired`, `player.pairing.invited`,
`player.pairing.revoked`) and remote commands (`host.command`, `host.test.command`).

**`200 OK` response (`HostEventsResponseDto`):**
```json
{
  "SinceId": 0,
  "LastEventId": 2,
  "Events": [
    {
      "Id": 1,
      "Type": "player.connected",
      "TimestampUtc": "2026-10-31T15:01:00.0000000Z",
      "GameDay": 12,
      "GameTime": 1500,
      "Summary": "Jugador conectado: Alex",
      "Data": {
        "playerId": 987654321,
        "playerName": "Alex"
      }
    },
    {
      "Id": 2,
      "Type": "festival.signup",
      "TimestampUtc": "2026-10-31T15:02:00.0000000Z",
      "GameDay": 12,
      "GameTime": 1500,
      "Summary": "Alex se apuntó a festival_spring13.",
      "Data": {
        "festivalId": "festival_spring13",
        "playerId": 987654321,
        "playerName": "Alex",
        "signedUpCount": 1
      }
    }
  ]
}
```

### `GET /api/v1/host/commands`

**Closed** list of Pelican console commands that can be run remotely. With the SMAPI console
disabled (server without a terminal) it is the only way to launch them. Anything not on the list
cannot be invoked, even if it exists as a command.

**`200 OK` response (`HostCommandCatalogDto`):**
```json
{
  "Commands": [
    { "Command": "p4e_status", "Risk": "Read", "Usage": "p4e_status", "Available": true },
    { "Command": "p4e_checkpoint_now", "Risk": "Safe", "Usage": "p4e_checkpoint_now", "Available": true },
    { "Command": "p4e_claim_reset", "Risk": "Sensitive", "Usage": "p4e_claim_reset <nombre_o_id>", "Available": true }
  ]
}
```

`Risk`: **`Read`** only reports (`p4e_status`, `p4e_doors`, `p4e_claim_list`, `p4e_pair_list`);
**`Safe`** changes something without losing progress (`p4e_checkpoint_now`: saves and copies
without changing the day; `p4e_pair_allow <user> <ip>`: prepares an IP registration with a valid
pairing code `PairingInviteTtlHours`, 72 h by default); **`Sensitive`** changes the world or a
player (`p4e_claim_reset`, `p4e_pair_approve`, `p4e_migrate_player`, `p4e_pair_revoke`,
`p4e_forage_wave`, `p4e_festival_move`): whoever invokes it should have the owner's approval.
`p4e_rollover_now` (advances the day; use `POST /api/rollover`), `p4e_safe_restart` (has its own
route with warnings), `p4e_time` (mismatches the real calendar), `p4e_migrate_save`, `p4e_give_*`
and the test commands are left out on purpose.

There are commands that **only** exist in the SMAPI console and not in this list:
`p4e_pair_reject` (rejects a pending request), `p4e_checkpoint_spike` (requires
`CheckpointDebugMode`), the `p4e_farm_*`, `p4e_skill`, `p4e_weather`, `p4e_lightning` (require
`EnableTestCommands`), `p4e_render`, `p4e_rollover_now`, `p4e_safe_restart`, `p4e_time`,
`p4e_migrate_save` and `p4e_give_*`.

On a direct IP connection, an updated client that is already authorised answers the server
challenge (`P4E\x02`) during discovery, before receiving the character list. The challenge expires
after 30 seconds, it is bound to the host session and only accepts one response. The client proves
possession of its asymmetric private key through ECDSA NIST P-256 digital signing without
transmitting secrets over the game channel; changing IP does not require a new pairing. Both the
server and the client must have this version. Old clients receive a safe rejection with a version
notice. The approval of a new device is handled out of band (by direct message on Discord via
Putre) validating its public fingerprint.

This challenge authenticates the device identity and the access to the farmhand slot until
entering the game. UDP traffic encryption during the save is out of the agreed scope.

### `POST /api/v1/host/command`

Runs **on the game thread** a command from that list and returns what it writes to the SMAPI log
while it runs (up to 40 lines of 300 characters; what it emits later, on other ticks, is not
included). It validates the command, the number of arguments and their alphabet
(`A-Z a-z 0-9 . _ - @ :`, max. 64 for SHA-256 fingerprints) and requires a loaded save. Every
attempt, accepted or rejected, leaves a `host.command` event.

**Request body (`HostCommandRequestDto`):**
```json
{
  "Command": "p4e_claim_reset",
  "Args": [ "mTx" ]
}
```

**`200 OK` response (`HostCommandResultDto`):**
```json
{
  "Ok": true,
  "Command": "p4e_claim_reset",
  "Risk": "Sensitive",
  "Output": [ "Posesión de 'mTx' liberada." ],
  "Message": "p4e_claim_reset ejecutado.",
  "ExecutedOnThreadId": 1,
  "EventId": 31
}
```

A command that is not allowed or has invalid arguments returns `Ok: false` with the reason in
`Message` and is not run.

### `GET /api/v1/host/render`

State of the **server without rendering** (`HeadlessRendering`). On an unattended server
(`AutoHostSaveName` configured) the host stops rendering with the world loaded: the simulation
keeps running at 60 Hz, the network works the same and each client renders its own. Without a GPU,
rendering on the CPU takes whole cores even if nobody looks at the host screen. It still renders on
the title screen and the loading screen, with the save screen open (`SaveGameMenu` does not start
saving until it has been rendered) and while there is a render lease.

**`200 OK` response (`HostRenderStatusDto`):**
```json
{
  "HeadlessRenderingEnabled": true,
  "Drawing": false,
  "Reason": "Suppressed",
  "Leases": [],
  "Message": ""
}
```

`Reason`: `Disabled` (option turned off), `Attended` (without `AutoHostSaveName`: there is a human
host), `WorldNotReady` (title or loading), `DrawRequiredMenu` (save screen), `Lease` (active
lease) or `Suppressed` (not rendering).

### `POST /api/v1/host/render`

Requests or releases an expiring **render lease**, for whoever needs to see the host screen
(Stardew Voyager driving menus, Putre or a diagnostic). Each requester has at most one: asking
again renews it. It lasts between 1 and 120 minutes (it is trimmed); if the requester drops, it
expires on its own and the host goes back to not rendering. `Minutes: 0` releases it. It does not
touch the game state. The `takeMapScreenshot` captures (the ones from the Voyager bridge) do not
need a lease.

Also: console command `p4e_render [status] | <minutes> | off` (requester `console`) and, for other
mods, the mod API `RequestRendering(requester, minutes)` / `ReleaseRendering(requester)` /
`IsHostDrawing()` (`Helper.ModRegistry.GetApi<...>("Pelican4Ever.SVRTMP")`).

**Request body (`HostRenderRequestDto`):**
```json
{
  "Requester": "putre",
  "Minutes": 15
}
```

**`200 OK` response (`HostRenderStatusDto`):**
```json
{
  "HeadlessRenderingEnabled": true,
  "Drawing": true,
  "Reason": "Lease",
  "Leases": [
    { "Requester": "putre", "ExpiresAtUtc": "2026-11-09T05:15:00Z" }
  ],
  "Message": "Dibujado prestado a 'putre' hasta 2026-11-09T05:15:00.0000000Z."
}
```

---

## 3. Classic Routes `/api/`

Earlier routes, kept for compatibility with external clients (Voyager bridge, supervision
scripts). Same authentication and main thread rules.

| Method | Route | Effect |
| --- | --- | --- |
| GET | `/api/status` | `ServerStatusDto`: mod version, clock, `DedicatedHost`, players, doors and market |
| POST | `/api/rollover` | Forces the day change (save, backup and peer shutdown) |
| POST | `/api/mines/reset` | Regenerates the empty mine floors |
| POST | `/api/festival/reschedule` | Reschedules a festival: body `{"originalDate": "...", "newDate": "..."}`, both mandatory (400 if one is missing) |

Any other route returns `404 {"error": "Not found"}`.

---

## 4. Non-Remote Console Commands

The API list is a deliberate subset of the console. These are only launched from the SMAPI console
(or from another mod):

| Command | What it does |
| --- | --- |
| `p4e_pair_reject <fingerprint_or_alias>` | Rejects a pending registration request |
| `p4e_rollover_now` | Forces the day change (it has its own route: `POST /api/rollover`) |
| `p4e_safe_restart [reason] [--immediate\|-i] [--minutes N\|-m N] [--cancel\|-c]` | Controlled restart (it has its own route) |
| `p4e_time <+X\|-X\|reset\|set HH:mm>` | Mismatches the clock (tests only) |
| `p4e_render [status\|<minutes 1-120>\|off]` | Render lease |
| `p4e_migrate_save [slot\|geo]` | Migrates a save to the Central Plaza |
| `p4e_give_gold <amount> [player]`, `p4e_give_item <id_or_name> [amount] [player]` | Debugging |
| `p4e_farm_found`, `p4e_farm_tp`, `p4e_farm_inspect`, `p4e_farm_spawn`, `p4e_lightning`, `p4e_weather`, `p4e_skill` | Residence tests: they require `EnableTestCommands: true` |
| `p4e_checkpoint_spike [clone] [force]` | Destructive diagnostic: requires `CheckpointDebugMode: true` |
| `p4e_dr4co_sprite`, `p4e_putre_sprite` | Host cosmetics |
