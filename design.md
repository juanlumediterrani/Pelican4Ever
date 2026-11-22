# Pelican 4Ever — Especificación General del Sistema (GSS)

🌐 **Idiomas:** Español | [English](design.en.md) | [中文](design.zh-CN.md)

> **Nombre del producto:** *Pelican 4Ever* (`SV-RTMP`, `UniqueID: Pelican4Ever.SVRTMP`)
> **Documento:** General System Specification (GSS). Qué es el producto, para quién existe, cómo se comportan sus mecánicas en el mundo persistente y qué requisitos funcionales y no funcionales cumple.
> El detalle técnico de implementación vive en [`architecture.md`](architecture.md), las decisiones de arquitectura en [`docs/adr/`](docs/adr/README.md), el desarrollo en [`backlog.md`](backlog.md), las pruebas vivas ejecutables por agentes en [`live-agent-tests-backlog.md`](live-agent-tests-backlog.md) y las pruebas de sesión con jugadores en [`test-users-tasks.md`](test-users-tasks.md).
> **Versión:** 0.5-draft · **Fecha:** 2026-11-16

---

## 1. Propósito, Alcance y Glosario Normativo

**Pelican 4Ever** transforma **Stardew Valley 1.6.15+** (sobre **SMAPI 4.1+**) de un juego cooperativo de sesiones síncronas y jornadas de 14 minutos en un **mundo persistente en tiempo real (escala 1:1)** operado 24/7 para comunidades de **4 a 16 jugadores**.

### 1.1 Concepto Nuclear y Escala

* **Pilar de experiencia:** Un «segundo hogar» virtual. Cada vecino entra entre 30 y 90 minutos al día para cuidar su residencia privada (accesible desde la Plaza Central o la cadena de puentes), gestionar su industria pasiva, gastar su presupuesto de energía y saciedad en pesca, minería o fauna, realizar encargos al pueblo desde su buzón y coincidir en la plaza, en el pueblo o en festivales de tarde/noche.
* **Escala declarativa:** Configurable de **4 a 16 parcelas** mediante `FarmSlotCount` (`4, 8, 12, 16`), con hasta **2 residentes por parcela** (Propietario + 1 Inquilino) y hasta **2 anexos temáticos** (`40×32`) por residencia.
* **Modelo de confianza y autoridad:** Servidor privado/comunitario con cliente modificado obligatorio (`RequireClientMod = true`). El Host es la fuente única de autoridad (`ServerWorldState`) y los clientes validan su identidad de dispositivo mediante criptografía asimétrica (ECDSA P-256).

### 1.2 Fuera de Alcance

* Clientes vanilla sin SMAPI ni modpack requerido (`RequiredClientMods`).
* Anti-cheat criptográfico contra modificación arbitraria de la DLL local del cliente más allá de la validación de modpack, firma de dispositivo e invariantes autoritativos en el Host.
* Almacenamiento de secretos del servidor o tokens de Discord/API en la configuración de los clientes.
* Restauración en caliente de partidas sin detener el proceso del juego (siempre se ejecuta fuera de línea vía `Putre` / `saves.restore`, [ADR-0010](docs/adr/0010-fail-closed-health-and-backup-contract.md)).
* Soporte oficial en producción para `FarmSlotCount > 8` hasta completar y validar el banco de carga (`LIVE-2503` / `PUTRE-401`).

### 1.3 Definiciones y Glosario

