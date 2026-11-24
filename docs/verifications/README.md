# Verificaciones y evidencias de laboratorio

Esta carpeta almacena protocolos de prueba puntual e informes fechados de laboratorio o *benchmarks* sobre el juego real. Sirven como **respaldo empírico** de épicas y ADRs, **no** como contratos normativos vigentes.

> **Regla para agentes (Brain):** Ignora `docs/verifications/**` durante el desarrollo ordinario salvo cuando el usuario pida explícitamente auditar mediciones históricas o redactar un nuevo informe de verificación. La verdad operativa y normativa vive en [`AGENTS.md`](../../AGENTS.md), [`docs/adr/`](../adr/README.md) y [`docs/host-api.md`](../host-api.md).

## Convención para nuevos informes

- Todo nuevo informe generado desde [`live-agent-tests-backlog.md`](../../live-agent-tests-backlog.md) debe guardarse aquí (por ejemplo, `docs/verifications/<tema>-verification-YYYY-MM-DD.md`), nunca en la raíz de `docs/`.
- Si una medición consolida cifras normativas (p. ej. aceptación de `ADR-0008` o `ADR-0013`), transplanta el veredicto al ADR correspondiente en el mismo commit.

## Informes registrados

| Fichero | Ámbito | ADR / Épica asociada |
| --- | --- | --- |
| [adr-0008-live-verification-2026-11-14.md](adr-0008-live-verification-2026-11-14.md) | Verificación técnica en vivo de curva de XP, pesca, minas, Caverna Calavera y telemetría económica (`TASK-2102`..`TASK-2106`) | [ADR-0008](../adr/0008-skill-economy-rebalance.md) · Épica 21 |
| [v0.5-residence-scaling-verification-2026-11-17.md](v0.5-residence-scaling-verification-2026-11-17.md) | Medición de niveles 16/32/64 en AWS EC2 y estrés con 16 clientes concurrentes (`TASK-2409`, `TASK-2411`) | [ADR-0010](../adr/0010-scalable-residences-admission-queue.md) · Épica 24 |
| [skill-xp-multiplayer-verification.md](skill-xp-multiplayer-verification.md) | Guion reproducible multijugador para validar curva de experiencia y telemetría (`TASK-2102`) | [ADR-0008](../adr/0008-skill-economy-rebalance.md) · Épica 21 |
