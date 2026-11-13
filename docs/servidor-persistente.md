# Servidor persistente desatendido

Cómo dejar una partida de Pelican 4Ever siempre disponible en una máquina con sesión gráfica (el juego
necesita pantalla), sin nadie que la cargue a mano. Es opcional: un anfitrión humano puede seguir
cargando la partida desde «Cooperativo → Anfitrión» como siempre.

## Piezas

| Pieza | Qué hace |
| --- | --- |
| `AutoHostSaveName` (`config.json`) | Al llegar a la pantalla de título, el host carga como anfitrión de cooperativo esa partida, por el nombre exacto de su carpeta en `Saves/`. Si no existe, no carga otra: lo registra y se queda en el título. |
| `RestartExitsProcess` (`config.json`) | El reinicio controlado (`p4e_safe_restart`, `POST /api/v1/host/restart`) cierra el juego al llegar al título, para que systemd lo relance con el mod actualizado. Sin esta opción, vuelve al título y la carga automática reabre la partida en el mismo proceso. Requiere `AutoHostSaveName`. |
| `RolloverLock` (módulo) | Mientras dura la secuencia diaria (guardado pre-cierre, cierre ordenado de peers, avance de día y guardado posterior) el host rechaza las conexiones nuevas (`RolloverLockModule`, aviso en el log, evento `player.rejected` y expulsión al siguiente tick). Es un bloqueo por evento, no por reloj: no hay ventana de mantenimiento fija. La actualización del runtime la gestiona el supervisor parando el servicio (`RestartExitsProcess`), que mientras tanto no acepta conexiones. |
| `HeadlessRendering` (`config.json`, `true` por defecto) | Con `AutoHostSaveName`, el host deja de dibujar con el mundo cargado: la simulación sigue a 60 Hz y cada cliente dibuja lo suyo. Sin GPU, dibujar por CPU ocupa núcleos enteros aunque nadie mire. Dibuja igualmente en el título, con la pantalla de guardado abierta (el juego la necesita) y mientras haya un préstamo de dibujado (`POST /api/v1/host/render`, `p4e_render <minutos>`, API de mod). Sin efecto si un humano hace de host. |
| `RequiredClientMods` (`config.json`) | Resto del modpack que se exige a cada cliente, en la versión exacta del servidor (ver `docs/modpack.md`). Vacío = solo Pelican 4Ever. |
| `AgentHost` (`config.json`) | Si el host se acuesta, registra el descanso sin salir al título. Imprescindible si el host es un agente. |
| `deploy/systemd/pelican4ever-host.service` | Lanza SMAPI y lo relanza tras una caída o un reinicio controlado (30 s de espera, como mucho 5 veces en 15 min). No convive con `stardew-voyager-live.service`. |

El tiempo no necesita supervisión: el reloj 1:1, la madrugada congelada y los horarios de luz/Pelican empiezan a las 06:00. La fecha cambia a las 02:00: aviso a las 01:50, guardado nativo y backup host SHA-256 antes del cierre ordenado de peers; si el backup o la desconexión no se verifican, el día no avanza. Los backups quedan archivados en `Pelican4Ever_Backups/`.

Si el log reporta `Address already in use` al cargar la partida, es el puerto UDP `24642` (lo fija el motor y solo admite una instancia alojando por máquina): otra instancia de Stardew o un proceso anterior sin liberarlo. `AutoHost` espera hasta 60 s a que se libere, loguea quién lo ocupa (`PID …`) y, si lo ocupa el propio proceso con `RestartExitsProcess: true`, cierra el proceso para que systemd lo relance limpio. Para verlo a mano: `ss -lunp | grep 24642` y `pgrep -af Stardew`.

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

## Servidor sin pantalla: CPU

En una máquina sin GPU (Xvfb y render por CPU) hay dos consumos que no aportan nada al servidor. Medido en una VM de
2 vCPU (`m7i-flex.large`) con la partida cargada y sin jugadores remotos:

