# Backlog de Pruebas Vivas Ejecutables por Agentes (`live-agent-tests-backlog`)

Este documento agrupa las **pruebas de integración y carga en vivo sobre el juego real** (Stardew Valley + SMAPI + mods activos) que pueden lanzar y verificar **agentes autónomos** de forma reproducible, separadas tanto del [backlog de desarrollo (`backlog.md`)](backlog.md) como de las verificaciones con jugadores humanos ([`test-users-tasks.md`](test-users-tasks.md)).

---

## Protocolo de Ejecución para Agentes

1. **Aislamiento total del laboratorio:**
   - Ejecutar cada escenario en un directorio temporal dedicado `/tmp/p4e-<id>-lab` con `XDG_CONFIG_HOME`, `XDG_DATA_HOME`, `XDG_CACHE_HOME` y copia aislada de `Mods/` y partida de prueba por cada proceso (Host + $N$ *farmhands* LAN en `127.0.0.1`).
   - **Nunca** modificar las partidas reales del usuario ni su carpeta `Mods/` principal durante un laboratorio (salvo compilar cuando toque).
2. **Orquestación en hilo principal del juego:**
   - Combinar la API REST local del Host (`AdminApiPort`, p. ej. `127.0.0.1:8085`), comandos de consola SMAPI (`p4e_*`, activando `EnableTestCommands: true` y `AllowUnverifiedGameVersion: true` únicamente en el `config.json` del laboratorio cuando sea necesario) y/o el puente HTTP de *Stardew Voyager* (asignando puertos distintos si corren varias instancias simultáneas).
3. **Cierre limpio y evidencias:**
   - Verificar que no quedan excepciones en los logs de SMAPI de Host ni clientes.
   - Guardar el informe de evidencias en `docs/` (tablas de resultados, JSON de telemetría o extractos de `HostEventLog`) cuando la prueba cierre una épica o ADR.
   - Finalizar todos los procesos del laboratorio al concluir.

---

## Pruebas Vivas Pendientes

- [ ] **LIVE-915: Tránsito bidireccional Host + Farmhand por los 24 anexos y cambio de estación en vivo**
  * **Origen:** [TASK-915](backlog.md) · **Pruebas humanas complementarias:** [`TEST-915`](test-users-tasks.md)
  * **Dependencias:** Ninguna (corrección de `MapCloneHelper` ya aplicada en `Anexos 24/24 OK` / `Residencias 24/24 OK`).
  * **Procedimiento ejecutable por agente:**
    1. Levantar Host + 1 *farmhand* en laboratorio aislado con `EnableTestCommands: true`.
    2. Adquirir/desbloquear anexos de las 8 plantillas (`Standard`, `Riverland`, `Forest`, `Hilltop`, `Wilderness`, `FourCorners`, `Beach`, `Meadowlands`) en las 3 orientaciones (`North`/`South`/`East` según catálogo) y caminar/warpear con ambos jugadores de ida y vuelta verificando que ninguno queda atrapado en borde ni aparece fuera de mapa.
    3. Avanzar estaciones con `p4e_time` / cambio de fecha y comprobar recarga de tilesets sin errores en logs SMAPI.
  * **Criterios de aceptación:**
    1. 24/24 anexos transitables ida y vuelta tanto en el Host como en el *farmhand*.
    2. Cero excepciones en SMAPI al entrar, salir y cambiar de estación.

- [ ] **LIVE-2209: Verificación viva Host + Farmhand de pedidos por buzón, bloqueo en `NightHold` (`2550`), materialización a las `12:00` y aislamiento de paquetes**
  * **Origen:** [TASK-2209](backlog.md) · **Pruebas humanas complementarias:** [`TEST-2209`](test-users-tasks.md)
  * **Dependencias:** `TASK-2201..TASK-2208` completadas (`MailOrderLogicTests.cs` en verde).
  * **Procedimiento ejecutable por agente:**
    1. Levantar Host + 2 *farmhands* (`fh1`, `fh2`) en laboratorio aislado.
    2. Desde `fh1`, realizar pedidos nocturnos por buzón: objetos de tienda (Pierre/Willy), construcción diferida de Robin, compra de animal de Marnie y mejora de herramienta / geoda en Clint.
    3. Llevar el reloj a tramo `NightHold` (`02:30`–`05:50`, `timeOfDay == 2550`) tras rollover y verificar mediante `p4e_mail_orders` que `IsReadyToDeliver` permanece bloqueado hasta llegar a `12:00` reales del día objetivo.
    4. Al alcanzar `12:00`, comprobar que aparecen los paquetes físicos y andamios en la residencia de `fh1`, que `fh1` puede abrirlos y recuperar sus ítems, y que `fh2` (visitante sin propiedad) queda bloqueado al intentar abrir los paquetes de `fh1`.
  * **Criterios de aceptación:**
    1. `p4e_mail_orders` y `ServerWorldState` reflejan la cola y entrega exacta a las `12:00` tras sobrevivir a un checkpoint/rollover.
    2. `fh2` recibe denegación al intentar abrir el cofre de entrega de `fh1`, y todos los edificios/animales/herramientas de `fh1` llegan íntegros.

