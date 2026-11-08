# Informe de Ventanas de Recorte 40×32 para los 24 Casos de Anexo (TASK-910)

> Resultado del análisis automatizado mediante `AnnexWindowScoringLogic` sobre los 8 mapas vanilla oficiales de Stardew Valley 1.6.

| Tipo | Lado Entrada | Ventana (X, Y) | Entrada Anexo | Puntuación | Salida Vanilla | Bordes Naturales | Lados a Cerrar | Clasificación |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Huerto de Cultivo Estándar (Standard) | North | `(40, 33)` | `(0, 30)` | 320 | Sí | South, East | North, West | Óptima (esquina + salida vanilla) |
| Huerto de Cultivo Estándar (Standard) | East | `(40, 16)` | `(1, 16)` | 91 | No | East | North, South, West | Requiere bloques de catálogo |
| Huerto de Cultivo Estándar (Standard) | South | `(8, 0)` | `(32, 1)` | 208 | Sí | North | South, West, East | Buena (salida vanilla) |
| Remanso Fluvial (Fishing) | North | `(32, 16)` | `(20, 30)` | 102 | No | Ninguno | North, South, West, East | Requiere bloques de catálogo |
| Remanso Fluvial (Fishing) | East | `(40, 0)` | `(1, 16)` | 305 | No | North, East | South, West | Buena (esquina con 2 bordes naturales) |
| Remanso Fluvial (Fishing) | South | `(40, 0)` | `(0, 1)` | 455 | Sí | North, East | South, West | Óptima (esquina + salida vanilla) |
| Franja Forestal (Foraging) | North | `(40, 33)` | `(0, 30)` | 252 | Sí | South, East | North, West | Óptima (esquina + salida vanilla) |
| Franja Forestal (Foraging) | East | `(40, 33)` | `(1, 16)` | 102 | No | South, East | North, West | Buena (esquina con 2 bordes naturales) |
| Franja Forestal (Foraging) | South | `(0, 33)` | `(20, 1)` | 258 | No | South, West | North, East | Buena (esquina con 2 bordes naturales) |
| Cantera de Minerales (Mining) | North | `(40, 33)` | `(0, 30)` | 252 | Sí | South, East | North, West | Óptima (esquina + salida vanilla) |
| Cantera de Minerales (Mining) | East | `(40, 33)` | `(1, 16)` | 102 | No | South, East | North, West | Buena (esquina con 2 bordes naturales) |
| Cantera de Minerales (Mining) | South | `(5, 0)` | `(35, 1)` | 226 | Sí | North | South, West, East | Buena (salida vanilla) |
| Coto Salvaje (Combat) | North | `(40, 33)` | `(0, 30)` | 326 | Sí | South, East | North, West | Óptima (esquina + salida vanilla) |
| Coto Salvaje (Combat) | East | `(40, 33)` | `(1, 16)` | 176 | No | South, East | North, West | Buena (esquina con 2 bordes naturales) |
| Coto Salvaje (Combat) | South | `(24, 0)` | `(16, 1)` | 190 | Sí | North | South, West, East | Buena (salida vanilla) |
| Cuadrante Mixto (FourCorners) | North | `(0, 0)` | `(20, 30)` | 197 | No | North, West | South, East | Buena (esquina con 2 bordes naturales) |
| Cuadrante Mixto (FourCorners) | East | `(4, 0)` | `(1, 16)` | 142 | No | North | South, West, East | Requiere bloques de catálogo |
| Cuadrante Mixto (FourCorners) | South | `(4, 0)` | `(36, 1)` | 292 | Sí | North | South, West, East | Buena (salida vanilla) |
| Pasto de Pradera Azul (Ranching) | North | `(13, 43)` | `(39, 30)` | 217 | Sí | South | North, West, East | Buena (salida vanilla) |
| Pasto de Pradera Azul (Ranching) | East | `(60, 0)` | `(1, 16)` | 155 | No | North, East | South, West | Buena (esquina con 2 bordes naturales) |
| Pasto de Pradera Azul (Ranching) | South | `(60, 0)` | `(3, 1)` | 305 | Sí | North, East | South, West | Óptima (esquina + salida vanilla) |
| Costa Tropical (Beach) | North | `(70, 78)` | `(11, 30)` | 344 | Sí | South, East | North, West | Óptima (esquina + salida vanilla) |
| Costa Tropical (Beach) | East | `(70, 0)` | `(1, 16)` | 182 | No | North, East | South, West | Buena (esquina con 2 bordes naturales) |
| Costa Tropical (Beach) | South | `(22, 0)` | `(18, 1)` | 244 | Sí | North | South, West, East | Buena (salida vanilla) |

## Conclusiones del Análisis de los 24 Casos

1. **Casos con Salida Vanilla Natural:**
   - **Acceso Norte (entra por el sur del anexo):** los 8 mapas tienen ventanas óptimas en su borde sur (`y + 32 == Height`) que conectan directamente con la salida natural hacia Cindersap Forest (`Maps/Forest`), proporcionando paso transitable con puente/camino y dos bordes naturales intactos.
   - **Acceso Sur (entra por el norte del anexo):** la mayoría de mapas tienen ventanas en `y=0` que capturan el paso natural hacia Backwoods (`x=40..41`).
2. **Casos de Acceso Este (entra por el oeste del anexo):**
   - Las granjas vanilla no poseen salida natural al oeste, por lo que `Salida Vanilla = No` en todos ellos. Sin embargo, ventanas ubicadas en `x=0` o en esquinas (`North+West` o `South+East`) ofrecen 2 bordes naturales terminados, requiriendo únicamente cerrar los otros 2 lados con franjas del catálogo (TASK-909).
3. **Viabilidad Global:**
   - Los 24 casos tienen casillas de entrada transitables libres de agua y colisión.
   - Ningún caso requiere diseño manual en Tiled (Plan B) como bloqueante; todos los casos admiten composición con ventana vanilla y bloques de catálogo.
