# Backlog Técnico y de Producto — *Pelican 4Ever* (`SV-RTMP`)

Basado en las especificaciones revisadas de [design.md](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/design.md) y [architecture.md](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/architecture.md), y verificado sobre el código decompilado de **Stardew Valley 1.6.15** y **SMAPI 4.5.2**.

---

## 🛠️ 1. Toolchain y Entorno del Proyecto

* **.NET SDK 8.0**: Compila `Pelican4Ever.Core` y `Pelican4Ever` hacia `net6.0` (runtime del juego) y ejecuta `Pelican4Ever.Tests` sobre `net8.0`.
* **`Pathoschild.Stardew.ModBuildConfig` (v4.3.2)**: Empaqueta y despliega automáticamente el mod en la carpeta `Mods/Pelican4Ever` al compilar. La ruta local del juego se define en `local.props` (no versionado; usar `local.props.example` como plantilla).
* **ILSpy CLI (`ilspycmd`)**: Decompilador de línea de comandos para inspeccionar `Stardew Valley.dll`.
* **xUnit (`Pelican4Ever.Tests`)**: Suite de pruebas unitarias rápidas sobre toda la lógica pura de tiempo, calendario, sueño y saciedad (`dotnet test`).
* **JunimoServer / Docker**: Contenedor con `xvfb` y `noVNC` apoyado sobre el modo dedicado nativo de Stardew Valley 1.6.15 (`Game1.HasDedicatedHost`).

---

## 📋 2. Backlog por Fases e Iteraciones

### Fase 0: Fundamentos, Arquitectura Limpia y Suite de Tests (Completada)
> **Objetivo:** Separar la lógica de dominio pura (`Pelican4Ever.Core`) del mod SMAPI (`Pelican4Ever`), validar configuración al arranque, asegurar versión de clientes y cubrir todas las reglas temporales y de balance con tests unitarios.

- [x] **TASK-001: Solución multiproyecto (`Pelican4Ever.Core`, `Pelican4Ever`, `Pelican4Ever.Tests`)**
  - **Criterios de Aceptación:** `dotnet build Pelican4Ever.sln` compila con `0` advertencias (`TreatWarningsAsErrors = true`) y despliega `Pelican4Ever.dll` + `Pelican4Ever.Core.dll` en `Mods/Pelican4Ever`.
- [x] **TASK-002: Configuración tipada con validación al arranque (`ServerConfig.cs`)**
  - **Criterios de Aceptación:** `ServerConfig.Validate()` comprueba formatos `HH:mm`, `DayStartTime == 06:00`, `NightHoldStartTime <= 01:50`, `CalendarEpoch` en lunes y rangos numéricos. Si hay errores, el mod los registra y se desactiva limpiamente.
- [x] **TASK-003: Lógica pura de Reloj, Calendario por Época, Sueño y Saciedad (`Pelican4Ever.Core`)**
  - **Criterios de Aceptación:** Implementados `GameClock`, `ServerCalendar`, `ServerTime`, `PlayerRecord`, `SleepPolicy`, `SatietyBudget` y `ServerWorldState` (esquema v2 con migración), cubiertos por **59 tests unitarios** en `Pelican4Ever.Tests`.
- [x] **TASK-004: Registro modular aislado (`IModule`, `ModContext`), `PersistenceModule`, `ClientVersionGuardModule` y `StatusCommandModule`**
  - **Criterios de Aceptación:** Cada módulo se registra con su propio `try/catch` y puede activarse/desactivarse en `config.json`. `ClientVersionGuardModule` expulsa clientes sin el mod o con versión incompatible. El comando `p4e_status` muestra reloj esperado, fecha derivada y estado de jugadores.

---

### Fase 0.5: Spikes de Validación de Motor (Antes de Iteración 1)
> **Objetivo:** Validar con prototipos mínimos en el juego real los 4 puntos de fricción del motor identificados en la revisión de arquitectura.

- [ ] **SPIKE-01: Disparo automático de `NewDay` + `Save` nativo con clientes conectados**
  - **Pregunta:** ¿Se puede iniciar la transición diaria nativa a las `06:00 AM` (o con comando `p4e_rollover_now`) cuando hay `Farmhands` conectados fuera de la cama sin colgar el `ReadyCheck` `"sleep"`?
  - **Criterio de Éxito:** El día avanza, la partida se guarda en disco y los clientes continúan conectados en el nuevo día.
- [ ] **SPIKE-02: Persistencia de `Pelican4Ever_Farm_01` declarada en `Data/Locations` con una `Cabin`**
  - **Pregunta:** ¿Guarda y recarga `SaveGame.loadDataToLocations` todos los cultivos, objetos y la `Cabin` de un `Farmhand` dentro de una ubicación personalizada `Pelican4Ever_Farm_01`?
  - **Criterio de Éxito:** Tras plantar cultivos, colocar un cofre y reiniciar el servidor, todo permanece intacto y el `Farmhand` conserva su punto de aparición.
- [ ] **SPIKE-03: Reloj 1:1 con `realMilliSecondsPerGameTenMinutes = 600_000` y `NightHold` en `2550`**
  - **Pregunta:** ¿Avanzan correctamente hornos, rutas de NPCs e iluminación ambiental entre `0600` y `2550`, y se mantiene el jugador sin desmayarse durante el tramo `01:50–06:00` (`2550`)?
  - **Criterio de Éxito:** Cero desmayos a las `02:00 AM` y las máquinas descuentan los 250 minutos de madrugada (`2550 → 0600`) durante el Rollover nativo.
- [ ] **SPIKE-04: Interacción con el modo dedicado nativo 1.6.15 (`Game1.HasDedicatedHost` / `DedicatedServer`)**
  - **Pregunta:** ¿Qué ajustes necesita `DedicatedServer.Tick()` para que no pause `Game1.netWorldState.Value.IsPaused` cuando no hay jugadores conectados (`onlineIds.Count == 0`)?
  - **Criterio de Éxito:** El reloj del servidor sigue avanzando y ejecuta el Rollover de las `06:00 AM` con `0` clientes conectados.

---

### Iteración 1: El Motor Temporal, Rollover Nativo y Sueño Asíncrono (Épica 1)
> **Objetivo:** Conectar `GameClock`, `ServerCalendar` y `SleepPolicy` con el bucle de Stardew Valley.

- [x] **TASK-101: Módulo de Reloj en Tiempo Real y `NightHold` (`RealTimeClockModule.cs`)**
  - **Prioridad:** Crítica
  - **Descripción:**
    - Fijar `Game1.realMilliSecondsPerGameTenMinutes = 600_000` y sincronizar `gameTimeInterval = clockReading.MillisecondsIntoSlot` para mantener la interpolación nativa de luz ambiental.
    - En el Host, controlar los pasos de `Game1.performTenMinuteClockUpdate()` mediante `GameClock.TicksToReach(Game1.timeOfDay, clockReading.TimeOfDay)` y congelar el reloj en `2550` durante `IsNightHold` (`01:50–06:00` reales).
    - Parchear `Game1.shouldTimePass` y `DedicatedServer.Tick` para que el tiempo no se pause por menús ni por ausencia de jugadores.
  - **Criterios de Aceptación:** El reloj refleja la hora real entre `06:00` y `01:50`, se mantiene en `2550` de `01:50` a `06:00` sin provocar desmayo y las máquinas procesan el tramo restante al cambiar de día.
