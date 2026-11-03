# ADR-0003: 4 Huecos de Granja Declarativos (`Data/Locations`) con Plantilla Estándar

* **Estado:** Aceptado
* **Fecha:** 2026-10-29

## Contexto

El diseño original preveía instanciar ubicaciones con `new Farm(mapPath, $"Farm_{player.UniqueMultiplayerID}")` bajo demanda y permitir que cada jugador eligiese entre los 8 tipos de granja del juego.

La inspección de `Stardew Valley.dll` reveló tres restricciones del motor:
1. En `SaveGame.loadDataToLocations`, el motor reconstruye las ubicaciones a partir de `Data/Locations` (`CreateOnLoad`) antes de volcar los datos del XML. Las ubicaciones creadas en caliente sin entrada en `Data/Locations` corren riesgo de descartarse al recargar.
2. Las mecánicas específicas de cada tipo de granja (forraje de Bosque, minerales de Colina, monstruos de Salvaje) consultan el campo global `Game1.whichFarm`, no la instancia de `Farm`.
3. `GameLocation.CanBuildHere()` (línea 2459) exige que `Game1.multiplayer.isAlwaysActiveLocation(this)` sea `true`, lo que sincroniza cada granja a todos los clientes conectados.

## Decisión

1. Acotar el MVP a **4 jugadores (`FarmSlotCount = 4`)**, todos con la **plantilla de granja Estándar (`Maps\Farm`)**.
2. Registrar de forma declarativa en `Data/Locations` (vía `Helper.Events.Content.AssetRequested`) las 4 ubicaciones fijas `Pelican4Ever_Farm_01` a `Pelican4Ever_Farm_04` con `CreateOnLoad` y `AlwaysActive = true`.
3. Al reclamar un portal residencial en la plaza (inicialmente proyectado en el pueblo y posteriormente consolidado en la Plaza Central y puentes mediante ADR-0005), asignar al jugador uno de los huecos fijos y reubicar su `Cabin` dentro de dicho hueco.

## Consecuencias

* **Positivas:** Guardado y recarga 100% compatibles con `SaveGame.loadDataToLocations`; Robin permite construir edificios sin parches adicionales; para 4 jugadores el tráfico de `AlwaysActive = true` es despreciable.
* **Negativas:** En el MVP todos los jugadores comparten el mismo diseño de mapa de granja (Estándar) y el límite por defecto es de 4 propietarios.
