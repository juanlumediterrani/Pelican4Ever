# Reconexión y contexto obsoleto en SMAPI 4.5.2

El log del host del 6 de noviembre de 2026 muestra una conexión correcta a
las 04:41 y una salida a las 04:52 sin el mensaje `Player quit` de SMAPI.
A las 11:47, 11:48, 11:53 y 11:54 llega el contexto SMAPI del mismo granjero,
seguido del aviso de que el juego no lo vio desconectarse, clasificación
como vanilla y expulsión por `no usa SMAPI`.

En `SMultiplayer.OnServerProcessingMessage` de SMAPI 4.5.2, si ya existe el
ID al recibir `ModContext`, SMAPI elimina la entrada anterior y retorna sin
registrar el contexto nuevo ni responder con el contexto del host. El
`PlayerIntroduction` siguiente crea entonces un peer vanilla. Por eso
aplazar la validación hasta `PeerConnected` no resuelve este caso.

`ClientVersionGuardModule` instala un prefix antes de ese procesamiento.
Solo para un mensaje `ModContext` recibido por el host, elimina la entrada
anterior si el granjero no figura en `Game1.getOnlineFarmers()`. SMAPI
procesa después el mensaje original y realiza el handshake completo.
Una conexión activa conserva su entrada y el tratamiento normal de SMAPI
para contextos duplicados. La comprobación de versión y los tokens de
propiedad no cambian. La reparación también funciona con `RequireClientMod`
desactivado, porque el contexto se necesita para los mensajes de otros mods.

Fuente revisada: https://github.com/Pathoschild/SMAPI/blob/4.5.2/src/SMAPI/Framework/SMultiplayer.cs

La corrección depende de miembros internos de SMAPI. Al actualizar SMAPI,
comprobar el prefix y repetir estas pruebas en cliente y servidor:

1. Entrar con un cliente compatible, salir normalmente y reconectar.
2. Repetir después de una desconexión de red y de salir mediante la cama.
3. Verificar que ambos lados reciben contexto SMAPI y que el host no
   clasifica al cliente compatible como vanilla.
4. Verificar que un cliente sin el mod o con versión incompatible sigue
   siendo expulsado cuando `RequireClientMod` está activado.

Las pruebas unitarias cubren la eliminación selectiva de entradas obsoletas,
la conservación de conexiones activas y una primera conexión sin contexto
previo. Validación local: 931 pruebas correctas, compilación del mod sin
errores ni avisos e instalación del prefix Harmony contra la DLL de SMAPI
4.5.2 usando el runtime .NET 6 incluido en el juego. Un arnés aislado con
.NET 8 falló al aplicar Harmony; se repitió con el runtime real del juego.
La reproducción con dos instancias de juego sigue siendo necesaria
para confirmar la corrección del fallo observado en el servidor.
