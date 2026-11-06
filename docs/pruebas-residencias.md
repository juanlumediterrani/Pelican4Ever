# Pruebas en vivo de los tipos de residencia

Comandos del host para comprobar en partida las mecánicas por tipo de granja de TASK-607 sin esperar días reales ni subir habilidades a mano.

## Activarlos

Solo funcionan con `"EnableTestCommands": true` en `config.json`, que viene desactivado. Úsalos solo en partidas de prueba: `p4e_farm_found` regenera el hueco y borra lo que hubiera en él.

Antes de empezar:

```
p4e_checkpoint_now
```

Se ejecutan en la consola SMAPI del host o en su chat con `/`. Cada comando que cambia el mundo queda en el registro de sucesos como `host.test.command`.

## Comandos

| Comando | Qué hace |
|---|---|
| `p4e_farm_found <hueco> <tipo> [jugador]` | Funda la residencia para el jugador (sin jugador, el host) o, si ya es suya, cambia el tipo y la regenera como al fundarla. Tipos: `Standard`, `Fishing`, `Foraging`, `Mining`, `Combat`, `FourCorners`, `Ranching` (o `Meadowlands`), `Beach`. Un jugador solo puede tener una residencia. |
| `p4e_farm_tp <destino> [jugador] [house\|north\|south\|east\|cave]` | Teletransporta al jugador a la casa, a una de las llegadas norte, sur o este, o a la cueva. En un anexo lleva junto a su entrada. |
| `p4e_farm_inspect <destino>` | Mapa, tipo, dueño y sus niveles, propiedades de mapa, clima, cajas, forraje, nodos de cantera, pararrayos, monstruos, papel y suelo de la casa y contenido del regalo de bienvenida. |
| `p4e_farm_spawn <destino> <forage\|ores\|crate\|monsters> [n]` | Fuerza n veces: el forraje de `Data/Locations`, los minerales diarios de la cantera, una caja de suministros sobre una casilla `BeachSpawn` o un monstruo de suelo. El forraje propio de playa y bosque solo sale con el cambio de día (`p4e_rollover_now`). |
| `p4e_lightning <destino> [n]` | n rayos seguros, sin tope diario ni tirada de probabilidad. |
| `p4e_weather <sun\|rain\|storm> [today\|tomorrow]` | Clima del valle hoy o mañana. |
| `p4e_skill <jugador> <farming\|fishing\|foraging\|mining\|combat\|luck> <0-10>` | Fija el nivel y su experiencia mínima. No concede profesiones. |

`<destino>` es `03` (residencia), `03/North` (anexo de ese lado) o `host` (granja principal). También se aceptan los nombres completos `Pelican4Ever_Farm_03` y `Pelican4Ever_Annex_03_North`.

Ya existían y sirven en estas pruebas: `p4e_doors`, `p4e_give_item`, `p4e_give_gold`, `p4e_time` y `p4e_rollover_now`.

## Pruebas

Partida del servidor de tipo **Estándar** y al menos un farmhand conectado (en los ejemplos, `Juan` en el hueco `03`).

1. **Playa**
   ```
   p4e_farm_found 03 Beach Juan
   p4e_farm_inspect 03
   p4e_farm_tp 03 Juan east
   p4e_farm_spawn 03 crate 5
   ```
   Comprobar: mapa `Maps\Farm_Island`, `SpawnBeachFarmForage=T`, casa, buzón, cueva y las tres llegadas en su sitio. Romper las cajas: alguna suelta `(O)920`, nunca Geoda omni. Con `p4e_give_item 599 1 Juan` (aspersor), en la arena no deja colocarlo; en el rectángulo fértil sí riega. Tras `p4e_rollover_now`, hay forraje de playa en la orilla.
2. **Anexo de playa**: comprarlo desde la residencia y `p4e_farm_tp 03/North Juan`. Revisar el recorte y que se puede volver a la residencia.
3. **Pradera**: `p4e_farm_found 03 Ranching Juan` y `p4e_farm_inspect 03`. Regalo `(O)178 x15`, papel y suelo 41, gallinero con dos gallinas.
4. **Fluvial**: `p4e_farm_found 03 Fishing Juan`. El regalo incluye `(BC)FishSmoker`.
5. **Salvaje**
   ```
   p4e_farm_found 03 Combat Juan
   p4e_skill Juan combat 10
   p4e_farm_spawn 03 monsters 20
   p4e_farm_inspect 03
   ```
   Aparecen `Iridium Golem` y monstruos escalados aunque el host tenga combate 0. Con `p4e_skill Juan combat 1`, no salen gólems de iridio.
6. **Colinas**
   ```
   p4e_farm_found 03 Mining Juan
   p4e_skill Juan mining 10
   p4e_farm_spawn 03 ores 20
   p4e_farm_inspect 03
   ```
   Hay nodos de oro e iridio. Con `p4e_skill Juan mining 0`, solo piedra, cobre y geodas.
7. **Tormenta**: `p4e_give_item (BC)9 3 Juan`, colocar los pararrayos en la residencia y `p4e_lightning 03 3`. Los pararrayos tienen pila (`p4e_farm_inspect 03`). `p4e_lightning host` sigue funcionando en la granja principal. Con `p4e_weather storm`, a lo largo del día caen rayos en residencias y granja principal.

Al terminar, restaurar el checkpoint o la copia previa si la partida se va a seguir usando.
