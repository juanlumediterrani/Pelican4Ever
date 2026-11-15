# Entrar al servidor preview (servidor oficial de pruebas)

El **servidor preview** es la partida oficial de Pelican 4Ever, abierta a testers y colaboradores
en la fase de pruebas. Es un servidor **real y persistente**: el reloj va a 1:1 con la hora real,
el día cambia solo a las 02:00 y la partida no se reinicia cuando tú te vas.

> **El servidor preview exige el modpack completo, en la versión exacta.** No es un servidor
> "abierto": si te falta un mod o la versión no coincide, el servidor te expulsa al entrar.

Documentos relacionados: [despliegue/cliente.md](despliegue/cliente.md) (guía completa de cliente) ·
[despliegue/cliente.en.md](despliegue/cliente.en.md)

---

## 1. Requisitos

| Elemento | Versión |
| --- | --- |
| Stardew Valley | 1.6.15 |
| SMAPI | 4.1 o superior |
| Pelican 4Ever | 0.2.0 |
| Modpack | **Versiones exactas** (tabla abajo) |
| Aprobación de dispositivo | Sí, hay que emparejarse |

### Modpack exigido por el servidor

| Mod | UniqueID | Versión |
| --- | --- | --- |
| Content Patcher | `Pathoschild.ContentPatcher` | 2.9.1 |
| SpaceCore | `spacechase0.SpaceCore` | 1.28.4 |
| Fashion Sense | `PeacefulEnd.FashionSense` | 7.5.0 |
| Dr4co Dragon Body | `Dr4co.Stardew.FashionSenseBody` | 0.5.0 |
| Nature In The Valley | `Nature.NatureInTheValley` | 1.3.4 |
| Nature In The Valley Content | `Nature.NatureInValleyContent` | 1.3.4 |
| SH's Wild Animals In The Valley | `SmolHooman.WildAnimalsInTheValley` | 1.2.1 |
| Pelican 4Ever NITV Compat | `Pelican4Ever.NITVCompat` | 1.0.0 |

Los mods que el servidor **no** tiene instalados no se exigen. Los que tengas de más no se
comprueban. El listado completo está en la tabla de esta misma guía.

---

## 2. Paso a paso

### Paso 1 · Entra en el Discord

Únete al servidor oficial: <https://discord.gg/2x5RuvvQB>

Ahí se avisa de las ventanas de prueba, de los cambios de versión del modpack y se recogen los
reportes de errores.

### Paso 2 · Instala el mod y el modpack

Descomprime `Pelican4Ever 0.2.0.zip` dentro de tu carpeta `Mods/` e instala el resto del modpack
con **las versiones exactas** de la tabla de arriba. Detalle de rutas y permisos en
[despliegue/cliente.md](despliegue/cliente.md).

### Paso 3 · Conecta por IP directa

En el juego: **Cooperativo → Unirse a partida LAN** (*Conexión Directa*), con la **IP y el puerto
del servidor preview** que se dan en el Discord.

### Paso 4 · Tu primer intento: la huella

El servidor está protegido por lista blanca de dispositivos. En tu primer intento:

1. El mod genera (o ya tiene) tu clave en `Mods/Pelican4Ever/data/device_key.json`.
2. El saludo criptográfico (`P4E\x02`) valida que tu clave es auténtica.
3. Como tu dispositivo no está autorizado, el servidor **registra la solicitud y te desconecta**
   mostrando tu **huella pública corta**: 12 hexadecimales, por ejemplo `a1b2c3d4e5f6`.

### Paso 5 · Pide la aprobación

Envía por **mensaje privado (DM)** a **Dextrure** en el Discord:

- tu nombre de jugador o alias,
- tu **huella corta** de 12 hexadecimales.

La aprobación es **permanente**: se liga a tu clave de dispositivo, no a tu IP. Podrás entrar
desde el router, con IP dinámica, sin volver a emparejarte.

### Paso 6 · Entra

Vuelve a *Conexión Directa* con la misma IP y puerto. Esta vez el servidor reconoce tu huella y
abrirá el **selector de peones**:

- **Tester nuevo:** elige cualquier ranura libre. Queda vinculada de forma permanente y exclusiva a
  tu dispositivo.
