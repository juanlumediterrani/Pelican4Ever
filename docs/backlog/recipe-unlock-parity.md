# Backlog — Paridad de desbloqueo de recetas

- **Rama:** `fix/recipe-unlock-parity`
- **Fecha:** 2026-11-14
- **Estado:** abierto
- **Alcance:** recetas de *crafting* y *cooking* cuyo desbloqueo depende de eventos, correo o estado por jugador, y que pueden romperse en la topología host/clientes de Pelican 4Ever.

---

## 1. Contexto y causa raíz ya corregida

El evento vanilla del Horno (`Farm:992553`) no estaba roto: la condición `n copperFound` comprueba el `mailReceived` del **jugador local**, no el del host. El fallo real era que el rollover de Pelican 4Ever cierra todos los peers antes de avanzar el día, y el juego nativo solo procesa `mailForTomorrow` de `Game1.player` en `_newDayAfterFade` / `ReceiveMailForTomorrow`. El correo de los farmhands quedaba atascado para siempre.

Evidencia en la partida `Pelican4Ever_454183161`: el host tenía `spring_2_1` en `mailReceived`, mientras el farmhand conectado (`-1349064639806311618`) seguía con `hatter` y `spring_2_1` en `mailForTomorrow`.

Correcciones ya incluidas en esta rama:

- **RCP-001:** `FurnaceUnlockModule` marca `copperFound` en el `mailReceived` del jugador local al obtener cobre y entrega la receta `Furnace` como red de seguridad a partir de las 12:00 si el evento no se jugó.
- **RCP-002:** `DailyRolloverModule.DeliverPendingFarmhandMail` entrega el `mailForTomorrow` de los farmhands desconectados (banderas y cartas) antes del guardado post-cierre del rollover.
- Pruebas: `FurnaceUnlockLogicTests`, `FarmhandMailDeliveryLogicTests`.

---

## 2. Matriz A — Eventos que conceden recetas

Enumeración obtenida de `Data/Events/*`, `Strings/Locations`, `Event.skipEvent()` y `Data/mail` del juego 1.6.15.

| ID | Ubicación | Receta(s) | Precondición vanilla | Ámbito | Riesgo en Pelican | Tarea | Estado |
|---|---|---|---|---|---|---|---|
| `992553` | `Farm` | Furnace | `t 600 1130` + `n copperFound` | Local | Correo atascado; el host parecía ser el único que podía cumplirlo | RCP-001 | **Hecho** |
| `900553` | `Farm` | Garden Pot | `t 600 1130` + `Hn ccPantry` + `A cc_Greenhouse` + `w sunny` | **Host + local** | `Hn` exige que el **host** haya completado el pantry del Centro Cívico; la reparación del invernadero propio del farmhand no cuenta | RCP-003 | **Hecho** |
| `980558` | `Farm` | Mini-Jukebox | `t 600 1130` + `w sunny` + `f Gus 1250` | Local | Evento solo en la granja del host y en ventana 06:00–11:30; si se pierde, no hay receta | RCP-006 | **Hecho** |
| `3131209` | `Beach` | Quality Bobber | `n willyBugWadCutscene` | Local + correo | El flag lo entrega la orden especial «Juicy Bugs Wanted»; si el correo no llega, el evento nunca se abre | RCP-006 | **Hecho** (verificar correo) |
| `19` | `JoshHouse` | Cookies (cooking) | `f Evelyn 1000` + `p Evelyn` | Local | Evento por amistad; sin fallback si el jugador no visita la casa | RCP-006 | **Hecho** |
| `26` | `Mountain` | Wild Bait | `f Linus 1000` + `w sunny` + `t 2000 2400` | Local | Ventana nocturna + clima; fácil de perder | RCP-006 | **Hecho** |
| `33` | `ScienceHouse` | Flute Block + Drum Block | `f Robin 1500` + `p Robin` | Local | Evento por amistad; sin fallback | RCP-006 | **Hecho** |
| `-666777` | `IslandWest` (Birdie) | Fairy Dust | Evento `Strings\Locations:IslandSecret_Event_BirdieFinished` | Local | El evento se lanza desde código y el `skipEvent()` tiene una concesión hardcodeada; auditar ambos caminos | RCP-006 | **Hecho** |
| `FieldOfficeFinale` | `IslandFieldOffice` | Ostrich Incubator | `!fieldOfficeFinale` + colección completa | Local | Evento de progreso personal en Ginger Island; verificar en clientes | RCP-006 | **Hecho** (si la carta existe) |
| `719926` | `Sunroom` | Tea Sapling (`mail CarolineTea`) | `t 900 1700` + `w sunny` | Local + correo | El evento envía la carta `CarolineTea`; si el correo se atasca, no hay receta | RCP-004 | **Hecho** |

