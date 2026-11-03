# ADR-0005: Plaza Central en la Granja Host y Mapas Puente Declarativos

* **Estado:** Aceptado
* **Fecha:** 2026-11-03

## Contexto

Las 16 residencias se reparten entre la Granja Host (`Farm`, 80×65) y tres mapas puente encadenados (`Pelican4Ever_Bridge_1..3`, 35×30), como proponía la rama `refactor/residence-entrances`:

* **Granja Host:** cinco salidas propias, que son cuatro portales (norte-oeste, norte-este, sur-oeste y sur-este) y un paso al oeste (`x=0`, `y=26..27`) hacia el puente 1.
* **Cada puente:** cuatro portales (dos al norte y dos al sur). Su borde oeste lleva al puente siguiente y su borde este, al anterior o a la Granja Host.

Las implementaciones iniciales en prototipo creaban los puentes en caliente con `Game1.locations.Add` y dependían de copias dinámicas no sincronizadas. Esto presentaba graves limitaciones:

1. Los puentes no se persistían en `SaveGame.loadDataToLocations` y cada cliente generaba su propia copia desconectada.
2. Al apoyarse en copias en memoria de `Maps/BusStop`, cualquier recarga de mapa reinsertaba warps vanilla hacia paradas de autobús.
3. La geometría se basaba en umbrales empíricos desincronizados entre subsistemas.
4. Diversas rutinas solo reconocían el nombre `"Farm"`.

## Decisión

1. **Puentes declarativos.** Cada puente `Pelican4Ever_Bridge_N` tiene su entrada formal en `Data/Locations` (`CreateOnLoad`, tipo `GameLocation`, exterior con `Nature.NIV.Outside = "true"`) y su mapa propio `Maps/Pelican4Ever_Bridge_N`. Se genera determinísticamente en `AssetRequested` (`MapCloneHelper.CreateBridgeMap`). Su número de instancias depende de `FarmSlotCount`: $\lceil(\text{FarmSlotCount} - 4) / 4\rceil$, entre 0 y 3 puentes (para soportar de 4 a 16 parcelas).
2. **Mapa del puente limpio y determinista.** Al generarlo:
   * Se elimina la propiedad `Warp` y las acciones de tile vanilla de la plantilla (autobús, máquina de billetes, cartel).
   * Se tallan las aperturas de portales y los pasos este y oeste.
   * Los warps de tránsito inter-mapa se escriben declarativamente en la propiedad `Warp` del mapa (`PlazaTopologyVerifier.BuildPassageWarpString`), garantizando persistencia ante cualquier recarga.
3. **Geometría explícita en Core.**
   * `HostPlazaLayout`: Describe cada ubicación de la plaza (ancho, alto, profundidad de camino norte/sur y pasos encadenados).
   * `DoorCatalogValidator`: Valida la integridad del catálogo de 16 portales en `doors.json`.
   * `SpatialWarpRouter`: Enruta las llegadas y retornos de forma biunívoca según el borde cardinal (`PortalEdge`).
   * `PlazaTopologyVerifier`: Proporciona utilidades puras para el formateo/parseo de warps, áreas de despeje interior de 3×4 y resolución determinista de retornos con fallback seguro.
4. **Generalización de la plaza.** Todas las mecánicas de portales, colisiones, mensajes de bienvenida y comandos `/residencias` operan sobre el concepto generalizado de «ubicación de la plaza» (`HostPlazaLayout.IsPlazaLocation`).

## Hallazgos Empíricos sobre Mapas XNB Reales

Durante la implementación y verificación con los archivos reales de Stardew Valley 1.6 (`Content/Maps/Farm.xnb` y `Content/Maps/BusStop.xnb`) se determinaron los siguientes parámetros exactos:

1. **ViewportClamp en `BusStop`:** El archivo `Maps/BusStop.xnb` tiene un tamaño físico de `65×30`, pero incluye la propiedad nativa de Stardew Valley `ViewportClamp = "10 0 35 30"`. Esto demostró que el área útil y canónica del mapa es exactamente el rectángulo de `35×30` con origen en `(10, 0)`, descartando las columnas vacías vanilla de ambos extremos.
2. **Profundidad de Camino Norte (`NorthPathDepth`):**
   * En `Farm`, el acantilado rocoso septentrional desciende hasta `y = 7..9`, requiriendo `NorthPathDepth = 10` para alcanzar la llanura transitable.
   * En los puentes (`BusStop`), la franja superior de árboles y vallas es más estrecha, requiriendo `NorthPathDepth = 5` para conectar con el patio central.
3. **Paso Oeste en Granja Host:** El corredor oeste hacia el Puente 1 se ubica en `x = 0..7, y = 26..27`. Se despeja el cerezo estático en `AlwaysFront` (`x = 1..6, y = 23..27`) para evitar que copas flotantes obstruyan visualmente el paso al cruzar hacia el puente.
4. **Sellado de Antigua Carretera:** En los puentes, la antigua calzada pavimentada de la parada (`y = 22..25`) se sella con vallas de madera en los extremos `x = 0` y `x = 34` para impedir caídas accidentales al vacío.

## Consecuencias y Verificación

* **Persistencia y Red:** Los puentes y portales se serializan limpiamente en el guardado y se sincronizan de forma idéntica entre host y clientes.
* **Resiliencia ante desescalado:** Si el administrador reduce `FarmSlotCount` (ej. de 16 a 4), la Granja Host cierra su paso oeste y cualquier retorno desde un puente inactivo se redirige de forma segura a la Granja Host (`64, 16`) sin atrapar a ningún jugador.
* **Verificación Automatizada (`TASK-1401` y `TASK-1403`):**
  * Verificado mediante suite de 793 pruebas unitarias sin dependencias externas en `Pelican4Ever.Tests`.
  * Verificado topológicamente mediante recorrido **BFS (flood-fill)** sobre los assets `.xnb` reales de Stardew Valley 1.6 (`Maps/Farm.xnb` y `Maps/BusStop.xnb`), confirmando conectividad transitable del 100% en todos los portales (01..16), casillas de retorno, áreas de despeje de 3×4 y pasos de enlace Este/Oeste (3857 casillas transitables en Farm, 695 en Puente 1, 695 en Puente 2 y 684 en Puente 3).
