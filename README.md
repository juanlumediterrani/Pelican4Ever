# Pelican 4Ever (`SV-RTMP`)

Mod para **Stardew Valley 1.6.15** (SMAPI 4.1+) que transforma el modo multijugador en un **mundo persistente en tiempo real (1:1)** pensado para un servidor dedicado 24/7 de hasta **4 jugadores (MVP)** con granjas privadas conectadas a puertas de Pelican Town.

## Documentación

* [Diseño del Juego (`design.md`)](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/design.md)
* [Arquitectura Técnica (`architecture.md`)](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/architecture.md)
* [Backlog por Iteraciones (`backlog.md`)](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/backlog.md)
* [Decisiones de Arquitectura (`docs/adr/`)](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/docs/adr)

## Estructura de la Solución

* **`Pelican4Ever.Core`**: Lógica de dominio pura (`net6.0`, sin dependencias de Stardew Valley ni SMAPI): reloj 1:1 con `NightHold`, calendario perpetuo basado en `CalendarEpoch`, máquina de sueño asíncrono (`SleepPolicy`), presupuesto diario de saciedad (`SatietyBudget`) y validación de configuración (`ServerConfig`).
* **`Pelican4Ever`**: Mod SMAPI (`net6.0`) con registro modular (`IModule`), persistencia en el Host (`PersistenceModule`), verificación de versión en clientes (`ClientVersionGuardModule`) y comando de diagnóstico `p4e_status`.
* **`Pelican4Ever.Tests`**: Suite de tests unitarios con xUnit (`net8.0`).

## Compilación y Tests

1. Si tu instalación de Steam no está en la ruta estándar, copia `local.props.example` a `local.props` y ajusta `<GamePath>`.
2. Compila y despliega automáticamente el mod en `Mods/Pelican4Ever`:
   ```bash
   dotnet build Pelican4Ever.sln
   ```
3. Ejecuta la suite de tests unitarios:
   ```bash
   dotnet test Pelican4Ever.Tests
   ```
