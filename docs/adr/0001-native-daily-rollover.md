# ADR-0001: Rollover nativo con fecha a las 02:00 y amanecer a las 06:00

* **Estado:** Aceptado
* **Fecha:** 2026-11-02 (revisión)

## Contexto

El diseño inicial proponía ejecutar a las `05:30 AM` un bucle manual que incrementase `Game1.dayOfMonth` e invocase `location.DayUpdate(dayOfMonth)` "en caliente" sin pasar por `Game1.NewDay()`.

Al decompilar `Stardew Valley.dll` (1.6.15), se verificó que la transición real de día (`Game1.NewDay` → `_newDayAfterFade`) ejecuta decenas de subsistemas acoplados: liquidación de `personalShippingBin` por jugador, reparto de correo, cálculo de suerte y clima, avance de misiones y `SpecialOrders`, actualización de amistad, animales, árboles frutales, `stats.DaysPlayed`, `netWorldState` y la corrutina `SaveGame.Save()`, que requiere sincronizar a todos los clientes conectados.

## Decisión

1. La fecha de calendario y el rollover ocurren a las **02:00 AM**; `DayStartTime` se mantiene en **06:00 AM** para el reloj, la luz y los horarios de Pelican.
2. A las 01:50 se avisa una vez. A las 02:00 se ejecuta `SaveGame.Save`, se copia el save a un backup host temporal y se verifican sus archivos por SHA-256. Si falla cualquiera de esos pasos, no se desconecta a nadie ni se avanza el día.
3. Tras backup verificado, el host solicita a los clientes el cierre individual. Si persisten conectados tras 20 s, se envía el cierre nativo del peer; el host espera su desconexión confirmada hasta 10 s más. Si siguen conectados, aborta el cambio de día en vez de entrar en un ReadyCheck bloqueado.
4. Solo con cero peers se invoca `Game1.NewDay(0f)`. Las consecuencias del día cerrado se procesan tras `DayStarted`; el reloj sigue en NightHold hasta las 06:00. El cierre de peers aún requiere ensayo de desarrollo antes de cualquier rollout.

## Consecuencias

* **Positivas:** El backup del estado conectado se obtiene antes de cerrar clientes; no se invoca el cambio nativo mientras un ReadyCheck queda esperando peers; fecha y amanecer tienen contratos distintos.
* **Negativas:** Si el cierre de peer no se confirma, la secuencia queda fallida y el día no avanza; se necesita observación/reparación y no se reintenta automáticamente.