| Término | Definición |
| :--- | :--- |
| **`NightHold`** | Estado del reloj real entre las `01:50` y las `06:00` en el que `Game1.timeOfDay` permanece congelado en `2550`, evitando el desmayo forzoso de las `2600` mientras las máquinas siguen procesando. |
| **`CalendarEpoch`** | Fecha real (lunes) tomada como Día 1 de Primavera del Año 1 para calcular de forma determinista el año, estación y día del servidor y alinear las estaciones de 28 días reales con semanas calendario reales. |
| **Rollover Nativo** | Transición diaria automática a las `02:00 AM` reales mediante `Game1.NewDay` + `SaveGame.Save`, precedida por aviso (`01:50`), respaldo verificado con SHA-256 y cierre ordenado de pares. |
| **Bed-Logout** | Mecánica asíncrona por la que un jugador descansa en su cama (*«Descansar y Desconectar»*) marcando `RestedOnGameDay == hoy` para evitar la penalización única del Rollover sin pausar ni cerrar el servidor para los demás. |
| **Checkpoint Intradía** | Guardado periódico en caliente del mundo y los *farmhands* conectados (`CheckpointIntervalMinutes`) para acotar la pérdida ante una caída entre las `06:00` y las `02:00`, rotado por `CheckpointRetention`. |
| **Plaza Central y Mapas Puente** | Topología residencial declarativa ([ADR-0005](docs/adr/0005-host-plaza-and-bridge-maps.md)): 4 portales en la Granja Host (`Farm`) encadenados al oeste con hasta 3 mapas puente (`Pelican4Ever_Bridge_1..3` de `35×30`), cada uno con 4 portales y buzones individuales. |
| **Anexo de Residencia** | Expansión privada adquirible de `40×32` casillas y entrada única generada con tilesets vanilla ([ADR-0009](docs/adr/0009-annex-vanilla-composition.md)), hasta `MaxAnnexesPerResidence = 2` por parcela. |
| **Co-Residencia (`Owner` / `Tenant`)** | Convivencia de un Propietario (`OwnerId`) y hasta 1 Inquilino (`TenantId`) en la misma residencia compartiendo edificios y animales mientras mantienen cartera, correo, paquetes, XP y minas individuales. |
| **Pedido Diferido por Buzón** | Encargo 24/7 a tiendas del valle desde cualquier buzón de residencia/portal con cobro inmediato y materialización física (paquete exclusivo del comprador, andamio o animal) a las `12:00` reales del día objetivo. |
| **Modo `Lockdown` (*Fail-Closed*)** | Estado propuesto ([ADR-0010](docs/adr/0010-fail-closed-health-and-backup-contract.md)) en el que `ModEntry` bloquea la carga de la partida y las conexiones entrantes si falla algún parche `Security`/`Integrity` o si la versión del juego no está verificada. |

---

## 2. Principios de Diseño

1. **Reloj 1:1 sin desmayo punitivo en madrugada:** Un día real equivale a un día de juego; los jugadores noctámbulos pueden jugar en `NightHold` (`2550`) sin perder su jornada por reloj ni bloquear el cambio de fecha de las `02:00 AM`.
2. **Autoridad estricta en el Host con cliente verificado:** Todo estado persistente compartido o sensible vive en `ServerWorldState` en el Host. Los clientes aportan interfaz local y su identidad criptográfica de dispositivo.
3. **Progreso individual dentro de un mundo comunitario:** El Centro Cívico, el Museo, Joja y los pisos mineros son comunitarios. El dinero, `Shipping Bin`, la saciedad, las habilidades, el correo, los paquetes, las relaciones con NPCs y el progreso del Insectario son individuales (incluso entre co-residentes).
4. **Ritmo de sesiones de 30–90 minutos:** La combinación de energía regenerable offline, saciedad con vaciado continuo de 4 horas, *«Bien comido»* sentado y freno selectivo de actividades no acotadas por calendario ([ADR-0008](docs/adr/0008-skill-economy-rebalance.md)) recompensa sesiones frecuentes frente al farmeo infinito.
5. **Operación 24/7 *fail-closed* y observable por agentes:** El servidor no debe degradarse en silencio ante roturas de parches o bloqueos del hilo de juego; toda anomalía debe detener la exposición insegura o exponerse fuera del hilo principal para que `Putre` alerte o reinicie automáticamente.

---

## 3. Perfiles de Usuario

| Perfil | Necesidad Principal | Fricción en Vanilla / Operación Sin Mod | Respuesta de *Pelican 4Ever* |
| :--- | :--- | :--- | :--- |
| **Vecino de Pelican 4Ever** | Jugar 30–90 min/día en su granja privada y coincidir en el pueblo sin depender de cuándo abra el anfitrión. | El host debe estar presente; el reloj de 14 min obliga a dormir todos a la vez. | Mundo 1:1 continuo 24/7, portal y buzón propios, descanso individual y mercado/pedidos asíncronos. |
| **Co-Residente / Inquilino (`Tenant`)** | Compartir granja, edificios y animales con un amigo sin perder su cartera, progreso ni personaje propio. | Vanilla ata a cada cliente a una `Cabin` fija y mezcla o bloquea granjas ajenas. | Solicitud/gestión desde el buzón exterior, edificios y `AnimalPage` compartidos, progreso personal aislado. |
| **Jugador Noctámbulo** | Construir, comprar o pescar pasada la medianoche sin tiendas cerradas ni desmayo a las `02:00`. | A las `02:00` cae el desmayo forzoso y las tiendas cierran por la tarde. | Tramo `NightHold` en `2550` y pedidos 24/7 por buzón con entrega a las `12:00`. |
| **Candidato en Admisión (`#pelican-gate`)** | Entrar a un servidor privado de forma segura aunque cambie su IP doméstica. | Emparejamiento por IP frágil o listas manuales lentas. | Identidad ECDSA P-256 (`P4E\x02`), ticket temporal con huella corta y observación autónoma de 2 cupos con **Putre**. |
| **Anfitrión Agente / Guardián (`Dr4co` / `Putre`)** | Operar el servidor 24/7, anunciar eventos, vigilar salud y evitar pérdidas de datos sin shell libre. | El proceso colgado no termina; un parche roto abre acceso; sin formato de restore. | API `/api/v1/host/*` (incluyendo `/health` propuesto sin bloqueo de hilo), `AgentHost`, watchdog systemd y contrato `p4e-backup.json`. |
| **Administrador / Dueño** | Calibrar economía, aprobar cambios críticos y restaurar copias verificadas con confianza. | Sin telemetría diaria ni rotación/respaldo externo garantizado. | Telemetría `DailyEconomy`, rotación + sync S3 vía Putre y aprobaciones de nivel 2 de un solo uso. |

