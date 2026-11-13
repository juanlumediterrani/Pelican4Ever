# Client mode: joining a Pelican 4Ever server

How to install the mod on your PC or Steam Deck and join a Pelican 4Ever server — the official
playtest server or one you host yourself.

Related: [servidor-preview.en.md](../servidor-preview.en.md) (joining the official playtest
server) · [host-local.md](host-local.md) · [host-vps.md](host-vps.md)

---

## 1. Requirements

| Item | Version |
| --- | --- |
| Stardew Valley | 1.6.15 (Steam or GOG) |
| SMAPI | 4.1 or newer (4.1.10+ recommended) |
| Pelican 4Ever | 0.2.0 (same *major.minor* as the server) |
| Modpack | **Exact** versions used by the server (below) |

Always launch the game through **SMAPI**, never through the plain game executable.

---

## 2. Required modpack (exact versions)

During the initial handshake the server verifies that the client declares every mod of the modpack
**at the exact version the server runs**. If anything is missing or mismatched, the server kicks
the client during the handshake.

| Mod | UniqueID | Reference version |
| --- | --- | --- |
| Content Patcher | `Pathoschild.ContentPatcher` | 2.9.1 |
| SpaceCore | `spacechase0.SpaceCore` | 1.28.4 |
| Fashion Sense | `PeacefulEnd.FashionSense` | 7.5.0 |
| Dr4co Dragon Body | `Dr4co.Stardew.FashionSenseBody` | 0.5.0 |
| Nature In The Valley | `Nature.NatureInTheValley` | 1.3.4 |
| Nature In The Valley Content | `Nature.NatureInValleyContent` | 1.3.4 |
| SH's Wild Animals In The Valley | `SmolHooman.WildAnimalsInTheValley` | 1.2.1 |
| Pelican 4Ever NITV Compat | `Pelican4Ever.NITVCompat` | 1.0.0 |
| **Pelican 4Ever** | `Pelican4Ever.SVRTMP` | 0.2.0 |

> These are the versions of the reference server. The rule is always the one of the server you are
> joining: if the host updates a mod in the pack, you must update to the same exact version or you
> will not get in. The server log says exactly what is missing or mismatched (for example
> `falta spacechase0.SpaceCore 1.28.4`).

Mods the server does **not** have installed are not required (the ID is ignored with a warning).
Extra mods on your side are not checked.

---

## 3. Installation

1. Copy the `Pelican4Ever` folder (unzipped from `Pelican4Ever 0.2.0.zip`) into `Mods/`:
   - **Linux / Steam Deck:** `~/.steam/steam/steamapps/common/Stardew Valley/Mods/`
     (or `~/.local/share/Steam/steamapps/common/Stardew Valley/Mods/`)
   - **Windows:** `C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\Mods\`
2. Install the rest of the modpack in the same `Mods/` folder (one folder per mod).
3. Verify that `.../Mods/Pelican4Ever/manifest.json` exists.
4. **Do not copy the server's `config.json`**: on a client you can delete the generated one and
   SMAPI will rebuild it with the defaults. Only the networking options matter on a client; host
   options (`AutoHostSaveName`, `RestartExitsProcess`, `HeadlessRendering`) do not apply.

### 3.1 First launch and keys

Start the game through SMAPI. The mod generates your cryptographic key pair at:

```
Mods/Pelican4Ever/data/device_key.json
```

On Linux and macOS the mod sets `0600` permissions automatically (your private key must not be
readable by other users).

---

## 4. Connecting

1. In the main menu: **Multiplayer → Join LAN Game** (*Direct Connection*).
2. Enter the **IP and port** the host gives you (usually `24642`).
   - With Steam not running there are no Steam invite codes: you always join by IP.
3. The initial handshake is cryptographic (`P4E\x02`): the server sends a 30-second challenge and
   your client signs it with your private key. **Your private key and any reusable token are never
   transmitted** over the game channel.

### 4.1 First contact: pairing

The first time, the server checks your **device fingerprint** (SHA-256 of your public key). Since
you are not authorized yet, it disconnects you cleanly and shows:

> Dispositivo registrado para aprobación. Huella: a1b2c3d4e5f6. Solicita al propietario que apruebe
> tu acceso en Discord.

Other messages you may see, depending on the whitelist state:

| Message | Meaning | What to do |
| --- | --- | --- |
| `Dispositivo registrado para aprobación. Huella: …` | Your request is pending | Send the fingerprint to the host and wait for approval |
| `Servidor protegido por lista blanca. Solicita una invitación al propietario en Discord.` | No invite exists for your IP | Ask the host to create an invite for your IP |
| `Todas las invitaciones para tu IP están reservadas por otras solicitudes pendientes.` | Invites exist for your IP but are reserved by another fingerprint | Ask for a fresh invite for your alias, or for the pending one to be approved |

**The fingerprint does not depend on your IP**: if your IP changes or you reboot the router, you
still get in with the same approval.

Once approved, press *Direct Connection* again and you reach the farmhand menu:

- **Free** farmhands can be picked: they are permanently bound to your device.
- Farmhands owned by **other players** are dimmed, labelled `[De otro jugador]` and cannot be
  selected.

---

## 5. What you can do in game

| Command | What it does |
| --- | --- |
| `/g <message>` | Global server chat |
| `/l <message>` | Proximity chat (only nearby players hear it) |
| `/w <player> <message>` | Direct whisper to another neighbour |
| `/residencia` | Residence permissions: `Locked`, `Friends`, `Public`, friend list and visitor building permission |
| `/evento empezar` | Sign up for the day's festival heat |

The Central Plaza (`Farm`) is the main map: from there, and along the bridges to the east, are the
portals to the 16 private residences. Each neighbour has their own farm (8 official types) and
their own private cave.

---

## 6. Troubleshooting

### "I get kicked on join"

The server checks your mod and modpack versions during the handshake and kicks with the exact
reason in its log (`player.rejected`). Common causes:

| Reason in the server log | Fix |
| --- | --- |
| `no usa SMAPI.` | Launch the game through SMAPI, not the plain executable |
| `no tiene instalado Pelican 4Ever.` | The `Mods/Pelican4Ever` folder is missing |
| `versión incompatible (0.1.0; el servidor usa 0.2.0).` | The mod itself is compared by *major.minor*: reinstall the server's version |
| `no tiene el modpack del servidor: falta <ID> <version>.` | That mod is missing: install it at that **exact** version |
| `no tiene el modpack del servidor: <ID> <yourversion> (el servidor usa <version>).` | Version mismatch: align it with the server's |

> The client does not get a descriptive message — the server just kicks. That is why the server's
> fingerprint (log) is the first thing to check.

### "I changed computer / reformatted"

Copy `Mods/Pelican4Ever/data/device_key.json` to the new machine: your identity travels with it and
you keep the same approval. If the file is gone, ask the host to bind your new fingerprint to your
character (`p4e_migrate_player <fingerprint> <farmhand>`).

### "It used to work and now it doesn't"

- Did a modpack mod auto-update? Versions must be **exact**.
- Did the server restart and change the whitelist? Ask your host to check `p4e_pair_list`.
- If you share an IP (house or company NAT), invites may be reserved by other fingerprints.

### "The game is slow"

The server does the heavy lifting (simulation and networking); your machine only draws. If it is
slow, look at your graphics settings and mods, not at the server.

---

## 7. Uninstalling

Delete the `Mods/Pelican4Ever` folder (and the rest of the modpack if you don't need it for
anything else). Deleting `Mods/Pelican4Ever/data/device_key.json` creates a brand-new identity: the
server will treat it as an unauthorized device and you will have to pair again.