- [x] **TASK-102: Módulo de *Bed-Logout* y mensajería al Host (`BedLogoutModule.cs`)**
  - **Prioridad:** Crítica
  - **Descripción:**
    - Al conectar un jugador (`PeerConnected` / `SaveLoaded`), actualizar `PlayerRecord.LastActiveGameDay = Game1.Date.TotalDays`.
    - Parche `Prefix` en `GameLocation.answerDialogueAction` para `"Sleep_Yes"`: enviar mensaje `BedLogoutRequest` al Host (que registra `RestedOnGameDay = Game1.Date.TotalDays`), mostrar confirmación en el HUD y desconectar al menú (`Game1.ExitToTitle()`) tras `1500 ms` sin activar el cierre de día global.
  - **Criterios de Aceptación:** Dormir en la cama desconecta al jugador con estado `Rested` (`p4e_status`) mientras los demás siguen jugando.
- [x] **TASK-103: Gestor del Rollover Diario Nativo a las `06:00 AM` y `CatchUpPlan` (`DailyRolloverModule.cs`)**
  - **Prioridad:** Crítica
  - **Descripción:**
    - Emitir avisos en el HUD a los jugadores conectados `15`, `5` y `1` minuto antes de las `06:00 AM`.
    - A las `06:00:00` reales (o mediante el comando `p4e_rollover_now`), lanzar la transición nativa de fin de día y guardado, aplicando `ServerCalendar.PlanCatchUp` si el servidor estuvo apagado.
    - En `DayStarted`, evaluar `SleepPolicy.Evaluate` para cada `PlayerRecord`, aplicar `WakeUpEnergy` y `MedicalFee` sobre cada `Farmer` (con `useSeparateWallets = true`), congelar la caída de amistad de jugadores `Offline_*` y crear copia de seguridad rotativa del save.
  - **Criterios de Aceptación:** Los cultivos avanzan, se liquidan las `Shipping Bins` personales y los jugadores ausentes varios días seguidos no reciben penalizaciones adicionales.

---

### Iteración 2: 4 Huecos de Granja y Puertas en Pelican Town (Épica 2)
> **Objetivo:** Habilitar 4 granjas privadas estándar (`Pelican4Ever_Farm_01..04`) conectadas a puertas de Pelican Town para el MVP de 4 jugadores.

- [x] **TASK-201: Inyección declarativa de los 4 huecos en `Data/Locations` (`FarmSlotModule.cs`)**
  - **Prioridad:** Crítica
  - **Descripción:** Suscribirse a `AssetRequested` para `"Data/Locations"` y registrar `Pelican4Ever_Farm_01` a `Pelican4Ever_Farm_04` (según `Config.FarmSlotCount`) con `CreateOnLoad` y `AlwaysActive = true`. Activar `Game1.player.team.useSeparateWallets.Value = true` al cargar o crear la partida maestra.
  - **Criterios de Aceptación:** Las 4 ubicaciones existen en `Game1.locations` al cargar la partida, permiten plantar y construir con Robin, y persisten en el XML del guardado.
- [x] **TASK-202: Reubicación de `Cabin` por hueco de granja (`CabinVaultManager.cs`)**
  - **Prioridad:** Crítica
  - **Descripción:** Al reclamar una puerta, mover o vincular la `Cabin` asociada al `Farmhand` dentro de su ubicación `Pelican4Ever_Farm_0X` en las coordenadas de la casa principal (`64, 14` de la plantilla Estándar).
  - **Criterios de Aceptación:** El buzón, las mejoras de casa y el punto de aparición del jugador operan dentro de su hueco asignado.
- [x] **TASK-203: Catálogo de puertas en Pelican Town (`assets/doors.json`) y enrutado bidireccional**
  - **Prioridad:** Alta
  - **Descripción:**
    - Definir en `assets/doors.json` las 4 puertas reclamables de `Town` y sus coordenadas de llegada.
    - Al interactuar con una puerta en `Town`, mostrar diálogo para reclamar puerta libre, entrar a la granja propia o visitar la granja de un vecino autorizado (validado por el Host en `ServerWorldState.Doors`).
    - Redirigir las salidas de `Pelican4Ever_Farm_0X` de vuelta a la puerta correspondiente en `Town`.
  - **Criterios de Aceptación:** Cada uno de los 4 jugadores entra y sale de su granja privada desde su puerta de Pelican Town.
- [x] **TASK-204: Políticas de visita (`Locked`, `Friends`, `Public`) y protección anti-griefing**
  - **Prioridad:** Media
  - **Descripción:**
    - Permitir al propietario cambiar la política de acceso desde su puerta (enviando petición al Host, que actualiza `ServerWorldState.Doors` y replica en `location.modData`).
    - En granjas donde `GetFarmOwnerId() != Game1.player.UniqueMultiplayerID`, bloquear picos, hachas, azadas sobre cultivos, bombas y apertura de cofres, permitiendo usar la regadera.
  - **Criterios de Aceptación:** Un visitante no puede entrar si la puerta está en `Locked` ni destruir objetos o abrir cofres en granjas ajenas.

---

### Iteración 3: Economía, Presupuesto de Saciedad y Minas Persistentes (Épica 3)
> **Objetivo:** Equilibrar el gasto diario de energía con la comida, el Spa, los precios y los turnos de 12 horas en las minas.

- [x] **TASK-301: Presupuesto de Saciedad en Comida, Platos Cocinados y Tope del Spa (`EconomyModule.cs`)**
  - **Prioridad:** Alta
  - **Descripción:**
    - Parche `Prefix`/`Postfix` en `Farmer.doneEating`: recortar la ganancia instantánea de energía usando `SatietyBudget.AllowedGain` (hasta el 50% de `MaxStamina` por día real), conservando siempre la curación de `Health`, los `Buffs` y los ítems especiales (`Stardrop`).
    - Si se consume un plato cocinado y quedan usos en `MaxDailyWellFedBuffs` (`2`), activar regeneración pasiva (`+1 Energía / 60s reales` durante 60 min).
    - Limitar la regeneración del Spa a `1 punto / 30s reales` con tope diario `SpaDailyCap` (`60`) mediante `SatietyBudget.AllowedSpaGain`.
  - **Criterios de Aceptación:** La comida restaura energía hasta agotar el presupuesto diario de Saciedad; a partir de ahí sigue curando vida y dando buffs sin sumar energía instantánea.
- [x] **TASK-302: Minas Persistentes por Turnos de 12 Horas (`06:00` y `18:00`)**
  - **Prioridad:** Alta
  - **Descripción:**
    - Parche `Prefix` en `MineShaft.clearInactiveMines` (llamado cada 10 minutos desde `MineShaft.UpdateMines10Minutes`) que impide descargar niveles vacíos durante el turno.
    - A las `06:00` y `18:00` reales (`Config.ParsedMineResetTimes`), limpiar los niveles de `MineShaft.activeMines` y `VolcanoDungeon` en los que no haya jugadores dentro y mostrar aviso en el HUD.
  - **Criterios de Aceptación:** Los pisos minados permanecen vacíos de rocas durante todo el turno de 12 horas aunque todos los jugadores salgan de la mina.
