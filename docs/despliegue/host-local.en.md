# Host deployment locally (your own machine)

Guide to set up and play the host save on your PC or Steam Deck: you see the game, you manage
it and the neighbours join by direct IP.

If what you want is **a 24/7 server with no screen on a VPS**, use
[host-vps.md](host-vps.en.md) instead of this one.

Related documents: [cliente.md](cliente.en.md) · [host-vps.md](host-vps.en.md) ·
[../host-api.md](../host-api.md) · [../servidor-preview.md](../servidor-preview.en.md)

---

## 1. What "local host mode" is

The host is the player who loads the co-op save. Locally:

- The game opens **with a window** and you see everything that happens (you can play your turn).
- You can **sleep and close** the game without taking the others down: the world stays alive on
  its own until the host loads the save again.
- Neighbours connect through *Direct Connection* to the UDP port `24642` of your machine.

Stardew Valley **has no dedicated server mode**: there is always a local player in front
(the host). The `DedicatedServerMode` flag in `config.json` **is not used in the current code**.

---

## 2. Requirements

- Stardew Valley **1.6.15** (Steam or GOG).
- **SMAPI 4.1 or newer** (installed in the game; the mod declares `MinimumApiVersion: 4.1.0`).
- The mod **Pelican 4Ever 0.2.0** and the full modpack, at the same versions the server uses
  (see [../servidor-preview.md](../servidor-preview.en.md)).

Common `Mods/` paths:

