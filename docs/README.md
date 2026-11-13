# Documentación de Pelican 4Ever

Índice de la documentación viva del proyecto. Lo que no aparece aquí (o está en
[`archive/`](archive/)) es histórico: informes de una fecha concreta, planes de una épica cerrada
o procedimientos que ya han sido sustituidos por estas guías.

## Guías de despliegue

| Guía | Para quién | Qué cubre |
| --- | --- | --- |
| [`despliegue/host-vps.md`](despliegue/host-vps.md) | Anfitrión | Servidor 24/7 en un VPS Linux: instalación, `Xvfb`, systemd, firewall, copias, actualizaciones y diagnóstico |
| [`despliegue/host-local.md`](despliegue/host-local.md) | Anfitrión | Partida anfitriona en tu propio equipo: configuración, altas de jugadores, panel web |
| [`despliegue/cliente.md`](despliegue/cliente.md) | Jugador | Instalar el mod y el modpack, conectarse a un servidor, emparejamiento, comandos de chat |
| [`despliegue/cliente.en.md`](despliegue/cliente.en.md) | Jugador | The same guide, in English |
| [`servidor-preview.md`](servidor-preview.md) | Tester | Entrar en el **servidor oficial de pruebas**, con sus requisitos de modpack y la aprobación de dispositivo |
| [`servidor-preview.en.md`](servidor-preview.en.md) | Tester | The same guide, in English |

## Referencia

| Documento | Contenido |
| --- | --- |
| [`host-api.md`](host-api.md) | API REST de anfitrión v1: rutas, autenticación, comandos remotos permitidos y sucesos |
| [`adr/`](adr/) | Registros de decisiones de arquitectura (ADR) |
| [`../backlog.md`](../backlog.md) | Hoja de ruta técnica: épicas, tareas y su resolución |
| [`../architecture.md`](../architecture.md) | Arquitectura técnica del sistema |
| [`../design.md`](../design.md) | Diseño de juego |
| [`../agents/README.md`](../agents/README.md) | Cosmética del host (sprite del dragón) y utilidades de pixel art |

## Reglas de la casa

- El juego es **Stardew Valley 1.6.15** con **SMAPI 4.1+**.
- El mod se llama **Pelican 4Ever** (`Pelican4Ever.SVRTMP`), versión actual **0.2.0**.
- El servidor expone **un solo puerto público**: UDP `24642`. La API de anfitrión (`8085`) se
  queda en loopback.
- `config.json` se **valida al arrancar**: si algo no cuadra, el mod no se registra. No hay modo
  degradado.
- El anfitrión es siempre un jugador local: **no existe modo servidor dedicado nativo**. La
  opción `DedicatedServerMode` no se usa en el código actual.
- Las versiones del modpack que exige el servidor son **exactas** (no *major.minor*). La del
  propio mod se compara por *major.minor*.
- La identidad de cada jugador es su **clave de dispositivo** (ECDSA P-256), no su IP: cambiar de
  IP no obliga a volver a emparejarse.

## Historial

Los documentos archivados están en [`archive/`](archive/) con una nota al principio de cada uno
explicando su estado. No los borres sin revisar: algunos contienen el detalle técnico (formatos de
datos, inventarios de bugs) que todavía no se ha trasplantado a las guías vivas.
