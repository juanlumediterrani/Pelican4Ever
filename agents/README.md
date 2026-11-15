# Sprites de agentes

Los recursos de esta carpeta son complementos visuales independientes para los
agentes que juegan en servidores Pelican 4Ever. No son necesarios para compilar,
instalar ni ejecutar el mod Pelican 4Ever.

## Dr4co

`Dr4co.FashionSense/` es un pack para Fashion Sense que añade el cuerpo jugable
de Dr4co en cuatro direcciones con un ciclo de movimiento de cuatro cuadros.
Para habilitarlo, copia esa carpeta en `Stardew Valley/Mods` y selecciona
**Dr4co** desde el espejo de Fashion Sense.

Fashion Sense dibuja una capa vanilla (pelo, camisa, pantalón, zapatos) por cada
tipo sin apariencia propia, y la dibuja *encima* del cuerpo: con solo el cuerpo
se vería el granjero humano tapando al dragón. Por eso el pack incluye
apariencias transparentes **None** de pelo, camisa, pantalón y zapatos. En el
espejo hay que elegir **Dr4co** como cuerpo y **None** en esos cuatro tipos (la
ruta `/fs_body` del puente de Voyager lo hace sola). `HidePlayerBase: true` no
sirve para esto: Fashion Sense deja de dibujar el propio cuerpo.

El boceto original de la hoja SiD se conserva como `Dr4co.FashionSense/source/dr4co-sid-v2.png`; el pack usa una copia como textura `body.png`.

### Dependencia opcional

- **Fashion Sense 7.5.0 o posterior** (`PeacefulEnd.FashionSense`): requerido
  solo para cargar y seleccionar este pack cosmético. Pelican 4Ever no depende
de Fashion Sense y funciona igual si el framework o este pack no están instalados.

### Selección sin el espejo (servidor dedicado / automatizado)

Pelican 4Ever trae un comando para el host que lo hace todo (cuerpo y capas
`None`), en la consola de SMAPI o en el chat de una partida multijugador:

```
p4e_dr4co_sprite              # pone el cuerpo de Dr4co al host
p4e_dr4co_sprite off          # vuelve al granjero normal
p4e_dr4co_sprite on Pepe      # a otro jugador conectado (nombre o id)
```

El pack de Putre (`Putre.Stardew.FashionSenseBody`, en `agents/`) se activa igual, con su
propio comando:

```
p4e_putre_sprite              # pone el cuerpo de Putre al host
p4e_putre_sprite off
p4e_putre_sprite on Pepe
```

Para otro jugador, el host le envía un mensaje y es su propio cliente quien lo
aplica (cada cliente es dueño de su granjero), así que tiene que estar
conectado y con Pelican 4Ever instalado. El cambio queda en el `modData` del
granjero y se guarda con la partida. Detalle de lo que hace por debajo:

La API pública de Fashion Sense (`SetAppearance`) **no admite el tipo `Player`**
(devuelve «Failed to set the Player appearance…» aunque el cuerpo esté cargado).
Hair/Shirt/Pants/Shoes sí se pueden fijar con la API usando el pack
`Dr4co.Stardew.FashionSenseBody` y nombre `None`. El cuerpo solo se puede fijar
escribiendo el `modData` del granjero y marcando el sprite como sucio:

```
farmer.modData["FashionSense.CustomBody.Id"] = "Dr4co.Stardew.FashionSenseBody/Player/Dr4co";
```

Hay que usar el ID completo `pack/Player/Dr4co`; `pack/Dr4co` no funciona. Y la
carpeta del pack debe ser la **0.2.0 o posterior**: la 0.1.0 no trae las capas
`None` y se ve el granjero humano encima del dragón.

Tamaño y alineación (v0.4.0): Fashion Sense **ignora `Scale`** en los cuerpos y
dibuja la esquina superior izquierda de cada celda sobre el marco vanilla de
16×32, con 1 px de arte = 1 px del juego. Medido en el juego con una hoja de
calibración: la sombra (punto de apoyo) cae en x=8, y=30 de la celda en las
vistas frontal, derecha y trasera, y en x=24−8 en la fila izquierda, porque el
juego voltea la celda entera (por eso la fila izquierda lleva la misma arte que
la derecha, sin espejar). A la izquierda del punto de apoyo solo caben ~9 px,
así que el dragón (~26 px de alto) apoya los pies en la última fila (y=31) de cada
fotograma y en x=9,5; las vistas
frontal/trasera se estrechan un 15 %. La arte sale de `agents/sheets/dr4co-sid-v2.png`: el
script `scripts/build_dr4co_body.py` solo calcula la geometría y GIMP
(`gimp-console-2.10`, Script-Fu) hace el recorte, el escalado NoHalo, el umbral
de alfa y la paleta indexada de 32 colores. También en GIMP: en las vistas
laterales se desplaza 1 px el pie delantero (adelante y elevado / atrás) para
que también dé el paso, y los bordes claros o rojo vivo de la silueta (punta
del hocico, cuernos, vientre) reciben un contorno rojo muy oscuro.

