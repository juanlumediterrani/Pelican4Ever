# ADR-0007: Identidad Asimétrica por Dispositivo y Desafío Criptográfico en Lidgren

* **Estado:** Aceptado
* **Fecha:** 2026-11-11
* **Contexto:** Épica 18 (TASK-1801), reemplaza el esquema de token compartido y lista blanca fija por IP de [ADR-0004](0004-host-authority-client-mod-and-satiety.md).

## Contexto

El acceso al servidor multijugador directo por IP de Stardew Valley 1.6.15/SMAPI utilizaba una lista blanca de IPs y un código de emparejamiento (pairing) de 72 horas. Este esquema presentaba tres limitaciones críticas de seguridad y usabilidad:

1. **Dependencia de la IP como identidad:** Si un jugador autorizado cambiaba de dirección IP (red doméstica dinámica, portátil en otra red o datos móviles), Lidgren llamaba a `sendAvailableFarmhands` con `userId` vacío; el servidor evaluaba el acceso antes de conocer la identidad del cliente y lo desconectaba de inmediato con «DeniedNoInvite».
2. **Exposición de credenciales (`OwnerToken`):** Para asociar el peón con el cliente, el host copiaba el `OwnerToken` al campo nativo `farmer.userID.Value`. En Stardew Valley, la lista de peones disponibles se transmite a todos los clientes que inician conexión, exponiendo los tokens de todos los peones a cualquier observador o cliente en la red. Asimismo, el token se almacenaba en texto plano en la partida y en el sidecar `pelican4ever_pairing.json`.
3. **Inviabilidad de secretos compartidos (HMAC):** Un prototipo intermedio de desafío con HMAC-SHA256 requería que el servidor almacenase el mismo secreto que el cliente, realizase una búsqueda $O(N)$ sobre todos los tokens conocidos y dependía de un token que ya estaba expuesto en el tráfico nativo.

## Decisión

1. **Identidad asimétrica por dispositivo (ECDSA NIST P-256):**
   - Cada instalación de cliente genera un par de claves asimétricas en el estándar NIST P-256 (`ECCurve.NamedCurves.nistP256`).
   - La clave privada nunca abandona el dispositivo del jugador.
   - El servidor almacena únicamente la clave pública (formato estándar SubjectPublicKeyInfo / SPKI) y su huella digital criptográfica SHA-256 (`DeviceFingerprint`).
   - La huella digital y el identificador de dispositivo son datos públicos, nunca credenciales secretas.

2. **Protocolo de desafío-respuesta previo a la entrega de peones (`P4E\x02`):**
   - Durante el descubrimiento UDP de Lidgren (`sendVersionInfo`), el host emite un desafío que contiene: marcador `P4E\x02`, identificador de sesión del servidor (UUID), timestamp UTC y un nonce criptográfico de 32 bytes de un solo uso con caducidad estricta de 30 segundos.
   - El cliente firma el desafío (`Contexto || SessionId || Timestamp || Nonce || HostEndpoint`) con su clave privada en formato IEEE P1363 (64 bytes) y lo adjunta en el mensaje de conexión inicial (`RemoteHailMessage`).
   - En `sendAvailableFarmhands`, el host valida la firma contra la clave pública autorizada y vincula la identidad a la instancia de `NetConnection` mediante un mapa débil (`ConditionalWeakTable`). Si la prueba es inválida, caducada o desconocida, la conexión se rechaza **antes** de enviar la lista de peones.

3. **Aprobación asíncrona fuera de banda (Discord DM / Putre):**
   - El alta de nuevos dispositivos no intercambia tokens ni contraseñas por el canal de juego. El propietario autoriza la huella digital concreta del dispositivo por mensaje directo con Putre.

4. **Retirada de credenciales en la red y almacenamiento:**
   - Se elimina la copia de tokens secretos a `farmer.userID.Value`. El campo `userID` solo contendrá identificadores públicos o permanecerá vacío para peones no reclamados.
   - Se elimina la transmisión de secretos y códigos por mensajes de mod (`ModMessages.PairingSubmit`).

5. **Límites explícitos:**
   - La autenticación garantiza la identidad del dispositivo y el acceso exclusivo a su ranura de peón hasta entrar a la partida. El cifrado del tráfico UDP durante la partida queda fuera de alcance.

## Consecuencias

* **Positivas:**
  - Los jugadores autorizados pueden entrar desde cualquier IP sin volver a emparejarse ni sufrir bloqueos por IP dinámica.
  - El servidor no custodia claves privadas ni contraseñas; el robo del archivo de guardado o del sidecar no permite suplantar a ningún jugador.
  - El rechazo ocurre antes de emitir la lista de granjeros, protegiendo la privacidad de los personajes.
  - Verificación en tiempo constante $O(1)$ sin riesgo de denegación de servicio por búsqueda iterativa de tokens.
* **Negativas:**
  - Requiere que tanto el servidor como los clientes utilicen la versión actualizada del mod con soporte del protocolo `P4E\x02`. Los clientes desactualizados son rechazados de forma segura con un mensaje explícito.
