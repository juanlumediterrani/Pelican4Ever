## 1. Visión General del Proyecto

* **Nombre:** *Pelican 4Ever* (`SV-RTMP`)
* **Concepto nuclear:** Transformar Stardew Valley de un juego cooperativo de sesiones síncronas y turnos rápidos a un **mundo persistente en tiempo real (escala 1:1)** alojado en un servidor dedicado 24/7.
* **Escala y Modelo de Confianza:** Diseñado para comunidades de **4 a 16 jugadores** (escalable mediante configuración declarativa `FarmSlotCount = 4..16`). Funciona bajo un modelo de **servidor entre amigos con cliente modificado obligatorio** (`RequireClientMod = true`): tanto el servidor como los clientes instalan `Pelican 4Ever`.
* **Pilar de experiencia:** Un "segundo hogar" virtual. El jugador entra entre 30 y 90 minutos al día para cuidar su granja privada (conectada a su portal en la Plaza Central o en la cadena de puentes), gestionar su industria pasiva, gastar su presupuesto diario de energía + saciedad en las minas o la pesca y coincidir en la plaza, en el pueblo o en festivales de tarde/noche con el resto de la comunidad.

---

## 2. Sistema Temporal y Calendario

### 2.1. Reloj en Tiempo Real (1:1) y "Madrugada" (`NightHold`)

El motor de Stardew Valley codifica el reloj en formato entero (`0600` a `2600`) y fuerza el desmayo en cuanto alcanza `2600` (`Game1.Update` / `performTenMinuteClockUpdate`). Para evitar desmayos forzosos sin reescribir las constantes internas del motor:

* **Inicio del día (`06:00` real = `0600` juego):** El día de juego comienza a las **06:00 AM** de la zona horaria del servidor (ej. `Europe/Madrid`).
* **Avance 1:1 (`06:00` a `01:50` real → `0600` a `2550` juego):** Cada 10 minutos reales el reloj avanza 10 minutos de juego (`600` → `2400` a medianoche → `2550` a las `01:50 AM`).
* **Tramo de Madrugada (`01:50` a `06:00` real → congelado en `2550`):** A partir de las `01:50 AM` reales, el reloj entra en estado **`NightHold`** (congelado en `2550`, nunca llega a `2600`). Los jugadores noctámbulos pueden seguir pescando, minando o decorando sin sufrir desmayo por horario; al llegar el Rollover de las `02:00 AM` el motor procesa los minutos restantes de madrugada (`2550 → 0600`) sobre hornos y máquinas.
* **Iluminación ambiental continua:** Interpolada suavemente en cada tramo de 10 minutos reales (`MillisecondsIntoSlot` sobre `600.000 ms`).

### 2.2. Calendario Perpetuo Basado en Época (`CalendarEpoch`)

* **Duración de estación:** Se mantiene el mes nativo de **28 días reales** (4 semanas exactas; 112 días reales por año de juego).
* **Sincronización perpetua por Época:** La fecha del juego (`Year`, `Season`, `Day`) se **calcula matemáticamente** a partir de una fecha lunes real configurada (`CalendarEpoch`, p. ej. `2026-11-02` = Día 1 de Primavera, Año 1).
  * El Día 1 de cada estación es siempre **Lunes real** y el Día 7 es siempre **Domingo real**.
  * Si el servidor sufre una caída durante la noche, al arrancar compara `Game1.Date.TotalDays` con la fecha derivada del calendario real y aplica el plan de recuperación (`CatchUpPlan`: ejecuta hasta `MaxCatchUpRollovers` seguidos o salta a la fecha actual).

### 2.3. El "Rollover" Diario Nativo (`02:00 AM`)

Todos los días a las **02:00 AM (hora del servidor)** se ejecuta la transición diaria apoyándose en el **flujo nativo del motor (`Game1.NewDay` + `SaveGame.Save`)**:

1. **Aviso previo:** Se notifica un aviso en el HUD a los jugadores conectados **10 minutos** antes del corte (`01:50`, `RolloverWarningMinutes`).
2. **Transición nativa segura:** Si hay jugadores conectados a las `02:00 AM`, verán la pantalla nativa de fin de día y guardado (10–25 segundos). Forzar el flujo nativo garantiza que se ejecuten sin fisuras todas las rutinas de Stardew Valley 1.6 (crecimiento de cultivos, `Shipping Bins` personales, correo, clima, suerte, animales, amistad, `netWorldState` y serialización XML).
3. **Resolución de descanso y copia de seguridad:** Antes de cerrar los peers se genera una **copia de seguridad con firma SHA-256**; al completarse el cambio de día se evalúa la política de sueño de cada jugador registrado. Si el backup o la desconexión ordenada no se verifican, el día **no** avanza.
4. **Amanecer a las `06:00 AM`:** El reloj del juego y los horarios de los NPC arrancan a las `06:00 AM` reales; el cambio de fecha de madrugada no adelanta las rutinas diurnas.