---

## 4. Especificación de Subsistemas del Mundo

### 4.1. Sistema Temporal, Calendario y Rollover ([ADR-0001](docs/adr/0001-native-daily-rollover.md), [ADR-0002](docs/adr/0002-realtime-clock-and-epoch-calendar.md))

* **Reloj 1:1 (`06:00`–`01:50` → `0600`–`2550`):** Cada 10 minutos reales el reloj avanza 10 minutos de juego (`600.000 ms` por tramo), con interpolación continua de luz ambiental y bloqueo de pausas individuales en el servidor.
* **Madrugada (`NightHold`, `01:50`–`06:00`):** El reloj queda congelado en `2550` (sin llegar nunca a `2600`), permitiendo actividad nocturna sin desmayo por horario mientras hornos y máquinas procesan los minutos restantes al llegar el cambio de día.
* **Calendario perpetuo (`CalendarEpoch`):** Meses de 28 días reales (112 días por año); el Día 1 de cada estación es siempre lunes real y el Día 7 domingo real. Al arrancar tras una caída, `CatchUpPlan` ejecuta hasta `MaxCatchUpRollovers` (`3` por defecto) o salta a la fecha real.
* **Rollover nativo (`02:00 AM`):** Avisa con `RolloverWarningMinutes` (`10` min antes), genera copia verificada con SHA-256, cierra pares de forma ordenada y ejecuta `Game1.NewDay` + `SaveGame.Save`. Si falla la copia o el cierre ordenado, el día no avanza. El amanecer y las rutinas diurnas arrancan a las `06:00 AM`.

### 4.2. Sueño Asíncrono (*Bed-Logout*) y Checkpoints Intradía

* **Estados derivados (`SleepPolicy`):** El estado de cada jugador (`Awake`, `Rested`, `OfflineSafe`, `OfflinePenalized`) se deriva de `LastActiveGameDay`, `RestedOnGameDay` y `LastOutcome` en `ServerWorldState.Players[UID]`.
  * Quien usa su cama (*«Descansar y Desconectar»*) marca `Rested`, despierta al 100% de energía y pasa a `OfflineSafe` sin penalización.
  * Quien queda `Awake` al llegar el rollover sufre una penalización única (`PenaltyGoldPercent = 0.1` con tope `PenaltyGoldCap = 1000` y `PenaltyEnergyFraction = 0.5`) y pasa a `OfflinePenalized` (los días sucesivos sin conectar no vuelven a penalizar).
  * La amistad con NPCs queda congelada mientras el jugador está en `OfflineSafe` u `OfflinePenalized`.
* **Checkpoints intradía (Épica 11):** Guardado periódico configurable (`CheckpointIntervalMinutes`, `CheckpointCloneFarmhands = true`, compresión gzip opcional) que acota la pérdida máxima ante caída entre las `06:00` y las `02:00`, respetando ventanas seguras (no corre durante rollover o eventos/festivales) y con retención `CheckpointRetention` (`12` por defecto).

### 4.3. Energía, Economía 1:1, Minas y Progreso ([ADR-0006](docs/adr/0006-session-energy-satiety-wellfed-spa.md), [ADR-0008](docs/adr/0008-skill-economy-rebalance.md))

