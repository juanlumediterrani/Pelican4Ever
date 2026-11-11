# API de Anfitrión v1 (`Pelican 4Ever` — `TASK-803` y `TASK-804`)

Contrato HTTP local para operar un servidor Pelican 4Ever desde fuera del juego. Permite supervisar el reloj en tiempo real, consultar el estado de los vecinos, coordinar tandas y ceremonias de festivales, registrar el descanso diario del host sin desconectar la sesión y programar reinicios controlados.

**Es opcional.** Pelican 4Ever funciona igual sin ella: un anfitrión humano tiene `/evento empezar`, Lewis, los comandos de consola y el rollover automático. La API no arranca hasta configurar un `AdminApiToken` propio y escucha solo en local. Un cliente posible es el anfitrión agente de **Stardew Voyager** / **OpenClaw**, que necesita además `AgentHost: true` para descansar sin desconectarse; ni este mod depende de Voyager ni Voyager de este mod.

---

## 1. Seguridad, Red y Ejecución en el Hilo Principal

1. **Escucha solo en loopback por defecto:**
   - Por defecto (`AdminApiBindAllInterfaces: false` en `config.json`), el servidor escucha únicamente en `http://127.0.0.1:{AdminApiPort}/` y `http://localhost:{AdminApiPort}/`.
   - No se emite la cabecera `Access-Control-Allow-Origin: *`.
2. **Rechazo de arranque con el token por defecto:**
   - Si `AdminApiToken` está vacío o conserva el valor por defecto `pelican4ever_secret_admin_token`, `AdminApiModule` **no arranca** el `HttpListener` y registra un error en el log de SMAPI.
3. **Autenticación obligatoria en todas las rutas:**
   - Tanto el panel web (`GET /`, `GET /dashboard`) como cualquier ruta `/api/*` y `/api/v1/host/*` exigen `Authorization: Bearer <AdminApiToken>` (o `?token=<AdminApiToken>` para el panel web).
   - Sin token válido, el servidor responde `401 Unauthorized`.
4. **Despacho en el hilo principal del juego (`UpdateTicked`):**
   - Toda consulta del mundo o acción mutadora encolada desde un hilo HTTP se ejecuta en el hilo principal de Stardew Valley a través de `MainThreadActionQueue.Drain()` durante el evento `GameLoop.UpdateTicked`.

---

## 2. Rutas del Espacio Versionado `/api/v1/host/`

### `GET /api/v1/host/clock`

Devuelve la hora del juego y real, si el servidor está en tramo de madrugada (`IsNightHold`), el instante del próximo Rollover, el resultado y copia de seguridad del último Rollover y la partida servida.

**Respuesta `200 OK` (`HostClockDto`):**
```json
{
  "RealTimeUtc": "2026-10-31T15:00:00.0000000Z",
  "LocalTime": "2026-10-31T16:00:00",
  "TimeZoneId": "Europe/Madrid",
  "GameTimeOfDay": 1600,
  "TotalGameDays": 12,
  "Year": 1,
  "Season": "spring",
  "DayOfMonth": 13,
  "IsNightHold": false,
  "NextRolloverLocalTime": "2026-11-01T02:00:00",
  "SecondsUntilNextRollover": 36000,
  "LastRolloverGameDay": 11,
  "LastRolloverOutcomeSummary": "rested=3, penalized=0, untouched=1",
  "LastBackupFileName": "PelicanValley_20261031_020000_000",
  "LastBackupCreatedUtc": "2026-10-31T01:00:00.0000000Z",
  "SaveFolderName": "PelicanValley_452974881",
  "IsMultiplayerServer": true
}
```

`SaveFolderName` es la carpeta de `Saves/` de la partida cargada e `IsMultiplayerServer` indica si está abierta como servidor de cooperativo; un supervisor desatendido los usa para comprobar que el servidor sirve la partida autorizada (`TASK-805`, ver `docs/servidor-persistente.md`).

