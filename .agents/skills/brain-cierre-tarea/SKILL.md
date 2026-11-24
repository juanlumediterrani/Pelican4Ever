---
name: brain-cierre-tarea
description: >-
  Checklist obligatorio de cierre de tarea, commit, PR o release en Pelican4Ever.
  Úsalo siempre antes de dar por cerrada una tarea técnica, commitear o abrir una PR
  para enrutar el diff contra AGENTS.md y ejecutar el write-back al Brain (ADRs o docs/host-api.md).
---

# Cierre de Tarea y *Write-Back* al Brain (`Pelican 4Ever`)

Ejecuta este protocolo **antes** de hacer commit, abrir una PR o preparar una etiqueta de release.

---

## 1. Inspeccionar impacto contra el Brain

Desde la raíz del repositorio:

```bash
python3 scripts/verify_brain.py --check
python3 scripts/verify_brain.py --diff
```

Si los cambios ya están commiteados en una rama de trabajo, usa:

```bash
python3 scripts/verify_brain.py --since origin/main
```

Revisa los ADRs y contratos que reporte el enrutamiento para confirmar que el cambio no contradice ninguna cifra o invariante normativo.

---

## 2. Regla Obligatoria de *Write-Back*

> **Regla de cierre:** Si esta tarea cambia reglas de negocio, cifras normativas, API o arquitectura, **propone y aplica la actualización del ADR o de `docs/host-api.md` antes de abrir la PR** (idealmente en el mismo commit).

### Criterio de actualización según lo que cambió

1. **Rutas, payloads o comandos remotos de `/api/v1/host/*`:**
   - Sincroniza 1:1 [`HostApiCatalog.Routes`](../../../Pelican4Ever.Core/Admin/HostApiContract.cs) y [`docs/host-api.md`](../../../docs/host-api.md) con cabecera `### \`METHOD /api/v1/host/...\`` (verificado por `AdminApiLogicTests`).
2. **Calibración o hallazgo empírico en un ADR `Propuesto` (`ADR-0008`, `ADR-0013`):**
   - Actualiza ese ADR en **español** (`.md` base) y, cuando proceda aceptar la propuesta, pasa su estado a `Aceptado` actualizando [`docs/adr/README.md`](../../../docs/adr/README.md).
3. **Cambio o revocación de una decisión ya `Aceptada` (`ADR-0001`..`0007`, `0009`, `0010`, `0012`):**
   - **No reescribas** la decisión histórica: redacta un nuevo ADR en `docs/adr/` con el siguiente número libre (**`0014+`**, respetando `0011` reservado en `feat/v0.6-extended-concurrency`).
   - Enlaza el nuevo ADR en [`docs/adr/README.md`](../../../docs/adr/README.md) y en la tabla de enrutamiento de [`AGENTS.md`](../../../AGENTS.md).
   - Añade una nota de sustitución/ampliación en la cabecera del ADR anterior.
4. **Reporte puntual de prueba en vivo o benchmark:**
   - Guárdalo en [`docs/verifications/`](../../../docs/verifications/README.md) (`<tema>-verification-YYYY-MM-DD.md`), **nunca** en la raíz de `docs/`.
5. **Idioma único del Brain:**
   - Actualiza siempre el `.md` canónico en español. No edites `*.en.md` ni `*.zh-CN.md` salvo que la tarea sea explícitamente de traducción.
6. **Si el cambio NO altera reglas de negocio, API ni arquitectura** (p. ej. refactor interno, corrección puntual de bug dentro del contrato ya documentado o WIP de prueba):
   - Al abrir la PR con `gh pr create`, incluye en el cuerpo una línea explícita:
     `Brain: sin cambios normativos (<motivo breve>)`
     (requisito verificado por [`.agents/hooks.json`](../../hooks.json)).

---

## 3. Verificaciones Técnicas de Cierre

1. **Suite unitaria, auditoría Harmony y contratos (`host-api.md`, `ClientVersionPolicy`, `BrainDocsContractTests`):**
   ```bash
   python3 "../MCP Pelican/skills/smapi-harmony-pelican-bridge/scripts/check_harmony_patches.py" .   # usa --strict en archivos C# modificados
   dotnet test Pelican4Ever.Tests/Pelican4Ever.Tests.csproj -c Release -p:EnableModDeploy=false -p:EnableModZip=false --nologo
   ```
2. **Compilación de laboratorio sin pisar el runtime local del jugador:**
   ```bash
   dotnet build Pelican4Ever.sln -c Release -p:EnableModDeploy=false -p:EnableModZip=false
   ```
3. **Si vas a etiquetar o preparar una release (`main`) — `DEC-0001` / `DEC-0002`:**
   ```bash
   python3 scripts/calc_min_client_version.py --since <etiqueta_suelo> --target <nueva_version> --commits --check
   ```
   - Deposita el artefacto `.zip` limpio en `/home/mtx/Documentos/Niu D'Ocells/produccio/externa/Pelican4Ever Servidor/mods/` (conservando versión actual + anterior), regenera `SHA256SUMS` y actualiza `SERVEI.md` y `produccio/README.md` (nunca `~/Documentos/Producción/`).
   - Para tareas de gestión empresarial o hitos en vivo, registra/cierra en ERPNext (`PROJ-0005`, `DEC-0001`).
4. **Higiene Git (`dev` WIP):** verifica con `git status --short` que no mezclaste ni revertiste archivos de trabajo en curso ajenos a tu tarea.
