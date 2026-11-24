# ADR-0008: Rebalanceo de Habilidades y Economía para el Reloj 1:1

* **Estado:** Propuesto (los valores son puntos de partida sin calibrar en partida; pasa a *Aceptado* al cerrar TASK-2107)
* **Fecha:** 2026-11-14
* **Ámbito:** `Pelican4Ever.Core/Economy/{DailySkillExperience,SkillExperienceCurve,SkillCheckpointBarrier,SkullCavernDescent,CategoryPriceMultiplier,DailyGoldLedger}.cs`, `Modules/{SkillExperienceModule,FishingRebalanceModule,MineLadderModule,SkullCavernDescentModule,DailyGoldTelemetryModule,EconomyModule}.cs`
* **Contexto:** Épica 21. Complementa a [ADR-0002](0002-realtime-clock-and-epoch-calendar.md) (reloj 1:1) y a [ADR-0006](0006-session-energy-satiety-wellfed-spa.md) (energía por sesiones).

## Contexto

Un día de Stardew Valley dura 14 minutos reales (120 tramos de 7 s). En *Pelican 4Ever* el día dura 24 horas, una sesión dura entre 30 y 90 minutos y no hay desmayo a las 02:00 (`NightHold`). El jugador dispone, por día de calendario, de varias veces el tiempo de acción de vanilla.

Ese exceso no infla todas las actividades por igual. Depende de qué limitaba cada una en vanilla:

| Actividad | Freno efectivo con 1:1 | Resultado |
| --- | --- | --- |
| Cultivo | Calendario: los días de crecimiento son días reales | Igual que vanilla por día; la más lenta en tiempo real |
| Ganadería | Calendario: 5 XP de Cultivo por caricia o producto, por animal y día | Igual que el cultivo |
| Recolección de forraje | Calendario (reaparición diaria) y reparto entre 4–16 jugadores | Ya diluida |
| Criaturas de *Nature in the Valley* | Sin freno conocido: cada captura da `3 + 4^rareza` u `11 + 4^rareza` XP de Recolección | Inflada |
| Pesca | Energía: `8 − 0,1 × nivel` por lanzamiento | Una barra con Saciedad son unos 50 lanzamientos; varias sesiones al día la multiplican |
| Minería | Energía: `2 × (potencia + 1) − 0,1 × nivel` por golpe; el descenso ya no tiene reloj | Inflada en profundidad |
| Combate | Ninguno: la espada no gasta energía y la comida cura aunque la Saciedad esté llena | La más inflada |
| Caverna Calavera | Ninguno: su único límite en vanilla era el reloj | Iridio sin freno |

Fórmulas comprobadas en el código descompilado de 1.6.15 (no en la wiki):

* **Picada** (`FishingRod.calculateTimeUntilFishingBite`): aleatorio entre `600` y `30000 − 250 × nivel` ms, menos `5000`/`10000` por aparejo; `×0,75` en el primer lanzamiento y `×0,5` con cebo; mínimo `500` ms.
* **Minijuego** (`BobberBar.update`): la barra de captura empieza en `0,3`; sube `0,002` por tick con el pez dentro y baja `0,003` por tick fuera (`0,002` con la caña de principiante).
* **XP de pesca** (`FishingRod.pullFishFromWater`): `max(1, (calidad + 1) × 3 + dificultad / 3)`, `×2,4` si la captura es perfecta, `×2,2` con tesoro y `×5` en peces legendarios.
* **Escalera al romper piedra** (`MineShaft.checkStoneForItems`): `0,02 + 1 / piedrasRestantes + suerte / 100 + suerteDiaria / 5`, más `0,04` sin enemigos; garantizada al romper la última piedra.
* **Escalera al matar un monstruo** (`MineShaft.monsterDrop`): `0,15`, más `0,07` con la estatua del enano.
* **Menas** (`MineShaft.createLitterObject`): `0,029` por piedra generada.
* **XP** (`Farmer.gainExperience`): toda la experiencia de habilidad pasa por este método y se aplica en el cliente dueño del personaje (el host reenvía la de los *farmhands*). Los niveles se alcanzan a `100, 380, 770, 1300, 2150, 3300, 4800, 6900, 10000, 15000` XP.

## Decisión

1. **Principio de balance.** El calendario manda. Lo que el calendario no frena, lo frena la energía. Lo que ninguno de los dos frena recibe un freno propio. Las actividades frenadas por el calendario no se tocan.

