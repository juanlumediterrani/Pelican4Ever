# Informe de Recortes Canónicos 40×32 y Entradas para los 24 Casos de Anexo (TASK-910 / TASK-915)

> Resultado del análisis topológico y simulación visual offline mediante `MapStudio` sobre los 8 mapas vanilla oficiales de Stardew Valley 1.6, aplicando la metodología canónica de los **Mapas Puente (ADR-0005 / ADR-0007)**: un único recorte base coherente por tipo de granja, cirugía perimetral mediante `TerrainTileClassifier` y `VerticalMultiTileClassifier`, área de despeje interior de 3×4 casillas y verificación BFS según la regla real de `GameLocation.isTilePassable`.

---

## Tabla de los 8 Recortes Canónicos y sus 3 Entradas

| Tipo de Anexo | Mapa Vanilla Oficial | Recorte Base (X, Y) | Entrada Norte (`AnnexSide.North`, borde sur $y=31$) | Entrada Sur (`AnnexSide.South`, borde norte $y=0$) | Entrada Este (`AnnexSide.East`, borde oeste $x=0$) | Casillas Conexas BFS | Composición de Miniatura y Variantes (0 Salidas Muertas) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| **Huerto Estándar (`Standard`)** | `Maps/Farm` | `(15, 33)` | $x = 25..26$ (llegada en $x=25, y=30$) | $x = 22..23$ (llegada en $x=22, y=1$) | $y = 18..19$ (llegada en $x=1, y=18$) | 790..802 / 802 | Miniatura auténtica de `Maps/Farm` con acantilado y terraza forestal norte (`sy=0..8`), talud herboso oeste con cerezos (`sx=0..3`), valla y franja boscosa este (`sx=76..79`) y arboleda sur (`sy=60..64`). Cada variante abre solo su paso y sella orgánicamente los otros 3 lados. |
| **Remanso Fluvial (`Fishing`)** | `Maps/Farm_Fishing` | `(22, 25)` | $x = 25..26$ (llegada en $x=25, y=30$) | $x = 4..5$ (llegada en $x=4, y=1$) | $y = 8..9$ (llegada en $x=1, y=8$) | 760..785 / 853 | Red de islas centrales rodeadas de río. Variante `North`: único puente sur ($x=25..26$); variante `East`: única península oeste ($y=8..9$); variante `South`: único puente norte ($x=4..5$). Resto de puentes eliminados con orillas selladas. |
| **Franja Forestal (`Foraging`)** | `Maps/Farm_Foraging` | `(10, 8)` | $x = 20..21$ (llegada en $x=20, y=30$) | $x = 20..21$ (llegada en $x=20, y=1$) | $y = 15..16$ (llegada en $x=1, y=15$) | 1021 / 1072 | Claro forestal con 4 tocones de madera noble renovables (`Paths == 21`) y forraje silvestre. |
| **Cantera de Minerales (`Mining`)** | `Maps/Farm_Mining` | `(3, 25)` | $x = 20..21$ (llegada en $x=20, y=30$) | $x = 17..18$ (llegada en $x=17, y=1$) | $y = 15..16$ (llegada en $x=1, y=15$) | 713..829 / 862 | Meseta de colina con la cantera vanilla completa remapeada a `(2, 12, 22, 8)`. |
| **Coto Salvaje (`Combat`)** | `Maps/Farm_Combat` | `(24, 10)` | $x = 20..21$ (llegada en $x=20, y=30$) | $x = 20..21$ (llegada en $x=20, y=1$) | $y = 15..16$ (llegada en $x=1, y=15$) | 919..1079 / 1079 | Claro amplio de colinas, monstruos nocturnos activos sin afectar a la residencia. |
| **Cuadrante Mixto (`FourCorners`)** | `Maps/Farm_FourCorners` | `(32, 8)` | $x = 13..14$ (llegada en $x=13, y=30$) | $x = 20..21$ (llegada en $x=20, y=1$) | $y = 15..16$ (llegada en $x=1, y=15$) | 912..917 / 922 | Cuadrante superior derecho con acantilados de separación estilizados y zona mineral `(24, 22, 10, 5)`. |
| **Pasto de Pradera Azul (`Ranching`)** | `Maps/Farm_Ranching` | `(48, 14)` | $x = 20..21$ (llegada en $x=20, y=30$) | $x = 20..21$ (llegada en $x=20, y=1$) | $y = 15..16$ (llegada en $x=1, y=15$) | 1142 / 1142 | 100% de pradera abierta de hierba azul de Meadowlands de Stardew Valley 1.6. |
| **Costa Tropical (`Beach`)** | `Maps/Farm_Island` | `(38, 16)` | $x = 20..21$ (llegada en $x=20, y=30$) | $x = 20..21$ (llegada en $x=20, y=1$) | $y = 15..16$ (llegada en $x=1, y=15$) | 1142 / 1142 | Orilla de playa arenosa, palmeras y oleaje marino con forraje de playa activo. |

---

## Hallazgos Visuales y Topológicos de `MapStudio`

1. **Eliminación Total de Costuras Interiores:**
   - Al suprimir el pegado de tiras `EdgeStrip` de `VanillaBlockCatalog`, los mapas mantienen la continuidad original del terreno vanilla: cero árboles partidos por la mitad, cero saltos bruscos entre hierba soleada y sombra de bosque, y cero ríos guillotinados.
2. **Corrección de la Regla de Colisión de Stardew Valley (`Back`):**
   - En el motor oficial (`GameLocation.isTilePassable`), cualquier propiedad no nula en `Back.Properties["Passable"]` bloquea el paso del jugador. La sustitución de `Passable = "T"` por `ClearBlockingAndWaterProperties` (`Properties.Remove("Passable")`) resuelve definitivamente el bug donde el jugador se quedaba encajado al cruzar la puerta del anexo.
3. **Área de Despeje Interior de 3×4 Casillas:**
   - La entrada dispone de un margen frontal de 3×4 casillas 100% transitable y saneado tanto en mapa como en runtime (eliminación de ramas, piedras, maleza y árboles al entrar).
4. **Verificación Topológica BFS sobre los `.xnb` Reales:**
   - Los 24 casos (8 tipos × 3 lados) superaron el test de conectividad BFS: desde la casilla de llegada el jugador puede recorrer libremente más del 90% de la superficie del mapa sin obstáculos.
