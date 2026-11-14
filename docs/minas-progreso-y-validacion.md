# Minas: comportamiento y validación pendiente

Rama de trabajo: `codex/minas-progreso-individual`.

## Comportamiento acordado

- **Minas normales:** los pisos y recursos son compartidos. Cada personaje utiliza su profundidad para el ascensor y tiene su propio registro de cofres de recompensa. Una misión especial puede imponer un límite temporal compartido al ascensor.
- **Caverna Calavera:** sin ascensor; pisos y recursos compartidos. Se conserva el comportamiento original de salas de tesoro, recompensas y evento de Qi. Los cofres de las salas se crean localmente y su consumo se registra en el personaje; falta verificar su repetición entre días y regeneraciones.
- **Volcán de Isla Jengibre:** sin ascensor; pisos, recursos y cofres normales compartidos. Se conservan los atajos y mejoras originales de la partida.
- **Regeneración:** se limpian pisos desocupados a las horas configuradas (por defecto 06:00 y 18:00), y también tras el tiempo de inactividad configurado (45 minutos por defecto). Los pisos ocupados se conservan.

## Pendiente antes de incorporar a `dev`

- [ ] Probar con dos jugadores de diferente profundidad que el ascensor solo ofrece los pisos alcanzados por cada uno, también después de guardar y reconectar.
- [ ] Probar que ambos jugadores pueden reclamar su propio cofre de un mismo piso, incluso con el otro presente, y que no vuelven a reclamarlo tras salir, reiniciar el piso y reconectar.
- [ ] Comprobar el límite del ascensor durante una misión especial y que los pisos y recursos siguen compartidos.
- [ ] Comprobar los reinicios programados y por inactividad, incluidos pisos ocupados, y el comportamiento de los cofres de la Caverna Calavera y del Volcán entre regeneraciones y días.
- [ ] Una vez superadas las pruebas, incorporar los cambios de esta rama a `dev`.

La compilación y las pruebas unitarias han pasado; estas comprobaciones en juego aún no se han realizado.
