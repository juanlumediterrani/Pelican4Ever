# Spike de checkpoints intradía (TASK-1601)

Guía para comprobar en partida si se puede guardar a mitad de día sin perder ni duplicar nada. Cierra los gaps G1–G5 y G10 de la Épica 11 de [backlog.md](../backlog.md). Los resultados se apuntan en la tabla del final y luego en cada gap del backlog.

## Qué hace `p4e_checkpoint_spike`

Es un comando de la consola SMAPI del host (`CheckpointSpikeModule`). Lanza `SaveGame.Save()` como lo haría un checkpoint y, al terminar, deja una carpeta en `~/.config/StardewValley/Pelican4Ever_Backups/spike/<partida>_<fecha>/` con:

- `save/`: copia de la partida recién guardada.
- `report.json`: duración del guardado, tick más largo, si se dispararon `Saving` y `Saved`, el estado de cada farmhand conectado (vivo y copia de `farmhandData`) y el recuento de Junimos, tiendas de campaña y compañeros de trinkets antes, justo después y 30 segundos después.

En la consola escribe un resumen con nivel `ALERT`, para que se vea entre el resto del log.

```
p4e_checkpoint_spike            guarda tal cual
p4e_checkpoint_spike clonar     antes copia el estado vivo de los farmhands a farmhandData
p4e_checkpoint_spike forzar     permite lanzarlo con festival o evento en curso
```

No se lanza a menos de 10 minutos del rollover, con otro guardado en curso ni con el rollover en marcha. El rollover tampoco empieza mientras el spike está guardando.

**Por qué existe `clonar`:** el juego guarda a los farmhands desde `netWorldState.farmhandData`, una copia que solo se refresca cuando el jugador se desconecta o al dormir (`Multiplayer.saveFarmhands`). Esa función no sirve a mitad de día, porque además llama a `ResetFarmhandState` sobre el jugador vivo: puede mandarlo a la cama o, si su casa no es una `Cabin`, sacarlo de `otherFarmers`. `clonar` usa solo la parte de copia (`NetRoot.CloneInto`).

## Preparación

1. Usa una **partida de prueba**, nunca la del servidor real. El spike sobrescribe la partida igual que un guardado normal.
2. Compila y despliega la rama `feat/day-checkpoints` (sin `EnableModDeploy=false`, para que llegue al juego).
3. Activa `CheckpointDebugMode: true` **solo en la copia de prueba**. Sin esta opción, el comando se rechaza; los checkpoints normales no necesitan activarla.
4. Para el paso G1 basta con `~/.config/StardewValley/ErrorLogs/SMAPI-latest.txt`, que guarda siempre las líneas `TRACE` aunque la consola no las muestre.
5. Arranca el host y conecta **al menos un farmhand** (mejor dos, para G2).

## Pasos

### 1. Guardado sin clonar (G1, G2, G4)

1. Con el farmhand: coge objetos del suelo, gasta energía y habla con un NPC. Así su estado vivo se separa del que había al conectarse.
2. En el host: `p4e_checkpoint_spike`.
3. Apunta del resumen: `ms`, `tick más largo`, `Saving` y `Saved`.
4. Busca en `SMAPI-latest.txt`, justo después de `Guardado iniciado`:
   - `ServerWorldState guardado en la partida maestra (checkpoint spike)` (el volcado explícito de TASK-1602; debe salir siempre);
   - `ServerWorldState guardado en la partida maestra (Saving)` (solo si SMAPI disparó `Saving`: **es la respuesta a G1**);
   - cualquier línea de SpaceCore cercana.
5. Mira las líneas `G4:` del resumen: dicen si la copia que se guarda iba retrasada respecto al jugador vivo.
6. Compara con el XML:

   ```bash
   python3 -I scripts/checkpoint_spike_compare.py ~/.config/StardewValley/Pelican4Ever_Backups/spike/<carpeta>
   ```

   Esperado sin clonar: «el XML tiene la copia RETRASADA de farmhandData». Si sale «estado VIVO», el riesgo de G4 desaparece.
7. Durante el guardado, apunta qué ve el farmhand: si se congela, cuánto y si se desconecta.

### 2. Guardado clonando (G4, G10)

1. El farmhand deja un objeto en un cofre, coge otro y, si puedes, vende algo en el mercado comunitario.
2. `p4e_checkpoint_spike clonar`.
3. Las líneas `G4: ... tras clonar` deben decir `coincide`.
4. `checkpoint_spike_compare.py` sobre la nueva carpeta: esperado «estado VIVO».
5. Comprueba en el farmhand que no se ha movido, ni perdido el objeto que llevaba en la mano, ni desconectado (el clon reemplaza su entrada en `farmhandData` y se envía a todos los clientes).

