# Registros de decisiones de arquitectura (ADR)

> **Fuente única de verdad (Brain):** los ficheros en **español** (`.md` sin sufijo de idioma) son la referencia normativa para agentes y desarrollo; las traducciones `*.en.md` y `*.zh-CN.md` son vistas derivadas para lectura humana.

Un ADR es una **decisión** y su motivo: no se reescribe cuando el contexto cambia, se sustituye
por otro ADR. La tabla es el índice; el detalle está en cada fichero.

| ADR | Decisión | Estado | Fecha |
| --- | --- | --- | --- |
| [0001](0001-native-daily-rollover.md) · [EN](0001-native-daily-rollover.en.md) · [中文](0001-native-daily-rollover.zh-CN.md) | Rollover nativo con fecha a las **02:00** y amanecer a las **06:00** | Aceptado | 2026-11-02 |
| [0002](0002-realtime-clock-and-epoch-calendar.md) · [EN](0002-realtime-clock-and-epoch-calendar.en.md) · [中文](0002-realtime-clock-and-epoch-calendar.zh-CN.md) | Reloj 1:1 (`0600–2550`) con `NightHold` y calendario derivado de `CalendarEpoch` | Aceptado | 2026-10-29 |
| [0003](0003-declarative-farm-slots.md) · [EN](0003-declarative-farm-slots.en.md) · [中文](0003-declarative-farm-slots.zh-CN.md) | Huecos de granja declarativos en `Data/Locations` (plantilla estándar) | Aceptado (ampliado por 0005) | 2026-10-29 |
| [0004](0004-host-authority-client-mod-and-satiety.md) · [EN](0004-host-authority-client-mod-and-satiety.en.md) · [中文](0004-host-authority-client-mod-and-satiety.zh-CN.md) | Autoridad en el host, mod obligatorio en clientes y módulos `IModule` | Aceptado; punto 1 ampliado por 0012 y punto 3 sustituido por 0006 | 2026-10-29 |
| [0005](0005-host-plaza-and-bridge-maps.md) · [EN](0005-host-plaza-and-bridge-maps.en.md) · [中文](0005-host-plaza-and-bridge-maps.zh-CN.md) | Plaza Central en la granja del host y mapas puente declarativos | Aceptado | 2026-11-03 |
| [0006](0006-session-energy-satiety-wellfed-spa.md) · [EN](0006-session-energy-satiety-wellfed-spa.en.md) · [中文](0006-session-energy-satiety-wellfed-spa.zh-CN.md) | Energía por sesiones: saciedad con vaciado continuo, «Bien comido» sentado y buff del spa | Aceptado (sustituye el punto 3 de 0004) | 2026-11-06 |
| [0007](0007-dispositivo-identidad-y-acceso-seguro.md) · [EN](0007-dispositivo-identidad-y-acceso-seguro.en.md) · [中文](0007-dispositivo-identidad-y-acceso-seguro.zh-CN.md) | Identidad asimétrica por dispositivo (ECDSA P-256) y desafío `P4E\x02` en Lidgren | Aceptado | 2026-11-11 |
| [0008](0008-skill-economy-rebalance.md) · [EN](0008-skill-economy-rebalance.en.md) · [中文](0008-skill-economy-rebalance.zh-CN.md) | Rebalanceo para el 1:1: curva diaria de experiencia, pesca más lenta, menos escaleras y coste de energía en la Caverna Calavera | Propuesto | 2026-11-14 |
| [0009](0009-annex-vanilla-composition.md) | Generación de anexos con el flujo «Mapas Puente» (`MapStudio` + recorte canónico + clasificadores verificados) | Aceptado | 2026-11-08 |
| [0010](0010-scalable-residences-admission-queue.md) | Residencias escalables (16/32/64) con 16 simultáneos, cola de admisión, expulsión por inactividad y gates al este de `Town` | Aceptado (`64` experimental) | 2026-11-15 |
| `0011` *(reservado)* | Concurrencia ampliada v0.6 (`0011-extended-concurrency-interest-management.md` en rama `feat/v0.6-extended-concurrency`) | Propuesto (otra rama) | 2026-11-15 |
| [0012](0012-client-version-compatibility-and-branching.md) | Mod único con suelo `MinCompatibleClientVersion` desacoplado, regla normativa de subida y flujo de ramas cortas sobre `main` | Aceptado (amplía el punto 1 de 0004) | 2026-11-23 |
| [0013](0013-fail-closed-health-and-backup-contract.md) · [EN](0013-fail-closed-health-and-backup-contract.en.md) · [中文](0013-fail-closed-health-and-backup-contract.zh-CN.md) | Arranque *fail-closed*, salud del host (`/health` + watchdog) y contrato de copias con Putre *(antes ADR-0010)* | Propuesto | 2026-11-16 |

## Cómo leerlos

- **Contexto:** por qué la decisión era necesaria.
- **Decisión:** qué se decidió, en sus términos exactos.
- **Consecuencias:** qué queda atado a esa decisión.

## Reglas

1. Un ADR no se edita para cambiar la decisión: se marca como *sustituido* y se escribe otro.
2. Las cifras citadas (puertos, horas, topes) son **normativas**: el código debe coincidir con
   ellas.
3. Cada decisión nueva que afecte al formato de datos, al protocolo, al calendario o al modelo de
   autoridad merece un ADR.
