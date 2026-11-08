# ADR-0007: Generación de Anexos con el Flujo «Mapas Puente» (`MapStudio` + Recorte Canónico + Clasificadores Verificados)

* **Estado:** Aceptado (Revisado tras post-mortem en vivo del 2026-11-08)
* **Fecha:** 2026-11-08

## Contexto y Post-Mortem de Intentos Previos

Los minimapas de anexo de residencia (40×32 casillas) requieren un acabado visual idéntico al de Stardew Valley vanilla y una transitabilidad sin bloqueos en las 3 orientaciones (`North`, `East`, `South`) de los 8 tipos de granja. Antes de esta revisión se probaron tres enfoques que fracasaron por razones opuestas:

1. **Repintado procedural empírico sin atlas (`4eeb66f`):** Unas 1000 líneas de reglas ad-hoc con índices elegidos sin verificar contra el atlas `.xnb` (p. ej. usar `1244` creyendo que era madera cuando en `spring_outdoorsTileSheet` es borde de río, o mezclar sombra de bosque `351` con pradera soleada `175`).
2. **Mapas `.tbin` dibujados desde cero por un agente (`c06b40c`, descartado):** Un modelo de lenguaje no puede diseñar un mapa entero casilla a casilla sin referencia visual.
3. **24 ventanas por puntuación matemática + pegado de tiras de catálogo + prohibición absoluta de `StaticTile` (`d3def1a`, descartado tras prueba en vivo):**
   - **Bloqueo total en las entradas:** Se escribió `backLayer.Tiles[cx, cy].Properties["Passable"] = "T"`. En `GameLocation.isTilePassable` de Stardew Valley 1.6, cualquier propiedad `Passable` no nula en `Back` (incluso `"T"`) hace la casilla **intransitable** (`return passable == null`). Además, al vaciar `Buildings`/`Front` sin cambiar el tile de `Back`, quedaron expuestos tiles con `TileIndexProperties["Passable"]` o `"Water"` en la hoja de sprites.
   - **Costuras interiores y efecto «MissingNo»:** Pegar tiras de 4 casillas (`VanillaBlockCatalog` `EdgeStrip`) encima de un recorte de 40×32 creó una costura interior en `x=3` / `y=3`, partiendo árboles multi-casilla por la mitad y mezclando hierba de sombra de bosque (`351`) con pradera soleada (`175`).
   - **Anexo Fluvial destrozado:** Recortar 40×32 por puntuación numérica y pegar tiras de borde guillotinó islas, canales y puentes de `Maps/Farm_Fishing`.
   - **Árboles y escombros de runtime en la entrada:** No se definió ni limpió un área de despeje interior de **3×4 casillas** frente a la entrada, dejando al jugador atrapado contra árboles (`Tree`) y rocas al entrar.
   - **Ceguera visual:** Se desplegó confiando únicamente en `dotnet test` sin haber renderizado ni inspeccionado visualmente los `.png` 1:1 en `MapStudio`.
4. **Salidas muertas y cercados «placeholder» de 1 casilla (Gap detectado e iteración del 2026-11-08):**
   - **Gap de entrada/salida única por orientación (`AnnexSide`):** Cada minimapa anexo se acopla a **un único lado** de la residencia (`North`, `East` o `South`), por lo que en el juego **solo posee una única entrada/salida activa** en su borde recíproco (borde Sur cuando `AnnexSide.North`, borde Oeste cuando `AnnexSide.East`, borde Norte cuando `AnnexSide.South`). Recortar un mapa vanilla que tiene 2 o 3 salidas originales dejaba **salidas o puentes muertos** en los bordes no activos que chocaban contra muros invisibles.
   - **Prohibición de vallas «spaceholder» en los bordes del recorte:** Colocar una simple valla de madera de 1 casilla de grosor sobre el borde de tierra cortada (`Standard`) no representa una miniatura de la granja original, sino un marcador de posición insuficiente. Cada minimapa debe enmarcar sus 4 lados (`Norte`, `Sur`, `Oeste`, `Este`) con los **activos perimetrales auténticos de su granja vanilla inspiracional** (acantilados de roca y terraza forestal norte, talud herboso oeste con cerezos, franja boscosa este y arboleda sur en `Maps/Farm`; islas redondeadas sobre agua y un único puente/península activa en `Maps/Farm_Fishing`).

## Decisión: Flujo «Mapas Puente» + Composición de Miniatura por Variante de Entrada Única ([ADR-0005](0005-host-plaza-and-bridge-maps.md))

En la Épica 10 (`Pelican4Ever_Bridge_1..3` y las 5 entradas de `Pelican4Ever_HostFarm`), el trabajo quedó fino y aceptable porque combinó **inspección visual previa con `MapStudio`**, **composición coherente con activos del mapa original** y **cirugía localizada con clasificadores verificados en el atlas `.xnb`**. Adoptamos y ampliamos ese flujo para los 8 anexos (24 variantes):