Arte de la v0.5.0 (en curso, usable pero sin terminar): misma geometría que la
v0.4.0, pero sale de una hoja nueva generada por IA
(`agents/Dr4co sprites/fuente/dr4co_hoja_ia.jpg`). El proceso está en tres
scripts (Python con Pillow y NumPy, desde la raíz del repositorio):

1. `scripts/dr4co_sprites_grandes.py`: recorta las 8 poses útiles, quita el fondo
   y el ruido del JPG y las deja en `agents/Dr4co sprites/grandes/` con una
   paleta compartida de 20 colores (la trasera del otro paso es la misma espejada).
2. `scripts/dr4co_reducir.py`: reduce las poses a tamaño de juego (k-centroid y
   cúbica) para comparar; deja las pruebas en `agents/Dr4co sprites/pruebas/`.
3. `scripts/dr4co_montar_body.py "agents/Dr4co sprites/1"`: monta el `body.png`.
   El frontal son los tres cuadros reducidos a mano en GIMP de
   `agents/Dr4co sprites/1/` (27 px de alto); la lateral y la trasera salen con
   reducción cúbica a la misma escala, contorno coloreado al estilo de Stardew
   (*sel-out*: cada borde toma un tono oscuro del color vecino en vez de negro) y
   paleta fija. El resultado queda en `agents/Dr4co sprites/pruebas/montada/` y
   se copia a `Bodies/Dr4co/body.png`.

Pendiente: el paso frontal y los ojos se retocan a mano; comprobar en el juego
la alineación con la sombra y que `AgentToolOffset` (ajustado para el dragón de
26 px de la v0.4.0) siga sacando la herramienta de las garras.

`HideSleeves: true` en las cuatro direcciones evita que, al usar el hacha o el
pico, el juego dibuje el brazo/manga del granjero sobre el ala del dragón.

La fila izquierda va 2 px más hacia la cola que la derecha: a la izquierda del
punto de apoyo solo caben ~8 px y, si no, la punta del hocico se cortaba.

El juego dibuja la herramienta a la altura de las manos de un granjero de
32 px y Fashion Sense no permite moverla. El módulo `AgentCosmetics` de
Pelican 4Ever (`Pelican4Ever/Modules/AgentCosmeticsModule.cs`, valores en
`Pelican4Ever.Core/Players/AgentToolOffset.cs`) la desplaza mientras se dibuja
cuando el cuerpo activo es el de Dr4co, para que salga de sus garras. Solo lee
el id del cuerpo en `modData`, así que Pelican 4Ever sigue sin depender de
Fashion Sense, y la casilla a la que golpea la herramienta no cambia.

Verificado en local (Fashion Sense 7.5.0, SMAPI, partida cargada): dragón visible
y sin restos humanos andando en las cuatro direcciones.

El cuerpo conserva las acciones y animaciones de herramientas del granjero del
juego. Comprobado visualmente en un host de pruebas aislado: dragón completo
en las cuatro orientaciones y sin restos humanos. Sin comprobar todavía:
animaciones de herramientas, natación y cliente remoto.

## Referencia del granjero vanilla (`farmer_reference/`)

Para diseñar o entrenar nuevas skins de agentes (como Dr4co) y calibrar proporciones,
puntos de apoyo y posición de las manos/herramientas, `agents/farmer_reference/`
contiene los sprites originales de Stardew Valley 1.6 y el granjero ya ensamblado:

- `unpacked/`: hojas PNG nativas (`1x`) extraídas directamente de `Content/Characters/Farmer/*.xnb`
  (`farmer_base.png`, `farmer_girl_base.png`, `farmer_base_bald.png`, `shirts.png`, `pants.png`,
  `hairstyles.png`, `hairstyles2.png`, `accessories.png`, `hats.png`, `skinColors.png`, `shoeColors.png`).
