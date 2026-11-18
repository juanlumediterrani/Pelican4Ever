# ADR-0010 · Arranque *fail-closed*, salud del host y contrato de copias con Putre

🌐 **Idiomas:** Español | [English](0010-fail-closed-health-and-backup-contract.en.md) | [中文](0010-fail-closed-health-and-backup-contract.zh-CN.md)

- **Estado:** Propuesto
- **Fecha:** 2026-11-16
- **Ámbito:** `Pelican 4Ever` (mod del host) y su repositorio hermano `Putre` (`putre-ops` / Hermes), que opera la VM de producción.
- **Relacionados:** [ADR-0001](0001-native-daily-rollover.md) (rollover y copia SHA-256), [ADR-0004](0004-host-authority-client-mod-and-satiety.md) (autoridad en el host), [ADR-0007](0007-dispositivo-identidad-y-acceso-seguro.md) (identidad por dispositivo). Backlog: Épica 25 en [backlog.md](../../backlog.md) y backlog de Putre (`Putre/backlog.md`).

## Contexto

Una auditoría del 2026-11-16 encontró cuatro fallos que no hacen ruido en un servidor 24/7:

1. **Los parches Harmony fallan en abierto.** Hay unos 84 parches. Los tres asistentes que los aplican (`ClientSecurityModule.TryPatch`, `FarmSlotModule.PatchSafe` y `MailOrderModule.PatchSafe`) registran el error y siguen. Si `GameServer.checkFarmhandRequest` no se parchea, el servidor acepta farmhands sin el desafío de dispositivo de ADR-0007. Lo mismo pasa con `VisitorGuardModule`, la propiedad de los paquetes y el aislamiento de parcelas. Una actualización de Stardew (1.6.16+) o de SMAPI basta para provocarlo, y el `manifest.json` no fija versión del juego.
2. **Un cuelgue no se detecta.** Las unidades systemd usan `Restart=always/on-failure`, que solo actúa si el proceso termina. Si el hilo del juego se bloquea (bucle, interbloqueo o un guardado que no acaba), el proceso sigue vivo y nadie se entera. Putre solo comprueba la salud dentro de la ventana de mantenimiento de las 01:50.
3. **Las copias no tienen contrato.** `SaveBackupService` verifica SHA-256 al copiar, pero no deja manifiesto. `BackupRetentionDays = 0` (sin rotación) es el valor por defecto. `backup.saves_to_s3` existe en Putre, pero no está programado. `saves.restore` devuelve `unavailable` hasta acordar el formato con Pelican.
4. **Nunca se ha medido la escala.** El diseño apunta a 8–16 jugadores en una VM de 2 vCPU con render por CPU, y las pruebas en vivo usan como mucho Host + 2 farmhands.

## Decisión

### 1. Criticidad de parches y arranque *fail-closed*

Cada parche Harmony se declara en un registro único (`PatchRegistry`) con una criticidad:

| Criticidad | Ejemplos | Si falla |
| --- | --- | --- |
| `Security` | `checkFarmhandRequest`, `sendPlayerIntroduction`, `FarmhandSlot`, `VisitorGuardModule`, apertura de paquetes de `MailOrderModule`, `ClientVersionGuardModule` | **Bloqueo** |
| `Integrity` | Rollover nativo, `SaveGame`, checkpoints, reloj 1:1/`NightHold`, `findStructure`, persistencia de `ServerWorldState` | **Bloqueo** |
| `Experience` | Minimapas, cosméticos, NITV, chat, menús | Arranque **degradado** con aviso |

La decisión de si se bloquea es lógica pura en Core (`PatchHealthPolicy.Decide`) y tiene tests. En **bloqueo** (`Lockdown`):

- **No se carga la partida ni se abre el servidor multijugador.** No depende de ningún parche: lo decide `ModEntry` antes de `AutoHostSaveName`. Así, un fallo del parche de autenticación no deja una puerta abierta.
- La API de host sigue respondiendo para que Putre lo vea y avise.
- Solo se sale del bloqueo corrigiendo el fallo o con una excepción explícita del dueño: `LockdownOverride` lista los parches aceptados por nombre, y la excepción se registra como suceso `host.lockdown.overridden`. No existe un interruptor global de «arrancar igualmente».

### 2. Lista de versiones verificadas

`ServerConfig.VerifiedGameVersions` (por defecto `["1.6.15"]`) y `MinimumSmapiVersion` (por defecto `4.1.0`). Una versión del juego fuera de la lista provoca **bloqueo** con motivo `UnverifiedGameVersion`, salvo que `AllowUnverifiedGameVersion = true`. Esa opción existe para el laboratorio y no debe activarse en producción. En la VM, el juego se instala con `steamcmd` en una versión fija, nunca con actualización automática. Putre comprueba la lista antes de `release.apply` y de `pelican.runtime_update`.

### 3. Salud observable fuera del hilo del juego

