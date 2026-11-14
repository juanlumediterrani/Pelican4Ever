# Tareas de testeo con varios jugadores (`test-users-tasks`)

Tareas de verificación y calibración que requieren partidas reales con **varios jugadores humanos** (host + clientes), separadas del [backlog técnico de desarrollo](backlog.md).

---

## Épica 21 — Calibración de economía y habilidades (ADR-0008)

- [ ] **TASK-2107: Calibración en partida y aceptación de ADR-0008**
  * **Prioridad:** Media
  * **Dependencias:** `TASK-2102..TASK-2106` (verificación técnica en servidor/laboratorio cerrada; ver [adr-0008-live-verification-2026-11-14.md](docs/adr-0008-live-verification-2026-11-14.md)).
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
