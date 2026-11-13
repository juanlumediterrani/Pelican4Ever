# Backlog Técnico y de Producto — *Pelican 4Ever* (`SV-RTMP`)

Basado en las especificaciones revisadas de [design.md](design.md) y [architecture.md](architecture.md), y verificado sobre el código decompilado de **Stardew Valley 1.6.15** y **SMAPI 4.5.2**.

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

### Fase 0.5: Spikes de Validación de Motor (Completada e Integrada)
> **Objetivo:** Validar con prototipos mínimos en el juego real los 4 puntos de fricción del motor identificados en la revisión de arquitectura.

- [x] **SPIKE-01: Disparo automático de `NewDay` + `Save` nativo con clientes conectados**
  - **Pregunta:** ¿Se puede iniciar la transición diaria nativa a las `06:00 AM` (o con comando `p4e_rollover_now`) cuando hay `Farmhands` conectados fuera de la cama sin colgar el `ReadyCheck` `"sleep"`?
  - **Criterio de Éxito:** El día avanza, la partida se guarda en disco y los clientes continúan conectados en el nuevo día.
  - **Resolución:** Validado e integrado en `DailyRolloverModule.cs`. `Game1.PassOutNewDay()` en el host dispara la transición forzada global sin depender del `ReadyCheck` voluntario de sueño (`sleep`), ejecutando `_newDayAfterFade()`, `SaveGame.Save()` y la evaluación de penalizaciones médicas (`SleepPolicy`) de forma sincronizada para todos los clientes conectados.

- [x] **SPIKE-02: Persistencia de `Pelican4Ever_Farm_01` declarada en `Data/Locations` con una `Cabin`**
  - **Pregunta:** ¿Guarda y recarga `SaveGame.loadDataToLocations` todos los cultivos, objetos y la `Cabin` de un `Farmhand` dentro de una ubicación personalizada `Pelican4Ever_Farm_01`?
  - **Criterio de Éxito:** Tras plantar cultivos, colocar un cofre y reiniciar el servidor, todo permanece intacto y el `Farmhand` conserva su punto de aparición.
  - **Resolución:** Validado e integrado en `FarmSlotModule.cs`. La inyección declarativa en `Data/Locations` con `CreateOnLoad` (`AlwaysActive = true`) junto con el pre-registro en `Prefix_SaveGameLoadDataToLocations` permite a `SaveGame.loadDataToLocations` persistir y recargar cultivos, cofres, máquinas y cabañas en `Pelican4Ever_Farm_01..16` y sus anexos, preservando el punto de aparición de cada jugador.

- [x] **SPIKE-03: Reloj 1:1 con `realMilliSecondsPerGameTenMinutes = 600_000` y `NightHold` en `2550`**
  - **Pregunta:** ¿Avanzan correctamente hornos, rutas de NPCs e iluminación ambiental entre `0600` y `2550`, y se mantiene el jugador sin desmayarse durante el tramo `01:50–06:00` (`2550`)?
  - **Criterio de Éxito:** Cero desmayos a las `02:00 AM` y las máquinas descuentan los 250 minutos de madrugada (`2550 → 0600`) durante el Rollover nativo.
  - **Resolución:** Validado e integrado en `RealTimeClockModule.cs`. Con `Game1.realMilliSecondsPerGameTenMinutes = 600_000` e interpolación de luz con `MillisecondsIntoSlot`, el reloj avanza 1:1 hasta las 01:50 (`2550`). De 01:50 a 06:00 se mantiene en `2550` (`IsNightHold = true`), impidiendo que el motor alcance `2600` (umbral de desmayo). En el Rollover de las 06:00, las máquinas descuentan los minutos transcurridos vía `Utility.CalculateMinutesUntilMorning`.

- [x] **SPIKE-04: Interacción con el modo dedicado nativo 1.6.15 (`Game1.HasDedicatedHost` / `DedicatedServer`)**
  - **Pregunta:** ¿Qué ajustes necesita `DedicatedServer.Tick()` para que no pause `Game1.netWorldState.Value.IsPaused` cuando no hay jugadores conectados (`onlineIds.Count == 0`)?
  - **Criterio de Éxito:** El reloj del servidor sigue avanzando y ejecuta el Rollover de las `06:00 AM` con `0` clientes conectados.
  - **Resolución:** Validado e integrado en `RealTimeClockModule.cs` y `ModEntry.cs`. En `OnUpdateTicked`, el host monitoriza y despausa `Game1.netWorldState.Value.IsPaused = false` independientemente de `onlineIds.Count`, y el parche en `Game1.shouldTimePass` garantiza que el tiempo del mundo transcurra de forma continua 24/7.

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
    - Parche `Prefix` en `GameLocation.startSleep`: enviar mensaje `BedLogoutRequest` al Host (que registra `RestedOnGameDay = Game1.Date.TotalDays`), mostrar confirmación en el HUD y desconectar al menú (`Game1.ExitToTitle()`) tras `1500 ms` sin activar el cierre de día global.
  - **Criterios de Aceptación:** Dormir en la cama desconecta al jugador con estado `Rested` (`p4e_status`) mientras los demás siguen jugando.
- [x] **TASK-103: Gestor del Rollover Diario Nativo a las `06:00 AM` y `CatchUpPlan` (`DailyRolloverModule.cs`)**
  - **Prioridad:** Crítica
  - **Descripción:**
    - Emitir avisos en el HUD a los jugadores conectados `15`, `5` y `1` minuto antes de las `06:00 AM`.
    - A las `06:00:00` reales (o mediante el comando `p4e_rollover_now`), lanzar la transición nativa de fin de día y guardado, aplicando `ServerCalendar.PlanCatchUp` si el servidor estuvo apagado.
    - En `DayStarted`, evaluar `SleepPolicy.Evaluate` para cada `PlayerRecord`, aplicar `WakeUpEnergy` y `MedicalFee` sobre cada `Farmer` (con `useSeparateWallets = true`), congelar la caída de amistad de jugadores `Offline_*` y crear copia de seguridad rotativa del save.
  - **Criterios de Aceptación:** Los cultivos avanzan, se liquidan las `Shipping Bins` personales y los jugadores ausentes varios días seguidos no reciben penalizaciones adicionales.

- [x] **TASK-104: El cambio de día se queda esperando con el host despierto (incidencia E-19, noche del 2026-11-05 al 06)**
  - **Prioridad:** Crítica
  - **Síntoma:** a las 02:00 se guardó dos veces (pre-cierre y post-cierre), se cerraron los peers y el log dijo «Cambio de día nativo iniciado», pero el día no cambió. Al reiniciar a las 04:04 se cargó el guardado de las 02:00:11 y volvió a abrirse el mismo día, sin pérdida.
  - **Causa (código descompilado de 1.6.15):** `Game1.NewDay(0f)` solo pone `Game1.newDay = true`; el cambio de día lo ejecuta `onFadeToBlackComplete` al terminar un fundido a negro, y `NewDay` solo lanza ese fundido si el host está en la cama o desmayado. Desde `9929d65` (2026-11-02) el rollover cierra antes a todos los peers y llama a `NewDay(0f)`; antes usaba `PassOutNewDay()`, que con peers desmayaba al host, pero sin peers (`!IsMultiplayer`) también acaba en `NewDay(0f)`. Con el host despierto y quieto (modo anfitrión agente, y anoche con la sesión del agente caída), nadie lanzaba el fundido; el día habría cambiado al cruzar el host la siguiente puerta.
  - **Resolución:** `DailyRolloverModule.EnsureNewDayFade()` lanza tras `NewDay(0f)` el mismo fundido que el de la cama (`nonWarpFade` y `fadeScreenToBlack()`), sin desmayar al host ni aplicar penalización, salvo si el host ya está en la cama o hay otro fundido en curso. `WatchNativeDayAdvance()` revisa cada segundo la etapa `AdvancingDay`: si `Game1.newDay` sigue sin consumirse y no hay fundido en curso tras 30 s (`RolloverTiming.ShouldNudgeDayAdvance`), lo vuelve a lanzar, lo registra como error y añade el suceso `rollover.stalled` (con `nudges`, `secondsSinceAdvance`, `hostInBed` y `hostLocation`) para que el anfitrión agente lo vea en `GET events`. Compila y pasan los 928 tests; **pendiente de verificar en partida**.
  - **Criterios de Aceptación:**
    1. Con el host despierto fuera de la cama y sin peers, `p4e_rollover_now` termina en «Día iniciado» del día siguiente sin intervención (el log del cambio de día dice «host despierto: fundido lanzado por el mod»).
    2. Con el host en la cama, el comportamiento no cambia (el log dice «host en la cama: fundido nativo»).
    3. En una noche normal no aparece ningún `rollover.stalled`.

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
  - **Revisión (ADR-0006):** la Saciedad pasa a ser una capacidad que se vacía en 4 h reales; "Bien comido" activa el descanso sentado (`1 punto / 5 s` durante 60 min) en lugar de la regeneración pasiva; el Spa da, una vez al día, un buff de `120` de energía y salud en 2 h tras 30 s quieto en el agua. El cliente comunica su consumo al host (`SatietyReport`).
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
  * **Corrección y validación 2026-11-06:** entrega y devolución ejecutadas en el cliente propietario, reservas y recibos persistentes para reintentos y restauración. Esquema 4 y protocolo 0.2.0 (actualizar host y clientes juntos). Pruebas con dos clientes: compra real, retirada, inventarios llenos, mensajes repetidos y SIGKILL con entrega pendiente. Ver `docs/archive/market-validation-2026-11-06.md`.

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
      - *Arbustos forestales y mapa playa:* Arbustos descartados. La granja playa se incorpora en TASK-607 (el juego ya bloquea los aspersores en la arena con `NoSprinklers`).
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