| OS | Path |
| --- | --- |
| Linux / Steam Deck | `~/.steam/steam/steamapps/common/Stardew Valley/Mods/` (or `~/.local/share/Steam/steamapps/common/Stardew Valley/Mods/`) |
| Windows | `C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\Mods\` |

---

## 3. Install the mod

1. Build it on your development machine (`dotnet build Pelican4Ever.sln -c Release`) or use the
   ZIP `Pelican4Ever 0.2.0.zip` the team gives you.
2. Unzip the ZIP **inside** the `Mods/` folder, so that you end up with
   `Mods/Pelican4Ever/manifest.json`.
3. Also install the modpack (Content Patcher, SpaceCore, Fashion Sense, Dr4co Dragon Body,
   Nature In The Valley, SH's Wild Animals In The Valley and `Pelican4Ever.NITVCompat`): the
   server requires clients to have **the exact version the host runs**.
4. Start the game **through SMAPI** (the `StardewModdingAPI` launcher), not the plain
   executable.

On startup, the mod generates `Mods/Pelican4Ever/data/device_key.json` with your key pair
(ECDSA P-256). On Linux and macOS it leaves them at `0600`.

---

## 4. Create the host save

In the game: **Co-op → Host → New Farm**. Pick the host farm and the layout you want
(Standard, Riverland, Forest, Hilltop, Wilderness, Four Corners, Beach or Meadowlands). Save
the game and note the **exact folder name** that appears in `Saves/` (for example
`Pelican4Ever_452974881`).

That name is used for two things:

- **`AutoHostSaveName`** in `config.json`: when the title screen is reached, the mod loads that
  save as the host on its own. If the name does not exist exactly, it **does not load any
  other**: it writes that down in the log.
- The folder where the pairing sidecar lives: `Saves/<partida>/pelican4ever_pairing.json`.

---

## 5. Local `config.json`

It is in `Mods/Pelican4Ever/config.json` (SMAPI creates it on its own with the default values
if it does not exist; full template in [`config.example.json`](../../config.example.json)).

```json
{
  "TimeZoneId": "Europe/Madrid",
  "AutoHostSaveName": "Pelican4Ever_452974881",
  "RestartExitsProcess": false,
  "HeadlessRendering": false,
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
  "AgentHost": false
}
```

Notes for the local case:

- **`HeadlessRendering: false`**: you are looking at the screen, that is no reason to turn
  rendering off. If you leave the save loaded without watching it, set it to `true` to save
  CPU.
- **`RestartExitsProcess: false`**: the controlled restart goes back to the title screen and
  the auto-loader reopens the save in the same process (3 s). With `true` the game closes and
  whatever relaunches it (systemd, a script) opens it again with the new mod.
- **`AdminApiBindAllInterfaces: false`**: the API stays on `127.0.0.1` only.
  With `true` they listen on every interface: do not do that on a public network without a
  firewall.
- **`AdminApiToken`**: the API **does not start** if it is empty, if it keeps the default value
  `pelican4ever_secret_admin_token` or if it is the placeholder `CAMBIA-ESTE-TOKEN`.
- **If the `config.json` is invalid, the mod does not register** (log: «Configuración inválida:
  Pelican 4Ever queda DESACTIVADO hasta corregir config.json»). There is no degraded mode.

---

## 6. Open the game to the world

The game opens the co-op server on **UDP port 24642** (the 1.6.15 engine sets it). Only one
instance can host per machine.

- **Neighbours on your same network:** give them the local IP of the machine that hosts the
  game (`ip addr` / `ipconfig`) and the port `24642`.
- **Neighbours over the internet:** open UDP `24642` on the router (port forwarding) and on the
  system firewall, and use your public IP. If your IP changes, pairing by fingerprint keeps
  working (the identity does not depend on the IP), but the neighbours have to point to the
  new one. A VPS with a fixed IP avoids this problem: it is the recommended option for stable
  groups.

> **Steam does not need to be open**: without it there are no Steam invite codes, you join
> through *Direct Connection*.

---

## 7. Register the neighbours

The server is protected by a device whitelist (`RequirePairingWhitelist`). The complete flow,
step by step, is in [cliente.md](cliente.en.md). Summary from the host side:

1. **Create an invite** bound to the neighbour's IP. You get a 6-character code, single use,
   valid for `PairingInviteTtlHours` (72 h by default):
   ```
   p4e_pair_allow <nombre> <IP_DEL_VECINO>
   ```
2. The neighbour tries to connect: the server registers its device and disconnects it, showing
   its **short fingerprint** (12 hex characters, e.g. `a1b2c3d4e5f6`).
3. **Approve the fingerprint**:
   ```
   p4e_pair_approve a1b2c3d4e5f6
   ```
   (or `p4e_pair_approve <huella> <id_de_peón>` to approve and also bind the character).
4. The neighbour connects again and enters the farmhand selector. Other players' farmhands
   show up dimmed with `[De otro jugador]` and cannot be selected.

Support commands:

| Command | What it does |
| --- | --- |
| `p4e_pair_list` | Authorized devices, live invites, pending requests, blocked attempts |
| `p4e_pair_reject <huella>` | Rejects a pending request |
| `p4e_pair_revoke <usuario\|huella\|id>` | Revokes a device's access and kicks its players |
| `p4e_claim_list` | Lists farmhands and who owns them |
| `p4e_claim_reset <nombre_o_id>` | Releases ownership of a character so its owner can claim it |

> The list of remote API commands is **shorter** than the console: `p4e_pair_reject` and
> `p4e_claim_list` are console-only (the API does expose `p4e_claim_list`, `p4e_pair_list`,
> `p4e_pair_allow`, `p4e_pair_approve`, `p4e_migrate_player`, `p4e_claim_reset`,
> `p4e_pair_revoke`). Exact catalogue in [../host-api.md](../host-api.md).

---

## 8. Local API

With the save loaded, the API is available:

```bash
TOKEN=TOKEN-LARGO-Y-ALEATORIO
curl -s -H "Authorization: Bearer $TOKEN" http://127.0.0.1:8085/api/v1/host/clock
```

There is no web panel in this version: the panel lives in the `feat/web-panel` branch and is
not in `main` or `dev`. Everything it did is done through the API (`/api/v1/host/`).

---

## 9. When you finish playing

- You can **go to bed and exit to the menu** without taking the others down: each player sleeps
  and disconnects separately. If the host is human, closing the game **leaves the save without
  a server** until it is loaded again.
- To keep the world alive with nobody in front, leave the host unattended: `AgentHost: true`
  makes the host's rest register **without disconnecting it or exiting to the title screen**.
- With `AutoHostSaveName` set, going back to the title screen by hand reopens the save in
  3 s: to stop the server, stop the process (under systemd:
  `systemctl --user stop pelican4ever-host`).

systemd units already available for an unattended local host:
`deploy/systemd/pelican4ever-host.service` (user, with a graphical session) and
`deploy/systemd/pelican4ever-preprod.service` + `pelican4ever-preprod-xvfb.service`
(user, own virtual screen, isolated `HOME`/`XDG_*`).

---

## 10. Common problems

| Symptom | Cause | Solution |
| --- | --- | --- |
| The game starts but the mod does not show up | Invalid `config.json` | The SMAPI log lists all the configuration errors |
| The neighbour is kicked on join | Different mod or modpack version | The server log states the exact reason; the API's `player.rejected` collects it |
| «Servidor protegido por lista blanca…» | The device is not authorized | Create an invite with `p4e_pair_allow` and approve the fingerprint |
| «Todas las invitaciones para tu IP están reservadas…» | There are invites for another fingerprint on that IP (NAT) | Create another invite for that alias or approve the pending one |
| The save does not load when the game is reopened | `AutoHostSaveName` does not match | Check the log (`SaveMissing`) and the exact folder name in `Saves/` |
| `Address already in use` | Another Stardew instance or port 24642 in use | `ss -lunp \| grep 24642` and `pgrep -af Stardew` |
| The neighbour sees the save but not the NITV animals | `Pelican4Ever.NITVCompat` is missing on their client | Exact modpack version on the client, mandatory |
