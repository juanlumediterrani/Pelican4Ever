# Pelican 4Ever (`SV-RTMP`)

🌐 **Languages:** English | [Español](README.es.md)

A mod for **Stardew Valley 1.6.15** (SMAPI 4.1+) that transforms cooperative multiplayer into a **persistent, real-time world (1:1 scale)** tailored for 24/7 dedicated servers supporting **up to 16 players**.

Features customizable private residences accessible via the Central Plaza and a deterministic bridge network, 8 authentic farm types with canonical miniature annexes (40×32), a persistent community economy, asynchronous rest without forced collapses, DevOps remote administration via REST API & web dashboard, and native autonomous agent support.

---

## 🌟 Key Features

### 1. 1:1 Real-Time Clock & Persistent 24/7 Daily Cycle
- **Real-Time Progression:** The in-game clock advances in lockstep with real time (`600,000 ms` per 10 game minutes; 1 real day equals 1 game day).
- **Continuous Dawn (*NightHold*):** Between 01:50 AM and 06:00 AM real time, the clock freezes at `2550` (never reaching the forced `2600` collapse), allowing nocturnal gameplay without pass-outs. Nighttime ambience is simulated smoothly, and craftables/machines process materials continuously.
- **Decoupled Date Rollover:** The calendar date advances at `02:00 AM`. Before advancing, an automated backup with SHA-256 integrity verification is generated, connected peers are synchronized, and players receive a 10-minute warning broadcast.
- **Natural Daylight and Schedules:** Daylight illumination and Pelican Town NPC schedules still trigger at `06:00 AM`; the 02:00 AM date rollover does not prematurely skip morning schedules.
- **Epoch-Based Perpetual Calendar:** Seasons last 28 real days (exactly 4 weeks), ensuring that Day 1 is always a real-world Monday and Day 7 is always a Sunday.

### 2. Up to 16 Private Residences & Thematic 40×32 Annexes
- **Portal & Bridge Network:** 16 private residences distributed across the Central Plaza (`Farm`) and a deterministic bridge chain (`Pelican4Ever_Bridge_1..3`).
- **8 Authentic Farm Types:** Choose among Standard, Riverland, Forest, Hilltop, Wilderness, Four Corners, Beach, and Meadowlands (Ranching), featuring active foraging, fishing pools, quarry ore respawns, and nocturnal monster spawning tailored to each biotope.
- **Individual Private Caves:** Each resident independently chooses whether their private cave produces Fruit Bats or Mushrooms.
- **Canonical Miniature Annexes (40×32):** Expandable thematic farmland expansions constructed 100% using authentic vanilla tilesets (ADR 0007 / Epic 9.1), featuring single-entrance variants, natural cliff/water perimeter barriers, and automated debris sanitization on save reload (MapVersion 3).
- **Access Policies & Anti-Griefing:** Granular per-residence security (`Locked`, `Friends`, `Public`) with dynamic friend lists and visitor building permission toggles via the `/residencia` command.
- **Dynamic Network Culling:** Vacant residences and annexes consume zero client bandwidth or network overhead.

### 3. Asynchronous Sleep & Energy Balance
- **Individual Rest & Disconnect:** Each farmer can sleep in their bed and exit to the title screen or disconnect individually without pausing the world or kicking others.
- **Satiety System & «Well-Fed» Buff:** Satiety depletes gradually over 4 real hours. Reaching 1/4 satiety from eating any food or cooked meal activates the *Well-Fed* buff, enabling passive energy regeneration while sitting on chairs or the ground (`GroundSitModule`).
- **Daily Spa Regeneration:** A relaxing visit to the Railroad Spa restores up to 120 Energy and Health over 2 real hours.
- **Equitable Mining Shifts:** Mine floor states and ores regenerate every 12 real hours (at 06:00 and 18:00) to ensure fair resource distribution across player time zones.

### 4. Community Life & Social Immersion
- **Neighborhood Mailboxes:** Send and receive neighborly letters right outside each farm portal with synchronized visual indicators.
- **Multi-channel & Spatial Proximity Chat:** Global channel (`/g`), local proximity chat with spatial distance volume attenuation (`/l`), and private direct whispers (`/w`).
- **Asynchronous Community Market:** Community bulletin board for buying and selling surplus goods, crops, and artisan goods between online and offline farmers, backed by secure escrow and delivery confirmation.
- **Decoupled Live Festivals:** Seamless entry and exit without camera cuts or clock freezes; multi-heat competitive minigames with official prizes awarded at 23:50, plus live community ceremonies (Luau, Dance of the Moonlight Jellies).

### 5. DevOps Panel, Host REST API v1 & Headless Mode
- **Integrated Web Dashboard (SPA):** Lightweight web interface served on port `8085` displaying real-time server metrics, an interactive world map, connected players, and residence occupancy.
- **Versioned REST API (`/api/v1/host/`):** Secure local HTTP endpoints to manage the clock, broadcast announcements, manage players, coordinate festival heats, and poll incremental event streams (`events?since=N`).
- **Remote Console Commands:** Execute whitelisted administrative console commands remotely and securely via the REST API.
- **Scheduled Safe Restart (`p4e_safe_restart`):** Calculates unsaved player progress at risk and orchestrates graceful server restarts with guaranteed persistence.
- **Headless Server Mode:** Dedicated host servers can run on Linux without graphical rendering, drastically reducing CPU and RAM footprint.

