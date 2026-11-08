# ADR-0007: Composición de Anexos solo con Piezas Vanilla

* **Estado:** Aceptado
* **Fecha:** 2026-11-08

## Contexto

Los minimapas de anexo de residencia (40×32 casillas) permitían ampliar el terreno privado pero presentaban problemas de acabado estético y coherencia espacial:

1. **Repintado procedural frágil:** `MapCloneHelper.CreateAnnexMap` recortaba un cuadrante fijo del mapa vanilla, sellaba el perímetro con `Passable=F` invisible y aplicaba unas 1000 líneas de reglas procedurales por índice de tile (puentes de tablas, suavizado de orillas, barandillas, descarte de glitches). El resultado dejaba paredes invisibles en mitad del campo, árboles partidos por la mitad y un aspecto artificial.
2. **Intento fallido de mapas dibujados por agente (commit `c06b40c`, descartado):** Se probó generar mapas `.tbin` dibujados casilla a casilla por un modelo de lenguaje. El resultado fue inadmisible: un modelo de texto carece de percepción espacial y sentido estético para ensamblar tilesets complejos de Stardew Valley.

## Opciones Evaluadas

* **Opción A (Seguir con recorte y más parches procedurales por índice):** Descartada. Aumenta la deuda técnica, añade fragilidad entre versiones y no resuelve el corte brusco de los bordes.
* **Opción B (Mapas dibujados por un agente de IA):** Descartada tajantemente tras el fracaso de `c06b40c`.
* **Opción C (Composición exclusivamente con piezas vanilla):** Seleccionada. Recorte de ventanas óptimas puntuadas por herramienta, aprovechando esquinas y pasos de salida ya dibujados por ConcernedApe, y cerrando lados abiertos con bloques completos copiados del mapa vanilla original.
* **Opción D (Mapas dibujados a mano por una persona en Tiled):** Plan B para aquellos tipos o lados específicos que no admitan una solución estética perfecta mediante la Opción C.

## Decisión

1. **Regla de oro: Ningún agente elige índices de tile.**
   * Ningún agente ni código del mod colocará tiles con índices fijos a mano (`new StaticTile(..., índice)`), ni generará mapas dibujados por IA, ni implementará reglas de "decoración" por índice.
   * Todo lo visible en un anexo debe provenir de un mapa vanilla del juego, respetando su forma y proporciones originales.
   * Se autoriza únicamente:
     - Recortar ventanas de mapas vanilla.
     - Pegar **bloques enteros** (definidos por mapa de origen, rectángulo fuente y capas completas: `Back`, `Buildings`, `Front`, `AlwaysFront`, `Paths`).
     - Modificar propiedades (`Passable`, `Type`, `Diggable`, `Warp`) y despejar objetos de suelo o colisión en pasos de entrada.

2. **Aprovechamiento de bordes y salidas vanilla naturales:**
   * Las esquinas de los mapas `Maps/Farm_*` ya poseen dos bordes naturales acabados (acantilados, lindes de árboles, agua).
   * Las salidas naturales de las granjas (hacia Backwoods al norte, BusStop al este y Forest al sur) se usan como entradas al anexo cuando la ventana elegida las incluya.
   * La posición de la entrada pasa a ser una coordenada declarativa propia de cada ventana en lugar de un punto central fijo.

3. **Carga dinámica desde el juego:**
   * Los bloques y ventanas se leen directamente del mapa vanilla cargado por el juego (`Helper.GameContent.Load<Map>("Maps/Farm_*")`).
   * Esto garantiza que los anexos cambien de estación automáticamente (primavera, verano, otoño, invierno) sin duplicar texturas ni arte en el mod.

4. **Tratamiento de anexos existentes (TASK-912):**
   * Al migrar a la nueva composición, los objetos, cultivos, máquinas y árboles de los jugadores que queden sobre colisión o fuera de los límites transitables se trasladarán automáticamente a un cofre de resguardo situado cerca de la entrada (fuera de la casilla de llegada).
   * La migración es ejecutada exclusivamente por el host una sola vez por anexo.

## Consecuencias

* **Positivas:**
  - Acabado visual idéntico al juego original, sin cortes geométricos bruscos ni muros invisibles en el campo abierto.
  - Compatibilidad estacional nativa y limpia.
  - Código desacoplado de heurísticas visuales frágiles por número de tile.
* **Negativas:**
  - Requiere evaluar y catalogar las 24 ventanas (8 tipos × 3 lados). Si alguna orientación en un mapa particular no tiene una solución natural adecuada, deberá recurrirse a la Opción D (diseño manual en Tiled).