### Concesiones hardcodeadas en `Event.skipEvent()`

Si un jugador salta el evento, el juego concede la receta igualmente. Cualquier fallback propio debe replicar esta lista:

| Evento | Receta(s) |
|---|---|
| `33` | Drum Block, Flute Block |
| `26` | Wild Bait |
| `19` | Cookies |
| `992553` | Furnace |
| `900553` | Garden Pot |
| `980558` | Mini-Jukebox |
| `-666777` | Fairy Dust |

---

## 3. Matriz B — Correo que concede recetas

El correo se entrega al abrir la carta en `LetterViewerMenu.HandleItemCommand` y siempre aplica a `Game1.player` (jugador local). Si la carta no llega al buzón del farmhand, la receta no se aprende.

### B.1 Crafting recipes con `%item craftingRecipe`

| Mail key | Receta | Origen del envío | Riesgo | Tarea |
|---|---|---|---|---|
| `RarecrowSociety` | Deluxe Scarecrow | Buzón aleatorio al tener todos los espantapájaros raros (`Farmer.cs`) | Carta directa a `mailbox`; verificar en clientes | RCP-005 |
| `CarolineTea` | Tea Sapling | Evento `Sunroom:719926` | Evento + `mailForTomorrow` | RCP-004 |
| `KrobusCrafting` | Dark Sign | `Stats.checkForFriendshipAchievements` (`f Krobus 3`) | `mailForTomorrow` atascado | RCP-004 |
| `robinResource` | Stone Chest | Orden especial | `sendToEveryone` / `broadcastedMail` | RCP-005 |
| `ClintReward` | Geode Crusher | Orden especial | `sendToEveryone` / `broadcastedMail` | RCP-005 |
| `willyBugWadCutscene` | Quality Bobber | Orden especial «Juicy Bugs Wanted» | Correo + evento `Beach:3131209` | RCP-005 |
| `WizardReward` | Monster Musk | Orden especial | `sendToEveryone` / `broadcastedMail` | RCP-005 |
| `WizardReward2` | Mini-Obelisk | Orden especial | `sendToEveryone` / `broadcastedMail` | RCP-005 |
| `DemetriusReward` | Farm Computer | Orden especial | `sendToEveryone` / `broadcastedMail` | RCP-005 |
| `linusTrashCleanup` | Fiber Seeds | Orden especial | `sendToEveryone` / `broadcastedMail` | RCP-005 |
| `carolineSO` | Solar Panel | Orden especial (ingredientes de isla) | `sendToEveryone` / `broadcastedMail` | RCP-005 |
| `guntherBones` | Bone Mill | Orden especial | `sendToEveryone` / `broadcastedMail` | RCP-005 |

### B.2 Cooking recipes con `%item cookingRecipe %%`

Cada carta elige la receta `f <NPC> <prioridad>` no aprendida de menor prioridad. Son 19 cartas:

`RobinCooking`, `DemetriusCooking`, `LinusCooking`, `PierreCooking`, `CarolineCooking`, `GeorgeCooking`, `EvelynCooking`, `PamCooking`, `LewisCooking`, `GusCooking`, `ClintCooking`, `JodiCooking`, `KentCooking`, `EmilyCooking`, `MarnieCooking`, `ShaneCooking`, `SandyCooking`, `WillyCooking`, `LeoCooking`.