### `GET /api/v1/host/players`

Devuelve los jugadores conectados y registrados, su ubicación y coordenadas actuales, su residencia asignada, si están dentro del festival de hoy y su `PlayerRecord` completo (estado de descanso y saciedad).

**Respuesta `200 OK` (`HostPlayersResponseDto`):**
```json
{
  "TotalOnline": 2,
  "TotalRegistered": 4,
  "Players": [
    {
      "PlayerId": 1234567890,
      "Name": "VoyagerHost",
      "IsOnline": true,
      "IsHost": true,
      "LocationName": "Town",
      "TileX": 28,
      "TileY": 67,
      "ResidenceSlotId": "Pelican4Ever_Farm_01",
      "ResidenceDoorId": "Town_Door_01",
      "ResidenceFarmType": "Standard",
      "InFestival": false,
      "SleepState": "Rested",
      "LastActiveGameDay": 12,
      "RestedOnGameDay": 12,
      "LastOutcome": "Rested",
      "SatietyUsed": 40,
      "WellFedBuffsUsed": 1,
      "WellFedActive": true,
      "SpaBuffsUsed": 0,
      "SpaBuffActive": false,
      "SatietyGameDay": 12,
      "PendingMarketGold": 0,
      "IsClaimed": true,
      "OwnerTokenMasked": "a1b2...c5d6"
    }
  ]
}
```

### `GET /api/v1/host/festival`

Devuelve el estado del festival de hoy, su ventana horaria, ubicación, si dispone de evento principal o ceremonia en vivo, jugadores presentes en el recinto, inscritos a la próxima tanda, tandas ejecutadas y quiénes ya han participado hoy.

**Respuesta `200 OK` (`HostFestivalDto`):**
```json
{
  "IsFestivalToday": true,
  "FestivalId": "festival_spring13",
  "FestivalName": "Festival del Huevo",
  "Season": "spring",
  "DayOfMonth": 13,
  "LocationName": "Town",
  "WindowStart": 1800,
  "WindowEnd": 2350,
  "IsWindowOpen": true,
  "HasMainEvent": true,
  "IsCeremonyFestival": false,
  "PresentPlayerIds": [1234567890, 987654321],
  "SignedUpPlayerIds": [987654321],
  "PlayedPlayerIds": [],
  "RunsCount": 0
}
```

### `GET /api/v1/host/map?location=Town`

Devuelve un `HostMapDto` con `LocationName`, `Width`, `Height`, `Tiles`, `Players`, `Warps`, `AvailableMaps`, `Season` y `GameDay`. Solo consulta ubicaciones existentes, incluidos los interiores de edificios con sus nombres únicos. Si no se encuentra la ubicación solicitada, muestra Town. Sin mundo cargado devuelve dimensiones 0.

`Tiles` es un `byte[]` serializado por System.Text.Json como **base64** (no como un array JSON). Hay que decodificarlo antes de leer el índice `y * Width + x`. Por ejemplo, las celdas `[1, 3, 8, 11]` se reciben como `"AQMICw=="`. Los valores son: 0 desconocido, 1 suelo, 2 bloqueado, 3 agua, 4 edificio, 5 camino, 6 hierba, 7 árbol, 8 salida, 9 mina, 10 NPC y 11 jugador. Es una clasificación de supervisión, no una malla de navegación.

`Players` contiene marcadores con `PlayerId`, `Name`, `TileX`, `TileY`, `IsHost`; `Warps` contiene `TileX`, `TileY`, `TargetMap`, `TargetX`, `TargetY`. Cada consulta del mundo se ejecuta en el hilo principal y exige el mismo token que las demás rutas.

### `POST /api/v1/host/festival/start`

Equivale a `/evento empezar` en `FestivalParticipationModule`. Cierra la inscripción actual y arranca la siguiente tanda del evento principal únicamente para los jugadores apuntados.

