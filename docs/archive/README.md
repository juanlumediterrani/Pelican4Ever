# Archivo de documentación

Documentos que **ya no son la referencia vigente**: informes de una fecha concreta, planes de
épicas cerradas, propuestas sin implementar y guías que han sido sustituidas por
[`docs/despliegue/`](../despliegue/).

Cada archivo archivado lleva al principio una nota que explica **por qué** se archivó y **dónde
está ahora** su contenido. No los borres sin revisar: algunos guardan detalle técnico (formatos de
datos, inventarios de errores, procedimientos de emergencia) que aún no se ha trasplantado a las
guías vivas.

## Qué hay aquí

| Documento | Motivo del archivado | Dónde está ahora |
| --- | --- | --- |
| `servidor-persistente.md` | Sustituido por las guías de despliegue | [host-vps.md](../despliegue/host-vps.md), [host-local.md](../despliegue/host-local.md) |
| `guia-testers.md` / `guia-testers.en.md` | Anterior a la identidad de dispositivo | [cliente.md](../despliegue/cliente.md), [servidor-preview.md](../servidor-preview.md) |
| `plan-despliegue-y-guia-testers-identidad.md` | Plan de la Épica 18, ya cerrada | [ADR 0007](../adr/0007-dispositivo-identidad-y-acceso-seguro.md) |
| `protocolo-identidad-y-modelo-amenazas.md` | Especificación criptográfica de `P4E\x02` | [ADR 0007](../adr/0007-dispositivo-identidad-y-acceso-seguro.md) |
| `almacenamiento-claves-dispositivo.md` | Formato del fichero de clave | [ADR 0007](../adr/0007-dispositivo-identidad-y-acceso-seguro.md) |
| `migracion-y-restauracion-identidad.md` | Migración puntual de un jugador | [host-local.md](../despliegue/host-local.md) |
| `arquitectura-produccion-hermes.md` | Propuesta **no implementada** (contenedores) | [host-vps.md](../despliegue/host-vps.md) |
| `deployment-lines.md` / `development-line.md` | Rutas y ramas de una máquina concreta | [host-vps.md](../despliegue/host-vps.md) |
| `checkpoint-validation-2026-11-06.md` / `market-validation-2026-11-06.md` / `spike-checkpoints.md` | Informes con SHAs y resultados fechados | [host-vps.md](../despliegue/host-vps.md) § Copias de seguridad |
| `pruebas-residencias.md` | Comandos de prueba de una fase concreta | [host-api.md](../host-api.md), backlog |
| `save-migration-guide.md` | Migración de partidas de un solo uso | `scripts/migrate_save_to_plaza.py` |
| `reconnection-smapi-4.5.2.md` | Post-mortem de un fallo puntual de SMAPI | `ClientVersionGuardModule` |
| `pixel_art_pipeline.md` | Duplicaba la habilidad de pixel art | `.agents/skills/pixel-art/`, [agents/README.md](../../agents/README.md) |