* **Energía por sesiones + Saciedad:** Barra base de `270` (+ *Stardrops*); se regenera completa tras `OfflineRegenFullBarHours = 4` horas desconectado. Comer restaura energía instantánea hasta una capacidad de saciedad del 50% de la energía máxima (`DailySatietyFraction = 0.5`), que se vacía continuamente en `SatietyDrainHours = 4` horas reales.
* **«Bien comido» y Spa:** Hasta `MaxDailyWellFedBuffs = 2` veces al día, alcanzar `WellFedSatietyThresholdFraction = 0.25` activa «Bien comido» durante `60 min`, regenerando `1` de energía cada `5 s` al sentarse en sillas o en el suelo (`GroundSitKey`). Permanecer `30 s` quieto en el Spa activa una vez al día `120` de energía y `120` de salud repartidos en `120 min`.
* **Rebalanceo 1:1 ([ADR-0008](docs/adr/0008-skill-economy-rebalance.md), *propuesto pendiente de `TASK-2107`*):** Cupo diario de XP íntegra (`SkillDailyFullXp = 300` para Pesca, Recolección, Minería y Combate; excedente a `SkillOverflowXpMultiplier = 0.25`), picada de pesca `2.5×` más lenta y minijuego `1.5×` más largo, menor aparición de escaleras en minas (`MineStoneLadderChanceMultiplier = 0.5`, `MineMonsterLadderChance = 0.08`) y coste de energía por piso en Caverna Calavera (`SkullCavernDescentBaseCost = 1.0`, `SkullCavernDescentDepthDivisor = 25.0`). Los cultivos, máquinas 1:1 (horno ~30 min, envasadora ~2,8 días, barril ~6,9 días) y multiplicadores configurables (`CropPriceMultiplier`, `ArtisanPriceMultiplier`, `FishPriceMultiplier`, `MineralPriceMultiplier`, todos `1.0x` por defecto) completan el equilibrio.
* **Minas persistentes (12 h):** Los pisos ocupados no se descargan; los vacíos caducan tras `MineFloorCooldownMinutes = 45` y todos los desocupados se reinician a las `06:00` y `18:00` reales (`MineResetTimes`).
* **Progreso compartido vs. individual:**

| Ámbito | Regla en *Pelican 4Ever* |
| :--- | :--- |
| **Economía y Cajas de Envío** | **Individual** (`team.useSeparateWallets = true`, `personalShippingBin`), incluso entre co-residentes. |
| **Parcela, Casa, Edificios y Animales** | **Individual por parcela** (`Pelican4Ever_Farm_01..16`), compartida únicamente entre `OwnerId` y `TenantId` de esa parcela. |
| **Centro Cívico / Museo / Joja** | **Compartido (comunitario)** para todo el servidor. |
| **Insectario (NITV y Wild Animals)** | **Individual** en capturas, donaciones, colección visible, desbloqueos y recompensas. Cada personaje puede donar la misma especie y reclamar una vez cada recompensa que desbloquee con sus propias donaciones. |
| **Ascensor de las Minas, Correo, Paquetes y XP** | **Individual** por jugador. Los pisos de mina son compartidos; los cofres de recompensa de mina son personales. |
| **Relaciones con NPCs** | **Individual** por jugador, sin decaimiento en estado `Offline_*`. |

El anfitrión y cada cliente deben ejecutar la versión de Pelican 4Ever que incluye este cambio. En partidas existentes, el historial compartido de donaciones de NITV se conserva en el mapa como registro antiguo. Como ese historial carece de autoría, cada personaje empieza su registro personal donando sus propios ejemplares. Las recompensas ya cobradas siguen registradas en el personaje. Los cobros antiguos se identificaban por objeto, por lo que se consideran cobrados los premios que coincidan con ese objeto.

### 4.4. Plaza Central, Residencias, Anexos, Co-Residencia y Buzón

* **Topología declarativa ([ADR-0003](docs/adr/0003-declarative-farm-slots.md), [ADR-0005](docs/adr/0005-host-plaza-and-bridge-maps.md)):** Hasta 16 residencias (`Pelican4Ever_Farm_01..16`) con `AlwaysActive = true` y 8 tipos oficiales de granja, conectadas mediante la Plaza Central (`Farm`, 4 portales) y la cadena de puentes `Pelican4Ever_Bridge_1..3` (`35×30`, 4 portales cada uno), con enrutamiento espacial coherente (`SpatialWarpRouter`) y *culling* dinámico para clientes en las ubicaciones desocupadas.
* **Anexos 40×32 ([ADR-0009](docs/adr/0009-annex-vanilla-composition.md)):** Cada residencia puede adquirir hasta `MaxAnnexesPerResidence = 2` anexos temáticos (`FirstAnnexPriceMultiplier = 0.4`, `SecondAnnexPriceMultiplier = 1.0`) con cueva individual (`PrivateFarmCaves`).
* **Visitas y permisos (`VisitorGuardModule`):** El propietario fija `Locked`, `Friends` o `Public` y `VisitorsCanEdit`. Los visitantes sin permiso de edición tienen bloqueadas herramientas destructivas, apertura de cofres/paquetes y colocación/retirada de objetos o terrarios, permitiéndose regar (según política), pescar, usar honda o capturar fauna NITV con redes.
* **Co-Residencia (Épica 23):** Cada parcela admite hasta 1 inquilino (`TenantId`) solicitado y gestionado desde el buzón exterior. El inquilino comparte casa (`_Home` con 2 camas y despertar diferenciado), mejoras, bodega, cueva, invernadero, anexos y gestión/posesión de edificios y animales (`Robin`, `Mago`, `Marnie`, `AnimalPage`), con su propia mascota posible, evacuación segura al expulsar y traspaso automático de propiedad si el propietario libera la parcela.
* **Pedidos por buzón a las `12:00` (Épica 22):** Desde buzones de puerta o residencia se puede encargar 24/7 a tiendas del valle; el pago se cobra en el acto y la entrega se materializa a las `12:00` reales del día objetivo (nunca durante `NightHold`) en forma de paquetes exclusivos del comprador, andamios o animales.

