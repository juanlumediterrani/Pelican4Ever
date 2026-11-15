# Despliegue del anfitrión en un VPS (servidor 24/7)

Guía para dejar Pelican 4Ever sirviendo una partida de forma permanente en un servidor Linux
remoto, sin escritorio y sin que nadie tenga que abrir el juego a mano.

- **No es** un «servidor dedicado» nativo del juego: el anfitrión sigue siendo una partida de
  cooperativo normal (`Game1.multiplayerMode = 2`) con un jugador local (el propio host).
  `DedicatedServerMode` en `config.json` **no hace nada** en el código actual: no lo uses como
  interruptor de nada.
- Lo que sí es propio del despliegue en VPS es **no dibujar** (`HeadlessRendering`) y **no tener
  GPU**: el juego necesita un contexto gráfico aunque no pinte nada, y eso lo resuelve `Xvfb`.

Documentos relacionados: [host-local.md](host-local.md) · [cliente.md](cliente.md) ·
[../host-api.md](../host-api.md) · [../servidor-preview.md](../servidor-preview.md)

---

## 1. Requisitos

| Recurso | Mínimo | Recomendado |
| --- | --- | --- |
| CPU | 2 vCPU | 4 vCPU (16 jugadores) |
| RAM | 2 GB | 4 GB |
| Disco | 20 GB | 40 GB (partida + copias + checkpoints) |
| SO | Debian 12 / Ubuntu 22.04 x86-64 | cualquiera con systemd |
| Red | salida a internet + puerto UDP `24642` entrante | — |

Medido en una VM de 2 vCPU con la partida cargada, sin jugadores remotos y con
`HeadlessRendering` activo: **~23 % de CPU** del juego (el dibujado por CPU era el 94 %).

---

## 2. Preparar el sistema

```bash
sudo apt update
sudo apt install -y xvfb libgl1-mesa-dri libglu1-mesa curl unzip git
sudo useradd --system --create-home --home-dir /var/lib/pelican4ever --shell /usr/sbin/nologin pelican4ever
sudo mkdir -p /opt/pelican4ever/game /var/lib/pelican4ever/.config/StardewValley/Saves \
              /var/lib/pelican4ever/.config/StardewValley/Pelican4Ever_Backups \
              /etc/pelican4ever
sudo chown -R pelican4ever:pelican4ever /var/lib/pelican4ever /opt/pelican4ever
```

`Xvfb` es obligatorio: sin contexto gráfico el juego no arranca. `libgl1-mesa-dri` aporta el
render por software (`llvmpipe`) que usa la pantalla virtual.

---

## 3. Instalar el juego y SMAPI

Stardew Valley **no se puede ejecutar sin una licencia**: usa SteamCMD con la cuenta que posee el
juego, o copia la carpeta del juego desde un equipo que lo tenga.

### Opción A · SteamCMD (recomendada, el juego se actualiza solo)

```bash
sudo apt install -y steamcmd
sudo mkdir -p /var/lib/steamcmd
sudo ln -sf /var/lib/steamcmd/steamcmd.sh /usr/bin/steamcmd

sudo -u pelican4ever steamcmd \
  +force_install_dir /opt/pelican4ever/game \
  +login <CUENTA_STEAM> \
  +app_update 413150 validate \
  +quit
```

### Opción B · Copia desde otro equipo

Copia la carpeta completa del juego (Windows o Linux) a `/opt/pelican4ever/game` y dale
propiedad a `pelican4ever`. Esta vía no se actualiza sola: repetirla en cada parche del juego.

### SMAPI

