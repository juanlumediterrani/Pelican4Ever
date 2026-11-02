# ADR-0002: Reloj 1:1 (`0600–2550`) con `NightHold` y Calendario Derivado de `CalendarEpoch`

* **Estado:** Aceptado
* **Fecha:** 2026-10-29

## Contexto

En `Game1.performTenMinuteClockUpdate()` (línea 5896) el motor limita `timeOfDay = Math.Min(timeOfDay, 2600)`, y en `Game1.Update()` (línea 6452) evalúa en cada frame `if (timeOfDay >= 2600 || player.stamina <= -15f)` para forzar `player.startToPassOut()`. Además, `Farmer.passOutFromTired` no distingue si el desmayo fue provocado por el reloj o por agotamiento de energía.

Por otro lado, si el calendario del servidor simplemente incrementa un día en cada Rollover, cualquier caída del servidor durante el corte de fecha desalinearía para siempre los días de la semana del juego respecto a la semana real.

## Decisión

1. **Reloj y horarios alineados a `06:00` real (`DayStartTime = "06:00"`):** A las `06:00 AM` reales el reloj arranca en `0600` y avanza 1:1 cada 10 minutos reales hasta las `01:50 AM` (`2550`).
2. **Tramo de Madrugada (`NightHoldStartTime = "01:50"`):** Entre las `01:50 AM` y las `06:00 AM` reales, `GameClock.Read` devuelve `TimeOfDay = 2550, IsNightHold = true`. El motor no llega a `2600`; el cambio de fecha a las 02:00 no altera el inicio de luz ni los horarios de Pelican a las 06:00.
3. **Calendario perpetuo por Época (`ServerCalendar`):** La fecha de juego (`TotalDays`) se calcula como `(fechaRealTrasCalendarDayChangeTime - CalendarEpoch).Days`, con corte fijo a las 02:00 y una época que es lunes. Entre medianoche y las 02:00 sigue la fecha anterior. Si el servidor estuvo apagado, `ServerCalendar.PlanCatchUp` detecta el desfase.

## Consecuencias

* **Positivas:** Cero parches invasivos sobre `passOutFromTired`; las máquinas procesan exactamente 1440 minutos por día real; el Día 1 de cada estación es siempre lunes aunque el servidor haya estado caído.
* **Negativas:** Entre las `01:50 AM` y las `06:00 AM` el reloj visual del HUD permanece en `01:50 AM` (`2550`); la fecha ya es la nueva desde las 02:00.
