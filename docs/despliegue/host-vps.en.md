# Host deployment on a VPS (24/7 server)

Guide to have Pelican 4Ever serving a save permanently on a remote Linux server, with no
desktop and with nobody having to open the game by hand.

- It is **not** a native "dedicated server" of the game: the host is still a normal co-op save
  (`Game1.multiplayerMode = 2`) with a local player (the host itself). `DedicatedServerMode` in
  `config.json` **does nothing** in the current code: do not use it as a switch for anything.
- What is specific to the VPS deployment is **not drawing** (`HeadlessRendering`) and **having
  no GPU**: the game needs a graphics context even if it paints nothing, and `Xvfb` solves
  that.

Related documents: [host-local.md](host-local.en.md) · [cliente.md](cliente.en.md) ·
[../host-api.md](../host-api.md) · [../servidor-preview.md](../servidor-preview.en.md)

---

## 1. Requirements

| Resource | Minimum | Recommended |
| --- | --- | --- |
| CPU | 2 vCPU | 4 vCPU (16 players) |
| RAM | 2 GB | 4 GB |
| Disk | 20 GB | 40 GB (save + backups + checkpoints) |
| OS | Debian 12 / Ubuntu 22.04 x86-64 | anything with systemd |
| Network | outgoing internet + incoming UDP port `24642` | — |

Measured on a 2 vCPU VM with the save loaded, without remote players and with
`HeadlessRendering` on: **~23 % CPU** from the game (CPU rendering was 94 %).

---

## 2. Prepare the system

```bash
sudo apt update
sudo apt install -y xvfb libgl1-mesa-dri libglu1-mesa curl unzip git
sudo useradd --system --create-home --home-dir /var/lib/pelican4ever --shell /usr/sbin/nologin pelican4ever
sudo mkdir -p /opt/pelican4ever/game /var/lib/pelican4ever/.config/StardewValley/Saves \
              /var/lib/pelican4ever/.config/StardewValley/Pelican4Ever_Backups \
              /etc/pelican4ever
sudo chown -R pelican4ever:pelican4ever /var/lib/pelican4ever /opt/pelican4ever
```

`Xvfb` is mandatory: without a graphics context the game does not start. `libgl1-mesa-dri`
provides the software rendering (`llvmpipe`) that the virtual screen uses.

---

## 3. Install the game and SMAPI

Stardew Valley **cannot run without a licence**: use SteamCMD with the account that owns the
game, or copy the game folder from a machine that has it.

### Option A · SteamCMD (recommended, the game updates itself)

```bash
sudo apt install -y steamcmd
sudo mkdir -p /var/lib/steamcmd
sudo ln -sf /var/lib/steamcmd/steamcmd.sh /usr/bin/steamcmd

sudo -u pelican4ever steamcmd \
  +force_install_dir /opt/pelican4ever/game \
  +login <CUENTA_STEAM> \
  +app_update 413150 validate \
  +quit
```

### Option B · Copy from another machine

Copy the whole game folder (Windows or Linux) to `/opt/pelican4ever/game` and give ownership
to `pelican4ever`. This route does not update itself: repeat it on every game patch.

### SMAPI

