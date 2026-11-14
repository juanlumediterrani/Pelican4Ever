# Minas: comportamiento y validación pendiente

Rama de trabajo: `codex/minas-progreso-individual`.

## Comportamiento acordado

- **Minas normales:** los pisos y recursos son compartidos. Cada personaje utiliza su profundidad para el ascensor y tiene su propio registro de cofres de recompensa. Una misión especial puede imponer un límite temporal compartido al ascensor.
- **Caverna Calavera:** sin ascensor; pisos y recursos compartidos. Se conserva el comportamiento original de salas de tesoro, recompensas y evento de Qi. Los cofres de las salas se crean localmente y su consumo se registra en el personaje; falta verificar su repetición entre días y regeneraciones.
- **Volcán de Isla Jengibre:** sin ascensor; pisos, recursos y cofres normales compartidos. Se conservan los atajos y mejoras originales de la partida.
- **Regeneración:** se limpian pisos desocupados a las horas configuradas (por defecto 06:00 y 18:00), y también tras el tiempo de inactividad configurado (45 minutos por defecto). Los pisos ocupados se conservan.

## Validación en laboratorio (2026-11-14, host + cliente headless en `dev`)

- [x] Ascensor individual y límite por misión: con `deepest=25` en host y `10` en cliente, `MineElevatorMenu` ofreció `[0,5,10,15,20,25]` al host y `[0,5,10]` al cliente (conservado tras rollover y reconexión). Con `LowestMineLevelForOrder=10`, el host quedó acotado a `[0,5,10]`.
- [x] Cofres de recompensa por piso: en `UndergroundMine10` (`[(B)506x1]`), host y cliente abrieron su propio cofre estando en el mismo piso; tras salir, reiniciar pisos y volver (y tras rollover/reconexión), ninguno pudo reclamarlo de nuevo (`consumed=True`).
- [x] Reinicios y preservación de pisos ocupados: `TriggerMineResetManual()` descargó pisos vacíos (`10:0`, `5:0`) y niveles del Volcán (`1:0`, `9:0`) preservando los ocupados (`4:1`, `3:1`). El cooldown por inactividad (`CheckMineFloorCooldowns`, simulando 50 min con tope 45 min) descargó `UndergroundMine121` (regenerado con nuevo hash `16135252 -> 17667057`) y `VolcanoDungeon9` preservando `UndergroundMine4` ocupado.
- [ ] Pendiente de comprobar en partida real prolongada: disparo automático por reloj real (`06:00` y `18:00`, que llama al mismo `ResetMines()`) y repetición de salas de tesoro aleatorias de la Caverna Calavera tras varios días/reinicios.