- `dr4co_template/`: plantillas equivalentes al formato de cuerpo `4×4` (`24×32` por celda) de Fashion Sense:
  - `farmer_body_1x.png` (`96×128 px`) y `farmer_body_4x.png` (`384×512 px`, zoom real `4x` del juego).
  - `farmer_vs_dr4co_x6.png`: comparativa ampliada (`x6`) con la rejilla de `24×32` y la sombra marcada en `x=8, y=30`.
  - `farmer_vs_dr4co_caminar_4x.gif`: ciclo de paso en las 4 direcciones a tamaño de pantalla (`4x`).
  - `poses_9/`: las 9 poses únicas (`frontal`/`lateral`/`trasera` × `quieto`/`paso1`/`paso2`) a `1x` (`16×32`) y `4x` (`64×128`).
- `assembled/`: el granjero completo ensamblado con las tablas `featureXOffsetPerFrame` y `featureYOffsetPerFrame` de `FarmerRenderer`:
  - `farmer_full_sheet_1x.png` (`96×672 px`, los 126 cuadros a `1x`) y `farmer_full_sheet_4x.png` (`384×2688 px`, los 126 cuadros a `4x` Nearest-Neighbor).
  - `farmer_full_sheet_carry_1x.png`: los 126 cuadros con los brazos secundarios de sostener objetos (`armOffset = 12`).
  - `farmer_animations_showcase_1x.png` y `farmer_animations_showcase_4x.png`: secuencias principales (caminar, correr, llevar objeto, usar herramientas, regar/cosechar, espada, esquilar, comer, sentarse) en las 4 direcciones.

Para regenerar todas las hojas ensambladas (p. ej. cambiando camisa, pelo o colores):

```bash
python3 scripts/build_farmer_reference.py
```

## Pipeline de sprites por IA para Dr4co (`scripts/`)

Para completar la skin completa de Dr4co (ciclo de paso, acciones de herramientas y combate, y retrato de diálogo)
manteniendo la coherencia de píxeles y paleta fija de 12 colores:

1. **Ciclo de paso (`scripts/dr4co_llm_pipeline.py`)**:
   - Entrada: guía 3×3 de poses únicas sin herramientas (`llm_pose_guide_3x3.png`) + hoja de diseño (`dr4co-sid-v2.png`).
   - Generación LLM: `agents/Dr4co sprites/fuente/dr4co_3x3_llm.png`.
   - Post-procesado: *Head-Lock* (fija la cabeza de `quieto` en `paso1` y `paso2`), rebote vertical de paso (+1 px), reducción por área y cuantización de 12 colores con contorno *sel-out*.
   - Salidas: `agents/Dr4co sprites/pruebas/pipeline_ia/` (`body_v1_refinado.png`, `body_v2_llm3x3.png`, `comparativa_4_hojas_x6.png`, `comparativa_caminar_4x.gif`).

2. **Acciones de herramientas y combate (`scripts/dr4co_actions_pipeline.py`)**:
   - Entrada: guía 3×3 de acciones vanilla (`llm_action_guide_3x3.png`) con postura pura de garras/cuerpo (sin herramientas de metal que tapen la silueta).
   - Generación LLM: `agents/Dr4co sprites/fuente/dr4co_action_3x3_llm.png` (9 poses: golpe de herramienta, regadera y tajo de combate en Abajo, Derecha y Arriba).
   - Salidas en `agents/Dr4co sprites/pruebas/pipeline_acciones/`:
     - `comparativa_acciones_x6.png`: panel comparativo de las 12 acciones (Abajo, Derecha, Arriba, Izquierda) contra el granjero vanilla.
     - `dr4co_acciones_animadas_4x.gif`: animación en bucle a tamaño de pantalla (4x) ejecutando golpe de pico/hacha, regar y tajo con espada.
     - `dr4co_skin_showcase_1x.png` y `dr4co_skin_showcase_4x.png`: sábana completa de la skin combinando caminar y acciones en las 4 direcciones.
     - Recortes a 1x en `agents/Dr4co sprites/acciones_1x/` y en alta resolución en `grandes_acciones/`.

3. **Retrato de diálogo (`scripts/build_dr4co_portrait.py`)**:
   - Generación LLM: `agents/Dr4co sprites/fuente/dr4co_portrait_llm.png` (busto expresivo de Dr4co con cuernos crema, ojos de reptil y escamas rojas).
   - Salidas en `agents/Dr4co sprites/portrait/`:
     - `dr4co_portrait_64x64.png`: tamaño estándar de retrato de diálogo de Stardew Valley a 1x.
     - `dr4co_portrait_256x256.png`: retrato ampliado a 4x con marco de madera y pergamino del juego.
     - Copiado directo al pack en `agents/Dr4co.FashionSense/Bodies/Dr4co/portrait.png`.
