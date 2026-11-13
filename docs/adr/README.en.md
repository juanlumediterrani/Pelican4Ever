# Architecture Decision Records (ADR)

An ADR is a **decision** and its reason: it is not rewritten when the context changes, it is
replaced by another ADR. The table is the index; the detail is in each file.

| ADR | Decision | Status | Date |
| --- | --- | --- | --- |
| [0001](0001-native-daily-rollover.md) · [EN](0001-native-daily-rollover.en.md) · [中文](0001-native-daily-rollover.zh-CN.md) | Native rollover with the date at **02:00** and dawn at **06:00** | Accepted | 2026-11-02 |
| [0002](0002-realtime-clock-and-epoch-calendar.md) · [EN](0002-realtime-clock-and-epoch-calendar.en.md) · [中文](0002-realtime-clock-and-epoch-calendar.zh-CN.md) | 1:1 clock (`0600–2550`) with `NightHold` and a calendar derived from `CalendarEpoch` | Accepted | 2026-10-29 |
| [0003](0003-declarative-farm-slots.md) · [EN](0003-declarative-farm-slots.en.md) · [中文](0003-declarative-farm-slots.zh-CN.md) | Declarative farm slots in `Data/Locations` (standard template) | Accepted (extended by 0005) | 2026-10-29 |
| [0004](0004-host-authority-client-mod-and-satiety.md) · [EN](0004-host-authority-client-mod-and-satiety.en.md) · [中文](0004-host-authority-client-mod-and-satiety.zh-CN.md) | Authority on the host, mandatory mod on clients and `IModule` modules | Accepted; point 3 (daily satiety budget) replaced by 0006 | 2026-10-29 |
| [0005](0005-host-plaza-and-bridge-maps.md) · [EN](0005-host-plaza-and-bridge-maps.en.md) · [中文](0005-host-plaza-and-bridge-maps.zh-CN.md) | Central Plaza on the host farm and declarative bridge maps | Accepted | 2026-11-03 |
| [0006](0006-session-energy-satiety-wellfed-spa.md) · [EN](0006-session-energy-satiety-wellfed-spa.en.md) · [中文](0006-session-energy-satiety-wellfed-spa.zh-CN.md) | Energy per session: satiety with a continuous drain, seated "Well-Fed" and the spa buff | Accepted (replaces point 3 of 0004) | 2026-11-06 |
| [0007](0007-dispositivo-identidad-y-acceso-seguro.md) · [EN](0007-dispositivo-identidad-y-acceso-seguro.en.md) · [中文](0007-dispositivo-identidad-y-acceso-seguro.zh-CN.md) | Asymmetric identity per device (ECDSA P-256) and the `P4E\x02` challenge in Lidgren | Accepted | 2026-11-11 |

## How to read them

- **Context:** why the decision was needed.
- **Decision:** what was decided, in its exact terms.
- **Consequences:** what stays tied to that decision.

## Rules

1. An ADR is not edited to change the decision: it is marked as *superseded* and another one is
   written.
2. The figures quoted (ports, hours, limits) are **normative**: the code must match them.
3. Every new decision that affects the data format, the protocol, the calendar or the authority
   model deserves an ADR.
