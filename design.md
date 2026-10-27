## 1. Visión General del Proyecto

* **Nombre:** *Pelican 4Ever* (`SV-RTMP`)
* **Concepto nuclear:** Transformar Stardew Valley de un juego cooperativo de sesiones síncronas y turnos rápidos a un **mundo persistente en tiempo real (escala 1:1)** alojado en un servidor dedicado 24/7.
* **Pilar de experiencia:** Un "segundo hogar" virtual. El jugador entra entre 30 y 90 minutos al día para cuidar su granja privada (conectada a una puerta de Pelican Town), gestionar su industria pasiva, gastar su presupuesto diario de energía en las minas o la pesca y coincidir en el pueblo o en festivales de tarde/noche con el resto de la comunidad.

---

## 2. Sistema Temporal y Calendario

### 2.1. Reloj en Tiempo Real (1:1)

* El reloj del servidor está vinculado a la hora real del sistema anfitrión (ej. `CET/CEST`).
* **Sin colapso a las 02:00 AM:** El ciclo horario cubre las 24 horas del día real (`00:00` a `23:59`). Se elimina el desmayo forzoso de las 02:00 AM.
* **Iluminación ambiental continua:**
* `06:00 – 07:30`: Amanecer gradual.
* `07:30 – 18:30`: Luz diurna (ajustada según la estación).
* `18:30 – 20:30`: Atardecer progresivo.
* `20:30 – 06:00`: Noche cerrada (iluminación de farolas y antorchas activa).



### 2.2. Estructura de Estaciones y Año

* **Duración de estación:** Se mantiene el mes nativo de **28 días reales** (4 semanas exactas).
* **Sincronización perpetua de días:** El Día 1 de cada estación es siempre Lunes real; el Día 7 es siempre Domingo real. Los eventos semanales del juego (Carro Ambulante los viernes y domingos, cierre de Pierre los miércoles, días de suerte o programas de TV) coinciden permanentemente con los días de la semana del mundo real.
* **Duración del año:** 112 días reales (~3,7 meses reales por año de juego; ~3,25 años de juego por año natural).

### 2.3. El "Rollover" Diario (Cambio de Día del Servidor)

* Todos los días a las **05:30 AM (hora del servidor)** se ejecuta el corte de mantenimiento y simulación diaria (*Daily Rollover*), con una duración aproximada de 5 a 10 segundos:
1. Se procesa el avance de fase de todos los cultivos regados y árboles.
2. Se liquida el contenido de las Cajas de Envíos (*Shipping Bins*) y se ingresa el oro a cada jugador.
3. Se reparten las cartas en los buzones y se renueva el forraje del mundo.
4. Se evalúa el estado de descanso (*Sleep State*) de los jugadores y se guarda la partida maestra.



---

## 3. Mecánica de Sueño Asíncrono ("Bed-Logout")

Para evitar que todos los jugadores tengan que dormir a la vez y prevenir penalizaciones injustas por inactividad prolongada, cada jugador posee una máquina de estados de descanso persistente con cuatro estados posibles:

| Estado | Cómo se alcanza | Efecto en el Rollover (05:30 AM) |
| --- | --- | --- |
| **`Awake`** | El jugador se conecta al servidor e inicia sesión activa en cualquier momento del día. | Si llega el Rollover de las 05:30 AM y sigue en `Awake` (cerró el juego fuera de la cama o sigue despierto), **sufre penalización** y pasa a `Offline_Penalized`. |
| **`Rested`** | El jugador se mete en su cama, selecciona *"Descansar y Desconectar"* y sale de la partida (o permanece en cama sin moverse). | No sufre penalización. Recupera el **100% de su Energía** para la jornada siguiente y pasa a `Offline_Safe`. |
| **`Offline_Safe`** | Estado automático tras procesar un Rollover desde `Rested`. | Si el jugador no entra al servidor durante varios días seguidos, permanece en este estado sin sufrir ninguna penalización. |
| **`Offline_Penalized`** | Estado automático tras procesar un Rollover desde `Awake` (no durmió en su cama el día que jugó). | Aplica **una única vez** el cargo médico (10% del oro hasta un tope de 1.000g) y deja la Energía al **50%**. En días posteriores sin conectarse, no vuelve a penalizar. |

---

## 4. Economía y Balanceo (Pasivo vs. Activo)

Dado que el tiempo real ralentiza la agricultura (días reales) pero mantiene la velocidad de las animaciones del jugador (segundos reales), se aplican cuatro reglas de equilibrio:

