# ADR-0011: Concurrencia Ampliada por Gestión de Interés y Tope de Jugadores por Mapa

* **Estado:** Propuesto
* **Fecha:** 2026-11-15
* **Versión objetivo:** 0.6 (rama `feat/v0.6-extended-concurrency`)
* **Depende de:** [ADR-0010](0010-scalable-residences-admission-queue.md) (admisión única, magnitudes separadas, puentes no siempre activos) y [ADR-0004](0004-host-authority-client-mod-and-satiety.md) (mod obligatorio en clientes).

## Contexto

Con 32 o 64 residencias (ADR-0010) el siguiente límite es la simultaneidad: 16 jugadores a la vez. Se quiere superar ese número con un tope de **16 jugadores por mapa** allí donde el mapa lo necesite, sin tope fijo en los mapas ligeros.

Hechos verificados sobre el decompilado de Stardew Valley 1.6.15:

1. **No hay tope de jugadores en el motor** más allá de `MaximumConnections` de Lidgren y de una cabaña por peón (ADR-0010).
2. **El host reenvía a todos, sin mirar la ubicación.** `GameServer.processIncomingMessage` llama a `rebroadcastClientMessage(message, Multiplayer.AllPlayers)` para todo tipo de `Multiplayer.isClientBroadcastType`, entre ellos el `0` (delta de jugador) y el `6` (delta de ubicación). Cada cliente emite su delta cada 3 ticks (`farmerDeltaBroadcastPeriod`). Con *n* clientes son *n·(n−1)* flujos: 240 con 16, 4032 con 64.
3. **Las ubicaciones siempre activas se envían a todos.** `Multiplayer.broadcastLocationMessage` manda los deltas de una ubicación siempre activa a todo `Game1.otherFarmers`; `GameServer.checkFarmhandRequest` envía todas ellas completas al conectar. Con el culling actual, cada residente conectado mantiene activas sus hasta 7 ubicaciones **para todos los clientes**: con 64 conectados, unas 450.
4. **Las ubicaciones no siempre activas ya tienen gestión de interés.** El mismo método envía sus deltas solo a los jugadores que están dentro, y el host la reenvía completa a quien entra. `Town` funciona así.
5. **El host es un cliente completo en un solo hilo:** actualiza en cada tick toda ubicación activa.
6. **La entrada a un mapa pasa por el host.** El cliente pide la ubicación con el mensaje tipo `5`, que `GameServer` atiende en `warpFarmer`.

Decisiones de producto tomadas:

* En `Town` y en los puentes se intenta **subir el tope por encima de 16**: sincronizan sobre todo jugadores y posiciones, apenas objetos. Las restricciones de acciones o de aforo se irán aplicando según lo que se mida.
* En los mapas que den problemas, el tope es **16**.
* Si la solución encarece de forma apreciable los requisitos del host, debe poder **activarse o desactivarse desde la configuración**.

## Decisión

### 1. Rasgo opcional

| Clave | Valor por defecto | Efecto |
| --- | --- | --- |
| `ExtendedConcurrency` | `false` | Apagado: el servidor se comporta exactamente como en la 0.5 y `MaxConcurrentPlayers` no pasa de `16`. |
| `MaxConcurrentPlayers` | `16` | Con el rasgo encendido admite `1..64`. |
| `DefaultLocationCapacity` | `16` | Aforo de todo mapa que no sea de alta capacidad. |
| `LocationCapacityOverrides` | `{}` | Aforo propio por nombre de ubicación; ajusta lo anterior sin tocar código. |

Con el rasgo apagado no se aplica ningún parche de esta decisión. Cambiarlo exige reiniciar el servidor.

### 2. Aforo por mapa

1. El aforo lo decide `LocationCapacityLogic` (`Pelican4Ever.Core/Networking`), consultado desde el mismo punto de admisión de ADR-0010.
2. **Mapas de alta capacidad** (sin aforo propio; rige `MaxConcurrentPlayers`): `Town`, la Plaza Central (`Farm`) y todo `Pelican4Ever_Bridge_*`.
3. **El resto de mapas** tiene aforo `DefaultLocationCapacity` (`16`): residencias y sus dependencias, anexos, interiores, minas, Caverna Calavera y cualquier otro. Un mapa pasa a alta capacidad solo tras medirlo.
4. El aforo se aplica a las **entradas voluntarias**. Nunca se deniega: la entrada de un residente a su residencia y dependencias, un traslado forzado (desmayo, cambio de día, expulsión de una residencia, fin de evento) ni un teletransporte de administración.
5. La autoridad es el host, al atender el mensaje tipo `5`. El cliente consulta antes una vista replicada de la ocupación para no iniciar el fundido de pantalla de un traslado que va a ser denegado.
6. Quien no cabe recibe un mensaje con el mapa y su ocupación; no hay cola por mapa.

