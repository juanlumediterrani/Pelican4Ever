# `ARCHITECTURE.md` (Technical Architecture Document)

## 1. Stack Tecnológico y Topología de Red

* **Plataforma objetivo:** Stardew Valley 1.6.x (64-bit) sobre Linux (x86_64).
* **Frameworks de inyección:** SMAPI 4.x+ y **Harmony 2.x** (`Lib.Harmony`).
* **Infraestructura de Servidor Dedicado:** Basado en contenedor Docker (**JunimoServer** o imagen propia con `xvfb` + servidor VNC ligero para depuración). El proceso principal ejecuta el binario del juego con `Game1.IsMasterGame == true`, manteniendo al personaje Host (`Game1.player`) oculto, invulnerable y congelado en una coordenada fuera de los límites del mapa.
* **Protocolo de Transporte:** Capa nativa de red de Stardew Valley (`NetRoot`, `NetFields`, `Lidgren.Network` / `GalaxyNet`).

---

## 2. Estructura de la Solución (C#)

```text
Pelican4Ever/
├── manifest.json
├── Pelican4Ever.csproj
├── ModEntry.cs                      # Punto de entrada SMAPI, registro de eventos y Harmony
├── Config/
│   └── ServerConfig.cs              # Multiplicadores, hora de Rollover, cooldowns de mina
├── Models/
│   ├── ModDataKeys.cs               # Claves de modData y métodos de extensión
│   ├── PlayerPersistentData.cs      # Estado de sueño, ID de granja, stacks de saciedad
│   └── ServerWorldState.cs          # Registro de granjas instanciadas y calendario de festivales
├── Core/
│   ├── RealTimeClockPatcher.cs      # Sincronización 1:1 con DateTime.Now y bloqueo de 2:00 AM
│   └── DailyRolloverManager.cs      # Cronjob de las 05:30 AM, ejecución de DayUpdate() en caliente
├── FarmRouter/
│   ├── DynamicFarmLoader.cs         # Instanciación de mapas .tmx en Game1.locations
│   ├── CabinVaultManager.cs         # Vinculación silenciosa de Cabins para el netcode nativo
│   └── TownDoorInterceptor.cs       # Hooks de interacción en puertas de Pelican Town y permisos
├── Economy/
│   ├── StaminaFoodPatcher.cs        # Parche en Farmer.eatObject y regeneración pasiva
│   ├── PersistentMineManager.cs     # Retención de MineShaft en memoria y limpieza cada 12h
│   └── PriceMultiplierPatcher.cs    # Ajuste en Object.sellToStorePrice
└── WorldLife/
    ├── NpcMicroWanderController.cs  # Máquina de estados de movimiento local en puntos de ruta
    └── FestivalInstanceManager.cs   # Clonación de mapas de evento y comando de reprogramación
```

---

## 3. Modelo de Datos y Claves de Persistencia (`modData`)

Para garantizar que los clientes no necesiten bases de datos externas y el estado viaje automáticamente por los `NetFields` nativos del juego, se utiliza el diccionario sincronizado `modData` en las entidades `Farmer` y `GameLocation`, respaldado por `Helper.Data.WriteSaveData` en el servidor:

```csharp
public static class ModDataKeys
{
    // Claves en Farmer.modData (sincronizadas por red automáticamente)
    public const string SleepState = "Pelican4Ever.SleepState";       // "Awake" | "Rested" | "Offline_Safe" | "Offline_Penalized"
    public const string AssignedFarmId = "Pelican4Ever.FarmLocation"; // Ej: "Farm_u_76561198000000000"
    public const string FarmMapType = "Pelican4Ever.FarmMapType";     // "Standard" | "Forest" | "Beach" | "FourCorners" | "Meadowlands"
    public const string SatietyStacks = "Pelican4Ever.SatietyStacks"; // "0" | "1" | "2"
    public const string LastLoginDay = "Pelican4Ever.LastLoginDay";   // int serializado

    // Claves en GameLocation.modData (para control de acceso a granjas)
    public const string FarmOwnerId = "Pelican4Ever.OwnerUniqueMultiplayerID";
    public const string DoorAccessPolicy = "Pelican4Ever.AccessPolicy"; // "Public" | "Friends" | "Locked"
}

```

