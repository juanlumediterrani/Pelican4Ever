> **Documento archivado (2026-11-13).** No lo actualices: Informe de validación con fecha fija (SHAs de commits y binarios concretos). Su conclusión de bloqueo fue superada por el arreglo del mercado. Procedimiento vivo: [despliegue/host-vps.md](../despliegue/host-vps.md) § Copias de seguridad.
>
> Se conserva como registro histórico.

# Validación local de checkpoints · 2026-11-06

> Actualización posterior: el usuario autorizó la promoción conservando los checkpoints automáticos desactivados. Se publicaron `main` (`280802e`) y `dev` (`c16679a`). Este documento conserva los resultados de la primera validación; la corrección posterior del mercado se documenta en [su informe](market-validation-2026-11-06.md).

## Entorno y aislamiento

Stardew Valley 1.6.15 (build 24356), SMAPI 4.5.2, .NET SDK 8.0.427, Linux. Un host y hasta dos procesos de cliente en el mismo equipo, en un espacio de nombres de red privado con loopback. Cada proceso utiliza su propio perfil XDG y su propia copia de los mods. La partida es una copia de `Prueba29_453599927`; los Saves originales y los DLL de la instalación de Steam no se despliegan ni se reemplazan.

Mods presentes durante las pruebas: SpaceCore 1.28.4, Content Patcher 2.9.1, Fashion Sense 7.5.0, Nature In The Valley 1.3.4, Dr4coVoyagerBridge 0.1.1 y sus packs instalados. Un arnés temporal en `/tmp/p4e-checkpoint-lab` conecta los clientes por IP y registra estados deterministas, usando el hilo del juego. El arnés no forma parte del mod distribuido.

## Pruebas iniciales sobre `80cc24e`

| Caso | Resultado |
| --- | --- |
| Guardado sin clonar, 1 cliente | El XML conserva el inventario anterior: confirma G4. 3582 ms totales, 3555 ms de bloqueo máximo. |
| Guardado clonando, 2 clientes | Ambos XML coinciden con inventario, energía, correo y amistad vivos. 2989 ms totales, 2964 ms máximos. Sin desconexiones. |
| Guardado clonando, 1 cliente | Coincide con el estado vivo. 3422 ms totales, 3396 ms máximos. |
| Eventos SMAPI | No se emiten `Saving` ni `Saved`. Sí se registra el volcado explícito del estado del servidor. |
| Efectos secundarios | Tienda y Junimo: 1 → 0 → 0 a los 30 s. Compañero loro: 1 → 1 → 1. |
| Copia real de checkpoint | Los 6 archivos gzip descomprimidos coinciden byte a byte y por SHA-256 con los archivos originales de la copia. |
| Caída y restauración | Host terminado con SIGKILL, partida restaurada desde gzip y cargada. Los dos farmhands conservan 17 maderas, 23 piedras y 237 puntos de amistad; cofre con 11 maderas. Se omiten penalizaciones/contadores por carga y el reloj vuelve a la hora real. |

La recuperación offline aumenta la energía al reconectar; la salud sigue la inicialización normal de Stardew. No se exige igualdad de esos valores después del tiempo offline. Se verifica el inventario exacto y el estado serializado antes de reconectar.

## Correcciones derivadas

- Omitir checkpoints que borrarían tiendas, Junimos o personajes de WitchHut, incluidos interiores de edificios. La política lo aplica también a peticiones manuales.
- Agotar el guardado en el mismo tramo del hilo principal que clona los jugadores y vuelca `ServerWorldState`. Con SMAPI 4.5.2, `SModHooks.StartTask` ejecuta la tarea de guardado sincrónicamente. Esto evita cambios de mundo/red entre ticks de clonación y serialización.
- Cancelar si no puede clonarse cualquier farmhand o volcarse el estado del servidor.
- Detectar el modo de error de escritura del juego aunque su enumerador termine sin excepción; no anunciarlo como un checkpoint correcto.
- Reservar el spike a `CheckpointDebugMode=true`, desactivado por defecto.

## Validación de las correcciones