1. **Presupuesto Diario de Energía (*Hard Stamina Cap*):**
* El jugador dispone de su barra de energía diaria (270 base + aumentos por *Stardrops*).
* **Reforma de la Comida:** Comer alimentos restaura **Salud (*Health*)** para el combate en las minas y aplica **Bonificadores (*Buffs*)** de estadísticas (+Pesca, +Minería, +Velocidad), pero **no devuelve Energía instantánea**.
* **Saciedad y Spa:** El jugador puede consumir hasta 2 platos "del día" en el Saloon o cocinados en casa que otorgan un bono de *Regeneración Pasiva* (+1 Energía cada minuto real durante 60 minutos). El Spa restaura 1 punto de Energía cada 30 segundos reales de reposo.


2. **Persistencia de Minas por Turnos (12 Horas):**
* Los pisos de las Minas y la Caverna Calavera no se reinician al salir por la escalera.
* El estado de cada piso visitado permanece en memoria en el servidor y **solo se regenera dos veces al día real (`05:30 AM` y `17:30 PM`)**. Las vetas de mineral son un recurso compartido y finito por turno.


3. **Procesado Industrial en Tiempo Real:**
* Las máquinas operan con el reloj real: un horno funde lingotes en 30 minutos reales; una envasadora tarda 2-3 días reales; un barril de vino tarda 6-7 días reales.


4. **Revalorización Agrícola y Artesanal:**
* Los cultivos y productos animales/artesanales reciben un multiplicador de valor base configurable (por defecto `2.2x` en cosechas directas y `1.8x` en procesados) para compensar su ciclo de maduración en días reales.



---

## 5. Vivienda Urbana y Granjas Instanciadas

* **Puertas en Pelican Town:** Se habilitan fachadas y puertas interactivas distribuidas por el pueblo y sus afueras.
* **Asignación y Elección de Mapa:** Al entrar por primera vez al servidor, el jugador reclama una puerta libre y selecciona mediante un menú su plantilla de granja preferida (*Estándar, Bosque, Cuatro Esquinas, Playa, Salvaje, Colina o Pradera*).
* **Sistema de Visitas y Permisos:**
* Al interactuar con una puerta del pueblo, se abre un submenú: *"Entrar a mi granja"* o *"Visitar granja de [Nombre de Jugador]"*.
* El propietario puede configurar su puerta desde dentro en tres modos: **Pública** (cualquiera puede entrar a ver o regar), **Amigos** (permite interacción autorizada) o **Cerrada con llave**.
* En granjas ajenas, el servidor bloquea el uso de herramientas destructivas (picos, hachas, bombas) y la apertura de cofres privados.



---

## 6. Vida del Pueblo (NPCs) y Festivales

### 6.1. Comportamiento Autónomo de NPCs (*Micro-Wandering*)

* Los aldeanos mantienen sus horarios reales (`08:00`, `12:00`, `17:00`, etc.), pero su velocidad de desplazamiento entre rutas se reduce un 50% (paso de paseo).
* Al llegar a un destino de su horario, en lugar de quedarse congelados durante horas reales, entran en modo **Deambulación Local (*Micro-Wandering*)**: se mueven libremente dentro de un área segura de 4x4 casillas alrededor de su punto de destino cada 45–90 segundos reales, alternando orientaciones, animaciones contextuales y bocadillos de texto ambiental.

### 6.2. Festivales en Prime-Time e Instanciados

* **Ventana horaria adaptada:** Todos los festivales abren sus puertas de **18:00 a 23:59 (hora del servidor)** para coincidir con el horario de ocio real de los jugadores.
* **Mapa en paralelo (Sin bloqueo de tránsito):** Durante la ventana del festival, el mapa `Town` normal sigue existiendo para que los jugadores puedan cruzar hacia la playa o las tiendas. Al entrar a la zona del evento, un diálogo pregunta: *"¿Entrar al Festival de [Nombre] o cruzar el pueblo normalmente?"*.
* **Asistencia individual:** Entrar a la instancia del festival registra la asistencia del jugador, le permite hablar con los NPCs, comprar en el puesto del evento y participar en la actividad principal sin requerir que el resto del servidor esté conectado al mismo tiempo.
* **Comando de agenda comunitaria:** Los administradores disponen de un comando para mover la fecha de un festival a cualquier otro día dentro de la misma estación (ej. mover un festival que cae en miércoles al sábado o domingo de esa misma semana).

---

---
