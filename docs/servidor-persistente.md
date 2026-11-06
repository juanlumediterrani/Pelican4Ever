# Servidor persistente desatendido

Cómo dejar una partida de Pelican 4Ever siempre disponible en una máquina con sesión gráfica (el juego
necesita pantalla), sin nadie que la cargue a mano. Es opcional: un anfitrión humano puede seguir
cargando la partida desde «Cooperativo → Anfitrión» como siempre.

## Piezas

| Pieza | Qué hace |
| --- | --- |
| `AutoHostSaveName` (`config.json`) | Al llegar a la pantalla de título, el host carga como anfitrión de cooperativo esa partida, por el nombre exacto de su carpeta en `Saves/`. Si no existe, no carga otra: lo registra y se queda en el título. |
| `RestartExitsProcess` (`config.json`) | El reinicio controlado (`p4e_safe_restart`, `POST /api/v1/host/restart`) cierra el juego al llegar al título, para que systemd lo relance con el mod actualizado. Sin esta opción, vuelve al título y la carga automática reabre la partida en el mismo proceso. Requiere `AutoHostSaveName`. |
| `MaintenanceWindowMinutes` (`config.json`) | Modo mantenimiento: desde el cambio de fecha (02:00) y durante estos minutos (25 por defecto, 0 lo desactiva, máx. 180) el host rechaza a cualquier jugador que intente entrar (`MaintenanceLockModule`, aviso en el log y expulsión al siguiente tick). Cubre guardado, copia, avance de día y una actualización del runtime sin reconexiones. Se aplica sin tocar `config.json`: el valor por defecto rige si falta la clave. |
| `AgentHost` (`config.json`) | Si el host se acuesta, registra el descanso sin salir al título. Imprescindible si el host es un agente. |
| `deploy/systemd/pelican4ever-host.service` | Lanza SMAPI y lo relanza tras una caída o un reinicio controlado (30 s de espera, como mucho 5 veces en 15 min). No convive con `stardew-voyager-live.service`. |

El tiempo no necesita supervisión: el reloj 1:1, la madrugada congelada y los horarios de luz/Pelican empiezan a las 06:00. La fecha cambia a las 02:00: aviso a las 01:50, guardado nativo y backup host SHA-256 antes del cierre ordenado de peers; si el backup o la desconexión no se verifican, el día no avanza. Los backups quedan archivados en `Pelican4Ever_Backups/`.

## Instalación

1. Crea la partida persistente desde «Cooperativo → Anfitrión → Nueva granja» y anota el nombre de su
   carpeta en `~/.config/StardewValley/Saves/` (por ejemplo `Pelican4Ever_452974881`).
2. En `Mods/Pelican4Ever/config.json`:
   ```json
   "AgentHost": true,
   "AutoHostSaveName": "Pelican4Ever_452974881",
   "RestartExitsProcess": true
   ```
3. Instala y activa el servicio de usuario:
   ```bash
   install -Dm644 deploy/systemd/pelican4ever-host.service ~/.config/systemd/user/pelican4ever-host.service
   systemctl --user daemon-reload
   systemctl --user import-environment DISPLAY XAUTHORITY
   systemctl --user enable --now pelican4ever-host.service
   ```
   Si el juego no está en la ruta de Steam por defecto, pon `STARDEW_DIR=/ruta/al/juego` en
   `~/.config/pelican4ever/host.env`.
4. Comprueba que el servidor está arriba: `journalctl --user -u pelican4ever-host -f` muestra
   `[AutoHost] Cargando ... como anfitrión` y la API responde en `http://127.0.0.1:8085/api/v1/host/clock`.

## Supervisión externa (opcional)

systemd relanza las caídas, pero no una unidad detenida o que agotó su límite de arranques. Un cron
puede cubrirlo; Stardew Voyager trae uno, `voyager --live host ensure`, que solo arranca la unidad si
está parada y se niega si el servidor sirve otra partida que la autorizada (guía en
`docs/pelican-servidor-persistente.md` de Stardew Voyager). Pelican 4Ever no depende de él.

## Actualizar el mod sin perder la partida

