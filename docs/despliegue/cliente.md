# Modo cliente: jugar en un servidor Pelican 4Ever

Guía para instalar el mod en tu equipo (PC, Steam Deck, o el que uses) y entrar en un servidor
Pelican 4Ever: el oficial de pruebas o uno que hostees tú.

Documentos relacionados: [servidor-preview.md](../servidor-preview.md) (entrar en el servidor oficial
de pruebas) · [host-local.md](host-local.md) · [host-vps.md](host-vps.md)

---

## 1. Requisitos

| Elemento | Versión |
| --- | --- |
| Stardew Valley | 1.6.15 (Steam o GOG) |
| SMAPI | 4.1 o superior (4.1.10+ recomendado) |
| Pelican 4Ever | 0.2.0 (misma *major.minor* que el servidor) |
| Modpack | El mismo modpack y **las mismas versiones** que tenga el servidor |

El juego se arranca **siempre por SMAPI**, nunca por el ejecutable normal.

---

## 2. Modpack obligatorio

El servidor comprueba en el saludo inicial que el cliente declare **el modpack completo en la
versión exacta que tiene instalada**. Si falta un mod o la versión no coincide, el servidor
expulsa al cliente durante el handshake y no hay forma de entrar.

- Pide al anfitrión el modpack que usa (o la lista de mods y sus versiones) e instálalo **con esas
  versiones exactas**: la comparación es de versión completa, no de *major.minor*.
- Si el anfitrión actualiza un mod del pack, tú tienes que actualizarlo a la misma versión exacta
  o dejarás de entrar.
- Los mods que el servidor **no** tiene instalados no se exigen; los que tengas de más no se
  comprueban.
- El detalle de qué mods componen el modpack y sus versiones está en
  [../servidor-preview.md](../servidor-preview.md).

---

## 3. Instalación