- [x] **TASK-303: Multiplicadores de precios configurables por categoría (`PriceMultiplierPatcher.cs`)**
  - **Prioridad:** Media
  - **Descripción:** Si `CropPriceMultiplier != 1.0f` o `ArtisanPriceMultiplier != 1.0f`, aplicar el multiplicador en `StardewValley.Object.sellToStorePrice` según `Category` (`Vegetable`/`Fruit`/`Flower` vs. `ArtisanGoods`).
  - **Criterios de Aceptación:** El precio de venta en tiendas y `Shipping Bin` refleja los multiplicadores de `config.json`.

---

### Iteración 4: Contenedor Dedicado y Vida Urbana (Épica 4 / Post-MVP)
> **Objetivo:** Desplegar el servidor 24/7 en Docker y añadir mejoras de inmersión para NPCs y festivales.

- [x] **TASK-401: Integración con Host Dedicado Nativo (`Game1.HasDedicatedHost`) y Docker Compose**
  - **Prioridad:** Alta
  - **Descripción:**
    - Activar `Game1.player.team.hasDedicatedHost.Value = true` cuando `Config.DedicatedServerMode == true`.
    - Crear `docker-compose.yml` y scripts de arranque/backup sobre `JunimoServer` con volúmenes persistentes para `Saves/`, `Mods/Pelican4Ever/config.json` y copias de seguridad diarias.
  - **Criterios de Aceptación:** El contenedor levanta la partida 24/7 en Linux y acepta a los 4 clientes del MVP.
- [x] **TASK-402: Controlador de *Micro-Wandering* de NPCs (`NpcMicroWanderController.cs` / `WorldLifeModule.cs`)**
  - **Prioridad:** Media (Post-MVP)
  - **Descripción:** Reducir la velocidad en ruta de los NPCs y hacer que deambulen cada 45–90 segundos reales en un radio $\pm 2$ alrededor de su casilla de destino sin pisar `Warps`.
- [x] **TASK-403: Reprogramación de Festivales y Ventana de Tarde/Noche (`18:00–23:50`)**
  - **Prioridad:** Media (Post-MVP)
  - **Descripción:** Comando `p4e_festival_move` sobre `Data/Festivals/FestivalDates`, ajuste de horario a `1800 2350` y diálogo al entrar a `Town` para permitir cruzar el pueblo sin bloquear a los demás jugadores.

---

## 🚀 3. Backlog de Expansión Post-MVP (Mundo Persistente Completo)

Tareas analizadas técnicamente contra el motor decompilado de **Stardew Valley 1.6.15** y desglosadas con criterios de aceptación exactos.

### Épica 5: Comunicación e Inmersión Comunitaria
> **Objetivo:** Facilitar la vida compartida en Pelican Town mediante interacción asíncrona y multicanal.

- [x] **TASK-501: Buzones Urbanos Interactivos en Fachadas (`TownMailboxInterceptor`)**
  * **Análisis Técnico:** En `GameLocation.performAction`, el caso `"Mailbox"` (línea 9953) solo se ejecuta si la ubicación es `Farm` o comprueba si pertenece al jugador. Al pulsar sobre el tile del buzón en `Town`, podemos registrar una acción de tile personalizada `"Pelican4Ever.TownMailbox <doorId>"` o interceptarla en `ButtonPressed`. Si la puerta pertenece a `Game1.player.UniqueMultiplayerID`, se llama a `GameLocation.mailbox()`, abriendo las cartas sin obligar a entrar a la granja. Si hay cartas pendientes (`Game1.mailbox.Count > 0`), se inyecta un `TemporaryAnimatedSprite` de banderita alzada sobre el buzón.
  * **Prioridad:** Alta
  * **Criterios de Aceptación:**
    1. Cada puerta asignada en `Town` cuenta con un buzón interactivo contiguo.
    2. Al interactuar con el buzón de la residencia propia, se abre la interfaz nativa de lectura de correo (`LetterViewerMenu`).
    3. Si el buzón pertenece a otro jugador, muestra el mensaje estándar: `"Buzón de [NombreVecino]"`.
    4. El sprite exterior refleja si hay correo por leer.

- [x] **TASK-502: Chat Multicanal y de Proximidad (`ChatChannelModule.cs`)**
  * **Análisis Técnico:** `Multiplayer.sendChatMessage` (línea 1127) envía el mensaje 10 con `recipientID`. Si `recipientID == AllPlayers (-1)`, es global. Al interceptar `ChatBox.textBoxEnter` (línea 130), podemos capturar prefijos:
    - `/l` o `/local`: Se envía un mensaje SMAPI o mensaje filtrado a los jugadores cuya `Farmer.currentLocation.NameOrUniqueName == Game1.player.currentLocation.NameOrUniqueName`.
    - `/g` o `/global`: Transmisión global estándar.
    - `/w <jugador> <mensaje>`: Susurro privado aprovechando el `chatKind = 3` nativo de `ChatBox.receiveChatMessage`.
  * **Prioridad:** Alta
  * **Criterios de Aceptación:**
    1. Escribir `/l <texto>` envía el mensaje únicamente a los jugadores en el mismo mapa o a menos de 30 casillas de distancia, con color diferenciado (ej. verde claro).
    2. Escribir `/w <nombre> <texto>` envía un mensaje privado directo que solo ven el emisor y el receptor.
    3. Escribir texto estándar o `/g <texto>` lo transmite al canal global.

- [x] **TASK-503: Tablón de Mercado Vecinal Asíncrono (`CommunityMarketModule.cs`)**
  * **Análisis Técnico:** En un servidor 24/7, los jugadores tienen horarios distintos. Usando un cofre especial registrado en el Host o un menú `ItemGrabMenu` personalizado colocado frente a la tienda de Pierre, se serializa en `ServerWorldState.MarketListings` una lista de ofertas `(ListingId, SellerUid, ItemData, PriceGold)`. Cuando un comprador abona el oro, el Host retira el ítem, lo transfiere al inventario del comprador y deposita el oro directamente en la cartera personal (`Farmer.Money`) del vendedor mediante `GetPlayer(sellerUid)`.
  * **Prioridad:** Media
  * **Criterios de Aceptación:**
    1. Interacción con el tablón de Pierre abre el menú del "Mercado Vecinal".
    2. Cualquier jugador puede poner a la venta un ítem de su inventario fijando un precio en oro.
    3. Los compradores autorizados pueden adquirir el producto descontando su oro personal y recibiendo el ítem al instante, incluso si el vendedor está desconectado.

---

### Épica 6: Escalabilidad Urbana y Variedad de Granjas (8–16 Jugadores)
> **Objetivo:** Ampliar la capacidad residencial del servidor sin disparar el uso de red ni saturar el pueblo.

- [x] **TASK-601: Expansión del Catálogo a 16 Residencias Urbanas y Rurales**
  * **Análisis Técnico:** Extender `assets/doors.json` para mapear 12 puertas adicionales en coordenadas transitables que no bloqueen rutas de NPCs:
    - 4 en `Town` (fachadas este y sur adicionales).
    - 4 en `BusStop` y alrededores de la carretera.
    - 4 en `Mountain` (cerca del lago y la carpintería de Robin) y `Forest`.
  * **Prioridad:** Alta
  * **Criterios de Aceptación:**
    1. El archivo `assets/doors.json` contiene 16 entradas no colisionantes con objetos ni NPCs.
    2. `FarmSlotCount` configurable hasta 16 en `ServerConfig`.
    3. El comando `p4e_status` reporta el estado y ocupación de las 16 residencias.

