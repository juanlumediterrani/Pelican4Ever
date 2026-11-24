# Tareas de testeo con varios jugadores (`test-users-tasks`)

Tareas de verificación y calibración que requieren partidas reales con **varios jugadores humanos** (host + clientes) o **validación visual humana**, separadas por ámbito del proyecto:

- **Backlog técnico de desarrollo:** [`backlog.md`](backlog.md)
- **Backlog de pruebas vivas ejecutables por agentes:** [`live-agent-tests-backlog.md`](live-agent-tests-backlog.md)
- **Pruebas y calibración con jugadores humanos:** este documento (`test-users-tasks.md`)

---

## Épica 21 — Calibración de economía y habilidades (ADR-0008)

- [ ] **TASK-2107: Calibración en partida y aceptación de ADR-0008**
  * **Prioridad:** Media
  * **Dependencias:** `TASK-2102..TASK-2106` (verificación técnica en servidor/laboratorio cerrada; ver [adr-0008-live-verification-2026-11-14.md](docs/verifications/adr-0008-live-verification-2026-11-14.md)).
  * **Descripción:** Jugar al menos una semana real con el grupo con los valores iniciales y revisar la telemetría diaria (`data/economy-telemetry/<saveId>/*.json` y eventos `DailyEconomy`) generada por `TASK-2106`. Ajustar los valores, anotar en [ADR-0008](docs/adr/0008-skill-economy-rebalance.md) las cifras finales y pasarlo a *Aceptado* en los tres idiomas (`es`, `en`, `zh-CN`). Actualizar `design.md` (§4), `architecture.md`, `config.example.json` y los índices de ADR en los tres idiomas.
  * **Criterios de aceptación:**
    1. Queda escrito cuántos días tarda un jugador de una sesión diaria en llegar a nivel 5 y a nivel 10 de Pesca, Minería y Combate.
    2. Queda escrito el oro diario típico por actividad (cultivo, pesca, minería) y la decisión sobre los multiplicadores de precio (`FishPriceMultiplier`, `MineralPriceMultiplier`).
    3. Las cifras de ADR-0008 coinciden con los valores por defecto del código.
    4. Decisión explícita sobre el botín de combate fuera de la Caverna Calavera, que ADR-0008 deja sin freno.

---

## Épica 6 y 7 — Verificaciones multijugador pendientes en partida real

- [ ] **TEST-607: Cueva individual y pararrayos de residencias con dos jugadores** *(movida desde `TASK-607` en [backlog.md](backlog.md))*
  * **Alcance:**
    1. Comprobar con dos jugadores reales que cada residente elige de forma independiente entre setas y murciélagos frutícolas en su cueva privada y en el anexo Coto Salvaje (`PrivateFarmCaves`), persistiendo en `PlayerRecord.CaveChoice` sin leer `MasterPlayer.caveChoice`.
    2. Repetir la prueba de pararrayos en día de tormenta con residencias que tengan `(BC)9` colocados físicamente (no en el suelo) y confirmar la carga repartida entre host y residencias.

- [ ] **TEST-701: Festivales asíncronos, ceremonias, entrada libre y tandas con 2–3 jugadores** *(movida desde `TASK-701`..`TASK-704` en [backlog.md](backlog.md))*
  * **Alcance:**
    1. **Premios de festival (`TASK-701`):** comprobar la entrega local al campeón conectado (sin abrir menú en pantalla ajena) y al ganador desconectado al reconectar (`PendingFestivalPrizes`), sin duplicar premios al reintentar.
    2. **Ceremonias comunitarias (`TASK-702`):** verificar aviso único a las 20:45 y arranque automático a las 21:00 en Luau / Danza de las Medusas únicamente con los asistentes registrados en `FestivalPresence`, sin congelar ni teletransportar a quienes estén fuera.
    3. **Entrada y salida libre (`TASK-703`):** confirmar entre varios jugadores que entrar al mapa festivo no mueve la cámara ni teletransporta, y que salir/volver a entrar no avanza la hora del servidor ni cierra el festival para los demás.
    4. **Evento principal por inscripción (`TASK-704`):** con 2–3 jugadores (algunos apuntados y otros no o en su granja), verificar que solo los inscritos entran en la tanda y las puertas `waitForOtherPlayers` no esperan al resto.

