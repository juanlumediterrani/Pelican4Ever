# ADR-0006: Energía por Sesiones — Saciedad con Vaciado Continuo, "Bien comido" Sentado y Buff del Spa

* **Estado:** Aceptado (sustituye el punto 3 de [ADR-0004](0004-host-authority-client-mod-and-satiety.md))
* **Fecha:** 2026-11-06

## Contexto

ADR-0004 limitaba la energía de la comida a un presupuesto **diario** (`+50%` de la barra por día real), con 2 buffs "Bien comido" de `+1 Energía/minuto` y un Spa de `1 punto/30 s` con tope de `60` al día. Descansar sentado (GAP-07) daba `1 punto/15 s` sin condiciones ni tope, es decir, energía ilimitada a quien se quedara sentado.

El objetivo de juego es:

* Una sesión dura entre **30 min y 1 h 30 min**. Si el jugador quiere seguir, se sienta a descansar.
* El **punto dulce** es conectarse **30–45 min varias veces al día**.
* Al jugador **intensivo** se le deja un puente de plata: puede seguir jugando, pero a un ritmo más relajado.

Además, la Saciedad de los farmhands solo existía en la copia local del cliente: el host no la conocía y la pisaba al sincronizar, así que se reiniciaba al reconectar.

## Decisión

1. **Saciedad con capacidad y vaciado continuo:** la comida da energía instantánea hasta llenar una capacidad de `DailySatietyFraction × MaxStamina` (`135` en una barra de 270). La Saciedad se vacía de forma lineal en `SatietyDrainHours = 4` horas reales, conectado o no, y no se reinicia al cambiar de día. Las 4 h coinciden con la recuperación sin conexión (`OfflineRegenFullBarHours = 4`): quien vuelve tras 4 h llega descansado y con hambre.
2. **"Bien comido" activa el descanso sentado:** el efecto "Bien comido" se otorga al tener al menos **1/4 de la barra de Saciedad** (`WellFedSatietyThresholdFraction = 0.25`, es decir, ~34 puntos para una barra de 135) mediante cualquier comida (pescado, cultivos, recolección o platos cocinados). Mientras la Saciedad se mantenga en o sobre 1/4 (llenándola o mientras se vacía progresivamente en 4 horas), sentarse en un banco, silla o la taberna, o en el suelo con la tecla `GroundSitKey` (`GroundSitModule`), recupera `1 punto` de energía cada `SittingRegenSeconds = 5` s (`12/min`). Sin "Bien comido", sentarse no recupera nada. Los platos cocinados conceden además una ventana garantizada de `WellFedDurationMinutes = 60` min. "Bien comido" no da regeneración pasiva propia, sino que habilita el descanso activo sentado.
3. **Buff del Spa:** quedarse `SpaStillSeconds = 30` s quieto dentro del agua del Spa o de la piscina activa, **una vez al día**, un buff de recuperación pasiva de `SpaBuffTotalPoints = 120` puntos de energía **y** `120` de salud repartidos en `SpaBuffDurationMinutes = 120` min (`1 punto/min` de cada uno), sigas en el agua o no. Se mantiene el bloqueo de la recuperación nativa del juego en la piscina.
4. **Autoridad del host:** el cliente envía al host un mensaje `SatietyReport` al comer o activar el Spa (Saciedad, contadores diarios y segundos restantes de cada buff). El host lo acota, lo sella con su propio reloj y lo guarda en `ServerWorldState`. Mientras el jugador haya cambiado su consumo en la sesión, su copia local tiene prioridad sobre la sincronización de residencias del host.

## Consecuencias

* **Positivas:** las sesiones cortas y repetidas son la forma más eficiente de jugar. El jugador intensivo puede seguir, pero tiene que parar a comer y sentarse, y tras dos platos y el Spa solo le queda la Saciedad (unos 34 puntos por hora). Sentarse deja de ser una fuente ilimitada de energía. La Saciedad, "Bien comido" y el Spa se conservan al desconectar.
* **Negativas:** una sesión que encadena comida, "Bien comido" y Spa recibe más energía que con el presupuesto diario de ADR-0004. Sin "Bien comido", sentarse ya no recupera nada, y hay que comunicárselo a los jugadores. El buff del Spa avanza en tiempo real aunque el jugador se desconecte.
