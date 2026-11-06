# Mercado: entrega a jugadores remotos · 2026-11-06

## Problema reproducido

El anfitrión cobraba 100g y marcaba la oferta como vendida, pero `Farmer.addItemByMenuIfNecessary` no entregaba los objetos al comprador remoto. La misma llamada perdía objetos al retirar una oferta. Cambiar directamente el inventario remoto desde el host tampoco sirve: Stardew solo difunde los deltas del granjero propietario de ese proceso.

## Corrección

La entrega se ejecuta en el cliente propietario, con un identificador único por operación. El cliente guarda en `Farmer.modData` un recibo persistente de aceptación o rechazo; se transmite junto con el inventario en el mismo `NetFarmerRoot`.

El host reserva el precio de compra y guarda una entrega pendiente. El vendedor cobra cuando el host observa el recibo y el inventario sincronizados; un simple ACK no basta. Si el cliente rechaza la entrega por falta de espacio, la oferta se reabre y se devuelve la reserva. Una retirada no elimina la oferta hasta confirmar la devolución.

Los mensajes repetidos consultan el recibo: no vuelven a añadir objetos, pagar al vendedor ni reactivar una entrega rechazada. El diario de operaciones pendientes forma parte de `ServerWorldState` (esquema 4) y se reenvía al reconectar el destinatario. Los recibos se conservan con el personaje para que una restauración no pierda la información de qué se entregó.

La versión del mod pasa a **0.2.0**: host y clientes deben actualizarse juntos. El control existente compara versión mayor/menor y rechaza clientes 0.1.x cuando `RequireClientMod` está activo. Para volver a una versión anterior, restaurar también su partida anterior o resolver primero todas las entregas pendientes.

## Pruebas

Se reutilizan el host y dos clientes en red privada, perfiles XDG y partida copiada descritos en [la validación de checkpoints](checkpoint-validation-2026-11-06.md). Los archivos de la instalación habitual no se despliegan ni se reemplazan.

| Prueba | Resultado |
| --- | --- |
| Suite de dominio y migraciones | 965 tests aprobados, 0 fallos y 0 omitidos. Aviso preexistente xUnit2000 en `AgentToolOffsetTests`. |
| Compilación del mod | Correcta, 0 avisos y 0 errores; despliegue solo a las copias de prueba. |
| Compra normal entre dos clientes | Comprador: 500g/17 maderas → 400g/34 maderas; vendedor: 500g → 600g. Coincide el inventario del cliente propietario con el del host. |
| Compra y entrega repetidas | No cambia el resultado: comprador 400g/34 maderas, sin otro pago al vendedor. |
| Retirada de oferta | Las 17 maderas vuelven al cliente propietario, no cambia su oro y se elimina la oferta. Repetir la entrega no añade objetos. |
| Retirada con inventario lleno | Conserva la oferta y el oro. Al liberar espacio, un mensaje antiguo de esa entrega sigue rechazado; una retirada nueva devuelve los objetos una sola vez. |
| Compra con inventario lleno al llegar la solicitud | Se rechaza desde el cliente, se devuelve la reserva de 100g y la oferta continúa disponible; el vendedor no cobra. |
| Reintento de una compra rechazada | El mensaje antiguo no entrega objetos al liberar espacio. Una compra nueva entrega 17 maderas y descuenta 100g una sola vez. |
| Checkpoint con entrega detenida | XML real: esquema 4, entrega pendiente con reserva de 100g, comprador 200g/34 maderas, vendedor 700g y sin recibo anticipado. |
| SIGKILL, restauración gzip y reconexión | Se recupera la entrega pendiente: comprador 200g/51 maderas, vendedor 800g, diario vacío. La nueva entrega y los mensajes de compra repetidos no alteran esos valores. |
| Checkpoint posterior a completar la compra | XML: comprador 200g/51 maderas, vendedor 800g, recibo persistente de aceptación y ninguna entrega pendiente. |

Los casos de compra y rechazo se observaron tanto en la consola/estado del host como en los inventarios de los dos clientes. El arnés temporal no forma parte de la distribución. Se utilizó una partida de prueba con inventarios preparados entre escenarios, sin modificar partidas originales.

## Relación con checkpoints

La copia del personaje contiene inventario y recibo; el estado del mod contiene las reservas pendientes. Si un checkpoint se toma antes de recibir el delta del personaje, conserva la entrega pendiente. Si ya conserva el recibo, la reconexión no vuelve a entregar los objetos. La confirmación final del host paga al vendedor una sola vez y elimina la operación pendiente.

Esto verifica el protocolo de entrega del mercado; no constituye una garantía general sobre cualquier mod externo ni sobre todas las acciones simultáneas de la simulación multijugador.


## Binarios probados y entrega

Los checkpoints anteriores se publicaron en `main` (`280802e`) y `dev` (`c16679a`), por autorización expresa del usuario. La corrección del mercado se prepara en `codex/fix-market-remote-delivery`, pendiente de la firma solicitada por el usuario al terminar las pruebas.

SHA-256 de los binarios usados en la recuperación final:

- `Pelican4Ever.dll`: `908474b10427a982327137a9b3f12db55471690239510077bc726eac47a175bc`
- `Pelican4Ever.Core.dll`: `bb868c4c8fd3413a268f577b49e4c002b2611f4f387b186a811978c6a6fed52b`

Al terminar se cierran el host y los dos clientes aislados. Los checkpoints automáticos siguen desactivados por defecto. Los recibos se conservan por operación; no se borran automáticamente, ya que son necesarios para identificar reintentos después de una restauración.