---

## 4. Especificación de Subsistemas y Parches Harmony

### 4.1. Subsistema 1: Reloj en Tiempo Real y Rollover Diario (`RealTimeClockPatcher`)

Stardew Valley representa la hora en formato entero militar (`0600` a `2600`) en `Game1.timeOfDay` y avanza 10 minutos cada vez que `Game1.gameTimeInterval` supera los `7000ms` (más modificadores de ubicación).

* **Parche 1 (`Prefix` en `Game1.UpdateGameClock`):**
* **Acción:** Se intercepta el método en el servidor (`Game1.IsMasterGame`). Se fija `Game1.gameTimeInterval = 0` permanentemente para anular el avance nativo.
* **Sincronización con hora del sistema:** Cada segundo real (evento `OneSecondUpdateTicked` de SMAPI), se lee `DateTime.Now`. Dado que el motor de Stardew dispara la lógica de máquinas y rutas en incrementos de 10 minutos, el reloj interno se actualiza cuando el minuto real es múltiplo de 10, mapeando las horas de madrugada (`00:00` – `05:20`) al formato extendido o manteniendo el ciclo de 24h con iluminación forzada.


* **Parche 2 (`Prefix` en `Farmer.passOutFromTired`):**
* **Acción:** Devuelve `false` (cancela la ejecución original) si el motivo es la hora del reloj, permitiendo jugar pasada la medianoche sin desmayarse.


* **Ejecución del Rollover en caliente (`DailyRolloverManager`):**
* A las `05:30:00` reales, el servidor ejecuta la transición diaria sin requerir que los jugadores estén en la cama:
1. Si hay jugadores conectados en ese instante, les muestra un *HUD Message* de aviso 60 segundos antes y los teletransporta temporalmente a su habitación o congela su input durante los ~4 segundos que dura el proceso.
2. Incrementa `Game1.dayOfMonth` (y cambia `Game1.currentSeason` si supera el día 28).
3. Itera sobre `Game1.locations` (incluyendo todas las granjas instanciadas) invocando `location.DayUpdate(Game1.dayOfMonth)`.
4. Evalúa la máquina de estados `ModDataKeys.SleepState` sobre `Game1.getAllFarmers()` aplicando las reglas de energía y penalización.
5. Dispara el guardado maestro asíncrono.





```csharp
[HarmonyPatch(typeof(GameLocation), nameof(GameLocation.answerDialogueAction))]
public static class BedLogoutPatch
{
    public static bool Prefix(string questionAndAnswer, ref bool __result)
    {
        if (questionAndAnswer == "Sleep_Yes")
        {
            // En lugar de iniciar el cierre del día para todo el servidor:
            Game1.player.modData[ModDataKeys.SleepState] = "Rested";
            Game1.addHUDMessage(new HUDMessage("Descanso registrado. Desconectando de forma segura...", 2));
            
            // Desconexión limpia del cliente hacia el menú principal tras 1.5s
            DelayedAction.functionAfterDelay(() => Game1.ExitToTitle(), 1500);
            __result = true;
            return false; // Cancela la transición de noche nativa de Stardew
        }
        return true;
    }
}

```

---

### 4.2. Subsistema 2: Enrutador de Granjas Instanciadas y Vínculo de `Cabins`

El protocolo de red de Stardew Valley exige que cada jugador invitado (`Farmhand`) tenga asignada una instancia de la clase `Cabin` para sincronizar su perfil, buzón e inventario desconectado.

