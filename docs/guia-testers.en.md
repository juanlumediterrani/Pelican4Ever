# Playtester Guide: Connection and Testing in Pelican 4Ever

🌐 **Languages:** [Español](guia-testers.md) | English

Welcome to the **Pelican 4Ever** playtesting phase!

This guide contains everything you need to install the mod on your PC or Steam Deck, securely pair your device with the preview server, and start playing.

---

## 1. Prerequisites

Before you begin, make sure you have:

* **Stardew Valley 1.6.15** (Steam or GOG).
* **SMAPI 4.1.10 or higher** properly installed.
* **Mod update package:** `Pelican4Ever 0.2.0.zip` (available in the Discord playtest channel).
* A stable internet connection.

---

## 2. Mod Installation

### Step 1: Copy Mod Files
1. Download and extract `Pelican4Ever 0.2.0.zip`.
2. Move the `Pelican4Ever` folder into the `Mods` directory of your Stardew Valley installation:
   * **Linux / Steam Deck:**  
     `~/.steam/steam/steamapps/common/Stardew Valley/Mods/`  
     *(or `~/.local/share/Steam/steamapps/common/Stardew Valley/Mods/`)*
   * **Windows:**  
     `C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\Mods\`  
     *(or your custom game installation path)*
3. Verify that the final path is `.../Mods/Pelican4Ever/manifest.json`.

### Step 2: First Launch & Key Generation
1. Launch Stardew Valley using the **SMAPI** executable.
2. During startup, the mod will automatically generate your unique cryptographic key pair in:  
   `Mods/Pelican4Ever/data/device_key.json`
3. On Linux and Steam Deck, the mod automatically applies strict permissions (`0600`) so your private key remains safe and isolated.

---

## 3. Server Connection & Secure Pairing

The server uses a cryptographic device authentication system (`P4E\x02`). This means **you never need to remember passwords or tokens**: your gaming machine authenticates itself automatically, and once approved, you can reconnect from any internet connection (even if your home router restarts or your public IP address changes).

### Step 1: Initial Connection Attempt
1. From the main game menu, navigate to **Co-op** -> **Join LAN Game** (Direct Connection).
2. Enter the preview server's IP address and port (shared in the Discord server).
3. The server and your client will perform an automatic cryptographic verification handshake.
4. Because it is your first time connecting and your device is not yet whitelisted, the server will register your request and cleanly disconnect you with an on-screen prompt displaying your **short public fingerprint** (a 12-character hex code, for example: `a1b2c3d4e5f6`).

### Step 2: Request Approval
1. Copy or write down your short public fingerprint.
2. Head over to the Pelican 4Ever Discord server and send a direct message (DM) to the host or post in the private playtester channel with:
   * Your player name / alias.
   * Your short public fingerprint (e.g. `a1b2c3d4e5f6`).
3. The server host will approve your device on the server.

### Step 3: Enter the Server & Choose Your Farmer
1. Once the host confirms your approval, click **Join LAN Game** once more.
2. Your device will be recognized immediately.
3. The farmhand selection screen will appear:
   * **New playtesters:** Select any available unclaimed slot. It will become permanently and exclusively bound to your device.
   * **Slots belonging to others:** Will display a `[Belongs to another player]` tag and remain locked against accidental selection.
4. Enter the farm and enjoy the persistent world!

---

## 4. What Are We Testing? Key Features

During your play sessions, we invite you to explore and share feedback on the following core mechanics:

### 🕰️ Real-Time Clock & NightHold Dawn
* The in-game clock advances 1:1 in lockstep with real time (1 real day = 1 in-game day).
* **No forced midnight collapses:** Between 01:50 AM and 06:00 AM real time, the clock freezes at `2550`, allowing you to play at night comfortably without energy exhaustion or pass-outs.
* The official calendar date advance occurs automatically at **02:00 AM**, preceded by an audio cue and a 10-minute HUD warning broadcast.

### 🏡 Private Residences & Central Plaza
* The primary map (`Farm`) is the **Central Plaza**. From there and across the bridges to the east, you will find portals to the 16 private residences.
* Each farmer has their own dedicated farm map (choose among 8 official vanilla layouts) and private cave (mushrooms or fruit bats).
* Use the `/residencia` command or interact with the mailbox/post outside your portal to set your access policy (`Locked`, `Friends only`, or `Public`).

### 🍎 Satiety & Asynchronous Rest
* You can sleep in your bed and disconnect or exit to title at any time without pausing the server or kicking others.
* Eating cooked food or crops replenishes your satiety bar. While "Well-Fed", sitting down on chairs, benches, or on the ground (`GroundSitModule`) restores energy over time.
* Visiting the Railroad Spa grants health and energy regeneration over 2 real hours.

### 🏪 Community Market
* Check the neighborhood bulletin board to browse listings from other farmers or list your own surplus crops, minerals, and artisan goods. Profits are collected even if buyers purchase your items while you are offline.

### 💬 Multi-Channel & Proximity Chat
* `/l <message>`: Local proximity chat (heard only by neighbors in the same area or nearby).
* `/g <message>`: Global server-wide chat.
* `/w <player> <message>`: Direct private whisper.

---

## 5. Frequently Asked Questions & Troubleshooting

#### What happens if my public IP address changes or my router restarts?
Nothing at all. Your identity is tied to the local cryptographic key stored on your device, not your IP address. You can reconnect as usual from anywhere.

#### I switched computers or reinstalled my operating system. Can I recover my character?
Yes. Your private key is stored in `Mods/Pelican4Ever/data/device_key.json`. When switching computers, simply copy this file to your new installation. If you lost the file, contact the host on Discord to link your new machine's fingerprint to your existing character.

#### The game shows "Incompatible version" or "P4E\\x02 required":
Ensure you have extracted the `0.2.0` release of `Pelican4Ever` into your `Mods` folder and launched Stardew Valley through SMAPI.

#### Where should I report bugs or suggestions?
On our official Discord server in the `#bugs-feedback` channel or via direct message to the development team. Thank you for helping playtest Pelican 4Ever!
