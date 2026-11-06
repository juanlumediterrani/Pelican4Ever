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
3. Para el paso G1 basta con `~/.config/StardewValley/ErrorLogs/SMAPI-latest.txt`, que guarda siempre las líneas `TRACE` aunque la consola no las muestre.
4. Arranca el host y conecta **al menos un farmhand** (mejor dos, para G2).

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
| | | | | | | | | | | |

Decisión al cerrar el spike (criterio 2 de TASK-1601): **viable por defecto / viable con restricciones / no viable**, y por qué.
