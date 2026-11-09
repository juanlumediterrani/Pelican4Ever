# Guía del Modpack: Pelican 4Ever + Nature In The Valley + Dr4co Host

Esta guía define la composición, dependencias, mitigaciones y despliegue del **Modpack Oficial** para servidores multijugador persistentes 24/7 de **Pelican 4Ever (`SV-RTMP`)**.

---

## 📦 Lista de Mods del Modpack (Bill of Materials)

Todos los mods listados a continuación deben estar instalados en el **servidor anfitrión** y en **todos los clientes** para garantizar sincronización de red y evitar errores de ítems nulos o desconexiones:

| Mod / Paquete | Autor | Rol / Dependencia | Nexus / Ubicación |
| :--- | :--- | :--- | :--- |
| **Pelican 4Ever (`SV-RTMP`)** | Pelican 4Ever Team | Mod principal de persistencia 24/7, reloj 1:1, residencias y economía | [Repositorio local](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG) |
| **SMAPI (4.1+)** | Pathoschild | Cargador base de mods para Stardew Valley 1.6+ | [smapi.io](https://smapi.io/) |
| **Content Patcher (1.30+)** | Pathoschild | Framework para inyección de datos y assets sin alterar binarios | [Nexus #1915](https://www.nexusmods.com/stardewvalley/mods/1915) |
| **SpaceCore (1.24+)** | spacechase0 | Framework de C# requerido por NITV para herramientas y skills | [Nexus #1348](https://www.nexusmods.com/stardewvalley/mods/1348) |
| **Fashion Sense (7.5.0+)** | PeacefulEnd | **Requerido para el sprite del Host.** Framework de cuerpos/animaciones | [Nexus #9969](https://www.nexusmods.com/stardewvalley/mods/9969) |
| **Dr4co Dragon Body** | Dr4co / Codex | Pack cosmético para Fashion Sense que viste al Host como dragón | [`agents/Dr4co.FashionSense`](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/agents/Dr4co.FashionSense) (PR #1) |
| **Nature In The Valley (NITV) 1.3.4** | w8AwA8w | Mecánica de captura de fauna con red, terrarios e Insectario | [Nexus #30138](https://www.nexusmods.com/stardewvalley/mods/30138) |
| **NITV Multiplayer Support** | XanCreations | **Parche obligatorio de concurrencia** para spawns con múltiples jugadores | [Nexus #30138 (Addons)](https://www.nexusmods.com/stardewvalley/mods/30138) |
| **NITV - SH's Wild Animals In The Valley** | plurffle (SmolHooman) | Paquete de fauna salvaje ambiental y agresiva para el framework NITV | [Nexus #42968](https://www.nexusmods.com/stardewvalley/mods/42968) |
| **Pelican 4Ever NITV Compat** | Pelican 4Ever Team | Pack de Content Patcher que mapea las 16 residencias privadas en NITV | [`deploy/modpack/Pelican4Ever.NITVCompat`](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/deploy/modpack/Pelican4Ever.NITVCompat) |

---

## 🐲 Sprite Personalizado del Host (Dr4co Dragon Body)

El PR `#1` (`codex/dr4co-agent-sprite`) añade el pack cosmético en la carpeta `agents/Dr4co.FashionSense`.

### Dependencia identificada
* Requiere **Fashion Sense 7.5.0 o posterior** (`PeacefulEnd.FashionSense`).
* **Nota de arquitectura:** Pelican 4Ever no depende directamente de Fashion Sense en su DLL de C#; es una dependencia visual gestionada a través de la carpeta `Mods/`.

### Cómo seleccionarlo en el Host
1. Instala `Fashion Sense` y la carpeta `agents/Dr4co.FashionSense` dentro del directorio `Mods/` de la instalación de Stardew Valley.
2. Al iniciar la partida con el Host, utiliza el **Espejo de Mano de Fashion Sense** (accesible desde el inventario o menú de inicio de Fashion Sense).
3. En la pestaña de cuerpo (*Body*), selecciona **Dr4co**.
4. El personaje mantendrá todas las animaciones de movimiento en 4 direcciones y el uso de herramientas nativas del granjero.

---

## ⚙️ Mitigación de Colisiones y Ajustes de Configuración

### 1. Parche de Concurrencia Multijugador y Aislamiento de Spawns
* **Motivo:** En NITV vainilla, el evento `Player.Warped` de SMAPI se activa cuando cualquier jugador de la partida cambia de mapa. Al no verificar si el jugador es local, el cambio de mapa de un granjero provocaba que los clientes remotos recalculasen sus árboles y vaciasen su fauna activa (`ClearUnclaimedCreatures`).
* **Solución nativa en Pelican 4Ever:** [`FarmSlotModule.cs`](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/Pelican4Ever/Modules/FarmSlotModule.cs) incluye parches Harmony automáticos que interceptan `OnWarp` para aislar el ciclo exclusivamente al granjero local (`eventArgs.IsLocalPlayer`) y enrutar las etiquetas de bioma (`validLocation`) hacia `Forest` y `Beach`. No se requiere instalar librerías externas adicionales.

### 2. Aumento del Límite de Apariciones (*Spawn Cap*)
En el archivo `config.json` generado por Nature In The Valley (`Mods/NatureInTheValley/config.json`):
* Aumentar `MaxSpawnsPerLocation` (o multiplicador de densidad) a un factor de **2.5x o 3x**.
* Esto asegura que cuando haya 16 granjeros activos recorriendo el Valle simultáneamente, todos encuentren fauna sin agotar la cuota global de la partida.

### 3. Spawns en las 16 Residencias Privadas, Plaza Central y Puentes
* Las criaturas de NITV y SH's Wild Animals se clasifican mediante etiquetas oficiales de entorno en `Data/Locations`:
  * `"Nature.NIV.Outside"`: Habilitado en las 16 parcelas privadas, todos los anexos perimetrales, la Plaza Central del Host (`Farm`) y en todos los mapas puente de la plaza (`Pelican4Ever_Bridge_1..3`) mediante `FarmTemplateCatalog.ApplyNitvCustomFields`. Esto garantiza que la fauna salvaje exterior de *Nature In The Valley* aparezca de forma continua y natural tanto en la granja anfitriona como en toda la cadena de puentes transitables.
  * `"Nature.NIV.Forest"`: Inyectado dinámicamente en parcelas y anexos de Recolección (*Foraging*) y Cuatro Esquinas (*FourCorners*). Permite la aparición de fauna de bosque sin depender de superar 60 árboles.
  * `"Nature.NIV.Beach"`: Inyectado dinámicamente en parcelas y anexos Fluviales (*Fishing*) y Costeros (*Beach*). Permite la aparición de fauna acuática y de litoral.
  * `"Nature.NIV.Underground"`: Inyectado en las cuevas privadas de cada residencia (`Pelican4Ever_Farm_XX_Cave`) para habilitar criaturas subterráneas.
* El pack Content Patcher [`deploy/modpack/Pelican4Ever.NITVCompat`](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/deploy/modpack/Pelican4Ever.NITVCompat) y el módulo C# [`FarmSlotModule.cs`](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/Pelican4Ever/Modules/FarmSlotModule.cs) mantienen sincronizadas estas propiedades en `Data/Locations`.

### 4. Dinámica con el Reloj en Tiempo Real 1:1 y Madrugada (*NightHold*)
* Durante la **Madrugada continua (*NightHold*)**, entre las 01:50 y las 06:00 de la vida real, el reloj del juego permanece fijo en `2550`.
* Las especies nocturnas de NITV y SH's Wild Animals (como polillas lunares, murciélagos, búhos o criaturas nocturnas) estarán activas continuamente durante esas 4 horas reales.
* Los animales diurnos no reaparecerán hasta el reinicio diario automático (*Rollover*) de las **06:00 AM reales**.

---

## 🔒 El servidor exige el modpack

`RequireClientMod` (activo por defecto) expulsa a quien entre sin SMAPI o sin Pelican 4Ever con la misma versión mayor.menor. Con **`RequiredClientMods`** el servidor exige también el resto del modpack: cada UniqueID de la lista, **en la versión exacta que tiene instalada el propio servidor** (no se repite en config.json; al actualizar un mod en el servidor, la exigencia cambia sola). Un ID que el servidor no tenga se ignora con un aviso.

```json
"RequiredClientMods": [
  "Pathoschild.ContentPatcher",
  "spacechase0.SpaceCore",
  "PeacefulEnd.FashionSense",
  "Dr4co.Stardew.FashionSenseBody",
  "Nature.NatureInTheValley",
  "Nature.NatureInValleyContent",
  "SmolHooman.WildAnimalsInTheValley",
  "Pelican4Ever.NITVCompat"
]
```

Al expulsar, el registro de SMAPI del servidor y el suceso `player.rejected` de la API de anfitrión dicen qué falta o qué versión no coincide (por ejemplo, `falta spacechase0.SpaceCore 1.28.4`). Los mods que el cliente tenga de más no se comprueban. Los mods que solo usa el servidor (el puente de Stardew Voyager) no van en la lista.

## 🚀 Despliegue del Modpack

1. **Estructura final del directorio `Mods/`:**
   ```text
   Stardew Valley/Mods/
   ├── ContentPatcher/
   ├── SpaceCore/
   ├── FashionSense/
   ├── Dr4co.FashionSense/
   ├── NatureInTheValley/
   ├── NITVMultiplayerSupport/
   ├── NITV - SH's Wild Animals In The Valley/
   ├── Pelican4Ever/
   └── Pelican4Ever.NITVCompat/
   ```
2. **Empaquetado para jugadores:**
   Comprime la carpeta anterior (excluyendo archivos de guardado o tokens personales en `data/client_token.json`) y distribúyela a todos los integrantes del servidor para que todos ejecuten exactamente la misma versión.