- [x] **TASK-602: Elección de Plantilla de Mapa al Reclamar Granja**
  * **Análisis Técnico:** Actualmente `CreateLocationData` en `FarmSlotModule` fija `"Maps\\Farm"`. Ampliar la definición para permitir que al reclamar la puerta por primera vez, el jugador elija entre: Standard, Forest, Hilltop, Wilderness, FourCorners, Beach, Meadowlands o Riverland. El mod inyecta la plantilla correspondiente (`"Maps\\Farm_Forest"`, `"Maps\\Farm_Beach"`, etc.) y calcula el punto de spawn de la cabaña usando las coordenadas específicas de cada mapa (ej. Standard en 64,15; Beach en 72,18; etc.).
  * **Prioridad:** Media
  * **Criterios de Aceptación:**
    1. Al interactuar con una puerta libre, el diálogo muestra un submenú con los 8 tipos de granja.
    2. La ubicación `Pelican4Ever_Farm_XX` carga el mapa seleccionado por el jugador.
    3. La cabaña y buzón se posicionan en las coordenadas correctas del tipo de granja elegido.

- [x] **TASK-603: Culling Dinámico de Red para Granjas Inactivas (`LocationCullingModule.cs`)**
  * **Análisis Técnico:** En `Multiplayer.isAlwaysActiveLocation(loc)` (línea 1306), si devuelve `true`, todos los objetos, cultivos y animales de esa granja se transmiten a todos los clientes conectados. Para 16 granjas, esto generaría un consumo de ancho de banda innecesario. Parcheando `isAlwaysActiveLocation`: si la ubicación es `Pelican4Ever_Farm_XX` y ningún jugador está dentro de ella ni su dueño está online, devuelve `false`. En cuanto un jugador cruza el warp hacia esa granja, se marca temporalmente como activa.
  * **Prioridad:** Media
  * **Criterios de Aceptación:**
    1. Las granjas de jugadores desconectados que no tienen visitantes no sincronizan paquetes delta continuos.
    2. Al entrar un visitante a una granja inactiva, se sincroniza en caliente en menos de 500 ms sin pérdida de estado.

- [x] **TASK-604: Comportamiento propio del tipo de granja en cada residencia (niveles 1 y 2)**
  * **Análisis Técnico:** Stardew decide gran parte del comportamiento de una granja con un único tipo global (`Game1.whichFarm`), y solo aplica los datos `Farm_<tipo>` de `Data/Locations` a la ubicación llamada `Farm`. Sin replicarlo, una residencia fluvial pescaba solo la lista `Default` (basura). Implementado en `FarmSlotModule` (datos y mapa) y `FarmTypeModule` (comportamiento), con las reglas por tipo en `Pelican4Ever.Core/World/FarmTypeBehavior.cs`.
    - **Datos:** cada hueco copia de `Farm_Standard`, `Farm_Riverland`, `Farm_Forest`, `Farm_Hilltop`, `Farm_Wilderness`, `Farm_FourCorners` o `Farm_MeadowlandsFarm` los peces, zonas de pesca, forraje, excavaciones, malezas y arcilla. `Data/Locations` se reconstruye al cargar, al reclamar y al sincronizar con los clientes.
    - **Nivel 1 (propiedades de mapa que `Farm` ya lee por ubicación):** `SpawnMountainFarmOreRect` en Colinas (`5 37 22 8`, 28 días iniciales) y Cuatro Esquinas (`51 67 11 3`, 10 días); `SpawnForestFarmForage` en la forestal.
    - **Nivel 2 (parches Harmony):** monstruos nocturnos solo en residencias de combate (y en ellas aunque el equipo no los active); burbujas de peces en residencias fluviales; tocones forestales que reaparecen; tocón fijo y forraje del cuadrante de Cuatro Esquinas.
  * **Prioridad:** Media
  * **Criterios de Aceptación:**
    1. Una residencia fluvial pesca la tabla de la granja fluvial vanilla aunque la partida sea Estándar.
    2. Colinas y Cuatro Esquinas generan minerales a diario en su zona; la forestal genera su forraje y tocones.
    3. Los monstruos nocturnos aparecen solo en residencias de combate.
  * **Recomendación operativa:** crear la partida del servidor como **Estándar**. Con otro tipo global, el comportamiento de ese tipo se aplica también a todas las residencias (p. ej. Colinas intenta generar minerales en las 16).

- [x] **TASK-605: Diferencias restantes del tipo de granja global (nivel 3)**
  * **Análisis Técnico:** Replicadas las diferencias críticas de comportamiento por tipo de granja individual y resueltos los destinos de teletransporte:
    - **Destinos de teletransporte a la residencia:** Parches en `Object.totemWarpForReal` (Tótem de granja `(O)688`) e `IslandWest.performAction` (Obelisco de la Granja en Isla Jengibre) en `FarmSlotModule`. Si el jugador tiene residencia asignada, se le teletransporta directamente a su mapa `Pelican4Ever_Farm_XX` (según propiedad `WarpTotemEntry` o en el porche de su cabaña).
    - **Pradera (Meadowlands):** Al fundar la residencia se crea el gallinero (`Coop`) en `(54, 9)` con dos gallinas iniciales adoptadas y su perímetro de vallas con cancela (`FarmTypeModule.SpawnInitialMeadowlandsSetup`), complementado con la generación de hierba azul silvestre de TASK-606.
    - **Resolución/Descarte explícito de puntos secundarios:**
      - *Misiones:* Se mantiene la misión inicial estándar vanilla (la alternativa no aporta valor al modo persistente).
      - *Monstruos salvajes:* Los monstruos nocturnos ya operan en residencias de combate (TASK-604); la variante de proporción 0.66 vs 0.33 se descarta por irrelevante para el juego.
      - *Arbustos forestales y mapa playa:* Descartados; la granja playa no está en el catálogo de plantillas por no admitir aspersores en arena.
      - *Mobiliario:* La cabaña ya inicializa su mobiliario óptimo con `CleanAndArrangeResidenceInterior`.
      - *Lobby/MapArea:* Descartados por ser cosméticos sin impacto en la simulación 24/7.
  * **Prioridad:** Baja
  * **Criterios de Aceptación:**
    1. El tótem de granja y el obelisco llevan al jugador a su residencia asignada.
    2. Una residencia Pradera empieza con su gallinero, dos gallinas y genera hierba azul periódicamente.
    3. Cada punto restante queda resuelto o descartado explícitamente en esta tarea.

- [x] **TASK-606: Las residencias no reciben hierba nueva cada día**
  * **Análisis Técnico:** En el motor vanilla, `GameLocation.spawnWeeds` (llamado desde `Farm.DayUpdate`) solo siembra hierba nueva si `name.Value == "Farm"`, por lo que las residencias `Pelican4Ever_Farm_XX` omitían la generación diaria de hierba. Resuelto en `FarmTypeModule.Postfix_FarmDayUpdate` con `SpawnDailyGrass` y la lógica pura `FarmGrassLogic`:
    - Replica el algoritmo de `spawnWeeds` con los mismos intentos (`MinDailyWeeds`..`MaxDailyWeeds`, multiplicador de 15 en primavera día 1), comprobaciones de casillas excavables y probabilidad del 15%.
    - Soporte nativo para Pradera (`Meadowlands`): 10% de probabilidad de generar hierba azul (`Grass` tipo 7), determinado por el tipo de granja de la residencia y no por el tipo global del servidor.
  * **Prioridad:** Media
  * **Criterios de Aceptación:**
    1. Una residencia sin intervención del jugador gana matas de hierba nuevas a diario en proporción similar a la granja host.
    2. Las residencias de Pradera generan hierba azul silvestre de forma periódica.