2. **Curva diaria de experiencia.** Un único parche sobre `Farmer.gainExperience` aplica, por habilidad y día de juego:
   * Los primeros `SkillDailyFullXp` puntos del día cuentan al 100 %. Valor inicial: `300` para Pesca, Recolección, Minería y Combate.
   * Lo que exceda ese cupo cuenta al `SkillOverflowXpMultiplier = 0.25`.
   * Cultivo queda **exento** (no tiene cupo). Esto cubre también la ganadería, que da XP de Cultivo.
   * `SkillXpMultiplier` (por habilidad, `1.0` por defecto) permite un recorte fijo adicional, aplicado antes del cupo.
   * Las fracciones se acumulan y no se pierden: un evento de 1 XP al 25 % cuenta 1 punto cada cuatro eventos.
   * El contador diario por habilidad es autoritativo en el host (`ServerWorldState`), se guarda con la partida y se reinicia por día de juego, con el mismo patrón que la Saciedad (`SatietyReport`).
   * La experiencia de Maestría (nivel total ≥ 25) recibe el mismo recorte, porque se calcula en el mismo método.

3. **Pesca.**
   * `FishingBiteTimeMultiplier = 2.5`: multiplica el tiempo hasta la picada ya calculado, de modo que cebos y aparejos conservan su ventaja relativa. Los estanques de peces quedan exentos.
   * `FishingMinigameDurationMultiplier = 1.5`: divide entre `1,5` lo que la barra de captura sube y baja por tick. El minijuego dura más sin cambiar la proporción de tiempo que hay que mantener el pez dentro.

4. **Escaleras de las minas.**
   * `MineStoneLadderChanceMultiplier = 0.5`: multiplica la probabilidad de escalera al romper piedra. La escalera garantizada al romper la última piedra se conserva.
   * `MineMonsterLadderChance = 0.08` (vanilla `0.15`). El `+0.07` de la estatua del enano se conserva.
   * Los pisos que exigen matar a todos los monstruos no cambian.
   * La densidad de menas **no** se toca.

5. **Caverna Calavera: el descenso cuesta energía.** Al llegar a un piso más profundo que el anterior, el jugador paga `SkullCavernDescentBaseCost + piso / SkullCavernDescentDepthDivisor` de energía, con `piso` contado desde la entrada de la Caverna (`mineLevel − 120`). Valores iniciales: `1` y `25`.
   * Quien cae por un pozo o usa una escalera fabricada paga por cada piso que salta.
   * Bajar hasta el piso 100 cuesta unos `300` puntos.
   * El descenso nunca se bloquea: si la energía no alcanza, queda en `0` y el jugador pasa a estar agotado.
   * Las Minas normales y el Volcán no tienen este coste.

6. **Precios.** Los multiplicadores por categoría de `Object.sellToStorePrice` se amplían con `FishPriceMultiplier` (categoría `-4`) y `MineralPriceMultiplier` (categorías `-2`, `-12` y `-15`), ambos `1.0`. Ningún precio cambia en esta decisión: antes de mover uno se registra en el log del host, por jugador y día, el oro ganado y la XP obtenida por habilidad.

## Alternativas descartadas

* **Multiplicador fijo de XP como única medida:** recorta igual a quien juega 30 minutos que a quien juega 6 horas. Queda disponible (`SkillXpMultiplier`), pero no es el mecanismo principal.
* **Reducir la XP de ganadería:** ya está frenada por el calendario, igual que el cultivo.
* **Reducir la densidad de menas:** los pisos son compartidos y solo se regeneran a las `06:00` y a las `18:00` o tras 45 minutos vacíos, así que las menas ya se reparten entre los jugadores.
* **Tope duro de pisos nuevos por día:** corta la sesión en seco en lugar de encarecerla.
* **Coste de energía por golpe de espada:** cambia la sensación del combate en todo el juego para corregir un problema que se concentra en la Caverna Calavera.

## Consecuencias

* **Positivas:**
  * Quien entra 30 minutos al día progresa al ritmo de vanilla por día de calendario; jugar más horas sigue rindiendo, pero a una cuarta parte.
  * Una barra de energía de pesca pasa de unos 25 a unos 45–50 minutos (estimación), así que la pesca llena una sesión y deja tiempo para la vida social.
  * La Caverna Calavera vuelve a tener un límite por inmersión, pagado con la moneda propia del mod.
  * Todo es configurable y cada valor neutro (`0` en el cupo, `1.0` en los multiplicadores) devuelve el comportamiento de vanilla.
* **Negativas:**
  * El botín de combate sigue sin freno fuera de la Caverna Calavera; solo se limita su experiencia.
  * La curva diaria no se ve en la interfaz de vanilla: hay que comunicarla a los jugadores.
  * Los pisos son compartidos: una escalera abierta por un jugador sirve a los siguientes, y eso amortigua el efecto del punto 4 en servidores con mucha actividad.
  * El contador diario añade estado por jugador que debe sobrevivir a reconexiones, checkpoints y rollovers.
* **Sin calibrar:** los valores `300`, `0.25`, `2.5`, `1.5`, `0.5`, `0.08`, `1` y `25` son estimaciones. Los tiempos de pesca y el coste de la Caverna están calculados sobre las fórmulas, no medidos en partida.
