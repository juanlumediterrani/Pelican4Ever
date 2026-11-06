# ADR-0004: Autoridad en el Host, Mod Obligatorio en Clientes y Presupuesto de Saciedad

* **Estado:** Aceptado. El punto 3 (presupuesto diario de Saciedad) está sustituido por [ADR-0006](0006-session-energy-satiety-wellfed-spa.md).
* **Fecha:** 2026-10-29

## Contexto

En la capa de red de Stardew Valley, cada `Farmhand` es propietario de su propio `Farmer` (`NetRoot<Farmer>`) y ejecuta en su cliente `Farmer.doneEating`, el uso de herramientas y los diálogos. Si el Host escribe directamente en `farmer.modData` de un cliente conectado, se producen condiciones de carrera con la replicación del cliente.

Asimismo, eliminar por completo la energía instantánea de la comida dejaba sin utilidad la cocina para los jugadores activos que quieren hacer algo más que agotar la barra base de 270 de energía.

## Decisión

1. **Mod obligatorio en Host y Clientes (`ClientVersionGuardModule`):** El Host verifica en `PeerContextReceived` que el cliente tenga `Pelican 4Ever` con la misma versión `Major.Minor` y expulsa en el siguiente tick a quien no cumpla el requisito.
2. **Fuente única de la verdad en el Host (`ServerWorldState`):** El estado de los jugadores (`PlayerRecord`) y de las puertas (`DoorAssignment`) se almacena únicamente en `ServerWorldState` en el Host. `GameLocation.modData` se usa solo como vista replicada de lectura para los clientes.
3. **Presupuesto diario de Saciedad (`SatietyBudget`):** La comida sigue devolviendo energía instantánea además de vida y buffs, pero limitada a un presupuesto diario (`DailySatietyFraction = 0.5`, es decir, +50% de la barra máxima por día real), complementado por hasta 2 buffs diarios de regeneración pasiva en platos cocinados y un tope diario en el Spa (`SpaDailyCap = 60`).
4. **Registro modular aislado (`IModule`):** Toda la lógica pura reside en `Pelican4Ever.Core` (sin referencias al juego y probada con xUnit) y cada subsistema SMAPI/Harmony implementa `IModule` con aislamiento de errores al registrarse.

## Consecuencias

* **Positivas:** Sin condiciones de carrera en `Farmer.modData`; la cocina conserva su valor estratégico dentro de un techo diario controlado; toda la lógica de negocio se prueba con `dotnet test` sin abrir el juego.
* **Negativas:** Todos los jugadores deben instalar el mod en su cliente para entrar al servidor.
