# ADR-0010: Residencias Escalables (16/32/64), Cola de Admisión y Gates al Este de Pelican Town

* **Estado:** Implementado (pendiente medición de 64 residencias y verificación en vivo)
* **Fecha:** 2026-11-15
* **Versión objetivo:** 0.5 (rama `feat/v0.5-residence-scaling`)
* **Amplía:** [ADR-0003](0003-declarative-farm-slots.md) y [ADR-0005](0005-host-plaza-and-bridge-maps.md). **Prepara:** ADR-0011 (concurrencia ampliada, versión 0.6).

## Contexto

El servidor admite hoy 16 residencias (`FarmSlotCount` entre 1 y 16) repartidas entre la Granja Host y tres puentes al oeste (ADR-0005). Se quiere que un mismo mundo aloje **16, 32 o 64 residencias** manteniendo **16 jugadores simultáneos**: más vecinos que plazas, que entran por turnos.

Hechos verificados sobre el decompilado de Stardew Valley 1.6.15 y el código del mod:

1. **El motor no tiene un tope de jugadores propio.** El único límite de conexiones es `LidgrenServer.initialize` (`MaximumConnections = Game1.multiplayer.playerLimit * 2`, con `playerLimit = 8` en el constructor de `Multiplayer`). El mod no modifica `playerLimit`, así que los 16 simultáneos actuales son ese valor por defecto. Un jugador 17 no llega a establecer conexión: no se le puede explicar que el servidor está lleno.
2. **Residencias y simultaneidad ya están desacopladas en red.** `Postfix_IsAlwaysActiveLocation` delega en `LocationCullingLogic.ShouldKeepActive`, que deja fuera de la sincronización continua toda residencia o anexo sin residente conectado ni visitantes dentro. El tráfico hacia los clientes depende de quién está conectado, no del número de residencias.
3. **`FarmSlotCount` hace dos papeles.** Además de contar residencias, `FarmSlotModule.RemoveHostCabins` eleva con él `highestPlayerLimit` y `currentPlayerLimit`; `Game1.AddLocations` crea un `Cellar{i}` vanilla por cada unidad de `highestPlayerLimit`.
4. **El saludo envía todos los peones.** `GameServer.sendAvailableFarmhands` serializa completo (`NetRef<Farmer>.WriteFull`) cada peón disponible y `Prefix_SendAvailableFarmhands` no filtra la lista. Con co-residencia (Épica 23) hay hasta dos cuentas por residencia: 64 residencias son hasta 128 peones por intento de conexión, además de exponer a cada cliente los personajes de todos.
5. **La topología es una cadena única.** `HostPlazaLayout.Chain` describe Granja Host → `Pelican4Ever_Bridge_1..3` (`MaxBridges = 3`), y `doors.json` cataloga 16 portales.
6. **Los puentes se registran con `AlwaysActive = true`** (`FarmSlotModule`, entrada de `Data/Locations`). `GameLocation.CanBuildHere` solo exige esa condición a las ubicaciones donde se construye, que son las granjas.
7. **La red de vagonetas admite muchos destinos.** `GameLocation.MinecartTransport` muestra los destinos con `ShowPagedResponses` (5 por página).

## Decisión

### 1. Magnitudes independientes

| Magnitud | Clave de configuración | Valores en 0.5 |
| --- | --- | --- |
| Residencias | `FarmSlotCount` (se conserva el nombre) | `1..16`, `32` o `64` |
| Jugadores simultáneos (clientes, sin contar al host) | `MaxConcurrentPlayers` (nueva) | `1..16`, por defecto `16` |
| Plazas de espera | `AdmissionQueueCapacity` (nueva) | `0..32`, por defecto `16` |
| Reserva de plaza tras caída de red | `ReconnectGraceMinutes` (nueva) | `0..30`, por defecto `5` (`0` la desactiva) |

`FarmSlotCount` deja de alimentar `highestPlayerLimit` y `currentPlayerLimit`. `LidgrenServer` se inicializa con `MaximumConnections = MaxConcurrentPlayers + AdmissionQueueCapacity`. Cambiar cualquiera de ellas exige reiniciar el servidor.

### 2. Admisión por orden de llegada con cola