Recetas `f <NPC> <nivel>` asociadas (36 entradas): Salad, Cheese Cauli., Parsnip Soup, Vegetable Stew, Fried Calamari, Strange Bun, Fried Mushroom, Bean Hotpot, Salmon Dinner, Fish Taco, Crispy Bass, Pepper Poppers, Tom Kha Soup, Rhubarb Pie, Spaghetti, Fried Eel, Spicy Eel, Sashimi, Red Plate, Eggplant Parm., Rice Pudding, Ice Cream, Blueberry Tart, Autumn's Bounty, Pumpkin Soup, Super Meal, Cran. Sauce, Stuffing, Algae Soup, Pale Broth, Chowder, Lobster Bisque, Fish Stew, Escargot, Mango Sticky Rice, Poi.

Riesgo: el envío se decide en `Stats.checkForFriendshipAchievements` con `Game1.player.friendshipData`; el correo va a `mailForTomorrow` y hasta RCP-002 se quedaba atascado en farmhands. Tarea: **RCP-004**.

---

## 4. Matriz C — Otras fuentes de recetas

| Fuente | Recetas / datos | Mecanismo | Riesgo | Tarea |
|---|---|---|---|---|
| Nivel de habilidad | 71 crafting + 9 cooking con condición `s <skill> <level>` | `LevelUpMenu` en local; `Farmer.AddMissedMailAndRecipes` / `Game1.fixProblems` en host | Verificar farmhands offline y niveles subidos mientras el host procesa el día | RCP-007 |
| Recetas `default` | 11 crafting + 1 cooking | `LearnDefaultRecipes` | Sin conflicto esperado | — |
| Condición legacy `l <n>` | 14 crafting + 34 cooking (Furnace, Heavy Furnace, Anvil, Mini-Forge, suelos de Robin, Omelet, etc.) | El campo `l` no lo evalúa `TryParseLevelRequirement`; la fuente real es evento, TV, Mastery, tienda o libro | Mapa incompleto de fuentes; riesgo de recetas sin ruta de desbloqueo | RCP-008 |
| Queen of Sauce / TV | 34 recetas de cocina con `l`/`l 100` | `TV.cs` al ver el canal; `Book_QueenOfSauce` desbloquea todas | Verificar TV en residencias privadas y recetas por jugador | RCP-009 |
| Mastery (1.6) | Heavy Furnace, Anvil, Mini-Forge, Statue of Blessings, Mystic Tree Seed, Challenge Bait, etc. | `MasteryTrackerMenu.claimReward` | Reclamo debe ser del jugador local, no del host | RCP-010 |
| Bodega / Cask | Cask | `FarmHouse.loadObjects` / `AddCellarTiles` con `Game1.player` | Si el host mejora una residencia ajena, ¿quién recibe la receta? | RCP-010 |
| Museo | Ancient Seeds | `LibraryMuseum` al donar `(O)499` | Donación y recompensa por jugador | RCP-010 |
| Tiendas | ~48 entradas `(Recipe)` en `Data/Shops` (Robin, Pierre, Krobus, Dwarf, etc.) | Compra y `Recipe` item | Verificar que compra el jugador correcto y no el host | RCP-011 |
| Libros | Queen of Sauce Cookbook y libros de habilidad | `Object.readBook` | Per-player; verificar sync | RCP-009 |
| `MarkCraftingRecipeKnown` / `MarkCookingRecipeKnown` | Cualquier receta | Trigger action / `SetSimpleFlagRequest` | Mods/content packs pueden conceder a `PlayerActionTarget` equivocado | RCP-012 |

---

## 5. Tareas del backlog

### RCP-001 — Furnace por jugador y fallback · **Hecho**
- Marcar `copperFound` en `mailReceived` local al obtener `(O)378`.
- Entregar `Furnace` a partir de las 12:00 si el evento no se jugó.
- Pruebas de lógica pura y HUD.