* **Creación de la Granja Instanciada (`DynamicFarmLoader`):**
1. Cuando un jugador elige su tipo de granja, el servidor instancia un nuevo objeto `Farm(mapAssetPath, $"Farm_{player.UniqueMultiplayerID}")`.
2. Se añade a `Game1.locations` en el Host. Al estar en `Game1.locations` con su propio `NetRoot`, SMAPI y el motor sincronizan el mapa automáticamente a todos los clientes conectados.


* **Resolución del requisito de `Cabin` (`CabinVaultManager`):**
* En lugar de dejar las cabañas amontonadas en la granja base del Host, el servidor mueve el edificio `Cabin` vinculado al `UniqueMultiplayerID` del jugador dentro de su nueva granja instanciada `Farm_{UID}`, reemplazando sus coordenadas por las de la `FarmHouse` principal (64, 14) y aplicando la textura exterior de casa de granja completa. Así, el buzón, las mejoras de casa con Robin y el punto de aparición al conectar funcionan con el código nativo del juego sin parches adicionales.


* **Intercepción de Puertas en el Pueblo (`TownDoorInterceptor`):**
* Se suscribe a `Helper.Events.Input.ButtonPressed`. Cuando el jugador pulsa el botón de acción frente a las coordenadas de una puerta registrada en el mapa `Town`:
* Muestra un diálogo `createQuestionDialogue` con las opciones: `Ir a mi granja` | `Visitar a un vecino...` | `Cancelar`.
* Al elegir un destino autorizado, ejecuta `Game1.warpFarmer(targetFarmName, entryX, entryY, 2)`.


* Para el camino de salida de la granja instanciada (hacia el Bus Stop / Town), un parche en `Game1.warpFarmer` detecta si el origen empieza por `"Farm_"` y redirige las coordenadas de llegada exactamente frente a la puerta del pueblo correspondiente a esa granja.



---

### 4.3. Subsistema 3: Economía, Energía y Minas Persistentes

* **Reforma de Comida (`StaminaFoodPatcher`):**
* Se aplica un parche `Prefix` y `Postfix` sobre `Farmer.doneEating`:



```csharp
[HarmonyPatch(typeof(Farmer), nameof(Farmer.doneEating))]
public static class StaminaFoodPatcher
{
    public static void Prefix(Farmer __instance, out float __state)
    {
        // Guardamos la energía exacta que tenía el jugador antes de que el método sume la comida
        __state = __instance.Stamina;
    }

    public static void Postfix(Farmer __instance, float __state)
    {
        // Revertimos la ganancia instantánea de energía manteniendo la curación de Health y los Buffs
        __instance.Stamina = __state;
        
        // Opcional: Si el plato otorga regeneración pasiva y no ha superado el límite diario (2)
        int stacks = int.TryParse(__instance.modData.GetValueOrDefault(ModDataKeys.SatietyStacks, "0"), out int s) ? s : 0;
        if (stacks < 2)
        {
            __instance.modData[ModDataKeys.SatietyStacks] = (stacks + 1).ToString();
            Game1.addHUDMessage(new HUDMessage("Saciedad activada: Regeneración pasiva de energía.", 2));
        }
    }
}

```

* **Persistencia de Minas por Turnos de 12 Horas (`PersistentMineManager`):**
* En Stardew Valley vanilla, `MineShaft.UpdateWhenCurrentLocation` y el bucle principal invocan `MineShaft.clearActiveMines()` en cuanto el conteo de jugadores dentro de la mina llega a `0`.
* **Parche (`Prefix` en `MineShaft.clearActiveMines`):** Se bloquea su ejecución ordinaria devolviendo `false`. El método original solo se invoca mediante un flag interno desde el cronjob del servidor a las `05:30 AM` y a las `17:30 PM` reales, vaciando la lista estática `MineShaft.activeMines` dos veces al día.



---

### 4.4. Subsistema 4: Controlador de Deambulación de NPCs (`NpcMicroWanderController`)

Para evitar editar archivos JSON de rutas y mantener compatibilidad total con otros mods, se inyecta un controlador ligero en el evento `OneSecondUpdateTicked` (ejecutado únicamente en el Host):

