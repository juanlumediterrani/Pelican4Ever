> **Documento archivado (2026-11-13).** No lo actualices: Procedimiento de migración de partidas de una versión anterior (residencias incrustadas en la granja del host) a la Plaza Central y los puentes. De un solo uso; el script sigue en `scripts/migrate_save_to_plaza.py`.
>
> Se conserva como registro histórico.

# Guía de Migración de Partidas Guardadas: `origin/main` a Plaza Central y Puentes

Esta guía documenta el procedimiento oficial para migrar una partida guardada creada bajo la arquitectura previa de `origin/main` (donde los 16 accesos a residencias estaban incrustados exclusivamente en el mapa de la Granja Host) a la nueva arquitectura canónica de **Plaza Central y Puentes** (`refactor/residence-entrances`).

---

## 1. Contexto del Cambio Arquitectónico

En `origin/main`, el mod ubicaba las 16 residencias privadas en el perímetro de `Farm`:
- **4 en el borde oeste** (`x=1, y=16..46`), forzando salidas en medio del bosque y acantilado.
- **6 en el borde norte** (`y=1`).
- **6 en el borde sur** (`y=63`).

En la nueva arquitectura de **Plaza Central y Puentes** (`refactor/residence-entrances` / Épica 10):
- La **Granja Host (`Farm`)** recupera su amplitud y armonía visual vanilla, albergando únicamente **4 residencias maestras**:
  - `Farm_Portal_01` (Noroeste, `18, 1`, borde Norte)
  - `Farm_Portal_02` (Noreste, `62, 1`, borde Norte)
  - `Farm_Portal_03` (Suroeste, `18, 63`, borde Sur)
  - `Farm_Portal_04` (Sureste, `62, 63`, borde Sur)
- El borde oeste en `(0..7, 26..27)` se transforma en el **Corredor Oeste**, un paseo transitable y señalizado que conecta la Granja Host con una cadena modular de **Puentes (`Pelican4Ever_Bridge_1..3`)**.
- Cada puente alberga 4 residencias (2 al norte y 2 al sur), escalando limpiamente según `FarmSlotCount` hasta un total de 16 jugadores.
- El bebedero de animales (`Pet Bowl`) se reubica fuera del cercado en `(53, 7)`, garantizando accesibilidad y estética 100% fiel a ConcernedApe.

---

## 2. Tabla Comparativa de Salidas