### 3. Efectos secundarios (G3)

1. Prepara antes una tienda de campaña (Tent Kit) colocada, un trinket con compañero equipado y, si la partida lo tiene, el Centro Comunitario con Junimos visibles.
2. `p4e_checkpoint_spike` y espera el segundo resumen (30 s): `Junimos CC`, `tiendas`, `personajes WitchHut`, `compañeros de trinkets` antes → justo después → +30 s.
3. Si algo baja y no vuelve, es un efecto que hay que evitar o aceptar en TASK-1603.

### 4. Carga desde el checkpoint (G5)

1. Tras el paso 2, cierra el juego del host a la fuerza (`kill` del proceso, sin volver al título).
2. Arranca y carga la partida. Apunta:
   - hora del juego tras la carga y si `RealTimeClockModule` la pone en la hora real;
   - dónde aparecen host y farmhand al reconectarse;
   - si el log de `DailyRolloverModule` dice «inicio causado por carga de partida» y no aplica penalizaciones;
   - qué hacen al cargar NITV (criaturas), FashionSense y Dr4coVoyagerBridge, y si alguno da error;
   - si el objeto del cofre y el del farmhand del paso 2 están una sola vez (**G10**).

## Resultados

Rellenar una fila por prueba y copiar las conclusiones a los gaps de la Épica 11.

| Fecha | Commit | Farmhands | Clonar | Total ms | Tick más largo ms | MB | `Saving` | `Saved` | G4 (XML) | Notas |
|---|---|---|---|---|---|---|---|---|---|---|
| 2026-11-06 | `80cc24e` | 1 | no | 3582 | 3555 | 25,5 | no | no | copia retrasada | Sin desconexión. |
| 2026-11-06 | `80cc24e` | 2 | sí | 2989 | 2964 | 25,5 | no | no | estado vivo (ambos) | Inventarios, energía, correo y amistad coinciden con el XML. |
| 2026-11-06 | `80cc24e` | 0 | sí | 3560 | 3546 | 25,5 | no | no | — | Tienda y Junimo desaparecen y no vuelven a los 30 s; el loro vuelve. |
| 2026-11-06 | `80cc24e` | 1 | sí | 3422 | 3396 | 25,5 | no | no | estado vivo | Reconexión después de restaurar la copia gzip. |
| 2026-11-06 | `80cc24e` + correcciones locales | 2 | sí | 3410 | 3410 | 25,9 | no | no | estado vivo (ambos) | Enumerador agotado en una llamada, después de la prueba de mercado. |

Decisión al cerrar el spike (criterio 2 de TASK-1601): **viable por defecto / viable con restricciones / no viable**, y por qué.


### Resultado del 6 de noviembre de 2026

El mecanismo de guardado intradía es **viable con restricciones**; la promoción queda bloqueada por la pérdida de objetos detectada en la prueba del mercado (G10, informe enlazado al final). Se mantiene `CheckpointIntervalMinutes = 0` por defecto.

- SMAPI 4.5.2 no emite `Saving`/`Saved` al llamar a `SaveGame.Save()`. El volcado explícito conserva `ServerWorldState`. SpaceCore 1.28.4 intercepta el enumerador/serializador mediante Harmony; la copia vuelve a cargar con SpaceCore, NITV, FashionSense y Dr4coVoyagerBridge. Esto no acredita compatibilidad con cualquier mod que dependa de esos eventos.
- Sin clonación se guarda el inventario anterior del farmhand. Con `NetRoot.CloneInto` coinciden los dos jugadores; el checkpoint de producción cancela si no puede clonar a cualquiera de ellos o volcar el estado del mod.
- `cleanupBeforeSave()` borra tiendas, Junimos y personajes de WitchHut. El checkpoint normal se omite mientras existan, también dentro de edificios. El spike de diagnóstico puede reproducir el efecto y por eso requiere `CheckpointDebugMode`.
- El bloqueo medido ronda los 3–3,6 s para 25,5 MB, sin desconexión de los clientes. La clonación, el volcado y el guardado normal se agotan en una única llamada del hilo principal: no se intercalan ticks de red entre la copia de los jugadores y la del mundo.
- Tras matar el host y descomprimir su copia gzip se conserva el inventario y la amistad; el reloj vuelve a la hora real y `DayStarted` omite penalizaciones y contadores duplicados. Se aplican las reglas normales de carga (salud/posición y recuperación de energía offline), no una reanudación de la simulación exactamente donde se detuvo.

Las pruebas se ejecutan con perfiles XDG separados y host/cliente(s) en un espacio de nombres de red sin LAN. La instalación habitual y las partidas originales no se modifican. El informe de validación está en [checkpoint-validation-2026-11-06.md](checkpoint-validation-2026-11-06.md).
