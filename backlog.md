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

- [ ] **TASK-301: Presupuesto de Saciedad en Comida, Platos Cocinados y Tope del Spa (`EconomyModule.cs`)**
  - **Prioridad:** Alta
  - **Descripción:**
    - Parche `Prefix`/`Postfix` en `Farmer.doneEating`: recortar la ganancia instantánea de energía usando `SatietyBudget.AllowedGain` (hasta el 50% de `MaxStamina` por día real), conservando siempre la curación de `Health`, los `Buffs` y los ítems especiales (`Stardrop`).
    - Si se consume un plato cocinado y quedan usos en `MaxDailyWellFedBuffs` (`2`), activar regeneración pasiva (`+1 Energía / 60s reales` durante 60 min).
    - Limitar la regeneración del Spa a `1 punto / 30s reales` con tope diario `SpaDailyCap` (`60`) mediante `SatietyBudget.AllowedSpaGain`.
  - **Criterios de Aceptación:** La comida restaura energía hasta agotar el presupuesto diario de Saciedad; a partir de ahí sigue curando vida y dando buffs sin sumar energía instantánea.
- [ ] **TASK-302: Minas Persistentes por Turnos de 12 Horas (`06:00` y `18:00`)**
  - **Prioridad:** Alta
  - **Descripción:**
    - Parche `Prefix` en `MineShaft.clearInactiveMines` (llamado cada 10 minutos desde `MineShaft.UpdateMines10Minutes`) que impide descargar niveles vacíos durante el turno.
    - A las `06:00` y `18:00` reales (`Config.ParsedMineResetTimes`), limpiar los niveles de `MineShaft.activeMines` y `VolcanoDungeon` en los que no haya jugadores dentro y mostrar aviso en el HUD.
  - **Criterios de Aceptación:** Los pisos minados permanecen vacíos de rocas durante todo el turno de 12 horas aunque todos los jugadores salgan de la mina.
- [ ] **TASK-303: Multiplicadores de precios configurables por categoría (`PriceMultiplierPatcher.cs`)**
  - **Prioridad:** Media
  - **Descripción:** Si `CropPriceMultiplier != 1.0f` o `ArtisanPriceMultiplier != 1.0f`, aplicar el multiplicador en `StardewValley.Object.sellToStorePrice` según `Category` (`Vegetable`/`Fruit`/`Flower` vs. `ArtisanGoods`).
  - **Criterios de Aceptación:** El precio de venta en tiendas y `Shipping Bin` refleja los multiplicadores de `config.json`.

---

### Iteración 4: Contenedor Dedicado y Vida Urbana (Épica 4 / Post-MVP)
> **Objetivo:** Desplegar el servidor 24/7 en Docker y añadir mejoras de inmersión para NPCs y festivales.

- [ ] **TASK-401: Integración con Host Dedicado Nativo (`Game1.HasDedicatedHost`) y Docker Compose**
  - **Prioridad:** Alta
  - **Descripción:**
    - Activar `Game1.player.team.hasDedicatedHost.Value = true` cuando `Config.DedicatedServerMode == true`.
    - Crear `docker-compose.yml` y scripts de arranque/backup sobre `JunimoServer` con volúmenes persistentes para `Saves/`, `Mods/Pelican4Ever/config.json` y copias de seguridad diarias.
  - **Criterios de Aceptación:** El contenedor levanta la partida 24/7 en Linux y acepta a los 4 clientes del MVP.
- [ ] **TASK-402: Controlador de *Micro-Wandering* de NPCs (`NpcMicroWanderController.cs`)**
  - **Prioridad:** Media (Post-MVP)
  - **Descripción:** Reducir la velocidad en ruta de los NPCs y hacer que deambulen cada 45–90 segundos reales en un radio $\pm 2$ alrededor de su casilla de destino sin pisar `Warps`.
- [ ] **TASK-403: Reprogramación de Festivales y Ventana de Tarde/Noche (`18:00–23:50`)**
  - **Prioridad:** Media (Post-MVP)
  - **Descripción:** Comando `p4e_festival_move` sobre `Data/Festivals/FestivalDates`, ajuste de horario a `1800 2350` y diálogo al entrar a `Town` para permitir cruzar el pueblo sin bloquear a los demás jugadores.
