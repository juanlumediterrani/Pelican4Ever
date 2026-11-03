# `ARCHITECTURE.md` — Documento de Arquitectura Técnica (*Pelican 4Ever*)

## 1. Stack Tecnológico y Topología

* **Plataforma objetivo:** Stardew Valley **1.6.15** (64-bit) sobre Linux (`x86_64`), ejecutado con **SMAPI 4.1+** (probado sobre SMAPI 4.5.2) y **Harmony 2.x** (`Lib.Harmony`).
* **Servidor Dedicado (Nativo 1.6 + Contenedor):**
  * Stardew Valley 1.6.15 incorpora soporte nativo de host automatizado (`Game1.HasDedicatedHost`, `Game1.IsDedicatedHost`, `Farmer.IsDedicatedPlayer` y la clase `StardewValley.Network.Dedicated.DedicatedServer` activada mediante `player.team.hasDedicatedHost.Value = true`).
  * El despliegue se apoya en **JunimoServer** (o contenedor Docker Linux con `xvfb` + `noVNC`) combinando el bot nativo `DedicatedServer` con las reglas de reloj continuo de `Pelican 4Ever` (impidiendo que `DedicatedServer.Tick()` pause el mundo cuando `onlineIds.Count == 0`).
* **Topología Cliente-Servidor y Confianza:**
  * El **mismo paquete del mod (`Pelican4Ever.dll` + `Pelican4Ever.Core.dll`) es obligatorio en Host y Clientes** (`ClientVersionGuardModule` expulsa en `PeerContextReceived` a cualquier cliente sin SMAPI o con distinta versión `Major.Minor`).
  * **Host (`Context.IsMainPlayer`) = Fuente Única de la Verdad** para el reloj, el calendario, la asignación de portales/residencias y el registro de jugadores (`ServerWorldState`).
  * **Cliente (`Context.IsLocalPlayer`) = Ejecutor de Interfaz y Jugador Local** para el recorte de energía al comer (`Farmer.doneEating`), bloqueo local de herramientas en granjas ajenas, diálogos de portal y cama. Todas las mutaciones de estado compartido se solicitan al Host vía `Helper.Multiplayer.SendMessage`.

---

## 2. Estructura de la Solución (.NET)

La solución se divide en tres proyectos para separar completamente la lógica de dominio pura de las dependencias de `Stardew Valley.dll` y poder ejecutar pruebas unitarias en milisegundos:

