# Product & Technical Backlog — *Pelican 4Ever* (`SV-RTMP`)

Basado en las especificaciones de [design.md](file:///home/mtx/Documentos/Projects/Stardew Valley MORPG/design.md) y [architecture.md](file:///home/mtx/Documentos/Projects/Stardew Valley MORPG/architecture.md).

---

## 🛠️ 1. Herramientas Necesarias (Toolchain & Entorno)

### 1.1. Desarrollo y Compilación (.NET / SMAPI)
* **.NET SDK 6.0 / 8.0 (`dotnet-sdk`)**: Stardew Valley 1.6.x corre sobre .NET 6.0; requerido para compilar el proyecto C# (`Pelican4Ever.csproj`).
* **NuGet Package — `Pathoschild.Stardew.ModBuildConfig` (v4.1+)**: Automatiza las referencias a las DLLs del juego (`Stardew Valley.dll`, `StardewValley.GameData.dll`), SMAPI y `0Harmony.dll` (`EnableHarmony = true`), además de empaquetar y desplegar el mod en la carpeta `Mods/`.
* **SMAPI 4.x+ (Stardew Modding API)**: API base para eventos del ciclo de vida, comandos de consola, inyección de assets (`AssetRequested`) y persistencia (`modData` / `WriteSaveData`).
* **Harmony 2.x (`Lib.Harmony`)**: Incluido con SMAPI para parches `Prefix`, `Postfix` y `Transpiler` en métodos internos del motor.
* **IDE C#**: JetBrains Rider, Visual Studio Code (con *C# Dev Kit*) o Visual Studio, configurado con depuración adjunta al proceso `Stardew Valley`.

### 1.2. Ingeniería Inversa e Inspección del Motor
* **ILSpy / AvaloniaILSpy** (o **dotPeek**): Imprescindible en Linux para descompilar `Stardew Valley.dll` y verificar firmas exactas de métodos (`Game1.UpdateGameClock`, `Farmer.passOutFromTired`, `MineShaft.clearActiveMines`, `Event.tryToLoadFestival`, etc.).
* **Decompilado de Contenido (`StardewXnbHack`)**: Herramienta para desempaquetar los archivos `.xnb` de `Content/` (mapas `.tmx`, `Data/Festivals/*`, `Data/Objects`, `Data/Machines`) y consultar coordenadas de puertas, warps y horarios de NPCs.

### 1.3. Edición de Mapas y Coordenadas
* **Tiled Map Editor**: Para inspeccionar `Town.tmx` y las plantillas de granja (`Farm.tmx`, `Farm_Forest.tmx`, etc.), identificar coordenadas de puertas interactivas en Pelican Town, posicionar las `Cabin`/`FarmHouse` en `(64, 14)` y definir propiedades `Action` / `TouchAction`.
* **Debug Mode (Mod de SMAPI)**: Mod de soporte durante el desarrollo para visualizar coordenadas de casillas (`Tile X, Y`), nombres de mapas e inspeccionar `Warps` en vivo.

### 1.4. Entorno de Pruebas Multijugador Local y Servidor Dedicado
* **Instancias Múltiples en Local**: Configuración de lanzamiento con argumentos `--mods-path` o clientes secundarios en ventana para probar flujos `Host (MasterGame)` + múltiples `Farmhands` en una misma máquina.
* **Docker + Docker Compose**: Basado en `JunimoServer` o imagen Linux (`ubuntu`/`debian`) con:
  * **Xvfb** (*X Virtual Framebuffer*) o **Xorg dummy** para renderizado headless.
  * **x11vnc / noVNC**: Acceso visual ligero vía navegador para depurar el estado del Host oculto.
  * **SteamCMD**: Descarga y actualización automatizada de los binarios de Stardew Valley 1.6 (64-bit Linux).

---

## 📋 2. Backlog del Proyecto por Épicas e Iteraciones

### Fase 0: Fundamentos, Scaffolding y Modelos de Datos
> **Objetivo:** Inicializar la solución C#, la configuración del servidor y el contrato de datos sincronizado en `modData`.

- [x] **TASK-001: Inicialización de la solución `Pelican4Ever` y `manifest.json`**
  - **Prioridad:** Alta (Bloqueante)
  - **Descripción:** Crear `Pelican4Ever.csproj` con `Pathoschild.Stardew.ModBuildConfig`, habilitar Harmony (`<EnableHarmony>true</EnableHarmony>`), definir `manifest.json` y el punto de entrada `ModEntry.cs`.
  - **Criterios de Aceptación:** El proyecto compila sin errores en Linux y SMAPI carga el mod mostrando su versión en consola.
- [x] **TASK-002: Configuración del servidor (`ServerConfig.cs`)**
  - **Prioridad:** Alta
  - **Descripción:** Implementar el modelo de configuración serializable en `config.json` con: hora de Rollover (`05:30`), turnos de reinicio de minas (`05:30`, `17:30`), multiplicadores económicos (`2.2x` cosechas, `1.8x` procesados), límite de stacks de saciedad (`2`) y huso horario.
  - **Criterios de Aceptación:** Se genera `config.json` automáticamente en el primer arranque con valores por defecto válidos.
- [x] **TASK-003: Definición de `ModDataKeys`, `PlayerPersistentData` y `ServerWorldState`**
  - **Prioridad:** Alta
  - **Descripción:** Implementar las constantes de `ModDataKeys` para `Farmer.modData` (`SleepState`, `AssignedFarmId`, `FarmMapType`, `SatietyStacks`, `LastLoginDay`) y `GameLocation.modData` (`FarmOwnerId`, `DoorAccessPolicy`), junto con los modelos de estado global guardados vía `Helper.Data.WriteSaveData`.
  - **Criterios de Aceptación:** Lectura/escritura tipada mediante métodos de extensión sobre `Farmer` y `GameLocation`, sincronizada por `NetFields`.

---

### Iteración 1: El Motor Temporal y Sueño Asíncrono (Épica 1)
> **Objetivo:** Sincronizar el reloj del servidor 1:1 con la hora real, eliminar el colapso de las 02:00 AM, habilitar el *Bed-Logout* y ejecutar el *Daily Rollover* a las 05:30 AM sin dormir.

- [ ] **TASK-101: Parche de Reloj en Tiempo Real 1:1 (`RealTimeClockPatcher.cs`)**
  - **Prioridad:** Crítica
  - **Descripción:**
    - Parche `Prefix` en `Game1.UpdateGameClock` (solo en `Game1.IsMasterGame`) fijando `Game1.gameTimeInterval = 0`.
    - En `OneSecondUpdateTicked`, sincronizar `Game1.timeOfDay` con `DateTime.Now` en saltos de 10 minutos para disparar máquinas y horarios de NPCs correctamente durante las 24h (`00:00` a `23:59`).
  - **Criterios de Aceptación:** El reloj del juego refleja la hora del sistema anfitrión y no avanza por el temporizador nativo de 7 segundos.
- [ ] **TASK-102: Curva de iluminación ambiental continua de 24 horas**
  - **Prioridad:** Media
  - **Descripción:** Ajustar el cálculo de luz ambiental del motor para cubrir los tramos `06:00–07:30` (amanecer gradual), `07:30–18:30` (luz diurna estacional), `18:30–20:30` (atardecer progresivo) y `20:30–06:00` (noche cerrada con farolas y antorchas activas).
  - **Criterios de Aceptación:** No se producen saltos bruscos de color a medianoche ni a las 02:00 AM.
- [ ] **TASK-103: Supresión del desmayo a las 02:00 AM (`Farmer.passOutFromTired`)**
  - **Prioridad:** Crítica
  - **Descripción:** Parche `Prefix` en `Farmer.passOutFromTired` que devuelve `false` cuando la causa es el horario, manteniendo intacto el desmayo por llegar a `0` de Energía o Salud.
  - **Criterios de Aceptación:** Un jugador conectado a las 02:00 AM o 03:30 AM continúa jugando con normalidad.
- [ ] **TASK-104: Sincronización perpetua de Calendario (Día 1 = Lunes, 28 días)**
  - **Prioridad:** Media
  - **Descripción:** Alinear el cálculo de `Game1.dayOfMonth` con la semana real para que el Día 1 de cada estación sea siempre Lunes y el Día 7 sea Domingo. Añadir comando de consola `sv_sync_calendar` para inicializar la partida maestra alineada al día de la semana actual.
  - **Criterios de Aceptación:** Eventos semanales (Carro Ambulante viernes/domingos, cierre de Pierre miércoles) coinciden con la semana real.
- [ ] **TASK-105: Mecánica de Sueño Asíncrono y *Bed-Logout* (`BedLogoutPatch`)**
  - **Prioridad:** Crítica
  - **Descripción:**
    - Al conectar un jugador (`PeerConnected` / `SaveLoaded`), cambiar su estado `ModDataKeys.SleepState` a `"Awake"`.
    - Parche `Prefix` en `GameLocation.answerDialogueAction` para `"Sleep_Yes"`: registrar `SleepState = "Rested"`, mostrar `HUDMessage` de confirmación y ejecutar `Game1.ExitToTitle()` tras 1500ms sin iniciar el cierre de día global.
  - **Criterios de Aceptación:** Un jugador que duerme en su cama se desconecta limpiamente con estado `"Rested"` mientras los demás siguen jugando en el servidor.
- [ ] **TASK-106: Gestor del Rollover Diario en Caliente a las 05:30 AM (`DailyRolloverManager.cs`)**
  - **Prioridad:** Crítica
  - **Descripción:**
    - Aviso HUD 60 segundos antes (`05:29:00`) a los jugadores conectados y bloqueo breve de input durante la transición.
    - A las `05:30:00`: incrementar `Game1.dayOfMonth` (y rotar `Game1.currentSeason` / `Game1.year` tras el día 28), ejecutar `location.DayUpdate(Game1.dayOfMonth)` en todas las ubicaciones (incluidas granjas instanciadas), liquidar `Shipping Bins`, repartir correo y regenerar forraje.
    - Evaluar la máquina de estados de sueño en todos los `Farmer`:
      - `Rested` $\rightarrow$ `Offline_Safe` (100% Energía).
      - `Awake` $\rightarrow$ `Offline_Penalized` (cargo médico único del 10% de oro hasta 1.000g y Energía al 50%).
      - `Offline_Safe` / `Offline_Penalized` sin conexión $\rightarrow$ se mantienen sin penalizaciones adicionales.
    - Ejecutar guardado maestro y comando de prueba manual `rollover_now`.
  - **Criterios de Aceptación:** Al ejecutar `rollover_now` o llegar las 05:30 AM, los cultivos regados avanzan de fase, se cobra la caja de envíos y se aplican los estados de energía/penalización sin reiniciar el servidor.

---

### Iteración 2: Vivienda Urbana y Enrutador de Granjas Instanciadas (Épica 2)
> **Objetivo:** Permitir que cada jugador reclame una puerta en Pelican Town, elija su plantilla de granja y disponga de una instancia aislada sincronizada con el netcode nativo (`Cabin`).

- [ ] **TASK-201: Cargador dinámico de Granjas Instanciadas (`DynamicFarmLoader.cs`)**
  - **Prioridad:** Crítica
  - **Descripción:** Instanciar objetos `Farm(mapAssetPath, $"Farm_{player.UniqueMultiplayerID}")` según la plantilla elegida (*Estándar, Bosque, Cuatro Esquinas, Playa, Salvaje, Colina, Pradera*) y registrarlos en `Game1.locations` del Host.
  - **Criterios de Aceptación:** Dos clientes conectados pueden estar simultáneamente en sus respectivas granjas `Farm_<UID>` sin colisión de objetos ni cultivos.
- [ ] **TASK-202: Reubicación y camuflaje de `Cabin` como `FarmHouse` (`CabinVaultManager.cs`)**
  - **Prioridad:** Crítica
  - **Descripción:** Mover la instancia de `Cabin` asignada al `Farmhand` desde la granja base del Host hacia su granja instanciada `Farm_<UID>` en las coordenadas `(64, 14)`, aplicando la textura de `FarmHouse` principal.
  - **Criterios de Aceptación:** El buzón propio, el punto de aparición al entrar al servidor y las mejoras de casa con Robin funcionan sobre la granja instanciada del jugador.
- [ ] **TASK-203: Registro de puertas en Pelican Town y flujo de reclamación inicial (`TownDoorInterceptor.cs`)**
  - **Prioridad:** Alta
  - **Descripción:**
    - Definir el catálogo de coordenadas de puertas reclamables en `Town`.
    - Si un jugador sin `AssignedFarmId` interactúa con una puerta libre, mostrar menú de selección de plantilla de granja, crear su instancia y vincular la puerta.
    - Si interactúa con una puerta ya asignada, mostrar diálogo: *"Ir a mi granja"*, *"Visitar a un vecino..."* o *"Cancelar"*.
    - Parchear `Game1.warpFarmer` al salir de `"Farm_*"` hacia `BusStop`/`Town` para devolver al jugador frente a su puerta asignada en Pelican Town.
  - **Criterios de Aceptación:** El ciclo de entrada desde Pelican Town a la granja instanciada y salida de vuelta a la puerta del pueblo funciona en ambos sentidos.
- [ ] **TASK-204: Sistema de permisos de visita y protección anti-griefing**
  - **Prioridad:** Media
  - **Descripción:**
    - Menú interior en la puerta de la granja para configurar `DoorAccessPolicy`: `Public`, `Friends` o `Locked`.
    - En granjas donde `Game1.player.UniqueMultiplayerID != FarmOwnerId`, bloquear uso de picos, hachas, azadas destructivas, bombas y apertura de cofres (permitiendo usar la regadera en modo `Public`/`Friends`).
  - **Criterios de Aceptación:** Un visitante no autorizado no puede entrar si está `Locked`, y dentro de una granja ajena no puede destruir cultivos ni abrir cofres.

---

### Iteración 3: Economía, Energía, Procesado Real y Minas Persistentes (Épica 3)
> **Objetivo:** Equilibrar el presupuesto diario de energía con la velocidad de juego en tiempo real, escalar el procesado industrial y mantener las minas persistentes en turnos de 12 horas.

- [ ] **TASK-301: Reforma de Comida y Saciedad Pasiva (`StaminaFoodPatcher.cs`)**
  - **Prioridad:** Alta
  - **Descripción:**
    - Parche `Prefix`/`Postfix` en `Farmer.doneEating` para restaurar `__instance.Stamina` a su valor previo, conservando la curación de `Health` y los `Buffs`.
    - Implementar el sistema de *Saciedad* (`ModDataKeys.SatietyStacks`, máx. 2 platos diarios) que regenera `+1 Energía` cada 60 segundos reales durante 60 minutos, y ajustar el Spa para restaurar `+1 Energía` cada 30 segundos reales en reposo. Resetear `SatietyStacks` en el Rollover de las 05:30 AM.
  - **Criterios de Aceptación:** Comer alimentos cura vida y aplica buffs sin subir energía de golpe; comer un plato del día activa el tick pasivo de +1 energía/minuto.
- [ ] **TASK-302: Minas Persistentes por Turnos de 12 Horas (`PersistentMineManager.cs`)**
  - **Prioridad:** Alta
  - **Descripción:**
    - Parche `Prefix` en `MineShaft.clearActiveMines` que impide el vaciado de `MineShaft.activeMines` cuando el conteo de jugadores llega a 0.
    - Cronjob en el servidor a las `05:30 AM` y `17:30 PM` reales que invoca la limpieza real de `MineShaft.activeMines` y notifica por HUD el inicio de un nuevo turno minero.
  - **Criterios de Aceptación:** Si un jugador pica todas las rocas del piso 5, sale de la mina y otro jugador entra 20 minutos después (dentro del mismo turno de 12h), el piso 5 sigue limpio.
- [ ] **TASK-303: Escalado de máquinas en tiempo real**
  - **Prioridad:** Media
  - **Descripción:** Ajustar `minutesUntilReady` de las máquinas procesadoras (hornos ~30 min reales, envasadoras 2–3 días reales, barriles 6–7 días reales) para que operen de forma coherente con el reloj 1:1 y con el `DayUpdate` del Rollover.
  - **Criterios de Aceptación:** Un horno funde un lingote tras 30 minutos reales; barriles y envasadoras descuentan el tiempo correctamente cada 10 minutos y durante el Rollover.
- [ ] **TASK-304: Multiplicadores de precios agrícolas y artesanales (`PriceMultiplierPatcher.cs`)**
  - **Prioridad:** Media
  - **Descripción:** Parche `Postfix` en `StardewValley.Object.sellToStorePrice` aplicando los multiplicadores de `ServerConfig` (`2.2x` para cosechas directas y `1.8x` para productos artesanales/procesados).
  - **Criterios de Aceptación:** El valor de venta en tiendas y en la Caja de Envíos refleja los multiplicadores configurados.

---

### Iteración 4: Vida Urbana (NPCs), Festivales e Infraestructura (Épica 4)
> **Objetivo:** Dotar de vida continua a los NPCs en horario 1:1, instanciar festivales en horario *Prime-Time* (18:00–23:59) sin bloquear el pueblo y empaquetar el servidor dedicado.

- [ ] **TASK-401: Controlador de *Micro-Wandering* de NPCs (`NpcMicroWanderController.cs`)**
  - **Prioridad:** Media
  - **Descripción:**
    - Reducir un 50% la velocidad de desplazamiento en ruta de los NPCs (paso de paseo).
    - En `OneSecondUpdateTicked` (Host), registrar el `Tile` de destino como ancla cuando el NPC termina su ruta. Cada 45–90 segundos reales, mover al NPC con `PathFindController` a una casilla libre dentro de un área segura de $4\times 4$ ($\pm 2$ casillas) verificando colisiones y evitando `Warps`, alternando orientaciones y bocadillos de texto ambiental.
    - Limpiar el ancla en `NPC.checkSchedule` cuando toque cambiar de destino horario.
  - **Criterios de Aceptación:** Los NPCs no permanecen estáticos durante horas en su punto de destino y nunca activan un `Warp` accidentalmente al deambular.
- [ ] **TASK-402: Reprogramación dinámica de Festivales (`FestivalInstanceManager.cs`)**
  - **Prioridad:** Media
  - **Descripción:**
    - Comando SMAPI `festival_reschedule <season> <oldDay> <newDay>` y comando de chat `/festival move <oldDay> <newDay>` para administradores.
    - Interceptar `Data/Festivals/FestivalDates` mediante `Helper.Events.Content.AssetRequested` y refrescar en vivo con `InvalidateCache`.
  - **Criterios de Aceptación:** Al mover un festival de día, el calendario frente a la tienda de Pierre refleja la nueva fecha inmediatamente sin reiniciar el servidor.
- [ ] **TASK-403: Festivales Instanciados en Prime-Time (18:00 – 23:59) sin bloqueo de tránsito**
  - **Prioridad:** Alta
  - **Descripción:**
    - Parchear `Event.tryToLoadFestival` para evitar el bloqueo global (`NetReady`) de todos los jugadores conectados.
    - Entre las `18:00` y las `23:59` de un día de festival, cargar la instancia paralela `Town_Festival` conservando el mapa `Town` ordinario abierto al tránsito.
    - Al cruzar un `Warp` hacia `Town` en esa franja, preguntar al jugador: *"¿Entrar al Festival de [Nombre] o cruzar el pueblo normalmente?"*, registrando su asistencia individual, compras y conversaciones con NPCs.
  - **Criterios de Aceptación:** Un jugador puede asistir al festival a las 19:30 mientras otro jugador cruza `Town` hacia la playa sin ser arrastrado al evento.
- [ ] **TASK-404: Modo Host Headless y Contenedor Docker (`JunimoServer`)**
  - **Prioridad:** Media
  - **Descripción:**
    - Lógica de servidor dedicado en `ModEntry`: si se ejecuta en modo Host dedicado, mantener a `Game1.player` oculto, invulnerable, con estado de sueño ignorado y congelado fuera de los límites del mapa.
    - Crear `Dockerfile` y `docker-compose.yml` para desplegar el servidor con `Pelican4Ever` preinstalado, volúmenes persistentes para `Saves/` y `config.json`.
  - **Criterios de Aceptación:** El contenedor levanta la partida automáticamente en Linux, acepta conexiones entrantes 24/7 y ejecuta el Rollover de las 05:30 AM sin intervención humana.