**Respuesta `200 OK` (`HostActionResultDto`):**
```json
{
  "Ok": true,
  "Message": "[Festival] Empieza el evento (tanda #1) con 2 participantes.",
  "ExecutedOnThreadId": 1,
  "EventId": 14
}
```

### `POST /api/v1/host/festival/ceremony`

Dispara la ceremonia comunitaria en vivo (`TASK-702`, Luau o Danza de las Medusas Lunares) con todos los vecinos presentes en el recinto del festival.

**Respuesta `200 OK` (`HostActionResultDto`):**
```json
{
  "Ok": true,
  "Message": "Ceremonia iniciada para 3 vecino(s) en festival_summer11.",
  "ExecutedOnThreadId": 1,
  "EventId": 15
}
```

### `POST /api/v1/host/announce`

Envía un aviso global a los jugadores conectados por chat, HUD o ambos canales, y lo registra en el log de sucesos.

**Cuerpo de petición (`HostAnnounceRequestDto`):**
```json
{
  "message": "La búsqueda de huevos comenzará en 5 minutos.",
  "channel": "both"
}
```

**Respuesta `200 OK` (`HostActionResultDto`):**
```json
{
  "Ok": true,
  "Message": "Anuncio emitido (both).",
  "ExecutedOnThreadId": 1,
  "EventId": 16
}
```

### `POST /api/v1/host/rest`

Registra el descanso diario del jugador anfitrión (`RestedOnGameDay` y `LastActiveGameDay`) sin llamar a `Game1.ExitToTitle()` ni desconectar la sesión (`TASK-804`).

**Respuesta `200 OK` (`HostActionResultDto`):**
```json
{
  "Ok": true,
  "Message": "Descanso registrado. Modo anfitrión agente activo: el servidor permanece en línea.",
  "ExecutedOnThreadId": 1,
  "EventId": 17
}
```

### `POST /api/v1/host/restart`

Programa o cancela un reinicio controlado del servidor (`TASK-804`), equivalente al comando de consola `p4e_safe_restart`.
- Avisa a los jugadores conectados con antelación configurable (`warningMinutes`).
- Calcula e informa del progreso diario no guardado en riesgo (`ProgressAtRisk`), dado que Stardew Valley solo guarda al cambiar de día.
- Por defecto (`immediate: false`), espera hasta justo después del siguiente guardado/Rollover (`WaitingForNextSave: true`).
- Si se solicita `immediate: true` por causa mayor, ejecuta el reinicio tras los minutos de preaviso indicados.

**Cuerpo de petición (`HostRestartRequestDto`):**
```json
{
  "reason": "Actualización de mantenimiento",
  "immediate": false,
  "warningMinutes": 5,
  "cancel": false
}
```

**Respuesta `200 OK` (`SafeRestartStatusDto`):**
```json
{
  "IsPending": true,
  "Mode": "after_next_save",
  "Reason": "Actualización de mantenimiento",
  "Immediate": false,
  "WaitingForNextSave": true,
  "WarningMinutes": 5,
  "RequestedAtUtc": "2026-10-31T15:10:00.0000000Z",
  "ExecuteNotBeforeUtc": null,
  "WarningAnnouncement": "[Reinicio Controlado] El servidor se reiniciará justo después del próximo guardado/Rollover (preaviso de 5 min)...",
  "ProgressAtRisk": {
    "CurrentGameDay": 12,
    "CurrentGameTime": 1430,
    "GameMinutesSinceMorning": 510,
    "OnlinePlayersCount": 2,
    "UnrestedActivePlayersCount": 1,
    "WouldLoseUnsavedDayProgress": true,
    "Summary": "Progreso no guardado del día 12 (8h 30m desde las 06:00, 2 jugador(es) conectado(s), 1 sin descansar hoy). Stardew Valley solo guarda al cambiar de día (Rollover)."
  },
  "EventId": 57
}
```

### `GET /api/v1/host/events?since=N`