---

## 3. Mecánica de Sueño Asíncrono ("Bed-Logout")

El estado autoritativo de cada jugador reside únicamente en el servidor (`ServerWorldState.Players[UID]`). Los estados `SleepState` se **derivan** a partir de `LastActiveGameDay`, `RestedOnGameDay` y `LastOutcome`:

| Estado Derivado | Condición | Efecto al cerrar el día en el Rollover (`02:00 AM`) |
| --- | --- | --- |
| **`Awake`** | Conectado hoy (`isOnline` o `LastActiveGameDay == hoy`) y aún no ha usado la cama (`RestedOnGameDay != hoy`). | Si llega el Rollover de las `02:00 AM` sin haber descansado (o sigue conectado fuera de la cama), **sufre penalización única** (`RolloverOutcome.Penalized`) y pasa a `OfflinePenalized`. |
| **`Rested`** | El jugador se mete en su cama, selecciona *"Descansar y Desconectar"* (`RestedOnGameDay == hoy`) y sale al menú. | Sin penalización (`RolloverOutcome.Rested`). Despierta con el **100% de su Energía** y pasa a `OfflineSafe`. |
| **`OfflineSafe`** | No ha entrado en el día en curso y su último resultado fue `Rested` o `Untouched`. | Permanece intacto (`RolloverOutcome.Untouched`) aunque pase días o semanas sin conectarse. |
| **`OfflinePenalized`** | No ha entrado en el día en curso y su último resultado fue `Penalized`. | La penalización (10% del oro personal hasta `1.000g` y **50% de Energía**) se aplica **una sola vez**. En días posteriores sin conectarse devuelve `Untouched` y no vuelve a penalizar. |

* **Amistad congelada en ausencia:** Mientras un jugador está en `OfflineSafe` u `OfflinePenalized`, se bloquea la caída diaria de amistad por no hablar con los NPCs.

---

## 4. Economía y Balanceo (Pasivo vs. Activo)

1. **Energía por Sesiones + Saciedad (`SatietyBudget`, ADR-0006):**
   * **Barra base:** El jugador empieza la jornada con su barra de energía (`270` base + *Stardrops*).
   * **Ritmo de sesiones:** una sesión dura entre 30 min y 1 h 30 min. El punto dulce es conectarse 30–45 min varias veces al día: tras 4 h desconectado, la barra está llena (`OfflineRegenFullBarHours = 4`) y la Saciedad, vacía. Quien quiera seguir jugando se sienta a descansar; el jugador intensivo puede continuar, pero a un ritmo más relajado.
   * **Saciedad (Comida con sentido):** Comer alimentos **sí restaura Energía instantánea**, además de Salud (*Health*) y *Buffs*, hasta llenar una **capacidad de Saciedad** equivalente al **50% de su energía máxima** (`DailySatietyFraction = 0.5`, es decir, `135` en una barra de 270). La Saciedad se **vacía de forma continua en 4 horas reales** (`SatietyDrainHours = 4`), conectado o no, y no se reinicia al cambiar de día ni al reconectar. Con la Saciedad llena se puede seguir comiendo para curar Vida o mantener *Buffs*, pero sin ganar Energía instantánea.
   * **"Bien comido" (cualquier comida):** Hasta **2 veces al día** (`MaxDailyWellFedBuffs = 2`), comer cualquier alimento con la saciedad por encima del umbral (`WellFedSatietyThresholdFraction = 1/4`) activa "Bien comido" durante 60 minutos. Mientras dura, **sentarse** en un banco, silla o la taberna, o **en el suelo** (tecla `GroundSitKey`, por defecto `.`), recupera `1 punto de Energía cada 5 segundos` (`SittingRegenSeconds = 5`, `12/min`). Sin "Bien comido", sentarse no recupera nada.
   * **Spa (una vez al día):** Quedarse **30 segundos quieto** dentro del agua del Spa o de la piscina activa un buff de **recuperación pasiva de `120` de Energía y `120` de Salud en 2 horas** (`1 punto/min` de cada uno), aunque luego se salga del agua. Solo se puede activar una vez al día.

2. **Persistencia de Minas por Turnos (12 Horas):**
   * Se bloquea la descarga automática de pisos vacíos (`MineShaft.clearInactiveMines`).
   * Los pisos visitados de las Minas, Caverna Calavera y Volcán permanecen en memoria y **solo se regeneran dos veces al día real (`06:00` y `18:00`)**, salvo los pisos donde haya un jugador dentro en ese instante (que se limpian en cuanto salga).