1. Toda decisión de entrada pasa por un único punto de dominio puro, `AdmissionLogic` (`Pelican4Ever.Core/Networking`), que devuelve *admitido*, *en cola (posición n)* o *rechazado (motivo)*. ADR-0011 añade ahí el tope por mapa.
2. Una conexión entra en la cola **después** de superar la prueba de identidad de dispositivo (ADR-0007): quien no está autorizado no ocupa plaza de espera.
3. Con `MaxConcurrentPlayers` clientes en juego, la conexión se retiene antes de la selección de personaje y recibe su posición por el mensaje de espera que el juego ya usa (`OutgoingMessage` tipo 11). Al liberarse una plaza se atiende a la conexión más antigua.
4. Con la cola llena, la conexión se cierra con un mensaje que lo dice.
5. La cola no se persiste: tras el cierre ordenado del cambio de día, la entrada vuelve a ser por orden de llegada.

### 3. Reserva de plaza tras una caída de red

1. Cuando un jugador en juego pierde la conexión **sin haberla cerrado él** (corte de red, tiempo de espera agotado, cierre inesperado del cliente), su plaza queda reservada durante `ReconnectGraceMinutes` (por defecto `5`).
2. Mientras dura la reserva, la plaza cuenta como ocupada para la admisión: la cola no avanza sobre ella.
3. Si el mismo dispositivo vuelve dentro del plazo, entra directamente en su plaza sin pasar por la cola. Pasado el plazo, la plaza se libera y el jugador entra como cualquier otro.
4. No generan reserva: la salida voluntaria (salir al título, cerrar sesión en la cama), la expulsión por inactividad, la expulsión por administración ni el cierre ordenado del cambio de día.
5. Las reservas no se persisten: un reinicio del servidor las anula.

### 4. Expulsión por inactividad

1. Un jugador está inactivo cuando pasan `IdleKickMinutes` (por defecto `30`) sin movimiento, uso de herramienta, interacción ni mensaje de chat.
2. **Solo se expulsa cuando el servidor está por encima de la mitad de su capacidad:** los jugadores conectados, contando al inactivo, superan `MaxConcurrentPlayers / 2`. Con capacidad para 16 se expulsa a partir de 9 conectados; con 8 o menos, no. Por debajo de ese umbral, estar quieto no perjudica a nadie y forma parte del juego (descanso sentado de «Bien comido», buff del spa).
3. El contador de inactividad corre siempre; el umbral de ocupación se comprueba en el momento de expulsar. Quien lleva 30 minutos quieto con 8 conectados es expulsado cuando entra el noveno, tras el aviso.
4. Antes de expulsar se avisa con `IdleKickWarningMinutes` de antelación (por defecto `2`); cualquier acción del jugador reinicia el contador.
5. La expulsión es un cierre ordenado: se guarda el estado vivo del jugador igual que en una desconexión normal y no cuenta como penalización de sueño.

### 5. Saludo con lista de peones filtrada

`Prefix_SendAvailableFarmhands` envía únicamente:

* el peón ligado al dispositivo, si lo tiene; o
* **un** peón sin reclamar, si el dispositivo aún no tiene personaje.

Nunca se serializa el peón de otro jugador.

### 6. Topología: cadenas ancladas

La plaza pasa de una cadena a un conjunto de **cadenas con ancla**:

| Cadena | Ancla | Puentes | Residencias |
| --- | --- | --- | --- |
| Oeste (existente, sin cambios) | Borde oeste de la Granja Host | `Pelican4Ever_Bridge_1..3` | `01..16` (4 en la Granja Host) |
| Este-Herrería | Borde este de `Town`, a la altura de la casa de Clint | Puentes pares `4, 6, 8, 10, 12, 14` | 4 por puente |
| Este-Museo | Borde este de `Town`, a la altura del museo | Puentes impares `5, 7, 9, 11, 13, 15` | 4 por puente |

1. El puente `N ≥ 4` aloja las residencias `4N + 1 .. 4N + 4` (puente 4 → `17..20`, puente 15 → `61..64`). La asignación es fija: pasar de 32 a 64 no mueve ninguna residencia existente.
2. Con `FarmSlotCount ≤ 16` no hay gates ni puentes al este y `Maps/Town` no se modifica. Con `32` se abren los dos gates y los puentes `4..7` (dos por cadena). Con `64`, los puentes `4..15` (seis por cadena).
3. Los gates se tallan editando `Maps/Town` en `AssetRequested` (edición, no sustitución) y escribiendo sus warps en la propiedad `Warp` del mapa, igual que los pasos de ADR-0005. Las casillas exactas se fijan con `MapStudio` y verificación BFS, y se anotan en este ADR como hallazgo empírico.
4. Los puentes del este se generan con `MapCloneHelper.CreateBridgeMap`, el mismo generador verificado de los puentes `1..3`. El primer puente de cada cadena conecta con `Town` por su borde oeste; la cadena crece hacia el este.
5. Cada puente del este tiene vagoneta en la red existente (`MinecartPlazaLogic`).
6. **Los puentes `4..15` se registran sin `AlwaysActive`.** Los puentes `1..3` se migran a la misma condición si la verificación no encuentra código que dependa de tenerlos sincronizados fuera de ellos.

