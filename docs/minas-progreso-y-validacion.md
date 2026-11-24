# Minas, Caverna Calavera y Volcán: comportamiento multijugador y validación

Estado: Integrado en `dev`/`main` (referencia normativa del subsistema de minas).

## Comportamiento acordado

- **Minas normales:** los pisos y recursos son compartidos. Cada personaje utiliza su profundidad para el ascensor y tiene su propio registro de cofres de recompensa. Una misión especial puede imponer un límite temporal compartido al ascensor.
- **Caverna Calavera:** sin ascensor; pisos y recursos compartidos. Se conserva el comportamiento original de salas de tesoro, recompensas y evento de Qi. Los cofres de las salas se crean localmente y su consumo se registra en el personaje (`chestConsumedMineLevels` por encima del piso 120 se limpia en cada cambio de día también para farmhands desconectados vía `DailyRolloverModule.ResetOfflineFarmhandSkullCavernChests`).
- **Volcán de Isla Jengibre:** sin ascensor; pisos, recursos y cofres normales compartidos. Se conservan los atajos y mejoras originales de la partida.
- **Regeneración:** se limpian pisos desocupados a las horas configuradas (por defecto 06:00 y 18:00), y también tras el tiempo de inactividad configurado (45 minutos por defecto). Los pisos ocupados se conservan.

## Validación en laboratorio (2026-11-14, host + cliente headless en `dev`)

- [x] Ascensor individual y límite por misión: con `deepest=25` en host y `10` en cliente, `MineElevatorMenu` ofreció `[0,5,10,15,20,25]` al host y `[0,5,10]` al cliente (conservado tras rollover y reconexión). Con `LowestMineLevelForOrder=10`, el host quedó acotado a `[0,5,10]`.
- [x] Cofres de recompensa por piso: en `UndergroundMine10` (`[(B)506x1]`), host y cliente abrieron su propio cofre estando en el mismo piso; tras salir, reiniciar pisos y volver (y tras rollover/reconexión), ninguno pudo reclamarlo de nuevo (`consumed=True`).
- [x] Reinicios y preservación de pisos ocupados: `TriggerMineResetManual()` descargó pisos vacíos (`10:0`, `5:0`) y niveles del Volcán (`1:0`, `9:0`) preservando los ocupados (`4:1`, `3:1`). El cooldown por inactividad (`CheckMineFloorCooldowns`, simulando 50 min con tope 45 min) descargó `UndergroundMine121` (regenerado con nuevo hash `16135252 -> 17667057`) y `VolcanoDungeon9` preservando `UndergroundMine4` ocupado.
- [x] Disparo automático por reloj real (`18:00`, `06:00` y rollover programado `02:00` mediante fichero `TZif` aislado con `TimeZoneId = ""`, sin alterar la hora ni zona del sistema): a las `18:00:00` y `06:00:00` `EconomyModule` ejecutó `ResetMines()` automáticamente descargando pisos vacíos (`121:0,5:0` / Volcano `9:0`, y `122:0,6:0` / Volcano `8:0`) mientras preservó los pisos ocupados (`4:1` y `3:1`); a las `02:00:00` `DailyRolloverModule` emitió el aviso de 10 minutos y ejecutó el rollover programado completo (`trigger=scheduled`, backup y cierre de peer).
- [x] Salas de tesoro aleatorias y forzadas en Caverna Calavera tras reinicios y cambio de día: host y cliente recibieron cofres locales independientes en salas aleatorias (`UndergroundMine132` en Día 2 y `135` en Día 3) y en el piso `220`; tras `ResetMines()` en el mismo día ambos conservaron `consumed=True` (`0` cofres); tras rollover al Día 3, `ResetOfflineFarmhandSkullCavernChests()` corrigió el bloqueo que dejaba a los farmhands con `consumed=True` para siempre, limpiando `>120` en ambos jugadores (`host`: `2` cofres -> limpio, `client`: `2` cofres en `220` y `132` -> `consumed=False`) sin tocar pisos `<=120`.