1. El servidor mantiene un `Dictionary<string, Vector2> NpcScheduleAnchors` en memoria.
2. Cuando un `NPC` finaliza un desplazamiento de su horario (`npc.controller == null` y `!npc.isMoving()`), si no tiene un ancla registrada para esa franja horaria, se guarda su `npc.Tile` actual como centro de su zona de estancia.
3. Cada 45–90 segundos (aleatorizado por instancia de NPC), el servidor elige una casilla candidata dentro de un radio de $\pm 2$ casillas respecto al ancla:
* Verifica `location.isTilePassable(new Location(x, y), Game1.viewport)` y que no colisione con muebles ni salidas de mapa (`Warp`).
* Si es válida, asigna un nuevo `PathFindController(npc, location, targetTile, randomDirection)` de corto alcance y reduce temporalmente `npc.speed = 1` (paso de paseo).


4. Al cambiar la hora del horario oficial (`NPC.checkSchedule`), se limpia el ancla para que el NPC camine hacia su siguiente ubicación del pueblo.

---

### 4.5. Subsistema 5: Festivales Instanciados y Reprogramables (`FestivalInstanceManager`)

* **Reprogramación de Fechas en Caliente:**
* Se registra un comando de consola en SMAPI (`festival_reschedule <season> <oldDay> <newDay>`) y un comando de chat de administrador (`/festival move <oldDay> <newDay>`).
* Suscripción al evento `Helper.Events.Content.AssetRequested`: si `e.NameWithoutLocale.IsEquivalentTo("Data/Festivals/FestivalDates")`, el mod intercepta el diccionario `IDictionary<string, string>` y reemplaza la clave `"<season><oldDay>"` por `"<season><newDay>"`. Al ejecutar el comando, se llama a `Helper.GameContent.InvalidateCache("Data/Festivals/FestivalDates")`, actualizando al instante todos los calendarios del pueblo.


* **Instanciación Paralela en Prime-Time (18:00 – 23:59):**
* Se parchea `Event.tryToLoadFestival` para que no fuerce el bloqueo global del servidor ni exija que todos los jugadores estén presentes (`NetReady`).
* Cuando el reloj del servidor marca las `18:00` en un día de festival, el servidor instancia una ubicación temporal `Town_Festival` cargando el asset del festival (`Maps/Town-EggFestival`, etc.).
* Cuando un jugador cruza un `Warp` hacia `Town` entre las `18:00` y las `23:59`, se intercepta la transición mostrando el diálogo de elección: si elige asistir, se le redirige a `Town_Festival`, se incrementa su contador de participación y se le permite interactuar con los NPCs y tiendas del evento de forma independiente al resto de jugadores.



---

## 5. Hoja de Ruta de Implementación (Fases del MVP)

Para validar la arquitectura sin acumular deuda técnica, el orden óptimo de desarrollo es:

1. **Iteración 1 (El Motor Temporal):** Implementar `RealTimeClockPatcher`, el bloqueo de las 02:00 AM, el parche `BedLogoutPatch` y el `DailyRolloverManager` a las 05:30 AM. Probar en local que los cultivos avanzan de fase al disparar el comando de Rollover sin necesidad de dormir.
2. **Iteración 2 (El Enrutador de Granjas):** Implementar `DynamicFarmLoader` y `TownDoorInterceptor`. Validar que dos clientes conectados ven sus respectivas granjas aisladas al cruzar su puerta en Pelican Town y que los objetos colocados persisten tras el Rollover.
3. **Iteración 3 (El Parche de Economía):** Activar `StaminaFoodPatcher`, el retén de 12 horas en `PersistentMineManager` y los multiplicadores de precios.
4. **Iteración 4 (Vida Urbana y Despliegue):** Añadir `NpcMicroWanderController`, el instanciador de festivales y empaquetar el mod sobre el contenedor Docker de `JunimoServer`.