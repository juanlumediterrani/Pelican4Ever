# Joining the preview server (official playtest server)

The **preview server** is the official Pelican 4Ever world, open to playtesters and collaborators
during the testing phase. It is a real, persistent server: the clock runs 1:1 with real time, the
day rolls over on its own at 02:00, and the world keeps running when you disconnect.

> **The preview server requires the full modpack, at the exact versions.** It is not an "open"
> server: if a mod is missing or the version does not match, you are kicked on join.

Related: [despliegue/cliente.en.md](despliegue/cliente.en.md) (full client guide)

---

## 1. Requirements

| Item | Version |
| --- | --- |
| Stardew Valley | 1.6.15 |
| SMAPI | 4.1 or newer |
| Pelican 4Ever | 0.2.0 |
| Modpack | **Exact** versions (table below) |
| Device approval | Yes, you must pair first |

### Modpack required by the server

| Mod | UniqueID | Version |
| --- | --- | --- |
| Content Patcher | `Pathoschild.ContentPatcher` | 2.9.1 |
| SpaceCore | `spacechase0.SpaceCore` | 1.28.4 |
| Fashion Sense | `PeacefulEnd.FashionSense` | 7.5.0 |
| Dr4co Dragon Body | `Dr4co.Stardew.FashionSenseBody` | 0.5.0 |
| Nature In The Valley | `Nature.NatureInTheValley` | 1.3.4 |
| Nature In The Valley Content | `Nature.NatureInValleyContent` | 1.3.4 |
| SH's Wild Animals In The Valley | `SmolHooman.WildAnimalsInTheValley` | 1.2.1 |
| Pelican 4Ever NITV Compat | `Pelican4Ever.NITVCompat` | 1.0.0 |

Mods the server does **not** have installed are not required. Extra mods on your side are not
checked. The full list is in the table above.

---

## 2. Step by step

### Step 1 · Join the Discord

<https://discord.gg/2x5RuvvQB>

Test windows, modpack version changes and bug reports are posted there.

### Step 2 · Install the mod and the modpack

Unzip `Pelican4Ever 0.2.0.zip` into your `Mods/` folder and install the rest of the modpack at
the **exact versions** in the table above. Paths and permissions in
[despliegue/cliente.en.md](despliegue/cliente.en.md).

### Step 3 · Connect by direct IP

In the game: **Multiplayer → Join LAN Game** (*Direct Connection*), using the **IP and port of the
preview server** given in the Discord.

### Step 4 · Your first attempt: the fingerprint

The server is protected by a device whitelist. On your first attempt:

1. The mod creates (or already has) your key at `Mods/Pelican4Ever/data/device_key.json`.
2. The cryptographic handshake (`P4E\x02`) proves your key is genuine.
3. Because your device is not authorized yet, the server **registers the request and disconnects
   you**, showing your **short public fingerprint**: 12 hex characters, e.g. `a1b2c3d4e5f6`.

### Step 5 · Request approval

Send a **direct message (DM)** to **Dextrure** on Discord with:

- your player name or alias,
- your **12-character short fingerprint**.

Approval is **permanent**: it is bound to your device key, not to your IP. You can join after a
router reboot or with a dynamic IP without pairing again.

### Step 6 · Get in

Press *Direct Connection* again with the same IP and port. This time the server recognises your
fingerprint and opens the **farmhand menu**:

- **New tester:** pick any free slot. It is permanently and exclusively bound to your device.
- **Other players' slots:** dimmed, labelled `[De otro jugador]`, and not selectable.

If you already had a character (for example from the closed beta), ask the host to bind it to your
new fingerprint.

---

## 3. What to test

This is a testing phase: what you report feeds the backlog. Worth checking:

| System | What to observe |
| --- | --- |
| **1:1 clock** | In-game time matches real time (1 real day = 1 in-game day) |
| **Continuous night** | Between **01:50** and **06:00** the clock freezes at `2550`: you can play at night without passing out |
| **Day rollover** | At **02:00** the date advances; a 10-minute warning in the HUD plus a sound cue |
| **Residences** | The Central Plaza and the bridges to the east lead to the 16 private residences; each neighbour has a farm (8 types) and a private cave; `/residencia` switches permissions (`Locked`, `Friends`, `Public`) |
| **Asynchronous rest** | You can sleep and return to the menu without pausing the world or kicking anyone |
| **Satiety and "Well-Fed"** | Eating any food raises the satiety bar; at ≥ 1/4 satiety the *Well-Fed* buff lets you regenerate energy while seated |
| **Spa** | A visit to the railroad spa restores up to 120 energy and health over 2 real hours |
| **Neighbourhood market** | Buy and sell crops and artisan goods, with payment even if the buyer was offline |
| **Chat** | `/g` global, `/l` proximity, `/w` whispers |
| **NITV wildlife** | Animals and insects across the 16 residences, the plaza and the bridges (requires the compatibility pack) |
| **Pets** | Collecting and adopting pets from the client's **Animals** tab |

---

## 4. Common problems on the preview server

### I get kicked right after joining

It is the **modpack**: the server requires each mod at its exact version and kicks you during the
handshake. The server log states the exact reason (`player.rejected`):

- `no tiene el modpack del servidor: falta <ID> <versión>.` → that mod is missing.
- `no tiene el modpack del servidor: <ID> <yourversion> (el servidor usa <versión>).` → version
  mismatch.
- `no usa SMAPI.` → you launched the game without SMAPI.
- `versión incompatible (…)` → your mod version does not match (compared by *major.minor*).

### "Server protected by a whitelist…"

No active invite exists for your IP. Ask for one on the Discord before trying again: if your IP is
shared (home, work, university) some invites may already be reserved by another fingerprint.

### "All invites for your IP are reserved…"

There is already a pending request with a different fingerprint from your IP. Wait for it to be
approved, or ask the host for a fresh invite under your alias.

### My IP changed and I can't get in

The fingerprint does not depend on your IP: once approved you keep getting in. If you are being
kicked, the problem is a mod version or the server restarting with a reloaded whitelist — ask on
the Discord.

### I changed computer

Copy `Mods/Pelican4Ever/data/device_key.json` to the new machine and you will keep the same
approval. If it is gone, ask them to bind your new fingerprint to your character.

### The game is slow or I get disconnected

- The server is persistent and does the heavy lifting; if your machine is slow, look at your
  graphics settings.
- Occasional disconnects are normal on a home network: the world keeps running and you rejoin
  with the same fingerprint.

---

## 5. Where to report

- **Discord:** `#bugs-feedback` channel or a direct message to the dev team.
- Include what you were doing, what you expected, what happened, and your **12-character short
  fingerprint** if it is a connection problem.
