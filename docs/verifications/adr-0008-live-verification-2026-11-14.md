# Verificación en vivo de ADR-0008 · 14 de noviembre de 2026

## Entorno

Stardew Valley 1.6.15 build 24356 y SMAPI 4.5.2. Host y farmhand ejecutados como procesos independientes en una red privada de loopback. Cada proceso usó un directorio de mods y perfil XDG separados; el host cargó una copia de `Prueba29_453599927`. El arnés temporal en `/tmp/p4e-epic21-lab` actuó desde el hilo de juego para conectar por LAN, conceder XP, desplazar al jugador, tomar lecturas y solicitar checkpoints. Las partidas y mods originales no se modificaron.

Para aislar la economía se desactivaron `ClientSecurity`, `ClientVersionGuard` y `AdminApi` en ambos procesos. Se usaron los mods y contenidos locales restantes. El puente Voyager intentó escuchar en el mismo puerto en las dos instancias y solo una pudo abrirlo; las acciones registradas aquí proceden del arnés temporal, no del puente. Los dos procesos se cerraron al terminar.

## Resultados

| Escenario | Resultado observado |
| --- | --- |
| XP diaria | El farmhand recibió 301 XP de Pesca por 304 XP de entrada: 300 a ritmo completo y 4 a 25 %. El host vio el mismo contador. 500 XP de Cultivo del host se concedieron íntegros. |
| Fracción y reconexión | Tras un evento adicional, el contador era 305, con 301 concedidos y resto 0,25. Al reconectar el mismo farmhand, tres eventos de 1 XP concedieron exactamente un punto; total 308 de entrada y 302 concedidos. |
| Checkpoint y reinicio | Con un farmhand conectado, el checkpoint terminó clonando un farmhand. El archivo comprimido contenía el contador y el resto. Otro checkpoint conservó 308/302 y 150 de crédito; tras reiniciar el host, el estado cargado coincidió. |
| Cambio de día | El rollover cerró el cliente, guardó y avanzó el día. El primer punto de Pesca del host y del farmhand reconectado se concedió íntegro; ambos contadores nuevos empezaron en 1. |
| Telemetría | Al cerrar el día se registró para el farmhand 308 XP de entrada, 302 concedidos y 150 de créditos individuales. Se creó un archivo por jugador y día y se emitió el evento de host. El crédito se generó directamente, no mediante una venta. |
| Caverna Calavera | Entrar al piso 1 no redujo la energía. Una transición del piso 1 al 9 redujo 270 a 260,24 (coste 9,76). Con energía insuficiente, el jugador llegó al piso 11 con energía 0 y agotamiento. La transición de ocho pisos se provocó con un salto de prueba; no se usó físicamente un pozo. |
| Picada | Con la misma semilla y sin cebo, el resultado real del método fue 53.690,625 ms frente a 21.476,25 ms de vanilla: 2,5×. |
| Minijuego | Un tick real con el pez dentro incrementó la barra de 0,3 a 0,30133334: aproximadamente 0,002/1,5. Después, un `BobberBar.update` repetido manteniendo el pez dentro alcanzó 1,0 en 501 ticks con multiplicador 1 y 750 con 1,5 (1,497×). El arnés no consiguió reproducir una pérdida de progreso al mantener el pez fuera, así que el escape sigue pendiente. |
| Precios | Llamadas reales a `sellToStorePrice`, con valores temporales en el cliente: pescado 200→400 con 2× y gema 750→375 con 0,5×. No se probó la caja de envíos ni la sincronización de valores de precio entre configuraciones distintas. |
| Escalera por piedra | En el piso 3 se ejecutó la rutina real sobre 1.156 posiciones válidas con las mismas semillas: 35 escaleras con multiplicador 1 y 13 con 0,5. Con multiplicador 0, la última piedra generó una salida. Es una muestra exploratoria, no una calibración estadística. |

La primera versión de la prueba instrumentada de piedras generó coordenadas fuera del mapa y provocó excepciones al sincronizar ese piso en el laboratorio. Se corrigió el arnés para limitar las coordenadas al tamaño real del mapa; la muestra de la tabla es la repetición. Esa incidencia pertenece al arnés temporal y no acredita la sincronización normal de escaleras entre clientes.

## Automatización y límites

La compilación de la solución terminó sin avisos ni errores. Cinco pruebas existentes de dispositivos fallaban por mezclar fechas fijas caducadas con el reloj real; se corrigieron los datos de prueba. La suite completa pasó con 1.377 pruebas antes de los ajustes posteriores descritos abajo.