- **Ranuras de otros jugadores:** aparecen atenuadas con la etiqueta `[De otro jugador]` y el clic
  está desactivado.

Si tienes un personaje que ya usabas antes (por ejemplo, porque venías de la beta cerrada),
pídeselo al anfitrión para que lo vincule a tu nueva huella.

---

## 3. Qué hay que probar

Esta es una fase de pruebas: lo que reportes alimenta el backlog. Lo que conviene mirar:

| Sistema | Qué observar |
| --- | --- |
| **Reloj 1:1** | La hora del juego coincide con la hora real (1 día real = 1 día de juego) |
| **Madrugada continua** | Entre la **01:50** y las **06:00** el reloj se congela en `2550`: se puede jugar de noche sin desmayo |
| **Cambio de día** | A las **02:00** la fecha avanza; aviso 10 minutos antes en el HUD y aviso de sonido |
| **Residencias** | La Plaza Central y los puentes hacia el este dan a las 16 residencias; cada vecino tiene granja (8 tipos) y cueva privada; `/residencia` cambia los permisos (`Cerrada`, `Solo amigos`, `Pública`) |
| **Descanso asíncrono** | Puedes acostarte y salir al menú sin pausar el mundo ni echar a nadie |
| **Saciedad y «Bien comido»** | Al comer cualquier alimento sube la barra; con ≥ 1/4 de saciedad, el buff «Bien comido» permite regenerar energía sentado |
| **Spa** | Una visita a las vías del tren regenera hasta 120 de energía y salud durante 2 horas reales |
| **Mercado vecinal** | Compra y venta de cosechas y productos artesanales, con cobro aunque el comprador estuviera desconectado |
| **Chat** | `/g` global, `/l` por proximidad, `/w` susurros privados |
| **Fauna de NITV** | Animales e insectos en las 16 residencias, en la plaza y en los puentes (requiere el pack de compatibilidad) |
| **Mascotas** | Recogida y adopción de mascotas desde la pestaña **Animales** del cliente |

---

## 4. Problemas frecuentes en el preview

### Me expulsan nada más entrar

Es el **modpack**: el servidor exige cada mod en su versión exacta y te expulsa durante el saludo
inicial. El log del servidor dice el motivo exacto (`player.rejected`):

- `no tiene el modpack del servidor: falta <ID> <versión>.` → falta ese mod.
- `no tiene el modpack del servidor: <ID> <tuversión> (el servidor usa <versión>).` → versión
  distinta.
- `no usa SMAPI.` → estás arrancando el juego sin SMAPI.
- `versión incompatible (…)` → tu versión del mod no coincide (se compara por *major.minor*).

### «Servidor protegido por lista blanca…»

No hay ninguna invitación activa para tu IP. Pídesela en el Discord antes de intentarlo otra vez:
si tu IP es compartida (casa, empresa, universidad), puede que haya invitaciones reservadas por
otra huella.

### «Todas las invitaciones para tu IP están reservadas…»

Ya hay una solicitud pendiente con otra huella desde tu IP. Espera a que la aprueben o pídele al
anfitrión una invitación nueva con tu alias.

### Me han cambiado la IP y ahora no entro

La huella no depende de la IP: si te aprueban una vez, sigues entrando. Si te expulsan, el
problema es de versión de mod o de que el servidor se reinició con la lista blanca recargada:
pregunta en el Discord.

### He cambiado de ordenador

Copia `Mods/Pelican4Ever/data/device_key.json` al equipo nuevo y entrarás con la misma
aprobación. Si no lo tienes, pide que vinculen tu nueva huella a tu personaje.

### El juego va lento o se me desconecta

- El servidor es persistente y la carga la lleva él; si tu equipo va lento, mira tus gráficos.
- Desconexiones puntuales son normales en una red doméstica: el mundo sigue vivo y vuelves a
  entrar con la misma huella.

---

## 5. Dónde reportar

- **Discord:** canal `#bugs-feedback` o mensaje directo al equipo de desarrollo.
- Incluye: qué estabas haciendo, qué esperabas, qué pasó, y la **huella corta** de tu dispositivo
  (la de 12 hexadecimales) si es un problema de conexión.
