# Observaciones de CPU y memoria del host — 2–9 de noviembre de 2026

Anotación para la revisión del límite de fotogramas. Fuente: resúmenes de contabilidad de systemd del servicio del host y una lectura puntual de sus contadores. Horas en Europe/Madrid. Las cifras son del **servicio completo**, no de un hilo, función o proceso individual; pueden incluir procesos auxiliares.

## Ejecuciones anteriores

Cada fila resume una ejecución finalizada en la fecha indicada, no un día completo. La CPU media se calcula como tiempo de CPU acumulado / tiempo real: 100 % equivale a un núcleo lógico ocupado de media, de modo que puede superar el 100 %. La memoria indicada es el pico registrado por systemd para la ejecución, no el promedio ni el RSS de un proceso.

| Fin | Tiempo real | Tiempo de CPU | CPU media aproximada | Pico de memoria | Pico de swap |
|---|---:|---:|---:|---:|---:|
| 2 nov. | 4 h 51 min | 16 h 50 min | 347 % | 1,9 G | 1,4 G |
| 4 nov. | 14 h 25 min | 2 d 15 h 1 min | 437 % | 2,2 G | 952,1 M |
| 5 nov. | 18 h 13 min | 3 d 19 h 22 min | 502 % | 2,2 G | 971,5 M |
| 6 nov. | 10 h 33 min | 2 d 5 h 35 min | 508 % | 2,5 G | 1,2 G |
| 7 nov. | 11 h 39 min | 2 d 9 h 34 min | 494 % | 1,7 G | 1 G |
| 8 nov. | 24 h 21 min | 3 d 3 h 57 min | 312 % | 1,7 G | 1,3 G |

Son ejemplos de ejecuciones largas disponibles en el journal, no una serie continua ni un promedio semanal. Los picos son valores reportados por systemd; no tienen por qué ocurrir simultáneamente.

## Lectura posterior al límite de 30 FPS

El journal registra «Tope de 30 fps aplicado al bucle del juego» el 8 de noviembre a las 04:50 y de nuevo a las 11:35. Para el servicio iniciado a las 11:35:16, la lectura del 9 de noviembre a las 01:42:01 muestra:

- Tiempo acumulado de CPU: aproximadamente 48 h 21 min durante 14 h 7 min de tiempo transcurrido, equivalente a **343 %** de un núcleo lógico de media.
- Memoria actual: **815 513 600 bytes**; pico registrado: **2 348 367 872 bytes**.

La lectura es puntual y abarca toda la ejecución del servicio, incluido el arranque. No hay aquí datos correlacionados de jugadores, carga, configuración, cambios de versión o reparto por proceso; por tanto **no demuestra cuánto ahorra el límite** ni permite comparar directamente antes y después. Tampoco demuestra ausencia de renderizado con el servidor vacío: este PR no implementa esa desactivación. Para medir el efecto hacen falta muestras comparables, con y sin límite, mismas condiciones y número de jugadores, y CPU/memoria del servicio y del proceso del juego por separado.
