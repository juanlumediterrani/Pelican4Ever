# Verificación multijugador de la curva de experiencia

Pendiente de ejecutar en una partida de prueba con host y farmhand, ambos con la misma versión del mod. Registrar fecha, versiones, pasos, valores observados y resultados antes de cerrar TASK-2102. Usar una copia de la partida: los pasos conceden experiencia y crean un checkpoint.

1. Con los valores por defecto, anotar los puntos de Pesca del farmhand. Obtener 300 XP y después cuatro eventos independientes de 1 XP; comprobar que estos últimos suman 1 XP y que Cultivo no cambia de ritmo.
2. Superar el cupo, desconectar y reconectar el farmhand el mismo día. Comprobar que la siguiente experiencia sigue reducida y que una fracción pendiente continúa acumulándose. Comprobar también un evento de Recolección de Nature in the Valley.
3. Con el farmhand conectado, lanzar `p4e_checkpoint_now` mientras gana XP. Comprobar en el log que termina el checkpoint, restaurar la copia de prueba y verificar que los puntos del personaje y el contador conservado concuerdan. Repetir con reinicio controlado.
4. Impedir temporalmente que un farmhand confirme la sincronización y comprobar que el checkpoint se cancela al agotarse el plazo, que no se sustituye la copia anterior y que el farmhand continúa recibiendo XP tras liberarse.
5. Avanzar el día mediante el rollover en la partida de prueba. Comprobar que el cupo vuelve a estar completo tanto para un jugador conectado como para otro que estaba desconectado.
6. Generar créditos por tienda, mercado vecinal y envíos con carteras individuales; repetir con cartera compartida. Tras el rollover, comprobar el evento `economy.daily` y el fichero de `data/economy-telemetry/<id-partida>/`. Los envíos deben contarse en el día cerrado; la muestra distingue créditos individuales de compartidos y no identifica los compartidos como ingresos personales. Reintentar el rollover y comprobar que no se duplica la muestra.

La barrera retiene temporalmente las nuevas concesiones de XP hasta terminar o cancelar el checkpoint. La prueba debe revisar también si se produce actividad que conceda XP durante esa ventana y documentar su comportamiento al restaurar la copia; la sincronización de red y el guardado nativo no quedan cubiertos por las pruebas unitarias.

La cifra de oro es la suma de créditos brutos observados, no el beneficio neto ni exclusivamente ingresos por ventas: puede incluir reembolsos, transferencias y ajustes de administración. La cartera compartida no permite atribuir de forma fiable el oro de los envíos a una persona.
