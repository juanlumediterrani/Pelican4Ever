> **Documento archivado (2026-11-13).** No lo actualices: Procedimiento de una migración puntual (un jugador de la beta cerrada al sistema de claves). No se repite: el flujo actual está en [despliegue/host-local.md](../despliegue/host-local.md) § Dar de alta a los vecinos.
>
> Se conserva como registro histórico.

# Plan de Migración de Identidad y Restauración Segura (TASK-1804)

> **Documento de Referencia Técnica y Operativa**  
> **Épica 18: Identidad de dispositivos y acceso seguro a la demo**  
> **Fecha:** 2026-11-11  
> **Estado:** Aprobado / Implementado

---

## 1. Contexto y Objetivos

El sistema heredado de acceso vinculaba a los jugadores a un secreto compartido (`OwnerToken` o token GUID) que se copiaba a `farmer.userID.Value` y se transmitía por la red nativa de Stardew Valley. Además, las autorizaciones dependían de la dirección IP de conexión del jugador (mTx).

La migración a la arquitectura asimétrica de dispositivos (`P4E\x02` con ECDSA NIST P-256) permite que:
1. El jugador (ej. **mTx**) conserve íntegramente su personaje, progreso, inventario, residencia y `UniqueMultiplayerID`.
2. El dispositivo autorizado pueda conectarse libremente desde cualquier dirección IP sin necesidad de nuevas invitaciones ni re-emparejamientos.
3. Las credenciales antiguas se retiren definitivamente: poseer solo el token anterior no concede acceso ni permite asociar claves nuevas.
4. Antes de modificar el estado, se genera una copia de seguridad verificable con suma SHA-256 en disco.
5. Cualquier restauración posterior no reactiva credenciales obsoletas ni dispositivos revocados.

---

## 2. Invariantes de Seguridad de la Migración

- **Tratamiento del token antiguo como comprometido:**  
  Bajo el modelo de amenazas de la Épica 18, cualquier token legado (`OwnerToken` GUID previo) debe considerarse públicamente conocido o filtrado. Por tanto:
  * **Ningún cliente puede solicitar ni completar una migración de forma automática** aportando únicamente el token antiguo.
  * La migración exige **prueba criptográfica de posesión** del nuevo par de claves (mediante firma válida del desafío emitido por el host) **y aprobación explícita fuera de banda** del propietario del servidor (vía consola del host o DM en Discord con Putre).
- **Preservación total del progreso y activos:**  
  El identificador único de red (`UniqueMultiplayerID = 555L` para mTx) no se altera. Las asignaciones de residencias (`DoorAssignment.OwnerId`), el slot asignado (`FarmSlotId`), los inventarios, estadísticas y cofres privados permanecen idénticos.
- **Retiro atómico de credenciales legadas:**  
  Al completarse la migración, el token secreto legado es purgado del estado del servidor. `PlayerRecord.OwnerToken` pasa a almacenar la huella pública SHA-256 del dispositivo (64 caracteres hexadecimales).
- **Fallo cerrado:**  
  Un cliente desactualizado o un tercero con el token legado recibe un fallo tajante (`DeniedMismatch` o `DeniedNoInvite`) sin activar rutas alternativas ni fallbacks silenciosos.

---

## 3. Procedimiento de Migración Paso a Paso (Caso mTx)

### Paso 1: Generación y protección de clave en el cliente
1. El cliente de mTx actualiza el mod Pelican4Ever.
2. Al arrancar, `ClientIdentity.InitializeDeviceKey()` genera localmente un par de claves ECDSA NIST P-256 y lo guarda en `device_key.json` con permisos POSIX `0600` (Linux/macOS) o protección ACL de usuario (Windows).

### Paso 2: Conexión inicial y prueba de posesión
1. mTx se conecta al servidor (incluso desde una IP distinta).
2. Durante el descubrimiento (`LidgrenServer.sendVersionInfo`), el host emite un desafío con un nonce aleatorio de 32 bytes y un TTL de 30 segundos (`DeviceAuthChallenge`).
3. El cliente de mTx firma el contexto canónico con su clave privada y lo envía en el mensaje de saludo (`RemoteHailMessage`).
4. El host valida la firma con `ServerChallengeManager.TryVerifyAndConsume()`, calculando la huella digital SHA-256 pública del dispositivo (ej. `a1b2c3d4e5f6...`).
5. Al no estar autorizado aún, si existe una invitación o contacto previo, se registra una solicitud en `PendingEnrollments`. El cliente es desconectado limpiamente con el mensaje:
   ```text
   Dispositivo registrado para aprobación. Tu huella es: a1b2c3d4e5f6. Solicita la aprobación al propietario en Discord.
   ```