- [ ] **LIVE-2310: Automatización por arnés de construcción/animales compartidos (`CarpenterMenu`, `AnimalPage`) y reconexión `ExitToTitle` entre Propietario e Inquilino**
  * **Origen:** [TASK-2310](backlog.md) · **Pruebas humanas complementarias:** [`TEST-2310`](test-users-tasks.md)
  * **Dependencias:** `TASK-2301..TASK-2310` cerradas en `dev`.
  * **Procedimiento ejecutable por agente:**
    1. Reutilizar la topología de `/tmp/p4e-epic23-lab` (`Host` + `fh1` propietario + `fh2` inquilino en `Pelican4Ever_Farm_01`).
    2. Desde el arnés en hilo principal de `fh2` (inquilino), abrir el flujo real de construcción/demolición (`CarpenterMenu`) y de compra/gestión en `AnimalPage`, validando en vivo la creación y traslado de un edificio/animal compartido y la visibilidad mutua para `fh1`.
    3. Ejecutar `ExitToTitle` en `fh1` y `fh2`, reconectar ambos clientes seleccionando sus respectivas ranuras en `GameServer.sendAvailableFarmhands` y comprobar que conservan cartera independiente, hogar compartido (`Pelican4Ever_Farm_01_Home`) y camas diferenciadas.
  * **Criterios de aceptación:**
    1. Construcción, demolición y gestión de `AnimalPage` operan correctamente desde el inquilino sobre `Pelican4Ever_Farm_01` y sus anexos sin duplicar ranuras.
    2. Tras `ExitToTitle` y reconexión, ambos clientes recuperan su personaje correcto y su co-residencia sin quedar huérfanos.

- [ ] **LIVE-2404: Verificación multijugador en vivo de la intersección NITV ↔ Pelican 4Ever (spawns por bioma `0..3`, Insectario, buff `"NatCSpeedN"`, redes de visitante y `locationCap`)**
  * **Origen:** [TASK-2404](backlog.md) · **Pruebas humanas complementarias:** [`TEST-2404`](test-users-tasks.md)
  * **Dependencias:** `TASK-2401..TASK-2403` implementadas.
  * **Procedimiento ejecutable por agente:**
    1. Levantar Host + 2 *farmhands* con `Pelican4Ever`, `NatureInTheValley`, `NatureInValleyContent`, `SmolHooman.WildAnimalsInTheValley` y `Pelican4Ever.NITVCompat`.
    2. Validar aparición de criaturas con códigos `"0"`, `"1"` (`Forest`/`Foraging`/`FourCorners` aunque haya `<= 60` árboles), `"2"` (`Fishing`/`Beach` aunque haya `< 40%` agua) y `"3"` (`Pelican4Ever_Farm_01_Cave..16_Cave`).
    3. Simular carga de red (`NatInValeyNet`) seguida de warp interrumpido: verificar que `"NatCSpeedN"` conserva duración de `20000 ms` sin escalarse `85.7x` ni guardarse en `PersistedFoodBuff`.
    4. Visitar una parcela ajena con `VisitorsCanEdit = false`: confirmar que el visitante puede usar la red NITV para capturar fauna pero tiene bloqueado colocar/retirar `Terrarium`.
    5. Viajar al Insectario desde `BusStop (28, 11)` (`NatWarpInsect`), retornar (`NatInValley_ReturnHome`), donar un ejemplar en `NIVInnerInsec` y confirmar la replicación de `NatureInTheValley/Donated/*` entre Host y *farmhands*, además de medir conteo de `NatCreature` frente a `locationCap * numberOfPlayers()`.
  * **Criterios de aceptación:**
    1. Biomas `0..3` generan fauna esperada sin errores en SMAPI tras warps de otros jugadores, rollover y `NightHold`.
    2. `"NatCSpeedN"` no queda pegado ~28,5 min, las redes funcionan en visitas sin permiso de edición (manteniendo terrarios bloqueados), y queda documentada la medición real de `locationCap`.