---

### Épica 7: Minijuegos de Festivales y Eventos Comunitarios Asíncronos
> **Objetivo:** Participación individual o grupal flexible en festivales sin congelar el servidor.

- [x] **TASK-701: Minijuegos de Festival Asíncronos Desacoplados**
  * **Análisis Técnico:** En festivales como la Fiesta del Huevo (`spring13`) o el Festival de Hielo (`winter08`), el minijuego activa un temporizador individual (`Event.drawFestivalScore`). Cada cliente ejecuta su minijuego localmente y al terminar (`Event.EndPlayerControlSequence`), envía su puntuación al Host mediante `FestivalMinigameModule.SubmitScore`.
    - `Event.DefaultCommands.AwardFestivalPrize` suprime los premios inmediatos por tanda individual (`CurrentCommand += 2`), consolidando la entrega oficial al campeón de la jornada a las 23:50 vía `ResolveDailyFestivalAwards` (Sombrero de Paja / Ticket de Premio en Fiesta del Huevo; Gorra de Marinero + Aparejos / Ticket de Premio en Festival de Hielo). Si el ganador está desconectado, se deposita en su inventario o en `mailForTomorrow`.
    - La clasificación comunitaria puede consultarse en cualquier momento hablando con Lewis («Ver clasificación de hoy»), pulsando sobre el tablón de anuncios (`Billboard`) en el mapa festivo (se abre como `LetterViewerMenu`) o con el comando `/festival`.
  * **Prioridad:** Media
  * **Criterios de Aceptación:**
    1. Un jugador puede realizar la búsqueda de huevos a las 19:00 y otro a las 22:00 en tandas independientes.
    2. Las puntuaciones individuales se consolidan en una tabla comunitaria visible en el tablón del evento y mediante Lewis.
    3. El ganador de la jornada recibe el premio oficial automáticamente al finalizar la ventana del festival (23:50).
  * **Revisión del 2026-10-31 (sobre `14b90f5`).** Puntuaciones, clasificación y supresión del premio por tanda están bien (`CurrentCommand += 2` es lo mismo que hace el original con un no ganador). Falla la entrega del premio, criterio 3:
    - **Ganador conectado:** el host llama a `recipient.addItemByMenuIfNecessary(prize)` sobre un jugador remoto. Si su inventario está lleno, el menú de recogida se abre en la pantalla del host, y añadir objetos al inventario de otro jugador desde el host no es seguro porque ese inventario lo controla su cliente. Hay que mandar un mensaje del mod al cliente del ganador para que reciba el premio en local.
    - **Ganador desconectado:** se añade a `mailForTomorrow` un texto `%item … %%…`, pero `mailForTomorrow` solo admite IDs de carta definidos en `Data/mail`, así que no le llega nada. Hay que dejar el premio pendiente en `PlayerRecord` y entregarlo al conectarse (como `PendingMarketGold`), o definir la carta en `Data/mail`.
  * **Corregido el 2026-10-31:**
    - El host deja el premio pendiente en `PlayerRecord.PendingFestivalPrizes` (clave `<festival>@<día>`) y lo envía al cliente del ganador si está conectado, o al conectarse (`OnPeerConnected`).
    - El cliente lo entrega en local, sin menús abiertos: marca de primera victoria en su `mailReceived` y objetos con `addItemsByMenuIfNecessary`. Después confirma al host, que borra el pendiente.
    - Las claves ya entregadas se guardan en `Farmer.modData` para no repetir el premio si el aviso llega dos veces.
    - Objetos y marcas en `FestivalPrizeLogic` (Core, con tests). Sin verificar en partida.

- [x] **TASK-702: Ceremonias Comunitarias Programadas en Vivo**
  * **Análisis Técnico:** Para eventos comunitarios sin minijuego (Luau `summer11`, Danza de las Medusas `summer28`):
    - A las 20:45, el Host emite un único aviso por el chat anunciando que la ceremonia comenzará en 15 minutos en la playa (`CeremonyLogic.IsWarningWindow` + `IsCeremonyFestival`).
    - A las 21:00, el Host detecta a todos los vecinos presentes en la playa (`CeremonyLogic.IsPlayerInCeremonyLocation`) y arranca automáticamente una tanda sin inscripción previa mediante `FestivalParticipationModule.StartCeremonyRun`.
    - Los jugadores ausentes (en minas, granjas u otros mapas) continúan jugando sin congelaciones de pantalla, tirones ni teletransporte forzoso.
    - El diálogo de Lewis en estos festivales informa del inicio a las 21:00 y permite al Host adelantar el comienzo si los vecinos ya están reunidos.
  * **Prioridad:** Baja
  * **Criterios de Aceptación:**
    1. Las cinemáticas grupales solo arrastran a los jugadores que se encuentran dentro del mapa del evento a la hora señalada.
    2. Los jugadores ausentes no sufren congelación de pantalla ni teletransporte forzoso.
  * **Revisión del 2026-10-31 (sobre `14b90f5`).** El aviso único desde el host y el arranque a las 21:00 están implementados, pero la detección de asistentes no encaja con el juego:
    - Se busca a los jugadores con `currentLocation.Name == "Beach"`. Durante un festival, `Event.ChangeToTemporaryMap` mete al jugador en una ubicación temporal llamada `"Temp"`, así que el host presente en la ceremonia nunca cuenta como asistente.
    - Para los jugadores remotos, la ubicación que ve el host mientras están en el mapa temporal no está verificada.

    Propuesta: que cada cliente avise al host al entrar y al salir del festival (en `FestivalAccessModule`, donde ya se sabe), y que la ceremonia use esa lista. Sin verificar en partida.
  * **Corregido el 2026-10-31:**
    - Cada cliente avisa al host al empezar su evento de festival (`Postfix_TryToLoadFestival`) y al terminarlo (salida o final del evento principal).
    - El host lleva la cuenta en `FestivalPresence` (Core, con tests) y la limpia al desconectarse el jugador o al cambiar de día.
    - La ceremonia de las 21:00 y la opción de Lewis usan esa lista en lugar de `currentLocation.Name`. Sin verificar en partida.

