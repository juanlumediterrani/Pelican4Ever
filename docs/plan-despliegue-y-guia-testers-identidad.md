# Plan de Despliegue y Guía para Testers: Identidad de Dispositivos (Épica 18 / TASK-1807)

> **Documento de Operaciones y Pruebas**  
> **Épica 18: Identidad de dispositivos y acceso seguro a la demo**  
> **Fecha:** 2026-11-11  
> **Estado:** Aprobado para Despliegue en Demo

---

## 1. Resumen Ejecutivo

Este plan define el protocolo de actualización coordinada entre el servidor anfitrión (Host SMAPI / Debian) y los clientes de prueba (testers en Linux y Windows) para desplegar el nuevo sistema de autenticación asimétrica de dispositivos (`P4E\x02`).

### Objetivos Clave
1. Permitir que los clientes autorizados (incluyendo **mTx**) entren por IP directa aunque su dirección pública cambie, sin requerir re-emparejamientos periódicos.
2. Eliminar completamente la exposición o transmisión de credenciales reutilizables (`OwnerToken`, tokens GUID o códigos de pairing) sobre el canal de juego o la red nativa de Stardew.
3. Preservar íntegramente las partidas, inventarios, residencias y progreso de los peones.
4. Garantizar un procedimiento de rollback seguro que no reactive secretos obsoletos.

---

## 2. Requisitos Previos y Matriz de Compatibilidad

* **Versión de juego:** Stardew Valley 1.6.15 con SMAPI 4.1.10+.
* **Plataformas de cliente soportadas:**
  * **Linux / Steam Deck:** Requiere permisos POSIX `0600` en `data/device_key.json` (gestionado automáticamente por el mod vía P/Invoke libc).
  * **Windows 10/11:** Almacenamiento protegido local en AppData/Mods sin privilegios de administrador.
* **Regla de compatibilidad estricta:**  
  Tanto el anfitrión como los clientes deben tener instalada la versión actualizada del mod (`Pelican4Ever 0.2.0`). Si un cliente con versión previa intenta conectar, recibirá un rechazo tajante antes de la entrega de la lista de peones. No existe fallback inseguro.

---

## 3. Guía Paso a Paso para Testers

### Paso 1: Instalación de la Actualización
1. Descarga el paquete `Pelican4Ever 0.2.0.zip`.
2. Descomprime su contenido en tu carpeta de mods de Stardew Valley:
   * **Linux:** `~/.steam/steam/steamapps/common/Stardew Valley/Mods/Pelican4Ever/`
   * **Windows:** `C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\Mods\Pelican4Ever\`
3. Inicia el juego a través de SMAPI. Al cargar el mod, generará automáticamente tu par de claves criptográficas seguras (ECDSA NIST P-256) en `Mods/Pelican4Ever/data/device_key.json`.

### Paso 2: Primer Contacto con el Servidor
1. Entra a **Cooperativo -> Conexión Directa** e introduce la IP y puerto del servidor demo.
2. Durante el saludo inicial, el servidor emitirá un desafío temporal de 30 segundos. Tu cliente firmará el desafío con su clave privada y enviará la prueba de posesión.
3. Si aún no estás en la lista blanca de dispositivos autorizados, el servidor registrará tu solicitud y te desconectará de forma limpia con un mensaje en pantalla indicando tu **huella digital pública corta** (ejemplo: `a1b2c3d4e5f6`).

### Paso 3: Aprobación Fuera de Banda
1. Envía tu huella pública o alias al propietario del servidor por Discord.
2. El propietario autorizará tu acceso mediante Putre en su canal privado DM:
   ```text
   pelican.command_admin command="p4e_pair_approve" args="a1b2c3d4e5f6"
   ```
   *(O mediante consola SMAPI: `p4e_pair_approve a1b2c3d4e5f6`)*.

### Paso 4: Entrada al Servidor y Selección de Peón
1. Vuelve a pulsar **Conexión Directa** hacia el servidor.
2. Tu dispositivo será reconocido inmediatamente por su huella criptográfica.
3. Se abrirá el menú de peones:
   * Si eres un jugador nuevo, selecciona una ranura libre nueva; quedará autoritativamente vinculada a tu dispositivo.
   * Las ranuras de otros compañeros mostrarán la etiqueta `[De otro jugador]` y permanecerán bloqueadas contra selección accidental o maliciosa.
4. ¡Disfruta de la partida! Podrás reconectarte en cualquier momento, incluso si tu router reinicia o cambia de IP pública.

---

## 4. Procedimiento Especial: Migración de mTx

Para el jugador existente **mTx**, no es necesario reiniciar el personaje ni perder la residencia asignada:

1. mTx actualiza su cliente con `Pelican4Ever 0.2.0`.
2. mTx intenta la conexión; se emite el desafío y se registra la solicitud con su huella.
3. En el servidor se ejecuta automáticamente un respaldo verificable con suma SHA-256:
   `pelican4ever_pairing.backup_YYYYMMDD_HHmmss_fff.json`
4. El propietario ejecuta en Discord o consola:
   ```text
   p4e_migrate_player <huella_mtx> mTx
   ```
5. El sistema purga el token legado (`OwnerToken` GUID previo) y vincula la huella pública al `UniqueMultiplayerID = 555L`. Su ranura de granja (`Pelican4Ever_Farm_01`), residencia, nivel de saciedad e inventarios se conservan intactos.
6. mTx vuelve a entrar y accede de inmediato a su personaje.

---

## 5. Procedimiento de Contingencia y Rollback Seguro

Si se detectara alguna anomalía crítica durante la sesión de prueba:

1. **Punto de restauración garantizado:**  
   Antes de aplicar migraciones, el host genera una copia verificable del sidecar con validación SHA-256 (`DeviceMigrationLogic.CreateVerifiableBackup`).
2. **Restauración:**  
   Se puede restaurar el sidecar anterior mediante:
   ```bash
   sha256sum -c pelican4ever_pairing.backup_<timestamp>.json.sha256
   ```
   O invocando la API de restauración `DeviceMigrationLogic.RestoreFromBackup`.
3. **Invariante de seguridad de restauración:**  
   Restaurar una copia antigua **nunca** reactiva dispositivos que hubieran sido expresamente revocados ni vuelve a considerar válidos tokens legados ya purgados.

---

## 6. Límites Conocidos del Tráfico de Red (Fuera de Alcance)

> [!IMPORTANT]
> **Límite de alcance acordado en la Épica 18:**  
> La autenticación asimétrica `P4E\x02` opera en las fases de **descubrimiento, saludo (`RemoteHailMessage`), obtención de la lista de peones (`sendAvailableFarmhands`) y validación autoritativa de la selección de slot (`checkFarmhandRequest`)**.  
> El cifrado punto a punto del tráfico UDP durante la partida en curso (movimiento de avatares, sincronización de herramientas o chat en partida) **queda expresamente fuera del alcance de esta épica**, rigiéndose por el comportamiento nativo de Stardew Valley.