### 6. Autonomous Agent Support & Cosmetics
- **`AgentHost` Mode:** Tailored for autonomous AI hosts (such as Stardew Voyager or Putre), enabling automated sleep logging without closing the host network session.
- **Dr4co Dragon Cosmetic Pack (Fashion Sense 7.5.0+):** Full custom body with 4-directional walk cycles, 16×32 alignment with vanilla shadow, and claw tool offset correction (`AgentCosmeticsModule` / `AgentToolOffset`).
- **`p4e_dr4co_sprite` Command:** Instantly toggle dragon appearances from console or chat without requiring the in-game Fashion Sense mirror.

### 7. Modpack Integrity Verification
- **Strict Client Verification (`RequiredClientMods`):** The server validates connected clients during the SMAPI initial handshake, ensuring that the full modpack is present with matching versions to prevent missing items, desynchronization, and visual errors.

---

## 📚 Technical Documentation

* [Game Design (`design.md`)](design.md)
* [Technical Architecture (`architecture.md`)](architecture.md)
* [Iterations & Roadmap Backlog (`backlog.md`)](backlog.md)
* [Host REST API Specification (`docs/host-api.md`)](docs/host-api.md)
* [Persistent Modpack Guide (`docs/modpack.md`)](docs/modpack.md)
* [Unattended Persistent Server Guide (`docs/servidor-persistente.md`)](docs/servidor-persistente.md)
* [Production Architecture with Hermes (`docs/arquitectura-produccion-hermes.md`)](docs/arquitectura-produccion-hermes.md)
* [Architecture Decision Records (`docs/adr/`)](docs/adr/)
* [Agent Sprite & Cosmetics Guide (`agents/README.md`)](agents/README.md)
* [Pixel Art Generation Pipeline (`docs/pixel_art_pipeline.md`)](docs/pixel_art_pipeline.md)

---

## 🏗️ Solution Structure

```text
Pelican4Ever.sln
├── Pelican4Ever.Core/      # Pure domain logic (.NET 6, zero dependencies on SMAPI or MonoGame)
│   ├── Admin/              # Host API, main-thread action queue, events, and safe restart
│   ├── Chat/               # Multi-channel routing, command parsing, and spatial proximity
│   ├── Config/             # ServerConfig with strict deterministic validation
│   ├── Economy/            # Satiety, rest, spa, multipliers, and community market escrow
│   ├── Festivals/          # Registration heats, ceremonies, scoring, and rewards
│   ├── Networking/         # Client modpack enforcement and network culling
│   ├── Players/            # PlayerRecord, SleepState derivation, SleepPolicy, and agent tool offsets
│   ├── Server/             # ServerWorldState and server lifecycle management
│   ├── Time/               # GameClock, ServerCalendar, ServerTime, and CatchUpPlan
│   └── World/              # Residences, portals, thematic annexes, farm types, and private caves
├── Pelican4Ever/           # SMAPI Mod (.NET 6, compiled assembly with Harmony patches)
│   ├── Framework/          # ModContext, IModule, spatial routing, and map cloning helpers
│   └── Modules/            # 31 decoupled SMAPI modules with defensive registration
└── Pelican4Ever.Tests/     # Test suite with 1,235 unit and contract tests in xUnit (.NET 8)
```

---

## ⚙️ Compilation & Testing

### Prerequisites
* **.NET SDK 8.0**
* **Stardew Valley 1.6.15** and **SMAPI 4.1+**

### Build Steps
1. If your Steam or Stardew Valley installation resides in a custom path, copy `local.props.example` to `local.props` and configure `<GamePath>`:
   ```bash
   cp local.props.example local.props
   ```
2. Build the complete solution. The package compiles with `TreatWarningsAsErrors = true`, deploying the mod directly to your `Mods/Pelican4Ever` directory and creating the release ZIP:
   ```bash
   dotnet build Pelican4Ever.sln
   ```
3. Run the comprehensive unit and integration test suite:
   ```bash
   dotnet test Pelican4Ever.Tests
   ```

---

## 🐳 Docker Compose Deployment (24/7 Dedicated Server)

The repository provides out-of-the-box support for unattended server hosting using [docker-compose.yml](docker-compose.yml) based on `JunimoServer`:

1. Create your environment configuration from the template:
   ```bash
   cp .env.example .env
   # Edit .env with your Steam credentials for the dedicated host
   ```
2. Configure your secure administration token in `config.json` using [config.example.json](config.example.json):
   ```bash
   cp config.example.json config.json
   # Be sure to replace AdminApiToken with your own secure secret key
   ```
3. Launch the container in detached mode:
   ```bash
   docker compose up -d
   ```
4. The server will expose the configured ports:
   - `24642/udp`: Native multiplayer game port (Lidgren/Galaxy).
   - `8085`: REST API & Web Administration Dashboard (`http://localhost:8085/`).
   - `5900`: Optional VNC for remote visual inspection.
