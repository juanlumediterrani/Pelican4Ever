# ADR-0011: Concurrencia ampliada y aforo por mapa

* **Estado:** Propuesto
* **Fecha:** 2026-11-15. Revisión técnica del 2026-11-19.
* **Versión objetivo:** 0.6, rama `feat/v0.6-extended-concurrency`.
* **Dependencias:** [ADR-0010, residencias y admisión al servidor](0010-scalable-residences-admission-queue.md) y [ADR-0004, autoridad del host y mod obligatorio](0004-host-authority-client-mod-and-satiety.md).
* **Plan de trabajo:** [Épica 26 del backlog](../../backlog.md#concurrencia-v06).

## Objetivo y evidencia disponible

Queremos que más vecinos puedan jugar a la vez, tanto trabajando en sus residencias como reuniéndose en el pueblo. Publicaremos dos capacidades distintas: cuántos clientes remotos admite el servidor y cuántas personas pueden reunirse con buena respuesta en cada mapa ensayado. El primer objetivo son **24 clientes**. Los siguientes escalones experimentales son 32, 48 y 64.

La base de comparación es `dev` 0.5.1 (`d06b01f`), que ya incorpora las pruebas de la 0.5.0. En el ensayo del 17 de noviembre, una VM de 2 vCPU sostuvo **16 clientes remotos y un host** en una partida con 64 residencias pobladas. El checkpoint duró 8,299 s, incluidos 45,32 ms de clonación de los personajes. En la medición separada por tamaño del mundo, con 64 residencias se alcanzaron 4,71 GiB de memoria máxima, un checkpoint de 8,916 s y un cambio de día de 32,44 s. Se elevó la cuota del servicio de 4 a 6 GiB para ese ensayo. Con 32 residencias el checkpoint ya alcanzaba 6,551 s, por encima del objetivo habitual de 3 s. Estas mediciones justifican ensayar 24 y conservar la pausa de guardado como métrica independiente. Quedan por medir los bytes por tipo de mensaje y el efecto de reunir a los jugadores en un solo mapa. [Informe de verificación 0.5.0](https://github.com/juanlumediterrani/Pelican4Ever/blob/d06b01f85e08667675ae524ae9194af5da583def/docs/v0.5-residence-scaling-verification-2026-11-17.md)

La versión 1.6 anunció ocho jugadores en PC. El mod Unlimited Players es un precedente para elevar el límite configurable. El aforo operativo de Pelican4Ever se determinará con su propia partida, hardware y conjunto de mods. [Notas oficiales de Stardew Valley 1.6](https://www.stardewvalley.net/stardew-valley-1-6-update-full-changelog/) · [Unlimited Players](https://armitxes.net/Project/StardewValley/UnlimitedPlayers)

## Comportamiento observado en Stardew Valley 1.6.15

| Ruta inspeccionada | Comportamiento que condiciona la propuesta |
| --- | --- |
| `Multiplayer.UpdateLate`, `broadcastFarmerDeltas`, `GameServer.rebroadcastClientMessage` | Cada tres ticks hay normalmente una oportunidad de enviar los cambios pendientes del jugador. El host reenvía los tipos 0 (jugador) y 6 (ubicación) a los demás clientes. Con 16 clientes hay 240 parejas dirigidas posibles y con 64 hay 4.032. Para conocer paquetes y bytes hacen falta medidas de tráfico durante actividad real. |
| `GameServer.checkFarmhandRequest`, `sendServerIntroduction` | Al entrar se envían ubicaciones siempre activas completas y se presentan los demás personajes conectados. La conexión tiene un coste propio, distinto del tráfico durante el juego. |
| `Multiplayer.broadcastLocationMessage`, `isActiveLocation` | Las ubicaciones siempre activas llegan a todos. Para otras ubicaciones, el host también considera a quienes están en interiores de sus edificios. Una estructura puede compartir raíz de red con el mapa padre. |
| `Multiplayer.updateRoots`, `broadcastLocationDeltas` | El host recorre raíces y busca cambios en ubicaciones, incluidas las generadas. Puede gastar CPU en recorrer y serializar aunque el conjunto final de destinatarios sea pequeño. |
| Traslado en `Game1` y recepción en `GameServer.processIncomingMessage` | Con una raíz activa ya cargada, el cliente puede completar el viaje localmente. Si hay host dedicado, puede enviar el tipo 5 como aviso con `needsLocationInfo=false`. El servidor llama a `warpFarmer` cuando se solicita la ubicación. En esa ruta el cliente puede dejar `currentLocation` a `null` mientras espera. |
| `NetRoot.Write` y `Multiplayer.processIncomingMessage` | Escribir el delta marca limpia la raíz mediante `MarkClean()`. Hay eventos que clonan al `Farmer` remoto. Ambos comportamientos afectan a cualquier representación alternativa del personaje. |

En SMAPI 4.5.2, `SMultiplayer` instala interceptores al inicializar cliente y servidor y delega los mensajes ordinarios al juego. La compatibilidad del punto de parche concreto con Harmony y el conjunto de mods se comprobará en ejecución. [Código de SMAPI 4.5.2](https://github.com/Pathoschild/SMAPI/blob/4.5.2/src/SMAPI/Framework/SMultiplayer.cs)

## Decisión propuesta

### 1. Activación y límites

| Clave | Por defecto | Efecto propuesto |
| --- | --- | --- |
| `ExtendedConcurrency` | `false` | Requiere reinicio. Apagado conserva el comportamiento de la base 0.5.1, incluido el máximo de 16 clientes. |
| `MaxConcurrentPlayers` | `16` | Con el rasgo activo admite valores de 1 a 64. El perfil publicado indica el máximo probado para cada host. El contador excluye al anfitrión. |
| `DefaultLocationCapacity` | `16` | Umbral de entradas voluntarias por mapa, también para festivales y mapas temporales. |
| `LocationCapacityOverrides` | `{}` | Ajustes por nombre de ubicación. El ajuste tiene precedencia sobre el valor por defecto y la ocupación sigue limitada por los clientes admitidos al servidor. |

`Town`, la Plaza Central (`Farm`) y los puentes `Pelican4Ever_Bridge_*` son candidatos a un aforo mayor. Cada uno empieza con el umbral por defecto. En laboratorio se eleva explícitamente el aforo del mapa bajo prueba. En la guía de producción solo se recomienda el mayor valor superado allí, con sus condiciones. Los interiores, minas, Caverna Calavera y festivales mantienen el umbral de 16 hasta que exista una medición propia.

Para probar el escalón 24 hay que actualizar conjuntamente la validación de `ServerConfig`, la capacidad de `AdmissionLogic` y el umbral de `IdleKickLogic`. Los dos últimos limitan hoy su entrada a 16 aun cuando el transporte admita más conexiones. El rasgo apagado conserva los rangos, la cola y los parches de la base. La telemetría para medir esa referencia se habilita expresamente en el laboratorio. [Admisión en 0.5.1](https://github.com/juanlumediterrani/Pelican4Ever/blob/d06b01f85e08667675ae524ae9194af5da583def/Pelican4Ever.Core/Networking/AdmissionLogic.cs#L102) · [Inactividad en 0.5.1](https://github.com/juanlumediterrani/Pelican4Ever/blob/d06b01f85e08667675ae524ae9194af5da583def/Pelican4Ever.Core/Players/IdleKickLogic.cs#L46)

### 2. Admisión a un mapa antes del fundido

1. La admisión global de ADR-0010 decide quién entra en la partida. Una vez dentro, `LocationCapacityLogic` resuelve el destino efectivo y sus exenciones. El cliente solicita al host autorización **antes del fundido** para todo traslado voluntario sujeto a aforo, incluso con la raíz del destino ya cargada. La vista replicada de ocupación permite anticipar el aviso.
2. El host cuenta ocupantes y plazas ya concedidas para esa instancia, y reserva la última plaza en una sola operación. Asocia la concesión al jugador, destino e intento. La llegada confirma la ocupación una sola vez. Cancelación, desconexión y caducidad liberan la reserva. Una respuesta tardía de otro intento se ignora. La reserva de reconexión global de la 0.5 sigue su propio ciclo.
3. Ante denegación o espera agotada, el jugador conserva el control en el origen. La denegación muestra destino y ocupación, sin cola por mapa. La autorización cubre tanto el camino local como el que pide al host el estado completo.
4. El residente puede volver a su residencia y dependencias aunque el destino esté lleno. Un puente intermedio saturado debe ofrecerle un regreso efectivo a casa. Desmayos, cambio de día, expulsión de una residencia, fin de evento y teletransporte administrativo conservan un destino válido. Estas entradas pueden superar el umbral. Se cuentan al llegar y las nuevas visitas esperan fuera hasta que haya hueco.

### 3. Replicación residencial por receptor

Es la primera optimización que se ensayará. `ShouldKeepActive` conserva su papel en el tratamiento de ubicaciones activas. La nueva decisión de destinatarios se toma por **raíz de red** y tiene en cuenta al propietario y al inquilino conectados, ocupantes, visitantes en interiores dependientes y destino de una entrada autorizada. Se aplica al envío completo inicial, a los cambios generados por el host y al reenvío de cambios de ubicación del cliente (tipo 6). Se mantienen los destinatarios ordinarios de los mapas compartidos.

El receptor que adquiere interés necesita una copia completa vigente antes de aplicar sus deltas. Al perderlo y regresar, debe partir de un estado válido aunque haya cambios durante su ausencia. Los residentes conservan la copia sincronizada que usan Robin, Marnie y los pedidos remotos. La prueba funcional combina una modificación exterior mientras un visitante está en un edificio, su salida al exterior, una ausencia y una segunda visita. Se contrastan inventario, animales y construcción con el estado del host. [Retención de la instancia residencial](../../Pelican4Ever/Modules/FarmSlotModule.cs)

### 4. Personajes remotos y mapas concurridos

En el primer ensayo se conserva la replicación de jugadores de la 0.5.1. Se repite la carga con el filtro residencial y solo se cambia el tipo 0 si las medidas atribuyen a esos deltas una parte relevante del límite. La tabla recoge candidatos para ese experimento.

| Candidato | Beneficio que se mediría | Condición de corrección y coste |
| --- | --- | --- |
| A. Agrupar los deltas originales en orden | Menos envíos y cabeceras. Si todos reciben todos los deltas, se conserva la carga útil entre cada pareja. | Medir las ráfagas de entrega y el trabajo del cliente. Vaciar o reconciliar los pendientes al encontrarse. |
| B. Generar deltas por receptor | Evitar cambios intermedios para un destinatario lejano. | Hace falta una referencia independiente por receptor. Medir memoria y CPU y demostrar convergencia con dos receptores a ritmos distintos pese a `MarkClean()`. |
| C. Presencia remota compacta | Comunicar solo los cambios de presencia necesarios a distancia y recuperar el personaje completo al coincidir. | Auditar presentaciones y consumidores del `Farmer` remoto, incluidos eventos. Evitar que un delta antiguo sobrescriba la copia completa. El coste global depende también de la frecuencia de presencia. |

Para un candidato diferido, el objetivo inicial de emisión lejana es como máximo 30 ticks de espera programada (0,5 s nominales a 60 Hz). Se mide aparte la edad real del estado que observa el cliente. Quienes comparten mapa conservan la cadencia habitual. Cada receptor que mantiene la misma copia procesa sus incrementos válidos en orden o recibe una nueva base completa. Mundo, equipo y chat conservan sus rutas. [Entrega ordenada en Lidgren](https://github.com/lidgren/lidgren-network-gen3/blob/master/Lidgren.Network/Documentation/Tutorial.html) · [Estado de referencia para deltas](https://gafferongames.com/post/snapshot_compression/)

Con 40 personas reunidas hay 1.560 parejas dirigidas posibles en el mismo mapa. Si se satura, se ensaya primero generar cambios de jugador cada 6 ticks en vez de 3, preservando los incrementos necesarios. Si domina el coste de cambios del mapa, se ensayan restricciones avisadas sobre acciones concretas que sigan permitidas en la base. Si la experiencia sigue degradándose, se fija el último aforo que pasó la prueba. Una restricción entra en el perfil publicado solo con su medición.

### 5. Escalones y criterio de publicación

Se ensayan **24, 32, 48 y 64 clientes remotos** de forma incremental. Se conserva la misma partida inicial, hardware, conjunto de mods y guion al comparar variantes. El control a 24 eleva únicamente los límites necesarios para el ensayo y mantiene la replicación de referencia. Se compara con el filtro residencial y, si se justifica, con el candidato de personajes. Un escalón satisfactorio puede publicarse con la replicación de personajes original.

| Escenario | Evidencia que se recoge |
| --- | --- |
| Vecinos repartidos entre residencias pobladas | Bytes/s y mensajes/s por tipo y receptor, subida efectiva del host, recorrido y serialización de raíces, CPU, memoria máxima y tiempo desde conexión hasta control del personaje. |
| Reunión en movimiento | `Town`, `Farm` y los puentes se miden por separado. Al menos un cliente dibuja y permite observar llegada, movimiento, objeto en mano y uso de herramienta. Se anota el máximo de ocupantes probado en cada mapa. |
| Transiciones y persistencia | Combinar fin de festival, viajes, reconexiones y espera de admisión. Ejecutar un checkpoint y el cambio de día. Recargar la copia y contrastar personajes, inventarios y estado residencial. |

El umbral para el juego ordinario es **tick p95 ≤ 16 ms de trabajo del host** y ausencia de crecimiento sostenido de la cola hacia cualquier cliente durante **30 minutos**. Se mide tiempo real de ejecución de la actualización, además de la cadencia entre actualizaciones. Se registran la pausa máxima, la antigüedad de la actualización pendiente más vieja por receptor y el retraso observado al coincidir. Checkpoint y cambio de día se publican por separado, con su pausa y recuperación de las colas. Un p95 favorable por sí solo puede ocultar un guardado largo o un cliente siempre retrasado. [Temporización de MonoGame](https://docs.monogame.net/articles/getting_to_know/howto/HowTo_VariableStepGameLoop.html)

Para declarar un escalón **soportado** se documentarán además la convergencia del estado, la respuesta en un cliente visible, la recuperación de las transiciones y la restauración correcta bajo el perfil de guardado publicado. Un resultado incompleto o degradado queda experimental, y una carga que el generador no pudo sostener queda sin alcanzar. La guía indicará versión, CPU, RAM y cuota, subida, clientes admitidos, aforos ensayados y política de checkpoints. El nivel de residencias se informa por separado.

## Consecuencias y cuestiones abiertas

La separación por receptor puede reducir la subida y las copias residenciales en los clientes. Su efecto en CPU depende del tiempo que el host siga dedicando al recorrido de raíces y al guardado. Las reservas añaden un viaje de ida y vuelta en traslados limitados. Cambiar el protocolo de personajes añade riesgos de retraso y copias divergentes, y queda condicionado al beneficio medido.

Quedan pendientes la verificación de parches en vivo, las transiciones de interés con clientes ya conectados, el regreso por puentes llenos, los festivales y las tandas con más de 16 conectados, y el número de clientes reales que puede sostener cada máquina generadora. El render sin dibujado de la 0.5.1 se aplica al host. El banco tendrá que comprobar por separado el modo de laboratorio del cliente y la experiencia visible. [Política actual de renderizado](../../Pelican4Ever/Modules/HeadlessRenderModule.cs)