1. **Simulador Visual Offline (`MapStudio`) y Bucle con Agente Adversarial (`adversarial-vanilla-critic`):**
   * Extraer las hojas de tiles reales (`spring_outdoorsTileSheet`, `v16_Outdoors2`, etc.) y componer fuera del juego todas las capas (`Back`, `Buildings`, `Front`, `AlwaysFront`) en imágenes `.png` 1:1 comparadas directamente contra el mapa vanilla completo inspiracional.
   * **Regla de oro:** Ningún minimapa se da por terminado sin superar en bucle la auditoría de un **agente crítico adversarial (`adversarial-vanilla-critic`)** que contraste cada borde, esquina y entrada contra la granja vanilla original.

2. **Composición de Miniatura Fiel al Mapa Inspiracional y 3 Variantes de Entrada Única por Tipo (0 Salidas Muertas):**
   * Cada tipo de anexo (`Standard`, `Fishing`, `Foraging`, `Mining`, `Combat`, `FourCorners`, `Ranching`, `Beach`) parte de su recorte base 40×32 y genera **3 variantes específicas según el lado activo (`AnnexSide.North`, `AnnexSide.East`, `AnnexSide.South`)**:
     - **Única entrada/salida activa:** Solo el borde conectado a la residencia abre paso (camino entre acantilados en el Norte, paso entre talud y acantilado en el Oeste, o camino entre cerezos en el Sur para `Standard`; puente norte, península oeste o puente sur para `Fishing`).
     - **Cierre orgánico de los otros 3 lados (0 salidas muertas):** Los caminos o puentes de los bordes no activos se cierran orgánicamente con los mismos tiles de acantilado, orilla de río, hierba y dosel arbóreo (`AlwaysFront`) del mapa original.
     - **Enmarcado perimetral auténtico en los 4 lados:** En lugar de vallas «placeholder» pegadas al muro de colisión, los 4 bordes del minimapa injertan las franjas perimetrales reales del propio mapa vanilla inspiracional alineando paridad de acantilados (`467/468`, `394/419`) y continuidad de copas sin crear costuras interiores.

3. **Cirugía Fina con Clasificadores Verificados en el Atlas (`TerrainTileClassifier` y `VerticalMultiTileClassifier`):**
   * Prohibido adivinar números de tile: toda sustitución puntual usa exclusivamente [`TerrainTileClassifier`](../../Pelican4Ever.Core/World/TerrainTileClassifier.cs) y [`VerticalMultiTileClassifier`](../../Pelican4Ever.Core/World/VerticalMultiTileClassifier.cs), cuyos índices están verificados contra el atlas `.xnb`:
     - **Suelo (`TerrainTileClassifier`):** Pradera soleada (`175` con acentos `150, 151, 152, 256`), camino de tierra (`587`) con bordes suaves tierra/hierba (`225, 228, 201, 251, 250, 253`) y puentes de madera canónicos sobre canales de agua. Prohibido mezclar sombra de bosque (`351..401`) en pradera abierta.
     - **Objetos verticales multi-casilla (`VerticalMultiTileClassifier`):** Toda base en `Buildings[x, y]` conserva su remate en `Front[x, y - 1]`; se eliminan bases decapitadas en `y = 0` y copas huérfanas en bordes, y los árboles estáticos interceptados por una apertura se limpian por su caja completa (`3×4` / `6×4`).
     - **Cierre de tramos abiertos:** Vallas horizontales (`383..385` / `358..360`), verticales (`361, 386, 411, 436`) y arbustos completos 3×3 (`63..65` / `38..40` / `13..15`).

4. **Umbral Transitable y Área de Despeje Interior de 3×4 Casillas:**
   * En la apertura (2 casillas), corredor y margen interior de **3×4 casillas**:
     - En `Back`: **eliminar** la propiedad `Passable` (`Properties.Remove("Passable")`, jamás asignar `"T"`) y colocar un tile de suelo limpio sin `TileIndexProperties["Passable"]` ni `"Water"`.
     - En runtime (`FarmSlotModule`): despejar de `objects` (rocas, ramas, maleza), `terrainFeatures` (`Tree`, `Bush`, `Grass`) y `resourceClumps` el área 3×4 junto a la entrada al crear o cargar el anexo, y situar el letrero de retorno al costado exterior del corredor.

5. **Verificación Topológica BFS Fiel a `GameLocation.isTilePassable` y Migración con Cofre de Resguardo:**
   * `IsTilePassableSDV` replica exactamente `GameLocation.isTilePassable` (cualquier `Passable` en `Back` es intransitable).
   * Los 24 casos se verifican con BFS sobre los `.xnb` reales (como en `TASK-1403` de los Mapas Puente).
   * Los anexos ya comprados migran una sola vez (`MigrateAnnexToV2`), resguardando cualquier objeto o cultivo desplazado en un cofre situado fuera del área de despeje de entrada.