Devuelve la lista de solo añadir (append-only) de sucesos con `Id > N` en orden estrictamente creciente y sin duplicados. Si `LastEventId` es menor que el cursor solicitado, el registro se ha reiniciado y el cliente debe volver a consultar desde 0.
Incluye inscripciones a festivales (`festival.signup`, `festival.signup.left`), tandas iniciadas y terminadas (`festival.run.started`, `festival.run.ended`, `festival.ceremony.started`), rollover iniciado, completado y atascado en el cambio de día (`rollover.started`, `rollover.completed`, `rollover.stalled`: el mod ha tenido que volver a lanzar el fundido del cambio de día; trae `nudges` y `secondsSinceAdvance`), copia de seguridad creada (`backup.created`), checkpoints intradía (`checkpoint.created` con `backupName`, `bytes`, `totalMs`, `longestTickMs`, `slow` y `nextIntervalMinutes`; `checkpoint.skipped` con `reason` cuando tocaba y no se pudo; `checkpoint.failed` con `error`), descanso del host (`host.rested`), anuncios (`host.announcement`), reinicios controlados (`host.restart.scheduled`, `host.restart.cancelled`, `host.restart.executed`), préstamos de dibujado (`host.render.lease.granted`, `host.render.lease.released`) y conexión/desconexión de jugadores (`player.connected`, `player.disconnected`) y expulsión por mods (`player.rejected`, con el motivo) y comandos remotos (`host.command`).

**Respuesta `200 OK` (`HostEventsResponseDto`):**
```json
{
  "SinceId": 0,
  "LastEventId": 2,
  "Events": [
    {
      "Id": 1,
      "Type": "player.connected",
      "TimestampUtc": "2026-10-31T15:01:00.0000000Z",
      "GameDay": 12,
      "GameTime": 1500,
      "Summary": "Jugador conectado: Alex",
      "Data": {
        "playerId": 987654321,
        "playerName": "Alex"
      }
    },
    {
      "Id": 2,
      "Type": "festival.signup",
      "TimestampUtc": "2026-10-31T15:02:00.0000000Z",
      "GameDay": 12,
      "GameTime": 1500,
      "Summary": "Alex se apuntó a festival_spring13.",
      "Data": {
        "festivalId": "festival_spring13",
        "playerId": 987654321,
        "playerName": "Alex",
        "signedUpCount": 1
      }
    }
  ]
}
```

### `GET /api/v1/host/commands`

Lista **cerrada** de comandos de consola de Pelican que se pueden ejecutar a distancia. Con la consola de SMAPI desactivada (servidor sin terminal) es la única forma de lanzarlos. Lo que no está en la lista no se puede invocar, aunque exista como comando.

**Respuesta `200 OK` (`HostCommandCatalogDto`):**
```json
{
  "Commands": [
    { "Command": "p4e_status", "Risk": "Read", "Usage": "p4e_status", "Available": true },
    { "Command": "p4e_checkpoint_now", "Risk": "Safe", "Usage": "p4e_checkpoint_now", "Available": true },
    { "Command": "p4e_claim_reset", "Risk": "Sensitive", "Usage": "p4e_claim_reset <nombre_o_id>", "Available": true }
  ]
}
```

`Risk`: **`Read`** solo informa (`p4e_status`, `p4e_doors`, `p4e_claim_list`, `p4e_pair_list`); **`Safe`** cambia algo sin poder perder progreso (`p4e_checkpoint_now`: guarda y copia sin cambiar de día; `p4e_pair_allow <usuario> <ip>`: prepara un alta por IP con código de pairing válido 3 días); **`Sensitive`** cambia el mundo o a un jugador (`p4e_claim_reset`, `p4e_pair_revoke`, `p4e_forage_wave`, `p4e_festival_move`): quien lo invoque debería tener la aprobación del dueño. Quedan fuera a propósito `p4e_rollover_now` (adelanta el día), `p4e_safe_restart` (tiene su ruta con avisos), `p4e_time` (desajusta el calendario real), `p4e_migrate_save`, `p4e_give_*` y los comandos de prueba.