- [x] **TASK-703: Entrada y salida libre de festivales sin salto de cámara (`FestivalAccessModule.cs`)**
  * **Análisis Técnico:** En multijugador vanilla, entrar en el mapa del festival abre `ReadyCheckDialog("festivalStart")` para todos, y el guion `set-up` de `Data/Festivals/<fecha>` fija la cámara, teletransporta al jugador a una casilla fija (p. ej. 12,54 en la Fiesta del Huevo), oculta la vista con `viewport -1000 -1000` y la devuelve con `globalFadeToClear`. Salir abre `ReadyCheckDialog("festivalEnd")` y `Event.exitEvent` termina el festival para el servidor: hora a las 22:00 (24:00 en `summer28`/`fall27`), máquinas adelantadas esas horas, aldeanos a casa (host), jugadores a la granja y mensaje `endFest` a todos los clientes. Cambios:
    - `Game1.warpFarmer`: se oculta `weatherIcon == 1` durante la llamada; el mapa del festival se entra como cualquier otro, sin confirmación ni bloqueo antes de la hora.
    - `Event.tryToLoadFestival`: `FestivalEntryScript.KeepPlayerInPlace` deja la cámara en `follow`, al jugador en su casilla de llegada y quita la vista oculta y el fundido. Si esa casilla está ocupada en el mapa temporal del festival, se usa la entrada vanilla.
    - `Event.TryStartEndFestivalDialogue`: al tocar el borde, sale por el warp más cercano del mapa real con `isFestival = false` antes de `endBehaviors`, sin `endFest`.
    - Si el festival abre con el jugador ya dentro del mapa, empieza en ese momento y en su sitio.
  * **Prioridad:** Alta
  * **Criterios de Aceptación:**
    1. Entrar al festival no mueve la cámara ni teletransporta al jugador.
    2. Un jugador puede salir y volver a entrar varias veces el mismo día; la hora del servidor no cambia y los demás siguen en el festival.
    3. Sin verificar en partida (pendiente de prueba con dos instancias).

- [x] **TASK-704: Evento principal del festival por inscripción, confirmado por el host (`FestivalParticipationModule.cs`)**
  * **Análisis Técnico:** El evento principal (búsqueda de huevos, Danza de las Flores, Luau, Danza de las Medusas, pesca en el hielo) arranca en multijugador cuando el host responde a Lewis. `Event.forceFestivalContinue` envía `festivalEvent` y arrastra a todos los que están en el festival. Después, las puertas `waitForOtherPlayers` (`startContest`, `endContest`, `festivalEnd`) esperan a **todos los jugadores conectados**, y la orden `end` cierra el festival para el servidor. Cambios:
    - **Inscripción:** cualquier jugador habla con Lewis en el recinto y se apunta o se borra sin quedarse bloqueado. El host recibe un aviso con cada inscripción.
    - **Arranque:** el host lo confirma hablando con Lewis («Empezar ahora») o con `/evento empezar` si no está en el recinto. Solo los apuntados entran en el evento principal; los demás siguen paseando por el festival. `/evento` muestra la lista de apuntados.
    - **Puertas:** en cada tanda se renombran a `<puerta>#<tanda>` y se limitan a sus participantes con `ReadySynchronizer.SetLocalRequiredFarmers`. Las puertas solo se reinician al cambiar de día, así que una tanda posterior heredaría las anteriores ya abiertas.
    - **Final:** la orden `end` deja al participante en el recinto, en su sitio, sin cerrar el festival para el servidor.
    - Cada jugador participa en una sola tanda por festival y día, para no repetir premios. La Feria (`fall16`, juicio de granjas) sigue siendo vanilla.
    - Inscripciones en memoria del host (`FestivalSignUps`, en Core con tests); se pierden si el servidor se reinicia a mitad del festival.
  * **Prioridad:** Alta
  * **Criterios de Aceptación:**
    1. Con un jugador apuntado en el festival y otro en su granja, el host arranca el evento y el apuntado lo completa sin esperar al otro.
    2. Un jugador no apuntado que está en el recinto no entra en el evento principal.
    3. Sin verificar en partida (pendiente de prueba con dos o tres instancias).

---

### Épica 8: Panel de Administración Web y Telemetría (DevOps)
> **Objetivo:** Administración y monitorización del servidor 24/7 sin comandos de consola.

- [x] **TASK-801: API REST Ligera de Gestión Interna (`AdminApiModule.cs`)**
  * **Análisis Técnico:** Iniciar un `HttpListener` ligero en un puerto configurable (por defecto `8085`), autenticado mediante cabecera `Authorization: Bearer <token_en_config>`.
    - `GET /api/status`: Retorna JSON con hora real, hora de juego, total de días, jugadores online/offline y estado de granjas.
    - `POST /api/rollover`: Invoca `DailyRolloverModule.ExecuteRollover()`.
    - `POST /api/festival/reschedule`: Modifica y refresca fechas de festivales.
    - `POST /api/mines/reset`: Fuerza la regeneración del turno minero.
  * **Prioridad:** Alta
  * **Criterios de Aceptación:**
    1. Endpoint protegido por token que responde con JSON estructurado.
    2. Comandos críticos de administración ejecutables vía curl o panel web.

- [x] **TASK-802: Panel Web Dashboard Integrado (Frontend Ligero)**
  * **Análisis Técnico:** Servir una SPA mínima en HTML5/Vanilla JS o React estático desde el propio servidor o empaquetada en el contenedor de Docker Compose, conectada a los endpoints de la API REST.
  * **Prioridad:** Media
  * **Criterios de Aceptación:**
    1. Interfaz accesible por navegador que muestra el mapa visual de las 4–16 puertas y sus propietarios.
    2. Visualización de jugadores conectados, su energía actual y su estado de descanso.
    3. Botones rápidos para lanzar Rollover o limpiar minas con un solo clic.

- [x] **TASK-803: API de anfitrión v1 y registro de sucesos (para el anfitrión agente de Stardew Voyager)**
  * **Contexto:** Un agente de OpenClaw mantendrá online la instancia host de Pelican 4Ever y la jugará con Stardew Voyager (plan `docs/planes/pelican-anfitrion-20261031.md` y tareas SV-122 a SV-131 de ese proyecto). Los deberes del anfitrión (festivales, ceremonias, anuncios, supervisión del rollover) los hará por esta API, no con el control físico del jugador.
  * **Análisis Técnico:**
    - **Endurecer la API actual antes de ampliarla** (revisión del 2026-10-31 de `AdminApiModule.cs`):
      - escucha en todas las interfaces (`http://*:{port}/`) y responde con CORS `*`;
      - `GET /` sirve el panel con el token incrustado (`AdminApiLogic.GetDashboardHtml(token)`) **sin pedir autenticación**;
      - el token por defecto (`pelican4ever_secret_admin_token`) está en el código;
      - cada petición se atiende en un hilo del `ThreadPool` y las acciones tocan el juego desde ahí (`/api/rollover` llama a `Game1.PassOutNewDay()` fuera del hilo del juego).

      Cambios: escuchar solo en `127.0.0.1` salvo configuración explícita (`AdminApiBindAllInterfaces`), exigir el token también para el panel (`AdminApiLogic.CheckAccess`), rechazar arrancar con el token por defecto (`AdminApiLogic.IsTokenConfiguredSecurely`) y ejecutar toda acción en una cola atendida en `UpdateTicked` (`MainThreadActionQueue`).
    - **Espacio versionado `/api/v1/host/`:**
      - `GET clock`: hora del juego y real, madrugada, próximo rollover, y resultado y copia del último.
      - `GET players`: conectados, ubicación, residencia, si están en el festival y su `PlayerRecord` (descanso y saciedad).
      - `GET festival`: festival de hoy, ventana, ubicación, si tiene evento principal, apuntados, tandas y quién ha jugado.
      - `POST festival/start`: equivale a `/evento empezar` (`FestivalParticipationModule`).
      - `POST festival/ceremony`: ceremonia de TASK-702.
      - `POST announce`: aviso por chat o en pantalla.
      - `POST rest`: descanso del host (TASK-804).
    - **Registro de sucesos `GET events?since=N`:** lista de solo añadir (`HostEventLog`), con inscripciones, tandas iniciadas y terminadas, rollover iniciado y completado, copia creada, y jugadores conectados y desconectados.
    - **Contrato** documentado en `docs/host-api.md` y probado: cada ruta con su método y su forma de respuesta, sin necesidad de juego (lógica de rutas y modelos en `Pelican4Ever.Core`).
  * **Prioridad:** Alta
  * **Criterios de Aceptación:**
    1. Sin token, `GET /` y cualquier `/api/` responden 401; con el token por defecto el módulo no arranca y lo registra.
    2. Toda acción de la API se ejecuta en el hilo del juego (verificado por test de la cola en Core y en partida).
    3. Las rutas de `/api/v1/host/` coinciden con `docs/host-api.md` (test de contrato).
    4. Un suceso aparece una sola vez en `events?since=N` y en orden (test).

