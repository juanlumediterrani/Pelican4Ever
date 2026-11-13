# Pelican 4Ever documentation

Index of the living documentation of the project. Whatever does not appear here (or is in
[`archive/`](archive/)) is historical: reports from a specific date, plans for a closed epic, or
procedures that have already been replaced by these guides.

## Deployment guides

Each guide exists in **Spanish** (original), **English** (`*.en.md`) and **Simplified Chinese**
(`*.zh-CN.md`). File names, commands, configuration keys and paths stay identical in the three
languages.

| Guide | Languages | Who it is for | What it covers |
| --- | --- | --- | --- |
| Hosting on a VPS | [ES](despliegue/host-vps.md) · [EN](despliegue/host-vps.en.md) · [中文](despliegue/host-vps.zh-CN.md) | Host | 24/7 server on a Linux VPS: installation, `Xvfb`, systemd, firewall, backups, updates and diagnostics |
| Hosting locally | [ES](despliegue/host-local.md) · [EN](despliegue/host-local.en.md) · [中文](despliegue/host-local.zh-CN.md) | Host | Host save on your own machine: configuration, player registrations |
| Client mode | [ES](despliegue/cliente.md) · [EN](despliegue/cliente.en.md) · [中文](despliegue/cliente.zh-CN.md) | Player | Install the mod, connect to a server, pairing by fingerprint, chat commands |
| Preview server | [ES](servidor-preview.md) · [EN](servidor-preview.en.md) · [中文](servidor-preview.zh-CN.md) | Tester | Join the **official test server**, with its modpack requirements and device approval |

## Reference

Each reference document exists in **Spanish** (original), **English** (`*.en.md`) and **Simplified
Chinese** (`*.zh-CN.md`).

| Document | Languages | Content |
| --- | --- | --- |
| Host API | [ES](host-api.md) · [EN](host-api.en.md) · [中文](host-api.zh-CN.md) | Host REST API v1: routes, authentication, allowed remote commands and events |
| ADR | [index](adr/) · [EN](adr/README.en.md) · [中文](adr/README.zh-CN.md) | Architecture decision records, each one in the three languages |
| Architecture | [ES](../architecture.md) · [EN](../architecture.en.md) · [中文](../architecture.zh-CN.md) | System architecture |
| Game design | [ES](../design.md) · [EN](../design.en.md) · [中文](../design.zh-CN.md) | Game design |
| Backlog | [ES](../backlog.md) | Technical roadmap: epics, tasks and their resolution (**Spanish only**: it is a 1,700-line historical log) |
| Host cosmetics | [ES](../agents/README.md) | Dragon sprite and pixel art utilities (**Spanish only**) |

## Web panel (not in `main` or `dev`)

The web dashboard is **not** in `main` or `dev`: it lives in the `feat/web-panel` branch, where it
has also been refactored to be served from `assets/dashboard.html` instead of a constant embedded
in the DLL. The REST API (`/api/v1/host/*` and `/api/*`) does not depend on the panel and is
unchanged.

To bring the panel back:

```bash
git checkout feat/web-panel -- Pelican4Ever/assets/dashboard.html \
    Pelican4Ever.Core/Admin/AdminApiLogic.cs Pelican4Ever/Modules/AdminApiModule.cs \
    Pelican4Ever.Tests/AdminApiLogicTests.cs scripts/
```

## House rules

- The game is **Stardew Valley 1.6.15** with **SMAPI 4.1+**.
- The mod is called **Pelican 4Ever** (`Pelican4Ever.SVRTMP`), current version **0.2.0**.
- The server exposes **a single public port**: UDP `24642`. The host API (`8085`) stays on
  loopback.
- `config.json` is **validated at startup**: if anything does not add up, the mod does not
  register. There is no degraded mode.
- The host is always a local player: **there is no native dedicated server mode**. The
  `DedicatedServerMode` option is not used in the current code.
- The modpack versions the server requires are **exact** (not *major.minor*). The mod's own version
  is compared by *major.minor*.
- Each player's identity is their **device key** (ECDSA P-256), not their IP: changing IP does not
  force a new pairing.

## History

The archived documents are in [`archive/`](archive/) with a note at the beginning of each one
explaining its status. Do not delete them without reviewing: some contain the technical detail
(data formats, bug inventories) that has not yet been transplanted into the living guides.
