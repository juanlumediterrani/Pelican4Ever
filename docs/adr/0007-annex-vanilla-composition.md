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

## Decisión: Replicar el Flujo de Trabajo de los «Mapas Puente» ([ADR-0005](0005-host-plaza-and-bridge-maps.md))

En la Épica 10 (`Pelican4Ever_Bridge_1..3` y las 5 entradas de `Pelican4Ever_HostFarm`), el trabajo quedó fino y aceptable a la primera porque combinó **inspección visual previa con `MapStudio`**, **un único recorte coherente** y **cirugía localizada con clasificadores verificados en el atlas `.xnb`**. Adoptamos exactamente ese mismo flujo para los 8 anexos:

1. **Simulador Visual Offline (`MapStudio`) y Atlas Indexado (`.xnb` → `.png`):**
   * Extraer las hojas de tiles reales (`spring_outdoorsTileSheet`, etc.) y componer fuera del juego todas las capas (`Back`, `Buildings`, `Front`, `AlwaysFront`) en imágenes `.png` 1:1 con rejilla de coordenadas y mapa de colisión real.
   * **Regla de oro:** Ningún cambio en los anexos se da por terminado ni se despliega sin haber generado y revisado visualmente su render `.png` en `MapStudio`.

2. **Un único recorte canónico (40×32) por cada uno de los 8 tipos (sin pegar tiras de catálogo):**
   * Igual que `CreateBridgeMap` usa un único recorte coherente de `Maps/BusStop` (`10, 0, 35×30`), cada uno de los 8 anexos (`Standard`, `Fishing`, `Foraging`, `Mining`, `Combat`, `FourCorners`, `Ranching`, `Beach`) parte de **un único recorte base 40×32** elegido visualmente en `MapStudio` para preservar su topología natural (islas enteras con sus puentes en `Fishing`, pradera despejada en `Standard`, etc.).
   * **Se retira `VanillaBlockCatalog` (`EdgeStrip`) y `AnnexWindowScoringLogic`**, eliminando de raíz las costuras interiores y los ríos guillotinados.

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