1. Download the **Linux** package from [smapi.io](https://smapi.io/) (SMAPI 4.1 or newer).
2. Unzip it **inside** `/opt/pelican4ever/game`, so that you end up with
   `/opt/pelican4ever/game/StardewModdingAPI`.
3. Install the runtime that SMAPI needs:

```bash
sudo apt install -y dotnet-runtime-6.0
```

Check that it starts before going on (it must open the title screen on the virtual screen):

```bash
sudo -u pelican4ever env HOME=/var/lib/pelican4ever DISPLAY=:99 \
  xvfb-run -a sh -c 'cd /opt/pelican4ever/game && ./StardewModdingAPI'
```

---

## 4. Install the mod and the modpack

### 4.1 Build (on your development machine)

The mod project **is not built in CI**: it needs the game and SMAPI DLLs, which do not exist
on a GitHub runner. Build it locally and upload the resulting ZIP.

```bash
cp local.props.example local.props      # set <GamePath> if the game is not in the default location
dotnet build Pelican4Ever.sln -c Release
```

The build leaves:

- the mod deployed at `<GamePath>/Mods/Pelican4Ever/` (`EnableModDeploy` property),
- the package `Pelican4Ever/bin/Release/net6.0/Pelican4Ever 0.2.0.zip` (`EnableModZip`
  property).

> `global.json` pins the SDK to **8.0.427** with `rollForward: disable`: use exactly that
> version to build, or the build fails with CS9057.

### 4.2 Upload to the server

```bash
scp "Pelican4Ever/bin/Release/net6.0/Pelican4Ever 0.2.0.zip" usuario@tu-vps:/tmp/
scp deploy/modpack/Pelican4Ever.NITVCompat usuario@tu-vps:/tmp/
```

On the VPS:

```bash
sudo -u pelican4ever mkdir -p /opt/pelican4ever/game/Mods
sudo -u pelican4ever unzip -o -q /tmp/'Pelican4Ever 0.2.0.zip' -d /opt/pelican4ever/game/Mods/
sudo -u pelican4ever cp -r /tmp/Pelican4Ever.NITVCompat /opt/pelican4ever/game/Mods/
```

> `EnableModDeploy` **overwrites** `Mods/Pelican4Ever` on every build. If you build on the
> same machine where you play, copy `config.json` and `data/device_key.json` first.

### 4.3 Mandatory modpack on the server

The server requires the full modpack from the clients **at the exact version the server
runs** (see [../servidor-preview.md](../servidor-preview.en.md)). Install in `Mods/`: Content
Patcher, SpaceCore, Fashion Sense, Dr4co Dragon Body, Nature In The Valley (+ its content
pack), SH's Wild Animals In The Valley and the compatibility pack `Pelican4Ever.NITVCompat`.

If the server does not have one of those mods, the ID shows up in the requirements list but
**is ignored with a warning** in the log: the requirement only exists if the server has it
installed.

---

## 5. Create the host save

The "Co-op → Host → New Farm" screen needs a graphical interface, so the practical thing is
to **create the save on your machine** and copy it to the VPS:

1. On your PC, with the mod installed, create the host save and note the **exact folder
   name** that appears in `Saves/` (for example `Pelican4Ever_452974881`).
2. Copy that folder to the VPS:

```bash
scp -r "Saves/Pelican4Ever_452974881" usuario@tu-vps:/tmp/
sudo -u pelican4ever cp -r /tmp/Pelican4Ever_452974881 \
  /var/lib/pelican4ever/.config/StardewValley/Saves/
```

That name is the value of `AutoHostSaveName`. The auto-load module **never** picks "the most
recent save": if the name does not exist exactly, it loads none and says so in the log.

---

## 6. Server `config.json`

It lives in `/opt/pelican4ever/game/Mods/Pelican4Ever/config.json`. Full template in
[`config.example.json`](../../config.example.json). The essentials on a VPS:

```json
{
  "TimeZoneId": "Europe/Madrid",
  "AutoHostSaveName": "Pelican4Ever_452974881",
  "RestartExitsProcess": true,
  "HeadlessRendering": true,
  "RequireClientMod": true,
  "RequirePairingWhitelist": true,
  "RequiredClientMods": [
    "Pathoschild.ContentPatcher",
    "spacechase0.SpaceCore",
    "PeacefulEnd.FashionSense",
    "Dr4co.Stardew.FashionSenseBody",
    "Nature.NatureInTheValley",
    "Nature.NatureInValleyContent",
    "SmolHooman.WildAnimalsInTheValley",
    "Pelican4Ever.NITVCompat"
  ],
  "AdminApiPort": 8085,
  "AdminApiToken": "TOKEN-LARGO-Y-ALEATORIO",
  "AdminApiBindAllInterfaces": false,
  "AgentHost": false,
  "BackupRetentionDays": 30
}
```

Rules that the mod itself applies when reading the file:

- **If the configuration is invalid, the mod does not register** (log:
  «Configuración inválida: Pelican 4Ever queda DESACTIVADO hasta corregir config.json»).
  There is no degraded mode: either it all works or nothing works.
- An empty `AdminApiToken`, the default value `pelican4ever_secret_admin_token` or the
  placeholder `CAMBIA-ESTE-TOKEN` ⇒ **the HTTP API does not start**. Set your own long token.
- `AdminApiBindAllInterfaces: false` ⇒ the API listens only on `127.0.0.1:8085` and
  `localhost:8085`. With `true` it listens on `http://*:8085/` (do not do that without a
  firewall).
- `RestartExitsProcess: true` **requires** a non-empty `AutoHostSaveName`.
- `DayStartTime` must be exactly `06:00` and `CalendarDayChangeTime` exactly `02:00`.
- `CalendarEpoch` must be a **Monday** (in `yyyy-MM-dd`): the perpetual calendar is derived
  from it.
- `TimeZoneId` is **not validated**: a non-existent IANA identifier blows up at runtime.

### 6.1 Silence the SMAPI console

Under systemd standard input is `/dev/null`: SMAPI reads, gets end of file and **keeps
reading** (a whole CPU core). Turn it off with `Mods/SMAPI-config.json`:

```json
{
  "ListenForConsoleInput": false
}
```

Console commands are no longer available; they are issued through the host API
(`POST /api/v1/host/command`), which is a closed list. Details in
[../host-api.md](../host-api.md).

---

## 7. Install the service

Clone or upload the repository to the VPS (the service only needs the files in
`deploy/systemd/`) and copy the units:

```bash
sudo cp deploy/systemd/pelican4ever-vps-xvfb.service /etc/systemd/system/
sudo cp deploy/systemd/pelican4ever-vps.service        /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now pelican4ever-vps.service
```

The unit is a **system** unit, not a user one: it does not depend on anyone being logged in
or on a desktop existing. It launches the game with `HOME=/var/lib/pelican4ever`, so the
saves and the backups live outside `/opt`:

```
/var/lib/pelican4ever/.config/StardewValley/Saves/            partidas
/var/lib/pelican4ever/.config/StardewValley/Pelican4Ever_Backups/  copias diarias y checkpoints
/opt/pelican4ever/game/Mods/Pelican4Ever/                     mod + config.json
```

If the game directory is not the one in the unit, adjust `STARDEW_DIR` in
`/etc/systemd/system/pelican4ever-vps.service` or create `/etc/pelican4ever/host.env` (the
unit loads it if it exists).

### Check that it is alive

```bash
sudo systemctl status pelican4ever-vps
sudo journalctl -u pelican4ever-vps -f          # expect "[AutoHost] Cargando ... como anfitrión"
curl -s -H "Authorization: Bearer $TOKEN" http://127.0.0.1:8085/api/v1/host/clock
```

---

## 8. Network and firewall

| Port | Protocol | Opened to the outside? |
| --- | --- | --- |
| `24642` | UDP | **Yes**, the only public port of the game |
| `8085` | HTTP (host API) | **No**: keep it on loopback and access it through an SSH tunnel |
| `5900` | TCP (VNC) | Not used in this deployment (Xvfb runs with `-nolisten tcp`) |

```bash
sudo ufw allow 24642/udp
sudo ufw enable
```

Without VNC or a graphical server, there is no way to "see" the save. To look at the host
screen, ask for a **drawing loan** through the API (1–120 min) or use `p4e_render`:

```bash
curl -s -X POST http://127.0.0.1:8085/api/v1/host/render \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"requester":"admin","minutes":15}'
```

Access to the API from your laptop, through an SSH tunnel (do not open the port):

```bash
ssh -L 8085:127.0.0.1:8085 usuario@tu-vps
# then, on your laptop: curl -H "Authorization: Bearer $TOKEN" http://127.0.0.1:8085/api/v1/host/clock
```

**Without Steam open there are no invite codes**: players join by direct IP on port `24642`
(in the menu, *Direct Connection*).

---

## 9. Daily operation

### Useful commands (host API)

```bash
TOKEN=TOKEN-LARGO-Y-ALEATORIO
H="Authorization: Bearer $TOKEN"

# Clock and calendar status
curl -s -H "$H" http://127.0.0.1:8085/api/v1/host/clock

# Who is online
curl -s -H "$H" http://127.0.0.1:8085/api/v1/host/players

# Announce to every player
curl -s -X POST -H "$H" -H 'Content-Type: application/json' \
  -d '{"message":"Mantenimiento en 10 minutos","channel":"both"}' \
  http://127.0.0.1:8085/api/v1/host/announce

# Force the day rollover (save + backup + peer shutdown)
# Note: p4e_rollover_now is NOT in the remote command list; use the classic route.
curl -s -X POST -H "$H" http://127.0.0.1:8085/api/rollover

# Controlled restart after the next save
curl -s -X POST -H "$H" -H 'Content-Type: application/json' \
  -d '{"reason":"Actualización del mod","immediate":false,"warningMinutes":5,"cancel":false}' \
  http://127.0.0.1:8085/api/v1/host/restart
```

The full catalogue of routes and remote commands is in [../host-api.md](../host-api.md).

### Register a player

1. Create an **invite** bound to the player's IP (6-character code, single use, expiry
   `PairingInviteTtlHours`, 72 h by default):
   ```bash
   curl -s -X POST -H "$H" http://127.0.0.1:8085/api/v1/host/command \
     -d '{"command":"p4e_pair_allow","args":["nombre","IP_DEL_JUGADOR"]}'
   ```