1. Descarga el paquete **Linux** de [smapi.io](https://smapi.io/) (SMAPI 4.1 o superior).
2. Descomprímelo **dentro de** `/opt/pelican4ever/game`, de forma que quede
   `/opt/pelican4ever/game/StardewModdingAPI`.
3. Instala el runtime que SMAPI necesita:

```bash
sudo apt install -y dotnet-runtime-6.0
```

Comprueba que arranca antes de seguir (debe abrir la pantalla de título en la pantalla virtual):

```bash
sudo -u pelican4ever env HOME=/var/lib/pelican4ever DISPLAY=:99 \
  xvfb-run -a sh -c 'cd /opt/pelican4ever/game && ./StardewModdingAPI'
```

---

## 4. Instalar el mod y el modpack

### 4.1 Compilar (en tu equipo de desarrollo)

El proyecto del mod **no se compila en CI**: necesita las DLL del juego y de SMAPI, que no
existen en un runner de GitHub. Compílalo localmente y sube el ZIP resultante.

```bash
cp local.props.example local.props      # ajusta <GamePath> si el juego no está en la ruta habitual
dotnet build Pelican4Ever.sln -c Release
```

La compilación deja:

- el mod desplegado en `<GamePath>/Mods/Pelican4Ever/` (propiedad `EnableModDeploy`),
- el paquete `Pelican4Ever/bin/Release/net6.0/Pelican4Ever 0.2.0.zip` (propiedad `EnableModZip`).

> `global.json` fija el SDK **8.0.427** con `rollForward: disable`: usa exactamente esa versión
> para compilar, o la build falla con CS9057.

### 4.2 Subir al servidor

```bash
scp "Pelican4Ever/bin/Release/net6.0/Pelican4Ever 0.2.0.zip" usuario@tu-vps:/tmp/
scp deploy/modpack/Pelican4Ever.NITVCompat usuario@tu-vps:/tmp/
```

En el VPS:

```bash
sudo -u pelican4ever mkdir -p /opt/pelican4ever/game/Mods
sudo -u pelican4ever unzip -o -q /tmp/'Pelican4Ever 0.2.0.zip' -d /opt/pelican4ever/game/Mods/
sudo -u pelican4ever cp -r /tmp/Pelican4Ever.NITVCompat /opt/pelican4ever/game/Mods/
```

> `EnableModDeploy` **sobrescribe** `Mods/Pelican4Ever` en cada compilación. Si compilas en la
> misma máquina donde juegas, copia antes `config.json` y `data/device_key.json`.

### 4.3 Modpack obligatorio en el servidor

El servidor exige a los clientes el modpack completo **en la versión exacta que tiene el
servidor** (ver [../servidor-preview.md](../servidor-preview.md)). Instala en `Mods/`: Content Patcher, SpaceCore,
Fashion Sense, Dr4co Dragon Body, Nature In The Valley (+ su pack de contenido), SH's Wild Animals
In The Valley y el pack de compatibilidad `Pelican4Ever.NITVCompat`.

Si el servidor no tiene uno de esos mods, el ID aparece en la lista de requisitos pero **se ignora
con un aviso** en el log: el requisito solo existe si el servidor lo tiene instalado.

---

## 5. Crear la partida anfitriona

La pantalla de «Cooperativo → Anfitrión → Nueva granja» necesita interfaz gráfica, así que lo
práctico es **crear la partida en tu equipo** y copiarla al VPS:

1. En tu PC, con el mod instalado, crea la partida de anfitrión y anota el **nombre exacto de la
   carpeta** que aparece en `Saves/` (por ejemplo `Pelican4Ever_452974881`).
2. Copia esa carpeta al VPS:

```bash
scp -r "Saves/Pelican4Ever_452974881" usuario@tu-vps:/tmp/
sudo -u pelican4ever cp -r /tmp/Pelican4Ever_452974881 \
  /var/lib/pelican4ever/.config/StardewValley/Saves/
```

Ese nombre es el valor de `AutoHostSaveName`. El módulo de carga automática **nunca** elige «la
partida más reciente»: si el nombre no existe exactamente, no carga ninguna y lo dice en el log.

---

## 6. `config.json` del servidor

Vive en `/opt/pelican4ever/game/Mods/Pelican4Ever/config.json`. Plantilla completa en
[`config.example.json`](../../config.example.json). Lo imprescindible en un VPS:

```json
{
  "TimeZoneId": "Europe/Madrid",
  "AutoHostSaveName": "Pelican4Ever_452974881",
  "RestartExitsProcess": true,
  "HeadlessRendering": true,
  "RequireClientMod": true,
  "RequirePairingWhitelist": true,
  "RequiredClientMods": [
    "Pathoschild.ContentPatcher",
    "spacechase0.SpaceCore",
    "PeacefulEnd.FashionSense",
    "Dr4co.Stardew.FashionSenseBody",
    "Nature.NatureInTheValley",
    "Nature.NatureInValleyContent",
    "SmolHooman.WildAnimalsInTheValley",
    "Pelican4Ever.NITVCompat"
  ],
  "AdminApiPort": 8085,
  "AdminApiToken": "TOKEN-LARGO-Y-ALEATORIO",
  "AdminApiBindAllInterfaces": false,
  "AgentHost": false,
  "BackupRetentionDays": 30
}
```

Reglas que aplica el propio mod al leer el fichero:

- **Si la configuración es inválida, el mod no se registra** (log:
  «Configuración inválida: Pelican 4Ever queda DESACTIVADO hasta corregir config.json»).
  No hay modo degradado: o funciona todo, o no funciona nada.
- `AdminApiToken` vacío, con el valor por defecto `pelican4ever_secret_admin_token` o con el
  marcador `CAMBIA-ESTE-TOKEN` ⇒ **la API HTTP no arranca**. Pon un token propio largo.
- `AdminApiBindAllInterfaces: false` ⇒ la API escucha solo en `127.0.0.1:8085` y
  `localhost:8085`. Con `true` escucha en `http://*:8085/` (no lo hagas sin firewall).
- `RestartExitsProcess: true` **exige** `AutoHostSaveName` no vacío.
- `DayStartTime` debe ser exactamente `06:00` y `CalendarDayChangeTime` exactamente `02:00`.
- `CalendarEpoch` debe ser un **lunes** (en `yyyy-MM-dd`): el calendario perpetuo se deriva de ahí.
- `TimeZoneId` **no se valida**: un identificador IANA inexistente revienta en runtime.

### 6.1 Silenciar la consola de SMAPI

Bajo systemd la entrada estándar es `/dev/null`: SMAPI lee, recibe fin de fichero y **vuelve a
leer sin parar** (un núcleo entero de CPU). Desactívalo con `Mods/SMAPI-config.json`:

```json
{
  "ListenForConsoleInput": false
}
```

Los comandos de consola dejan de estar disponibles; se lanzan por la API de anfitrión
(`POST /api/v1/host/command`), que es una lista cerrada. Detalle en [../host-api.md](../host-api.md).

---

## 7. Instalar el servicio

Clona o sube el repositorio al VPS (el servicio solo necesita los ficheros de
`deploy/systemd/`) y copia las unidades:

```bash
sudo cp deploy/systemd/pelican4ever-vps-xvfb.service /etc/systemd/system/
sudo cp deploy/systemd/pelican4ever-vps.service        /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now pelican4ever-vps.service
```

La unidad es de **sistema**, no de usuario: no depende de que nadie haya iniciado sesión ni de
que exista un escritorio. Lanza el juego con `HOME=/var/lib/pelican4ever`, de modo que las
partidas y las copias viven fuera de `/opt`:

```
/var/lib/pelican4ever/.config/StardewValley/Saves/            partidas
/var/lib/pelican4ever/.config/StardewValley/Pelican4Ever_Backups/  copias diarias y checkpoints
/opt/pelican4ever/game/Mods/Pelican4Ever/                     mod + config.json
```

Si el directorio del juego no es el de la unidad, ajusta `STARDEW_DIR` en
`/etc/systemd/system/pelican4ever-vps.service` o crea `/etc/pelican4ever/host.env` (la unidad lo
carga si existe).

### Comprobar que está vivo

```bash
sudo systemctl status pelican4ever-vps
sudo journalctl -u pelican4ever-vps -f          # debe aparecer "[AutoHost] Cargando ... como anfitrión"
curl -s -H "Authorization: Bearer $TOKEN" http://127.0.0.1:8085/api/v1/host/clock
```

---

## 8. Red y firewall

| Puerto | Protocolo | ¿Se abre al exterior? |
| --- | --- | --- |
| `24642` | UDP | **Sí**, único puerto público del juego |
| `8085` | HTTP (API de anfitrión) | **No**: déjalo en loopback y accede por túnel SSH |
| `5900` | TCP (VNC) | No se usa en este despliegue (Xvfb va con `-nolisten tcp`) |

```bash
sudo ufw allow 24642/udp
sudo ufw enable
```

Sin VNC ni servidor gráfico, no hay forma de «ver» la partida. Para mirar la pantalla del host
pide un **préstamo de dibujado** por la API (1–120 min) o usa `p4e_render`:

```bash
curl -s -X POST http://127.0.0.1:8085/api/v1/host/render \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"requester":"admin","minutes":15}'
```

Acceso a la API desde tu portátil, por túnel SSH (no abras el puerto):

```bash
ssh -L 8085:127.0.0.1:8085 usuario@tu-vps
# luego, en el portátil: curl -H "Authorization: Bearer $TOKEN" http://127.0.0.1:8085/api/v1/host/clock
```

**Sin Steam abierto no hay códigos de invitación**: los jugadores entran por IP directa al
puerto `24642` (en el menú, *Conexión Directa*).

---

## 9. Operación diaria

### Comandos útiles (API de anfitrión)

```bash
TOKEN=TOKEN-LARGO-Y-ALEATORIO
H="Authorization: Bearer $TOKEN"

# Estado del reloj y del calendario
curl -s -H "$H" http://127.0.0.1:8085/api/v1/host/clock

# Quién está conectado
curl -s -H "$H" http://127.0.0.1:8085/api/v1/host/players

# Aviso a todos los jugadores
curl -s -X POST -H "$H" -H 'Content-Type: application/json' \
  -d '{"message":"Mantenimiento en 10 minutos","channel":"both"}' \
  http://127.0.0.1:8085/api/v1/host/announce

# Forzar el cambio de día (guarda + backup + cierre de peers)
# Ojo: p4e_rollover_now NO está en la lista de comandos remotos; se usa la ruta clásica.
curl -s -X POST -H "$H" http://127.0.0.1:8085/api/rollover

# Reinicio controlado tras el próximo guardado
curl -s -X POST -H "$H" -H 'Content-Type: application/json' \
  -d '{"reason":"Actualización del mod","immediate":false,"warningMinutes":5,"cancel":false}' \
  http://127.0.0.1:8085/api/v1/host/restart
```

El catálogo completo de rutas y de comandos remotos está en [../host-api.md](../host-api.md).

### Dar de alta a un jugador

1. Crea una **invitación** ligada a la IP del jugador (código de 6 caracteres, un solo uso,
   caducidad `PairingInviteTtlHours`, por defecto 72 h):
   ```bash
   curl -s -X POST -H "$H" http://127.0.0.1:8085/api/v1/host/command \
     -d '{"command":"p4e_pair_allow","args":["nombre","IP_DEL_JUGADOR"]}'
   ```
2. El jugador conecta una vez: el servidor registra la solicitud y lo desconecta mostrando su
   **huella corta** (12 hexadecimales).
3. Aprueba la huella:
   ```bash
   curl -s -X POST -H "$H" http://127.0.0.1:8085/api/v1/host/command \
     -d '{"command":"p4e_pair_approve","args":["a1b2c3d4e5f6"]}'
   ```
4. El jugador vuelve a conectar y entra.

`p4e_pair_list` muestra dispositivos autorizados, invitaciones vivas, solicitudes pendientes y
los últimos intentos bloqueados. `p4e_pair_revoke <usuario|huella|id>` revoca un dispositivo y
expulsa a sus jugadores conectados.

---

## 10. Actualizar el mod sin perder la partida

1. Compila en tu equipo (`dotnet build Pelican4Ever.sln -c Release`) y sube el ZIP.
2. Despliega el ZIP nuevo en `/opt/pelican4ever/game/Mods/` (sobrescribe la carpeta
   `Pelican4Ever/`; **no** borres `config.json`).
3. Reinicio controlado, que espera al siguiente guardado y avisa a los jugadores:
   ```bash
   curl -s -X POST -H "$H" -H 'Content-Type: application/json' \
     -d '{"reason":"Actualización del mod","immediate":false,"warningMinutes":5}' \
     http://127.0.0.1:8085/api/v1/host/restart
   ```
4. Con `RestartExitsProcess: true` el juego cierra el proceso; systemd lo relanza con el mod
   nuevo y `AutoHost` reabre la partida sola.
5. Los clientes con otra versión del mod son expulsados hasta que actualicen (`RequireClientMod`
   compara *major.minor*; el resto del modpack se compara por **versión exacta**).

Rollback: guarda el ZIP anterior, despliega el viejo y repite el reinicio controlado.

---

## 11. Copias de seguridad y checkpoints

- **Copias diarias:** las del cambio de día, en
  `/var/lib/pelican4ever/.config/StardewValley/Pelican4Ever_Backups/<partida>_<fecha UTC>/`, con
  verificación SHA-256. No se borran salvo que `BackupRetentionDays` sea mayor que 0; entonces se
  conservan las de esos días y **siempre** la más reciente.
- **Checkpoints intradía** (desactivados por defecto): con `CheckpointIntervalMinutes` mayor que 0
  el host guarda la partida cada ese intervalo sin cambiar de día ni desconectar a nadie, en
  `Pelican4Ever_Backups/checkpoints/`. Se conservan los últimos `CheckpointRetention`. No se hacen
  durante festivales ni en la ventana de preaviso del cambio de día.

### Restaurar una copia (con el servicio parado)

```bash
sudo systemctl stop pelican4ever-vps
cd /var/lib/pelican4ever/.config/StardewValley/Saves
mv Pelican4Ever_452974881 Pelican4Ever_452974881.antes-de-restaurar
cp -r /ruta/a/la/copia Pelican4Ever_452974881
gunzip Pelican4Ever_452974881/*.gz 2>/dev/null || true   # solo si venía de checkpoints/
sudo systemctl start pelican4ever-vps
```

---

## 12. Diagnóstico

| Síntoma | Causa habitual | Qué hacer |
| --- | --- | --- |
| `Address already in use` | El UDP `24642` ya está ocupado: otra instancia de Stardew o un proceso previo | `sudo ss -lunp \| grep 24642` · `pgrep -af Stardew` · el módulo espera hasta 60 s y luego lo dice en el log |
| «Configuración inválida» | Clave de `config.json` con valor no permitido | El log lista **todos** los errores; corrige y reinicia |
| La API responde 401 | Token ausente, incorrecto o con el valor inseguro por defecto | `curl -H "Authorization: Bearer $TOKEN" ...`; cambia `AdminApiToken` |
| La API no responde | `AdminApiPort <= 0`, token inseguro o partida no cargada | La API **solo** escucha con la partida cargada; mira el log |
| CPU al 100 % con la partida cargada | Dibujado por CPU activo | `HeadlessRendering: true` y `ListenForConsoleInput: false` en `SMAPI-config.json` |
| Un cliente es expulsado al entrar | Versión del mod o del modpack distinta | El log del servidor dice el motivo exacto (`player.rejected`) |
| La partida no carga al arrancar | `AutoHostSaveName` no coincide con la carpeta | El log dice `SaveMissing`; comprueba el nombre exacto de la carpeta en `Saves/` |
| El reloj va desfasado | `TimeZoneId` incorrecto o el servidor con otra zona | `Timedatectl` y `TimeZoneId` deben coincidir |

Comprobaciones rápidas:

```bash
sudo journalctl -u pelican4ever-vps -n 100 --no-pager
sudo systemctl show -p ActiveState,SubState,MemoryCurrent pelican4ever-vps
sudo du -sh /var/lib/pelican4ever/.config/StardewValley/*
```

---

## 13. Límites conocidos

- Solo **una** instancia del juego puede alojar partidas en red por máquina: el motor fija el
  puerto UDP `24642`.
- La partida se guarda **al cambiar de día** (a las 02:00). Un reinicio inesperado pierde lo hecho
  desde el último cambio de día: usa el reinicio controlado.
- Sin Steam abierto no hay códigos de invitación de Steam.
- El anfitrión duerme en su propia casa: con `AgentHost: true` registra el descanso **sin
  desconectarse**; con `false` (anfitrión humano) es lo normal.
