## 1. Visión General del Proyecto

* **Nombre:** *Pelican 4Ever* (`SV-RTMP`)
* **Concepto nuclear:** Transformar Stardew Valley de un juego cooperativo de sesiones síncronas y turnos rápidos a un **mundo persistente en tiempo real (escala 1:1)** alojado en un servidor dedicado 24/7.
* **Escala y Modelo de Confianza (MVP):** Diseñado para comunidades pequeñas (**4 jugadores en el MVP**, escalable a ~8–12). Funciona bajo un modelo de **servidor entre amigos con cliente modificado obligatorio** (`RequireClientMod = true`): tanto el servidor como los clientes instalan `Pelican 4Ever`.
* **Pilar de experiencia:** Un "segundo hogar" virtual. El jugador entra entre 30 y 90 minutos al día para cuidar su granja privada (conectada a una puerta de Pelican Town), gestionar su industria pasiva, gastar su presupuesto diario de energía + saciedad en las minas o la pesca y coincidir en el pueblo o en festivales de tarde/noche con el resto de la comunidad.

---

## 2. Sistema Temporal y Calendario

### 2.1. Reloj en Tiempo Real (1:1) y "Madrugada" (`NightHold`)

El motor de Stardew Valley codifica el reloj en formato entero (`0600` a `2600`) y fuerza el desmayo en cuanto alcanza `2600` (`Game1.Update` / `performTenMinuteClockUpdate`). Para evitar desmayos forzosos sin reescribir las constantes internas del motor:

* **Inicio del día (`06:00` real = `0600` juego):** El día de juego comienza a las **06:00 AM** de la zona horaria del servidor (ej. `Europe/Madrid`).
* **Avance 1:1 (`06:00` a `01:50` real → `0600` a `2550` juego):** Cada 10 minutos reales el reloj avanza 10 minutos de juego (`600` → `2400` a medianoche → `2550` a las `01:50 AM`).
* **Tramo de Madrugada (`01:50` a `06:00` real → congelado en `2550`):** A partir de las `01:50 AM` reales, el reloj entra en estado **`NightHold`** (congelado en `2550`, nunca llega a `2600`). Los jugadores noctámbulos pueden seguir pescando, minando o decorando sin sufrir desmayo por horario; al llegar el Rollover de las `06:00 AM`, el motor procesa automáticamente los minutos restantes de madrugada (`2550 → 0600`) sobre hornos y máquinas mediante `Utility.CalculateMinutesUntilMorning`.
* **Iluminación ambiental continua:** Interpolada suavemente en cada tramo de 10 minutos reales (`MillisecondsIntoSlot` sobre `600.000 ms`).

### 2.2. Calendario Perpetuo Basado en Época (`CalendarEpoch`)

* **Duración de estación:** Se mantiene el mes nativo de **28 días reales** (4 semanas exactas; 112 días reales por año de juego).
* **Sincronización perpetua por Época:** La fecha del juego (`Year`, `Season`, `Day`) se **calcula matemáticamente** a partir de una fecha lunes real configurada (`CalendarEpoch`, p. ej. `2026-11-02` = Día 1 de Primavera, Año 1).
  * El Día 1 de cada estación es siempre **Lunes real** y el Día 7 es siempre **Domingo real**.
  * Si el servidor sufre una caída durante la noche, al arrancar compara `Game1.Date.TotalDays` con la fecha derivada del calendario real y aplica el plan de recuperación (`CatchUpPlan`: ejecuta hasta `MaxCatchUpRollovers` seguidos o salta a la fecha actual).

### 2.3. El "Rollover" Diario Nativo (`06:00 AM`)

Todos los días a las **06:00 AM (hora del servidor)** se ejecuta la transición diaria apoyándose en el **flujo nativo del motor (`Game1.NewDay` + `SaveGame.Save`)**:

1. **Avisos previos:** Se notifican avisos en el HUD a los jugadores conectados **15, 5 y 1 minuto** antes del corte (`05:45`, `05:55`, `05:59`).
2. **Transición nativa segura:** Si hay jugadores conectados a las `06:00 AM`, verán la pantalla nativa de fin de día y guardado (10–25 segundos). Forzar el flujo nativo garantiza que se ejecuten sin fisuras todas las rutinas de Stardew Valley 1.6 (crecimiento de cultivos, `Shipping Bins` personales, correo, clima, suerte, animales, amistad, `netWorldState` y serialización XML).
3. **Resolución de descanso y copia de seguridad:** Al completarse el cambio de día se evalúa la política de sueño de cada jugador registrado y se guarda un backup rotativo de la carpeta del save.

---

## 3. Mecánica de Sueño Asíncrono ("Bed-Logout")

El estado autoritativo de cada jugador reside únicamente en el servidor (`ServerWorldState.Players[UID]`). Los estados `SleepState` se **derivan** a partir de `LastActiveGameDay`, `RestedOnGameDay` y `LastOutcome`:

| Estado Derivado | Condición | Efecto al cerrar el día en el Rollover (`06:00 AM`) |
| --- | --- | --- |
| **`Awake`** | Conectado hoy (`isOnline` o `LastActiveGameDay == hoy`) y aún no ha usado la cama (`RestedOnGameDay != hoy`). | Si llega el Rollover de las `06:00 AM` sin haber descansado (o sigue conectado fuera de la cama), **sufre penalización única** (`RolloverOutcome.Penalized`) y pasa a `OfflinePenalized`. |
| **`Rested`** | El jugador se mete en su cama, selecciona *"Descansar y Desconectar"* (`RestedOnGameDay == hoy`) y sale al menú. | Sin penalización (`RolloverOutcome.Rested`). Despierta con el **100% de su Energía** y pasa a `OfflineSafe`. |
| **`OfflineSafe`** | No ha entrado en el día en curso y su último resultado fue `Rested` o `Untouched`. | Permanece intacto (`RolloverOutcome.Untouched`) aunque pase días o semanas sin conectarse. |
| **`OfflinePenalized`** | No ha entrado en el día en curso y su último resultado fue `Penalized`. | La penalización (10% del oro personal hasta `1.000g` y **50% de Energía**) se aplica **una sola vez**. En días posteriores sin conectarse devuelve `Untouched` y no vuelve a penalizar. |

* **Amistad congelada en ausencia:** Mientras un jugador está en `OfflineSafe` u `OfflinePenalized`, se bloquea la caída diaria de amistad por no hablar con los NPCs.

---

## 4. Economía y Balanceo (Pasivo vs. Activo)

1. **Presupuesto Diario de Energía + Saciedad (`SatietyBudget`):**
   * **Barra base:** El jugador empieza la jornada con su barra de energía (`270` base + *Stardrops*).
   * **Presupuesto de Saciedad (Comida con sentido):** Comer alimentos **sí restaura Energía instantánea**, además de Salud (*Health*) y *Buffs*, pero hasta un **tope diario de Saciedad** equivalente al **50% de su energía máxima** (`DailySatietyFraction = 0.5`, es decir, `+135` de energía extra en una barra de 270). Una vez agotado el presupuesto diario de Saciedad, se puede seguir comiendo para curar Vida en combate o mantener *Buffs*, pero la ganancia de Energía instantánea se recorta a `0`.
   * **Platos Cocinados ("Bien comido"):** Hasta **2 veces al día** (`MaxDailyWellFedBuffs = 2`), comer un plato cocinado activa regeneración pasiva (`+1 Energía/minuto real` durante 60 minutos).
   * **Spa con tope diario:** El Spa restaura `1 punto de Energía cada 30 segundos reales`, con un **tope máximo de `+60 Energía` por día real** (`SpaDailyCap = 60`) para evitar el abuso por AFK.

2. **Persistencia de Minas por Turnos (12 Horas):**
   * Se bloquea la descarga automática de pisos vacíos (`MineShaft.clearInactiveMines`).
   * Los pisos visitados de las Minas, Caverna Calavera y Volcán permanecen en memoria y **solo se regeneran dos veces al día real (`06:00` y `18:00`)**, salvo los pisos donde haya un jugador dentro en ese instante (que se limpian en cuanto salga).

3. **Procesado Industrial 1:1 Nativo:**
   * Al ir el reloj 1:1 con el tiempo real, los `MinutesUntilReady` nativos de Stardew Valley ya escalan de forma natural sin tocar cada máquina: un horno tarda **30 minutos reales**, una envasadora **~2,8 días reales** y un barril **~6,9 días reales**.