### RCP-002 — Entrega de `mailForTomorrow` de farmhands en rollover · **Hecho**
- Procesar banderas (`%&NL&%`) y cartas antes del guardado post-cierre.
- Cubre Furnace, cartas de cocina, Krobus, CarolineTea y correo general.
- Añadir una prueba de integración con dos farmhands y un save real.

### RCP-003 — Garden Pot: eliminar dependencia del host · **Hecho**
- **Problema:** `Hn ccPantry` exige el correo del host; el invernadero reparado del farmhand no influye.
- **Implementado:** `RecipeFallbackModule` comprueba el invernadero de la residencia del jugador (`farm.greenhouseUnlocked`) o su propio `ccPantry`/`jojaPantry`/CC completado y, a partir de las 12:00, concede `Garden Pot` si la receta falta.
- **Aceptación:** un farmhand con invernadero propio recibe Garden Pot aunque el host no haya completado el pantry.

### RCP-004 — Correo de cocina y Krobus · **Cubierto por RCP-002, verificación pendiente**
- Las 19 cartas `*Cooking` y `KrobusCrafting` se entregan con el rollover corregido.
- Verificar que `Stats.checkForFriendshipAchievements` se ejecuta para el farmhand correcto.
- Prueba de regresión: amistad 3/7, carta en `mailForTomorrow`, rollover, apertura y receta aprendida.

### RCP-005 — Órdenes especiales y correo party-wide
- Auditar las 12 cartas de crafting de la Matriz B.1.
- Revisar `MailReward` + `broadcastPartyWideMail` + `FarmerTeam.AddAnyBroadcastedMail` con farmhands desconectados.
- Prueba: completar una orden con un farmhand offline y verificar que recibe la carta al reconectar.

### RCP-006 — Eventos de receta restantes · **Hecho**
- **Implementado:** `RecipeFallbackModule` cubre Mini-Jukebox, Wild Bait, Flute/Drum Block, Cookies, Quality Bobber, Tea Sapling, Fairy Dust y Ostrich Incubator.
- La concesión se hace a partir de las 12:00 solo si la receta falta y el requisito de fondo se cumple (amistad, carta o evento visto).
- No se arrancan eventos ni se reenvía correo: la escena vanilla sigue disponible si el jugador aún no la ha visto.
- Aceptación: ningún jugador queda sin la receta por no estar en la ubicación/ventana correcta.

### RCP-007 — Recetas por nivel de habilidad
- Verificar `AddMissedMailAndRecipes`/`fixProblems` para farmhands offline.
- Probar subida de nivel con el cliente desconectado y con el host procesando el día.

### RCP-008 — Mapa de recetas `l`
- Documentar la fuente real de las 14 crafting + 34 cooking con condición `l`.
- Marcar las que sean evento/TV/Mastery/tienda/libro.
- Añadir pruebas o fallbacks para las que no tengan ruta clara.

### RCP-009 — Queen of Sauce y libros
- Verificar TV en cada residencia privada.
- Verificar `Book_QueenOfSauce` y que el desbloqueo sea del lector, no del host.

### RCP-010 — Mastery, Cask y Museo
- Revisar `MasteryTrackerMenu.claimReward`, `FarmHouse` y `LibraryMuseum` con `Game1.player` en contexto host/cliente.
- Aceptación: la receta va al jugador que completa la acción.

### RCP-011 — Recetas de tienda
- Auditar las ~48 entradas `(Recipe)` de `Data/Shops`.
- Verificar compra en cliente y persistencia de `craftingRecipes`/`cookingRecipes`.

### RCP-012 — Trigger actions y flags de red
- Auditar `MarkCraftingRecipeKnown`, `MarkCookingRecipeKnown` y `SetSimpleFlagRequest`.
- Documentar que content packs deben usar `PlayerActionTarget` correcto.

### RCP-013 — Diagnóstico y comandos
- Añadir un comando `/p4e_recipes` o log de diagnóstico que liste, por jugador, recetas esperadas y fuente pendiente.
- Útil para verificar saves antiguos con correo atascado.