1. Copia la carpeta `Pelican4Ever` (descomprimida del ZIP `Pelican4Ever 0.2.0.zip`) dentro de
   `Mods/`:
   - **Linux / Steam Deck:** `~/.steam/steam/steamapps/common/Stardew Valley/Mods/`
     (o `~/.local/share/Steam/steamapps/common/Stardew Valley/Mods/`)
   - **Windows:** `C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\Mods\`
2. Instala el resto del modpack en la misma carpeta `Mods/` (una carpeta por mod), con las
   versiones exactas que use el servidor.
3. Comprueba que queda `.../Mods/Pelican4Ever/manifest.json`.
4. **No copies el `config.json` del servidor**: en cliente puedes borrar el que se genere y SMAPI
   lo reconstruye con los valores por defecto. En cliente solo importan las opciones de red; las
   de anfitrión (`AutoHostSaveName`, `RestartExitsProcess`, `HeadlessRendering`) no aplican.

### 3.1 Primer arranque y claves

Arranca el juego por SMAPI. El mod genera tu par de claves criptográficas en:

```
Mods/Pelican4Ever/data/device_key.json
```

En Linux y macOS el mod deja el fichero en permisos `0600` automáticamente (tu clave privada no
debe ser legible por otros usuarios).

---

## 4. Conectar al servidor

1. En el menú principal: **Cooperativo → Unirse a partida LAN** (*Conexión Directa*).
2. Escribe la **IP y el puerto** que te dé el anfitrión (por defecto `24642`).
   - Sin Steam abierto no hay códigos de invitación: se entra siempre por IP.
3. El saludo inicial es criptográfico (`P4E\x02`): el servidor te envía un desafío temporal de
   30 segundos y tu cliente lo firma con tu clave privada. **Nunca se transmite tu clave privada ni
   ningún token reutilizable** por el canal de juego.

### 4.1 Primer contacto: emparejamiento

La primera vez, el servidor comprueba tu **huella digital** (SHA-256 de tu clave pública). Como no
estás autorizado todavía, te desconecta limpiamente y te muestra algo como:

> Dispositivo registrado para aprobación. Huella: a1b2c3d4e5f6. Solicita al propietario que apruebe
> tu acceso en Discord.

Alternativas que verás según el estado de la lista blanca:

| Mensaje | Qué significa | Qué hacer |
| --- | --- | --- |
| `Dispositivo registrado para aprobación. Huella: …` | Tu solicitud está pendiente | Envía la huella al anfitrión y espera a que la apruebe |
| `Servidor protegido por lista blanca. Solicita una invitación al propietario en Discord.` | No hay ninguna invitación para tu IP | Pide al anfitrión que cree una invitación para tu IP |
| `Todas las invitaciones para tu IP están reservadas por otras solicitudes pendientes.` | Hay invitaciones para tu IP, pero ya están reservadas por otra huella | Pide una invitación nueva para tu alias o que apruebe la pendiente |

**La huella no depende de tu IP**: si te cambian la IP o reinicias el router, sigues entrando con
la misma aprobación.

Una vez aprobado, vuelves a pulsar *Conexión Directa* y entras al selector de peones:

- Los peones **libres** se pueden elegir: quedan vinculados de forma permanente a tu dispositivo.
- Los peones de **otros jugadores** aparecen atenuados, con la etiqueta `[De otro jugador]` y el
  clic desactivado.

---

## 5. Lo que puedes hacer dentro

| Comando | Qué hace |
| --- | --- |
| `/g <mensaje>` | Chat global del servidor |
| `/l <mensaje>` | Chat local por proximidad (solo te oyen los que están cerca) |
| `/w <jugador> <mensaje>` | Susurro directo a otro vecino |
| `/residencia` | Permisos de tu residencia: `Cerrada`, `Solo amigos`, `Pública`, lista de amigos y permiso de visita con herramientas |
| `/evento empezar` | Apuntarte a la tanda del festival del día |

La Plaza Central (`Farm`) es el mapa principal: desde ahí, y por los puentes hacia el este, están
los portales a las 16 residencias privadas. Cada vecino tiene su granja (8 tipos oficiales) y su
cueva privada.

---

## 6. Problemas frecuentes

### «Me expulsan al entrar»

El servidor comprueba tu versión del mod y del modpack en el saludo inicial y expulsa con el
motivo exacto en su log (`player.rejected`). Causas habituales:

| Motivo en el log del servidor | Solución |
| --- | --- |
| `no usa SMAPI.` | Arranca el juego por SMAPI, no por el ejecutable normal |
| `no tiene instalado Pelican 4Ever.` | Falta la carpeta `Mods/Pelican4Ever` |
| `versión incompatible (0.1.0; el servidor usa 0.2.0).` | La comparación del propio mod es por *major.minor*: reinstala la versión del servidor |
| `no tiene el modpack del servidor: falta <ID> <versión>.` | Falta ese mod: instálalo **con esa versión exacta** |
| `no tiene el modpack del servidor: <ID> <tuversión> (el servidor usa <versión>).` | Versión distinta: alinea la tuya con la del servidor |

> El cliente no recibe un mensaje descriptivo: el servidor lo expulsa. Por eso la huella del
> cliente es lo primero que hay que mirar.

### «He cambiado de ordenador o he formateado»

Copia `Mods/Pelican4Ever/data/device_key.json` al equipo nuevo: tu identidad viaja contigo y
sigas entrando con la misma aprobación. Si no tienes el archivo, pide al anfitrión que vincule tu
nueva huella a tu personaje (`p4e_migrate_player <huella> <peón>`).

### «Antes entraba y ahora no»

- ¿Actualizaste algún mod del pack sin querer? Las versiones tienen que ser **exactas**.
- ¿El servidor se reinició y cambió la lista blanca? Pide a tu anfitrión que revise
  `p4e_pair_list`.
- Si tu IP es compartida (NAT de empresa o de casa), puede haber invitaciones reservadas por
  otras huellas.

### «El juego va lento»

El servidor hace el trabajo pesado (simulación y red); tu equipo solo dibuja. Si va lento, mira
los gráficos y el mod que uses, no la partida.

---

## 7. Desinstalar

Borra la carpeta `Mods/Pelican4Ever` (y el resto del modpack si no lo usas para otra cosa).
Borrar `Mods/Pelican4Ever/data/device_key.json` genera una identidad nueva: el servidor la
tratará como un dispositivo sin autorizar y tendrás que volver a emparejarte.