4. **Multiplicadores Agrícolas y Artesanales Configurables:**
   * Se incluye soporte en `ServerConfig` (`CropPriceMultiplier`, `ArtisanPriceMultiplier`, por defecto `1.0x` hasta calibrar con telemetría real del grupo de 4 jugadores) aplicables por categoría (`Category`).

---

## 5. Vivienda Urbana y Huecos de Granja (`Farm Slots`)

* **MVP de 4 Huecos Estándar Declarativos:**
  * En lugar de instanciar mapas arbitrarios por UID en tiempo de ejecución (que rompen `SaveGame.loadDataToLocations` y dependen del estado global `Game1.whichFarm`), el mod registra **4 ubicaciones fijas** (`Pelican4Ever_Farm_01` a `Pelican4Ever_Farm_04`) en `Data/Locations` con `CreateOnLoad` usando la plantilla `Farm` estándar.
* **Puertas en Pelican Town:**
  * Se habilitan 4 puertas reclamables en Pelican Town (definidas en `assets/doors.json`).
  * Al reclamar una puerta libre, el servidor asigna ese `FarmSlotId` al jugador, vincula su `Cabin` (camuflada como `FarmHouse`) dentro de ese hueco y activa las carteras separadas (`useSeparateWallets = true`).
* **Visitas y Permisos Validados por el Host:**
  * El propietario configura su puerta en modo **`Locked`**, **`Friends`** o **`Public`**.
  * En granjas ajenas, el mod bloquea herramientas destructivas (picos, hachas, azadas sobre cultivos, bombas) y apertura de cofres, permitiendo regar en modo `Friends`/`Public`.

---

## 6. Progreso Compartido vs. Individual

| Ámbito | Regla en *Pelican 4Ever* |
| --- | --- |
| **Economía y Cajas de Envío** | **Individual** (`team.useSeparateWallets = true`). Cada jugador tiene su propio oro y su `personalShippingBin`. |
| **Granja, Casa y Mejoras** | **Individual** por hueco (`Pelican4Ever_Farm_01..04`). |
| **Centro Cívico / Museo / Joja** | **Compartido (Comunitario)** para todo el servidor. |
| **Ascensor de las Minas** | **Individual** o compartido según configuración del grupo (los pisos generados sí son compartidos durante cada turno de 12h). |
| **Relaciones con NPCs** | **Individual** por jugador, sin decaimiento mientras el jugador está en estado `Offline_*`. |

---

## 7. Vida del Pueblo (NPCs) y Festivales (Post-MVP / Iteración 4)

* **Comportamiento Autónomo de NPCs (*Micro-Wandering*):**
  * Los aldeanos reducen un 50% su velocidad en ruta y, al llegar a su destino horario, deambulan dentro de un radio seguro de $\pm 2$ casillas cada 45–90 segundos reales (evitando colisiones y `Warps`).
* **Festivales en Ventana de Tarde/Noche (`18:00 – 23:50`) y Reprogramables:**
  * Comando de administrador (`p4e_festival_move <season> <oldDay> <newDay>`) para mover festivales al fin de semana real mediante `Data/Festivals/FestivalDates`.
  * Adaptación de la ventana horaria del festival al horario de tarde/noche (`1800` a `2350`) evitando secuestrar el tránsito por `Town`.

---

## 8. Riesgos Conocidos y Límites de Alcance

* **Pérdida máxima en caso de crash del host:** Como Stardew Valley solo serializa el guardado completo en `NewDay`, una caída abrupta del contenedor antes de las `06:00 AM` puede perder el progreso del día en curso. Se mitiga con contenedor supervisado (`restart: unless-stopped`), backups automáticos tras cada Rollover y comando de guardado de emergencia (`p4e_rollover_now`) para administradores.
* **Fuera de alcance del MVP:**
  * Anti-cheat criptográfico contra clientes que modifiquen la DLL local (se asume servidor privado con comprobación de versión en `ClientVersionGuardModule`).
  * Reglas específicas de `Game1.whichFarm` por jugador (en el MVP las 4 granjas usan la plantilla Estándar).