```text
Pelican4Ever.sln
├── local.props.example                  # Plantilla de ruta local a Stardew Valley (copiar a local.props)
├── Pelican4Ever.Core/                   # Lógica pura (.NET 6, CERO referencias a Stardew/SMAPI/Harmony)
│   ├── Config/
│   │   └── ServerConfig.cs              # Configuración + validador determinista Validate()
│   ├── Time/
│   │   ├── GameClock.cs                 # Conversión hora real -> timeOfDay (0600..2550) + NightHold
│   │   ├── ServerCalendar.cs            # Calendario perpetuo derivado de CalendarEpoch y CatchUpPlan
│   │   └── ServerTime.cs                # Resolución de TimeZoneInfo IANA y reloj UTC -> Local
│   ├── Players/
│   │   ├── PlayerRecord.cs              # Registro autoritativo por jugador (días en TotalDays)
│   │   └── SleepPolicy.cs               # Derivación de SleepState, evaluación de Rollover y cargo médico
│   ├── Economy/
│   │   └── SatietyBudget.cs             # Presupuesto diario de Saciedad (comida) y tope del Spa
│   └── World/
│       ├── ServerWorldState.cs          # Estado persistido en el Host + migración de esquema (v2)
│       ├── HostPlazaLayout.cs           # Plaza Central y cadena de puentes escalables (TASK-1001)
│       ├── DoorCatalogValidator.cs      # Validación de geometría y portales en la plaza (TASK-1002)
│       ├── SpatialWarpRouter.cs         # Enrutamiento espacial, llegadas y retornos (TASK-1102)
│       ├── TerrainTileClassifier.cs     # Clasificación de terreno y autotiling de caminos (TASK-1103)
│       ├── ResidenceExits.cs            # Puntos de salida por plantilla de granja (TASK-1104)
│       ├── PlazaCatalogFormatter.cs     # Formateo puro de /residencias para chat y consola (TASK-1304)
│       └── PlazaTopologyVerifier.cs     # Verificación topológica, warps y áreas de despeje (TASK-1401/1403)
├── Pelican4Ever/                        # Mod SMAPI (.NET 6, referencia a Pelican4Ever.Core)
│   ├── manifest.json
│   ├── ModEntry.cs                      # Validación de config y registro aislado de módulos (IModule)
│   ├── Framework/
│   │   ├── IModule.cs                   # Contrato IModule { Name, Side, Register(ModContext) }
│   │   ├── ModContext.cs                # Contenedor inyectado (Helper, Monitor, Config, Harmony, WorldState)
│   │   └── MapCloneHelper.cs            # Recorte determinista de puentes (35×30) y anexos
│   ├── Models/
│   │   └── ModDataKeys.cs               # Claves de GameLocation.modData (vista replicada de solo lectura)
│   └── Modules/
│       ├── PersistenceModule.cs         # Carga, migración y guardado de ServerWorldState en el Host
│       ├── ClientVersionGuardModule.cs  # Verificación de versión del mod al conectar un par
│       ├── StatusCommandModule.cs       # Comando de diagnóstico p4e_status
│       ├── RealTimeClockModule.cs       # (Iteración 1) Sincronización 1:1, NightHold y bloqueo de pausa
│       ├── DailyRolloverModule.cs       # (Iteración 1) Avisos, disparo de NewDay nativo y SleepPolicy
│       ├── BedLogoutModule.cs           # (Iteración 1) Intercepción de Sleep_Yes y mensaje al Host
│       ├── FarmSlotModule.cs            # (Iteración 2) Registro en Data/Locations, Cabins, plaza y portales
│       ├── EconomyModule.cs             # (Iteración 3) SatietyBudget, Spa, precios y minas 12h
│       └── WorldLifeModule.cs           # (Iteración 4) Micro-wandering de NPCs y festivales
└── Pelican4Ever.Tests/                  # Suite xUnit (.NET 8) sobre Pelican4Ever.Core (793 tests)
```

---

## 3. Modelo de Datos y Fuente Única de la Verdad

### 3.1. Estado Autoritativo en el Host (`ServerWorldState`)

Para evitar condiciones de carrera sobre `Farmer.modData` (cuyo `NetRoot` pertenece al cliente de cada `Farmhand`), toda la información persistente de los jugadores vive en `ServerWorldState` (serializado en la partida maestra mediante `Helper.Data.WriteSaveData("Pelican4Ever.ServerWorldState", state)`):

* **`Players` (`Dictionary<long, PlayerRecord>`):**
  * `LastActiveGameDay`, `RestedOnGameDay` (expresados en `Game1.Date.TotalDays`, nunca en `dayOfMonth`).
  * `LastOutcome` (`Untouched` | `Rested` | `Penalized`), del que `SleepPolicy.GetState` deriva `Awake`, `Rested`, `OfflineSafe` u `OfflinePenalized`.
  * Contadores diarios con auto-reset por día de juego (`SatietyGameDay`, `SatietyUsed`, `WellFedBuffsUsed`, `SpaEnergyUsed`).
* **`Doors` (`Dictionary<string, DoorAssignment>`):** Vincula cada `DoorId` del pueblo con su `FarmSlotId` (`Pelican4Ever_Farm_01..04`), su `OwnerId`, su `AccessPolicy` (`Locked`, `Friends`, `Public`) y su conjunto `Friends`.

### 3.2. Vista Replicada en Ubicaciones (`GameLocation.modData`)

Solo las ubicaciones (controladas por el Host) exponen metadatos en `location.modData` como vista de lectura para la interfaz y el bloqueo local de herramientas en los clientes:
* `Pelican4Ever.OwnerId` (`long`)
* `Pelican4Ever.AccessPolicy` (`Locked` | `Friends` | `Public`)

---

## 4. Especificación Técnica de Subsistemas (Verificada contra Stardew Valley 1.6.15)

### 4.1. Subsistema 1: Reloj 1:1, `NightHold` y Rollover Nativo