2. The player connects once: the server registers the request and disconnects it, showing its
   **short fingerprint** (12 hex digits).
3. Approve the fingerprint:
   ```bash
   curl -s -X POST -H "$H" http://127.0.0.1:8085/api/v1/host/command \
     -d '{"command":"p4e_pair_approve","args":["a1b2c3d4e5f6"]}'
   ```
4. The player connects again and gets in.

`p4e_pair_list` shows authorized devices, live invites, pending requests and the latest
blocked attempts. `p4e_pair_revoke <usuario|huella|id>` revokes a device and kicks its
connected players.

---

## 10. Update the mod without losing the save

1. Build on your machine (`dotnet build Pelican4Ever.sln -c Release`) and upload the ZIP.
2. Deploy the new ZIP to `/opt/pelican4ever/game/Mods/` (it overwrites the `Pelican4Ever/`
   folder; do **not** delete `config.json`).
3. Controlled restart, which waits for the next save and warns the players:
   ```bash
   curl -s -X POST -H "$H" -H 'Content-Type: application/json' \
     -d '{"reason":"Actualización del mod","immediate":false,"warningMinutes":5}' \
     http://127.0.0.1:8085/api/v1/host/restart
   ```
4. With `RestartExitsProcess: true` the game closes the process; systemd relaunches it with
   the new mod and `AutoHost` reopens the save on its own.
