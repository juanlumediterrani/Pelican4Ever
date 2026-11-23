# ADR-0012: Mod Único con Compatibilidad Desacoplada (`MinCompatibleClientVersion`) y Flujo de Ramas Cortas

* **Estado:** Aceptado
* **Fecha:** 2026-11-23
* **Versión objetivo:** 0.5.4+
* **Amplía:** [ADR-0004](0004-host-authority-client-mod-and-satiety.md) (punto 1: política de versión entre cliente y host)

## Contexto

Durante el ciclo `0.5.x` surgió la duda de si convenía separar `Pelican 4Ever` en dos aplicaciones/mods distintos (cliente vs. host) al comprobar que cambios listos en `dev` (fixes solo de host como `TASK-2701` o fixes de interfaz Android) quedaban bloqueados o forzaban una actualización global de todos los clientes.

Al auditar el código y las restricciones del motor de Stardew Valley 1.6.15 se verificaron tres hechos:

1. **No existe abstracción de host autoritativo para el jugador:** cada `Farmhand` posee su propio `NetRoot<Farmer>` y ejecuta en cliente reglas esenciales del juego (`Farmer.doneEating`/saciedad, restricciones de herramientas y bombas, experiencia, pesca, minas, cama, menús de pedidos) además de compartir la definición declarativa de `Data/Locations`, mapas puente y biomas (ADR-0004). De los 45 módulos registrados en `ModEntry`, **32 son `ModuleSide.Both` (71 %)**, 12 son `ModuleSide.Host` y solo 1 es `ModuleSide.Client`. Partir el mod en dos duplicaría mantenimiento, manifiestos y pruebas cruzadas sobre el 71 % del código sin lograr un cliente tonto.
2. **Acoplamiento innecesario de `PatchVersion`:** `ClientVersionGuardModule` expulsaba a cualquier cliente con `client.PatchVersion < host.PatchVersion`. Así, un parche puramente de host (como `HeadlessEventGuardModule` o `AdminApiModule`) obligaba a redistribuir el mod a todos los jugadores de PC y Android.
3. **Cuello de botella en rama de integración larga (`dev`):** acumular fixes terminados junto a trabajo en curso sin commitear (WIP) en la misma rama `dev` retrasa su promoción a `main`.

## Decisión

Se mantiene **un único mod (`Pelican4Ever`)** compartido por host y clientes, y se adoptan dos medidas estructurales (Formas 1 y 2):

### Forma 1 · Desacoplamiento de `MinCompatibleClientVersion` y regla normativa de subida

1. **Fuente única de compatibilidad en Core:** `ClientVersionPolicy` (`Pelican4Ever.Core/Networking/ClientVersionPolicy.cs`) fija:
   - `ProtocolRevision` (actualmente `"1"`, reutilizada por `ClientVersionGuardModule` y `ClientSecurityModule`).
   - `MinCompatibleClientVersion` (`SemVer`, suelo mínimo aceptado por el host).
2. **Regla en ejecución (`ClientVersionGuardModule` → `ClientVersionPolicy.Evaluate`):**
   - El cliente debe compartir exactamente `Major.Minor` con el host (`manifest.json`).
   - El cliente debe cumplir `clientVersion >= MinCompatibleClientVersion` (en lugar de `client.PatchVersion >= host.PatchVersion`).
   - Dentro de la misma serie `Major.Minor`, se aceptan clientes más nuevos que el host (p. ej., un hotfix de UI/Android `0.5.5` conectando a un host `0.5.4`), siempre que `ProtocolRevision` coincida.
   - **Invariante *fail-closed*:** si por error `MinCompatibleClientVersion` queda fuera del `Major.Minor` del host o por encima de la versión del host, `NormalizeFloor` eleva el suelo al valor exacto del host y el test de contrato `ClientVersionPolicyTests.ManifestVersion_SatisfiesClientVersionPolicyContract` falla en CI.