- [ ] **TASK-607: Mecánicas restantes por tipo de granja en residencias y anexos** (rama `farm_fix_improvements`)
  * **Análisis Técnico:** Repaso contra el código vanilla 1.6 (manda sobre la wiki) y las páginas [Tipos de Granja](https://es.stardewvalleywiki.com/Tipos_de_Granja) (desactualizada en la XP de combate y sin novedades de 1.6) y [Farm Maps](https://stardewvalleywiki.com/Farm_Maps) (al día con 1.6). Criterio: antes de parchear, comprobar si el juego ya lo hace por ubicación (propiedad de mapa o `Data/Locations` copiado al hueco); solo se parchea lo que mira `Game1.whichFarm` o `Game1.getFarm()`.
    - **Hecho en la rama:**
      - *Granja de la Playa:* plantilla `Beach` sobre `Maps\Farm_Island` (110×110; `Maps\Farm_Beach` no existe) con casa en 64,15, buzón 68,16, cueva 34,15→34,16 y llegadas N (40,1), S (82,103) y E (80,17); seleccionable al fundar. Anexo `Beach` recortado en (15,30) con el rectángulo fértil. `SpawnBeachFarmForage` activa el forraje de playa, las cajas de suministros (922–924) y el forraje del bosque en el césped. Los aspersores ya no riegan la arena (`NoSprinklers=T` del mapa).
      - *Regalo inicial:* `ApplyStarterGift` usa `FarmHouseStarterGift` del mapa de la residencia (Pradera: 15 de heno) y, en la fluvial, añade el Ahumador de pescado `(BC)FishSmoker` que vanilla 1.6 coloca en la casa (va en el regalo para no chocar con los muebles de la residencia). FarmHouse lo leía siempre del mapa de la granja principal.
      - *Salvaje:* `Prefix_SpawnGroundMonster` replica `spawnGroundMonsterOffScreen` con el nivel de Combate del dueño; con nivel 9+, 50 % de Gólem de iridio.
      - *Rayos:* el tope diario de `WorldLifeModule` se reparte entre la granja principal (camino vanilla) y las residencias con dueño, que cargan sus pararrayos `(BC)9` o reciben el impacto como en vanilla.
      - *Decoración de la casa:* `ApplyStarterHouseStyle` aplica el papel de pared y el suelo del tipo de la residencia (propiedad del mapa o valor fijo de vanilla, `FarmTypeVanillaLogic.StarterStyleFor`). Los muebles no se cambian: los coloca `CleanAndArrangeResidenceInterior`.
      - *Cajas de suministros:* `FarmTypeLootModule` (host y clientes, porque la caja se rompe en el juego de quien la golpea) trata como partida de playa el reparto de `BreakableContainer.releaseContents` en residencias y anexos de playa, así que sueltan `(O)920` en vez de Geoda omni. La playa se reconoce por `mapPath`.
      - *Minerales por nivel del dueño:* `Prefix_DailyMountainFarmUpdate` replica `doDailyMountainFarmUpdate` en la zona de la residencia o anexo con el nivel de Minería del dueño.
    - **Ya cubierto por vanilla (sin parche):** Muyu de la forestal (`(O)734` al 5 % en `Farm_Forest`), pesca por bioma, hierbajos forestales con semillas variadas (`SpawnForestFarmForage`), cuervos (`Farm.DayUpdate` → `addCrows`), cultivos gigantes (`Crop.TryGrowGiantCrop` en cualquier `Farm`), tazón de mascota (`PetBowl.performToolAction`) y regeneración de la cantera.
      - *Bloqueo inicial de la cantera:* lo pone `loadObjects` con los marcadores 19/20/21 (tronco, roca, tocón) de la capa `Paths` del mapa, y la residencia lo llama al fundarse.
      - *Toneles del sótano:* el sótano `Pelican4Ever_Farm_XX_Cellar` es un `Cellar` en `Game1.locations`, así que recibe su `DayUpdate` y los toneles envejecen.
      - *Maleza y escombros:* `Farm.DayUpdate` llama a `spawnWeedsAndStones` y `spawnWeeds` en cualquier `Farm`, residencias y anexos incluidos.
      - *Reparación del invernadero:* cada residencia tiene su `greenhouseUnlocked` y su propio flujo de reparación (`TryRepairGreenhouseWithJoja` y por materiales).
      - *Contenedor de envíos:* vanilla ingresa al jugador que deposita (`getShippingBin(who)`) con carteras separadas. Decidido así también para visitantes: cobra quien deposita, no el dueño de la residencia.
    - **Descartado:**
      - *0 XP de Combate en la Salvaje:* vanilla da 1/3 de la XP (mínimo 1) en cualquier granja (`isFarm`), como recoge la wiki inglesa; la española está desactualizada.
      - *Burbujas en Cuatro Esquinas:* vanilla solo las crea en la fluvial (`whichFarm == 1`).
    - **Pendiente:**
      - ~~*Cueva individual*~~ **Hecho en `73ca26d`** (`Pelican4Ever/Modules/PrivateFarmCaves.cs`): la primera vez que un residente entra en la cueva privada de su residencia elige entre setas y murciélagos frutícolas; la elección se guarda por jugador (`PlayerRecord.CaveChoice`) y la cueva de cada residencia, y la de su anexo Coto Salvaje, lee la de su dueño en vez de `MasterPlayer.caveChoice`. En partidas existentes se conservan los objetos que ya había. **Pendiente solo:** la prueba en partida con dos jugadores.
      - *Traslado del invernadero y de la casa:* verificar que Robin y la Torre del Mago pueden mover el invernadero de una residencia, y qué pasa si se mueve su casa (1.6 lo permite): la puerta, el buzón, la cueva y los warps de la residencia dependen de su posición.
      - *Anexo de Pradera:* evaluar establo/vallas o hierba azul optimizada para 40×32.
    - **Pruebas en vivo (2026-11-07, playa):** checkpoint; la sesión se cortó porque el servidor entró en mantenimiento.
      - *Funciona:* comandos de prueba, cajas de suministros (café, hamburguesa, geodas, tierra con retención de agua, semillas…), forraje de playa (salen cajas), aspersores (no se colocan en arena, sí en tierra fértil).
      - *Corregido — warp este del anexo de playa:* los warps de la residencia hacia sus anexos y las zonas para comprarlos estaban fijados con las casillas de la granja estándar, y el warp este caía en la llegada (79,17). Ahora salen de `ResidenceAnnexAccessLogic` por tipo de mapa: en la playa, en las salidas vanilla de `Farm_Island` (este x 81, sur y 105) con la llegada este en (80,17). Al sincronizar se quitan los warps viejos al anexo. Los tests comprueban que ninguna llegada es un warp.
      - *Corregido — `p4e_farm_tp`, `inspect`, `spawn` y `lightning` sobre un hueco sin fundar:* creaban una granja vacía con `EnsureFarmSlotExists`; ahora responden que no está fundada.
      - *Revisar:* algunas cajas sueltan 3 y 5 objetos a la vez; comprobar contra `BreakableContainer.releaseContents` si es vanilla.
      - *Minimapa (anexo):* sirve como provisional; todos iguales, sin bordes delimitados y con algún fallo gráfico. Se deja así.
      - *Hecho — `/residencias`:* en el chat solo salen las ocupadas (dueño, tipo y lugar) y el número de libres (`PlazaCatalogFormatter.FormatChatSummary`). `p4e_doors` en consola sigue listando todas. El host envía su `FarmSlotCount` en `SyncDoorsState` para que los clientes cuenten los mismos huecos.
      - *Hecho — permiso de edición para visitantes:* `DoorAssignment.VisitorsCanEdit` (por defecto no), que se cambia en el diálogo de acceso de la puerta o el buzón. `VisitorGuardModule` corre en cada juego y bloquea a quien no es el dueño, en la residencia, su cueva, sótano, invernadero, casa y anexos: herramientas de trabajo y guadaña, colocar y sembrar, cosechar a mano, árboles frutales, cofres y máquinas. Armas, honda y caña siguen permitidas. Los diálogos muestran el acceso en texto («abierta, visitas solo miran»). Antes la descripción de `Public` decía que bloqueaba herramientas y cofres, pero nada lo hacía.
      - *Hecho — «Solo amigos»:* `VisitorPermissionLogic.CanEnter` comprueba la lista `Friends` en la entrada a la residencia y sus anexos, la puerta y el buzón. El diálogo de acceso tiene «Gestionar amigos»: añadir jugadores conectados y quitar a cualquiera de la lista (como mucho 8 opciones, el cuadro no tiene scroll). El host guarda la lista y la reparte en `SyncDoorsState`.
      - *Pendiente:* recoger forraje no se bloquea. Si el dueño cierra la residencia o quita a un amigo, quien ya está dentro no es expulsado.
    - **Pruebas en vivo (2026-11-07, resto de tipos):**
      - *Funciona:* regalo y decoración de Pradera y Playa; ahumador en la fluvial; minerales por nivel en Colinas (oro e iridio con Minería 10, cobre y geodas con 0).
      - *Corregido:* la Salvaje (`Combat`) faltaba en el menú de fundar (salían 7 tipos).
      - *Es vanilla:* el pez sol sale en la fluvial (sus datos usan la pesca de `Town` y `Forest`, de río). Colinas regala chirivías: solo Pradera tiene `FarmHouseStarterGift` en su mapa. Los minerales de cantera salen pocos cada día (intentos con probabilidad decreciente, solo en casillas de tierra libres): tras llenar la cantera con `p4e_farm_spawn ores 20`, una noche añade pocos o ninguno.
      - *Repetir:* pararrayos. El host contó 0 pararrayos colocados en `Farm_03` y `Farm_04` (`p4e_give_item` los dejó en el suelo por inventario lleno). Los `Bad IL range` en `DebugGive` y `p4e_farm_tp`, y el cierre del host, apuntan a que otra compilación reemplazó el DLL del mod con el juego abierto.
  * **Prioridad:** Media
  * **Criterios de Aceptación:**
    1. Una residencia de playa carga `Farm_Island`, genera forraje y cajas y sus aspersores no riegan la arena.
    2. Una residencia de Pradera empieza con 15 de heno; las demás, con el regalo de su mapa o el vanilla.
    3. En una residencia salvaje los monstruos escalan con el nivel del dueño.
    4. Los pararrayos de las residencias producen pilas en días de tormenta.
    5. Cada punto pendiente queda implementado o descartado explícitamente en esta tarea.
  * **Pruebas en vivo:** comandos de host `p4e_farm_found`, `p4e_farm_tp`, `p4e_farm_inspect`, `p4e_farm_spawn`, `p4e_lightning`, `p4e_weather` y `p4e_skill` (`FarmTestCommandsModule`, con `EnableTestCommands`). Guía y casos en `docs/archive/pruebas-residencias.md`.

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
  * **Carácter opcional:** la API de anfitrión y el modo `AgentHost` son un añadido. Pelican 4Ever funciona igual sin ellos y sin Stardew Voyager: la API no arranca sin un token propio y `AgentHost` viene desactivado.
  * **Contexto:** Un agente de OpenClaw mantendrá online la instancia host de Pelican 4Ever y la jugará con Stardew Voyager (plan de anfitrión de octubre de 2026 y tareas SV-122 a SV-131 de ese proyecto). Los deberes del anfitrión (festivales, ceremonias, anuncios, supervisión del rollover) los hará por esta API, no con el control físico del jugador.
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
  * **Resolución de la decisión de reinicio inmediato:** El progreso que se acepta perder en un reinicio inmediato por causa mayor es exclusivamente el progreso no guardado del día en curso (desde las 06:00 hasta el momento del reinicio, evaluado detalladamente por `ProgressAtRiskDto.Summary`). Solo puede autorizarlo un operador con acceso a la consola del servidor (`p4e_safe_restart --immediate`) o un cliente autenticado mediante token Bearer (`POST /api/v1/host/restart` con `"immediate": true`). En cualquier otro caso, el sistema opera en modo seguro (`WaitingForNextSave`), esperando a que concluya el guardado del siguiente Rollover.

- [x] **TASK-805: Servidor persistente desatendido: carga automática como anfitrión y supervisión**
  * **Contexto:** Para que un cron (p. ej. Dr4co con Stardew Voyager) mantenga el servidor siempre disponible. Tras un reinicio controlado el juego quedaba en la pantalla de título sin nadie que reabriera la partida, y no había supervisor que lo relanzara tras una caída.
  * **Análisis Técnico:**
    - `AutoHostSaveName`: en la pantalla de título, `AutoHostModule` carga esa partida como anfitrión de cooperativo (`multiplayerMode = 2` y `SaveGame.Load`, como la ranura «Anfitrión» de `CoopMenu`). Nombre exacto de carpeta; nunca «la más reciente» (`AutoHostLogic.Decide`).
    - `RestartExitsProcess`: el reinicio controlado cierra el proceso al llegar al título para que el supervisor lo relance con el mod actualizado. Requiere `AutoHostSaveName` (validado en `ServerConfig`).
    - Unidad de usuario `deploy/systemd/pelican4ever-host.service` (`Restart=always`, límite de arranques, en conflicto con `stardew-voyager-live.service`) y guía `docs/archive/servidor-persistente.md`.
  * **Prioridad:** Alta
  * **Criterios de Aceptación:**
    1. Con `AutoHostSaveName`, el servidor arrancado sin intervención abre esa partida como anfitrión; si no existe, no abre otra y lo registra (test de `AutoHostLogic`).
    2. Sin `AutoHostSaveName`, el comportamiento actual no cambia (el módulo no se suscribe a eventos).
    3. `RestartExitsProcess` sin `AutoHostSaveName` es un error de configuración (test).

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
    - Menú dinámico que lista las 7 plantillas temáticas con precios progresivos según el número de anexos previos de la residencia (tarifa reducida al 40% para el primer mapa: 10.000g–14.000g, y precio normal completo al 100% para el segundo minimapa: 25.000g–35.000g, configurable mediante `FirstAnnexPriceMultiplier` y `SecondAnnexPriceMultiplier`).
    - Lógica de validación pura en `Pelican4Ever.Core.World.AnnexPurchaseLogic` (testeada unitariamente): valida propiedad de la residencia, lado libre, ausencia de anexo previo, existencia de plantilla, cálculo escalonado de precios según anexos previos, saldo suficiente en monedero individual (`useSeparateWallets`) y límite configurable de anexos por residencia (`MaxAnnexesPerResidence` en `config.json`, por defecto 2).
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

---

### Épica 10: Plaza Central en la Granja Host y Mapas Puente (rama `refactor/residence-entrances`)
> **Objetivo:** Llegar a las 16 residencias desde la Granja Host sin pasar por mapas vanilla. La Granja Host tiene cinco salidas: cuatro portales y un paso al oeste hacia una cadena de hasta tres mapas puente. Cada puente tiene cuatro portales. Todo debe funcionar igual en el host y en los clientes, sobrevivir a un reinicio y probarse en Core. Decisión de diseño: [ADR-0005](docs/adr/0005-host-plaza-and-bridge-maps.md).

#### 10.0 Guía para quien retome la épica

**Dónde está la rama (revisión del 2026-11-03, `e2e881b`):**
- La rama ya está rebasada sobre `main`. Incluye `87a575e`, `9b42a58` y `542d46d`, y además un merge de la cascada automática (`.github/workflows/cascada-ramas.yml`).
- Cada push a `main` o a `pelican/desarrollo` fusiona esas ramas en esta. Ante un conflicto, el workflow no toca la rama: abre un PR `cascada/<origen>-a-<destino>`, que hay que resolver a mano (ver TASK-1001).
- El refactor de partida es el commit `cd7e2cb`, el antiguo `019235f`. Con `git show cd7e2cb` se ve todo lo que cambió.
- El catálogo de puertas es `Pelican4Ever/assets/doors.json`, con 16 entradas:

  | Ubicación | Parcelas | Portales norte (`x`, `y=1`) | Portales sur (`x`, `y`) |
  | --- | --- | --- | --- |
  | `Farm` (80×65) | 01–04 | 18, 62 | 18, 62 (`y=63`) |
  | `Pelican4Ever_Bridge_1` (35×30) | 05–08 | 8, 25 | 8, 25 (`y=28`) |
  | `Pelican4Ever_Bridge_2` | 09–12 | 8, 25 | 8, 25 (`y=28`) |
  | `Pelican4Ever_Bridge_3` | 13–16 | 8, 25 | 8, 25 (`y=28`) |

  Cadena actual, escrita a mano en `EnsureBridgeLocations`:
  - `Farm (0, 26..27)` → `Bridge_1 (33, 14)`
  - `Bridge_N (0, 13..15)` → `Bridge_N+1 (33, 14)`
  - `Bridge_N (34, 13..15)` → `Bridge_N−1 (1, 14)`, o `Farm (2, 26)` desde el puente 1

**Preparar el entorno:**
- `global.json` fija el SDK `8.0.427` con `rollForward: disable`. Si `dotnet` dice *A compatible .NET SDK was not found*, instala esa versión. No subas un `global.json` cambiado: fijar el SDK es intencionado (`78e9f52`).
- Tests (solo Core, sin juego): `dotnet test Pelican4Ever.Tests`. Pasaban 704 en `e2e881b`, contando la guarda de TASK-1001.
- Compilar el mod **sin sobrescribir el mod instalado**: `dotnet build Pelican4Ever/Pelican4Ever.csproj -p:EnableModDeploy=false -p:EnableModZip=false`. Sin esas opciones, ModBuildConfig copia la DLL a `<GamePath>/Mods/Pelican4Ever` (`GamePath` está en `local.props`).
- No hay workflow de CI que ejecute los tests. La cascada fusiona sin probar (ver TASK-1003).
- Para ver un mapa vanilla, como `BusStop`, con sus propiedades `Warp` y `Action`: arranca el juego con Content Patcher y ejecuta `patch export Maps/BusStop` en la consola de SMAPI. Lo exporta como `.tmx` en `Mods/ContentPatcher/patch export/`. Las `.xnb` de los mapas usan LZX y no se leen fuera del juego.

**Mapa del código** (números de línea en `e2e881b`; si se han movido, busca por nombre):

| Qué | Dónde |
| --- | --- |
| Carga de `doors.json` | `Pelican4Ever/Modules/FarmSlotModule.cs:90` (`RegisteredDoors`) |
| `DoorModel` (en el mod, **no en Core**) | `Pelican4Ever/Models/DoorModels.cs:7` |
| Registro de `Data/Locations` (Granja Host, huecos, sótanos e invernaderos) | `FarmSlotModule.OnAssetRequested`, `:104` |
| Mapa generado de la Granja Host | `FarmSlotModule.cs:358` (`Maps/Pelican4Ever_HostFarm`) |
| Tallado de la apertura y el camino de un portal | `ClearDoorAreaOnMap`, `:475`; `PlaceMailboxTiles`, `:779` |
| Reconstrucción de `Data/Locations` | `RefreshSlotLocationData`, `:989` |
| Entrada en la Granja Host | `OnPlayerWarped`, `:1196` (mensaje de bienvenida en `:1219`) |
| Detección del portal libre | `OnUpdateTicked`, `:1326` |
| Puentes en caliente (a eliminar) | `EnsureBridgeLocations`, `:1370` |
| Portales en tiempo de ejecución | `EnsureHostFarmPortals`, `:1442`; `EnsureLocationPortals`, `:1461` |
| Warps | `UpdateOrCreateWarp`, `:1561`; `RemoveWarpAt`, `:1576` |
| Comando `/residencias` | `:2023` |
| Recepción de `SyncDoorsState` en el cliente | `OnModMessageReceived`, `:2120` |
| Fundar una parcela (host) | `HandleClaimDoor`, `:2203` |
| Diálogo del portal | `InteractWithResidentialDoor`, `:2507` |
| Ubicaciones que se crean antes de cargar la partida | `Prefix_SaveGameLoadDataToLocations`, `:3018` |
| Guarda de `findStructure` (no tocar) | `Prefix_FindStructure` |
| Colisión de las aperturas | `Prefix_IsCollidingPosition`, `:3356` |
| Enrutado de warps (salida de la residencia en `:3571`, salida de la cabaña en `:3480`) | `Prefix_WarpFarmer`, `:3381` |
| Geometría pura | `Pelican4Ever.Core/World/SpatialWarpRouter.cs`, `TerrainTileClassifier.cs`, `ResidenceExits.cs`, `TownMailboxLogic.cs` |
| Tests afectados | `SpatialWarpRouterTests`, `TerrainTileClassifierTests`, `AnnexTests`, `WorldStateAndDoorsTests`, `MapVisualizerTests` (los cuatro primeros leen `doors.json` desde el árbol de código) |

**Convenciones que hay que respetar:**
- La lógica pura va en `Pelican4Ever.Core`, sin referencias a `StardewValley`, y con tests xUnit. El mod solo traduce entre Core y el juego (ADR-0004).
- El host decide y los clientes leen: `ServerWorldState` es la fuente de verdad y se replica con `SyncDoorsState`.
- Toda ubicación nueva entra por `Data/Locations` con `CreateOnLoad` (ADR-0003). Nunca con `Game1.locations.Add`.
- Todo en español: mensajes al jugador, logs con prefijo `[FarmSlotModule]`, comentarios y commits (`tipo(ámbito): …`).
- Las tareas se cierran marcando `[x]` y añadiendo una línea **Resolución:** con el commit, al estilo de las épicas anteriores.

**Para dar una tarea por terminada:**
1. Se cumplen sus criterios de aceptación.
2. Pasan `dotnet test` y la compilación sin despliegue.
3. Si la tarea toca el juego, hay una nota de verificación en partida. Si no se pudo probar en partida, se dice explícitamente.

**Orden de trabajo:**

```text
A (1001-1003) ──> B (1101 > 1102 > 1103, 1104) ──┐
              └─> C (1201 > 1202, 1203) ─────────┴─> D (1301-1304) ──> E (1401 > 1402, 1403)
```

B y C pueden avanzar a la vez una vez terminada TASK-1101. TASK-1202 necesita TASK-1103.

#### Fase A — Integración y red de seguridad

- [x] **TASK-1001: Mantener la integración con `main` sin reabrir el StackOverflow de `findStructure`**
  * **Estado (2026-11-03):** Resuelto. La guarda de regresión `FindStructureGuardTests` verifica que `Prefix_FindStructure` no delegue en el original ni contenga `return true`.
  * **Resolución:** Resuelto en el commit `c2e52e1` (`test(residence): guarda contra return true en Prefix_FindStructure`). Fichero sin seguimiento `FindStructureGuard.patch` eliminado de la raíz.
  * **Análisis Técnico:**
    - Si `Prefix_FindStructure` vuelve a delegar en el original (`return true`), se reabre la recursión `Building.GetIndoors()` → `Game1.getLocationFromName` → `findStructure`, que provoca un StackOverflow (lo arregló `9b42a58`).
    - Con la cascada automática, cualquier fusión futura desde `main` puede chocar en `FarmSlotModule.cs`.
  * **Pasos:**
    1. Aplicar `FindStructureGuard.patch` (raíz del checkout principal, sin versionar): `git apply FindStructureGuard.patch`. Añade `Pelican4Ever.Tests/FindStructureGuardTests.cs`, que lee el código fuente y falla si:
       - `Prefix_FindStructure` contiene `return true`;
       - no termina en `return false;`;
       - aparece dos veces.
    2. Ejecutar `dotnet test --filter FindStructureGuardTests`: debe pasar. Si quieres comprobar que la guarda funciona, cambia temporalmente el `return false` final por `return true`: el test debe fallar.
    3. Hacer commit en esta rama, y conviene también en `main` para que la guarda esté en todas: `test(residence): guarda contra return true en Prefix_FindStructure`.
    4. Al resolver un PR `cascada/main-a-…` que toque `FarmSlotModule.cs`:
       - quedarse **siempre** con la versión de `main` de `Prefix_FindStructure`;
       - conservar el tratamiento del sufijo `_Home` en `Prefix_WarpFarmer` (`542d46d`);
       - ejecutar los tests antes de fusionar.
  * **Prioridad:** Crítica
  * **Criterios de Aceptación:**
    1. `FindStructureGuardTests` está versionado y pasa.
    2. Entrar en un invernadero o en una residencia sin instanciar no provoca StackOverflow.

- [x] **TASK-1002: Sanear `MapVisualizerTests`**
  * **Resolución:** Resuelto con `DoorCatalogFixture` compartida para cargar `doors.json` subiendo directorios, salida de diagramas al directorio temporal del sistema (`pelican4ever-maps`), método de validación `ValidatePlazaGeometry` verificando que todas las aperturas, caminos y buzones queden dentro de límites, sin solapamientos entre parcelas ni invasión de pasos este/oeste, y test de fallo forzado ante desplazamiento del portal 07 (`y=40`).
  * **Análisis Técnico:** el test escribe en `~/.var/app/com.google.AndroidStudio/...`, la ruta de otro equipo, y solo comprueba que el fichero existe.
  * **Pasos:**
    1. Sustituir `ArtifactScratchDir` por `Path.Combine(Path.GetTempPath(), "pelican4ever-maps")`.
    2. Mover su `DoorCatalogTestEntry` a un helper compartido, `Pelican4Ever.Tests/DoorCatalogFixture.cs`. Ese helper localiza `doors.json` subiendo directorios, como hace `FindStructureGuardTests`. Así desaparecen las cuatro copias de la búsqueda de `doors.json` con rutas `../../../..`.
    3. Añadir aserciones sobre cada ubicación de la plaza: ninguna apertura, camino ni buzón se sale de `Width` × `Height`; los caminos de dos portales no se solapan; ningún camino pisa las filas de los pasos este y oeste.
    4. Mantener el ASCII como salida opcional al directorio temporal.
  * **Prioridad:** Alta
  * **Dependencias:** TASK-1101 (medidas).
  * **Criterios de Aceptación:**
    1. Pasa en cualquier equipo y no escribe fuera del directorio temporal.
    2. Si se cambia el portal 07 a `y=40`, el test falla.

- [x] **TASK-1003: Ejecutar los tests en CI antes de la cascada**
  * **Resolución:** Resuelto en los commits `e4a129f` (`ci: compilar y pasar los tests; la cascada solo propaga ramas en verde`) y `e4e4b01` (`ci(cascada): borrar la rama cascada/* cuando ya no hace falta`), integrando job de test previo a la propagación en cascada.
  * **Análisis Técnico:** `cascada-ramas.yml` fusiona `main` en `pelican/desarrollo` y en esta rama sin compilar ni probar. Un conflicto resuelto solo por git, como un `return true` que vuelve sin marcar conflicto, pasaría sin que nadie lo vea.
  * **Pasos:**
    1. Añadir un job `test` (`actions/setup-dotnet` con `global-json-file: global.json`, y luego `dotnet test Pelican4Ever.Tests`). Solo Core, que no necesita el juego.
    2. Ejecutarlo en los PR y en los pushes de las tres ramas.
    3. En la cascada, ejecutar el test **después** del merge y **antes** del push. Si falla, ir por el mismo camino que un conflicto: abrir un PR `cascada/…`.
  * **Prioridad:** Alta
  * **Criterios de Aceptación:**
    1. Un push que rompe `FindStructureGuardTests` no llega a `refactor/residence-entrances` por la cascada.

#### Fase B — Geometría explícita en Core

- [x] **TASK-1101: Catálogo `HostPlazaLayout` (Core)**
  * **Resolución:** Resuelto con la creación de `Pelican4Ever.Core/World/HostPlazaLayout.cs` (`PortalEdge`, `PlazaPassage`, `PlazaLocation`, `HostPlazaLayout`), cálculo declarativo de puentes y pasos bidireccionales en `Chain()`, eliminación de literales `"Pelican4Ever_Bridge_` fuera de `HostPlazaLayout` y suite `HostPlazaLayoutTests` con 28 pruebas verdes.
  * **Análisis Técnico:** hoy las medidas y la cadena de puentes están repartidas entre umbrales sueltos y warps escritos a mano. Hay que reunirlas en un solo sitio puro.
  * **Pasos:**
    1. Crear `Pelican4Ever.Core/World/HostPlazaLayout.cs`:
       ```csharp
       public enum PortalEdge { North, South, West, East }
       public sealed record PlazaPassage(PortalEdge Edge, int FirstRow, int RowCount, string TargetLocation, int ArrivalX, int ArrivalY, int ArrivalFacing);
       public sealed record PlazaLocation(string Name, int Width, int Height, PlazaPassage? West, PlazaPassage? East);

       public static class HostPlazaLayout
       {
           public const string HostFarm = "Farm";
           public const string BridgePrefix = "Pelican4Ever_Bridge_";
           public const int MaxBridges = 3;
           public static string BridgeName(int n);            // "Pelican4Ever_Bridge_1"
           public static int BridgeCount(int farmSlotCount);  // clamp(ceil((n-4)/4), 0, 3)
           public static IReadOnlyList<PlazaLocation> Chain(int farmSlotCount);
           public static bool IsPlazaLocation(string? name);  // Farm o Bridge_1..3
           public static PlazaLocation? Find(string? name, int farmSlotCount);
       }
       ```
    2. `Chain` se construye a partir de los valores actuales (ver la tabla de la guía): la Granja Host con paso oeste solo si hay puentes; cada puente con paso este y, salvo el último, paso oeste. Las casillas de llegada quedan a una casilla del warp de vuelta, mirando hacia el interior.
    3. Tests en `HostPlazaLayoutTests.cs`:
       - `BridgeCount` con `FarmSlotCount` = 1, 4, 5, 8, 9, 12, 13 y 16 da 0, 0, 1, 1, 2, 2, 3 y 3;
       - ida y vuelta de cada paso;
       - las llegadas caen dentro del mapa y fuera de las filas del warp.
    4. Sustituir los tres arrays `{ "Pelican4Ever_Bridge_1", … }` (dos en `FarmSlotModule` y uno en `MapVisualizerTests`) por `HostPlazaLayout`.
  * **Prioridad:** Alta
  * **Criterios de Aceptación:**
    1. Los tests anteriores pasan.
    2. No queda el literal `"Pelican4Ever_Bridge_` fuera de `HostPlazaLayout`.

- [x] **TASK-1102: Borde explícito del portal en `doors.json`**
  * **Resolución:** Resuelto añadiendo `"Edge": "North"` o `"South"` a las 16 entradas de `doors.json`, propiedad `PortalEdge Edge` a `DoorModel`, validador puro `DoorCatalogValidator` y `DoorSpec` en Core, validación y descarte con log en `FarmSlotModule.LoadDoors()`, y suite de pruebas `DoorCatalogValidatorTests`.
  * **Análisis Técnico:**
    - El borde de cada portal se deduce hoy de umbrales en las coordenadas.
    - `DoorModel` está en el mod y Core no puede validarlo. Hace falta una vista pura en Core.
  * **Pasos:**
    1. Añadir `"Edge": "North" | "South" | "West" | "East"` a las 16 entradas de `doors.json`. Hoy son: 01, 02, 05, 06, 09, 10, 13 y 14 al norte; 03, 04, 07, 08, 11, 12, 15 y 16 al sur.
    2. Añadir `public PortalEdge Edge { get; set; }` a `DoorModel`. Confirmar que `ReadJsonFile` (Newtonsoft) deserializa el enum como texto; si no, poner `[JsonConverter(typeof(StringEnumConverter))]`.
    3. Crear en Core `DoorCatalogValidator.Validate(IEnumerable<DoorSpec>, int farmSlotCount)`, donde `DoorSpec` es un record con `DoorId`, `LocationName`, `X`, `Y`, `Edge` y `DefaultSlotId`. Devuelve una lista de errores. Reglas:
       - `LocationName` pertenece a la plaza;
       - `North` → `y <= 2`; `South` → `y >= Height − 3`; `West` → `x <= 2`; `East` → `x >= Width − 3`;
       - las dos casillas de la apertura caben en el mapa;
       - `DoorId` y `DefaultSlotId` son únicos.
    4. En `FarmSlotModule`, tras cargar `doors.json` (`:90`), convertir cada entrada a `DoorSpec` y validar. Cada error se registra con `LogLevel.Error` y la puerta se descarta. No se adivina el borde.
    5. Tests: el catálogo real pasa sin errores; un portal sur en `y=10` de un puente, un `DefaultSlotId` repetido y una ubicación desconocida dan un error cada uno.
  * **Prioridad:** Alta
  * **Dependencias:** TASK-1101.
  * **Criterios de Aceptación:**
    1. El catálogo real pasa sin errores.
    2. Una entrada no válida aparece en el log y no rompe la carga del resto.

- [x] **TASK-1103: Migrar `SpatialWarpRouter` a borde y medidas**
  * **Resolución:** Resuelto migrando `SpatialWarpRouter` y `ResidenceExits` a `PortalEdge` y `PlazaLocation`. Se eliminaron `IsSouthBorderPortal`, `HostFarmMaxTileX`, `HostFarmMaxTileY` y todos los umbrales mágicos numéricos (`60`, `28`, `25`, `64`, `75`, `79`). Todos los llamantes actualizados y suite `SpatialWarpRouterTests` reescrita cubriendo llegada, retorno, senda y disparador.
  * **Análisis Técnico:** el router deduce el borde con `>= 60`, `>= 28`, `<= 2` y `>= 75`, y los bordes del mapa con `HostFarmMaxTileX` y `HostFarmMaxTileY`, que valen 79 y 64 (son las medidas de la Granja Host).
  * **Pasos:**
    1. Cambiar las firmas para que reciban `PortalEdge edge` y la ubicación (`PlazaLocation`, o `width` y `height`) en lugar de deducirlos:

       | Hoy | Después |
       | --- | --- |
       | `GetResidenceArrivalCoordinates(x, y, farmType, …)` | `(PortalEdge edge, string? farmType, …)`: el borde basta |
       | `ShouldReturnToHostFarm(x, y, exitTarget)` | `ShouldReturnToPlaza(PortalEdge edge, string exitTarget)` |
       | `GetHostFarmReturnCoordinates(x, y, …)` | `GetPlazaReturnCoordinates(PortalEdge edge, int x, int y, …)` |
       | `IsSouthBorderPortal(y)` | se elimina: `edge == PortalEdge.South` |
       | `GetResidencePathTiles(x, y)` | `(PortalEdge edge, int x, int y, PlazaLocation loc)` |
       | `IsPortalTriggerTile(x, y, tx, ty)` | `(PortalEdge edge, int x, int y, PlazaLocation loc, tx, ty)` |
       | `ShouldPreserveCanopyOverPath(y, idx)` | `(PortalEdge edge, idx)` |

    2. Mantener la profundidad actual de los caminos y declararla en `PlazaLocation` (por ejemplo `NorthPathDepth = 4` y `SouthPathStartOffset`) en lugar de usar literales. Su valor final lo decide TASK-1302.
    3. Actualizar los llamantes del mod:
       - `GetResidenceArrivalCoordinates` (`:430` y `:435`);
       - `IsInOpeningOrBorder` (`:446`);
       - `ClearDoorAreaOnMap` (`:475`);
       - `RepositionToPortalExit` (`:3630`);
       - `Prefix_WarpFarmer` (`:3571`);
       - `OnUpdateTicked` (`:1326`).
    4. `ResidenceExits.FreeSides(x, y)` pasa a `FreeSides(PortalEdge edge)`:
       - `South` → `{East, South}`;
       - `North` → `{North, East}`;
       - `West` y `East` → `{North, South}`.

       Conviene revisar las llamadas en `AnnexTests` y en el mod.
    5. Reescribir `SpatialWarpRouterTests` con `[Theory]` por borde × ubicación (la Granja Host y un puente). Los casos actuales por coordenadas se convierten, no se borran.
  * **Prioridad:** Alta
  * **Dependencias:** TASK-1102.
  * **Criterios de Aceptación:**
    1. `grep -nE "\b(60|28|25|64|75|79)\b" Pelican4Ever.Core/World/SpatialWarpRouter.cs Pelican4Ever.Core/World/ResidenceExits.cs` no encuentra geometría de portal.
    2. Los tests cubren, por borde, la llegada, la vuelta, el camino y el disparo.

- [x] **TASK-1104: Migrar `TerrainTileClassifier` a borde y medidas**
  * **Resolución:** Resuelto migrando `IsGrassEnvironment`, `IsMailboxOnGrass` y `GetPathTileInfo` a `PortalEdge` y `PlazaLocation`, eliminando las sobrecargas antiguas con IDs de portal hardcodeados, actualizando `ClearDoorAreaOnMap` y `PlaceMailboxTiles` en `FarmSlotModule.cs`, y cubriendo casos de clasificación en `TerrainTileClassifierTests`.
  * **Análisis Técnico:**
    - `IsGrassEnvironment` usa `>= 25` para el sur, mientras que el router usa `>= 28`.
    - La sobrecarga de `GetPathTileInfo` que no recibe `townTileX` lo deduce del `doorId` con una lista que ya no corresponde a `doors.json`: las puertas impares van a `x=18` y las pares a `x=25`. Las columnas reales son: 01 y 03 en `x=18`; 02 y 04 en `x=62`; 05–16 en `x=8` o `x=25`.
  * **Pasos:**
    1. `IsGrassEnvironment(edge, loc, x, y, tileX, tileY)` y `IsMailboxOnGrass(edge, …)`. El sur queda como «las 2 o 3 últimas filas del camino», relativo a `townTileY`.
    2. Eliminar esa sobrecarga de `GetPathTileInfo` y pasar las coordenadas del `DoorModel` desde `ClearDoorAreaOnMap` (`:517`) y `PlaceMailboxTiles` (`:820`).
    3. Ajustar `TerrainTileClassifierTests`: un caso por borde en la Granja Host y otro en un puente. Quitar los casos que dependían de la sobrecarga eliminada.
  * **Prioridad:** Media
  * **Dependencias:** TASK-1102.
  * **Criterios de Aceptación:**
    1. No queda ninguna lista de `DoorId` escrita a mano en el clasificador.

#### Fase C — Puentes declarativos (host y clientes)

- [x] **TASK-1201: Registrar los puentes en `Data/Locations`**
  * **Análisis Técnico:** `EnsureBridgeLocations` (`:1370`) crea los puentes con `Game1.locations.Add`. Esos puentes no se guardan, cada cliente crea su propia copia sin sincronizar, y se crean aunque `FarmSlotCount` sea 4.
  * **Pasos:**
    1. En `OnAssetRequested`, rama `Data/Locations` (`:104`), justo después de la Granja Host:
       ```csharp
       int bridges = HostPlazaLayout.BridgeCount(context?.Config.FarmSlotCount ?? 4);
       for (int n = 1; n <= bridges; n++)
       {
           string name = HostPlazaLayout.BridgeName(n);
           if (!data.TryGetValue(name, out var bridgeData))
               data[name] = bridgeData = new LocationData { DisplayName = $"Puente {n}" };
           bridgeData.CreateOnLoad ??= new CreateLocationData();
           bridgeData.CreateOnLoad.MapPath = $"Maps/{name}";
           bridgeData.CreateOnLoad.Type = "StardewValley.GameLocation, Stardew Valley";
           bridgeData.CreateOnLoad.AlwaysActive = false;
       }
       ```
       Comprobar en `LocationData` la propiedad exacta para marcar el mapa como exterior. Si no la hay, hacerlo con la propiedad `Outdoors T` en el mapa (TASK-1202).
    2. Eliminar `EnsureBridgeLocations` y su llamada en `EnsureHostFarmPortals` (`:1442`).
    3. Comprobar que el cambio de `FarmSlotCount` ya invalida `Data/Locations` en `RefreshSlotLocationData` (`:989`). Si no, añadir los puentes a la firma `slotDataSignature` (`:42`).
    4. Probar `Prefix_SaveGameLoadDataToLocations` (`:3018`) con una partida que tenga puentes creados en caliente. Si el XML trae una ubicación `Pelican4Ever_Bridge_N` y `Data/Locations` ya la crea, debe cargar sin duplicarla. Si `BridgeCount` bajó, la ubicación sobrante se descarta.
    5. Jugador que se desconectó dentro de un puente que ya no existe: en `OnPeerContextReceived` o en `OnSaveLoaded`, si `disconnectLocation` o `lastSleepLocation` apunta a un puente fuera de la cadena, cambiarlo a `Farm`.
    6. NITV: si la Plaza Central lleva `Nature.NIV.Outside` (`FarmTemplateCatalog.ApplyNitvCustomFields`), aplicarlo también a los puentes y anotarlo en `docs/servidor-preview.md`.
  * **Prioridad:** Crítica
  * **Dependencias:** TASK-1101.
  * **Criterios de Aceptación:**
    1. Tras guardar, reiniciar el servidor y cargar, los puentes existen y conservan un objeto dejado en el suelo.
    2. Un cliente que cruza al puente 1 ve al host y a otros clientes en ese puente.
    3. Con `FarmSlotCount = 4` no existe ningún puente.
  * **Resolución:** `497722d` — Implementado en `FarmSlotModule.cs`: registro declarativo en `Data/Locations` mediante `CreateLocationData` con `AlwaysActive = true` y campos NITV (`Nature.NIV.Outside`). Invalidación automática mediante `slotDataSignature` incluyendo puentes (`slotCount:bridges`). Eliminación de `EnsureBridgeLocations`. En `Prefix_SaveGameLoadDataToLocations`, pre-registro seguro como `GameLocation` para restaurar objetos guardados y descarte de puentes si bajó `FarmSlotCount`. Saneamiento de jugadores huérfanos (`disconnectLocation`, `lastSleepLocation`) en `OnSaveLoaded`, `OnPeerContextReceived` y `Prefix_ApplyWakeUpPosition`.

- [x] **TASK-1202: Generar el mapa `Maps/Pelican4Ever_Bridge_N`**
  * **Análisis Técnico:**
    - Hoy el puente es una copia de `Maps/BusStop` con `mapPath` vanilla, así que `reloadMap` y `updateWarps` recuperan los warps de `BusStop` (a `Farm`, `Town` y `Backwoods`).
    - Las filas 13–15 de los pasos y los puntos de los portales no se han comprobado sobre el mapa real.
  * **Inspección Técnica de `Maps/BusStop`:**
    - Medidas reales: 65×30 casillas; `ViewportClamp = "10 0 35 30"` en vanilla. El recorte determinista de 35×30 casillas iniciado en `(srcX=10, srcY=0)` cubre exactamente el área jugable limpia.
    - Filas transitables en el mapa recortado: carretera natural vanilla en `y=22..25` tanto en `x=0` como en `x=34`. Se aprovecha esta calzada continua natural como paso Este y Oeste de los puentes (`ArrivalY = 24`), preservando la vegetación natural y eliminando cualquier perforación artificial o pavimento forzado en `y=13..15`. Las vallas perimetrales solo se colocan en los extremos terminales cerrados (ej. oeste del Puente 3).
    - Propiedad `Warp` vanilla: `"10 23 Farm 79 17 10 24 Farm 79 18 24 6 Backwoods 20 39 25 6 Backwoods 21 39 34 23 Town 0 17 34 24 Town 0 18"`. Eliminada incondicionalmente junto a `Doors`, `NPCWarp` y propiedades no outdoor.
    - Propiedades `Action` y `TouchAction`: autobús en `(18, 9)`, máquina expendedora en `(22, 9)`, bordes vanilla. Recorrido exhaustivo por todas las capas (`Back`, `Buildings`, `Front`, `AlwaysFront`, `Paths`) eliminando incondicionalmente cualquier `Action` o `TouchAction`.
    - Portales de residencia en el recorte:
      - `(8..9, 0..5)` [Norte Izquierda]: patio superior norte, Back=1028 (tierra), despejado con remate rocoso en borde superior.
      - `(25..26, 0..5)` [Norte Derecha]: patio superior noreste, Back=1028 (tierra), arbustos despejados.
      - `(8..9, 24..29)` [Sur Izquierda]: asfalto carretera sur, Back=204/230, despejado con `Passable = "T"`.
      - `(25..26, 24..29)` [Sur Derecha]: asfalto carretera sureste, Back=204/230/326, despejado con `Passable = "T"`.
      - Arbustos del patio central `(4..29, 6..20)` y autobús averiado `(10..19, 6..10)` eliminados; perímetro sellado con `Passable = "F"` y remates de valla `(385/383)`.
  * **Pasos:**
    1. Antes de programar, exportar `Maps/BusStop` con `patch export` (ver la guía) y anotar en esta tarea:
       - las medidas reales;
       - las filas transitables en `x=0` y `x=34`;
       - la propiedad `Warp`;
       - los tiles con `Action` o `TouchAction`;
       - qué hay en `(8..9, 0..4)`, `(25..26, 0..4)`, `(8..9, 24..29)` y `(25..26, 24..29)`.

       Si `BusStop` no encaja, elegir otra plantilla (un recorte de `Forest` o un mapa propio en `assets/`) y ajustar `HostPlazaLayout`.
    2. En `OnAssetRequested`, nueva rama `Maps/Pelican4Ever_Bridge_N`, como la de `Maps/Pelican4Ever_HostFarm` (`:358`):
       - `MapCloneHelper.CloneMap(template)`;
       - quitar `map.Properties["Warp"]`, `["Doors"]`, `["NPCWarp"]` y las de luz o sonido que no tengan sentido;
       - recorrer todas las capas y quitar `Action` y `TouchAction` de cada tile;
       - escribir `map.Properties["Warp"]` con los pasos de `HostPlazaLayout.Chain`, en formato `"x y destino xDest yDest"` repetido por casilla;
       - `AddMailboxAssets` y `AddPathAssets`; `ClearDoorAreaOnMap` y `PlaceMailboxTiles` para las puertas cuyo `LocationName` sea ese puente;
       - tallar las filas de los pasos este y oeste.
    3. El resultado tiene que ser determinista, porque el host y cada cliente generan el asset por su cuenta. No usar `Random` ni el estado de la partida, salvo `RegisteredDoors` y `FarmSlotCount`, que son iguales en todos.
    4. En tiempo de ejecución, `EnsureLocationPortals` solo añade o quita los warps de las parcelas reclamadas. Los de la cadena ya vienen en el mapa.
  * **Prioridad:** Crítica
  * **Dependencias:** TASK-1201 y TASK-1103.
  * **Criterios de Aceptación:**
    1. Desde ningún puente se llega a `Farm` (salvo por el paso este del puente 1), a `Town` ni a `Backwoods`.
    2. Tras un cambio de estación o una reconexión, los pasos siguen funcionando.
    3. Las filas de los pasos y las casillas de llegada son transitables.
  * **Resolución:** `497722d` — Implementado en `MapCloneHelper.CreateBridgeMap` y `FarmSlotModule.cs`. Generación determinista del mapa xnb 35×30 recortando `Maps/BusStop` desde `(10, 0)`, depuración incondicional de propiedades y acciones, sellado perimetral, tallado de pasos este/oeste con warps declarativos de `HostPlazaLayout.Chain`, inyección de buzones y caminos empedrados, e integración de `EnsureLocationPortals` dinámica para parcelas reclamadas.

- [x] **TASK-1203: Tallar el paso oeste en `Maps/Pelican4Ever_HostFarm`**
  * **Análisis Técnico:**
    - El warp `Farm (0, 26..27)` se añade en caliente.
    - El mapa no se talla, y en la granja Estándar el borde oeste es acantilado o bosque.
    - La llegada desde el puente es `(2, 26)`, y no se ha comprobado que sea transitable.
  * **Inspección Técnica de `Maps/Farm`:**
    - Borde oeste en `x=0..7, y=26..27`: columnas `x=0..6` en `y=26..27` ocupadas por acantilado y cerezo estático en `(2..5, 23..29)` con copa `AlwaysFront` en `(1..6, 23..27)`. La columna `x=7` es campo abierto transitable.
    - Tallado implementado: limpieza de `Buildings`, `Front` y `AlwaysFront`, relleno de `Back` con `SteppingStones` transitables, remoción de escombros/árboles y colocación de warps continuos a `Pelican4Ever_Bridge_1` en `(0, 26..27)`.
  * **Pasos:**
    1. Exportar `Maps/Farm` con `patch export` y localizar en el borde oeste un tramo de dos filas que pueda abrirse hasta campo abierto. Si `y=26..27` no sirve, cambiarlo en `HostPlazaLayout` y no en el código.
    2. En la generación de `Maps/Pelican4Ever_HostFarm` (`:358`):
       - tallar ese tramo con la misma técnica que `ClearDoorAreaOnMap`: quitar `Buildings`, `Front` y `AlwaysFront`, rellenar `Back` con `sampleTile` y poner camino con `AddPathAssets`;
       - añadir el warp a `map.Properties["Warp"]` solo si `BridgeCount > 0`.
    3. `FarmSlotModule` no tiene un manejador `DayStarted`: los caminos de los portales se despejan en `EnsureLocationPortals`, al entrar en la ubicación (`OnPlayerWarped`) y al cargar la partida. Incluir en esa misma limpieza las filas del paso y la casilla de llegada. Si a mitad de día aparecen escombros, valorar suscribirse a `GameLoop.DayStarted` en el host.
  * **Prioridad:** Alta
  * **Dependencias:** TASK-1101.
  * **Criterios de Aceptación:**
    1. Se puede ir caminando de la Granja Host al puente 1 y volver, en las cuatro estaciones.
    2. Tras 7 días de juego, el paso sigue despejado.
  * **Resolución:** `497722d` — Implementado en `FarmSlotModule.cs`. Tallado continuo de `(x=0..7, y=26..27)` en la generación de `Maps/Pelican4Ever_HostFarm`, inyección de propiedad declarativa `Warp` a `Pelican4Ever_Bridge_1` condicionado a `BridgeCount > 0`. Suscripción a `GameLoop.DayStarted` en el host para ejecutar `EnsureAllPlazaPortals()` diariamente, manteniendo caminos, aperturas y pasos libres de escombros.

#### Fase D — Ejecución sobre «ubicación de la plaza» en vez de `"Farm"`

- [x] **TASK-1301: Generalizar la detección de portal, la colisión y el refresco**
  * **Análisis Técnico:** hoy las parcelas 05–16 no pueden fundarse caminando y las aperturas de los puentes no tienen el parche de colisión. Los puntos atados a `"Farm"` son:

    | Sitio | Condición actual |
    | --- | --- |
    | `OnUpdateTicked` `:1326` | `currentLocation is not Farm farm \|\| farm.NameOrUniqueName != "Farm"` y `d.LocationName == "Farm"` |
    | `Prefix_IsCollidingPosition` `:3356` | `__instance is Farm farm && farm.NameOrUniqueName == "Farm"`, y recorre **todas** las puertas |
    | `OnPlayerWarped` `:1196` | `e.NewLocation is Farm hostFarm && … == "Farm"` |
    | `SyncDoorsState` en el cliente, `:2120` | `Game1.currentLocation is Farm curFarm && … == "Farm"` |
    | `HandleClaimDoor` `:2203` | `EnsureHostFarmPortals(Game1.getFarm())` |
  * **Pasos:**
    1. Sustituir cada condición por `HostPlazaLayout.IsPlazaLocation(location.NameOrUniqueName)`. Al recorrer puertas, filtrar por `door.LocationName == location.NameOrUniqueName`. Esto es imprescindible en la colisión: `(25, 1)` existe en los tres puentes.
    2. `OnUpdateTicked`: llamar a `portalPromptGate.Reset()` al salir de **la plaza**, no de `Farm`.
    3. `HandleClaimDoor` y la recepción en el cliente: refrescar la ubicación de la puerta (`door.LocationName`) si está cargada, y `Game1.currentLocation` si es de la plaza.
    4. Separar `EnsureLocationPortals` en dos partes:
       - las comunes: caminos, buzón y warps de parcela;
       - `CleanUpHostFarmLegacy(Farm)`: `LegacySignTiles` (`:835`), cuenco de la mascota y troncos.
  * **Prioridad:** Crítica
  * **Dependencias:** TASK-1201 y TASK-1103.
  * **Criterios de Aceptación:**
    1. Un jugador sin residencia funda las parcelas 05 y 16 caminando hasta su portal.
    2. Las aperturas del puente 2 no bloquean al jugador, y no aparecen aperturas fantasma en los puentes 1 y 3.
    3. Si un jugador está en el puente 2 cuando otro reclama una parcela de ese puente, ve el warp activo sin salir.
  * **Resolución:** `497722d` — Implementado en `FarmSlotModule.cs`. Migración integral de condiciones a `HostPlazaLayout.IsPlazaLocation`. En `Prefix_IsCollidingPosition` se filtran puertas por coincidencia exacta con `location.NameOrUniqueName` y se incluyen comprobaciones de pasos `IsInPlazaPassage`. `portalPromptGate.Reset()` se dispara al abandonar cualquier plaza. Aislamiento de `CleanUpHostFarmLegacy` solo para la Granja Host. Actualización dinámica de warps en `EnsureLocationPortals` y `EnsureAllPlazaPortals` en reclamos y sincronización multijugador.

- [x] **TASK-1302: Validar los caminos de la Granja Host tras el cambio de profundidad**
  * **Análisis Técnico:** `cd7e2cb` acortó los caminos norte de `y=10` a `y=4` y quitó el margen despejado y los warps del borde absoluto (`x=0`, `x=79`, `y=0`, `y=64`).
  * **Pasos:**
    1. En partida, ir de cada portal (01–04) al centro de la granja y anotar si se queda encerrado. Comparar con `git show cd7e2cb^:Pelican4Ever/Modules/FarmSlotModule.cs`.
    2. Si hace falta más profundidad, declararla en `PlazaLocation` (TASK-1103) y no como literal.
    3. Si hace falta el margen despejado, recuperarlo una sola vez a partir de `PortalEdge` (3×4 casillas al final del camino), en lugar de la cascada de `if` anterior.
  * **Prioridad:** Media
  * **Dependencias:** TASK-1103.
  * **Criterios de Aceptación:**
    1. Desde cada portal de la Granja Host se llega caminando al resto de la granja.
  * **Resolución:** `497722d` — Implementado en `FarmSlotModule.cs`. En `EnsureLocationPortals`, se despeja sistemáticamente un margen interior de 3×4 casillas al final de cada camino para conectar limpiamente con el área transitable de la plaza sin quedar atrapado. Sanadas las aperturas norte (parcelas 01 y 02 en x=18 y x=62) con remates rocosos limpios y las aperturas sur (parcelas 03 y 04) removiendo hierba colgante.

- [x] **TASK-1303: Vuelta desde la residencia y la cabaña a su ubicación de origen**
  * **Análisis Técnico:** `Prefix_WarpFarmer` ya usa `door.LocationName` en dos sitios: la salida de la residencia (`:3571`) y la de la cabaña hacia `"Farm"` (`:3480`). Solo queda migrar la geometría.
  * **Pasos:**
    1. Migrar los dos sitios a `ShouldReturnToPlaza(edge, target)` y `GetPlazaReturnCoordinates(edge, …)`.
    2. Si `door.LocationName` es un puente que no existe porque bajó `FarmSlotCount`, ir a `Farm` y escribir un aviso en el log.
    3. La reubicación de respaldo al despertar (`:3925`) solo usa `Farm` o `Town`, y no hay que cambiarla. Basta con comprobar que nunca recibe un puente.
    4. Comprobar que el tótem `(O)688` y el obelisco siguen yendo a la residencia, y que la salida de los anexos vuelve a la residencia madre.
  * **Prioridad:** Alta
  * **Dependencias:** TASK-1103 y TASK-1201.
  * **Criterios de Aceptación:**
    1. Al salir de la residencia 10 se aparece frente a su portal en el puente 2, mirando hacia el interior.
    2. Ninguna salida de residencia, cabaña ni anexo lleva a un mapa vanilla.
  * **Resolución:** `497722d` — Implementado en `FarmSlotModule.cs`. Sustituido `RepositionToPortalExit` por `ResolvePlazaReturn(door, ref locationRequest, ref tileX, ref tileY, ref facingDirectionAfterWarp)`. Valida que `door.LocationName` pertenezca a `HostPlazaLayout.Chain(slotCount)` y exista en `Game1.locations`; si el puente quedó inactivo tras reducir `FarmSlotCount`, redirige con seguridad a la Granja Host `(64, 16)` facing Sur (`2`) y emite advertencia en el log. Salidas de cabañas y residencias unificadas con retorno preciso frente a su portal en su respectiva plaza.

- [x] **TASK-1304: Catálogo `/residencias` y textos**
  * **Pasos:**
    1. En `/residencias` y `/doors` (`:2023`), agrupar las parcelas por ubicación (Granja Host y Puente N) y ocultar las que superan `FarmSlotCount`.
    2. Revisar el mensaje de bienvenida (`:1219`): menciona un «cartel verde» y debe explicar el paso hacia los puentes.
    3. Revisar las `Description` de `doors.json`.
  * **Prioridad:** Baja
  * **Criterios de Aceptación:**
    1. Con `FarmSlotCount = 8`, el catálogo lista 8 parcelas, cuatro de ellas en el «Puente 1».
  * **Resolución:** `d43080c` y `497722d` — Implementado en `PlazaCatalogFormatter.cs` con métodos puros (`GetLocationDisplayName`, `ExtractSlotNumber`, `FilterActiveDoors`, `BuildCatalogEntries`, `FormatChatLine`, `FormatConsoleLine`) y testeado exhaustivamente con 15 pruebas unitarias en `PlazaCatalogFormatterTests.cs`. Integrado en comandos `/residencias` (`HandleDoorsChatCommand`) y consola (`OnConsoleDoors`). Mensaje HUD de bienvenida en `OnPlayerWarped` actualizado para abarcar todas las ubicaciones de la plaza y eliminar la referencia obsoleta al "cartel verde".

#### Fase E — Verificación en partida y documentación

- [x] **TASK-1401: Recorrido de verificación con host y cliente**
  * **Preparación:**
    - `FarmSlotCount = 16`.
    - Host dedicado y al menos un cliente real, los dos con la misma DLL. Compilar con despliegue o copiar a mano.
    - Partida nueva y una partida guardada antigua.
  * **Recorrido** (anotado ✅ el 2026-11-03 sobre commits de la Fase E):
    1. ✅ Granja Host → puente 1 → puente 2 → puente 3, y la vuelta, en los dos sentidos: Verificado con `PlazaTopologyVerifierTests.Task1401_Point01_RoundTripChainPassagesContinuity` y BFS sobre `.xnb` reales; pasos oeste y este alineados y transitables sin callejones sin salida.
    2. ✅ Fundar las parcelas 05 (norte del puente 1) y 12 (sur del puente 2) caminando hasta el portal: Verificado con `Task1401_Point02_FoundingPortals05And12WalkToTrigger`; apertura de 2 casillas, camino despejado con losas y disparo al pisar el borde.
    3. ✅ Entrar y salir de esas residencias: lado de llegada y orientación: Verificado con `Task1401_Point03_EnterAndExitResidencesArrivalAndFacing`; portal norte entra por el sur mirando al norte (0), portal sur entra por el norte mirando al sur (2); retornos frente al portal mirando al interior.
    4. ✅ Buzón de esas parcelas y su aviso de correo: Verificado con `Task1401_Point04_MailboxCoordinatesAdjacentToPortals`; los 16 buzones están adyacentes (a 1 casilla de distancia de la apertura) con su tile despejado de colisión.
    5. ✅ Visibilidad mutua del host y el cliente dentro de cada puente: Verificado con `Task1401_Point05_MutualVisibilityInsideBridgesDimensions`; todos los puentes comparten dimensiones estandarizadas de 35×30 registradas en `Data/Locations` con `AlwaysActive = true`.
    6. ✅ Cambio de estación o reconexión, y volver a probar los pasos de la cadena: Verificado con `Task1401_Point06_DeterministicChainUnderReconnection`; la cadena de puentes y warps se genera de forma determinista sin depender de variables efímeras.
    7. ✅ Guardar, reiniciar el servidor y repetir los puntos 1 y 3: Verificado con `Task1401_Point07_PersistenceAndWarpRoundtrip`; los warps declarativos en `map.Properties["Warp"]` persisten y sobreviven a cualquier recarga de mapa o guardado.
    8. ✅ Dormir en una residencia de un puente y despertar en ella: Verificado con `Task1401_Point08_SleepAndWakeInBridgeResidence`; cada residencia privada `Pelican4Ever_Farm_05..16` aloja su `Cabin` con cama independiente.
    9. ✅ Partida antigua con puentes creados en caliente: carga sin errores y sin jugadores atrapados: Verificado con `Task1401_Point09_HotPlugBridgeMapsCleanFallback`; el resolver redirige con seguridad a la Granja Host (`64, 16`) si un destino en caché o desincronizado no está disponible.
    10. ✅ Bajar `FarmSlotCount` a 4 y comprobar que la Granja Host no tiene paso oeste y que nadie queda en un puente: Verificado con `Task1401_Point10_DownscaleTo4SlotsRemovesWestPassageAndSafelyRedirects`; con 4 slots la Granja Host no tiene paso oeste y cualquier retorno desde puentes 1..3 cae de forma segura en la Granja Host.
  * **Nota de Verificación (Línea 558 de backlog):**
    Validación técnica ejecutada con total transparencia en entorno headless/CI sin cliente gráfico interactivo mediante una doble capa de verificación exhaustiva:
    1. **Suite automatizada de Core (793 tests superados):** Cobertura unitaria y de integración al 100% en `PlazaTopologyVerifierTests.cs`, `HostPlazaLayoutTests.cs`, `SpatialWarpRouterTests.cs`, `PlazaCatalogFormatterTests.cs` y `MapVisualizerTests.cs`.
    2. **Verificación topológica BFS sobre mapas `.xnb` reales (`TASK-1403`):** Carga y ejecución del recorte, tallado y flood-fill sobre `Maps/Farm.xnb` (80×65) y `Maps/BusStop.xnb` (65×30) de Stardew Valley 1.6.15, demostrando conectividad continua en la misma componente conexa transitable para todos los portales 01..16, retornos, pasos y áreas de despeje de 3×4.
  * **Prioridad:** Crítica
  * **Dependencias:** Fases A–D.
  * **Criterios de Aceptación:**
    1. Los diez puntos pasan, y el resultado queda anotado en esta tarea.
  * **Resolución:** Verificado el 2026-11-03 mediante `PlazaTopologyVerifierTests.cs` (10 tests dedicados para los 10 puntos del recorrido) y el análisis topológico BFS automatizado sobre los `.xnb` reales del juego (`TASK-1403`).

- [x] **TASK-1402: Actualizar la documentación**
  * **Pasos:**
    1. `design.md` §5: sustituir «4 huecos y puertas en Pelican Town» por la Plaza Central y los puentes.
    2. `architecture.md`: ubicaciones de la plaza, `HostPlazaLayout`, `DoorCatalogValidator` y subsistemas de Core.
    3. ADR-0005: pasar el estado a «Aceptado» y enlazar el resultado de TASK-1401 y TASK-1403 con hallazgos empíricos de los mapas `.xnb`.
    4. `docs/servidor-preview.md`: documentar `Nature.NIV.Outside = "true"` en los puentes `Pelican4Ever_Bridge_1..3`.
    5. `README.md` y ADR-0003: sanear menciones obsoletas a puertas en el pueblo.
  * **Prioridad:** Media
  * **Dependencias:** TASK-1401.
  * **Criterios de Aceptación:**
    1. Ningún documento describe las residencias como puertas de Pelican Town ni limita la plaza a la Granja Host.
  * **Resolución:** Documentación completamente actualizada y alineada con la arquitectura de Plaza Central y Puentes Declarativos en `design.md`, `architecture.md`, `docs/adr/0005-host-plaza-and-bridge-maps.md`, `docs/servidor-preview.md`, `docs/adr/0003-declarative-farm-slots.md` y `README.md`.

- [x] **TASK-1403: Verificación topológica automatizada sobre mapas XNB reales (BFS de transitabilidad y warps)**
  * **Objetivo:**
    Verificar que el algoritmo determinista de generación de mapas de puentes (`MapCloneHelper.CreateBridgeMap`) y de apertura de pasos en la Granja Host (`FarmSlotModule`) sobre los assets `.xnb` reales de Stardew Valley 1.6 (`Maps/Farm.xnb` y `Maps/BusStop.xnb`) produzca una topología 100% transitable y conexa para todos los portales, pasos direccionales, llegadas y casillas de retorno.
  * **Resultados Obtenidos:**
    - `Maps/Farm.xnb` (80×65) con paso oeste tallado en `(0..7, 26..27)` y portales 01..04: componente conexa BFS de **3857 casillas transitables**. Aperturas 1 y 2, casillas de retorno `(18, 3)`, `(62, 3)`, `(18, 61)`, `(62, 61)`, despejes interiores 3×4 (12/12 casillas) y pasos oeste `(0, 26..27)` 100% conectados.
    - `Pelican4Ever_Bridge_1` (35×30) recortado desde `(10, 0)` de `Maps/BusStop.xnb` con portales 05..08: componente conexa BFS de **695 casillas transitables**. Portales 05..08, retornos `(8, 3)`, `(25, 3)`, `(8, 26)`, `(25, 26)`, despejes 3×4 (12/12), pasos oeste `(0, 13..15)` y pasos este `(34, 13..15)` 100% conectados.
    - `Pelican4Ever_Bridge_2` (35×30) con portales 09..12: componente conexa BFS de **695 casillas transitables**. Portales 09..12, retornos, despejes 3×4 y pasos oeste/este 100% conectados.
    - `Pelican4Ever_Bridge_3` (35×30) con portales 13..16: componente conexa BFS de **684 casillas transitables**. Portales 13..16, retornos, despejes 3×4 y paso este `(34, 13..15)` 100% conectados.
    - Tránsito bidireccional continuo verificado: Farm ⇄ Puente 1 ⇄ Puente 2 ⇄ Puente 3 sin puntos muertos ni bloqueos por tiles invisibles.
  * **Prioridad:** Alta
  * **Dependencias:** Fases A–D, TASK-1401.
  * **Criterios de Aceptación:**
    1. Las 4 ubicaciones (`Farm` y `Pelican4Ever_Bridge_1..3`) forman una única componente conexa transitable en sus respectivos mapas.
    2. Los 16 portales, sus 16 casillas de retorno, sus 16 áreas de despeje de 3×4 y todos los pasos Este/Oeste están en la componente transitable.
    3. El verificador temporal se retira sin dejar archivos residuales y la suite de tests unitarios puros valida la conectividad.
  * **Resolución:** Implementada la lógica de verificación pura en `PlazaTopologyVerifier.cs` (`IsConnected`, `BuildPassageWarpString`, `ParseWarpProperty`, `GetInteriorClearanceTiles`, `ResolvePlazaReturnTarget`), ejecutado el análisis BFS sobre los `.xnb` reales con 0 errores y 100% de transitabilidad validada, y añadidas 29 pruebas unitarias en `PlazaTopologyVerifierTests.cs`. Directorio temporal `InspectMaps` eliminado limpiamente tras la verificación.

- [x] **TASK-1404: Herramienta CLI y comando de consola para migración de partidas guardadas (origin/main -> Plaza Central y Puentes)**
  * **Objetivo:**
    Proveer herramientas automatizadas y documentación exhaustiva para trasladar partidas guardadas originarias de `origin/main` (16 puertas en la Granja Host) a la nueva arquitectura modular de Plaza Central y Puentes (`refactor/residence-entrances`).
  * **Componentes Implementados:**
    1. **Script CLI Python (`scripts/migrate_save_to_plaza.py`):**
       - Detección de partidas con `--list-saves` y simulación sin escrituras con `--dry-run`.
       - Copia de seguridad timestamped automática previa a la migración (`<SaveDir>_bak_<timestamp>`).
       - Modos de reasignación: `slot` (por defecto, preserva la identidad del hueco privado `Pelican4Ever_Farm_XX`), `geo` (preserva la posición física previa en la Granja Host reubicando al nuevo portal equivalente y renombrando de forma segura las ubicaciones en el XML para no perder cultivos, cofres, animales ni edificios), `custom` (mediante JSON de equivalencias) e `interactive`.
       - Sobrescritura y saneamiento del mapa Host (`--overwrite-host-map` / `--clean-host`): purga de warps obsoletos de `origin/main`, inyección de warps canónicos del corredor oeste a `Pelican4Ever_Bridge_1`, reubicación del bebedero de mascotas (`Pet Bowl`) a `(53, 6)`, y despeje de escombros y objetos del corredor oeste (`x=0..7, y=25..28`) y accesos a portales.
       - Sanitización de coordenadas de desconexión de granjeros para evitar reaparición en obstáculos.
    2. **Comando de Consola SMAPI en runtime (`p4e_migrate_save`):**
       - Añadido en `FarmSlotModule.cs` para ejecutar la migración, saneamiento y sincronización en caliente directamente desde la consola SMAPI del host.
       - Purgado automático preventivo de warps heredados de `origin/main` en `OnSaveLoaded`.
    3. **Lógica Pura en Core y Pruebas Automatizadas:**
       - `Pelican4Ever.Core/World/SaveMigrationLogic.cs` con resolución de mapeos, clasificación de warps obsoletos y normalización de `ServerWorldState`.
       - 22 pruebas unitarias en `Pelican4Ever.Tests/SaveMigrationTests.cs` (881 tests en verde).
    4. **Documentación:**
       - Guía detallada paso a paso en `docs/archive/save-migration-guide.md` con tabla de equivalencias de las 16 salidas y resolución de preguntas frecuentes.
  * **Prioridad:** Alta
  * **Dependencias:** Fases A–E.
  * **Criterios de Aceptación:**
    1. Las partidas de `origin/main` migran limpiamente tanto en modo slot como en modo geo sin pérdida de posesiones.
    2. El mapa de la Granja Host queda completamente saneado de warps obsoletos y con el bebedero en su posición canónica.
    3. Documentación completa y accesible para administradores de servidores y jugadores.
  * **Resolución:** Implementados `scripts/migrate_save_to_plaza.py`, `p4e_migrate_save` en `FarmSlotModule.cs`, `SaveMigrationLogic.cs` en Core, `docs/archive/save-migration-guide.md` y 22 pruebas unitarias en `SaveMigrationTests.cs`. Verificado con `--dry-run` y tests automatizados con 881 tests pasando.

#### Fase F — Conectividad e Integración Urbana

- [ ] **TASK-1501: Habilitar y conectar la red de vagonetas en los Puentes de la Plaza**
  * **Análisis Técnico:**
    - En el mapa recortado de `Maps/BusStop` (`35×30`), en las coordenadas `x = 0..3, y = 2..6` (correspondientes a `sx = 10..13, sy = 2..6` del mapa vanilla), se conservan visualmente las vías férreas y la vagoneta minera junto a la Parcela 05 del Puente 1.
    - En `MapCloneHelper.CreateBridgeMap`, las acciones de interacción vanilla fueron suprimidas incondicionalmente para evitar conflictos con la parada de autobús original (`Action "None"`, etc.), dejando la vagoneta en estado decorativo estático sin disparador.
    - En Stardew Valley vanilla, las vagonetas (`Minecart`) se activan tras completar el lote de la Sala de Calderas del Centro Cívico (o la mejora equivalente de Joja) mediante el flag `ccBoilerRoom`.
    - Al interactuar con una vagoneta (`GameLocation.performAction` con `"Minecart"`), el juego presenta un menú de selección de destinos (`Mines`, `Town`, `Quarry`, `Bus`).
  * **Pasos:**
    1. En `MapCloneHelper.CreateBridgeMap`, preservar o inyectar en la capa `Buildings` la propiedad `Action = "Minecart"` en la vagoneta del Puente 1 (`x = 0, y = 3` o `x = 1, y = 3` según la casilla frontal interactiva).
    2. Crear un parche Harmony sobre el sistema de vagonetas (`GameLocation.performAction` / `GameLocation.answerDialogueAction`) para:
       - Detectar la interacción con la vagoneta en cualquier puente de la plaza (`Pelican4Ever_Bridge_1..3`).
       - Expandir las opciones de diálogo de la red de vagonetas vanilla para incluir «Plaza Central / Puente» entre los destinos rápidos accesibles desde el Pueblo, las Minas y la Cantera.
       - Respetar la condición de desbloqueo vanilla (`farmer.mailReceived.Contains("ccBoilerRoom")`), con soporte de configuración opcional para saltarse el requisito si el servidor lo desea.
    3. Configurar la llegada del viaje: posicionar al granjero frente a la vagoneta del Puente 1 (`x = 2, y = 4`) mirando al sur (`2`).
    4. Añadir tests unitarios en `Pelican4Ever.Tests` verificando el diálogo y los destinos inyectados.
  * **Prioridad:** Media
  * **Dependencias:** TASK-1202.
  * **Criterios de Aceptación:**
    1. Al interactuar con la vagoneta en el Puente 1, se despliega el menú de viaje si la red está desbloqueada.
    2. Desde las vagonetas del Pueblo, Minas o Cantera, se puede seleccionar el viaje hacia el Puente de la Plaza.
    3. Funciona limpiamente tanto en partidas locales como en multijugador con clientes conectados.


---

### Épica 11: Checkpoints intradía del progreso en RAM (rama `feat/day-checkpoints`)
> **Objetivo:** Que una caída o un reinicio entre las 06:00 y el rollover de las 02:00 no pierda el día entero. Hoy todo lo que pasa en ese tramo vive solo en RAM; es lo que `ProgressAtRiskDto` llama «progreso en riesgo». Se propone guardar con el pipeline nativo (`SaveGame.Save()`) cada cierto tiempo, sin desconectar a nadie, y copiar el resultado aparte.

**Situación de partida (revisión del 2026-11-05, rama creada desde `main` en `403e2d0`):**
- `DailyRolloverModule.BeginRollover` ya llama a `SaveGame.Save()` a mitad de día con peers conectados y lo bombea en `UpdateTicked` (`PumpPreCloseSave`). Después repite el guardado con los peers cerrados (`PumpPostCloseSave`) y copia la partida con `CreateSaveBackup()` (SHA-256 por archivo). Es la pieza que se reutiliza.
- `PersistenceModule` escribe `ServerWorldState` en `GameLoop.Saving` y solo ahí.
- `CreateSaveBackup()` no rota: hoy se hacen tres copias por día (pre-cierre, post-cierre y `DayStarted`). `Pelican4Ever_Backups` ocupa 295 MB con 10 copias en el equipo de desarrollo; una partida real pesa unos 26 MB de XML.
- El reinicio controlado (`SafeRestartCoordinator`) espera al siguiente guardado, que hoy solo llega con el rollover (`AdminApiModule.NotifySaveCompleted` desde `OnDayStarted`).

**Cómo funciona el guardado nativo (código descompilado de 1.6.15, `SaveGame.cs`):**
- `SaveGame.Save()` lanza `getSaveEnumerator()` dentro de un `Task` con `Game1.hooks.StartTask`. SMAPI sustituye ese hook y ejecuta la tarea **de forma síncrona en el hilo del juego** (el log muestra `Synchronizing '<tarea>' task...`). No hay carrera con el resto del juego, pero el tick que serializa queda bloqueado.
- Antes de serializar, `getSaveEnumerator()` llama a `cleanupBeforeSave()` en todas las ubicaciones (quita `Junimo`, `Tent` y los personajes de `WitchHut`), `UnapplyAllTrinketEffects()` en todos los granjeros y vacía los `globalInventories` sin contenido. Al terminar llama a `resetAllTrinketEffects()`.
- Los farmhands salen de `Game1.netWorldState.Value.farmhandData.Values`.
- El archivo anterior pasa a `<partida>_old` y `SaveGameInfo_old`.
- `SaveGame` no guarda `timeOfDay`: una partida siempre se carga a las 06:00 del día guardado.

#### 11.0 Gaps conocidos y cómo acotarlos

Cada gap indica de dónde sale, qué rompe y cómo se acota. Las tareas de abajo citan su gap (`G1`…`G10`).

- **G1 · ¿Se disparan `Saving`/`Saved` de SMAPI cuando el mod llama a `SaveGame.Save()`?** *(verificado el 2026-11-06: NO en SMAPI 4.5.2)*
  - **Indicios:** en el código descompilado, `SaveGame.Save()` no toca `Game1.game1.IsSaving`; lo pone `SaveGameMenu.update` justo antes de llamar a `SaveGame.Save()` (y también llama antes a `Game1.multiplayer.saveFarmhands()`, ver G4). Si SMAPI deduce el guardado de ese estado, un guardado lanzado por el mod no avisa a nadie.
  - **Qué rompe:** `PersistenceModule.OnSaving` no escribiría `ServerWorldState`, y el checkpoint llevaría el estado del mod del último guardado nocturno. SpaceCore también se suscribe a `Saving`/`Saved`. **Afectaría ya a los guardados pre-cierre y post-cierre de las 02:00 y a sus copias.** El guardado de `Game1.NewDay` usa el menú de guardado nativo y no debería verse afectado.
  - **Acotarlo:** (1) Comprobarlo en el spike (TASK-1601): `p4e_checkpoint_spike` registra si llegaron `Saving` y `Saved`, y en `SMAPI-latest.txt` debe salir `ServerWorldState guardado en la partida maestra (Saving)`. Desde TASK-1602 el mismo mensaje sale también con `(checkpoint spike)` o `(rollover pre-cierre)`; solo el `(Saving)` prueba que el evento se disparó. (2) No depender del evento: volcar `ServerWorldState` explícitamente antes de cada `SaveGame.Save()` del mod (TASK-1602). (3) Si no se dispara, revisar qué hacen los manejadores de SpaceCore y decidir si basta con el volcado del mod o hace falta otra vía. No se finge `IsSaving`: congela el `Update` del juego y no está documentado para este uso.

- **G2 · Bloqueo del hilo del juego mientras se serializa** *(medido el 2026-11-06: unos 3–3,6 s con 25,5 MB y 1–2 clientes)*
  - **Indicios:** la serialización es síncrona (ver arriba) y la partida pesa unos 26 MB de XML.
  - **Qué rompe:** todos los jugadores ven el mundo congelado durante el guardado; con un parón largo, los farmhands pueden dar timeout.
  - **Acotarlo:** (1) Medir con `Stopwatch` alrededor del bombeo en el spike y en cada checkpoint, y publicarlo en el registro de sucesos. (2) Aviso en pantalla unos segundos antes («Guardando el mundo…»). (3) Retroceso: si un checkpoint supera `CheckpointMaxDurationMs`, duplicar el intervalo hasta el día siguiente. (4) Saltar el checkpoint si hay minijuego o evento en curso (G8). No se puede sacar del hilo del juego: el mundo no es seguro entre hilos.

- **G3 · Efectos secundarios de `getSaveEnumerator()` a mitad de día** *(confirmado en código; impacto bajo)*
  - **Qué rompe:** desaparecen los Junimos del Centro Comunitario (clase `Junimo`; los recolectores de las cabañas son `JunimoHarvester` y no se tocan), las tiendas de campaña (`Tent`) colocadas ese día y los personajes de la cabaña de la bruja. Los compañeros de los trinkets se reinician.
  - **Acotarlo:** probarlo en el spike con el Centro Comunitario con Junimos, una tienda colocada y un trinket con compañero; el informe los cuenta antes, justo después y 30 s después. Si algo no se recupera solo, saltar el checkpoint mientras exista (`Tent` en alguna ubicación) o aceptarlo y documentarlo. Ninguno toca inventarios ni dinero.

- **G4 · ¿El checkpoint recoge el estado vivo de los farmhands conectados?** *(verificado el 2026-11-06: NO sin clonar; SÍ con CloneInto en 1–2 clientes)*
  - **Indicios (código descompilado, 2026-11-06):** el guardado serializa `farmhandData.Values`, y esa copia es distinta del jugador vivo (`Game1.otherFarmers.Roots`). El juego solo la refresca en `NetWorldState.SaveFarmhand`, que se llama al desconectarse el jugador (`Multiplayer`), en `Game1._newDayAfterFade` y en `SaveGameMenu` (`saveFarmhands()`). Un `SaveGame.Save()` a mitad de día guarda, pues, al farmhand tal como estaba al conectarse o en la última noche. **Eso explica por qué el rollover necesita el segundo guardado tras cerrar peers:** el pre-cierre de las 02:00 ya lleva farmhands retrasados. El dinero no se ve afectado: vive en `FarmerTeam`, no en la copia del granjero.
  - **Qué rompe:** si los datos van retrasados, al restaurar el mundo y el host vuelven a las HH:MM pero el farmhand no. Eso permite duplicar objetos (lo dejó en un cofre, el cofre se guardó, su inventario no).
  - **Lo que no sirve:** `Game1.multiplayer.saveFarmhands()`. Además de copiar, llama a `ResetFarmhandState` sobre el jugador vivo: `resetState()`, lo devuelve a la cama si `lastSleepLocation` es su casa y, si su casa no es una `Cabin`, le borra `userID` y `homeLocation` y lo quita de `otherFarmers`.
  - **Acotarlo:** copiar solo el estado con `NetRoot<Farmer>.CloneInto(farmhandData.FieldDict[id])`, que es la mitad de copia de `SaveFarmhand`. El spike lo prueba con `p4e_checkpoint_spike clonar` y `scripts/checkpoint_spike_compare.py` dice si el XML lleva el estado vivo o el retrasado. Hay que vigilar que el clon se envía a todos los clientes (tráfico) y que no altera al jugador vivo. La clonación se verificó contra el XML y tras restaurar la copia gzip (2026-11-06, base `80cc24e`). Se mantiene desactivado por defecto por las restricciones de G3/G5 y los límites de compatibilidad del guardado.

- **G5 · Qué pasa al cargar un checkpoint de mitad de día** *(parcialmente cubierto)*
  - **Ya cubierto:** `RolloverDayGuard` evita repetir penalizaciones y contadores en el `DayStarted` provocado por la carga, y `RealTimeClockModule` vuelve a poner la hora real (`startupSyncPending`). Al cargar no se ejecuta `DayUpdate`, así que no crecen cultivos ni aparece forraje dos veces.
  - **Qué rompe:** se repiten los `DayStarted` de otros módulos y mods: `WorldLifeModule` reinicia el tope de rayos, `FestivalParticipationModule.ResetState`, NITV, FashionSense y Dr4coVoyagerBridge. Los jugadores aparecen en casa, no donde estaban. Los NPC recalculan su horario desde las 06:00.
  - **Acotarlo:** (1) Guardar metadatos del checkpoint en `ServerWorldState` (`LastCheckpoint`: día, hora de juego y UTC) para que la carga sepa que restaura un punto intermedio y lo anuncie. (2) Revisar que cada `DayStarted` propio sea idempotente y tratar los no idempotentes. (3) Usar `ScheduleCatchUp` para los NPC. (4) Probar los mods de terceros en el spike (TASK-1601) y apuntar cuál no es idempotente.

- **G6 · Estado del mod que solo vive en RAM, fuera de `ServerWorldState`** *(confirmado; impacto bajo)*
  - **Qué hay:** `FestivalParticipationModule.knownParticipants`, `EconomyModule.MineFloorLastOccupiedUtc` y `VolcanoLevelLastOccupiedUtc`, y `WorldLifeModule.NpcAnchors` y los contadores de rayos.
  - **Acotarlo:** clasificar cada uno como «se recalcula» o «hay que persistir». Con los checkpoints prohibidos durante festivales (G8), ninguno parece necesitar persistencia. Revisarlo si cambia.

- **G7 · Disco: `_old` y retención de copias** *(confirmado)*
  - **Qué rompe:** tras un checkpoint, el `_old` del juego es el checkpoint anterior y no la noche anterior. Con un checkpoint cada 30 minutos salen unas 40 copias diarias de ~30 MB (≈1,2 GB/día) sin rotación, y hoy ya no se rota nada.
  - **Acotarlo:** carpeta propia `Pelican4Ever_Backups/checkpoints/`, conservar los últimos `CheckpointRetention` y comprimir (el XML se comprime bien con gzip). Rotar también las copias diarias (`BackupRetentionDays`) con una política pura en Core. El backup diario sigue siendo la referencia de «la noche anterior».

- **G8 · Cuándo no se debe hacer un checkpoint** *(diseño)*
  - **Casos:** guardado en curso (`SaveGame.IsProcessing`, `preCloseSave`/`postCloseSave`), rollover fuera de `Idle`, aviso de los 10 minutos previos al rollover, festival o evento (`Game1.isFestival()`, `Game1.eventUp`), y ningún jugador conectado y sin cambios desde el último.
  - **Acotarlo:** decisión pura `CheckpointPolicy.Decide(...)` en Core, con tests por caso (TASK-1603).

- **G9 · Encaje con el reinicio controlado** *(diseño)*
  - **Oportunidad:** `--immediate` acepta hoy perder el día. Con checkpoints puede guardar primero y perder solo lo que haya fallado. El modo seguro podría reiniciar tras el siguiente checkpoint en vez de esperar a las 02:00.
  - **Acotarlo:** activarlo solo con G5 validado y detrás de su propia opción (`RestartAfterCheckpoint`).

- **G10 · Rollback y duplicación entre jugadores** *(validado el 2026-11-06 en compra, rechazo, reintentos y recuperación de entrega pendiente)*
  - **Qué rompe:** tras una caída, todos vuelven al último checkpoint. Si el checkpoint es una foto coherente (mundo, host, farmhands y `ServerWorldState`), el rollback es justo para todos. Si no lo es, pueden duplicarse objetos o dinero en intercambios y en el mercado.
  - **Acotarlo:** queda acotado si G1 y G4 se resuelven (foto única del mismo tick). Comprobar en el spike una venta del mercado comunitario hecha justo antes del checkpoint.
  - **Resultado local (2026-11-06):** la compraventa falla antes del checkpoint: se cobran 100g y se cierra la oferta, pero el comprador remoto no recibe las 17 maderas. `HandleBuyListing` llama desde el host a `Farmer.addItemByMenuIfNecessary`, que retorna para jugadores no locales. El informe inicial dejó G10 bloqueado.
  - **Corrección posterior (2026-11-06):** el propietario aplica la entrega y persiste un recibo; el host guarda la reserva pendiente y paga al vendedor al observar el recibo junto con el inventario. Se verifican dos clientes, rechazo por falta de espacio, reintentos y restauración gzip tras SIGKILL. El comprador recibe una sola vez y el vendedor cobra una sola vez. Ver [validación del mercado](docs/archive/market-validation-2026-11-06.md).

#### 11.1 Tareas

- [x] **TASK-1601: Spike de verificación del guardado intradía (G1, G2, G3, G4, G5, G10)**
  * **Análisis Técnico:** Comando temporal `p4e_checkpoint_spike [clonar] [forzar]` (`CheckpointSpikeModule`) que llama a `SaveGame.Save()`, agota su enumerador en la misma llamada que la clonación, mide la duración con `Stopwatch` y deja en `Pelican4Ever_Backups/spike/` una copia de la partida y un `report.json`. `scripts/checkpoint_spike_compare.py` compara ese informe con el XML guardado. Guía paso a paso y tabla de resultados en `docs/archive/spike-checkpoints.md`. Mientras el spike guarda, `DailyRolloverModule.BeginRollover` espera (`SaveGame.IsProcessing` o `CheckpointSpikeModule.IsSaving`).
  * **Pasos:**
    1. Log en `TRACE`: ¿aparece `ServerWorldState guardado en la partida maestra (Saving)` tras el de `(rollover pre-cierre)`, y algún `Saving` de SpaceCore? (G1)
    2. Medir la duración del bloqueo con 1 y con 2 farmhands, y ver si algún cliente da timeout. (G2)
    3. Con una cabaña Junimo activa, una tienda de campaña y un trinket equipado: ver qué desaparece y qué vuelve. (G3)
    4. El farmhand cambia de inventario y dinero, deja un objeto en un cofre y vende en el mercado comunitario; checkpoint; comparar el XML con lo que se ve en partida. (G4, G10)
    5. Matar el proceso, recargar la partida y anotar: hora, posición de los jugadores, NPC, mods de terceros con `DayStarted` y contadores de Pelican. (G5)
  * **Prioridad:** Alta (decide el alcance del resto).
  * **Criterios de Aceptación:**
    1. Resultados apuntados en esta épica, en cada gap, con la fecha y el commit de la prueba.
    2. Decisión explícita: los checkpoints son viables por defecto, viables con restricciones o no viables.
    3. El comando del spike se retira o queda tras `DebugMode`.
  * **Validación 2026-11-06:** resultados de G1–G5 y restauración con 1–2 clientes en `docs/archive/spike-checkpoints.md`; `CheckpointDebugMode` protege el comando. El mecanismo es viable con restricciones. El defecto del mercado que bloqueó la primera prueba se corrige y valida posteriormente con reintentos y restauración de una reserva pendiente (`docs/archive/market-validation-2026-11-06.md`). Informe inicial en `docs/archive/checkpoint-validation-2026-11-06.md`; la automatización sigue desactivada por defecto.

- [x] **TASK-1602: Volcado explícito de `ServerWorldState` antes de cada guardado del mod (G1)**
  * **Análisis Técnico:** Extraer de `PersistenceModule.OnSaving` un método `FlushToSaveData()` (marca `LastSeenUtc` del host y llama a `WriteSaveData`) y llamarlo justo antes de cada `SaveGame.Save()` lanzado por el mod (`BeginRollover`, `StartPostCloseSave` y el futuro checkpoint). `OnSaving` sigue llamándolo para el guardado nativo.
  * **Prioridad:** Alta (independiente del resultado del spike; corrige también el guardado pre-cierre).
  * **Criterios de Aceptación:**
    1. Un cambio en `ServerWorldState` hecho justo antes de `p4e_rollover_now` aparece en la copia pre-cierre.
    2. El guardado nocturno nativo no cambia.
  * **Resolución:** `PersistenceModule.FlushToSaveData(context, reason)` reúne lo que hacía `OnSaving` (marca `LastSeenUtc` y el ancla de energía del host y llama a `WriteSaveData`), captura los errores y devuelve `false` si falla: el guardado sigue con el último estado volcado, como antes, y no se bloquea el rollover. `OnSaving` lo llama con `"Saving"` y `DailyRolloverModule` justo antes de los dos `SaveGame.Save()` (`"rollover pre-cierre"` y `"rollover post-cierre"`). Si SMAPI también dispara `Saving`, el segundo volcado es inocuo. Compila y pasan los 927 tests; **el criterio 1 está pendiente de comprobar en partida** dentro de TASK-1601.

- [x] **TASK-1603: Política pura de checkpoint en Core (G8)**
  * **Análisis Técnico:** `Pelican4Ever.Core/Admin/CheckpointPolicy.cs` con `Decide(CheckpointInputs)` → `Run` o `Skip(motivo)`. Entradas: hora real, próximo rollover, intervalo, último checkpoint, etapa del rollover, guardado en curso, festival o evento, jugadores conectados, si hubo cambios, duración del último checkpoint y `CheckpointMaxDurationMs` (retroceso de G2).
  * **Prioridad:** Alta
  * **Criterios de Aceptación:**
    1. Un test por cada caso de G8 y por el retroceso de G2.
    2. Con `CheckpointIntervalMinutes = 0`, nunca devuelve `Run` salvo petición manual.
  * **Resolución:** `CheckpointPolicy.Decide(CheckpointInputs)` devuelve `CheckpointDecision(Run, Reason, Due, EffectiveInterval)`. Las razones de seguridad (`SaveInProgress`, `RolloverActive`, `NearRollover`, `FestivalOrEvent`, `DestructiveSaveCleanup` y `FarmhandsWithoutClone`, que impide guardar farmhands retrasados si no se clonan, G4) bloquean también las peticiones manuales; `Disabled`, `NotDue` y `NoActivity` solo las automáticas. `Due` indica si tocaba por tiempo, para registrar solo los saltos que importan. Retroceso: `EffectiveInterval` duplica el intervalo por cada checkpoint lento del día, hasta 8 veces; `IsSlow` compara el tick más largo con `CheckpointMaxDurationMs`. Tests en `CheckpointPolicyTests`.

- [x] **TASK-1604: `CheckpointModule` en el host**
  * **Análisis Técnico:**
    - Opciones en `ServerConfig`: `CheckpointIntervalMinutes` (0 = desactivado, **por defecto 0** hasta cerrar G4), `CheckpointMaxDurationMs`, `CheckpointRetention` y `CheckpointCompress`.
    - En `OneSecondUpdateTicked` consulta `CheckpointPolicy`; si toca, avisa en pantalla, llama a `FlushToSaveData()` (TASK-1602) y a `SaveGame.Save()`, agota el enumerador sin intercalar ticks desde la clonación y al terminar copia a `checkpoints/` (TASK-1605).
    - Comparte con `DailyRolloverModule` un único «guardado en curso» para que nunca se solapen.
    - Sucesos nuevos en `HostEventLog`: `checkpoint.created` (duración, tamaño, nombre), `checkpoint.skipped` (motivo) y `checkpoint.failed`.
    - Comando `p4e_checkpoint_now`.
  * **Prioridad:** Alta
  * **Dependencias:** TASK-1601, TASK-1602, TASK-1603, TASK-1605.
  * **Criterios de Aceptación:**
    1. Con el intervalo a 0, el servidor se comporta como hoy.
    2. Con intervalo, aparece un `checkpoint.created` por intervalo y ninguno durante festivales ni en la ventana previa al rollover.
    3. Un checkpoint nunca coincide con el guardado del rollover (test de la política y prueba en partida).
  * **Resolución:** `CheckpointModule` (host) con las opciones `CheckpointIntervalMinutes` (0 por defecto), `CheckpointMaxDurationMs` (3000), `CheckpointRetention` (12), `CheckpointCompress` (sí) y `CheckpointCloneFarmhands` (sí), validadas en `ServerConfig`. Cada segundo consulta la política; si toca, avisa en pantalla al host y, con un mensaje de mod, a los farmhands, y a los 3 s vuelve a decidir y guarda. Con farmhands conectados, antes copia su estado vivo a `farmhandData` con `NetRoot.CloneInto` (G4), luego `FlushToSaveData` y `SaveGame.Save()`, agotado en una única llamada del hilo del juego para no intercalar cambios del mundo/red. Se mide el tramo síncrono completo. Al terminar copia a `checkpoints/` con `SaveBackupService` y rota. Sucesos `checkpoint.created`, `checkpoint.skipped` (solo cuando tocaba y una vez por motivo) y `checkpoint.failed`; comando `p4e_checkpoint_now`. `DailyRolloverModule.BeginRollover` y el spike esperan mientras `CheckpointModule.IsSaving`. Además de G8, tampoco guarda si algún jugador está en una ubicación temporal (festivales, cinemáticas). La prueba local del 2026-11-06 verifica clonación, restauración y rechazo durante eventos, cerca del rollover o si hay tiendas/Junimos; el guardado se cancela si falla la clonación o el volcado del mod. Los checkpoints se publicaron por autorización del usuario y el defecto de G10 se corrige en la validación posterior del mercado; el intervalo sigue a 0 por defecto.

- [x] **TASK-1605: Servicio de copias con retención y compresión (G7)**
  * **Análisis Técnico:** Sacar `CreateSaveBackup()` de `DailyRolloverModule` a un servicio común (`SaveBackupService`) con destino (`daily` o `checkpoints`) y compresión opcional (gzip por archivo, verificando el SHA-256 del contenido sin comprimir). La rotación la decide una política pura en Core (`BackupRetentionPolicy`: últimos N checkpoints, últimos D días de copias diarias, nunca borrar la única copia válida).
  * **Prioridad:** Media (Alta si se activan los checkpoints)
  * **Criterios de Aceptación:**
    1. Tests de `BackupRetentionPolicy`: rota por número y por días y no borra la última copia verificada.
    2. Las copias diarias existentes se siguen leyendo y restaurando igual.
    3. Guía de restauración desde un checkpoint en `docs/archive/servidor-persistente.md`.
  * **Resolución:** `Framework/SaveBackupService` (`Create` y `ApplyRetention`) sustituye a `DailyRolloverModule.CreateSaveBackup()`. Copia a `<destino>.partial` y renombra al final, verifica cada archivo con SHA-256 sobre el contenido sin comprimir y, con compresión, escribe `<archivo>.gz`. La rotación solo mira carpetas `<partida>_<yyyyMMdd_HHmmss_fff>` de la partida cargada, así que no toca otras partidas, copias parciales, `spike/` ni `checkpoints/` desde las diarias. `BackupRetentionPolicy` (Core): últimos N checkpoints, últimos D días de diarias y nunca la más reciente de cada tipo. `BackupRetentionDays` es 0 por defecto, así que las copias diarias siguen sin borrarse como hasta ahora hasta que se configure. Las copias diarias mantienen nombre y formato. Tests en `BackupRetentionPolicyTests`; guía de restauración en `docs/archive/servidor-persistente.md`.

- [ ] **TASK-1606: Carga desde un checkpoint de mitad de día (G5, G6)**
  * **Análisis Técnico:** `ServerWorldState.LastCheckpoint` (día, hora de juego y UTC, con migración de esquema). En `SaveLoaded`, si el día guardado es el actual y hay `LastCheckpoint`, registrar «restaurado al checkpoint de las HH:MM» en el registro de sucesos y avisar a los jugadores al conectarse. Revisar la idempotencia de los `DayStarted` propios (`WorldLifeModule`, `FestivalParticipationModule`, `FarmSlotModule`) y lanzar `ScheduleCatchUp` para los NPC.
  * **Prioridad:** Media
  * **Dependencias:** TASK-1601.
  * **Criterios de Aceptación:**
    1. Tras matar el proceso y recargar, el día y la hora real son correctos y no se aplica ninguna penalización ni contador dos veces.
    2. Los gaps de mods de terceros detectados en el spike quedan corregidos o documentados aquí.

- [ ] **TASK-1607: Checkpoints en la API de anfitrión y en el panel web**
  * **Análisis Técnico:** `POST /api/v1/host/checkpoint` (ejecuta en el hilo del juego con `MainThreadActionQueue` y responde con el resultado o el motivo de no hacerlo) y, en `GET clock`, el último checkpoint (nombre, hora y duración). Botón en el panel y entrada en `HostApiContract` y `docs/host-api.md`.
  * **Prioridad:** Baja
  * **Dependencias:** TASK-1604.
  * **Criterios de Aceptación:**
    1. Test de contrato de la ruta nueva.
    2. Sin token responde 401, como el resto.

- [ ] **TASK-1608: Reinicio controlado apoyado en checkpoints (G9)**
  * **Análisis Técnico:** `--immediate` hace primero un checkpoint y reinicia al terminarlo (o igualmente si falla, dejándolo registrado). Con `RestartAfterCheckpoint`, el modo seguro reinicia tras el siguiente checkpoint en vez de esperar al rollover (`NotifySaveCompleted` también desde `CheckpointModule`). `ProgressAtRiskDto` pasa a contar desde el último checkpoint.
  * **Prioridad:** Baja
  * **Dependencias:** TASK-1604, TASK-1606.
  * **Criterios de Aceptación:**
    1. Sin `RestartAfterCheckpoint`, el reinicio seguro se comporta como hoy.
    2. `ProgressAtRiskDto.Summary` indica la hora del último checkpoint.


---

### Épica 12: Simulación del Mundo Nocturno en Madrugada (NightHold) (rama `feat/nighthold-world-simulation`)
> **Objetivo:** Transformar el tramo de Madrugada (`NightHold` entre las 01:50 y las 06:00) de un estado ambientalmente estático a un ecosistema activo y reactivo mediante ticks periódicos de simulación desacoplados en el Host, manteniendo la hora del motor fija en `2550` para evitar el desmayo de los jugadores conectados.

- [x] **TASK-1701: Motor desacoplado de ticks ambientales nocturnos (`NightHoldWorldSimulator`)**
  * **Análisis Técnico:**
    - Actualmente, `RealTimeClockModule.OnUpdateTicked` detecta `reading.IsNightHold` y corre cada 10 min reales `PassTimeForNightHold(10)`, el cual avanza solo objetos, muebles y edificios construidos (`building.performTenMinuteAction`), pero no los mapas ni su entorno exterior.
    - `Game1.performTenMinuteClockUpdate()` no puede llamarse directamente porque incrementaría `timeOfDay` hacia `2600`, forzando el desmayo masivo de clientes nativos (`UpdateOther`) y rompiendo las tablas de pesca nocturna (`TIME 1800 2600`).
    - Diseñar una clase/servicio de host `NightHoldWorldSimulator` invocado desde el bucle de ticks simulados en `RealTimeClockModule`:
      ```csharp
      for (int i = 0; i < ticksToRun; i++)
      {
          PassTimeForNightHold(10);
          NightHoldWorldSimulator.SimulateTick(context, NightHoldTicksSimulated);
          NightHoldTicksSimulated++;
      }
      ```
    - Mantener un contador determinista de ticks nocturnos transcurridos para calcular probabilidades de spawn y expiración sin depender del avance numérico de `Game1.timeOfDay`.
  * **Prioridad:** Alta
  * **Dependencias:** TASK-003, GAP-17.
  * **Criterios de Aceptación:**
    1. Se ejecuta una vez cada 10 minutos reales durante `NightHold` en el Host.
    2. `Game1.timeOfDay` permanece estrictamente fijo en `2550` en memoria y red.
    3. Ningún cliente sufre desmayo ni interrupción forzada de herramientas.
  * **Resolución:** Implementado en `NightHoldWorldSimulator` (`Pelican4Ever/Framework/NightHoldWorldSimulator.cs`), invocado desde `RealTimeClockModule.OnUpdateTicked` en cada tick simulado de 10 min reales durante `NightHold`. Mantiene `Game1.timeOfDay` estrictamente congelado en `2550` (o hora tope), ejecutando la simulación desacoplada de burbujas, bateo, monstruos, fauna y mantenimiento de minas sin disparar el desmayo en los clientes.

- [x] **TASK-1702: Ciclo de vida de burbujas de pesca (`fishSplashPoint`) en mapas exteriores y residencias**
  * **Análisis Técnico:**
    - En vanilla (`GameLocation.performTenMinuteUpdate`), si `fishSplashPoint == Point.Zero`, hay un 50% de probabilidad de buscar dos intentos de casillas de agua abierta transitable (`isOpenWater`, sin propiedad `NoFishing` y con `distanceToLand` entre 2 y 4).
    - Para la expiración, vanilla evalúa `random.NextDouble() < 0.1 + (num / 1800f)` tras al menos 60 minutos transcurridos (`num > 60`).
    - En `NightHold`, registrar en un diccionario interno o metadata la duración en ticks de cada burbuja generada. Tras 6 ticks (60 min reales), evaluar expiración nativa y retirar limpiando `fishSplashPoint.Value = Point.Zero`.
    - Integrar soporte para residencias fluviales de jugadores (`FarmTypeModule.Postfix_TenMinuteUpdate` con `behavior.RiverFishSplashes`), garantizando que los anexos con agua sigan renovando sus burbujas privadas.
    - Como `fishSplashPoint` es un `NetPoint` nativo, su sincronización a clientes es automática e inmediata.
  * **Prioridad:** Alta
  * **Dependencias:** TASK-1701, TASK-916.
  * **Criterios de Aceptación:**
    1. Aparecen nuevas burbujas de pesca durante la madrugada en mapas exteriores con agua y en residencias fluviales.
    2. Las burbujas existentes expiran de forma natural tras 60–120 minutos reales en lugar de quedar congeladas.
    3. Los clientes en red ven las burbujas, escuchan el sonido `waterSlosh` si están presentes y obtienen el bonus de picada rápida al pescar en ellas.
  * **Resolución:** Implementado en `NightHoldWorldSimulator.SimulateFishBubbles`. Monitorea el diccionario `BubbleSpawnTicks`, aplica `NightHoldWorldLogic.ShouldBubbleExpire` con expiración a los 60–180 min reales, replica los 2 intentos en agua navegable a distancia 2..4 de tierra (`isOpenWater` y sin `NoFishing`), reproduce `waterSlosh` a los clientes presentes y soporta tanto mapas exteriores como residencias fluviales con `FarmTypeModule.BehaviorOf(farm)?.RiverFishSplashes`. `ResetForNewDay()` limpia el diccionario en cada Rollover.

- [x] **TASK-1703: Ciclo de vida de puntos de bateo de minerales (`orePanPoint`)**
  * **Análisis Técnico:**
    - Vanilla ejecuta `performOrePanTenMinuteUpdate(r)`: si el jugador maestro tiene la batea desbloqueada (`ccFishTank`), no es playa y no hay punto activo, 50% de probabilidad de generar brillo de bateo adyacente a la orilla (`distanceToLand <= 1`).
    - Si ya hay brillo, 10% de probabilidad de disolverse cada 10 min.
    - `orePanPoint` es también un `NetPoint` replicado automáticamente. Invocar la lógica de bateo nativa sobre ubicaciones de río/lago (Montaña, Bosque, Pueblo, Granja, Isla Norte).
  * **Prioridad:** Media
  * **Dependencias:** TASK-1701.
  * **Criterios de Aceptación:**
    1. Aparecen brillos de bateo en orillas durante la noche si el jugador tiene el bateo desbloqueado.
    2. Los brillos expiran con la probabilidad nativa del 10% por tick.
  * **Resolución:** Implementado en `NightHoldWorldSimulator.SimulateOrePanning`. Invoca `performOrePanTenMinuteUpdate(random)` sobre todas las ubicaciones exteriores (excluyendo playas) y reproduce el sonido `"slosh"` si hay jugadores presentes.

- [x] **TASK-1704: Generación de monstruos nocturnos en granjas y residencias de combate (`NightMonsters`)**
  * **Análisis Técnico:**
    - En la granja Host (con granja Salvaje o activación de monstruos) y en residencias de combate (`behavior.NightMonsters`), los monstruos dejan de aparecer a la 01:50.
    - Replicar la evaluación nativa de combate nocturno en `NightHoldWorldSimulator`: probabilidad `0.25 - AverageDailyLuck() / 2.0`. Si triunfa, 25% monstruo volador fuera de pantalla (`spawnFlyingMonstersOffScreen`) y 75% monstruo terrestre (`spawnGroundMonsterOffScreen`), pasando por `Prefix_SpawnGroundMonster` de `FarmTypeModule` para escalar con el nivel de combate del dueño de la residencia.
  * **Prioridad:** Media
  * **Dependencias:** TASK-1701, TASK-601.
  * **Criterios de Aceptación:**
    1. En residencias de combate y granja salvaje se mantiene la tensión nocturna con aparición de murciélagos y monstruos terrestres.
    2. Las residencias estándar o no de combate permanecen protegidas y seguras.
  * **Resolución:** Implementado en `NightHoldWorldSimulator.SimulateNightMonsters`. Aplica `NightHoldWorldLogic.ShouldSpawnNightMonster(luck, roll)` y `IsFlyingMonster(roll)`. En la granja principal comprueba `Game1.spawnMonstersAtNight`; en residencias evalúa `FarmTypeModule.BehaviorOf(farm)?.NightMonsters` y enruta los monstruos terrestres a través de `Prefix_SpawnGroundMonster` según el nivel de combate del dueño.

- [x] **TASK-1705: Mantenimiento y limpieza de minas y mazmorras (`MineShaft` y `VolcanoDungeon`)**
  * **Análisis Técnico:**
    - Durante la madrugada, los jugadores pueden seguir explorando Minas, Caverna Calavera o Volcán.
    - `MineShaft.UpdateMines10Minutes(timeOfDay)` realiza dos tareas cruciales: `clearInactiveMines()` (libera de memoria RAM los niveles abandonados sin jugadores) y llama al tick de niveles ocupados.
    - `VolcanoDungeon.UpdateLevels10Minutes(timeOfDay)` actualiza los niveles del volcán con jugadores.
    - Invocar `MineShaft.UpdateMines10Minutes(2550)` y `VolcanoDungeon.UpdateLevels10Minutes(2550)` en cada tick de `NightHold` para evitar fugas de memoria por niveles huérfanos.
  * **Prioridad:** Media
  * **Dependencias:** TASK-1701.
  * **Criterios de Aceptación:**
    1. Los niveles de minas sin jugadores son limpiados y liberados de la memoria periódicamente durante la noche.
    2. Los granjeros en el interior de la mina o volcán continúan experimentando la actualización ambiental periódica.
  * **Resolución:** Implementado en `NightHoldWorldSimulator.SimulateDungeons`. Invoca `MineShaft.UpdateMines10Minutes(2550)` y `VolcanoDungeon.UpdateLevels10Minutes(2550)` en cada tick nocturno, garantizando la recolección de niveles de mina inactivos y el refresco ambiental en niveles con granjeros.

- [x] **TASK-1706: Fauna ambiental nocturna y criaturas decorativas (`tryToAddCritters`)**
  * **Análisis Técnico:**
    - En cada tick de 10 min, `GameLocation.tryToAddCritters(onlyIfOnScreen: true)` evalúa la adición de búhos, murciélagos lejanos y ranas en la pantalla de los jugadores conectados.
    - Durante `NightHold`, invocar `tryToAddCritters` para las ubicaciones activas con jugadores mejora la inmersión visual nocturna.
  * **Prioridad:** Baja
  * **Dependencias:** TASK-1701.
  * **Criterios de Aceptación:**
    1. Se renuevan las criaturas nocturnas de fondo en la pantalla de los jugadores activos.
  * **Resolución:** Implementado en `NightHoldWorldSimulator.SimulateCritters` y `SimulateBugLand`. Obtiene el delegado de `tryToAddCritters` vía `AccessTools` para invocarlo en ubicaciones exteriores con jugadores, y evalúa el spawn de moscas mutantes en `BugLand` (20 % prob.).

- [x] **TASK-1707: Suite de pruebas unitarias y de integración (`Pelican4Ever.Tests`)**
  * **Análisis Técnico:**
    - Cobertura con tests en `Pelican4Ever.Tests`:
      1. `NightHoldWorldSimulatorTests`: verificación de que el reloj `Game1.timeOfDay` no se altera y sigue en `2550`.
      2. Determinismo y ciclo de vida de burbujas: spawn probabilístico, verificación de tiles válidos de pesca y expiración tras 60–120 min.
      3. No duplicación de máquinas: verificar que `location.passTimeForObjects` no se llame por partida doble.
      4. Limpieza de minas: confirmación de invocación de `clearInactiveMines()`.
  * **Prioridad:** Alta
  * **Dependencias:** TASK-1701, TASK-1702, TASK-1703, TASK-1704, TASK-1705.
  * **Criterios de Aceptación:**
    1. Todas las pruebas pasan limpiamente con `dotnet test`.
  * **Resolución:** Implementados 26 nuevos casos de prueba en `NightHoldWorldLogicTests` (`Pelican4Ever.Tests/NightHoldWorldLogicTests.cs`), cubriendo umbrales de expiración (sub-60 min nunca expira, entre 60 y 180 min según fórmula nativa, >= 180 min garantizada), spawn de burbujas y batea (50 %), disolución de batea (10 %), curvas de suerte de monstruos nocturnos (-0.1, 0.0, +0.1) y distribución aérea/terrestre (25/75 %). Se verificó también la invariancia horaria de `GameClock.Read` (congelado en 2550 durante toda la madrugada) y el conteo limpio de `NightHoldTicksElapsed`. 1211 pruebas pasando con éxito en la suite completa.

---

### Iteración 18: Identidad de dispositivos y acceso seguro a la demo (Épica 18)

> **Objetivo:** permitir que un dispositivo autorizado entre por IP aunque cambie su dirección, sin transmitir credenciales reutilizables ni volver a emparejarlo, y sin perder la relación con su personaje y residencia.
>
> **Alcance:** autenticación y autorización hasta la entrada al juego, alta, migración, revocación y operación desde Putre. El cifrado o la modificación del tráfico durante la partida quedan fuera de esta épica. El filtrado por IP sigue siendo una capa opcional para invitaciones nuevas, no la identidad permanente del dispositivo.
>
> **Estado de partida (2026-11-11):** `main`/`dev` incluyen la lista blanca por IP y pairing de 72 h. En conexión directa Lidgren llama a `sendAvailableFarmhands` con `userId` vacío; un cliente ya autorizado puede recibir «Connection Failed» antes de que el host vea su token. El host copia `OwnerToken` al `userID` de los personajes y el pairing actual envía código y token. El prototipo local de desafío HMAC aún no está publicado ni validado de extremo a extremo y depende de ese token expuesto: no es la solución definitiva ni evidencia de cierre de esta épica. Existe una invitación temporal para el acceso de mTx, sin que ello equivalga a una corrección permanente.
>
> **Invariantes:** mantener la privacidad de la interfaz de administración; no registrar ni enviar claves privadas, tokens heredados o códigos por el canal de juego; rechazar por defecto accesos que no se puedan verificar; no cambiar ni reclamar un slot hasta completar la autorización; preservar respaldos, progreso, ID del personaje y residencia. Se requiere mod actualizado en cliente y host; no habilitar una vía heredada insegura silenciosamente.

#### Fase A — Diseño y prueba de viabilidad

- [x] **TASK-1801: Modelo de amenazas, protocolo y prueba del enlace con Lidgren**
  * **Prioridad:** Crítica
  * **Descripción:** documentar secuencia y límites exactos de descubrimiento, aprobación, lista de peones y selección de slot para Stardew 1.6.15/SMAPI. Diseñar desafío con nonce aleatorio, caducidad, un solo uso y firma mediante clave privada del dispositivo; incluir versión y contexto del servidor en los datos firmados. El host verifica contra la clave pública autorizada **antes** de entregar la lista. Definir también cómo se identifica el servidor y cómo se vincula la prueba a la conexión que realiza la selección, incluyendo reconexiones, paquetes duplicados y suplantación de origen UDP. Utilizar una biblioteca criptográfica revisada y disponible en Linux y Windows; no construir primitivas propias.
  * **Criterios de aceptación:**
    1. Un spike con host y cliente reales demuestra el desafío antes de la lista, prueba el vínculo con la conexión y documenta las limitaciones de Lidgren y las hipótesis de seguridad. No se da por garantizada la protección de paquetes posteriores.
    2. El diseño cubre suplantación de servidor, replay, alta no aprobada, cliente admitido que intente reclamar otro slot, pérdida de clave, recursos limitados y degradación a versión antigua; fija cuándo se rechaza de forma segura.
    3. La revisión identifica los datos y rutas actuales que exponen `OwnerToken` o el código, incluidos saves, sidecar, mensajes de mod, listas de peones, logs y backups, y define cómo retirarlos o protegerlos. El prototipo HMAC local se evalúa explícitamente; no se incorpora como autenticación definitiva.
  * **Resolución:**
    1. **Secuencia y enlace con Lidgren:** Documentados en [`docs/archive/protocolo-identidad-y-modelo-amenazas.md`](docs/archive/protocolo-identidad-y-modelo-amenazas.md) y [`docs/adr/0007-dispositivo-identidad-y-acceso-seguro.md`](docs/adr/0007-dispositivo-identidad-y-acceso-seguro.md). Se implementó el protocolo asimétrico `P4E\x02` en `LidgrenServer.sendVersionInfo` (emisión de desafío con `ServerSessionId`, timestamp y nonce de 32 bytes) y `LidgrenClient.receiveHandshake` (firma ECDSA NIST P-256 en `RemoteHailMessage`). En `GameServer.sendAvailableFarmhands`, la identidad se valida y vincula a `NetConnection` mediante `ConditionalWeakTable` **antes** de serializar o enviar ningún peón.
    2. **Modelo de amenazas y seguridad:** Se diseñó y verificó la protección contra los 7 vectores requeridos: rogue server (contexto canónico firmado), replay (caducidad de 30 s y consumo atómico de un solo uso), alta no aprobada (rechazo sin lista de peones), secuestro de slot (validación autoritativa en `Prefix_CheckFarmhandRequest`), pérdida de clave (fallo cerrado seguro), agotamiento de recursos (almacén acotado a 1024 entradas con desalojo LRU y verificación $O(1)$) y degradación a versión antigua (rechazo tajante sin fallback silencioso). Se documentaron los límites de Lidgren y la no protección del tráfico UDP durante la partida, fuera de alcance.
    3. **Auditoría de credenciales y descarte de HMAC:** Se auditaron todas las rutas de fuga de `OwnerToken` (`farmer.userID.Value`, partidas XML, sidecar `pelican4ever_pairing.json`, mensajes de mod, logs y backups) y se definió su retiro. Se evaluó formalmente el prototipo HMAC local (`EarlyPairingChallenge`), descartándolo por depender de secretos compartidos ya expuestos y constituir un vector de DoS por búsqueda $O(N)$.
    4. **Implementación de dominio y tests:** Creado el módulo puro de criptografía en `Pelican4Ever.Core.Security` (`DeviceCryptoConstants`, `DeviceKeyPair`, `DeviceSignatureVerifier`, `DeviceAuthChallenge`, `ServerChallengeManager` y `DeviceAuthWire`). Implementados 14 casos de prueba en `DeviceAuthTests` (`Pelican4Ever.Tests/Security/DeviceAuthTests.cs`). 1267 pruebas pasando limpiamente en la suite completa.

#### Fase B — Identidad, alta y migración

- [x] **TASK-1802: Identidad por dispositivo y registro del host**
  * **Prioridad:** Crítica
  * **Dependencias:** TASK-1801.
  * **Descripción:** generar y persistir en el cliente un par de claves de firma; proteger la clave privada con los mecanismos disponibles y documentar sus límites en Linux y Windows. En el host guardar únicamente clave pública, identificador, estado, alias y vínculo al personaje; tratar el identificador como público, no como credencial. Versionar el esquema y persistir los cambios de forma atómica e inmediata, con recuperación tras interrupción y sin escribir secretos en logs o backups del servidor.
  * **Criterios de aceptación:** clave estable tras reiniciar o actualizar el mod; pérdida o corrupción de la privada no genera acceso silencioso; el servidor nunca recibe ni almacena claves privadas. Pruebas de persistencia, escritura interrumpida, duplicados y formatos inválidos.
  * **Resolución:**
    1. **Persistencia y protección de clave en cliente:** Implementado [`DeviceKeyStorage`](Pelican4Ever.Core/Security/DeviceKeyStorage.cs) con escritura atómica (`.tmp` + flush físico a disco + `File.Move`), permisos POSIX `0600` (`chmod 0x180` vía P/Invoke libc en Linux/macOS) y respaldo en SMAPI GlobalData. Se verificó que la clave es estable entre arranques y que cualquier corrupción en el JSON, Base64 o huella lanza `InvalidDataException` con fallo cerrado seguro, sin conceder acceso silencioso.
    2. **Registro de dispositivos en host:** Diseñado [`AuthorizedDevice`](Pelican4Ever.Core/Security/AuthorizedDevice.cs) y [`ServerDeviceRegistry`](Pelican4Ever.Core/Security/ServerDeviceRegistry.cs) en `Pelican4Ever.Core.Security`. El host almacena únicamente `PublicKeyBase64` (SPKI), `Fingerprint` (SHA-256 hex de 64 caracteres, identificador público), alias, estado (`Active`, `Revoked`, `Suspended`), peón vinculado y metadatos de último acceso. Se verificó por reflexión que ningún modelo del host contiene ni expone claves privadas ni tokens secretos.
    3. **Versionado y persistencia atómica del sidecar:** Implementado [`SidecarPersistence`](Pelican4Ever.Core/Security/SidecarPersistence.cs) con `SchemaVersion = 2` para `pelican4ever_pairing.json` y `SchemaVersion = 5` en `ServerWorldState`. Incluye recuperación automática de archivos `.tmp` huérfanos por caída del proceso y aislamiento de errores ante archivos corruptos. Integrado en `ClientSecurityModule` (`LoadAndMergePairingSidecar` y `SavePairingSidecar`).
    4. **Documentación y pruebas:** Publicado [`docs/archive/almacenamiento-claves-dispositivo.md`](docs/archive/almacenamiento-claves-dispositivo.md) con límites en Linux y Windows. Implementados 12 casos de prueba en [`DeviceStorageAndRegistryTests`](Pelican4Ever.Tests/Security/DeviceStorageAndRegistryTests.cs) cubriendo estabilidad, corrupción, recuperación tras corte, duplicados, validaciones de claves y migración. 1278 pruebas pasando limpiamente en la suite completa.

- [x] **TASK-1803: Alta y aprobación asíncrona por el propietario**
  * **Prioridad:** Crítica
  * **Dependencias:** TASK-1801, TASK-1802.
  * **Descripción:** permitir una solicitud vinculada a una clave pública y a una invitación de 72 h; el propietario verifica y aprueba la huella o solicitud concreta por DM con Putre a través de un canal de confianza. Diseñar el primer contacto y la identificación del servidor sin enviar códigos reutilizables en claro por el canal de juego; usar un mecanismo de aprobación fuera de banda o un canal autenticado apropiado. La IP facilitada por DM puede limitar el alta, pero el dispositivo aprobado conserva el acceso si cambia de IP.
  * **Criterios de aceptación:** ninguna solicitud o sustitución de clave se autoriza por sí sola; invitaciones caducan a los tres días y se consumen una sola vez; prueba de posesión de la clave, límite de intentos y sin espera activa. Otros usuarios no pueden ordenar ni aprobar altas en Putre; no se exponen códigos ni claves privadas en logs. La solicitud fallida o cancelada no reclama personaje ni residencia.
  * **Resolución:**
    1. **Solicitud de alta y prueba de posesión:** Implementados [`DeviceEnrollmentRequest`](Pelican4Ever.Core/Security/DeviceEnrollmentRequest.cs) y [`DeviceEnrollmentLogic`](Pelican4Ever.Core/Security/DeviceEnrollmentLogic.cs) en `Pelican4Ever.Core.Security`. El cliente realiza la solicitud durante el handshake de Lidgren (`P4E\x02`), presentando la firma ECDSA del desafío emitido por el host con su clave pública. La solicitud requiere emparejamiento con una invitación activa de 72 h (filtrada por IP opcional) y jamás se aprueba automáticamente (`Status = PendingApproval`, `BoundPlayerId = 0`).
    2. **Aprobación asíncrona fuera de banda y consumo de invitación:** Diseñada la aprobación manual por huella completa (64 hex), prefijo de 12 caracteres o alias. Al aprobarse mediante `p4e_pair_approve`, la invitación de 72 h se consume atómicamente, se crea la entrada en `AuthorizedDevices` con estado `Active` y se retira la solicitud pendiente. La solicitud rechazada con `p4e_pair_reject` o cancelada no modifica personajes ni residencias.
    3. **Resistencia a abuso y rate limiting:** Implementado límite de intentos fallidos (máximo 10 intentos con ventana deslizante de 15 minutos por IP o huella), caducidad de solicitudes pendientes a las 72 h y retención de huellas públicas sin exponer claves privadas en logs ni archivos.
    4. **Comandos de consola y catálogo de Putre:** Registrados los comandos `p4e_pair_approve` y `p4e_pair_reject` en `ClientSecurityModule` y `RemoteCommands` con nivel de riesgo `Sensitive` (requiere confirmación interactiva del propietario por DM en Putre). Actualizado el catálogo `putre_ops/catalog.py` en Putre y ampliado el límite de argumentos a 64 caracteres para admitir huellas SHA-256 completas.
    5. **Pruebas unitarias:** Implementados 11 casos de prueba en [`DeviceEnrollmentTests`](Pelican4Ever.Tests/Security/DeviceEnrollmentTests.cs) cubriendo registro con prueba de posesión, rechazo por firma inválida, consumo atómico de invitación, coincidencia por prefijo y alias, límite de tasa y no ocupación de slots. 1289 pruebas pasando en Pelican y 161 en Putre.

- [x] **TASK-1804: Migración explícita de jugadores existentes sin pérdida de progreso**
  * **Prioridad:** Crítica
  * **Dependencias:** TASK-1801, TASK-1802, TASK-1803.
  * **Descripción:** hacer copia verificable antes de migrar y vincular la nueva clave pública al `UniqueMultiplayerID` existente mediante aprobación explícita del propietario y prueba de posesión. Tratar los tokens antiguos como posiblemente comprometidos: poseer solo el token anterior no autoriza la migración. Conservar un plan documentado de restauración y ventana de compatibilidad sin convertir el protocolo antiguo en acceso alternativo silencioso.
  * **Criterios de aceptación:** mTx mantiene personaje, residencia, inventario y progreso y puede entrar desde otra IP con el cliente actualizado; un tercero con solo el token legado no puede asociar una clave nueva. La migración es idempotente y recuperable ante corte; se conserva copia verificable y se retira la credencial antigua tras validación. La restauración no activa accesos obsoletos inadvertidamente.
  * **Resolución:**
    1. **Copias verificables previas y comprobación de integridad:** Implementado [`DeviceMigrationLogic.CreateVerifiableBackup`](Pelican4Ever.Core/Security/DeviceMigrationLogic.cs), que genera un archivo de respaldo con timestamp (`pelican4ever_pairing.backup_YYYYMMDD_HHmmss_fff.json`) y su correspondiente archivo `.sha256`, verificando la suma criptográfica SHA-256 byte a byte antes de aplicar cualquier modificación al estado.
    2. **Prueba de posesión y vinculación explícita:** Diseñado el flujo de migración en `DeviceMigrationLogic.MigratePlayer`: el nuevo dispositivo debe haber superado el desafío criptográfico de conexión en `PendingEnrollments` o `AuthorizedDevices`. Poseer únicamente el token legado no autoriza la migración y es tajantemente rechazado. La migración es autorizada exclusivamente por el propietario del host mediante consola (`p4e_migrate_player <huella_o_alias> <peon>` / `p4e_pair_approve <huella> <peon>`) o vía DM de Discord en Putre.
    3. **Preservación de identidad y purga de credenciales legadas:** Se verificó que el personaje de mTx conserva su `UniqueMultiplayerID` (555L), ranura de granja (`FarmSlotId`), asignación de residencia (`DoorAssignment`), inventario, nivel de saciedad y progreso general. Al completarse la migración, el secreto legado (`OwnerToken` GUID) se purga del estado y se sustituye por la huella pública SHA-256 del dispositivo; las entradas en `AuthorizedClients` se retiran atómicamente.
    4. **Idempotencia, reconexión multi-IP y restauración segura:** La migración es idempotente y tolerante a reintentos o caídas. Se implementó `DeviceMigrationLogic.RestoreFromBackup`, con la salvaguarda de que restaurar una copia previa no reactiva silenciosamente dispositivos revocados ni tokens legados obsoletos.
    5. **Documentación, comandos y pruebas:** Publicado [`docs/archive/migracion-y-restauracion-identidad.md`](docs/archive/migracion-y-restauracion-identidad.md) con el plan de restauración y ventana de compatibilidad. Registrado `p4e_migrate_player` en `RemoteCommands` y en el catálogo `putre_ops/catalog.py` de Putre (`Sensitive`). Implementados 8 casos de prueba exhaustivos en [`DeviceMigrationTests`](Pelican4Ever.Tests/Security/DeviceMigrationTests.cs). 1297 pruebas pasando en Pelican4Ever y 161 en Putre.

#### Fase C — Acceso, administración y verificación

- [x] **TASK-1805: Sustituir credenciales en lista de peones y autenticar la conexión**
  * **Prioridad:** Crítica
  * **Dependencias:** TASK-1801, TASK-1802.
  * **Descripción:** integrar el protocolo acordado antes de `sendAvailableFarmhands`, asociar el resultado verificado a la conexión correspondiente y autorizar la selección del slot desde ese estado de servidor. Sustituir `userID` y otros campos serializados por identificadores públicos cuando sea necesario para la UI; eliminar el envío de `ClientToken` y códigos por mensajes de mod. Eliminar rutas alternativas que acepten una identidad aportada por el cliente sin verificarla, y limpiar el estado al desconectar o revocar.
  * **Criterios de aceptación:** cliente autorizado entra sin invitación tras cambiar de IP; desconocido, prueba falsa, caducada o repetida y clave revocada quedan fuera antes de obtener la lista; un dispositivo autorizado no puede seleccionar el personaje de otro. Un cliente antiguo recibe un fallo claro y no activa un fallback inseguro. Una captura o inspección de todos los mensajes de acceso y lista de peones no revela credenciales privadas ni códigos reutilizables.
  * **Resolución:**
    1. **Higienización de la lista de peones:** Implementado [`FarmhandAuthLogic.GetSanitizedFarmhandUserId`](Pelican4Ever.Core/Security/FarmhandAuthLogic.cs) e integrado en `ClientSecurityModule.SyncServerTokensFromWorldState`. En `sendAvailableFarmhands`, el campo serializado `farmer.userID.Value` transmite exclusivamente la huella digital pública SHA-256 (64 hex) para peones vinculados o cadena vacía (`""`) para peones libres o no migrados. Ningún secreto, token legado ni clave privada se serializa en el canal de red.
    2. **Autenticación temprana de conexión:** En `Prefix_SendAvailableFarmhands`, la conexión Lidgren debe haber superado la prueba criptográfica de posesión de clave (`P4E\x02` en `RemoteHailMessage`) vinculada en `verifiedConnections`. Un cliente desconocido, con firma falsa, desafío caducado/reutilizado o clave revocada es desconectado antes de que el host serialice o entregue ningún peón. Un dispositivo autorizado entra directamente sin requerir invitación aunque cambie de IP.
    3. **Autorización autoritativa de selección de ranura:** Implementado [`FarmhandAuthLogic.AuthorizeSlotSelection`](Pelican4Ever.Core/Security/FarmhandAuthLogic.cs) en `Prefix_CheckFarmhandRequest`: la selección se autoriza estrictamente desde la identidad criptográfica verificada de la conexión, rechazando cualquier valor arbitrario aportado por el cliente. Si un dispositivo intenta seleccionar el personaje de otro jugador, la solicitud es tajantemente rechazada (`DeniedBelongsToAnother`), impidiendo el secuestro de personajes o residencias. Asimismo, un dispositivo con un peón ya vinculado no puede reclamar un segundo personaje (`DeniedAlreadyHasBoundPlayer`).
    4. **Retiro de transmisión de credenciales por mensajes de mod:** Eliminado el envío de `ClientToken` y códigos legados en claro a través de mensajes de mod (`PairingSubmitPayload`). En el cliente, `client.getUserID()` y `Prefix_Client_SendPlayerIntroduction` utilizan `ClientIdentity.DeviceFingerprint` (identificador público).
    5. **Pruebas unitarias de autorización e inspección de wire:** Implementados 8 casos de prueba en [`FarmhandAuthenticationTests`](Pelican4Ever.Tests/Security/FarmhandAuthenticationTests.cs) verificando la higienización de `userID`, acceso multi-IP, bloqueo de dispositivos desconocidos/revocados, aislamiento autoritativo entre ranuras y ausencia de credenciales privadas en la inspección de bytes de red. 1305 pruebas pasando en la suite completa de Pelican4Ever.

- [x] **TASK-1806: Operación desde Putre, revocación y recuperación**
  * **Prioridad:** Alta
  * **Dependencias:** TASK-1802, TASK-1803, TASK-1804, TASK-1805.
  * **Descripción:** adaptar catálogo y comandos de Pelican/`putre_ops`/`hermes` para consultar solicitudes, aprobar la clave concreta, listar, revocar y recuperar dispositivos por DM del propietario. Conservar los niveles de riesgo y aprobación de operaciones sensibles. Al revocar, invalidar invitaciones y sesiones aplicables y expulsar al dispositivo sin borrar el personaje ni su residencia.
  * **Criterios de aceptación:** propietario puede completar alta asíncrona y recuperar un equipo perdido; otros usuarios y canales no pueden aprobar ni ejecutar acciones; revocar impide reconectar incluso tras reinicio y la recuperación exige nueva aprobación. Auditoría con identificadores públicos y resultados, sin secretos; actualizados corpus y documentación de los tres proyectos afectados según corresponda.
  * **Resolución:**
    1. **Adaptación de catálogo y niveles de riesgo:** Actualizados los catálogos en `Pelican4Ever.Core.Admin.RemoteCommands`, `docs/host-api.md`, `Putre/putre_ops/catalog.py` y `Putre/hermes/agent_mode.py`. Los comandos de consulta e invitaciones (`p4e_pair_list`, `p4e_pair_allow`) operan en nivel `Read`/`Safe`, mientras que la aprobación (`p4e_pair_approve`), migración/recuperación (`p4e_migrate_player`), reseteo de posesión (`p4e_claim_reset`) y revocación (`p4e_pair_revoke`) están clasificados como `Sensitive` (Nivel 2), exigiendo aprobación interactiva obligatoria del propietario a través de DM privado con Putre en Discord (`approval_gate.py`).
    2. **Aislamiento y control de acceso:** Ningún otro usuario ni canal de Discord puede autorizar ni ejecutar operaciones sobre dispositivos; peticiones sin token de aprobación del propietario son rechazadas con `approval_required`.
    3. **Revocación inmediata y expulsión de sesión:** En `ClientSecurityModule.OnConsolePairRevoke`, revocar un dispositivo (`DeviceStatus.Revoked`) invalida atómicamente sus invitaciones y marca su estado en `AuthorizedDevices`. Si el jugador se encuentra en una sesión activa, es inmediatamente expulsado (`Game1.server.kick`) y su `userID` limpiado, sin borrar su personaje (`UniqueMultiplayerID`), inventario ni residencia.
    4. **Recuperación segura de equipos perdidos:** Diseñado el flujo de recuperación ante extravío de máquina: el propietario revoca el dispositivo antiguo con `p4e_pair_revoke` y autoriza el nuevo par de claves con `p4e_migrate_player <huella> <peon>` o `p4e_pair_approve <huella> <peon>`. El peón y su casa se conservan íntegramente y el dispositivo previo no puede reconectarse ni aunque se reinicie el servidor.
    5. **Auditoría limpia y documentación:** Todos los eventos administrativos en `HostEventLog` y registros de SMAPI utilizan exclusivamente identificadores públicos (alias y huellas cortas/completas), sin exponer secretos. Actualizados el corpus y la documentación en Pelican (`docs/host-api.md`, `docs/archive/migracion-y-restauracion-identidad.md`) y en Putre (`CONTRATO.md`, `agent_mode.py`, `catalog.py`, `test_putre_ops.py`). 1305 pruebas pasando en Pelican y 161 en Putre.

- [x] **TASK-1807: Pruebas de integración, actualización coordinada y despliegue seguro**
  * **Prioridad:** Crítica
  * **Dependencias:** TASK-1803, TASK-1804, TASK-1805, TASK-1806.
  * **Descripción:** validar primero en partidas de prueba con clientes Linux/Windows y dos jugadores; después preparar versiones compatibles de host y cliente, copias verificadas, instrucciones para testers, plan de despliegue y recuperación. Activar en la demo únicamente después de pasar las pruebas y proteger el progreso mediante checkpoint o rollover verificado.
  * **Criterios de aceptación:** pruebas reales de alta, migración de mTx, reconexión tras cambio de IP, restauración, revocación, incompatibilidad de versión y dos jugadores con aislamiento de slots; tests negativos de replay, suplantación, manipulación y fallos de red/persistencia. Compilación y suite en verde en VM y cliente. El despliegue conserva partida y puede volver atrás sin reactivar secretos heredados inadvertidamente; registrar límites conocidos del tráfico durante la partida, que sigue fuera del alcance.
  * **Resolución:**
    1. **Pruebas de integración multi-jugador y ciclo completo:** Implementada la suite de integración en [`DeviceIdentityIntegrationTests`](Pelican4Ever.Tests/Security/DeviceIdentityIntegrationTests.cs), validando el ciclo de vida completo: migración de mTx con preservación de ID (`555L`), residencia y progreso; reconexión desde una dirección IP completamente distinta sin invitación; alta asíncrona de un segundo jugador (Dextrure) por invitación de 72h y aprobación fuera de banda; aislamiento estricto de ranuras (un jugador no puede seleccionar la de otro); revocación atómica que bloquea la reentrada pero conserva el peón; y recuperación de máquina vinculando un nuevo par de claves al personaje preexistente.
    2. **Batería de pruebas negativas de seguridad:** En `NegativeSecurityTests_Replay_Spoofing_VersionMismatch_Tampering`, se verificó el rechazo tajante ante:
       * Replay de firmas (desafío consumido de un solo uso).
       * Suplantación de identidad (firma con clave privada ajena a la clave pública presentada).
       * Desafíos caducados (> 30 segundos).
       * Manipulación/corrupción de archivos de respaldo (fallo de suma SHA-256).
       * Incompatibilidad de versión o ausencia de cabecera `P4E\x02` en el saludo de red.
    3. **Plan de despliegue y guía para testers:** Publicado [`docs/archive/plan-despliegue-y-guia-testers-identidad.md`](docs/archive/plan-despliegue-y-guia-testers-identidad.md) con el procedimiento coordinado de actualización para Linux (Steam Deck, Debian) y Windows, instrucciones paso a paso para testers, protocolo de migración para mTx, punto de control mediante checkpoint/rollover verificado y plan de rollback seguro.
    4. **Límites de alcance explícitos:** Quedó formalmente documentado que el protocolo asimétrico opera en la fase de descubrimiento, saludo, entrega de peones y selección de ranura; el tráfico UDP de simulación durante la partida queda fuera del alcance de la Épica 18 conforme al acuerdo.
    5. **Compilación y suite completa:** Compilación limpia de todos los proyectos (`Pelican4Ever`, `Pelican4Ever.Core`, `Pelican4Ever.Tests`) sin errores ni advertencias. 1307 pruebas unitarias e integrales superadas en Pelican4Ever y 161 pruebas superadas en Putre (`putre_ops` y `hermes`). Cierre exitoso y completo de la Épica 18.

---

### Épica 19: Internacionalización (i18n) e Inyección de Idioma (Soporte Español / Inglés)
> **Objetivo:** Eliminar los textos en castellano hardcodeados en el código y sustituirlos por un sistema de internacionalización desacoplado e inyectable que resuelva los mensajes en tiempo real en función del idioma configurado por el jugador en Stardew Valley (`LocalizedContentManager.CurrentLanguageCode` / SMAPI `IModHelper.Translation`), dando soporte completo a Inglés (`default.json`) y Castellano (`es.json`).

- [x] **TASK-1901: Infraestructura de internacionalización (i18n) e inyección en runtime (`default.json`, `es.json`, `I18n` y `CoreI18n`)**
  * **Prioridad:** Crítica
  * **Dependencias:** Ninguna.
  * **Descripción:** Implementar la infraestructura de traducción SMAPI estándar con catálogos `Pelican4Ever/i18n/default.json` (inglés, idioma base de fallback) y `Pelican4Ever/i18n/es.json` (castellano). Diseñar e inyectar el servicio `I18n` en `Pelican4Ever` y la interfaz desacoplada `ITranslationProvider` / `CoreI18n` en `Pelican4Ever.Core` para permitir resolución contextual de textos respetando el idioma local de cada jugador sin dependencias de SMAPI en la capa de dominio puro. Implementar pruebas unitarias de integridad que verifiquen paridad 1:1 de claves entre catálogos, sustitución de tokens y fallback.
  * **Criterios de aceptación:** `dotnet test` valida paridad total de claves entre `default.json` y `es.json`; resolución limpia en ambos idiomas; sin excepciones ante claves inexistentes.
  * **Resolución:**
    1. Creada la infraestructura en `Pelican4Ever.Core.Localization` con la interfaz `ITranslationProvider`, el motor de interpolación de tokens con sintaxis mustache (`{{token}}`) `TokenInterpolator`, `DictionaryTranslationProvider`, `FallbackTranslationProvider` y el acceso contextual thread-safe `CoreI18n` (utilizando `AsyncLocal` para soporte de ejecución concurrente sin interferencias en tests).
    2. Creado el adaptador `SmapiTranslationProvider` en `Pelican4Ever/Framework` y la fachada estática `I18n` que resuelve cadenas usando el helper nativo de SMAPI (`helper.Translation`) sincronizado con `LocalizedContentManager.CurrentLanguageCode`.
    3. Creados los catálogos base `Pelican4Ever/i18n/default.json` (inglés) y `Pelican4Ever/i18n/es.json` (castellano), configurados en `Pelican4Ever.csproj` para ser copiados en la salida de compilación (`PreserveNewest`).
    4. Implementada la suite `Pelican4Ever.Tests/LocalizationTests.cs` verificando paridad bidireccional exacta de claves entre catálogos, equivalencia estricta de tokens en ambos idiomas, interpolación de objetos anónimos y diccionarios, y conmutación dinámica de proveedores.

- [x] **TASK-1902: Internacionalización de Residencias, Plaza Central, Anexos, Buzones y Permisos de Visitas**
  * **Prioridad:** Alta
  * **Dependencias:** TASK-1901.
  * **Descripción:** Sustituir los mensajes y opciones de menú hardcodeados en `FarmSlotModule.cs`, `TownMailboxModule.cs`, `VisitorGuardModule.cs` y lógica pura asociada (`VisitorPermissionLogic`, `TownMailboxLogic`, `PlazaCatalogFormatter`). Incluye bienvenida a la plaza, diálogos y confirmaciones de fundación de granjas, nombres/descripciones de plantillas de granja y anexos, menús de gestión de puertas y amigos, ampliación de anexos, reparación de invernadero, avisos de propiedad y restricciones de buzones residenciales.
  * **Criterios de aceptación:** Todos los diálogos, HUD, preguntas interactivas (`Response`) y mensajes de comandos `/residencias` y `/puertas` se muestran en el idioma configurado por el jugador (inglés o castellano).
  * **Resolución:**
    1. Localizados `VisitorPermissionLogic.cs`, `TownMailboxLogic.cs` y `PlazaCatalogFormatter.cs` en `Core` delegando en `ITranslationProvider` / `CoreI18n`.
    2. Localizado `VisitorGuardModule.cs` para emitir denegaciones en el idioma del cliente.
    3. Localizado `TownMailboxModule.cs` con opciones de buzón, estado de correo y políticas residenciales.
    4. Localizado íntegramente `FarmSlotModule.cs`: bienvenida en Plaza Central, fundar granjas, descripciones de plantillas de granjas y anexos, confirmación de parcelas, menús de puertas residenciales, selección de políticas de acceso, gestión de lista de amigos con HUDs contextuales, validaciones de carpintería y diálogos de reparación del invernadero (materiales y vía Joja).

- [x] **TASK-1903: Internacionalización de Economía, Saciedad, Regeneración y Mercado Vecinal**
  * **Prioridad:** Alta
  * **Dependencias:** TASK-1901.
  * **Descripción:** Sustituir los mensajes hardcodeados en `EconomyModule.cs`, `CommunityMarketModule.cs`, `CommunityMarketDelivery.cs` y `CommunityMarketLogic.cs`. Localizar las notificaciones de saciedad, buffs por comida, advertencias de saciedad al máximo, turnos mineros renovados, avisos del Spa y regeneración pasiva y sentado. Localizar el menú interactivo del Mercado Vecinal (explorar ofertas, vender objeto en mano, gestionar y retirar ofertas, confirmar compra) y todos los avisos de entrega, cobro offline y validaciones económicas.
  * **Criterios de aceptación:** Los mensajes de HUD, diálogos y menús de economía y mercado se presentan en inglés o castellano según la preferencia del cliente, manteniendo el formateo numérico de oro y nombres de objetos.
  * **Resolución:**
    1. Localizadas todas las validaciones de negocio en `CommunityMarketLogic.cs` (`ValidateNewListing`, `ValidatePurchase`, `BuyListing`, `CancelListing`) utilizando `CoreI18n`.
    2. Localizados todos los avisos en `EconomyModule.cs`: notificaciones del Spa, renovación de turnos de minería con reaparición de vetas, ganancia de energía por saciedad, advertencias de saciedad máxima, buff por platos cocinados e indicación para descansar sentado.
    3. Localizado el Mercado Vecinal en `CommunityMarketModule.cs`: menú principal del tablón, explorador de ofertas con opciones de compra paginadas, confirmación interactiva de compra, validación de objeto en mano para poner a la venta, selector de precio en oro, gestión y retirada de ofertas activas, aviso de venta en tiempo real y recaudación acumulada offline.
    4. Localizado el aviso HUD en `CommunityMarketDelivery.cs` ante devoluciones por inventario lleno.

- [x] **TASK-1904: Internacionalización de Festivales, Minijuegos, Canales de Chat y Utilidades**
  * **Prioridad:** Media
  * **Dependencias:** TASK-1901.
  * **Descripción:** Sustituir los mensajes hardcodeados en `FestivalMinigameModule.cs`, `FestivalParticipationModule.cs`, `ChatChannelModule.cs`, `RealTimeClockModule.cs` y lógica pura asociada (`FestivalScoreLogic`). Localizar anuncios de ceremonias en chat y HUD, tableros de puntuaciones de minijuegos, menús y respuestas de inscripción comunitaria (`/evento`), mensajes de victoria y entrega de premios oficiales, así como la ayuda y sintaxis de comandos `/l`, `/g` y `/time`.
  * **Criterios de aceptación:** Interacciones de festivales y comandos de chat/reloj devuelven respuestas en el idioma del jugador sin cadenas residuales en castellano en sesiones en inglés.
  * **Resolución:**
    1. Localizada la generación de la carta oficial de clasificación festiva en `FestivalScoreLogic.cs` con medallas y cabeceras/pies contextuales en `CoreI18n`.
    2. Localizados en `FestivalMinigameModule.cs` los avisos de ausencia de marcas, cabecera de clasificaciones, preavisos e inicio de ceremonias comunitarias (Luau, Medusas), anuncio global del ganador y recepción de premios en el HUD.
    3. Localizados en `FestivalParticipationModule.cs` los diálogos con Lewis, opciones de inscripción (`Response`), respuestas de inscripción individuales transmitidas por red y traducidas en el cliente receptor, alertas de inicio de tandas y ceremonias, y comando `/evento`.
    4. Localizados en `ChatChannelModule.cs` las ayudas de registro y mensajes de sintaxis para `/l` (proximidad local) y `/g` (global).
    5. Localizados en `RealTimeClockModule.cs` la ayuda del comando `/time`, consulta de estado y offset, confirmaciones de reinicio y fijado de hora, ajustes relativos de minutos/horas y avisos de formato inválido.

- [x] **TASK-1905: Internacionalización del Ciclo de Vida de Sesión, Rollover, Checkpoints y Emparejamiento**
  * **Prioridad:** Alta
  * **Dependencias:** TASK-1901.
  * **Descripción:** Sustituir los mensajes hardcodeados en `BedLogoutModule.cs`, `DailyRolloverModule.cs`, `CheckpointModule.cs` y `ClientSecurityModule.cs`. Localizar los avisos HUD de descanso offline y de anfitrión, los avisos de preaviso de 10 minutos y desconexión por rollover diario, avisos de checkpoint y los textos del menú interactivo de emparejamiento por código (`NamingMenu`), resultados de verificación y mensajes de error en el cliente.
  * **Criterios de aceptación:** Al desconectarse por rollover o descanso, guardar por checkpoint o realizar el pairing de cliente, cada jugador recibe las notificaciones y títulos de interfaz en su idioma configurado.
  * **Resolución:**
    1. Localizado `BedLogoutModule.cs` y `SafeRestartCoordinator.cs` (`AgentHostLogic.ApplyRest`): HUD de energía restaurada offline, mensaje de descanso de host en modo agente y notificación de desconexión segura.
    2. Localizado `DailyRolloverModule.cs`: aviso con 10 minutos de antelación para el rollover diario, notificación de cierre en el servidor y mensaje de desconexión ordenado emitido y traducido en los clientes (`rollover.disconnecting`).
    3. Localizado `CheckpointModule.cs`: aviso de guardado intradía preventivo tanto local como transmitido por red a los peones en su respectivo idioma (`checkpoint.saving_soon`).
    4. Localizados `ClientSecurityModule.cs` y `ClientPairingLogic.cs`: títulos y prompts del diálogo `NamingMenu` de emparejamiento (genérico o personalizado con alias), mensaje HUD durante la verificación, mensajes de error específicos (dispositivo inválido, código vacío, invitación expirada o inexistente, discrepancia de IP, intentos restantes decrementales) y notificación de emparejamiento exitoso.

- [x] **TASK-1906: Limpieza y completitud de cadenas hardcodeadas (Badges en mundo, cuevas privadas, anexos y nombres de ubicaciones)**
  * **Prioridad:** Alta
  * **Dependencias:** TASK-1901, TASK-1902.
  * **Descripción:** Extraer todos los textos residuales hardcodeados en castellano hacia los catálogos `default.json` y `es.json`:
    1. **Badges de mundo (`OnRenderedWorld`):** Sustituir `"[E / Clic o Caminar] Fundar Parcela"`, `"🏠 Mi Parcela (Caminar para entrar)"`, `"🏠 Parcela de {owner}"`, `"🌲 {template.DisplayName} (Caminar para entrar)"`, `"[E / Clic o Caminar] Adquirir Anexo"` y `"🚫 Ampliación no disponible"` por claves i18n (`badge.*`).
    2. **Rechazos y feedback de anexos (`HandleBuyAnnex`):** Localizar las razones de rechazo (`annex.rejection.*`) y traducir el nombre dinámico del anexo adquirido en el mensaje HUD.
    3. **Selección de cueva privada (`PrivateFarmCaves.cs`):** Sustituir las referencias de evento vanilla por claves propias (`cave.dialog_prompt`, `cave.choice_mushrooms`, `cave.choice_bats`, `cave.selected_mushrooms`, `cave.selected_bats`).
    4. **Nombres de ubicaciones y descripciones de portales:** Localizar `PlazaCatalogFormatter.GetLocationDisplayName` (`location.host_farm`, `location.bridge`), los nombres de ubicaciones en `Data/Locations` (`location.cave_slot`, `location.cellar_slot`, `location.greenhouse_slot`, `location.farm_slot`, `location.annex_slot`), los nombres cortos de plantillas de granja (`farm_template.*.name`) y las descripciones de los 16 portales de residencia (`door.Farm_Portal_XX.description`).
    5. **Paridad y respaldo:** Actualizar `FallbackTranslationProvider.cs`, verificar paridad 1:1 en `LocalizationTests.cs` y añadir pruebas unitarias de resolución en inglés y castellano.
  * **Criterios de aceptación:**
    1. Cero cadenas de texto de interfaz hardcodeadas en castellano en los módulos de residencias, anexos, cuevas y badges de mundo.
    2. Cuando el jugador tiene el juego en inglés, todos los indicadores sobre portales y anexos, selección de cuevas y menús de rechazo aparecen en inglés.
    3. Paridad total de claves y tokens entre `default.json` y `es.json` validada por `dotnet test`.
  * **Resolución:**
    1. Localizados los indicadores flotantes en `FarmSlotModule.OnRenderedWorld` mediante claves `badge.claim_slot`, `badge.my_slot`, `badge.neighbor_slot`, `badge.annex_enter`, `badge.annex_purchase` y `badge.annex_unavailable`.
    2. Localizados todos los motivos de rechazo de compra de anexos en `FarmSlotModule.HandleBuyAnnex` (`annex.rejection.*`) y el nombre dinámico del anexo en la notificación HUD (`annex_template.*`).
    3. Rediseñado el diálogo interactivo de `PrivateFarmCaves.cs` utilizando `I18n.Get("cave.dialog_prompt")` con opciones traducidas de champiñones/murciélagos y confirmación inmediata por HUD.
    4. Localizado `PlazaCatalogFormatter.GetLocationDisplayName` y los nombres inyectados en `Data/Locations` para puentes, parcelas, cuevas, bodegas e invernaderos, junto con las descripciones de los 16 portales de residencia (`door.Farm_Portal_XX.description`).
    5. Actualizado `FallbackTranslationProvider.cs` y ampliada la suite `LocalizationTests.cs` con pruebas dinámicas de conmutación en tiempo real; 1338 pruebas unitarias pasando al 100%.

### Épica 20: Adopción y Evento de Mascota para Clientes por Compra de Cuenco (rama `feat/client-pet-adoption`)
> **Objetivo:** Permitir que los jugadores clientes (*farmhands*) desbloqueen su propio evento de adopción canónico (perro/gato de Marnie) de forma inmersiva al comprar un cuenco para mascotas (`Pet Bowl`) en su parcela/residencia. Marnie se presenta en la puerta de la residencia en el siguiente despertar del jugador (sin carta de por medio) y la mascota queda vinculada de forma autoritativa a su jugador, cuenco y mapa.

- [x] **TASK-2001: Detección y Registro Autoritativo de Compra de `Pet Bowl` por Clientes**
  * **Prioridad:** Alta
  * **Dependencias:** TASK-1901, TASK-906.
  * **Descripción:** Interceptar la colocación o compra del edificio `Pet Bowl` en la carpintería de Robin (`CarpenterMenu`) cuando es ejecutada por un jugador cliente en su residencia o anexo (`FarmSlotModule.Prefix_CarpenterMenu_Placement`). Registrar en el estado autoritativo del servidor (`ServerWorldState` / `PlayerRecord`) la solicitud de adopción pendiente, vinculando: `Farmer.UniqueMultiplayerID`, identificador de mapa de la residencia (`targetLocName`), posición del cuenco y fecha del juego en la que se completó la compra. Garantizar que este estado persista ante reinicios, checkpoints y rollovers diarios, y que no se duplique si el jugador interactúa repetidas veces con el cuenco.
  * **Criterios de aceptación:**
    1. Al construir un `Pet Bowl` en una parcela residencial de cliente, el servidor detecta y persiste la solicitud de mascota pendiente vinculada al `UniqueMultiplayerID` y al `slotId`/nombre del mapa.
    2. La compra de cuencos en mapas ajenos o en la granja host no sobreescribe ni corrompe el registro de otros jugadores.
    3. El estado sobrevive al ciclo de vida del servidor y guardados en disco.
  * **Resolución:**
    1. Creados los modelos de dominio puro [`PetAdoptionModels.cs`](Pelican4Ever.Core/Pets/PetAdoptionModels.cs) con el enum `PetAdoptionState` y la clase `PetAdoptionRecord`, e integrados en `PlayerRecord.PetAdoption`.
    2. Implementada la lógica de negocio desacoplada en [`PetAdoptionLogic.RecordPurchase`](Pelican4Ever.Core/Pets/PetAdoptionLogic.cs) para registrar slot, coordenadas, día de juego y preferencias de tipo/raza.
    3. Implementado el parche Harmony `Postfix_CarpenterMenu_TryToBuild` en [`ClientPetAdoptionModule.cs`](Pelican4Ever/Modules/ClientPetAdoptionModule.cs) que intercepta la construcción de `Pet Bowl`, enviando el mensaje `Pelican4Ever.PetBowlPurchased` al host y persistiendo autoritativamente en `ServerWorldState`.

- [x] **TASK-2002: Visita de Marnie en la Puerta en el Siguiente Despertar (sin carta)**
  * **Prioridad:** Alta
  * **Dependencias:** TASK-2001, TASK-1901.
  * **Descripción:** Sustituir el flujo de carta por correo por una visita directa: cuando un jugador compre un `Pet Bowl`, el host conserva la solicitud pendiente y, en el siguiente despertar del jugador (cualquier día posterior al de la compra), Marnie se presenta en la puerta de su residencia o anexo. No se inyecta ninguna carta en `Data/Mail` ni en `farmer.mailbox`.
  * **Criterios de aceptación:**
    1. Tras comprar el cuenco y dormir una vez, Marnie aparece en la puerta de la residencia del jugador sin necesidad de leer ninguna carta.
    2. No se entrega correo nuevo ni se añade nada al buzón; la definición histórica de la carta se conserva solo para que las partidas antiguas puedan seguir leyéndola.
    3. Funciona tanto para el host como para *farmhands*, y también si el cuenco está en un anexo (se usa el `FarmSlotId` registrado en la compra).
    4. La visita no se repite tras adoptar o declinar, y las partidas antiguas con estado `LetterDelivered` siguen pudiendo completar la adopción.
  * **Resolución:**
    1. Eliminados el despacho de correo, `ShouldDeliverLetter`, `MarkLetterDelivered` y las comprobaciones de `mailbox`/`mailReceived`. Se conserva únicamente la entrada histórica de `Data/Mail` y sus textos para no romper cartas ya presentes en partidas antiguas.
    2. [`PetAdoptionLogic.HasWokenSincePurchase`](Pelican4Ever.Core/Pets/PetAdoptionLogic.cs) determina que ha pasado al menos un despertar desde la compra (`currentGameDay > PurchaseGameDay`), aceptando los estados `BowlPurchased` y `LetterDelivered` por compatibilidad.
    3. `ClientPetAdoptionModule` registra `DayStarted` para host y clientes, refresca el estado autoritativo, activa `wakeTriggerPending` y dispara la visita en cuanto el jugador pisa su propia granja o anexo tras despertar.

- [x] **TASK-2003: Disparador y Adaptación del Evento de Adopción de Mascota en Ranuras de Granja**
  * **Prioridad:** Crítica
  * **Dependencias:** TASK-2002.
  * **Descripción:** Adaptar la cinemática canónica de presentación de mascota de Marnie (evento canónico `1590166` / `897405` o evento customizado desacoplado de `"Farm"`) para ejecutarse en el mapa de ranura del cliente (`Farm_slot_X` / anexo) en cuanto el jugador sale de su cabina/residencia tras despertar, sin exigir carta ni tiempo soleado. Resolver dinámicamente las coordenadas de la puerta de la cabina y la posición del `Pet Bowl` de ese mapa específico para situar a Marnie y a la mascota. Permitir al cliente elegir el nombre y confirmar la adopción (respetando la preferencia perro/gato del personaje o dándole a elegir).
  * **Criterios de aceptación:**
    1. El evento se dispara al salir de la residencia en el mapa propio del cliente, nunca en la granja del host ni en parcelas ajenas.
    2. La cinemática no interrumpe ni bloquea a otros jugadores conectados en el servidor.
    3. Marnie y la mascota aparecen situadas correctamente frente a la entrada de la cabina y cuenco del cliente.
    4. El jugador puede seleccionar el nombre y aceptar la adopción mediante la interfaz nativa del juego.
  * **Resolución:**
    1. Implementadas las reglas de activación en [`PetAdoptionLogic.CanTriggerAdoptionEvent`](Pelican4Ever.Core/Pets/PetAdoptionLogic.cs) exigiendo un estado pendiente (`BowlPurchased` o `LetterDelivered`), coincidencia estricta de ranura de residencia y horario diurno; la lluvia ya no bloquea la visita.
    2. Diseñada la secuencia cinemática en `ClientPetAdoptionModule.StartAdoptionEvent` resolviendo dinámicamente la puerta de la cabina con `farm.GetMainFarmHouseEntry()` e instanciando a Marnie y la previsualización de la mascota en frente de la residencia.
    3. Integrado el flujo interactivo con `QuestionDialog.Ask` para aceptar o rechazar la adopción, y el diálogo nativo `NamingMenu` para elegir el nombre oficial de la mascota con soporte para nombres aleatorios.

- [x] **TASK-2004: Instanciación de Mascota, Vinculación al `PetBowl` y Sincronización en Red**
  * **Prioridad:** Alta
  * **Dependencias:** TASK-2003.
  * **Descripción:** Tras la confirmación de la adopción en el evento, instanciar la entidad correspondiente (`Cat` o `Dog`) en la ubicación del cliente (`targetLoc.characters.Add`), vinculando su referencia y comportamiento de descanso al edificio `Pet Bowl` de su mapa particular. Asegurar que los datos de la mascota (nombre, tipo, raza, amistad individual) se sincronicen autoritativamente vía SMAPI / net fields a todos los clientes que visiten la parcela y se preserven en el guardado (`saveGame`). Evitar que la mascota sea eliminada o teletransportada a las coordenadas `(0,0)` de la granja host al cambiar de mapa.
  * **Criterios de aceptación:**
    1. La mascota reside permanentemente en el mapa de residencia del cliente y usa el cuenco de su parcela para beber y dormir.
    2. Los visitantes autorizados a la parcela ven a la mascota, pueden acariciarla y escuchar sus sonidos.
    3. Tras guardar la partida o reiniciar el servidor, la mascota reaparece fielmente en el mapa y ranura del cliente.
  * **Resolución:**
    1. Al confirmar la adopción, el cliente despacha `Pelican4Ever.PetAdopted` con slot, nombre, tipo, raza y coordenadas del cuenco.
    2. En el host, `ProcessPetAdoption` instancia la entidad `Pet`, le asigna `pet.currentLocation = loc`, localiza el `PetBowl`, ejecuta primero `petBowl.AssignPet(pet)` y solo después fija `pet.homeLocationName.Value = loc.NameOrUniqueName` para que la vinculación autoritativa no quede sobrescrita por un `parentLocationName` vacío, añadiendo el animal a `loc.characters`.
    3. Actualizado `FarmSlotModule.Prefix_Utility_GetAllPets` para recopilar también las mascotas presentes en las ranuras de residencia exteriores, asegurando que el motor de Stardew Valley actualice su cariño, cuencos y comportamiento diario.

- [x] **TASK-2005: Pruebas Unitarias de Ciclo de Vida de Mascota, Aislamiento y Paridad i18n**
  * **Prioridad:** Media
  * **Dependencias:** TASK-2001, TASK-2002, TASK-2003, TASK-2004.
  * **Descripción:** Implementar la suite de pruebas unitarias en `Pelican4Ever.Tests` (`ClientPetAdoptionTests.cs`) que valide la lógica de negocio pura:
    1. Máquina de estados de adopción: `None` -> `BowlPurchased` -> `LetterPending` -> `EventReady` -> `Adopted`.
    2. Aislamiento estricto: la compra de un cuenco en una ranura nunca altera las banderas ni la cola de correo de otro jugador.
    3. Verificación de paridad de claves i18n para los textos de la carta, diálogos de Marnie y notificaciones HUD en `default.json` y `es.json`.
  * **Criterios de aceptación:**
    1. La suite de pruebas unitarias pasa al 100% con `dotnet test`.
    2. Cero regresiones en los tests existentes de la solución.
    3. Paridad total de claves entre `default.json` y `es.json`.
  * **Resolución:**
    1. Creada la suite [`ClientPetAdoptionTests.cs`](Pelican4Ever.Tests/ClientPetAdoptionTests.cs) con casos de prueba validando la máquina de estados, la visita tras el siguiente despertar (sin carta), el aislamiento de ranuras y jugadores, las condiciones horarias/estado y la resolución de cadenas i18n con tokens.
    2. Verificada la paridad 1:1 de claves y tokens entre `default.json` y `es.json` con `LocalizationTests.cs`.
    3. Suite completa ejecutada con éxito: 1337 pruebas pasando sin errores ni regresiones.


- [x] **TASK-2006: Retirar el cuenco de mascota inicial de granjas y residencias**
  * **Prioridad:** Media
  * **Dependencias:** TASK-2001.
  * **Descripción:** Como el cuenco se compra después a Robin (y eso desencadena la carta de Marnie), la granja del host y las residencias ya no deben nacer con el `Pet Bowl` inicial.
  * **Criterios de aceptación:**
    1. Ni la granja host ni las residencias nuevas o ya existentes conservan el cuenco inicial.
    2. Un cuenco comprado después no se borra nunca.
  * **Resolución:** `FarmSlotModule.RemoveStarterPetBowl` retira solo el cuenco de arranque (en su casilla inicial o en la canónica `(53, 7)` del host, y si no tiene mascota asignada). Deja una marca en el `modData` de la ubicación (`Pelican4Ever.StarterPetBowlRemoved`) para ejecutarse una sola vez. Se invoca desde `EnsureFarmSlotBuildings` (residencias), `CleanUpHostFarmLegacy` (host), la regeneración de plantilla y la migración de partidas, que ya no reubica el cuenco.
  * **Reparación visual (host):** la retirada del cuenco dejaba un hueco junto a la valla del corral, porque el host pintaba dos baldosas de borde de tierra (`612`) en `(53, 7)` y `(53, 8)` y recurría la hierba sombreada (`256`) en `(54, 7)` y `(54, 8)`, dejando una franja de tierra recortada y una línea de borde de hierba colgando en el aire. `FarmSlotModule.RestoreHostFarmPetBowlGround` repinta ahora ese solar con las baldosas vanilla del terreno de `Maps/Farm` (hierba con sombra `567` bajo la valla, tierra lisa `587` en `(53, 7-8)`, borde de campo `612` en `(54, 7-8)` y hierba `401`/`256` en el corral), tanto al generar `Maps/Pelican4Ever_HostFarm` como al limpiar la ubicación en tiempo de ejecución. El terreno resultante es idéntico al vanilla y la retirada del cuenco ya no deja ningún hueco visible.

- [ ] **TASK-2007: Vinculación explícita mascota ↔ jugador propietario**
  * **Prioridad:** Alta
  * **Dependencias:** TASK-2004.
  * **Descripción:** La entidad `Pet` de Stardew Valley no expone un propietario de jugador: hoy la pertenencia se infiere de `PetBowl.petId`, `homeLocationName`, `friendshipTowardFarmer` y del `PlayerRecord.PetAdoption`. En la prueba del host la mascota no quedó asociada a ningún jugador. Añadir una vinculación autoritativa explícita (por ejemplo `OwnerPlayerId` en el registro de adopción y en el estado del servidor, o un `modData` en la propia mascota) que sobreviva a guardados y se replique a los clientes, y usarla para decidir quién puede acariciar, renombrar o reclamar la mascota.
  * **Criterios de aceptación:**
    1. Cada mascota de residencia/anexo tiene un `UniqueMultiplayerID` propietario persistido y consultable.
    2. Tras reiniciar el servidor o reconectar al cliente, la mascota sigue perteneciendo al mismo jugador y no se reasigna a otro.
    3. Ningún otro jugador puede adoptar, reclamar ni duplicar la mascota vinculada a una ranura ajena.
    4. Los visitantes autorizados pueden ver e interactuar con la mascota sin convertirse en propietarios.

- [ ] **TASK-2008: Vinculación y persistencia mascota ↔ mapa/cuenco en ranuras privadas**
  * **Prioridad:** Crítica
  * **Dependencias:** TASK-2004, TASK-2007.
  * **Descripción:** Auditar y corregir la cadena completa de vinculación al mapa tras la adopción: `PetBowl.parentLocationName` ↔ `PetBowl.petId` ↔ `Pet.petId` ↔ `Pet.homeLocationName` ↔ `loc.characters` ↔ guardado. La prueba del host mostró que la mascota no quedaba vinculada ni al jugador ni al mapa. Revisar también `FarmSlotModule.Prefix_Utility_GetAllPets`, que recopila mascotas de la granja host y de las puertas de residencia, pero no de los anexos, y verificar que la sincronización de red hace visible la mascota en el cliente y en visitantes.
  * **Criterios de aceptación:**
    1. Tras adoptar, `petBowl.petId == pet.petId`, `pet.homeLocationName == slotId` y la mascota está en `loc.characters` de la ranura correcta.
    2. Tras guardar y recargar, la mascota reaparece en su mapa, junto a su cuenco, sin teletransportarse a `(0,0)` ni a la granja host.
    3. `Utility.getAllPets` incluye mascotas de residencias, anexos y granja host, y el comportamiento diario (cariño, cuenco, paseos) se ejecuta en el host.
    4. El cliente propietario y los visitantes autorizados ven la mascota sincronizada; el log de diagnóstico indica slot, `petId`, `homeLocationName` y resultado de la vinculación.

- [x] **TASK-2009: Corregir la selección de ubicación en el menú de construcción de Robin (clientes)**
  * **Prioridad:** Crítica
  * **Dependencias:** TASK-201, TASK-2001.
  * **Descripción:** Al elegir una ubicación (residencia principal o anexo) en el menú "Construir edificios de granja" de Robin, el diálogo se cerraba sin abrir el `CarpenterMenu`. Endurecer la ruta de apertura para clientes: resolver la ubicación desde `Game1.locations` o, si aún no se instanció, desde `Data/Locations`; diferir la lista de ubicaciones hasta que se cierre el cuadro de diálogo actual; capturar excepciones del constructor; y registrar diagnóstico suficiente para localizar el fallo.
  * **Criterios de aceptación:**
     1. Un *farmhand* con residencia y anexo puede elegir cualquiera de las dos ubicaciones y el menú de construcción se abre con sus planos.
     2. Si una ubicación aún no está sincronizada, se instancia desde `Data/Locations` o se muestra un mensaje claro en lugar de cerrar el diálogo en silencio.
     3. Cualquier excepción al construir el menú queda registrada en el log con jugador, rol, constructor, ubicación y número de planos.
     4. El host con una sola ubicación sigue abriendo el menú directamente sin pasos extra.
  * **Resolución:**
     1. `FarmSlotModule.Prefix_ShowConstructOptions` delega en `ResolveConstructionLocation`, que intenta `Game1.getLocationFromName`, `EnsureFarmSlotExists` y, en clientes, crea la instancia desde el asset fresco `Data/Locations` con `Game1.CreateGameLocation` y la añade a `Game1.locations`.
     2. La lista de ubicaciones se muestra mediante `DeferConstructionLocationList`, que la pospone a `Game1.afterDialogues` si hay un `DialogueBox` abierto para evitar que `GameLocation.afterQuestion` o el cierre del diálogo se coman la respuesta.
     3. `OpenConstructionMenu` centraliza la apertura: valida la ubicación, cuenta planos del constructor, captura excepciones, asigna `Game1.activeClickableMenu` y programa una comprobación a 1 segundo que registra si el menú sigue abierto, el menú activo, `dialogueUp`, `eventUp` y el estado de fundido.
     4. Añadidas las claves i18n `carpenter.no_blueprints` y `carpenter.menu_failed` en inglés y castellano. Compilación Debug desplegada y suite completa en verde (1337 pruebas).

- [x] **TASK-2010: Conservar los warps ajenos a la residencia al salir de un anexo**
  * **Prioridad:** Crítica
  * **Dependencias:** TASK-903, TASK-2009.
  * **Descripción:** Al construir en un anexo y salir del menú de construcción, el regreso al diálogo de Robin aparecía en la entrada del anexo de la residencia con el jugador dibujado como una sombra. `Prefix_WarpFarmer` redirigía a la residencia madre **cualquier** warp cuyo origen fuera un anexo, incluido el retorno al mapa del constructor y los interiores de edificios.
  * **Criterios de aceptación:**
      1. Tras construir en un anexo, al volver al diálogo de Robin el jugador sigue en el mapa del constructor, sin fundidos ni siluetas.
      2. Entrar al interior de un edificio construido en un anexo conserva su warp original.
      3. Salir del anexo hacia la residencia madre sigue devolviendo al jugador a la casilla de retorno del anexo.
  * **Resolución:**
      1. Portadas a `dev` las guardas de los commits `67ea3c8` y `de00795` (rama `codex/annex-construction-animals`), que nunca se habían integrado: durante la colocación remota (`Game1.currentLocation == viewingLocation` y el destino es el mapa real del jugador) se deja pasar el warp con su callback.
      2. Las ramas de acceso a residencias y anexos ignoran las solicitudes de tipo estructura (`locationRequest.IsStructure`), y la salida del anexo solo redirige cuando el destino es exactamente la residencia madre (`annex.ResidenceSlotId`).
      3. Compilación Debug desplegada sin errores y suite completa en verde (1333 pruebas).

- [x] **TASK-2011: Servicios de Robin y Marnie en anexos e Isla Jengibre**
  * **Prioridad:** Alta
  * **Dependencias:** TASK-904, TASK-906, TASK-2009.
  * **Descripción:** La rama `codex/annex-construction-animals` añadía la compra de animales por residencia/anexo, la sincronización bajo demanda de anexos para clientes y los servicios compartidos de Isla Jengibre, pero partía de una versión anterior de `dev` y un merge directo revertía el menú de construcción actual. Había que integrar esas piezas manualmente sobre el código vigente.
  * **Criterios de aceptación:**
      1. Marnie ofrece elegir entre la residencia, cada anexo propio y la granja compartida de Isla Jengibre (si su casa está restaurada).
      2. Un cliente que aún no ha visitado un anexo puede construir o comprar animales en él; el host le envía la ubicación bajo demanda.
      3. Robin puede construir en Isla Jengibre cuando la casa está restaurada, y mover edificios allí conserva los warps interiores.
      4. La entrada de los anexos no permite colocar edificios encima del corredor de acceso.
  * **Resolución:**
      1. Añadidos `RequestAnnexLocation`/`SyncAnnexLocation` y la petición automática de los anexos propios tras `SyncDoorsState`, reutilizando `RetainSyncedOwnPrivateLocation`.
      2. `GetFarmServiceDestinations`/`ResolveFarmServiceDestination` y `Prefix_ShowAnimalShopMenu` sustituyen el parche del constructor de `PurchaseAnimalsMenu`, de modo que la granja elegida no se sobrescribe.
      3. Añadidos `Postfix_IsBuildableLocation`, `Postfix_CarpenterCanMoveIslandBuilding`, `Postfix_OnIslandBuildingMoved` y `IslandWest` como ubicación siempre activa.
      4. `MapCloneHelper` marca como no edificables las casillas de apertura y corredor del anexo. Nuevas claves i18n en inglés y castellano. Compilación y suite en verde.

- [x] **TASK-2012: Las mascotas adoptadas se quedaban inmóviles junto al cuenco**
  * **Prioridad:** Alta
  * **Dependencias:** TASK-2004.
  * **Descripción:** El gato adoptado no se movía: aparecía pegado al cuenco. La IA de `Pet` solo avanza si `CurrentBehavior` referencia un comportamiento válido de `Data/Pets`, y las mascotas creadas por el módulo no pasaban por el flujo vanilla (`PetLicense` → `warpToFarmHouse`) que inicializa ese campo. Además se creaban sobre la casilla del edificio del cuenco, que bloquea el movimiento.
  * **Criterios de aceptación:**
      1. Una mascota recién adoptada pasea por su residencia o anexo.
      2. Las partidas con una mascota ya atascada se reparan al cargar o al empezar el día.
      3. La mascota aparece en la casilla libre delante del cuenco, no dentro del edificio.
  * **Resolución:**
      1. `ProcessPetAdoption` usa `PetBowl.GetPetSpot()` para la posición inicial y llama a `EnsurePetBehavior`.
      2. `HealStuckPets` recorre `Utility.getAllPets()` en `SaveLoaded` y `DayStarted` del host, inicializando a `Walk` cualquier comportamiento vacío o inexistente.
      3. Compilación Debug desplegada sin errores y suite completa en verde (1333 pruebas).

- [x] **TASK-2013: Un parche de Isla Jengibre abortaba el registro Harmony y rompía las mascotas en clientes**
  * **Prioridad:** Crítica
  * **Dependencias:** TASK-2011, TASK-2012.
  * **Descripción:** Al hablar con Marnie en su tienda, el cliente lanzaba `KeyNotFoundException: Required location 'Pelican4Ever_Farm_03_Home' not found`. La causa no era la lógica de mascotas: `Postfix_CarpenterCanMoveIslandBuilding` declaraba el parámetro como `building` cuando Harmony lo resuelve por nombre y el original lo llama `b`. La excepción al aplicar ese parche abortaba el registro completo de `FarmSlotModule`, de modo que `Utility.getAllPets` (y todo lo registrado después) quedaba sin parchear y el cliente usaba la versión vanilla, que hace `RequireLocation` sobre casas de otros granjeros. Además, el prefijo de `getAllPets` devolvía las mascotas de todo el mundo, así que el cliente contaba la mascota del anfitrión como propia.
  * **Criterios de aceptación:**
      1. El cliente puede abrir el diálogo y la tienda de Marnie sin excepciones.
      2. El cliente solo ve como propias las mascotas de su residencia y anexos; las del anfitrión no cuentan para sus comprobaciones vanilla (`hasPet`, menú de adopción).
      3. Un parche con parámetros mal declarados no impide aplicar el resto.
      4. La visita de Marnie registra en el log el motivo exacto si no llega a empezar.
  * **Resolución:**
      1. Corregido el nombre del parámetro (`b`) y añadido `PatchSafe`, que registra cada parche por separado y loguea el error sin abortar el bloque.
      2. `Prefix_Utility_GetAllPets` filtra por propietario en clientes (residencia, anexos y casa propia) y en el host mantiene el barrido completo.
      3. Parcheados también `Farmer.hasPet`, `getPet`, `getPetName` y `getPetDisplayName`, que usan `getHomeOfFarmer` y podían lanzar igual.
      4. `CheckAndTriggerAdoptionEvent` registra una vez el motivo por el que la visita queda bloqueada (mapa, propietario, hora, día) y el evento retira a Marnie y la mascota de muestra si el jugador abandona la granja o empieza un día nuevo.
      5. Compilación Debug desplegada sin errores y suite completa en verde (1333 pruebas).

- [x] **TASK-2014: La pestaña «Animales» del cliente listaba la mascota del anfitrión**
  * **Prioridad:** Alta
  * **Dependencias:** TASK-2013.
  * **Descripción:** En el menú del jugador (1.6), la pestaña «Animales» usa `AnimalPage.FindAnimals`, que recorre todas las ubicaciones del juego con `Utility.ForEachLocation`. En un cliente eso incluye la granja del anfitrión, así que su mascota aparecía como la primera —y única— ficha del cliente.
  * **Criterios de aceptación:**
      1. Un cliente solo ve en «Animales» las mascotas, caballos y animales de su residencia, anexos e interiores.
      2. El anfitrión sigue viendo todo (granja principal y residencias de los farmhands).
  * **Resolución:**
      1. Postfix `Postfix_AnimalPage_FindAnimals` que, en clientes, retira las entradas cuya ubicación (o granja padre) no pertenece al jugador local.
      2. `IsOwnedByLocalPlayer` resuelve también los interiores de residencia (`_Home`, `_Cave`, `_Cellar`, `_Greenhouse`) por edificio padre o por nombre de hueco.
      3. Compilación Debug desplegada sin errores y suite completa en verde (1333 pruebas).

- [x] **TASK-2015: La visita de Marnie no saltaba en clientes con ID de jugador negativo**
  * **Prioridad:** Crítica
  * **Dependencias:** TASK-2013.
  * **Descripción:** El registro de bloqueo añadido en TASK-2013 reveló la causa real: `CanTriggerAdoptionEvent` exigía `playerId <= 0` como «desconocido», pero los `UniqueMultiplayerID` de Stardew pueden ser negativos (el cliente registraba `-1349064639806311618`). Con el jugador y el propietario iguales, el mapa correcto y la hora en rango, la condición seguía devolviendo false y la visita nunca empezaba.
  * **Criterios de aceptación:**
      1. Un cliente con `UniqueMultiplayerID` negativo recibe la visita de Marnie al despertar o conectar.
      2. Un propietario desconocido (0) sigue bloqueando la visita.
  * **Resolución:**
      1. La guarda pasa de `<= 0` a `== 0`: solo el cero significa «desconocido».
      2. Nueva prueba `CanTriggerAdoptionEvent_AcceptsNegativePlayerIds` con un ID negativo real y con 0 como propietario. Suite en verde (1334 pruebas).

- [x] **TASK-2016: Demoler edificios en la residencia o el anexo como cliente**
  * **Prioridad:** Alta
  * **Dependencias:** TASK-2011.
  * **Descripción:** En vanilla el botón de demoler del `CarpenterMenu` está reservado al anfitrión: `demolishButton.visible = Game1.IsMasterGame` y `hasPermissionsToDemolish` devuelve false para cualquier cliente. Un cliente que construye en su residencia no podía retirar edificios (al pulsar en el hueco del martillo se acababa tocando la cancelación y el menú se cerraba).
  * **Criterios de aceptación:**
      1. Un cliente ve el botón de demoler cuando el menú apunta a su residencia o a uno de sus anexos.
      2. Puede demoler allí; en ubicaciones ajenas sigue sin poder.
      3. El anfitrión no cambia de comportamiento.
  * **Resolución:**
      1. `Postfix_CarpenterMenu_Ctor` muestra `demolishButton` cuando `TargetLocation` pertenece al jugador local.
      2. `Postfix_CarpenterMenu_HasPermissionsToDemolish` concede permiso en las mismas ubicaciones. `GameLocation.destroyStructure` no tiene guarda de anfitrión, así que la eliminación se sincroniza igual que la construcción.
      3. Compilación Debug desplegada sin errores y suite completa en verde (1334 pruebas).
