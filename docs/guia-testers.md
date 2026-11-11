# Guía para Testers: Conexión y Pruebas en Pelican 4Ever

🌐 **Idiomas:** Español | [English](guia-testers.en.md)

¡Te damos la bienvenida a la fase de pruebas de **Pelican 4Ever**!

Esta guía contiene todo lo necesario para instalar el mod en tu ordenador o Steam Deck, vincular tu dispositivo de forma segura al servidor demo y comenzar a jugar.

---

## 1. Requisitos Previos

Antes de comenzar, asegúrate de contar con:

* **Stardew Valley 1.6.15** (Steam o GOG).
* **SMAPI 4.1.10 o superior** correctamente instalado.
* **Paquete de actualización del mod:** `Pelican4Ever 0.2.0.zip` (facilitado en el canal de pruebas de Discord).
* Conexión a internet estable.

---

## 2. Instalación del Mod

### Paso 1: Copiar los Archivos del Mod
1. Descarga y descomprime `Pelican4Ever 0.2.0.zip`.
2. Mueve la carpeta `Pelican4Ever` dentro del directorio `Mods` de tu instalación de Stardew Valley:
   * **Linux / Steam Deck:**  
     `~/.steam/steam/steamapps/common/Stardew Valley/Mods/`  
     *(o `~/.local/share/Steam/steamapps/common/Stardew Valley/Mods/`)*
   * **Windows:**  
     `C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\Mods\`  
     *(o la ruta donde tengas instalado el juego)*
3. Verifica que la ruta final sea `.../Mods/Pelican4Ever/manifest.json`.

### Paso 2: Primer Inicio y Generación de Claves
1. Inicia Stardew Valley mediante el ejecutable de **SMAPI**.
2. Durante el arranque, el mod generará automáticamente tu par de claves criptográficas personales en:  
   `Mods/Pelican4Ever/data/device_key.json`
3. En Linux y Steam Deck, el mod asigna automáticamente permisos estrictos (`0600`) para que tu clave privada quede resguardada.

---

## 3. Conexión al Servidor y Emparejamiento Seguro

El servidor cuenta con un sistema de autenticación de dispositivos (`P4E\x02`). Esto significa que **nunca necesitas recordar contraseñas ni tokens**: tu equipo se identifica de forma criptográfica y una vez aprobado podrás reconectar desde cualquier conexión a internet (incluso si tu router reinicia o cambia tu IP pública).

### Paso 1: Intento de Conexión Inicial
1. En el menú principal del juego, ve a **Cooperativo** -> **Unirse a partida LAN** (Conexión Directa).
2. Introduce la dirección IP y el puerto del servidor demo (proporcionados en el servidor de Discord).
3. El servidor y tu cliente realizarán un saludo criptográfico automático de verificación.
4. Como es tu primera vez y tu dispositivo aún no está en la lista de aprobados, el servidor registrará tu solicitud y te desconectará limpiamente mostrando en pantalla tu **huella digital pública corta** (un código de 12 caracteres hexadecimales, por ejemplo: `a1b2c3d4e5f6`).

### Paso 2: Solicitud de Aprobación
1. Copia o anota tu código de huella pública.
2. Entra al servidor de Discord de Pelican 4Ever y envía un mensaje privado (DM) al anfitrión o escribe en el canal privado de testers con:
   * Tu nombre de jugador / alias.
   * Tu huella digital pública corta (ej: `a1b2c3d4e5f6`).
3. El anfitrión aprobará tu dispositivo en el servidor.

### Paso 3: Entrada al Servidor y Selección de Personaje
1. Una vez que el anfitrión te confirme la aprobación, vuelve a pulsar en **Conexión Directa** hacia el servidor.
2. Tu dispositivo será reconocido al instante.
3. Se abrirá la pantalla de selección de peones:
   * **Nuevos testers:** Selecciona cualquiera de las ranuras libres disponibles. Quedará vinculada de forma permanente y exclusiva a tu dispositivo.
   * **Ranuras de otros jugadores:** Mostrarán la etiqueta `[De otro jugador]` y permanecerán bloqueadas contra selección accidental.
4. ¡Entra a la granja y disfruta del juego!

---

## 4. ¿Qué estamos probando? Funcionalidades Clave

Durante tus sesiones de juego, te invitamos a probar y darnos feedback sobre las siguientes mecánicas:

### 🕰️ Reloj en Tiempo Real y Madrugada Continua
* El reloj del juego avanza 1:1 con el tiempo real (1 día real = 1 día en el juego).
* **Sin desmayos forzados de madrugada:** Entre la 01:50 y las 06:00 AM reales, el reloj se congela en `2550`, permitiendo jugar de noche tranquilamente sin perder energía ni caer desmayado.
* El cambio oficial de fecha del calendario ocurre automáticamente a las **02:00 AM**, con un aviso sonoro y en el HUD 10 minutos antes.

### 🏡 Residencias Privadas y Plaza Central
* El mapa principal (`Farm`) es la **Plaza Central**. Desde allí y a través de los puentes hacia el este encontrarás los portales a las 16 residencias privadas.
* Cada granjero cuenta con su propia granja aislada (pudiendo elegir entre 8 tipos oficiales de granja) y su cueva privada (setas o murciélagos).
* Usa el comando `/residencia` o interactúa con el cartel/poste junto a tu portal para configurar los permisos de acceso (`Cerrada`, `Solo amigos` o `Pública`).

### 🍎 Saciedad y Descanso Asíncrono
* Puedes acostarte en tu cama y salir al menú principal en cualquier momento sin afectar a los demás jugadores.
* Al comer alimentos cocinados o cultivos se llena tu barra de saciedad. Al estar «Bien comido», sentarte en bancos, sillas o en el suelo recupera energía gradualmente.
* Visitar el Spa en las vías del tren otorga regeneración de energía y salud durante 2 horas reales.

### 🏪 Mercado Vecinal
* En el tablón de anuncios vecinal puedes consultar ofertas publicadas por otros jugadores o poner a la venta tus excedentes de cultivos, minerales o productos artesanales, cobrando tus beneficios incluso si el comprador adquirió el lote mientras estabas desconectado.

### 💬 Chat Multicanal y Proximidad
* `/l <mensaje>`: Chat local por proximidad (solo lo leen quienes estén en tu zona o cerca de ti).
* `/g <mensaje>`: Chat global del servidor.
* `/w <jugador> <mensaje>`: Susurros privados directos.

---

## 5. Preguntas Frecuentes y Resolución de Problemas

#### ¿Qué ocurre si cambia mi dirección IP pública o reinicio mi router?
Nada. Tu identidad está ligada a la clave criptográfica local de tu dispositivo, no a tu dirección IP. Podrás volver a entrar con total normalidad.

#### He cambiado de ordenador o formateado el sistema, ¿puedo recuperar mi personaje?
Sí. Tu clave privada reside en `Mods/Pelican4Ever/data/device_key.json`. Si vas a cambiar de máquina, basta con copiar ese archivo a tu nuevo equipo. Si no dispones del archivo, contacta con el anfitrión en Discord para vincular la nueva huella de tu equipo a tu personaje preexistente.

#### El juego indica «Versión incompatible» o «P4E\\x02 requerida»:
Asegúrate de haber instalado la versión `0.2.0` de `Pelican4Ever` y de haber reiniciado el juego a través de SMAPI.

#### ¿Dónde reportar incidencias o sugerencias?
En el servidor oficial de Discord: canal `#bugs-feedback` o directamente por mensaje al equipo de desarrollo. ¡Muchas gracias por tu colaboración como tester!