### 3. Gestión de interés

**Residencias y anexos: activos por receptor.** `LocationCullingLogic.ShouldKeepActive` sigue decidiendo si el host simula y acepta construcción en la ubicación. Se añade `ShouldSendTo(receptor)`: los deltas y el envío completo al conectar llegan solo a quien está dentro y a los residentes conectados de esa residencia. Los demás clientes no reciben nada de ella hasta que entran.

**Mensajes de cliente reenviados por ubicación.**

| Mensaje | Receptores |
| --- | --- |
| Delta de ubicación (tipo `6`) | Solo los clientes que tienen esa ubicación (dentro de ella, o residentes si es una residencia). |
| Delta de jugador (tipo `0`), mismo mapa | Inmediato, como en vanilla. |
| Delta de jugador (tipo `0`), otro mapa | Diferido: el host lo entrega agrupado, como máximo cada `30` ticks (0,5 s). |
| Estado del mundo, equipo y chat | A todos, sin cambios. |

Los deltas de la red del juego son incrementales: **ninguno se descarta** para un receptor que conserva ese objeto, o su copia se desincroniza. El mecanismo de entrega diferida se elige en un spike entre tres candidatos y se anota aquí como hallazgo:

* **A.** Agrupar en orden los deltas originales y entregarlos por lotes.
* **B.** Que el host reescriba un delta propio por receptor a partir de su copia del jugador.
* **C.** Sustituir al jugador lejano por un estado compacto (nombre, mapa, apariencia) y resincronizarlo completo cuando comparte mapa.

### 4. Mapas concurridos

Dentro de un mismo mapa el coste sigue siendo cuadrático (40 jugadores en `Town` son 1560 flujos). Cuando un mapa de alta capacidad supera los 16 jugadores se aplica, por este orden y solo hasta donde las medidas lo pidan:

1. **Menos frecuencia:** el delta de jugador entre ocupantes de ese mapa pasa de cada 3 ticks a cada 6.
2. **Menos acciones:** se restringen en ese mapa las acciones que generan deltas de ubicación (colocar objetos, bombas, labrar, talar).
3. **Menos gente:** se le fija un aforo en `LocationCapacityOverrides`.

### 5. Apertura por escalones medidos

`MaxConcurrentPlayers` se valida en cuatro escalones: **24, 32, 48 y 64**. Un escalón se documenta como soportado solo si, en el host de referencia y con esa carga en el banco de pruebas:

* el tick del host se mantiene en **16 ms** o menos en el percentil 95; y
* ningún cliente acumula cola de envío creciente durante 30 minutos.

Los requisitos de CPU, memoria y ancho de banda de subida de cada escalón se publican en la guía de despliegue. La telemetría que lo mide queda disponible en `AdminApi` también en producción.

## Consecuencias

* **Positivas:** el tráfico deja de crecer con el cuadrado del total de conectados y pasa a crecer con el cuadrado de los que comparten mapa; un cliente deja de recibir las residencias ajenas; el aforo por mapa es un dato de configuración que se ajusta tras cada medición sin tocar código.
* **Negativas:** el protocolo entre host y clientes se aparta del de vanilla (ya era obligatorio el mismo mod en ambos lados); el estado de los jugadores de otros mapas llega con hasta 0,5 s de retraso; aparece un caso nuevo de fallo, la copia desincronizada, que hay que vigilar con pruebas de carga; el host necesita más CPU y subida, de ahí que el rasgo venga apagado.
* **Compatibilidad:** con `ExtendedConcurrency = false` no cambia nada respecto a la 0.5.

## Sin verificar al redactar

1. Que SMAPI no sustituya `Multiplayer.broadcastLocationMessage` ni interfiera con un parche sobre `GameServer.rebroadcastClientMessage`.
2. Cuál de los candidatos A, B o C es correcto y cuánto ahorra cada uno; no hay ninguna medida todavía.
3. Cómo se cancela limpiamente en el cliente un traslado que el host deniega.
4. Qué hace hoy el culling cuando una residencia pasa a activa para clientes ya conectados que no la tienen.
5. Cuántos clientes sin render caben en la máquina de pruebas para alcanzar los escalones de 48 y 64.
6. El comportamiento de festivales, minijuegos, comprobaciones de «todos listos», checkpoints y cierre ordenado del cambio de día con más de 16 conectados.

## Pendiente de decidir

* **Festivales:** usan un mapa temporal y son el momento de mayor reunión. Empiezan con aforo `16` como cualquier mapa no medido; subirlo depende de la auditoría del punto 6 anterior.
