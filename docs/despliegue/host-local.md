# Despliegue del anfitrión en local (tu propio equipo)

Guía para montar y jugar la partida anfitriona en tu PC o Steam Deck: el juego se ve, lo gestionas
tú y los vecinos entran por IP directa.

Si lo que quieres es **un servidor 24/7 sin pantalla en un VPS**, usa
[host-vps.md](host-vps.md) en lugar de esta.

Documentos relacionados: [cliente.md](cliente.md) · [host-vps.md](host-vps.md) ·
[../host-api.md](../host-api.md) · [../servidor-preview.md](../servidor-preview.md)

---

## 1. Qué es «modo anfitrión local»

El anfitrión es el jugador que carga la partida de cooperativo. En local:

- El juego se abre **con ventana** y ves todo lo que pasa (puedes jugar tu turno).
- Puedes **dormir y cerrar** el juego sin tumbar a los demás: el mundo sigue vivo solo cuando el
  host vuelve a cargar la partida.
- Los vecinos conectan por *Conexión Directa* al puerto UDP `24642` de tu máquina.

Stardew Valley **no tiene modo servidor dedicado**: siempre hay un jugador local al frente
(el anfitrión). El flag `DedicatedServerMode` de `config.json` **no se usa en el código actual**.

---

## 2. Requisitos

- Stardew Valley **1.6.15** (Steam o GOG).
- **SMAPI 4.1 o superior** (instalado en el juego; el mod declara `MinimumApiVersion: 4.1.0`).
- El mod **Pelican 4Ever 0.2.0** y el modpack completo, en las mismas versiones que use el
  servidor (ver [../servidor-preview.md](../servidor-preview.md)).

Rutas habituales de `Mods/`:

| SO | Ruta |
| --- | --- |
| Linux / Steam Deck | `~/.steam/steam/steamapps/common/Stardew Valley/Mods/` (o `~/.local/share/Steam/steamapps/common/Stardew Valley/Mods/`) |
| Windows | `C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\Mods\` |

---

## 3. Instalar el mod

1. Compílalo en tu equipo de desarrollo (`dotnet build Pelican4Ever.sln -c Release`) o usa el ZIP
   `Pelican4Ever 0.2.0.zip` que te pase el equipo.
2. Descomprime el ZIP **dentro de** la carpeta `Mods/`, de forma que quede
   `Mods/Pelican4Ever/manifest.json`.
3. Instala también el modpack (Content Patcher, SpaceCore, Fashion Sense, Dr4co Dragon Body,
   Nature In The Valley, SH's Wild Animals In The Valley y `Pelican4Ever.NITVCompat`): el servidor
   exige a los clientes **la versión exacta que tiene el host**.
4. Arranca el juego **por SMAPI** (el lanzador `StardewModdingAPI`), no el ejecutable normal.

Al arrancar, el mod genera `Mods/Pelican4Ever/data/device_key.json` con tu par de claves
(ECDSA P-256). En Linux y macOS los deja en `0600`.

---

## 4. Crear la partida anfitriona

En el juego: **Cooperativo → Anfitrión → Nueva granja**. Elige la granja del anfitrión y la
plantilla que quieras (Standard, Riverland, Forest, Hilltop, Wilderness, Four Corners, Beach o
Meadowlands). Guarda la partida y anota el **nombre exacto de la carpeta** que aparece en `Saves/`
(por ejemplo `Pelican4Ever_452974881`).

Ese nombre sirve para dos cosas:

- **`AutoHostSaveName`** en `config.json`: al llegar a la pantalla de título, el mod carga esa
  partida como anfitrión solo. Si el nombre no existe exactamente, **no carga ninguna otra**:
  lo deja escrito en el log.
- La carpeta donde vive el sidecar de emparejamiento: `Saves/<partida>/pelican4ever_pairing.json`.

---

## 5. `config.json` local

Está en `Mods/Pelican4Ever/config.json` (SMAPI lo crea solo con los valores por defecto si no
existe; plantilla completa en [`config.example.json`](../../config.example.json)).

```json
{
  "TimeZoneId": "Europe/Madrid",
  "AutoHostSaveName": "Pelican4Ever_452974881",
  "RestartExitsProcess": false,
  "HeadlessRendering": false,
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
  "AgentHost": false
}
```

Notas para el caso local:

- **`HeadlessRendering: false`**: estás mirando la pantalla, no la razón para apagar el dibujado.
  Si dejas la partida cargada sin mirarla, ponlo en `true` para ahorrar CPU.
- **`RestartExitsProcess: false`**: el reinicio controlado vuelve a la pantalla de título y la
  carga automática reabre la partida en el mismo proceso (3 s). Con `true` el juego se cierra y
  quien lo relance (systemd, un script) lo vuelve a abrir con el mod nuevo.
- **`AdminApiBindAllInterfaces: false`**: la API queda solo en `127.0.0.1`. Con
  `true` se escuchan en todas las interfaces: no lo hagas en una red pública sin firewall.
- **`AdminApiToken`**: la API **no arranca** si está vacío, si conserva el valor por defecto
  `pelican4ever_secret_admin_token` o si es el marcador `CAMBIA-ESTE-TOKEN`.
- **Si el `config.json` es inválido, el mod no se registra** (log: «Configuración inválida: Pelican
  4Ever queda DESACTIVADO hasta corregir config.json»). No hay modo degradado.

---

## 6. Abrir el juego al mundo

El juego abre el servidor de cooperativo en el **puerto UDP 24642** (lo fija el motor 1.6.15).
Solo una instancia puede alojar por máquina.

- **Vecinos en tu misma red:** pasa la IP local del equipo que aloja el juego
  (`ip addr` / `ipconfig`) y el puerto `24642`.
- **Vecinos por internet:** abre el UDP `24642` en el router (redirección de puerto) y en el
  firewall del sistema, y usa tu IP pública. Si tu IP cambia, el emparejamiento por huella sigue
  funcionando (la identidad no depende de la IP), pero los vecinos tienen que apuntar a la nueva.
  Un VPS con IP fija evita este problema: es la opción recomendada para grupos estables.

> **Steam abierto no hace falta**: sin él no hay códigos de invitación de Steam, se entra por
> *Conexión Directa*.

---

## 7. Dar de alta a los vecinos

El servidor está protegido por lista blanca de dispositivos (`RequirePairingWhitelist`). El flujo
completo, paso a paso, está en [cliente.md](cliente.md). Resumen desde el anfitrión:

1. **Crea una invitación** ligada a la IP del vecino. Sale un código de 6 caracteres, de un solo
   uso, válido `PairingInviteTtlHours` (72 h por defecto):
   ```
   p4e_pair_allow <nombre> <IP_DEL_VECINO>
   ```
2. El vecino intenta conectar: el servidor registra su dispositivo y lo desconecta mostrando su
   **huella corta** (12 hexadecimales, p. ej. `a1b2c3d4e5f6`).
3. **Aprueba la huella**:
   ```
   p4e_pair_approve a1b2c3d4e5f6
   ```
   (o `p4e_pair_approve <huella> <id_de_peón>` para aprobar y además vincular el personaje).
4. El vecino vuelve a conectar y entra al selector de peones. Los peones de otros jugadores salen
   atenuados con `[De otro jugador]` y no se pueden seleccionar.

Comandos de apoyo:

| Comando | Qué hace |
| --- | --- |
| `p4e_pair_list` | Dispositivos autorizados, invitaciones vivas, solicitudes pendientes, intentos bloqueados |
| `p4e_pair_reject <huella>` | Rechaza una solicitud pendiente |
| `p4e_pair_revoke <usuario\|huella\|id>` | Revoca el acceso de un dispositivo y expulsa a sus jugadores |
| `p4e_claim_list` | Lista peones y quién es su dueño |
| `p4e_claim_reset <nombre_o_id>` | Libera la posesión de un personaje para que su dueño lo reclame |

> La lista de comandos remotos por API es **más corta** que la consola: `p4e_pair_reject` y
> `p4e_claim_list` solo están en consola (la API sí expone `p4e_claim_list`, `p4e_pair_list`,
> `p4e_pair_allow`, `p4e_pair_approve`, `p4e_migrate_player`, `p4e_claim_reset`,
> `p4e_pair_revoke`). Catálogo exacto en [../host-api.md](../host-api.md).

---

## 8. API en local

Con la partida cargada, la API está disponible:

```bash
TOKEN=TOKEN-LARGO-Y-ALEATORIO
curl -s -H "Authorization: Bearer $TOKEN" http://127.0.0.1:8085/api/v1/host/clock
```

No hay panel web en esta versión: el panel vive en la rama `feat/web-panel` y no está en
`main` ni en `dev`. Todo lo que hace se hace por la API (`/api/v1/host/`).

---

## 9. Cuando terminas de jugar

- Puedes **acostarte y salir al menú** sin tumbar a los demás: cada jugador duerme y se desconecta
  por separado. Si el anfitrión es humano, cerrar el juego **deja la partida sin servidor** hasta
  que se vuelva a cargar.
- Para que el mundo siga vivo sin nadie delante, deja el anfitrión desatendido: `AgentHost: true`
  hace que el descanso del host se registre **sin desconectarlo ni salir al título**.
- Con `AutoHostSaveName` puesto, volver al título a mano reabre la partida a los 3 s: para
  detener el servidor, detén el proceso (en systemd: `systemctl --user stop pelican4ever-host`).

Unidades systemd ya disponibles para un host local desatendido:
`deploy/systemd/pelican4ever-host.service` (usuario, con sesión gráfica) y
`deploy/systemd/pelican4ever-preprod.service` + `pelican4ever-preprod-xvfb.service`
(usuario, pantalla virtual propia, `HOME`/`XDG_*` aislados).

---

## 10. Problemas frecuentes

| Síntoma | Causa | Solución |
| --- | --- | --- |
| El juego arranca pero el mod no aparece | `config.json` inválido | El log de SMAPI lista todos los errores de configuración |
| El vecino es expulsado al entrar | Versión del mod o del modpack distinta | El log del servidor dice el motivo exacto; el `player.rejected` del API lo recoge |
| «Servidor protegido por lista blanca…» | El dispositivo no está autorizado | Crea una invitación con `p4e_pair_allow` y aprueba la huella |
| «Todas las invitaciones para tu IP están reservadas…» | Hay invitaciones de otra huella para esa IP (NAT) | Crea otra invitación para ese alias o aprueba la pendiente |
| La partida no carga al reabrir el juego | `AutoHostSaveName` no coincide | Revisa el log (`SaveMissing`) y el nombre exacto de la carpeta en `Saves/` |
| `Address already in use` | Otra instancia de Stardew o el puerto 24642 ocupado | `ss -lunp \| grep 24642` y `pgrep -af Stardew` |
| El vecino ve la partida pero no los animales de NITV | Falta `Pelican4Ever.NITVCompat` en su cliente | Versión exacta del modpack en el cliente, obligatorio |