### 7. Desescalado seguro

El host no reduce las residencias por debajo de la residencia reclamada más alta. Si `FarmSlotCount` es menor, arranca con el menor de los valores admitidos (`16`, `32`, `64`) que la conserva y lo avisa en el registro. Los retornos desde un puente inactivo siguen la regla de ADR-0005, con destino a la casilla interior del gate de `Town` para las cadenas del este.

### 8. 64 residencias, condicionado a medición

Cada residencia son hasta 7 ubicaciones (granja 80×65, casa, cueva, sótano, invernadero y dos anexos): unas 450 con 64. Todas pasan por el cambio de día, el guardado, la copia de seguridad y los checkpoints. `FarmSlotCount = 64` se documenta como soportado solo si, en el host de referencia y con las 64 residencias pobladas:

* un checkpoint termina dentro de `CheckpointMaxDurationMs`; y
* el cambio de día completo (guardado y copia verificada) termina en **120 s** o menos.

Si no se cumple, 64 queda marcado como experimental y 32 como máximo soportado.

## Consecuencias

* **Positivas:** más vecinos sin más carga de red por cliente; el jugador que no cabe sabe su posición en la cola; una caída de red no manda al jugador al final de la cola; el saludo deja de crecer con el número de cuentas y de exponer personajes ajenos; la topología por cadenas admite nuevas anclas sin rehacer el modelo.
* **Negativas:** `Maps/Town` pasa a depender de la configuración del servidor (host y clientes deben coincidir, como ya ocurre con los puentes); las cadenas del este llegan a seis pantallas, que la vagoneta acorta pero no elimina; el guardado y el cambio de día crecen linealmente con las residencias.
* **Migración:** las partidas con 16 residencias no cambian. `ServerWorldState` no necesita campos nuevos para las residencias `17..64` (los identificadores `Pelican4Ever_Farm_NN` ya son de dos dígitos); `doors.json` crece o se genera para los puentes `4..15`.

## Hallazgos empíricos de implementación

1. **`Maps/Town.xnb` (130×110):**
   * **Gate Este-Herrería (puentes pares `4, 6, 8, 10, 12, 14`):** filas `y = 81..82`, tallando `x = 111..129` con hierba transitable y warps en `x = 130` hacia `Pelican4Ever_Bridge_4 (1, 23..24)`. Retorno a `Town` en `(128, 81)` mirando al oeste.
   * **Gate Este-Museo (puentes impares `5, 7, 9, 11, 13, 15`):** filas `y = 92..93`, tallando `x = 111..129` con hierba transitable y warps en `x = 130` hacia `Pelican4Ever_Bridge_5 (1, 23..24)`. Retorno a `Town` en `(128, 92)` mirando al oeste.
2. **Desconexión voluntaria frente a caída de red:** `LidgrenClient.disconnect(neatly = true)` envía `OutgoingMessage` tipo `19` (`Multiplayer.disconnecting`) antes de cerrar el socket; las caídas de red y timeouts no lo envían.
3. **Puentes `1..3` sin `AlwaysActive`:** la auditoría confirmó que ningún módulo depende de sincronizar `Pelican4Ever_Bridge_1..3` en clientes fuera de ellos; todos los puentes `1..15` quedan registrados con `AlwaysActive = false`.

## Sin verificar al redactar

1. El terreno del borde este de `Maps/Town` en las dos alturas elegidas (no se ha abierto el `.xnb`).
2. El coste real de guardado, cambio de día y memoria con 64 residencias pobladas.
3. Que ningún módulo dependa de que los puentes `1..3` estén siempre activos en los clientes.
4. El comportamiento de los gates durante los festivales, que sustituyen `Town` por un mapa temporal.
5. Si `currentPlayerLimit` sigue haciendo falta para algo más que el tope de cabañas de Robin una vez separado de `FarmSlotCount`.
6. Cómo distingue el host con fiabilidad una caída de red de una salida voluntaria (motivo de desconexión de Lidgren frente al mensaje de despedida del cliente).