- [x] **TASK-804: Modo «anfitrión agente»: descanso sin desconexión y reinicio controlado**
  * **Contexto:** Para el anfitrión agente de Stardew Voyager (ver TASK-803). Voyager no pausará ni recargará la partida del servidor; necesita descansar sin salir y, solo por causa mayor, un reinicio que no pille desprevenidos a los jugadores.
  * **Análisis Técnico:**
    - Opción `AgentHost` en `config.json` (`ServerConfig.AgentHost`).
    - **Descanso del host:** hoy `BedLogoutModule` registra el descanso y llama a `Game1.ExitToTitle()` para cualquier jugador, también el host, lo que tumba el servidor. Con `AgentHost`, el host registra `RestedOnGameDay` y `LastActiveGameDay` sin salir (`AgentHostLogic.ApplyRest`). El descanso también puede pedirse con `POST /api/v1/host/rest`.
    - **Reinicio controlado** (`p4e_safe_restart` y `POST /api/v1/host/restart` coordinados por `SafeRestartCoordinator`):
      1. Avisa a los jugadores con antelación configurable (`SafeRestartWarningMinutes`).
      2. Indica qué progreso se perdería, porque Stardew solo guarda en el cambio de día (`ProgressAtRiskDto`).
      3. Reinicia justo después del siguiente guardado salvo que se pida el inmediato (`WaitingForNextSave`).
      4. Registra el motivo en el registro de sucesos (`HostEventLog`).
  * **Prioridad:** Alta
  * **Criterios de Aceptación:**
    1. Con `AgentHost`, el host descansa en la cama, queda registrado y sigue conectado; los demás jugadores se desconectan como hoy.
    2. Sin `AgentHost`, el comportamiento actual no cambia.
    3. El reinicio controlado avisa a los conectados y, en modo por defecto, espera al siguiente guardado.
  * **Decisión pendiente:** qué progreso se acepta perder en un reinicio inmediato por causa mayor, y quién lo autoriza.

---

### Épica 9: Anexos de Residencia (Minimapas Adquiribles)
> **Objetivo:** Sustituir las salidas de las residencias que todavía llevan a los mapas vanilla (`Backwoods`, `BusStop`, `Forest`) por anexos privados que el jugador adquiere: mapas temáticos de un cuarto del tamaño de una granja, con las mismas bonificaciones de su tipo y una única entrada coherente con el lado desde el que se accede.

**Situación de partida (código actual):**
- Las residencias usan los mapas vanilla de granja (80×65), con sus salidas vanilla al norte (`Backwoods`), al este (`BusStop`), al sur (`Forest`) y a la cueva de la granja (`FarmCave`).
- `SpatialWarpRouter.ShouldReturnToHostFarm` convierte **una** de esas salidas en la vuelta a la Plaza Central, según el lado del portal del host. Las demás siguen llevando al mapa vanilla, que a su vez devuelve a la granja host `Farm`, no a la residencia.
- Para los portales del sur del host vuelve la de arriba (`Backwoods`); para los del norte, la de abajo (`Forest`); para los del oeste, la de la derecha (`BusStop`). Cada residencia tiene, por tanto, dos salidas vanilla libres: los huecos de anexo.
- Las bonificaciones por tipo ya existen para residencias (TASK-604): `FarmTypeBehaviors` en Core, propiedades de mapa y parches en `FarmTypeModule`, y la copia de `Farm_<tipo>` en `Data/Locations`.

- [x] **TASK-901: Modelo de anexos y catálogo de plantillas (Core)**
  * **Análisis Técnico:** Implementadas todas las reglas puras del sistema de anexos privados en `Pelican4Ever.Core`:
    - `AnnexTemplate` y `AnnexTemplateCatalog`: catálogo con las 7 plantillas temáticas (Estándar, Fluvial, Forestal, Colinas, Salvaje, Cuatro Esquinas y Pradera) con precio en oro, tamaño 40×32 y claves de datos de ubicación.
    - `AnnexAssignment` y `ServerWorldState.Annexes`: persistencia de asignación por residencia y lado cardinal (`North`, `East`, `South`), con migración de esquema v2 → v3 en `ServerWorldState.Migrate()`.
    - `ResidenceExits.FreeSides(x, y)`: determina con exactitud geométrica los dos lados libres de la residencia que no regresan a la Plaza Central para los 16 portales de `doors.json`.
    - `AnnexEntrance.For(side, w, h)`: calcula las coordenadas simétricas de entrada en el borde opuesto y la casilla de retorno hacia la residencia con orientación cardinal coherente.
  * **Prioridad:** Media
  * **Criterios de Aceptación:**
    1. Para cada uno de los 16 portales de `doors.json`, `FreeSides` devuelve exactamente los lados vanilla que no vuelven al host (cubierto con test sobre el catálogo real).
    2. `AnnexEntrance` coloca entrada y vuelta en lados coherentes para los tres lados (cubierto con tests unitarios).
    3. Un estado v2 migra a v3 sin perder residencias ni jugadores (cubierto con tests unitarios).

- [x] **TASK-902: Mapas temáticos de un cuarto de tamaño (40×32)**
  * **Análisis Técnico:** Implementada la generación de mapas temáticos de 40×32 para los 7 tipos del catálogo (Estándar, Fluvial, Forestal, Colinas, Salvaje, Cuatro Esquinas y Pradera) mediante `MapCloneHelper.CreateAnnexMap` y `AnnexTemplateCatalog`:
    - **Recorte y sellado (`MapCloneHelper.CreateAnnexMap`):** recorta el cuadrante 40×32 más representativo de cada mapa vanilla (`Maps/Farm`, `Maps/Farm_Fishing`, `Maps/Farm_Foraging`, `Maps/Farm_Mining`, `Maps/Farm_Combat`, `Maps/Farm_FourCorners`, `Maps/Farm_Ranching`), elimina la propiedad `Warp` y acciones `TouchAction`/`Action` de salida vanilla, y sella los cuatro bordes con `Buildings` (`Passable = F`) dejando únicamente la apertura de 2 casillas y su corredor de entrada según el lado cardinal (`AnnexSide`).
    - **Bonificaciones recalculadas para 40×32:**
      - `Fishing` (`Farm_Riverland`): conserva/asigna `FarmFishLocationOverride = "Town 0.7"` y garantiza poza/canal de agua pescable y orilla despejada;
      - `Mining` (`Farm_Hilltop`): zona de mineral recalculada a `SpawnMountainFarmOreRect = "5 13 22 8"` con suelo `Type = Dirt`;
      - `Foraging` (`Farm_Forest`): `SpawnForestFarmForage = "T"`, franja oeste `x < 18` con `Type = Grass` y marcadores de tocones renovables (`Paths` índice 21 en `(6, 8)` y `(6, 20)`);
      - `FourCorners`: zona de mineral recalculada a `(24, 22, 10, 5)` y tocón renovable en `(6, 6)`.
  * **Prioridad:** Media
  * **Criterios de Aceptación:**
    1. Cada anexo mide 40×32, es transitable y no tiene más salida que su entrada.
    2. La Fluvial tiene agua pescable con la tabla de `Farm_Riverland`; Colinas genera mineral dentro de su zona; la Forestal genera forraje y tocones.
    3. Ningún warp del mapa lleva a una ubicación vanilla.