### 4.5. Vida Urbana, Mascotas, Chat e Intersección con Mods

* **NPCs y Festivales:** *Micro-wandering* ($\pm 2$ casillas cada 45–90 s a mitad de velocidad) y festivales desacoplados en ventana `18:00–23:50` (`FestivalWindowStart = 1800`, `FestivalWindowEnd = 2350`), con reprogramación (`p4e_festival_move`), entrada/salida libre, tandas por inscripción y ceremonias automáticas.
* **Mascotas de clientes (Épica 20):** Comprar un `Pet Bowl` en Robin dispara la visita de adopción de Marnie al despertar en la residencia del cliente y vincula la mascota de forma autoritativa.
* **Integración con `PelicanChat` y `Nature In The Valley` (Épica 24):** Canales `/l`, `/g`, `/w` y puente Discord vía `HostEventLog` + `/api/v1/host/chat`; exposición de códigos de bioma NITV (`"0"`/`"Outside"`, `"1"`/`"Forest"`, `"2"`/`"Beach"`, `"3"`/`"Underground"`) en `Pelican4Ever.NITVCompat`, exclusión del buff transitorio `"NatCSpeedN"` del escalado temporal `85.7x` y permiso de redes NITV para visitantes.
* **Cosméticos y Anfitrión Agente (`AgentHost`):** Descanso automático del host sin cerrar red y pack visual *Fashion Sense* para `Dr4co` y `Putre`.

### 4.6. Seguridad, Identidad y Relación con `Putre`

* **Identidad ECDSA P-256 ([ADR-0007](docs/adr/0007-dispositivo-identidad-y-acceso-seguro.md)):** Saludo `P4E\x02` en Lidgren independiente de la IP, emparejamiento por código temporal (`PairingInviteTtlHours = 72`) y comprobación de `RequiredClientMods`.
* **Protocolo de admisión de 2 cupos (`ProbationTelemetryModule`):** **Putre** abre tickets temporales en `#pelican-gate` (`MaxConcurrentPutreSlots = 2`), evalúa telemetría conductual escalonada y puede aplicar lista negra irrevocable por Putre (solo revocable por el dueño).
* **API de Anfitrión y Contrato de Resiliencia ([ADR-0010](docs/adr/0010-fail-closed-health-and-backup-contract.md)):** Endpoints loopback (`/api/v1/host/*`) despachados en el hilo principal, complementados en Épica 25 con `/api/v1/host/health` sin bloqueo de hilo, arranque *fail-closed*, watchdog `sd_notify` y manifiesto `p4e-backup.json` para sincronización S3 y `saves.restore` por Putre.

---

## 5. Requisitos Funcionales (`RF`)

### 5.1 Motor Temporal, Calendario y Rollover (`RF-01`..`RF-08`)

| ID | Requisito | Prioridad |
| :--- | :--- | :--- |
| `RF-01` | Avanzar el reloj del juego en escala real 1:1 (`600.000 ms` por cada 10 minutos de juego entre `06:00` y `01:50`) sin pausas por menús ni por servidor vacío. | Alta |
| `RF-02` | Interpolar suavemente la iluminación ambiental en cada tramo de 10 minutos reales (`MillisecondsIntoSlot`). | Media |
| `RF-03` | Congelar el reloj en `2550` (`NightHold`) entre las `01:50` y las `06:00` reales para evitar desmayos por horario permitiendo el procesado nocturno de máquinas. | Alta |
| `RF-04` | Calcular la fecha del juego (`28` días reales por estación) de forma determinista a partir de `CalendarEpoch`, haciendo que el Día 1 sea siempre lunes y el Día 7 domingo. | Alta |
| `RF-05` | Planificar la recuperación al arrancar tras caídas nocturnas mediante `CatchUpPlan` (hasta `MaxCatchUpRollovers` o salto directo). | Alta |
| `RF-06` | Emitir avisos previos en HUD (`RolloverWarningMinutes`, por defecto `10` min antes a las `01:50`) antes del cambio de día. | Alta |
| `RF-07` | Ejecutar el rollover diario a las `02:00 AM` con el flujo nativo `NewDay` + `SaveGame.Save`, copia verificada SHA-256 y cierre ordenado de pares; no avanzar el día si la copia falla. | Alta |
| `RF-08` | Iniciar el amanecer y los horarios diurnos de los NPCs a las `06:00 AM` reales (`DayStartTime`) sin adelantar rutinas tras el rollover de madrugada. | Alta |