---

## Épica 9 / 9.1 — Validación visual y paseo humano por los anexos (40×32)

- [ ] **TEST-915: Validación visual humana de los 24 anexos (40×32) y paseo en partida real** *(movida desde `TASK-915` en [backlog.md](backlog.md); ver prueba automatizada en [`LIVE-915`](live-agent-tests-backlog.md))*
  * **Alcance:**
    1. Revisar visualmente las 24 capturas generadas en `audit/render/*_map.png` (especialmente `Mining_East`, `Standard_*`, `Combat_*` y `Fishing_*` tras los ajustes de `MapCloneHelper`) confirmando ausencia de costuras, sombras huérfanas, bordes abiertos o árboles cortados.
    2. Recorrer los anexos adquiridos con varios jugadores humanos en partida real comprobando sensación visual, claridad del corredor 3×4, letreros y transición de estaciones.

---

## Épica 22, 23 y 24 — Flujos interactivos y convivencia multijugador humana

- [ ] **TEST-2209: Experiencia de usuario en pedidos nocturnos por buzón y entregas a mediodía (`12:00`)** *(movida desde `TASK-2209` en [backlog.md](backlog.md); ver prueba automatizada en [`LIVE-2209`](live-agent-tests-backlog.md))*
  * **Alcance:**
    1. En partida con varios jugadores, probar desde el buzón exterior del portal y desde el interior residencial los encargos nocturnos a Pierre, Willy, Robin (construcción/mejora), Marnie (animales) y Clint (mejora de herramienta / geodas).
    2. Confirmar la claridad de los textos traducidos, la aparición de andamios/paquetes al llegar las `12:00` reales y el bloqueo amistoso cuando otro vecino intenta abrir un paquete ajeno.

- [ ] **TEST-2310: Convivencia interactiva Propietario + Inquilino en menús gráficos de Robin, Marnie (`AnimalPage`) y reconexión** *(movida desde la nota de cierre de `TASK-2310` en [backlog.md](backlog.md); ver prueba automatizada en [`LIVE-2310`](live-agent-tests-backlog.md))*
  * **Alcance:**
    1. Con dos jugadores humanos compartiendo residencia (`OwnerId` + `TenantId`), operar de forma interactiva los menús gráficos de construcción, reubicación, pintura y demolición en Robin y Torre del Mago desde el cliente del inquilino y el del propietario.
    2. Comprobar la compra de animales en Marnie y la gestión compartida en la pestaña `AnimalPage` (cuidado, cambio de establo/corral, venta y adopción de mascota propia con `PetBowl`).
    3. Salir al menú principal (`ExitToTitle`) con ambos residentes, reconectar eligiendo cada personaje y verificar camas diferenciadas y despertar independiente.

- [ ] **TEST-2404: Captura cooperativa de fauna NITV, viaje al Insectario y donaciones en sesión multijugador** *(movida desde `TASK-2404` en [backlog.md](backlog.md); ver prueba automatizada en [`LIVE-2404`](live-agent-tests-backlog.md))*
  * **Alcance:**
    1. Jugar una sesión con varios clientes usando las redes de *Nature In The Valley* en Plaza Central (`Farm`), puentes (`Pelican4Ever_Bridge_1..3`), residencias (`Foraging`, `Beach`, `Standard`), anexos y cuevas privadas (`_Cave`), incluso en parcelas ajenas sin permiso de edición.
    2. Viajar al Insectario desde `BusStop (28, 11)` y valorar la densidad de fauna.
    3. Donar la misma especie con dos personajes en `NIVInnerInsec`. Comprobar que cada uno ve su propia donación en la colección y puede recoger la recompensa una vez. Repetir con el osezno (`BearCub`) de Wild Animals y verificar el peluche de oso.