---

## 6. Criterios de aceptación de la rama

1. Un farmhand que recoge cobre recibe la escena de Clint o, como máximo a las 12:00, la receta `Furnace`.
2. Un farmhand con amistad suficiente recibe las cartas de cocina/Krobus y aprende la receta al abrirlas.
3. Un farmhand offline cuando se completa una orden especial recibe su carta al reconectar.
4. Las recetas de eventos restantes tienen una ruta documentada y, si el evento es host-scoped o de ventana estricta, un fallback.
5. Save antiguo con `mailForTomorrow` atascado se repara en el siguiente rollover.
6. Suite de pruebas en verde y prueba manual con al menos dos clientes.

---

## 7. Fallbacks implementados (`RecipeFallbackModule`)

La tabla se evalúa en `SaveLoaded`, `DayStarted` y `TimeChanged`. Todas las reglas comparten:

- **Idempotencia:** si la receta ya está en `craftingRecipes`/`cookingRecipes`, no se hace nada.
- **Sin eventos:** no se llama a `Game1.PlayEvent`, `startEvent` ni `addMailForTomorrow`.
- **Sin HUD duplicado:** solo se muestra el mensaje si `TryAdd` devuelve `true`.
- **Sin log repetido:** los errores se registran una vez por regla y tipo de excepción.
- **Sin bucles:** `TimeChanged` solo lee estado y, como mucho, añade la receta una vez.

| Receta | Requisito de la red de seguridad | Hora mínima |
|---|---|---|
| Garden Pot | Invernadero propio desbloqueado, `ccPantry`, `jojaPantry` o CC propio completado | 12:00 |
| Mini-Jukebox | Amistad con Gus ≥ 1250 puntos (5 corazones) | 12:00 |
| Wild Bait | Amistad con Linus ≥ 1000 puntos (4 corazones) | 12:00 |
| Flute Block | Amistad con Robin ≥ 1500 puntos (6 corazones) | 12:00 |
| Drum Block | Amistad con Robin ≥ 1500 puntos (6 corazones) | 12:00 |
| Cookies (cooking) | Amistad con Evelyn ≥ 1000 puntos (4 corazones) | 12:00 |
| Quality Bobber | Carta `willyBugWadCutscene` o evento `3131209` visto | 12:00 |
| Tea Sapling | Carta `CarolineTea` o evento `719926` visto | 12:00 |
| Fairy Dust | Carta/flag `birdieQuestFinished` o evento `-666777` visto | 12:00 |
| Ostrich Incubator | Carta `fieldOfficeFinale` | 12:00 |

Furnace queda en `FurnaceUnlockModule` porque además marca `copperFound` en el jugador local.

---

## 8. Fuentes consultadas

- Contenido local del juego 1.6.15: `Data/Events`, `Data/mail`, `Data/CraftingRecipes`, `Data/CookingRecipes`, `Data/Shops`, `Data/SpecialOrders`, `Strings/Locations`.
- Código del juego: `Farmer.AddMissedMailAndRecipes`, `Stats.checkForFriendshipAchievements`, `LetterViewerMenu.HandleItemCommand`, `Event.skipEvent`, `MailReward`, `MasteryTrackerMenu`, `TV`, `FarmHouse`, `LibraryMuseum`.
- Stardew Valley Wiki: [Crafting](https://stardewvalleywiki.com/Crafting), [Cooking](https://stardewvalleywiki.com/Cooking), [Modding:Recipe data](https://stardewvalleywiki.com/Modding:Recipe_data).
- Páginas de recetas concretas: [Furnace](https://stardewvalleywiki.com/Furnace), [Garden Pot](https://stardewvalleywiki.com/Garden_Pot), [Mini-Jukebox](https://stardewvalleywiki.com/Mini-Jukebox), [Wild Bait](https://stardewvalleywiki.com/Wild_Bait), [Quality Bobber](https://stardewvalleywiki.com/Quality_Bobber).