### 5.2 Sueño Asíncrono y Checkpoints Intradía (`RF-09`..`RF-13`)

| ID | Requisito | Prioridad |
| :--- | :--- | :--- |
| `RF-09` | Permitir que cada jugador descanse en su cama (*«Descansar y Desconectar»*) marcando `RestedOnGameDay == hoy` sin pausar el mundo ni expulsar a terceros. | Alta |
| `RF-10` | Derivar `SleepState` (`Awake`, `Rested`, `OfflineSafe`, `OfflinePenalized`) exclusivamente desde `ServerWorldState.Players[UID]` en el Host. | Alta |
| `RF-11` | Aplicar la penalización de `Awake` (`PenaltyGoldPercent = 0.1` con tope `1.000g`, `PenaltyEnergyFraction = 0.5`) una sola vez y no volver a penalizar en días sucesivos offline (`OfflinePenalized`). | Alta |
| `RF-12` | Bloquear la caída diaria de amistad con NPCs mientras un jugador permanece en `OfflineSafe` u `OfflinePenalized`. | Media |
| `RF-13` | Ejecutar checkpoints intradía periódicos (`CheckpointIntervalMinutes`) clonando *farmhands* conectados (`CheckpointCloneFarmhands`), inhibidos en rollovers o festivales y rotados por `CheckpointRetention`. | Alta |

### 5.3 Economía, Saciedad y Progreso 1:1 (`RF-14`..`RF-20`)

| ID | Requisito | Prioridad |
| :--- | :--- | :--- |
| `RF-14` | Regenerar la barra base de energía (`270` + *Stardrops*) offline al completo en `OfflineRegenFullBarHours = 4` horas. | Alta |
| `RF-15` | Limitar la recuperación instantánea de energía al comer mediante una capacidad de Saciedad (`DailySatietyFraction = 0.5`) con vaciado continuo de `SatietyDrainHours = 4` horas reales sin bloquear curación de salud ni buffs. | Alta |
| `RF-16` | Activar «Bien comido» hasta `MaxDailyWellFedBuffs = 2` veces/día por encima de `WellFedSatietyThresholdFraction = 0.25`, recuperando `1` de energía cada `5 s` al sentarse en muebles o suelo (`GroundSitKey`). | Alta |
| `RF-17` | Activar una vez al día el buff del Spa tras permanecer `30 s` quieto en el agua (`120` de Energía y `120` de Salud durante `120 min`). | Media |
| `RF-18` | Aplicar el rebalanceo 1:1 configurable (propuesto ADR-0008): cupo diario `SkillDailyFullXp`, multiplicadores de picada/minijuego de pesca, escaleras en minas, coste en Caverna Calavera, multiplicadores de venta y telemetría diaria (`DailyEconomyTelemetry`). | Media |
| `RF-19` | Conservar vivos los pisos mineros ocupados y reiniciar los desocupados en turnos de 12 horas (`06:00` y `18:00`) o tras `MineFloorCooldownMinutes = 45`. | Alta |
| `RF-20` | Mantener carteras separadas (`useSeparateWallets = true`) y `personalShippingBin` individuales mientras Centro Cívico, Museo, Joja, pisos de mina e Insectario permanecen comunitarios. | Alta |

### 5.4 Residencias, Puentes, Anexos, Inquilinos y Buzón (`RF-21`..`RF-30`)

| ID | Requisito | Prioridad |
| :--- | :--- | :--- |
| `RF-21` | Registrar declarativamente de 4 a 16 residencias (`FarmSlotCount`) en `Data/Locations` repartidas entre la Plaza Central (`Farm`, 4 portales) y puentes `Pelican4Ever_Bridge_1..3` (`35×30`, 4 portales c/u). | Alta |
| `RF-22` | Permitir elegir entre los 8 tipos oficiales de granja al reclamar parcela, conservando orientación espacial coherente (`SpatialWarpRouter`) según el borde `North`/`South`. | Alta |
| `RF-23` | Aplicar *culling* dinámico de red en clientes para ocultar y evitar tráfico de residencias y anexos vacíos. | Alta |
| `RF-24` | Permitir adquirir hasta `MaxAnnexesPerResidence = 2` anexos `40×32` por parcela (`FirstAnnexPriceMultiplier = 0.4`, `SecondAnnexPriceMultiplier = 1.0`) y elegir cueva individual (`PrivateFarmCaves`). | Media |
| `RF-25` | Hacer cumplir políticas de acceso residencial (`Locked`, `Friends`, `Public`, `VisitorsCanEdit`) en Host y cliente bloqueando herramientas destructivas y cofres a visitantes. | Alta |
| `RF-26` | Permitir co-residencia de 1 Propietario y hasta 1 Inquilino mediante solicitudes, invitaciones, aceptación, abandono y expulsión desde el buzón exterior del portal. | Alta |
| `RF-27` | Desacoplar la `Cabin` técnica de login del hogar compartido (`Pelican4Ever_Farm_XX_Home` con 2 camas y despertar diferenciado) para evitar colisiones entre co-residentes. | Alta |
| `RF-28` | Compartir entre Propietario e Inquilino construcción (`Robin`, `Mago`), animales (`Marnie`, `AnimalPage`), bodega y anexos manteniendo individuales carteras, correo, paquetes, XP y minas. | Alta |
| `RF-29` | Evacuar al ex-inquilino si la puerta es `Locked`, redirigir sus paquetes pendientes al finalizar el inquilinato y transferir la titularidad (`OwnerId = TenantId`) si el propietario libera la parcela. | Alta |
| `RF-30` | Habilitar pedidos 24/7 a tiendas desde buzones con cobro inmediato y entrega física a las `12:00` reales (bloqueada en `NightHold`) en paquetes exclusivos del comprador. | Alta |

