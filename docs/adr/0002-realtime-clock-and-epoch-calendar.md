# ADR-0002: Reloj 1:1 (`0600–2550`) con `NightHold` y Calendario Derivado de `CalendarEpoch`

* **Estado:** Aceptado
* **Fecha:** 2026-10-29

## Contexto

En `Game1.performTenMinuteClockUpdate()` (línea 5896) el motor limita `timeOfDay = Math.Min(timeOfDay, 2600)`, y en `Game1.Update()` (línea 6452) evalúa en cada frame `if (timeOfDay >= 2600 || player.stamina <= -15f)` para forzar `player.startToPassOut()`. Además, `Farmer.passOutFromTired` no distingue si el desmayo fue provocado por el reloj o por agotamiento de energía.

Por otro lado, si el calendario del servidor simplemente incrementa un día en cada Rollover, cualquier caída del servidor durante las `06:00 AM` desalinearía para siempre los días de la semana del juego respecto a la semana real.

## Decisión

1. **Día de juego alineado a `06:00` real (`DayStartTime = "06:00"`):** A las `06:00 AM` reales el reloj arranca en `0600` y avanza 1:1 cada 10 minutos reales hasta las `01:50 AM` (`2550`).
2. **Tramo de Madrugada (`NightHoldStartTime = "01:50"`):** Entre las `01:50 AM` y las `06:00 AM` reales, `GameClock.Read` devuelve `TimeOfDay = 2550, IsNightHold = true`. Al no llegar nunca a `2600`, el motor jamás fuerza el desmayo por horario sin necesidad de parchear `Farmer.passOutFromTired`. En el Rollover de las `06:00 AM`, `Utility.CalculateMinutesUntilMorning(2550)` descuenta de forma nativa los 250 minutos restantes sobre todas las máquinas.
3. **Calendario perpetuo por Época (`ServerCalendar`):** La fecha de juego (`TotalDays`) se calcula siempre como `(fechaRealDelDíaDeJuego - CalendarEpoch).Days`, donde `CalendarEpoch` es un lunes real. Si el servidor estuvo apagado, `ServerCalendar.PlanCatchUp` determina cuántos Rollovers recuperar al arrancar.

## Consecuencias

* **Positivas:** Cero parches invasivos sobre `passOutFromTired`; las máquinas procesan exactamente 1440 minutos por día real; el Día 1 de cada estación es siempre lunes aunque el servidor haya estado caído.
* **Negativas:** Entre las `01:50 AM` y las `06:00 AM` el reloj visual del HUD permanece en `01:50 AM` (`2550`).
