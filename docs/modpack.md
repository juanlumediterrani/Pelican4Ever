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
| **Nature In The Valley (NITV)** | w8AwA8w | Mecánica de captura de fauna con red, terrarios e Insectario | [Nexus #30138](https://www.nexusmods.com/stardewvalley/mods/30138) |
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

### 1. Parche de Concurrencia Multijugador (NITV Multiplayer Support)
* **Motivo:** NITV vainilla mantiene una lista global de criaturas y calcula los spawns usando la posición del jugador anfitrión. En un servidor con 16 personas en mapas distintos, esto provoca que solo aparezcan criaturas donde esté el Host o un único jugador.
* **Instalación:** Copiar la carpeta de `NITV Multiplayer Support` en el directorio `Mods/` tanto del Host como de los clientes.

### 2. Aumento del Límite de Apariciones (*Spawn Cap*)
En el archivo `config.json` generado por Nature In The Valley (`Mods/NatureInTheValley/config.json`):
* Aumentar `MaxSpawnsPerLocation` (o multiplicador de densidad) a un factor de **2.5x o 3x**.
* Esto asegura que cuando haya 16 granjeros activos recorriendo el Valle simultáneamente, todos encuentren fauna sin agotar la cuota global de la partida.

### 3. Spawns en las 16 Residencias Privadas (`Pelican4Ever_Farm_01..16`)
* Las criaturas de granja buscan por defecto la ubicación `"Farm"` (que en P4E es la Plaza Central del Host).
* El pack incluido [`deploy/modpack/Pelican4Ever.NITVCompat`](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/deploy/modpack/Pelican4Ever.NITVCompat) inyecta metadatos en `Data/Locations` para que las parcelas `Pelican4Ever_Farm_01` a `Pelican4Ever_Farm_16` hereden la etiqueta de granja activa para NITV.
* Además, el módulo C# [`FarmSlotModule.cs`](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/Pelican4Ever/Modules/FarmSlotModule.cs) propaga automáticamente los `CustomFields` de las plantillas de granja hacia todas las residencias y sus anexos adquiridos.

### 4. Dinámica con el Reloj en Tiempo Real 1:1 y Madrugada (*NightHold*)
* Durante la **Madrugada continua (*NightHold*)**, entre las 01:50 y las 06:00 de la vida real, el reloj del juego permanece fijo en `2550`.
* Las especies nocturnas de NITV y SH's Wild Animals (como polillas lunares, murciélagos, búhos o criaturas nocturnas) estarán activas continuamente durante esas 4 horas reales.
* Los animales diurnos no reaparecerán hasta el reinicio diario automático (*Rollover*) de las **06:00 AM reales**.

---

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
