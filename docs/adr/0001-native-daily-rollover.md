# ADR-0001: Rollover Diario Mediante el Pipeline Nativo (`NewDay` + `Save`) a las 06:00 AM

* **Estado:** Aceptado
* **Fecha:** 2026-10-29

## Contexto

El diseño inicial proponía ejecutar a las `05:30 AM` un bucle manual que incrementase `Game1.dayOfMonth` e invocase `location.DayUpdate(dayOfMonth)` "en caliente" sin pasar por `Game1.NewDay()`.

Al decompilar `Stardew Valley.dll` (1.6.15), se verificó que la transición real de día (`Game1.NewDay` → `_newDayAfterFade`) ejecuta decenas de subsistemas acoplados: liquidación de `personalShippingBin` por jugador, reparto de correo, cálculo de suerte y clima, avance de misiones y `SpecialOrders`, actualización de amistad, animales, árboles frutales, `stats.DaysPlayed`, `netWorldState` y la corrutina `SaveGame.Save()`, que requiere sincronizar a todos los clientes conectados.

## Decisión

1. Ejecutar el Rollover diario a las **06:00 AM** invocando el **flujo nativo de fin de día (`Game1.NewDay`) y guardado (`SaveGame.Save`)**.
2. Emitir avisos en el HUD a los jugadores conectados (`15`, `5` y `1` minuto antes) y asumir por diseño que quienes sigan dentro a las `06:00 AM` verán la pantalla nativa de transición y guardado durante unos segundos.
3. Aplicar las reglas de sueño asíncrono (`SleepPolicy`) inmediatamente después del cambio de día y generar un backup rotativo del guardado en disco.

## Consecuencias

* **Positivas:** Compatibilidad total con Stardew Valley 1.6 y otros mods; cero riesgo de olvidar subsistemas internos del cambio de día; guardado XML íntegro todos los días.
* **Negativas:** Existe una pausa visible de unos segundos a las `06:00 AM` para los jugadores que estén conectados en ese instante exacto.