| Salida en `origin/main` | Ubicación previa | Posición previa | Borde previo | Nueva Salida | Nueva Ubicación | Nueva Posición | Nuevo Borde | Identificador de Parcela |
|---|---|---|---|---|---|---|---|---|
| **`Farm_Portal_01`** | `Farm` | `(1, 16)` | Oeste | **`Farm_Portal_05`** | `Pelican4Ever_Bridge_1` | `(8, 1)` | Norte | `Pelican4Ever_Farm_05` (o Farm 01 en modo Slot) |
| **`Farm_Portal_02`** | `Farm` | `(1, 26)` | Oeste | **`Farm_Portal_06`** | `Pelican4Ever_Bridge_1` | `(25, 1)` | Norte | `Pelican4Ever_Farm_06` (o Farm 02 en modo Slot) |
| **`Farm_Portal_03`** | `Farm` | `(1, 36)` | Oeste | **`Farm_Portal_07`** | `Pelican4Ever_Bridge_1` | `(8, 28)` | Sur | `Pelican4Ever_Farm_07` (o Farm 03 en modo Slot) |
| **`Farm_Portal_04`** | `Farm` | `(1, 46)` | Oeste | **`Farm_Portal_08`** | `Pelican4Ever_Bridge_1` | `(25, 28)` | Sur | `Pelican4Ever_Farm_08` (o Farm 04 en modo Slot) |
| **`Farm_Portal_05`** | `Farm` | `(18, 1)` | Norte | **`Farm_Portal_01`** | `Farm` | `(18, 1)` | Norte | `Pelican4Ever_Farm_01` (o Farm 05 en modo Slot) |
| **`Farm_Portal_06`** | `Farm` | `(28, 1)` | Norte | **`Farm_Portal_09`** | `Pelican4Ever_Bridge_2` | `(8, 1)` | Norte | `Pelican4Ever_Farm_09` (o Farm 06 en modo Slot) |
| **`Farm_Portal_07`** | `Farm` | `(52, 1)` | Norte | **`Farm_Portal_10`** | `Pelican4Ever_Bridge_2` | `(25, 1)` | Norte | `Pelican4Ever_Farm_10` (o Farm 07 en modo Slot) |
| **`Farm_Portal_08`** | `Farm` | `(62, 1)` | Norte | **`Farm_Portal_02`** | `Farm` | `(62, 1)` | Norte | `Pelican4Ever_Farm_02` (o Farm 08 en modo Slot) |
| **`Farm_Portal_09`** | `Farm` | `(18, 63)` | Sur | **`Farm_Portal_03`** | `Farm` | `(18, 63)` | Sur | `Pelican4Ever_Farm_03` (o Farm 09 en modo Slot) |
| **`Farm_Portal_10`** | `Farm` | `(28, 63)` | Sur | **`Farm_Portal_11`** | `Pelican4Ever_Bridge_2` | `(8, 28)` | Sur | `Pelican4Ever_Farm_11` (o Farm 10 en modo Slot) |
| **`Farm_Portal_11`** | `Farm` | `(52, 63)` | Sur | **`Farm_Portal_12`** | `Pelican4Ever_Bridge_2` | `(25, 28)` | Sur | `Pelican4Ever_Farm_12` (o Farm 11 en modo Slot) |
| **`Farm_Portal_12`** | `Farm` | `(62, 63)` | Sur | **`Farm_Portal_04`** | `Farm` | `(62, 63)` | Sur | `Pelican4Ever_Farm_04` (o Farm 12 en modo Slot) |
| **`Farm_Portal_13`** | `Farm` | `(8, 1)` | Norte | **`Farm_Portal_13`** | `Pelican4Ever_Bridge_3` | `(8, 1)` | Norte | `Pelican4Ever_Farm_13` |
| **`Farm_Portal_14`** | `Farm` | `(72, 1)` | Norte | **`Farm_Portal_14`** | `Pelican4Ever_Bridge_3` | `(25, 1)` | Norte | `Pelican4Ever_Farm_14` |
| **`Farm_Portal_15`** | `Farm` | `(8, 63)` | Sur | **`Farm_Portal_15`** | `Pelican4Ever_Bridge_3` | `(8, 28)` | Sur | `Pelican4Ever_Farm_15` |
| **`Farm_Portal_16`** | `Farm` | `(47, 63)` | Sur | **`Farm_Portal_16`** | `Pelican4Ever_Bridge_3` | `(25, 28)` | Sur | `Pelican4Ever_Farm_16` |

---

## 3. Modos de Reasignación

### A. Modo Conservación de Parcela (`--mode slot` - Recomendado por defecto)
- **Principio:** Cada jugador conserva exactamente su identificador de granja privada (`Pelican4Ever_Farm_XX`).
- **Qué ocurre:**
  - Los cultivos, árboles frutales, cofres, animales y edificios de cada jugador permanecen en su misma ubicación.
  - La puerta de acceso se actualiza para coincidir con el nuevo catálogo:
    - Granjas 01 a 04 acceden por la Plaza Central (Granja Host).
    - Granjas 05 a 08 acceden por el Puente 1.
    - Granjas 09 a 12 acceden por el Puente 2.
    - Granjas 13 a 16 acceden por el Puente 3.

### B. Modo Conservación Geográfica (`--mode geo`)
- **Principio:** Los jugadores que tenían una salida en una posición física específica de la Granja Host conservan esa misma ubicación física.
- **Qué ocurre:**
  - El jugador que poseía `Farm_Portal_05` en `(18, 1)` se traslada a `Farm_Portal_01` (que está exactamente en `18, 1`).
  - Su granja privada se reasigna a `Pelican4Ever_Farm_01`, y la herramienta renombra automáticamente en el archivo XML de guardado su mapa, cueva, bodega, invernadero y anexos para que **no pierda nada**.
  - Los jugadores del antiguo borde oeste son reubicados armoniosamente en el Puente 1.

### C. Modo Personalizado (`--mode custom` o `--mode interactive`)
- Permite definir equivalencias directas mediante un JSON o responder interactivamente en la terminal:
  ```json
  {
    "Farm_Portal_05": "Farm_Portal_01",
    "Farm_Portal_01": "Farm_Portal_05"
  }
  ```

---

## 4. Saneamiento y Sobrescritura de la Granja Host (`--clean-host`)

