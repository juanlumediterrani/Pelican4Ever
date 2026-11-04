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

El cuerpo conserva las acciones y animaciones de herramientas del granjero del
juego. Comprobado visualmente en un host de pruebas aislado: dragón completo
en las cuatro orientaciones y sin restos humanos. Sin comprobar todavía:
animaciones de herramientas, natación y cliente remoto.
