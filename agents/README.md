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
frontal/trasera se estrechan un 15 %. La arte sale de `dr4co-sid-v2.png`: el
script `scripts/build_dr4co_body.py` solo calcula la geometría y GIMP
(`gimp-console-2.10`, Script-Fu) hace el recorte, el escalado NoHalo, el umbral
de alfa y la paleta indexada de 32 colores. También en GIMP: en las vistas
laterales se desplaza 1 px el pie delantero (adelante y elevado / atrás) para
que también dé el paso, y los bordes claros o rojo vivo de la silueta (punta
del hocico, cuernos, vientre) reciben un contorno rojo muy oscuro.

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