#### A. Cómo funciona el reloj en Stardew Valley 1.6.15 (Decompilado)
1. En `Game1.UpdateGameClock(GameTime time)` (líneas 6050–6090), el Host acumula `gameTimeInterval += time.ElapsedGameTime.Milliseconds` cuando `shouldTimePass() && !IsClient`.
2. Cuando `gameTimeInterval > realMilliSecondsPerGameTenMinutes + ExtraMillisecondsPerInGameMinute * 10` (por defecto `7000 ms`), invoca `Game1.performTenMinuteClockUpdate()`.
3. En `performTenMinuteClockUpdate()` (líneas 5882–6024), el Host suma `timeOfDay += 10`, normaliza minutos (`% 100 >= 60`), aplica `timeOfDay = Math.Min(timeOfDay, 2600)`, ejecuta `location.performTenMinuteUpdate(timeOfDay)`, `MineShaft.UpdateMines10Minutes(timeOfDay)` y propaga el estado a los clientes mediante `netWorldState.Value.UpdateFromGame1()`.
4. Cuando un cliente recibe `NetWorldState` en `Multiplayer.receiveWorldState` (línea 506) y detecta `timeOfDay != Game1.timeOfDay`, ejecuta localmente su propio `Game1.performTenMinuteClockUpdate()`.
5. En `Game1.Update` (línea 6452): `if (timeOfDay >= 2600 || player.stamina <= -15f)` dispara `player.startToPassOut()`.

#### B. Diseño del Parche Temporal (`RealTimeClockModule`)
* **Sin desmayo por horario gracias a `NightHold`:**
  * `GameClock.Read` mapea `06:00 → 0600` hasta `01:50 → 2550`. Entre las `01:50` y las `06:00` reales devuelve `TimeOfDay = 2550, IsNightHold = true`.
  * Como `Game1.timeOfDay` **nunca alcanza `2600`**, la condición `timeOfDay >= 2600` de `Game1.Update` (línea 6452) jamás se activa por el reloj, mientras que el desmayo por agotamiento (`player.stamina <= -15f`) y por vida (`health <= 0`) sigue funcionando de forma 100% nativa sin tocar `Farmer.passOutFromTired`.
* **Sincronización 1:1 dirigida por el Host:**
  * Se fija `Game1.realMilliSecondsPerGameTenMinutes = 600_000` (10 minutos reales) para que la interpolación de luz ambiental de `UpdateGameClock` (líneas 6059 y 6065) use el divisor real y `gameTimeInterval = clockReading.MillisecondsIntoSlot`.
  * En `UpdateTicked` (Host), se bloquea el acumulador libre y se calcula `GameClock.TicksToReach(Game1.timeOfDay, clockReading.TimeOfDay)`: cuando el reloj real cruza un múltiplo de 10 minutos (y no estamos en `IsNightHold`), el Host invoca `Game1.performTenMinuteClockUpdate()` el número exacto de pasos necesarios. Los clientes reciben el cambio por `NetWorldState` y ejecutan su tick automáticamente.
* **Tiempo continuo sin pausas:**
  * Se parchea `Game1.shouldTimePass` (y en modo servidor dedicado se impide que `DedicatedServer.Tick` fije `Game1.netWorldState.Value.IsPaused = true` cuando no hay jugadores conectados y que los menús/eventos individuales pausen `IsTimePaused`).

#### C. Rollover Diario Nativo a las `06:00 AM` (`DailyRolloverModule`)
* A las `06:00:00` reales, el Host sincroniza la fecha objetivo con `ServerCalendar` y fuerza la transición diaria nativa (`Game1.NewDay(0f)` / `ReadyCheck` de sueño automático), dejando que `_newDayAfterFade()` ejecute todo el pipeline de cambio de día y `SaveGame.Save()`.
* En `DayStarted`:
  * Evalúa `SleepPolicy.Evaluate` sobre cada `PlayerRecord` del día que acaba de cerrarse.
  * Aplica `SleepPolicy.WakeUpEnergy` y `SleepPolicy.MedicalFee` sobre el `Farmer` correspondiente (usando carteras separadas `useSeparateWallets`).
  * Verifica con `ServerCalendar.PlanCatchUp` si la fecha de la partida coincide con la fecha real derivada de `CalendarEpoch`.

---

### 4.2. Subsistema 2: Plaza Central, Puentes Declarativos y Residencias Privadas (`FarmSlotModule`)