- [ ] **LIVE-2501: Prueba viva de arranque fail-closed (`Lockdown`), respuesta de `GET /api/v1/host/health` con hilo principal bloqueado y disparo de watchdog**
  * **Origen:** Épica 25 ([ADR-0010](docs/adr/0010-fail-closed-health-and-backup-contract.md))
  * **Dependencias:** `TASK-2501`, `TASK-2502`, `TASK-2503`, `TASK-2504`.
  * **Procedimiento ejecutable por agente:**
    1. Arrancar Host de laboratorio con una versión de juego simulada fuera de `VerifiedGameVersions` (con `AllowUnverifiedGameVersion = false`) y en una segunda ejecución provocar un fallo simulado de parche `Security` (`checkFarmhandRequest`): comprobar que `AutoHostSaveName` **no** se carga, que un *farmhand* no puede unirse y que `GET /api/v1/host/health` devuelve `Status = "Lockdown"` con `Lockdown.Reasons` y `Patches.Failed`.
    2. Arrancar normalmente y bloquear deliberadamente el hilo principal durante `> 5 s` desde un hook de prueba: verificar que `GET /api/v1/host/health` responde en `< 50 ms` desde el hilo HTTP con `SecondsSinceLastTick` creciente.
    3. Con un socket Unix simulado en `NOTIFY_SOCKET`, verificar recepción periódica de `WATCHDOG=1` cada ~15 s y cese automático del envío cuando `SecondsSinceLastTick > HangThresholdSeconds`.
  * **Criterios de aceptación:**
    1. En cualquier fallo `Security`/`Integrity` o versión no verificada, ninguna conexión de *farmhand* llega a entrar en la partida.
    2. `/api/v1/host/health` responde sin timeouts aunque el hilo del juego esté congelado, y el emisor de watchdog se detiene al superar `HangThresholdSeconds`.

- [ ] **LIVE-2502: Ensayo vivo de respaldo con `p4e-backup.json` y restauración mediante `saves.restore` de `putre-ops`**
  * **Origen:** Épica 25 ([ADR-0010](docs/adr/0010-fail-closed-health-and-backup-contract.md)) · **Coordina con:** `PUTRE-302`, `PUTRE-303` (`../Putre/backlog.md`)
  * **Dependencias:** `TASK-2505`, `PUTRE-302`.
  * **Procedimiento ejecutable por agente:**
    1. Ejecutar rollover y checkpoint en Host de laboratorio, verificando la generación atómica de `p4e-backup.json` (esquema v1, hashes SHA-256 sin comprimir, `ServerWorldState` incluido) y la rotación según `BackupRetentionDays` / `CheckpointRetention`.
    2. Avanzar estado/oro tras el respaldo, provocar corrupción simulada o pérdida y lanzar el flujo `saves.restore` de `putre-ops` apuntando al directorio de laboratorio.
    3. Comprobar que la partida previa se mueve intacta a `Pelican4Ever_Backups/pre-restore/<UTC>/`, que todos los SHA-256 se validan antes de copiar y que el Host rearranca con `health.Status == "Ok"`, `SaveLoaded == true` y `TotalGameDays` coincidente con el manifiesto.
  * **Criterios de aceptación:**
    1. Restauración íntegra verificada de extremo a extremo (incluyendo `ServerWorldState`, puertas, inquilinos y pedidos pendientes).
    2. La partida anterior queda preservada en `pre-restore/` sin eliminarse ningún fichero.

- [ ] **LIVE-2503: Banco de carga escalonado con 4, 8, 12 y 16 farmhands sintéticos**
  * **Origen:** Épica 25 ([ADR-0010](docs/adr/0010-fail-closed-health-and-backup-contract.md)) · **Coordina con:** `PUTRE-401` (`../Putre/backlog.md`)
  * **Dependencias:** `TASK-2506`, `PUTRE-401`.
  * **Procedimiento ejecutable por agente:**
    1. Levantar Host en entorno equivalente a la VM objetivo (`Nice=5`, `LP_NUM_THREADS=1`, render headless) con `FarmSlotCount = 16`, las 16 residencias reclamadas (con edificios, cultivos, máquinas y anexos poblados) y NITV activo.
    2. Conectar secuencialmente **4, 8, 12 y 16 *farmhands* sintéticos** ejecutando actividad concurrente (movimiento entre Plaza/Puentes/Anexos, capturas, checkpoints intradía y un rollover completo).
    3. Registrar en `docs/`: tamaño del XML de partida, duración total de `SaveGame.Save()`, `LastCheckpointLongestTickMs`, bytes/s de red en clonación de *farmhands*, uso de RAM frente a `MemoryMax=4G`, FPS/tick del servidor y densidad real de `NatCreature` por mapa.
  * **Criterios de aceptación:**
    1. Informe de métricas guardado en `docs/` para 4, 8, 12 y 16 clientes concurrentes.
    2. Calibración validada de `HangThresholdSeconds` / `WatchdogSec` y decisión explícita sobre el tope soportado de `FarmSlotCount` y mitigación de `locationCap` de NITV para actualizar [ADR-0010](docs/adr/0010-fail-closed-health-and-backup-contract.md).
