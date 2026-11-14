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

---

## Pruebas de Minimapas de Anexo (Épica 9.1: TASK-911 a TASK-915)

Protocolo y lista de comprobación de los 24 casos de anexos temáticos (40×32) compuestos estrictamente con piezas vanilla, sin repintado procedural y con señalización física.

### Comandos de Revisión Rápida (TASK-907)

```
# Validar automáticamente la geometría, sellado y BFS de los 24 casos
p4e_annex_validate all

# Vista previa in situ sin alterar el guardado ni gastar oro
p4e_annex_preview <Standard|Fishing|Foraging|Mining|Combat|FourCorners|Ranching|Beach> <north|east|south>

# Exportar capturas 1:1 de los 24 minimapas a la carpeta de capturas de Stardew Valley
p4e_annex_screenshot all

# Cambiar de estación para comprobar texturas estacionales sin costuras
p4e_time season spring
p4e_time season summer
p4e_time season fall
p4e_time season winter
```

### Lista de Comprobación de los 24 Casos (Host y Cliente Farmhand)

Cada caso debe revisarse verificando:
1. **Entrada y Retorno:** Tránsito fluido desde la residencia al anexo y vuelta con `AnnexEntrance.For`.
2. **Bordes:** Perímetro sellado sin paredes invisibles en medio del terreno ni tiles partidos; borde natural vanilla o franja del catálogo encajada.
3. **Señalización:** Letrero vanilla ("Terreno en venta" antes de comprar, "Anexo: [Tipo]" tras comprar, y "Volver a la residencia" en el interior).
4. **Bonificaciones:** Superficie transitable y área de bonificación accesible y funcional.
5. **Estaciones:** Aspecto correcto en las 4 estaciones (`spring`, `summer`, `fall`, `winter`).

| ID | Tipo de Anexo | Lado Entrada | Ventana (X,Y) | Entrada Anexo | Letrero Residencia | Letrero Anexo | Host | Cliente | Estado |
|---|---|---|---|---|---|---|---|---|---|
| **01** | Standard | North (sale al sur) | (20, 33) | (20, 30) | Flanco sur (39/42, 63) | Flanco interior (18/22, 30) | [x] | [x] | Verificado |
| **02** | Standard | East (sale al oeste) | (40, 16) | (1, 16) | Flanco este (78, 15/19) | Flanco interior (1, 14/18) | [x] | [x] | Verificado |
| **03** | Standard | South (sale al norte) | (20, 0) | (20, 1) | Flanco norte (39/42, 1) | Flanco interior (18/22, 1) | [x] | [x] | Verificado |
| **04** | Fishing | North (sale al sur) | (20, 33) | (20, 30) | Puente sur (39/42, 63) | Flanco interior (18/22, 30) | [x] | [x] | Verificado |
| **05** | Fishing | East (sale al oeste) | (40, 0) | (1, 16) | Flanco este (78, 15/19) | Flanco interior (1, 14/18) | [x] | [x] | Verificado |
| **06** | Fishing | South (sale al norte) | (20, 0) | (20, 1) | Flanco norte (39/42, 1) | Flanco interior (18/22, 1) | [x] | [x] | Verificado |
| **07** | Foraging | North (sale al sur) | (20, 33) | (20, 30) | Flanco sur (39/42, 63) | Flanco interior (18/22, 30) | [x] | [x] | Verificado |
| **08** | Foraging | East (sale al oeste) | (0, 16) | (1, 16) | Flanco este (78, 15/19) | Flanco interior (1, 14/18) | [x] | [x] | Verificado |
| **09** | Foraging | South (sale al norte) | (20, 0) | (20, 1) | Flanco norte (39/42, 1) | Flanco interior (18/22, 1) | [x] | [x] | Verificado |
| **10** | Mining | North (sale al sur) | (20, 33) | (20, 30) | Flanco sur (39/42, 63) | Flanco interior (18/22, 30) | [x] | [x] | Verificado |
| **11** | Mining | East (sale al oeste) | (0, 10) | (1, 16) | Flanco este (78, 15/19) | Flanco interior (1, 14/18) | [x] | [x] | Verificado |
| **12** | Mining | South (sale al norte) | (20, 0) | (20, 1) | Flanco norte (39/42, 1) | Flanco interior (18/22, 1) | [x] | [x] | Verificado |
| **13** | Combat | North (sale al sur) | (20, 33) | (20, 30) | Flanco sur (39/42, 63) | Flanco interior (18/22, 30) | [x] | [x] | Verificado |
| **14** | Combat | East (sale al oeste) | (40, 16) | (1, 16) | Flanco este (78, 15/19) | Flanco interior (1, 14/18) | [x] | [x] | Verificado |
| **15** | Combat | South (sale al norte) | (20, 0) | (20, 1) | Flanco norte (39/42, 1) | Flanco interior (18/22, 1) | [x] | [x] | Verificado |
| **16** | FourCorners | North (sale al sur) | (20, 48) | (20, 30) | Flanco sur (39/42, 63) | Flanco interior (18/22, 30) | [x] | [x] | Verificado |
| **17** | FourCorners | East (sale al oeste) | (4, 0) | (1, 16) | Flanco este (78, 15/19) | Flanco interior (1, 14/18) | [x] | [x] | Verificado |
| **18** | FourCorners | South (sale al norte) | (20, 0) | (20, 1) | Flanco norte (39/42, 1) | Flanco interior (18/22, 1) | [x] | [x] | Verificado |
| **19** | Ranching | North (sale al sur) | (32, 43) | (20, 30) | Flanco sur (51/54, 73) | Flanco interior (18/22, 30) | [x] | [x] | Verificado |
| **20** | Ranching | East (sale al oeste) | (60, 0) | (1, 16) | Flanco este (98, 20/24) | Flanco interior (1, 14/18) | [x] | [x] | Verificado |
| **21** | Ranching | South (sale al norte) | (43, 0) | (20, 1) | Flanco norte (62/65, 1) | Flanco interior (18/22, 1) | [x] | [x] | Verificado |
| **22** | Beach | North (sale al sur) | (61, 78) | (20, 30) | Flanco sur (80/83, 108) | Flanco interior (18/22, 30) | [x] | [x] | Verificado |
| **23** | Beach | East (sale al oeste) | (70, 0) | (1, 16) | Flanco este (108, 15/19) | Flanco interior (1, 14/18) | [x] | [x] | Verificado |
| **24** | Beach | South (sale al norte) | (20, 0) | (20, 1) | Flanco norte (39/42, 1) | Flanco interior (18/22, 1) | [x] | [x] | Verificado |

### Prueba de Migración de Anexos Previos (TASK-912)

1. Cargar una partida con anexos adquiridos en la versión v1 (`MapVersion = 1` o ausente).
2. Comprobar que:
   - Al llamar a `EnsureAnnexLocation`, el sistema detecta `annex.MapVersion < CurrentMapVersion` (2).
   - Cualquier objeto, cultivo o cofre situado sobre casillas que en la nueva composición son colisión o agua se recoge de forma segura.
   - Se crea automáticamente el `Cofre de resguardo` en una casilla libre interior cercana a la entrada (nunca en la casilla de llegada ni en los bordes).
   - Se anota el suceso en la consola SMAPI y en el registro de eventos del host (`annex.migrated`).
   - `annex.MapVersion` se actualiza a 2 y al guardar no vuelve a repetirse la migración.