| Situación | CPU del juego | CPU libre |
| --- | --- | --- |
| Por defecto | ~200 % | 0 % |
| Sin lector de consola de SMAPI | ~103 % (render ~94 %) | ~45 % |
| Sin lector de consola y con `HeadlessRendering` | ~23 % | ~87 % |

1. **Lector de consola de SMAPI.** Bajo systemd la entrada estándar es `/dev/null`: SMAPI lee, recibe fin de fichero y
   vuelve a leer sin parar (un núcleo entero). Desactívalo con `Mods/SMAPI-config.json` (no se pierde al actualizar
   SMAPI); los comandos de consola dejan de estar disponibles, usa la API de anfitrión:
   ```json
   { "ListenForConsoleInput": false }
   ```
   Sin consola, los comandos de administración de Pelican se lanzan por la API: `GET /api/v1/host/commands`
   lista los permitidos (con su riesgo) y `POST /api/v1/host/command` ejecuta uno y devuelve lo que escribe en el
   registro. Es una lista cerrada: no incluye `p4e_rollover_now`, `p4e_safe_restart`, `p4e_time`, `p4e_migrate_save`,
   `p4e_give_*` ni los de prueba.
2. **Dibujado del host.** `HeadlessRendering` (activo por defecto con `AutoHostSaveName`) deja de dibujar con el mundo
   cargado. Quien necesite ver la pantalla pide un préstamo: `POST /api/v1/host/render` o la API de mod.

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
- **Checkpoints intradía** (Épica 11 del backlog, **desactivados por defecto**): con `CheckpointIntervalMinutes` mayor que 0, el host guarda la partida cada ese número de minutos sin cambiar de día ni desconectar a nadie. `p4e_checkpoint_now` lanza uno a mano aunque estén desactivados. Cada uno deja una copia en `Pelican4Ever_Backups/checkpoints/<partida>_<fecha UTC>/`, con cada archivo comprimido en `.gz` si `CheckpointCompress` está activo, y se conservan los últimos `CheckpointRetention`. No se hacen durante festivales o eventos, en la ventana de aviso previa al rollover (10 minutos si no se configura otra), ni cuando el guardado nativo eliminaría tiendas de campaña, Junimos o personajes de WitchHut. Con farmhands conectados solo se hacen si `CheckpointCloneFarmhands` está activo. Cada checkpoint congela el juego mientras guarda; si tarda más de `CheckpointMaxDurationMs`, el intervalo se duplica hasta el día siguiente. Quedan en el registro de sucesos como `checkpoint.created`, `checkpoint.skipped` y `checkpoint.failed`.

**Restaurar una copia** (diaria o checkpoint), con el servicio parado:

1. Aparta la partida actual: `mv Saves/<partida> Saves/<partida>.antes-de-restaurar`.
2. Copia la carpeta elegida como `Saves/<partida>`. Si es de `checkpoints/` y está comprimida, descomprime sus archivos: `gunzip Saves/<partida>/*.gz`.
3. Arranca el servicio. Un checkpoint vuelve a abrir el mismo día desde las 06:00 del juego, con lo hecho hasta la hora del checkpoint; `RealTimeClockModule` lo sincroniza después con la hora real. El log dice «inicio causado por carga de partida» y no se repiten penalizaciones. La posición, la salud y la recuperación offline siguen las reglas normales de carga del juego/mod.

Validación local con dos clientes y límites de compatibilidad: [informe del 6 de noviembre de 2026](checkpoint-validation-2026-11-06.md). El comando de diagnóstico `p4e_checkpoint_spike` requiere `CheckpointDebugMode=true`; debe reservarse a partidas de prueba, ya que reproduce los efectos destructivos del guardado nativo.

**Comandos de prueba de residencias** (`p4e_farm_*`, `p4e_skill`, `p4e_weather`, `p4e_lightning`): requieren `EnableTestCommands=true` y solo deben usarse en partidas de prueba. Guía en [pruebas-residencias.md](pruebas-residencias.md).

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