3. **Procesado Industrial 1:1 Nativo:**
   * Al ir el reloj 1:1 con el tiempo real, los `MinutesUntilReady` nativos de Stardew Valley ya escalan de forma natural sin tocar cada máquina: un horno tarda **30 minutos reales**, una envasadora **~2,8 días reales** y un barril **~6,9 días reales**.

4. **Multiplicadores Agrícolas y Artesanales Configurables:**
   * Se incluye soporte en `ServerConfig` (`CropPriceMultiplier`, `ArtisanPriceMultiplier`, por defecto `1.0x` hasta calibrar con telemetría real del grupo de 4 jugadores) aplicables por categoría (`Category`).

---

## 5. Plaza Central, Puentes y Residencias Privadas (`Farm Slots`)

* **Escala Flexible de 4 a 16 Residencias Declarativas:**
  * En lugar de instanciar mapas arbitrarios por UID en tiempo de ejecución, el mod registra hasta **16 ubicaciones fijas** (`Pelican4Ever_Farm_01` a `Pelican4Ever_Farm_16`) en `Data/Locations` con `CreateOnLoad` usando las plantillas de granja correspondientes (`FarmTemplateCatalog`).
* **Plaza Central (`Farm`) y Cadena de Puentes (`Pelican4Ever_Bridge_1..3`):**
  * Las residencias no se ubican como puertas en Pelican Town, sino en la **Plaza Central** (el mapa de la Granja Host `Farm`) y en una **cadena determinista de puentes** (`Pelican4Ever_Bridge_1`, `Pelican4Ever_Bridge_2`, `Pelican4Ever_Bridge_3`) que escala dinámicamente según `FarmSlotCount`:
    - Con `FarmSlotCount = 4`: 4 parcelas en la Granja Host (sin puentes ni paso oeste).
    - Con `FarmSlotCount = 8`: 4 parcelas en la Granja Host + 4 en el Puente 1.
    - Con `FarmSlotCount = 12`: 4 parcelas en la Granja Host + 4 por puente en Puentes 1 y 2.
    - Con `FarmSlotCount = 16`: 4 parcelas en la Granja Host + 4 por puente en Puentes 1, 2 y 3.
  * Cada puente es un recorte determinista de `35×30` casillas derivado de `Maps/BusStop`, con calzada central limpia, perímetro sellado y pasos direccionales este y oeste continuos.
* **Portales con Borde Explícito (`PortalEdge`) y Entrada Caminando:**
  * Cada portal residencial se orienta hacia un borde cardinal (`North` o `South` en `doors.json`), cuenta con un camino de 2 casillas de ancho despejado hasta la calzada, pavimento de losas (`stepping_stones`) y un área de despeje interior de `3×4` casillas.
  * Al caminar hacia el portal hasta las dos últimas casillas del borde, se dispara el menú de fundación/reclamación de parcela libre o bien la entrada directa a la residencia privada si está ocupada y permitida.
  * La entrada y salida respetan la orientación espacial inmersiva (`SpatialWarpRouter`): los portales del Norte entran por el Sur de la residencia mirando al Norte, y los del Sur entran por el Norte de la residencia mirando al Sur.
* **Buzones Individuales:**
  * Cada portal cuenta con su propio buzón adyacente (`TownMailboxModule` / `TownMailboxLogic`), permitiendo recibir correo privado de los NPCs o del sistema justo al salir de la residencia.
* **Visitas y Permisos Validados por el Host:**
  * El propietario configura su residencia en modo **`Locked`**, **`Friends`** o **`Public`**.
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

* **Pérdida máxima en caso de crash del host:** Como Stardew Valley solo serializa el guardado completo en `NewDay`, una caída abrupta antes de las `02:00 AM` puede perder el progreso del día en curso. Se mitiga con un supervisor (`systemd` con `Restart=always` o contenedor con `restart: unless-stopped`), backups automáticos con SHA-256 tras cada Rollover, checkpoints intradía (`CheckpointIntervalMinutes`) y el reinicio controlado (`POST /api/v1/host/restart`), que espera al próximo guardado antes de cerrar el proceso.
* **Fuera de alcance del MVP:**
  * Anti-cheat criptográfico contra clientes que modifiquen la DLL local (se asume servidor privado con comprobación de versión en `ClientVersionGuardModule`).
  * Reglas específicas de `Game1.whichFarm` por jugador (en el MVP las 4 granjas usan la plantilla Estándar).