1. **Registro Declarativo en `Data/Locations` (Sin ubicaciones huérfanas al cargar):**
   * En `SaveGame.loadDataToLocations`, Stardew Valley 1.6 crea las ubicaciones a partir de `Data/Locations` (`CreateOnLoad`).
   * Mediante `Helper.Events.Content.AssetRequested` sobre `"Data/Locations"`, el mod inyecta de forma declarativa:
     - Las residencias privadas `Pelican4Ever_Farm_01` .. `Pelican4Ever_Farm_16` (según `FarmSlotCount`), con `CreateOnLoad = new CreateLocationData { MapPath = template.MapAssetKey, Type = "StardewValley.Farm, Stardew Valley", AlwaysActive = true }`.
     - Los mapas puente activos `Pelican4Ever_Bridge_1..3` calculados determinísticamente según `FarmSlotCount` (`ceil((FarmSlotCount - 4) / 4)`): tipo `GameLocation`, exteriores (`Nature.NIV.Outside = "true"`), sin recurrir jamás a `Game1.locations.Add` ni a puentes volátiles en memoria.
   * Nota de motor (verificada en `GameLocation.cs` línea 2459): `CanBuildHere()` exige que `Game1.multiplayer.isAlwaysActiveLocation(this)` sea `true`. Para los granjeros del servidor se mantiene `AlwaysActive = true`, lo que garantiza compatibilidad total con Robin, edificios y animales con un coste de red despreciable.

2. **Generación Determinista de Mapas Puente (`MapCloneHelper.CreateBridgeMap`):**
   * Cada mapa puente `Pelican4Ever_Bridge_N` (35×30) se genera recortando el área visible central `(10, 0, 35, 30)` de `Maps/BusStop`.
   * El generador limpia el autobús averiado y arbustos centrales, sella el perímetro contra el vacío salvo las aperturas de portales y pasos activos, coloca vallas en la antigua carretera y talla los pasos Este y Oeste escribiendo los warps declarativos continuos en `map.Properties["Warp"]`.

3. **Arquitectura de Dominio Puro (`Pelican4Ever.Core/World/`):**
   * **`HostPlazaLayout`:** Define la topología y dimensiones de la plaza (`Farm` de 80×65 y puentes `Pelican4Ever_Bridge_1..3` de 35×30), el conteo de puentes según `FarmSlotCount` y los pasos encadenados Este/Oeste con sus coordenadas de llegada bidireccionales.
   * **`DoorCatalogValidator`:** Valida el catálogo de 16 portales residenciales en `doors.json` (borde explícito `PortalEdge`, pertenencia a la plaza, márgenes y buzón adyacente).
   * **`SpatialWarpRouter`:** Calcula de forma determinista las casillas y orientación de llegada a la residencia según el borde cardinal del portal (`North`, `South`, `West`, `East`) y la plantilla de granja, las casillas de retorno a la plaza frente a la apertura, y las casillas de disparo (`IsPortalTriggerTile`).
   * **`TerrainTileClassifier`:** Clasifica el terreno para el tallado y autotiling procedural de caminos de losas (`stepping_stones`) respetando copas de árboles y terreno firme.
   * **`ResidenceExits`:** Determina los puntos de salida de cada plantilla de residencia hacia el exterior.
   * **`PlazaCatalogFormatter`:** Formatea limpiamente las listas de residencias y estados de parcelas para los comandos `/residencias` de chat y consola.
   * **`PlazaTopologyVerifier`:** Proporciona utilidades puras para el cálculo de áreas de despeje interior de 3×4 (`GetInteriorClearanceTiles`), serialización y parseo de cadenas `Warp` (`BuildPassageWarpString`, `ParseWarpProperty`), resolución segura de retorno a la plaza (`ResolvePlazaReturnTarget`), y verificación BFS de conectividad topológica (`IsConnected`).

