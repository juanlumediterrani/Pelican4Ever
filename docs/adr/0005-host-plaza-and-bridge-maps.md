# ADR-0005: Plaza Central en la Granja Host y Mapas Puente Declarativos

* **Estado:** Propuesto
* **Fecha:** 2026-11-03

## Contexto

Las 16 residencias se reparten entre la Granja Host (`Farm`, 80×65) y tres mapas puente encadenados (`Pelican4Ever_Bridge_1..3`, 35×30), como hace el commit `019235f` de la rama `refactor/residence-entrances`:

* **Granja Host:** cinco salidas propias, que son cuatro portales (norte-oeste, norte-este, sur-oeste y sur-este) y un paso al oeste (`x=0`, `y=26..27`) hacia el puente 1.
* **Cada puente:** cuatro portales (dos al norte y dos al sur). Su borde oeste lleva al puente siguiente y su borde este, al anterior o a la Granja Host.

Esa versión funciona en el host, pero choca con varias restricciones del motor y de este proyecto:

1. Los puentes se crean en caliente con `Game1.locations.Add`, sin entrada en `Data/Locations`. El ADR-0003 ya descartó ese camino: el guardado no reconstruye la ubicación, y cada cliente crea **su propia copia local** de cada puente, que no se sincroniza por red. Además, `EnsureHostFarmPortals` también se ejecuta en los clientes.
2. El puente es una copia de `Maps/BusStop` con `mapPath = "Maps/BusStop"`. Cualquier `reloadMap` o `updateWarps` vuelve a cargar los warps vanilla de la parada del autobús (`Farm`, `Town`, `Backwoods`) y borra los warps que añade el mod.
3. La geometría de cada portal se deduce de umbrales sueltos en las coordenadas (`>= 60`, `>= 28`, `>= 25`, `<= 2`, `>= 75`). Esos umbrales no coinciden entre `SpatialWarpRouter` y `TerrainTileClassifier`, y mezclan las medidas de la Granja Host (80×65) con las de los puentes (35×30).
4. Varias rutas del mod solo reconocen la ubicación `"Farm"`. Por eso en los puentes no se abre el diálogo para fundar una residencia, no se aplica el parche de colisión de las aperturas y no se refrescan los portales al recibir la sincronización.

## Decisión

1. **Puentes declarativos.** Cada puente `Pelican4Ever_Bridge_N` tendrá su entrada en `Data/Locations` (`CreateOnLoad`, tipo `GameLocation`, exterior) y su mapa propio `Maps/Pelican4Ever_Bridge_N`. Ese mapa se generará en `AssetRequested` igual que `Maps/Pelican4Ever_HostFarm`. Su número sale de `FarmSlotCount`: `ceil((FarmSlotCount − 4) / 4)`, entre 0 y 3.
2. **Mapa del puente limpio y autosuficiente.** Al generarlo:
   * Se quitan la propiedad `Warp` y las acciones de tile vanilla de la plantilla (autobús, máquina de billetes y similares).
   * Se tallan en el propio mapa las aperturas de los portales y los pasos este y oeste.
   * Los warps de la cadena se escriben en la propiedad `Warp` del mapa, para que sobrevivan a `reloadMap`. En tiempo de ejecución solo se añaden o quitan los warps de las parcelas reclamadas.
3. **Geometría explícita en Core.** Un catálogo puro (`HostPlazaLayout`) describirá cada ubicación de la plaza: nombre, ancho, alto y pasos encadenados. Cada portal declarará su borde (`Edge`: `North`, `South`, `West` o `East`) en `doors.json`. Las funciones del router y del clasificador recibirán el borde y las medidas de la ubicación en lugar de deducirlos de umbrales.
4. **Ubicación de la plaza en vez de `"Farm"`.** La detección del portal, la colisión, el refresco tras la sincronización y la vuelta desde la residencia trabajarán sobre «ubicación de la plaza» (`HostPlazaLayout.IsPlazaLocation`), no sobre el nombre `"Farm"`.

## Consecuencias

* **Positivas:**
  * Los puentes se guardan y se recargan.
  * Los clientes y el host comparten la misma ubicación, sincronizada por red.
  * Ningún paso lleva a un mapa vanilla.
  * La geometría se prueba con `dotnet test` sin abrir el juego.
* **Negativas:**
  * Hay que migrar `doors.json`, porque cambia su forma.
  * Las partidas que ya tengan puentes creados en caliente los perderán al cargar. No contienen nada del jugador, pero hay que comprobar que nadie se quede guardado dentro: se le reubica en la Granja Host.
  * Hay que verificar en partida la plantilla de `BusStop` (zonas transitables en las filas 13–15 y en los puntos de los portales), porque no se puede comprobar fuera del juego.