### Paso 3: Copia de seguridad verificable previa (Automática)
Antes de modificar el estado, el subsistema `DeviceMigrationLogic.CreateVerifiableBackup()` genera:
* Archivo de respaldo: `pelican4ever_pairing.backup_YYYYMMDD_HHmmss_fff.json`
* Archivo de suma de integridad: `pelican4ever_pairing.backup_YYYYMMDD_HHmmss_fff.json.sha256`
* Verifica byte a byte la correspondencia de la suma criptográfica SHA-256 antes de permitir cualquier modificación en memoria.

### Paso 4: Aprobación explícita del propietario
El propietario autoriza la migración vinculando la huella digital al personaje:

**Opción A — Por consola SMAPI:**
```text
p4e_migrate_player a1b2c3d4e5f6 mTx
```
*(O de forma equivalente: `p4e_pair_approve a1b2c3d4e5f6 mTx`)*

**Opción B — Desde Discord DM con Putre:**
```text
pelican.command_admin command="p4e_migrate_player" args="a1b2c3d4e5f6 mTx"
```
Putre solicita confirmación interactiva en el DM privado del propietario (Nivel de riesgo: `Sensitive`). Al confirmarse, se despacha al host.

### Paso 5: Aplicación atómica de la migración
`DeviceMigrationLogic.MigratePlayer`:
1. Comprueba la prueba de posesión en `PendingEnrollments` o `AuthorizedDevices`.
2. Asocia el dispositivo a `UniqueMultiplayerID = 555L` en `AuthorizedDevices`.
3. Pone el estado del dispositivo en `DeviceStatus.Active`.
4. Purga el token legado de `AuthorizedClients` y sobreescribe `player.OwnerToken = fingerprint`.
5. Consume la solicitud pendiente y cualquier código de invitación asociado.
6. Persiste atómicamente el sidecar mediante `SidecarPersistence.SaveAtomic()` y emite el evento administrativo `HostEventTypes.PlayerPaired`.

### Paso 6: Reentrada inmediata
mTx vuelve a conectarse desde cualquier IP. Su huella digital es verificada instantáneamente; accede al menú de peones con su slot desbloqueado y entra a su partida sin ninguna credencial compartida ni riesgo de suplantación.

---

## 4. Plan de Restauración Segura (Rollback y Disaster Recovery)

Si por cualquier eventualidad operativa fuera necesario restaurar un punto de guardado previo:

### Verificación de integridad del respaldo
Antes de aplicar la restauración, se debe validar que el archivo no ha sido alterado:
```bash
sha256sum -c pelican4ever_pairing.backup_20261111_120000_000.json.sha256
```
O programáticamente a través de `DeviceMigrationLogic.VerifyBackupIntegrity(backupPath, expectedSha256, out _, out _)`.

### Reglas de restauración segura (`DeviceMigrationLogic.RestoreFromBackup`)
Al restaurar un backup antiguo:
1. **Preservación de revocaciones:**  
   Cualquier dispositivo que estuviera marcado como `Revoked` en el servidor no se reactiva: se mantiene revocado para evitar que un dispositivo comprometido recupere acceso por restaurar una copia anterior.
2. **No reactivación de tokens legados:**  
   Las entradas heredadas de `AuthorizedClients` presentes en el JSON de respaldo **no** se restauran en el diccionario de accesos activos.
3. **Consistencia de peones:**  
   Los datos de partidas nativas (`SaveData`) se restauran de acuerdo con las políticas estándar de rollover de `SaveBackupService`.

---

## 5. Matriz de Pruebas Unitarias e Integración

| Identificador | Caso de Prueba | Resultado |
|---|---|---|
| `CreateVerifiableBackup_GeneratesFileAndMatchingSha256Checksum` | Generación y comprobación de suma SHA-256 de respaldo | Superado |
| `VerifyBackupIntegrity_DetectsTampering` | Detección inmediata de alteración o corrupción en backup | Superado |
| `RestoreFromBackup_PreservesRevokedStatusAndDoesNotReactivateLegacyTokens` | Restauración segura sin reactivar secretos legados ni dispositivos revocados | Superado |
| `MigratePlayer_MigratesMtxPreservingUniqueIdResidenceAndProgress` | Migración de mTx con preservación de ID, slot, residencia, saciedad y purga de token legado | Superado |
| `MigratePlayer_RejectsThirdPartyWithLegacyTokenAlone` | Rechazo de solicitud basada únicamente en el token legado | Superado |
| `MigratePlayer_IsIdempotent` | Reejecución segura sin alterar datos ni duplicar entradas | Superado |
| `MigratePlayer_AllowsConnectingFromDifferentIpAfterMigration` | Acceso exitoso de mTx desde IP distinta y bloqueo del atacante con token legado | Superado |
| `MigratePlayer_FailsIfPlayerDoesNotExist` | Fallo controlado si se intenta migrar a un personaje inexistente | Superado |