- [x] **TASK-903: Ubicaciones de anexo y su ciclo de vida**
  * **Análisis Técnico:**
    - Entradas `Pelican4Ever_Annex_<NN>_<Lado>` en `Data/Locations`, inyectadas dinámicamente en `OnAssetRequested` y reconstruidas con `RefreshSlotLocationData` (clonando `Fish`, `FishAreas` sin restricciones de rectángulo 80×65, `Forage` y `ArtifactSpots` desde `LocationDataKey`).
    - Persistencia tras reinicio del servidor: `Prefix_SaveGameLoadDataToLocations` pre-registra las ubicaciones `Pelican4Ever_Annex_*` presentes en el guardado antes de que `SaveGame.loadDataToLocations` transfiera cultivos, objetos, cofres y máquinas, y `Prefix_SkipOnAnnexFarm` evita que `Farm.AddDefaultBuildings`, `UpdatePatio` o `addGrandpaCandles` alteren el anexo 40×32.
    - Mismas reglas de seguridad y red que una residencia: culling de red (`LocationCullingLogic.IsPrivateFarmOrAnnex` / `ShouldKeepActive` con `AnnexPrefix`), `PublishOwnership` y propagación en `HandleChangePolicy`, bloqueo de entrada si la residencia está `Locked` en `Prefix_WarpFarmer`, y parches anti-griefing de herramientas (`Prefix_ToolBeginUsing`) y cofres (`Prefix_ChestCheckForAction`).
    - En `ServerWorldState.NormalizeDoorAssignments`, al liberarse una residencia sus anexos asociados se liberan con ella para no dejar anexos huérfanos sin residencia activa (cubierto con tests unitarios).
  * **Prioridad:** Media
  * **Criterios de Aceptación:**
    1. Un anexo guarda y recarga cultivos, objetos y máquinas tras reiniciar el servidor.
    2. Un visitante sin permiso no puede usar herramientas ni cofres en el anexo.
    3. Un anexo sin jugadores dentro ni propietario conectado no se sincroniza por red.

- [x] **TASK-904: Sustituir las salidas vanilla de las residencias**
  * **Análisis Técnico:** En `FarmSlotModule.Prefix_WarpFarmer`, las salidas de una residencia privada que no vuelven a la Plaza Central dejan de llevar a los mapas vanilla (`Backwoods`, `BusStop`, `Forest`):
    - Si el propietario tiene anexo adquirido en ese lado cardinal (`worldState.FindAnnex`), el warp se redirige automáticamente a la entrada del anexo (`locationRequest = Game1.getLocationRequest(annex.AnnexId)`) con las coordenadas y dirección de llegada de `AnnexEntrance.For`.
    - Si aún no tiene anexo adquirido, el warp vanilla se bloquea (`return false`), el jugador se detiene inmediatamente y, si es el propietario, se abre el diálogo interactivo de adquisición (`ShowBuyAnnexDialog`).
    - Al salir del anexo, `Prefix_WarpFarmer` y los warps en los tiles de la apertura devuelven al jugador a la residencia madre en la casilla exacta de retorno (`ResidenceReturnTile`) con orientación cardinal coherente (`ResidenceReturnFacingDirection`).
    - La salida perimetral que regresa a la Plaza Central del host (`SpatialWarpRouter.ShouldReturnToHostFarm`) se evalúa primero y permanece intacta.
  * **Prioridad:** Media
  * **Criterios de Aceptación:**
    1. Desde ninguna residencia se llega a `Backwoods`, `BusStop` ni `Forest`.
    2. Entrar y salir de un anexo deja al jugador en el lado coherente de cada mapa.
    3. La salida que vuelve a la Plaza Central no cambia.

- [x] **TASK-905: Adquirir un anexo**
  * **Análisis Técnico:**
    - Diálogo interactivo `ShowBuyAnnexDialog` al intentar cruzar una salida libre sin anexo o al interactuar con ella mediante botón de acción [E] o clic del cursor.
    - Menú dinámico que lista las 7 plantillas temáticas con sus precios en oro, validando fondos antes de confirmar la compra.
    - Lógica de validación pura en `Pelican4Ever.Core.World.AnnexPurchaseLogic` (testeada unitariamente): valida propiedad de la residencia, lado libre, ausencia de anexo previo, existencia de plantilla, saldo suficiente en monedero individual (`useSeparateWallets`) y límite configurable de anexos por residencia (`MaxAnnexesPerResidence` en `config.json`, por defecto 2).
    - Descuento del precio con `DailyRolloverModule.SetFarmerMoney`, provisión inmediata de la ubicación física con `EnsureAnnexLocation` y sincronización multijugador (`ModMessages.BuyAnnexRequest` y `BroadcastDoorsSync`) sin requerir reiniciar el servidor.
  * **Prioridad:** Baja
  * **Criterios de Aceptación:**
    1. Solo el propietario puede adquirir anexos de su residencia, como mucho uno por lado libre.
    2. Sin fondos, la compra se rechaza sin cobrar nada.
    3. Tras comprar, la salida lleva al anexo sin reiniciar.

- [x] **TASK-906: Bonificaciones del tipo en los anexos**
  * **Análisis Técnico:**
    - `FarmTypeModule.BehaviorOf` y la provisión en `Data/Locations` soportan ubicaciones de anexo (`LocationCullingLogic.AnnexPrefix`), aplicando la plantilla de cada anexo de manera independiente al tipo de la residencia madre (una residencia estándar puede tener un anexo fluvial o salvaje).
    - Anexo fluvial (`Fishing`): pesca la tabla fluvial `Farm_Riverland`, tiene agua pescable con `FarmFishLocationOverride = "Town 0.7"` y genera burbujas de peces mediante `FarmTypeModule.Postfix_LocationTenMinuteUpdate`.
    - Anexo minero (`Mining`): área de minerales recalculada en 40×32 (`5 13 22 8`) con generación diaria de menas mediante `doDailyMountainFarmUpdate`.
    - Anexo salvaje (`Combat`): monstruos nocturnos activos vía `NightMonsters = true` en `Prefix_SpawnMonsters` y `Postfix_FarmTenMinuteUpdate`, mientras la residencia madre permanece libre de monstruos.
  * **Prioridad:** Media
  * **Criterios de Aceptación:**
    1. Un anexo fluvial pesca la tabla fluvial y tiene burbujas de peces.
    2. Un anexo de Colinas genera mineral a diario dentro de su zona.
    3. Un anexo salvaje tiene monstruos nocturnos y la residencia estándar a la que pertenece no.