3. **Regla determinista para elevar `MinCompatibleClientVersion` al preparar una release:**
   Se calcula siempre sobre el diff completo desde la etiqueta del suelo vigente hasta `HEAD` (`ClientVersionPolicy.ComputeNextMinVersion` y `scripts/calc_min_client_version.py`):

   | Categoría de cambio | Rutas representativas | ¿Eleva `MinCompatibleClientVersion`? |
   | --- | --- | --- |
   | **Cambio de `Major` o `Minor`** | `manifest.json` (`0.5.x` → `0.6.0`) | **Sí, siempre** (al menos a `Major.Minor.0` / versión objetivo) |
   | **Protocolo, modelos de red o seguridad** | `ProtocolRevision`, `Pelican4Ever/Models/*`, `Pelican4Ever.Core/Security/*`, `AdmissionLogic.cs` | **Sí, siempre** |
   | **Definición del mundo / mapas / modpack** | `Pelican4Ever.Core/World/*`, `MapCloneHelper.cs`, `Pelican4Ever/assets/*`, `deploy/modpack/*` | **Sí, siempre** |
   | **Módulos compartidos (`Both`) o reglas en `Core`** | Resto de `Pelican4Ever/*` y `Pelican4Ever.Core/*` | **Sí por defecto (*fail-safe*)**; solo admite exención auditada (`--audit-safe`) si el diff se limita a UI/plataforma cliente sin alterar protocolo, `Data/Locations` ni reglas anti-ventaja |
   | **Solo host (`ModuleSide.Host`)** | Los 12 módulos `Host`, `Core/Admin/*`, `Core/Server/*`, `PelicanApi.cs`, `SystemdWatchdogService.cs`, `ClientVersionPolicy.cs` | **No** (mantiene el suelo anterior) |
   | **Solo UI/plataforma cliente segura** | `Framework/AndroidMenuCompat.cs`, `Modules/ModToggleModule.cs` | **No** (mantiene el suelo anterior) |
   | **Fuera de runtime** | `docs/*`, `*.md`, `Pelican4Ever.Tests/*`, `scripts/*`, `.github/*`, `i18n/*`, `agents/*`, `deploy/` (salvo `deploy/modpack/`) | **No** (mantiene el suelo anterior) |

4. **Verificación obligatoria antes de etiquetar:**
   ```bash
   python3 scripts/calc_min_client_version.py --since <etiqueta_suelo> --target <nueva_version> --commits --check
   dotnet test Pelican4Ever.Tests/Pelican4Ever.Tests.csproj -c Release
   ```
   Además, el test `AllHostOnlyModulesInRepo_AreClassifiedAsSafeByClientVersionPolicy` garantiza que todo nuevo módulo `ModuleSide.Host` se registre explícitamente en `ClientVersionPolicy`.

### Forma 2 · Flujo de ramas cortas sobre `main`

1. **`main` es la rama estable y desplegable.** Cada fix o feature acotado nace en una rama corta desde `main` (`fix/...`, `feat/...`) y se fusiona a `main` en cuanto supera `dotnet test` y las comprobaciones aplicables.
2. **Aislamiento de WIP mediante `git worktree`:** cuando hay trabajo en curso sin commitear en el checkout principal (p. ej. experimentos o pruebas vivas en `dev`), los parches y releases hacia `main` se preparan en un `git worktree` temporal limpio en vez de esperar a que termine el WIP o mezclarlo.
3. **Papel de `dev`:** mientras `dev` exista como rama de integración, recibe automáticamente los cambios de `main` mediante la cascada de CI (`.github/workflows/cascada-ramas.yml`), pero deja de ser un cuello de botella obligatorio para promover fixes independientes.

### Nota de evolución futura (descartada por ahora)

Si en el futuro el bloque puramente operativo del servidor creciese de forma independiente (los módulos `ModuleSide.Host` de infraestructura: `AdminApi`, `AutoHost`, `HeadlessRender`, `HeadlessEventGuard`, `Checkpoint`, `ProbationTelemetry`), podrá extraerse únicamente como un mod satélite opcional `Pelican4Ever.HostOps` consumiendo `PelicanApi`, conservando siempre un único mod `Pelican4Ever` para las reglas de juego.

## Primer cálculo aplicado: Release `0.5.4` (base `0.5.3`)

Al evaluar `0.5.3..0.5.4` con `scripts/calc_min_client_version.py`:

- Los cambios procedentes de `dev` (`74853e3` `fix(android)` y `b18654d` `TASK-2701`) y las integraciones de Corazón Pelican solo-host (`083a5a1`, `e71236f`, `7ba6fe7`, `b15b1a1`, `1b97b37`) **no** habrían exigido elevar el suelo por sí solos.
- Sin embargo, el commit **`c444aa6`** (ya en `main` tras `0.5.3`) absorbió `Pelican4Ever.NITVCompat` dentro de `Data/Locations` (`Pelican4Ever.Core/World/FarmTemplateLogic.cs` y `Pelican4Ever/Modules/FarmSlotModule.cs`) y retiró `deploy/modpack/Pelican4Ever.NITVCompat` de `RequiredClientMods`. Un cliente `0.5.3` con el modpack actualizado sin `NITVCompat` perdería esos `CustomFields` de bioma.
- **Resultado normativo para `0.5.4`:** `MinCompatibleClientVersion = 0.5.4`. A partir de `0.5.4`, cualquier parche `0.5.5+` que toque solo módulos `Host` o compatibilidad UI cliente podrá mantener `MinCompatibleClientVersion = 0.5.4` sin expulsar clientes `0.5.4`.

## Consecuencias

* **Positivas:** despliegues solo-host en Putre sin forzar actualizaciones masivas de clientes PC/Android; hotfixes cliente sin reiniciar el servidor; regla objetiva, auditable y probada en CI; cero duplicación de código en los 32 módulos compartidos.
* **Negativas:** ante un cambio en un módulo `Both`, la regla *fail-safe* exige revisar si es realmente inocuo (`--audit-safe`) o elevar `MinCompatibleClientVersion`.