### 5.5 Vida Urbana, Festivales, Mascotas y Mods (`RF-31`..`RF-36`)

| ID | Requisito | Prioridad |
| :--- | :--- | :--- |
| `RF-31` | Simular *micro-wandering* autónomo de aldeanos ($\pm 2$ casillas cada 45–90 s a mitad de velocidad) evitando colisiones y `Warps`. | Media |
| `RF-32` | Ejecutar festivales desacoplados en ventana de tarde/noche (`1800–2350`), reprogramables por fecha (`p4e_festival_move`), con entrada/salida libre y sin pausar el reloj. | Media |
| `RF-33` | Gestionar tandas por inscripción en festivales competitivos, ceremonias automáticas (`20:45` aviso / `21:00` inicio) y premios diferidos (`PendingFestivalPrizes`). | Media |
| `RF-34` | Desbloquear el evento de adopción de mascota propia para clientes al construir un `Pet Bowl`, vinculándola al cuenco y al jugador. | Media |
| `RF-35` | Garantizar compatibilidad con *Nature In The Valley*: biomas `"0"`..`"3"` en `Pelican4Ever.NITVCompat`, exclusión de `"NatCSpeedN"` del escalado `85.7x` y permiso de redes NITV en visitas sin alterar terrarios. | Media |
| `RF-36` | Soportar chat multicanal (`/l`, `/g`, `/w`) acoplable con `PelicanChat`, mercado vecinal con depósito en garantía, modo `AgentHost` y cosméticos `Dr4co`/`Putre`. | Media |

### 5.6 Seguridad, Identidad y API de Anfitrión (`RF-37`..`RF-42`)

| ID | Requisito | Prioridad |
| :--- | :--- | :--- |
| `RF-37` | Autenticar clientes mediante clave asimétrica ECDSA P-256 (`P4E\x02` en Lidgren) independiente de la IP y emparejamiento temporal (`PairingInviteTtlHours = 72`). | Alta |
| `RF-38` | Validar en handshake la versión del mod y la lista exacta de mods requeridos (`RequiredClientMods` en `ClientVersionGuardModule`). | Alta |
| `RF-39` | Operar el protocolo de admisión de 2 cupos (`MaxConcurrentPutreSlots = 2`), telemetría conductual (`ProbationTelemetryModule`) y lista negra irrevocable por Putre. | Alta |
| `RF-40` | Exponer la API REST de anfitrión (`/api/v1/host/*` y `HostEventLog`), incluyendo comandos whitelisted y reinicio controlado (`p4e_safe_restart`). | Alta |
| `RF-41` | (*Propuesto ADR-0010 / Épica 25*) Declarar cada parche Harmony en `PatchRegistry` (`Security`, `Integrity`, `Experience`) y entrar en `Lockdown` *fail-closed* si falla alguno crítico o si la versión del juego no está en `VerifiedGameVersions`. | Alta |
| `RF-42` | (*Propuesto ADR-0010 / Épica 25*) Exponer `GET /api/v1/host/health` fuera del hilo del juego, emitir pulsos `WATCHDOG=1` vía `sd_notify` y generar manifiestos `p4e-backup.json` en cada copia. | Alta |

---

## 6. Requisitos No Funcionales y Resiliencia Operativa (`RNF`)