Cuando se juega una partida originaria de `origin/main`, el XML del guardado contiene elementos obsoletos que deben sanearse:
1. **Purgado de Warps:** Se eliminan los warps guardados en el XML que apuntan a parcelas desde coordenadas obsoletas (`x=1, y=16..46`, `x=28`, `x=52`, etc.), evitando teletransportes fantasma.
2. **Paso Oeste:** Se inyectan los warps canónicos del corredor oeste (`0 26 Pelican4Ever_Bridge_1 33 14` y `0 27 Pelican4Ever_Bridge_1 33 14`).
3. **Cuenco del Perro (Pet Bowl):** Se actualiza el edificio `Pet Bowl` a la coordenada exterior `(53, 7)` para evitar que quede atrapado dentro de las vallas de la casa o bloqueando caminos.
4. **Despeje de Escombros y Vegetación:** Se eliminan malezas, piedras, ramas y tocones en el rectángulo del corredor oeste (`x=0..7, y=25..28`) y en los accesos de los 4 portales activos.
5. **Seguridad de Granjeros:** Si algún jugador o peón guardó la partida en una coordenada bloqueada o una ubicación huérfana, su posición de reaparición se fija de manera segura en `Farm (64, 15)`.

---

## 5. Método 1: Herramienta CLI (`scripts/migrate_save_to_plaza.py`)

Es la opción más completa y potente. Se ejecuta desde la terminal antes de iniciar la partida.

### 5.1. Listar partidas guardadas
```bash
python3 scripts/migrate_save_to_plaza.py --list-saves
```

### 5.2. Simulación previa (Dry-Run)
Verifica los cambios que se aplicarían sin modificar ningún archivo:
```bash
python3 scripts/migrate_save_to_plaza.py ~/.config/StardewValley/Saves/MiPartida_123456789 --dry-run
```

### 5.3. Migración recomendada (Modo Slot con copia de seguridad)
```bash
python3 scripts/migrate_save_to_plaza.py ~/.config/StardewValley/Saves/MiPartida_123456789
```
*Crea automáticamente una copia de seguridad en `MiPartida_123456789_bak_YYYYMMDD_HHMMSS` antes de modificar nada.*

### 5.4. Migración geográfica (Modo Geo)
```bash
python3 scripts/migrate_save_to_plaza.py ~/.config/StardewValley/Saves/MiPartida_123456789 --mode geo
```

### 5.5. Migración personalizada con archivo JSON
```bash
python3 scripts/migrate_save_to_plaza.py ~/.config/StardewValley/Saves/MiPartida_123456789 --mode custom --custom-map mi_mapeo.json
```

---

## 6. Método 2: Comando en Caliente SMAPI (`p4e_migrate_save`)

Si ya has iniciado el juego o prefieres realizar la migración en tiempo real desde la consola del mod:

1. Inicia Stardew Valley con SMAPI y carga la partida como **anfitrión (host)**.
2. Abre la consola de SMAPI y escribe:
   ```text
   p4e_migrate_save
   ```
   *(O escribe `p4e_migrate_save geo` si deseas aplicar el modo geográfico).*
3. El mod ejecutará inmediatamente:
   - Purgado de warps obsoletos de la Granja Host.
   - Reubicación del `Pet Bowl` a `(53, 7)`.
   - Despeje de escombros en el corredor oeste y portales.
   - Re-aplicación de los acabados de mapa y sincronización con todos los peones conectados.
   - Actualización del estado autoritativo en `ServerWorldState`.
4. Para consolidar los cambios en el archivo físico de guardado, simplemente pasa el día (duerme en la cama) para que Stardew Valley guarde la partida.

---

## 7. Preguntas Frecuentes

### ¿Se pierden los cofres, cultivos o animales de las granjas privadas?
**No.** Tanto en modo `slot` como en modo `geo`, los datos de las granjas privadas (`Pelican4Ever_Farm_XX`) se conservan intactos. En modo `geo`, la herramienta renombra ordenadamente todas las ubicaciones correspondientes (incluidas la cueva de setas/fruta, la bodega de barricas, el invernadero y los anexos temáticos).

### ¿Qué ocurre si un jugador tenía un anexo temático comprado?
El mod recalcula automáticamente los identificadores de los anexos (`Pelican4Ever_Annex_XX_Side`) para que continúen vinculados a su propietario sin desalineaciones.

### ¿Qué pasa si abro la partida antigua sin ejecutar la migración?
El mod cuenta con salvaguardas automáticas en `OnSaveLoaded`: purgará automáticamente los warps obsoletos de la Granja Host y normalizará el catálogo de puertas para evitar cuelgues o desconexiones. Sin embargo, se recomienda ejecutar el script o el comando `p4e_migrate_save` para asegurar el despeje del corredor oeste y la reubicación del cuenco del perro.