### `POST /api/v1/host/command`

Ejecuta **en el hilo del juego** un comando de esa lista y devuelve lo que escribe en el registro de SMAPI mientras se ejecuta (hasta 40 líneas de 300 caracteres; lo que emita más tarde, en otros ticks, no entra). Valida el comando, el número de argumentos y su alfabeto (`A-Z a-z 0-9 . _ - @ :`, máx. 40) y exige una partida cargada. Todo intento, aceptado o rechazado, deja un suceso `host.command`.

**Cuerpo de petición (`HostCommandRequestDto`):**
```json
{
  "Command": "p4e_claim_reset",
  "Args": [ "mTx" ]
}
```

**Respuesta `200 OK` (`HostCommandResultDto`):**
```json
{
  "Ok": true,
  "Command": "p4e_claim_reset",
  "Risk": "Sensitive",
  "Output": [ "Posesión de 'mTx' liberada." ],
  "Message": "p4e_claim_reset ejecutado.",
  "ExecutedOnThreadId": 1,
  "EventId": 31
}
```

Un comando no permitido o con argumentos no válidos devuelve `Ok: false` con el motivo en `Message` y no se ejecuta.

### `GET /api/v1/host/render`

Estado del **servidor sin dibujado** (`HeadlessRendering`). En un servidor desatendido (`AutoHostSaveName` configurado) el host deja de dibujar con el mundo cargado: la simulación sigue a 60 Hz, la red funciona igual y cada cliente dibuja lo suyo. Sin GPU, dibujar por CPU ocupa núcleos enteros aunque nadie mire la pantalla del host. Se dibuja igualmente en el título y la carga, con la pantalla de guardado abierta (`SaveGameMenu` no empieza a guardar hasta haberse dibujado) y mientras haya un préstamo de dibujado.

**Respuesta `200 OK` (`HostRenderStatusDto`):**
```json
{
  "HeadlessRenderingEnabled": true,
  "Drawing": false,
  "Reason": "Suppressed",
  "Leases": [],
  "Message": ""
}
```

`Reason`: `Disabled` (opción desactivada), `Attended` (sin `AutoHostSaveName`: hay una persona de host), `WorldNotReady` (título o carga), `DrawRequiredMenu` (pantalla de guardado), `Lease` (préstamo vigente) o `Suppressed` (sin dibujar).

### `POST /api/v1/host/render`

Pide o retira un **préstamo de dibujado** con caducidad, para quien necesite ver la pantalla del host (Stardew Voyager manejando menús, Putre o un diagnóstico). Cada solicitante tiene como mucho uno: pedirlo de nuevo lo renueva. Dura entre 1 y 120 minutos (se recorta); si quien lo pidió se cae, caduca solo y el host vuelve a no dibujar. `Minutes: 0` lo retira. No toca el estado del juego. Las capturas de `takeMapScreenshot` (las del puente de Voyager) no necesitan préstamo.

También: comando de consola `p4e_render [status] | <minutos> | off` (solicitante `console`) y, para otros mods, la API de mod `RequestRendering(requester, minutes)` / `ReleaseRendering(requester)` / `IsHostDrawing()` (`Helper.ModRegistry.GetApi<...>("Pelican4Ever.SVRTMP")`).

**Cuerpo de petición (`HostRenderRequestDto`):**
```json
{
  "Requester": "putre",
  "Minutes": 15
}
```

**Respuesta `200 OK` (`HostRenderStatusDto`):**
```json
{
  "HeadlessRenderingEnabled": true,
  "Drawing": true,
  "Reason": "Lease",
  "Leases": [
    { "Requester": "putre", "ExpiresAtUtc": "2026-11-09T05:15:00Z" }
  ],
  "Message": "Dibujado prestado a 'putre' hasta 2026-11-09T05:15:00.0000000Z."
}
```