| ID | Requisito | Métrica / Criterio |
| :--- | :--- | :--- |
| `RNF-01` | **Separación de Dominio Pura** | Toda la lógica pura de tiempo, calendario, sueño, economía, topología, seguridad y políticas reside en `Pelican4Ever.Core` (`net6.0`) con **0** referencias a `StardewValley`, `SMAPI`, `MonoGame` ni `Harmony`. |
| `RNF-02` | **Cobertura de Tests Unitarios** | La lógica pura de `Pelican4Ever.Core` se verifica con pruebas `xUnit` en `Pelican4Ever.Tests` (`net8.0`) ejecutables con `dotnet test` (1.684 pruebas en verde). |
| `RNF-03` | **Calidad de Compilación** | `TreatWarningsAsErrors = true` y `Nullable = enable` activos en `Pelican4Ever.Core` y `Pelican4Ever` (`0` advertencias toleradas). |
| `RNF-04` | **Internacionalización (`i18n`)** | Soporte completo en inglés (`i18n/default.json`), español (`i18n/es.json`) y chino simplificado (`i18n/zh-CN.json`), con proveedor fallback y sin claves crudas visibles. |
| `RNF-05` | **Arquitectura Modular Aislada** | Todos los módulos SMAPI implementan `IModule` y se registran limpiamente según lado (`Host` / `Client`) y configuración de activación (`ModToggleModule`). |
| `RNF-06` | **Superficie de Red y Cero Secretos en Clientes** | El único puerto público es `24642/udp`; la API REST (`AdminApiPort = 8085` por defecto, loopback) no se expone al exterior salvo configuración explícita, y los clientes nunca almacenan tokens de API ni de Discord. |
| `RNF-07` | **Integridad de Rollover y Checkpoints** | Toda copia diaria y checkpoint verifica SHA-256 por fichero; `RolloverDayGuard` previene doble ejecución de penalizaciones y ningún día avanza si falla la copia o el cierre ordenado. |
| `RNF-08` | **Operación Headless y Presupuesto de Cómputo** | El host dedicado soporta render suprimido (`HeadlessRenderModule` sobre `Xvfb`), operando en la VM de producción objetivo de `2 vCPU`, `LP_NUM_THREADS=1`, `Nice=5` y `MemoryMax=4G`. |
| `RNF-09` | **Arranque *Fail-Closed* (Propuesto ADR-0010)** | Cualquier fallo en parches `Security`/`Integrity` o versión fuera de `VerifiedGameVersions` (`["1.6.15"]`) y `MinimumSmapiVersion` (`4.1.0`) impide cargar `AutoHostSaveName` y bloquea conexiones sin depender de parches Harmony, salvo excepción nominal en `LockdownOverride`. |
| `RNF-10` | **Salud Observable Sin Bloqueo de Hilo (Propuesto ADR-0010)** | `GET /api/v1/host/health` responde desde el hilo HTTP en `< 50 ms` sin pasar por `MainThreadActionQueue`, exponiendo `Status`, `SecondsSinceLastTick`, `SaveLoaded`, `Lockdown.Reasons[]` y `Patches.Failed[]`. |
| `RNF-11` | **Detección de Cuelgues por Watchdog (Propuesto ADR-0010)** | Si `NOTIFY_SOCKET` existe, envía `WATCHDOG=1` cada `15 s` solo mientras `SecondsSinceLastTick <= HangThresholdSeconds` (`120 s`); `systemd` configura `WatchdogSec=180` y `NotifyAccess=all`. |
| `RNF-12` | **Vigilancia por Monitor Negativo en Putre (Propuesto ADR-0010)** | `Putre` sondea `/api/v1/host/health` cada `60 s`, silencia estados nominales `Ok` y emite `owner.alert` únicamente en transiciones a `Lockdown`, `Degraded`, cuelgue, caída o fallo de backup y su recuperación. |
| `RNF-13` | **Contrato de Copias y Retención S3 (Propuesto ADR-0010)** | Cada copia incluye `p4e-backup.json` (`FormatVersion = 1`) con rotación local por defecto `BackupRetentionDays = 14` y `CheckpointRetention = 12`; Putre ejecuta `aws s3 sync` sin `--delete` tras cada `backup.created` y la caducidad se gestiona en IaC. |
| `RNF-14` | **Restauración Verificada Offline (Propuesto ADR-0010)** | `saves.restore` (nivel 2 en Putre) detiene el juego, valida `p4e-backup.json` y SHA-256, mueve la partida previa a `Pelican4Ever_Backups/pre-restore/<UTC>/` sin borrar datos, copia la elegida y exige `health.Status == "Ok"`, `SaveLoaded == true` y `TotalGameDays` coherente. |
| `RNF-15` | **Validación Empírica de Escala (Propuesto ADR-0010)** | `FarmSlotCount > 8` no se declara soportado en producción hasta medir tamaño de guardado, duración de `SaveGame.Save()`, `LastCheckpointLongestTickMs`, ancho de banda y densidad NITV con 8, 12 y 16 *farmhands* sintéticos (`LIVE-2503` / `PUTRE-401`). |