1. Compila y despliega el mod nuevo en `Mods/Pelican4Ever` (el proceso en marcha no lo recarga).
2. Programa un reinicio controlado: `POST /api/v1/host/restart` con `"immediate": false`. Espera al
   siguiente guardado (cambio de día), avisa a los jugadores y cierra el juego; systemd lo relanza con el
   mod nuevo y la carga automática reabre la partida.
3. El estado del servidor (`ServerWorldState`) migra su esquema al cargar; los clientes con otra versión
   del mod son expulsados hasta que actualicen (`RequireClientMod`).

## Copias de la partida y checkpoints intradía

- **Copias diarias:** las del rollover, en `Pelican4Ever_Backups/<partida>_<fecha UTC>/`, sin comprimir. No se borran salvo que `BackupRetentionDays` sea mayor que 0; entonces se conservan las de esos días y siempre la más reciente.
- **Checkpoints intradía** (Épica 11 del backlog, **desactivados por defecto**): con `CheckpointIntervalMinutes` mayor que 0, el host guarda la partida cada ese número de minutos sin cambiar de día ni desconectar a nadie. `p4e_checkpoint_now` lanza uno a mano aunque estén desactivados. Cada uno deja una copia en `Pelican4Ever_Backups/checkpoints/<partida>_<fecha UTC>/`, con cada archivo comprimido en `.gz` si `CheckpointCompress` está activo, y se conservan los últimos `CheckpointRetention`. No se hacen durante festivales o eventos, ni en los 10 minutos previos al rollover. Con farmhands conectados solo se hacen si `CheckpointCloneFarmhands` está activo. Cada checkpoint congela el juego mientras guarda; si tarda más de `CheckpointMaxDurationMs`, el intervalo se duplica hasta el día siguiente. Quedan en el registro de sucesos como `checkpoint.created`, `checkpoint.skipped` y `checkpoint.failed`.

**Restaurar una copia** (diaria o checkpoint), con el servicio parado:

1. Aparta la partida actual: `mv Saves/<partida> Saves/<partida>.antes-de-restaurar`.
2. Copia la carpeta elegida como `Saves/<partida>`. Si es de `checkpoints/` y está comprimida, descomprime sus archivos: `gunzip Saves/<partida>/*.gz`.
3. Arranca el servicio. Un checkpoint vuelve a abrir el mismo día desde las 06:00 del juego, con lo hecho hasta la hora del checkpoint. El log dice «inicio causado por carga de partida» y no se repiten penalizaciones.

## Seguridad y posesión de personajes (Anti-usurpación)

En conexiones por IP directa, Stardew Valley nativo no valida usuarios (`userID = ""`). Pelican 4Ever implementa un sistema automático de tokens por cliente/equipo:
- Cada instalación de cliente genera un token único local guardado en `data/client_token.json` y `GlobalData` de SMAPI.
- Al reclamar un peón (crear personaje nuevo o entrar con uno sin dueño), el Host lo asocia autoritativamente a su `PlayerRecord.OwnerToken`.
- En la pantalla de selección de peón (`FarmhandMenu`):
  - Los personajes propios aparecen ordenados al principio.
  - Los peones pertenecientes a otros jugadores aparecen atenuados (al 50% de opacidad), con el texto `[De otro jugador]` y con el clic desactivado.
- Si un cliente intenta conectar a un peón ajeno, el Host rechaza la solicitud de forma autoritativa en `GameServer.checkFarmhandRequest`.
- **Comandos de consola del anfitrión:**
  - `p4e_claim_list`: lista todos los peones, su residencia y si están reclamados (con token ofuscado).
  - `p4e_claim_reset <nombre_o_id>`: restablece la posesión de un personaje para permitir que su dueño legítimo lo reclame desde un equipo nuevo.

## Límites

- Sin Steam abierto no hay códigos de invitación; los jugadores entran por IP (puerto 24642).
- Volver al título a mano con `AutoHostSaveName` puesto reabre la partida a los 3 s: para cerrar el
  servidor, para el servicio (`systemctl --user stop pelican4ever-host`).
