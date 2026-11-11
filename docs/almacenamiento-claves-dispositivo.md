# Almacenamiento de Claves de Dispositivo y Registro del Host (TASK-1802)

Este documento especifica la persistencia atómica, la protección de claves privadas en el cliente (Linux y Windows), la estructura del registro de dispositivos en el host y la gestión del esquema de datos para la Épica 18 de Pelican 4Ever.

---

## 1. Almacenamiento de Claves en el Cliente

### 1.1 Ubicación y Formato
- **Ubicación principal:** `data/device_key.json` dentro del directorio de la instalación local del mod.
- **Ubicación de respaldo:** SMAPI GlobalData (`pelican4ever-device-key`) para resiliencia ante reinstalación o borrado accidental de la carpeta del mod.
- **Formato:** JSON estructurado (`DeviceKeyFile`) con SchemaVersion 2:
  ```json
  {
    "SchemaVersion": 2,
    "Pkcs8Base64": "<clave privada NIST P-256 en formato PKCS#8 DER codificada en Base64>",
    "Fingerprint": "<huella digital SHA-256 de la clave pública (64 hex)>",
    "PublicKeyBase64": "<clave pública SPKI DER codificada en Base64>",
    "CreatedAtUtc": "2026-11-11T18:00:00.0000000Z"
  }
  ```

### 1.2 Protección y Permisos en el Sistema Operativo

#### Linux / macOS
- Al escribir o actualizar `data/device_key.json`, [`DeviceKeyStorage.RestrictFilePermissions`](file:///home/mtx/Documentos/Projects/Stardew Valley MORPG/Pelican4Ever.Core/Security/DeviceKeyStorage.cs#L35) invoca la llamada nativa `chmod(path, 0x180)` (permisos octales `0600`), garantizando acceso exclusivo de lectura y escritura para el usuario propietario (`UID`).
- **Límites conocidos:**
  - El usuario `root` o procesos que compartan el mismo `UID` en el sistema pueden leer el archivo.
  - El proceso de Stardew Valley / mono / .NET se ejecuta con los privilegios del usuario de la sesión de escritorio; no existe aislamiento entre procesos del mismo usuario en Linux tradicional sin sandboxing (Flatpak/Firejail).

#### Windows
- El archivo se guarda en la carpeta del mod o en `%APPDATA%/StardewValley/`, heredando las Listas de Control de Acceso (ACLs) estándar de NTFS que restringen el acceso al usuario que inició sesión y al grupo de Administradores.
- **Límites conocidos:**
  - Cualquier software malicioso ejecutado bajo la misma cuenta de usuario de Windows puede leer los archivos del perfil de usuario.

### 1.3 Resiliencia y Fallo Cerrado Seguro
1. **Comprobación estricta de integridad:** Al cargar la clave, [`DeviceKeyStorage.Load`](file:///home/mtx/Documentos/Projects/Stardew Valley MORPG/Pelican4Ever.Core/Security/DeviceKeyStorage.cs#L94) calcula la huella digital real de la clave privada importada y comprueba que coincida exactamente con `model.Fingerprint`.
2. **Rechazo seguro:** Si el archivo está corrupto (JSON truncado, Base64 inválido, PKCS#8 dañado o huella discordante), el sistema lanza `InvalidDataException` y **bloquea el acceso**. Nunca se genera una clave silenciosa ni se concede acceso no verificado.
3. **Escritura atómica:** [`DeviceKeyStorage.SaveAtomic`](file:///home/mtx/Documentos/Projects/Stardew Valley MORPG/Pelican4Ever.Core/Security/DeviceKeyStorage.cs#L57) escribe primero en `device_key.json.tmp`, fuerza el volcado a disco (`FileStream.Flush(flushToDisk: true)`), ajusta los permisos restringidos y realiza un reemplazo atómico con `File.Move(tmp, target, overwrite: true)`.

---

## 2. Registro de Dispositivos en el Host

### 2.1 Modelo `AuthorizedDevice`
En el host se registra únicamente la información pública y autoritativa de cada dispositivo:

| Campo | Tipo | Descripción |
| :--- | :--- | :--- |
| **`Fingerprint`** | `string` (64 hex) | Huella digital SHA-256 de la clave pública SPKI. Identificador público no confidencial. |
| **`PublicKeyBase64`** | `string` (Base64) | Clave pública del dispositivo en formato SubjectPublicKeyInfo (SPKI). |
| **`Alias`** | `string` | Nombre o identificador amigable del usuario (ej. `"mTx"`). |
| **`Status`** | `DeviceStatus` | Estado: `Active` (autorizado), `Revoked` (revocado), `Suspended`. |
| **`BoundPlayerId`** | `long` | `UniqueMultiplayerID` del granjero/ranura asignada. |
| **`CreatedAtUtc`** | `DateTimeOffset` | Momento de alta o aprobación. |
| **`LastSeenUtc`** | `DateTimeOffset` | Momento del último acceso con éxito. |
| **`LastSeenIp`** | `string` | Última IP observada (puramente informativa, no credencial). |

> [!IMPORTANT]
> **Invariante Cero Claves Privadas:** El servidor **nunca** recibe, solicita ni almacena claves privadas. El robo físico o filtración de la partida o del sidecar del host solo revela claves públicas y huellas digitales, que son matemáticamente insuficientes para autenticarse o generar firmas válidas.

### 2.2 Versionado del Esquema
- **Sidecar `pelican4ever_pairing.json`:** `SchemaVersion = 2`.
- **Estado maestro `ServerWorldState`:** `SchemaVersion = 5`.
- Se implementó la migración automática [`ServerDeviceRegistry.MigrateLegacyClients`](file:///home/mtx/Documentos/Projects/Stardew Valley MORPG/Pelican4Ever.Core/Security/ServerDeviceRegistry.cs#L130) para integrar clientes del esquema anterior sin romper datos históricos.

### 2.3 Persistencia Atómica del Sidecar
[`SidecarPersistence`](file:///home/mtx/Documentos/Projects/Stardew Valley MORPG/Pelican4Ever.Core/Security/SidecarPersistence.cs) garantiza que:
1. Los cambios se persisten de inmediato tras altas, bajas o modificaciones.
2. La escritura utiliza el patrón atómico `.tmp` + `FileStream.Flush(flushToDisk: true)` + `File.Move`.
3. En caso de caída del servidor durante el guardado, [`LoadAndMerge`](file:///home/mtx/Documentos/Projects/Stardew Valley MORPG/Pelican4Ever.Core/Security/SidecarPersistence.cs#L80) detecta archivos `.tmp` huérfanos y recupera el estado de forma automática y consistente.
4. Archivos corruptos devuelven error controlado y no comprometen la partida ni otorgan acceso indebido.