5. Clients with another version of the mod are kicked until they update (`RequireClientMod`
   compares *major.minor*; the rest of the modpack is compared by **exact version**).

Rollback: keep the previous ZIP, deploy the old one and repeat the controlled restart.

---

## 11. Backups and checkpoints

- **Daily backups:** the ones from the day rollover, in
  `/var/lib/pelican4ever/.config/StardewValley/Pelican4Ever_Backups/<partida>_<fecha UTC>/`, with
  SHA-256 verification. They are not deleted unless `BackupRetentionDays` is greater than 0;
  then the ones from those days are kept and **always** the most recent one.
- **Intraday checkpoints** (disabled by default): with `CheckpointIntervalMinutes` greater
  than 0 the host saves the game every that interval without changing the day or disconnecting
  anyone, in `Pelican4Ever_Backups/checkpoints/`. The last `CheckpointRetention` are kept.
  They are not made during festivals or in the pre-warning window of the day rollover.

### Restore a backup (with the service stopped)

```bash
sudo systemctl stop pelican4ever-vps
cd /var/lib/pelican4ever/.config/StardewValley/Saves
mv Pelican4Ever_452974881 Pelican4Ever_452974881.antes-de-restaurar
cp -r /ruta/a/la/copia Pelican4Ever_452974881
gunzip Pelican4Ever_452974881/*.gz 2>/dev/null || true   # only if it came from checkpoints/
sudo systemctl start pelican4ever-vps
```

---

## 12. Diagnostics

| Symptom | Common cause | What to do |
| --- | --- | --- |
| `Address already in use` | The UDP `24642` is already taken: another Stardew instance or a previous process | `sudo ss -lunp \| grep 24642` · `pgrep -af Stardew` · the mod waits up to 60 s and then says so in the log |
| «Configuración inválida» | A `config.json` key with a value that is not allowed | The log lists **all** the errors; fix them and restart |
| The API answers 401 | Missing, wrong token or still the insecure default value | `curl -H "Authorization: Bearer $TOKEN" ...`; change `AdminApiToken` |
| The API does not answer | `AdminApiPort <= 0`, insecure token or save not loaded | The API **only** listens with the save loaded; check the log |
| CPU at 100 % with the save loaded | CPU rendering active | `HeadlessRendering: true` and `ListenForConsoleInput: false` in `SMAPI-config.json` |
| A client is kicked on join | Different mod or modpack version | The server log states the exact reason (`player.rejected`) |
| The save does not load on startup | `AutoHostSaveName` does not match the folder | The log says `SaveMissing`; check the exact folder name in `Saves/` |
| The clock runs out of sync | Wrong `TimeZoneId` or the server on another time zone | `Timedatectl` and `TimeZoneId` must match |

Quick checks:

```bash
sudo journalctl -u pelican4ever-vps -n 100 --no-pager
sudo systemctl show -p ActiveState,SubState,MemoryCurrent pelican4ever-vps
sudo du -sh /var/lib/pelican4ever/.config/StardewValley/*
```

---

## 13. Known limits

- Only **one** instance of the game can host network saves per machine: the engine sets the
  UDP port `24642`.
- The save is written **on the day rollover** (at 02:00). An unexpected restart loses what was
  done since the last day rollover: use the controlled restart.
- Without Steam open there are no Steam invite codes.
- The host sleeps in its own house: with `AgentHost: true` it registers the rest **without
  disconnecting**; with `false` (human host) that is the norm.