- `GET /api/v1/host/health` se sirve desde el hilo HTTP con una instantánea atómica. **No pasa por `MainThreadActionQueue`**, así que responde aunque el juego esté colgado. Campos normativos: `Status` (`Ok` | `Degraded` | `Lockdown` | `Starting`), `LastTickUtc`, `SecondsSinceLastTick`, `SaveLoaded`, `Lockdown.Reasons[]`, `Patches.Total`, `Patches.Failed[]` (nombre y criticidad), `GameVersion`, `SmapiVersion`, `ModVersion` y `UptimeSeconds`.
- Sucesos nuevos en `HostEventLog`: `host.lockdown`, `host.degraded` y `host.lockdown.overridden`.
- **Watchdog de systemd:** si existe `NOTIFY_SOCKET`, un hilo de fondo envía `WATCHDOG=1` cada 15 s, **solo si** `SecondsSinceLastTick ≤ HangThresholdSeconds` (120 s por defecto, por encima del guardado más largo medido). La unidad declara `WatchdogSec=180` y `NotifyAccess=all`. Si el juego se cuelga, systemd mata el proceso y lo relanza con `Restart=always`. Sin `NOTIFY_SOCKET` (laboratorio o Docker), el hilo no hace nada.

### 4. Contrato de copias y restauración

- Cada copia (diaria o checkpoint) incluye `p4e-backup.json` con:
  - `FormatVersion = 1`;
  - `Kind` (`Daily` | `Checkpoint` | `PreRestore`);
  - `SaveFolderName`, `TotalGameDays`, `GameDate`, `CreatedUtc`, `ModVersion` y `GameVersion`;
  - `Files[]` con la ruta relativa, los bytes y el `Sha256` del contenido sin comprimir;
  - `Compressed`.
  
  `ServerWorldState` viaja dentro de la partida (`WriteSaveData`), así que la copia es una foto coherente. Quedan **fuera**: `config.json`, que gestionan Putre e IaC, y la telemetría de `data/`.
- **Retención local por defecto:** `BackupRetentionDays = 14` y `CheckpointRetention = 12`. La historia completa vive fuera del servidor, en S3. Putre la sincroniza con `aws s3 sync` sin `--delete` después de cada `backup.created`, y la caducidad la decide la política de ciclo de vida del bucket en IaC, nunca Putre (borrar copias es nivel 3 en su contrato).
- **Restaurar** es siempre `saves.restore` de Putre (nivel 2, con aprobación del dueño) y sigue este orden:
  1. Parar el juego.
  2. Verificar el manifiesto y los SHA-256.
  3. Mover la partida actual a `Pelican4Ever_Backups/pre-restore/<UTC>/` (nunca se borra).
  4. Copiar la partida restaurada.
  5. Arrancar.
  6. Exigir `health.Status == Ok`, `SaveLoaded` y `TotalGameDays == manifest.TotalGameDays` (o el día que dé `CatchUpPlan`).
  
  Pelican no restaura en caliente.

### 5. Escala medida antes de prometerla

`FarmSlotCount > 8` no se declara soportado en producción hasta que el banco de carga de agentes (backlog de pruebas vivas) mida, con 8, 12 y 16 farmhands sintéticos en la VM de referencia: el tamaño de la partida, la duración del guardado del rollover, `LastCheckpointLongestTickMs`, el ancho de banda por jugador y la densidad de criaturas de NITV (`locationCap × numberOfPlayers`). Las cifras resultantes se añaden a este ADR al aceptarlo.

## Reparto entre repositorios

| Pieza | `Pelican 4Ever` | `Putre` |
| --- | --- | --- |
| Registro de parches, política y bloqueo | Implementa | Lee `health` y avisa |
| Versiones verificadas | Comprueba al arrancar | Comprueba antes de actualizar; fija la versión de `steamcmd` en la VM |
| `/health` y sucesos | Expone | Sondea cada minuto; avisa solo de incidencias y recuperaciones |
| Watchdog systemd | Envía `WATCHDOG=1` | Declara `WatchdogSec`/`NotifyAccess` en la unidad de la VM |
| Manifiesto de copia | Escribe y valida | Verifica, sincroniza a S3 y restaura |
| Banco de carga | Proporciona farmhands sintéticos y métricas | Proporciona la VM de referencia y guarda el informe |

## Consecuencias

- **A favor:** una actualización del juego o un parche roto deja el servidor cerrado y visible, no abierto en silencio. Un cuelgue se convierte en un reinicio automático y en un aviso. La restauración pasa a ser un procedimiento verificable y Putre puede activar `saves.restore`.
- **En contra:** hay más indisponibilidad aparente. Tras una actualización del juego, el servidor no abre hasta que se verifique la versión. Un `HangThresholdSeconds` mal elegido puede matar un guardado largo, por eso se calibra con el banco de carga. Además, cada parche nuevo tiene que declarar su criticidad.
- **Cifras normativas** (regla 2 del índice): `WatchdogSec=180`, notificación cada 15 s, `HangThresholdSeconds=120`, `BackupRetentionDays=14`, `CheckpointRetention=12` y `VerifiedGameVersions=["1.6.15"]`.