Quedan pendientes la prueba real de pozos y escaleras fabricadas, los modos difícil y festival del desierto, la frecuencia de escaleras por monstruo y su bonificación, la sincronización visual de escaleras, estanques y concursos de pesca, captura y escape completos, telemetría de ventas y envíos con ambas modalidades de cartera, reintentos del rollover y calibración durante una semana real. Estos resultados no cierran TASK-2102 a TASK-2107 ni aceptan ADR-0008.

Un intento posterior de medir `monsterDrop` repitió llamadas sobre el mismo piso: el arnés observó 30/1.000 eventos con 0,15 y 0/1.000 con 0,08. Como las llamadas mutan estado de la mina y no se usaron pisos independientes para cada serie, se descarta como muestra estadística; la frecuencia por monstruo y su sincronización siguen pendientes.

## Ajuste posterior a la sesión

La revisión de TASK-2106 detectó el riesgo de contar dos veces una mutación observada en `FarmerTeam` y `Farmer.Money`. El seguimiento suprime los setters anidados y registra una llamada anidada de equipo solo en su límite exterior. En una nueva sesión, `AddIndividualMoney` del host sumó 17g una vez, la asignación directa del host sumó 19g una vez, y un abono de 23g a un farmhand quedó en su ficha. Con carteras compartidas, el host registró 13g y el cliente 31g como créditos compartidos, sin duplicación.

El rollover produjo un JSON por jugador con 13g y 31g compartidos, respectivamente. La primera pasada de ese escenario descubrió que `SetFarmerMoney` escribía en `Farmer.Money` de un farmhand remoto cuando la cartera era compartida, y Stardew lanzó una excepción durante `DayStarted`. Se corrigió para usar `FarmerTeam.SetIndividualMoney` siempre que el destinatario no sea el granjero local; el rerun aplicó los cargos médicos de ambos granjeros y completó `DayStarted` sin esa excepción.

También se bloqueó deliberadamente el ACK del cliente: el host canceló el checkpoint tras 10 segundos, como prevé la barrera. La compilación completa tras estas correcciones terminó con cero avisos y errores. La suite final se volvió a ejecutar después de estos cambios: 1.377 pruebas pasaron, 0 fallaron y 0 quedaron omitidas.

## Cierre técnico en vivo (lotes A, A-2, B y restauración de checkpoint)

Las baterías adicionales en el laboratorio headless (host y *farmhand* en procesos separados sobre `Prueba29_453599927`, manteniendo `AdminApi=false` salvo verificación explícita del registro JSON en disco) cerraron todos los escenarios pendientes de `TASK-2102` a `TASK-2106`:

| Escenario | Resultado observado |
| --- | --- |
| Checkpoint y XP en tránsito (`TASK-2102`) | Con `watch_ckpt_xp` activo durante una barrera de checkpoint del host, 50 XP de Pesca quedaron retenidos (`beforeXp=0`, `afterXp=0`, `pending=0->1`) y se liberaron al finalizar la barrera (`CKPT_XP_RELEASED xp=50 pending=0`), quedando registrados en cliente y host. Al restaurar el respaldo de ese checkpoint (`Prueba29_453599927_20261114_203344_325`) en un arranque posterior de host y cliente, ambos cargaron el estado previo exacto (`Foraging=285`, `Mining=150`, sin XP retenida en tránsito) con `SkillExperience` coherente (`EarnedToday`, `RawXpToday`, `GrantedXpToday`). |
| *Nature in the Valley*, tope y reinicio (`TASK-2102`) | En cliente, la captura simulada sobre la ruta real de *Nature in the Valley* (`nitv_catch 1 1`, `CommonButterfly` rareza 1) sumó `0 → 15` XP de Forraje; tras subir a `295` (`gain 2 280 1`), una segunda captura (`nitv_catch 0 1`, `12` XP brutos) cruzó el cupo de 300 y dejó `foraging = 295 → 301` (`EarnedToday=307`, `Remainder=0.75`, `RawXpToday=307`, `GrantedXpToday=301`). Tras `ExitToTitle`, cierre completo del proceso cliente y reconexión al host, `xp[2]=301` y los cuatro campos permanecieron intactos; tras avanzar del día 3 al día 4, `SkillExperience` del cliente se reinició a `Skills={}`. |
| Pesca: picada, captura, escape, estanque y festival (`TASK-2103`) | Con multiplicadores `2.5` y `1.5`: `calculateTimeUntilFishingBite` pasó de `21.656,25` a `54.140,625 ms` sin cebo (`2,50×`) y de `14.437,5` a `36.093,75 ms` con cebo (`2,50×`); un estanque poblado conservó `1.000 ms` fijos frente a `36.093,75 ms` en estanque vacío; minijuego/evento de festival quedaron exentos (`13.708,5` / `14.437,5 ms`). En `BobberBar.update`, captura (`p=1.00`): `501 → 750` ticks (`1,497×`); escape (`p=0.00`): `330 → 496` ticks (`1,503×`); concurso de festival: `501` ticks (exento). |
| Minas: monstruos y sincronización (`TASK-2104`) | Muestra limpia en el piso 6 (`valid=308`, `trials=200`): `monsterDrop` generó `22` escaleras en vanilla (`0,15`), `13` con configuración `0,08` y `38` con bonificación de estatua del enano (`+0,07`). Al generar una escalera en el piso 6 desde el cliente (`x=3, y=18, idx=173`), el host observó de inmediato `CHECK_TILE loc=UndergroundMine6 x=3 y=18 buildingsIdx=173`. |
| Caverna Calavera, pozos, escaleras y modos (`TASK-2105`) | Con `SkullCavernDescentBaseCost=1.0` y `SkullCavernDescentDepthDivisor=25.0`: caída por pozo (`enterMineShaft`) `UndergroundMine121 → 126`, energía `270 → 264,2` (`−5,80`); escalera fabricada `(BC)71` (`CheckForActionOnStaircase`) `126 → 127`, `264,2 → 262,92` (`−1,28`); modo difícil (`SkullCavesDifficulty=1`) `121 → 125`, `262,92 → 258,36` (`−4,56`); Festival del Desierto (`DESERTFEST active=1`) `125 → 128`, `258,36 → 254,52` (`−3,84`). Exenciones verificadas: Minas normales (`UndergroundMine5 → 8`, coste `0`) y Volcán (`VolcanoDungeon1 → 2`, coste `0`). |
| Precios sincronizados, ventas reales, carteras e idempotencia (`TASK-2106`) | Se corrigió `EconomyModule` para sincronizar y aplicar en clientes las `PriceRules` del host: aun teniendo `1.0` local, el cliente vendió `(O)128` a `400g` (`2.0×`) en `FishShop` y `(O)72` a `375g` (`0.5×`) en `Blacksmith`. En la serie completa (lote B): (1) **Cartera individual con farmhand desconectado**: cliente vendió `400g` y envió `400g` antes de desconectar (`onlineOthers=0`); host vendió `375g` y envió `375g`; tras el rollover al día 3 se escribieron `2_1390281752221324484.json` (`individualGrossCredits=800`, `skills={Foraging:{before:307,after:301}}`) y `2_1978756610321227796.json` (`individualGrossCredits=750`, `skills={}`), e idempotencia `telemetry_retry 2` dejó `files=2->2`. (2) **Cartera compartida y atribución matinal**: se corrigió `DailyGoldTelemetryModule` (sustituyendo `newDaySync?.hasInstance()` por `closingGameDay` capturado en `DayEnding` y limpiado en `DayStarted`/`SaveLoaded`/`ReturnedToTitle`) para evitar que ventas de la mañana siguiente se imputasen al día anterior; con ambos conectados en día 3 (`400g` tienda + `400g` envío en cliente; `375g` tienda + `375g` envío en host), el rollover al día 4 generó `3_1390281752221324484.json` (`sharedWalletGrossCredits=400`) y `3_1978756610321227796.json` (`sharedWalletGrossCredits=1150`) —total exacto `1.550g` sin duplicados ni fugas al día 2— e idempotencia `telemetry_retry 3` dejó `files=4->4`. |

### Compilación y suite final

Tras estas correcciones y dos pruebas unitarias nuevas en `Pelican4Ever.Tests/EconomyRebalanceTests.cs` (separación de días/carteras e invariantes frente a reportes futuros de clientes), la solución `Pelican4Ever.sln` compiló con `0` advertencias y `0` errores, y la suite completa de `Pelican4Ever.Tests` pasó con `1.379/1.379` pruebas (`0` fallos, `0` omitidas).

Queda únicamente `TASK-2107`: jugar al menos una semana real con el grupo humano para calibrar los valores definitivos, decidir el tratamiento del botín de combate fuera de la Caverna Calavera y mover ADR-0008 a *Aceptado* junto con `design.md` (§4), `architecture.md`, `config.example.json` y los índices de ADR (la tarea multijugador se movió a [test-users-tasks.md](../../test-users-tasks.md)).

### Comprobación posterior de `AdminApi` y telemetría de eventos (`dev`)

Con `AdminApi` habilitado y token configurado en el laboratorio:
- `GET http://127.0.0.1:8085/api/v1/host/events` devolvió `401` sin cabecera Bearer y `200` autenticado, registrando `economy.daily@2` (`2` eventos, uno por jugador) sin duplicados tras `telemetry_retry 2` (`files=2->2`, `dailyEcon=2`) y con `stalled=0` en el rollover despierto (`host despierto: fundido lanzado por el mod`).