4. **Vinculación de Residencias, Portales y Buzones:**
   * Cada residencia `Pelican4Ever_Farm_0X` aloja la `Cabin` asociada al `Farmhand` propietario y publica `OwnerId` y `AccessPolicy` en `location.modData`.
   * Los 16 portales residenciales se distribuyen entre la Granja Host (4 portales) y los puentes (4 portales por puente).
   * Al caminar hasta el portal hacia el borde del mapa, el mod abre el diálogo de fundación si la parcela está libre, o teletransporta de forma fluida a la residencia privada si está ocupada y permitida.
   * Cada portal dispone de un buzón adyacente individual (`TownMailboxModule`), permitiendo gestionar el correo personal al entrar o salir de la residencia sin interferencias.

---

### 4.3. Subsistema 3: Energía, Saciedad, Precios y Minas 12h (`EconomyModule`)

1. **Presupuesto de Saciedad en `Farmer.doneEating` (líneas 8851–8945):**
   * En `Farmer.doneEating()`, el motor suma `Stamina = Math.Min(MaxStamina, Stamina + @object.staminaRecoveredOnConsumption())` y `health = Math.Min(maxHealth, health + @object.healthRecoveredOnConsumption())` y aplica los `GetFoodOrDrinkBuffs()`.
   * `Prefix` guarda `float prevStamina = __instance.Stamina`.
   * `Postfix` calcula `int requestedGain = (int)Math.Round(__instance.Stamina - prevStamina)`, consulta `SatietyBudget.AllowedGain(requestedGain, record.SatietyUsed, budget)` y fija `__instance.Stamina = prevStamina + allowedGain` (respetando casos especiales como `Stardrop (O)434`). Si el ítem es un plato cocinado (`Category == Object.CookingCategory`) y quedan huecos en `MaxDailyWellFedBuffs`, activa el contador de regeneración pasiva (`+1 Energía / 60s reales`).
2. **Persistencia de Minas por Turnos de 12h (`06:00` y `18:00`):**
   * En `MineShaft.UpdateMines10Minutes` (línea 4788), el motor llama en cada tick de 10 minutos a `MineShaft.clearInactiveMines()` (privado estático, línea 4704), que descarga todos los niveles donde no hay jugadores. Al cambiar de día (`Game1.cs` línea 7925) llama a `MineShaft.clearActiveMines()`.
   * Se aplica un `Prefix` sobre `MineShaft.clearInactiveMines` que devuelve `false` durante el turno normal, y a las `06:00` y `18:00` reales el Host invoca la limpieza sobre los pisos sin jugadores activos (notificando por HUD el nuevo turno minero).

---

### 4.4. Subsistema 4: Vida Urbana y Festivales (`WorldLifeModule` — Post-MVP)

* **Micro-Wandering de NPCs (`NpcMicroWanderController`):** En el Host, cuando un `NPC` finaliza su `PathFindController` horario, guarda su casilla ancla y cada 45–90 segundos reales camina (`speed = 1`) a una casilla libre en un radio $\pm 2$ que no sea `Warp` ni colisione.
* **Festivales en Tarde/Noche (`FestivalInstanceManager`):**
  * Reprogramación de fechas editando `Data/Festivals/FestivalDates` en `AssetRequested` + `InvalidateCache`.
  * En `Data/Festivals/<season><day>`, se reescribe el segundo token de `conditions` a `"1800 2350"` para trasladar la ventana de acceso a las `18:00–23:50` reales y se intercepta el bloqueo `ReadyCheckDialog("festivalStart")` de `Game1.warpFarmer` (línea 9978) para permitir el tránsito normal por `Town` a quienes elijan no entrar al evento.

---

## 5. Registro de Decisiones de Arquitectura (ADRs)

Las decisiones arquitectónicas clave y sus alternativas descartadas están documentadas en [`docs/adr/`](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/docs/adr):

* **ADR-0001:** Rollover diario mediante el pipeline nativo `NewDay` + `Save` a las `06:00 AM`.
* **ADR-0002:** Reloj 1:1 (`0600–2550`) con congelación en Madrugada (`NightHold`) y calendario derivado de `CalendarEpoch`.
* **ADR-0003:** 4 huecos de granja declarativos (`Data/Locations`) con plantilla Estándar para el MVP.
* **ADR-0004:** Autoridad en el Host (`ServerWorldState`), mod obligatorio en clientes y registro modular aislado (`IModule`).
* **ADR-0005:** Plaza Central en la Granja Host y Mapas Puente Declarativos (`Data/Locations`) para el escalado de hasta 16 residencias privadas.