| Caso | Resultado |
| --- | --- |
| Compilación del mod | Correcta, 0 avisos y 0 errores, sin despliegue a Steam. |
| Tests de la rama con las correcciones | 950 tests aprobados, 0 fallos y 0 omitidos. También pasaron 953 con la integración provisional de `main`, posteriormente cancelada. Aviso preexistente xUnit2000 en `AgentToolOffsetTests`. |
| Protecciones en partida | Se rechaza el checkpoint con tiendas, Junimos, evento activo, ventana previa al rollover o farmhands sin clonación. El spike se rechaza con `CheckpointDebugMode=false`. |
| Spike síncrono, dos clientes | 3410 ms; ambos inventarios coinciden con el XML. |
| Automático con intervalo real de 5 minutos | Se emite `checkpoint.created` con `manual=false`, 2752 ms totales / 2751 ms de tramo síncrono, dos clientes conectados, 1 244 601 bytes de copia comprimida. |
| Manual posterior | `checkpoint.created`, 2857 ms. No se observan callbacks `UpdateTicked` intercalados mientras `CheckpointModule.IsSaving` está activo. |
| Retención configurada a 2 | Quedan exactamente las dos copias más recientes de la partida. Se conservan la carpeta de otra partida y una carpeta `.partial` de prueba. La copia más reciente se descomprime y coincide byte a byte con los 6 archivos guardados. |
| Estado del mod | `CustomData` del XML contiene los 3 jugadores y la oferta marcada como vendida. Los tokens de propietario no se incluyen en este informe. |
| Error de escritura con excepción | Se emite `checkpoint.failed`, no se crea copia y el SHA-256 de la partida no cambia. |
| Error nativo sin excepción | El primer ensayo detectó un falso positivo: `SaveGame` cambia al modo 9, distinto de `Game1.errorLogMode`. Se corrige para exigir continuar en `playingGameMode`. Repetición correcta: modo 9, solo `checkpoint.failed`, SHA-256 del save y conjunto de backups sin cambios. |

Las pruebas de la integración provisional utilizaron `80cc24e` + `cbc81bf` y las correcciones locales. La integración se cancela al fallar el mercado; la rama conserva las correcciones sin commit. Los archivos originales se verificaron mediante copias y hashes al cancelar.

## Bloqueo de la promoción: mercado

La prueba de compraventa falla **antes de ejecutar ningún checkpoint**:

1. Dos clientes con 500g y 17 maderas cada uno.
2. El cliente vendedor publica sus 17 maderas por 100g; se retiran de su inventario.
3. El comprador ejecuta la compra por la ruta real `ExecutePurchase` del módulo, con mensajes SMAPI al host.
4. La oferta queda vendida. Vendedor: 600g, 0 maderas; comprador: 400g, **17 maderas**, cuando debería tener **34**. El cofre sigue teniendo sus 11 maderas.

`CommunityMarketModule.HandleBuyListing` llama a `buyer.addItemByMenuIfNecessary(itemReceived)` desde el host. En Stardew Valley 1.6.15, `Farmer.addItemsByMenuIfNecessary` retorna inmediatamente si `!IsLocalPlayer`. El host descuenta el oro y cierra la oferta, pero no entrega nada al farmhand remoto. El código afectado ya existía en `main` y no pertenece al cambio de checkpoints; no se modifica el protocolo de mercado en esta validación.

**La batería de integración completa no pasa. No procede fusionar con main ni borrar la rama.** Los cambios de seguridad y este informe quedan para revisión; corregir y volver a probar la entrega del mercado es un requisito para cerrar G10.

## Decisión y límites

**Mecanismo viable con restricciones; promoción bloqueada por la prueba de mercado.** Los checkpoints automáticos permanecen desactivados por defecto. El bloqueo de varios segundos es visible; debe medirse de nuevo con partidas mayores y más clientes. No se acredita compatibilidad con mods distintos de los instalados ni con cualquier extensión que necesite los eventos SMAPI de guardado. TASK-1606 (metadatos y tratamiento completo de la carga), TASK-1607 (API/panel) y TASK-1608 (reinicio apoyado en checkpoint) siguen pendientes.

## Binarios finales de las correcciones

Compilación sin despliegue, rama `feat/day-checkpoints` (`80cc24e` + cambios locales sin commit). SHA-256:

- `Pelican4Ever.dll`: `6e8f90673cbc83ef01a20624112cbcb284324b66690e6e98b2c2adb962191905`
- `Pelican4Ever.Core.dll`: `c022f46b716cc4625e4d445a6cbe13b6b6353b234c34bfcd42cc141dcd542ffa`

La última repetición del error nativo de escritura usa estos binarios. Las instancias de prueba se cierran al terminar y `main` permanece en `cbc81bf`.
