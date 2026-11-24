# `AGENTS.md` — Índice Operativo del Brain (*Pelican 4Ever*)

> **Punto de entrada obligatorio para cualquier agente de IA.**
> No explores ni cargues `docs/` a ciegas. Lee primero estas reglas de contexto, localiza los archivos obligatorios en la tabla de enrutamiento condicional según la carpeta que vayas a tocar y aplica el protocolo de *write-back* antes de cerrar la tarea.

---

## 1. Reglas de Contexto y Fuente Única de Verdad

1. **Idioma único normativo:** El **español** (ficheros `.md` sin sufijo de idioma) es la única fuente de verdad operativa del repositorio.
2. **Rutas prohibidas en búsquedas y lectura de contexto** *(salvo petición literal del usuario para traducir o auditar histórico)*:
   - **`docs/archive/**`** — diseños retirados, protocolos obsoletos y planes de épicas cerradas.
   - **`docs/verifications/**`** — informes puntuales y logs fechados de sesiones de laboratorio; los veredictos vigentes ya están consolidados en los ADRs.
   - **`**/*.en.md` y `**/*.zh-CN.md`** — traducciones derivadas para lectores humanos (`architecture.{en,zh-CN}.md`, `design.{en,zh-CN}.md`, `docs/**/*.{en,zh-CN}.md`). **Prohibido** leerlas como referencia o editarlas parcialmente durante un cambio técnico.
3. **Orden de precedencia ante conflicto:**
   1. **Código fuente y tests ejecutables** (`Pelican4Ever.Core/`, `Pelican4Ever.Tests/`, `HostApiCatalog.Routes`, `ClientVersionPolicy`, `ServerConfig.Validate`).
   2. **ADRs `Aceptados`** en [`docs/adr/README.md`](docs/adr/README.md) y el contrato REST en [`docs/host-api.md`](docs/host-api.md).
   3. **Guías y especificaciones vivas** ([`architecture.md`](architecture.md), [`design.md`](design.md), [`docs/despliegue/`](docs/README.md)).
   4. **Backlogs** ([`backlog.md`](backlog.md), [`docs/backlog/recipe-unlock-parity.md`](docs/backlog/recipe-unlock-parity.md), [`live-agent-tests-backlog.md`](live-agent-tests-backlog.md), [`test-users-tasks.md`](test-users-tasks.md)).
4. **Tratamiento de ADRs en estado `Propuesto` o reservados:**
   - [`ADR-0008`](docs/adr/0008-skill-economy-rebalance.md) está implementado y verificado técnicamente en vivo, pero sigue `Propuesto` a la espera de calibración humana ([`TASK-2107`](test-users-tasks.md)).
   - [`ADR-0013`](docs/adr/0013-fail-closed-health-and-backup-contract.md) *(renumerado desde `ADR-0010`; código `TASK-2501..2505` aún cita `ADR-0010`)* está parcialmente implementado y pendiente de pruebas vivas (`LIVE-2501..2503`).
   - **`ADR-0011` está reservado** en la rama `feat/v0.6-extended-concurrency` (`0011-extended-concurrency-interest-management.md`); cualquier ADR nuevo en `main`/`dev` debe tomar **`0014+`**.
5. **Contratos entre repositorios (Nivel 2 · `MCP Pelican`):**
   - Para consultar contratos o ADRs de los repositorios hermanos acoplados (`Corazón Pelican`, `Stardew Voyager`; `Putre` es legacy y el agente runtime de AWS es `Yuki`), usa el servidor `MCP Pelican` (`/home/mtx/Escritorio/WIP Proyectos/Ecosistema Pelican/MCP Pelican`) en lugar de duplicar copias estáticas entre proyectos.

---

## 2. Invariantes Críticos del Sistema y Entorno

- **Plataforma y SDK:** Stardew Valley `1.6.15`, SMAPI `4.1+`, solución `.NET 6` (`AnyCPU` multiplataforma sin `<RuntimeIdentifier>`) compilada con el SDK `.NET 8.0.427` clavado en [`global.json`](global.json). Cero advertencias toleradas (`TreatWarningsAsErrors = true`, `Nullable = enable`).
- **Higiene Git y protección de `dev` / runtime del jugador:**
  - Comprueba siempre `git status -s` antes de compilar o cambiar de rama: `dev` suele contener trabajo en curso sin commitear (WIP). Nunca hagas `stash`, `checkout --` ni reviertas WIP ajeno.
  - Si necesitas preparar un fix o release sobre `main` sin perturbar `dev`, usa un `git worktree` temporal limpio y elimínalo al acabar.
  - En compilaciones de prueba o validación añade **siempre** `-p:EnableModDeploy=false -p:EnableModZip=false` para no sobrescribir `~/.steam/.../Stardew Valley/Mods/`.
- **Frontera `Core` vs `Mod`:**
  - Toda lógica pura de dominio va en [`Pelican4Ever.Core/`](Pelican4Ever.Core/Pelican4Ever.Core.csproj) (**cero** referencias a `StardewValley`, `SMAPI`, `MonoGame` o `Harmony`) con pruebas xUnit en [`Pelican4Ever.Tests/`](Pelican4Ever.Tests/Pelican4Ever.Tests.csproj).
  - Todo subsistema SMAPI implementa `IModule` en [`Pelican4Ever/Modules/`](Pelican4Ever/Modules/).
  - Todo parche Harmony nuevo se registra obligatoriamente en [`PatchRegistry`](Pelican4Ever/Framework/PatchRegistry.cs) declarando su criticidad (`Security`, `Integrity` o `Experience`).
- **Autoridad, estado y red:**
  - [`ServerWorldState`](Pelican4Ever.Core/World/ServerWorldState.cs) en el Host es la fuente única de verdad persistente.
  - Nunca escribas desde el Host en `farmer.modData` de un `Farmhand` remoto (`NetRoot<Farmer>` pertenece al cliente) ni en `Farmer.Money` remoto cuando hay cartera compartida (`FarmerTeam.SetIndividualMoney`). `GameLocation.modData` es solo vista replicada de lectura.
  - Único puerto público: `24642/udp` (identidad ECDSA P-256 con desafío `P4E\x02`). La API REST del Host (`8085`) vive en loopback y [`/api/v1/host/health`](docs/host-api.md) responde atómicamente fuera de `MainThreadActionQueue`.
- **Contratos vigilados por CI (`dotnet test` + `verify_brain.py`):**
  - Paridad 1:1 entre `HostApiCatalog.Routes` ([`HostApiContract.cs`](Pelican4Ever.Core/Admin/HostApiContract.cs)) y las cabeceras `### \`METHOD /api/v1/host/...\`` de [`docs/host-api.md`](docs/host-api.md).
  - Clasificación de módulos `ModuleSide.Host` y suelo `MinCompatibleClientVersion` en [`ClientVersionPolicy.cs`](Pelican4Ever.Core/Networking/ClientVersionPolicy.cs) y [`scripts/calc_min_client_version.py`](scripts/calc_min_client_version.py) ([`ADR-0012`](docs/adr/0012-client-version-compatibility-and-branching.md)).
  - Integridad estructural del Brain verificada por [`BrainDocsContractTests.cs`](Pelican4Ever.Tests/BrainDocsContractTests.cs) y [`scripts/verify_brain.py`](scripts/verify_brain.py).

---

## 3. Enrutamiento Condicional (Qué leer según qué código toques)

Antes de diseñar o editar archivos en una zona del repositorio, carga **únicamente** los documentos mapeados para esa zona:

| Si vas a tocar... | Lectura obligatoria (fuente en español) | Qué regula |
| --- | --- | --- |
| `Pelican4Ever.Core/Time/*`, `Modules/{RealTimeClockModule,DailyRolloverModule,RolloverLockModule}.cs`, `Framework/NightHoldWorldSimulator.cs`, `Core/Admin/{SafeRolloverWorkflow,RolloverDayGuard,TimeJumpHookRegistry}.cs` | [`ADR-0001`](docs/adr/0001-native-daily-rollover.md), [`ADR-0002`](docs/adr/0002-realtime-clock-and-epoch-calendar.md) | Reloj 1:1 (`0600–2550`), `NightHold`, calendario por `CalendarEpoch`, rollover nativo `02:00`, amanecer `06:00` y hook `/time` |
| `Core/Economy/{SatietyBudget,SittingRegenLogic,PassiveRegenLogic}.cs`, `Core/Players/{SleepPolicy,OfflineStaminaRegenLogic,GroundSitLogic,BedLogoutPayload,PersistedFoodBuff}.cs`, `Modules/{EconomyModule,BedLogoutModule,GroundSitModule}.cs` | [`ADR-0004`](docs/adr/0004-host-authority-client-mod-and-satiety.md) (§2, §4), [`ADR-0006`](docs/adr/0006-session-energy-satiety-wellfed-spa.md) | Saciedad con vaciado en 4 h, «Bien comido» sentado, buff diario del Spa y penalización de *Bed-Logout* |
| `Core/Economy/{DailySkillExperience,SkillExperienceCurve,SkillCheckpointBarrier,SkullCavernDescent,CategoryPriceMultiplier,DailyGoldLedger,CommunityMarketLogic,MailOrderLogic}.cs`, `Core/World/{MineCooldownLogic,MineProgressLogic}.cs`, `Modules/{SkillExperienceModule,FishingRebalanceModule,MineLadderModule,SkullCavernDescentModule,DailyGoldTelemetryModule,CommunityMarket*,MailOrder*}.cs` | [`ADR-0008`](docs/adr/0008-skill-economy-rebalance.md), [`minas-progreso-y-validacion.md`](docs/minas-progreso-y-validacion.md) | Cupo diario de XP (`300` + `0.25x`), pesca (`2.5x`/`1.5x`), minas 12 h y ascensor individual, Caverna Calavera, mercado y pedidos por buzón |
| `Core/World/{HostPlazaLayout,DoorCatalogValidator,SpatialWarpRouter,PlazaTopologyVerifier,FarmTemplateLogic,Residence*,VisitorPermissionLogic,TownMailboxLogic,SaveMigrationLogic,MinecartPlazaLogic,VanillaFarmExitTable,NpcShopkeeperLogic,LocationCullingLogic}.cs`, `Modules/{FarmSlotModule,FarmTypeModule,FarmTypeLootModule,PrivateFarmCaves,VisitorGuardModule,BombRestrictionsModule,TownMailboxModule}.cs`, `Pelican4Ever/assets/doors.json` | [`ADR-0003`](docs/adr/0003-declarative-farm-slots.md), [`ADR-0005`](docs/adr/0005-host-plaza-and-bridge-maps.md), [`ADR-0010`](docs/adr/0010-scalable-residences-admission-queue.md), [`pruebas-residencias.md`](docs/pruebas-residencias.md) | Plaza Central, puentes `1..15`, gates al este de `Town`, residencias `16/32/64`, co-residencia `Owner`/`Tenant` y permisos de visita |
| `Framework/MapCloneHelper.cs`, `Core/World/{TerrainTileClassifier,VerticalMultiTileClassifier,Annex*,ResidenceAnnex*}.cs`, `Modules/AnnexReviewModule.cs`, `Pelican4Ever/assets/anexo-*.tmx`, `tools/MapRenderer/*` | [`ADR-0009`](docs/adr/0009-annex-vanilla-composition.md), [`anexos-ventanas-recorte.md`](docs/anexos-ventanas-recorte.md) | Recortes 40×32, regla `Back.Properties["Passable"]`, clasificadores del atlas `.xnb` y verificación BFS |
| `Pelican4Ever.Core/Security/*`, `Core/Players/{ClientPairingLogic,PlayerAuthLogic}.cs`, `Modules/{ClientSecurityModule,ProbationTelemetryModule}.cs` | [`ADR-0007`](docs/adr/0007-dispositivo-identidad-y-acceso-seguro.md) | Claves ECDSA P-256, desafío `P4E\x02` previo a `sendAvailableFarmhands` y protocolo de observación |
| `Pelican4Ever.Core/Networking/*`, `Core/Players/IdleKickLogic.cs`, `Modules/{ClientVersionGuardModule,IdleKickModule,ModToggleModule}.cs`, `Framework/AndroidMenuCompat.cs`, `Pelican4Ever/manifest.json`, `scripts/calc_min_client_version.py` | [`ADR-0004`](docs/adr/0004-host-authority-client-mod-and-satiety.md), [`ADR-0010`](docs/adr/0010-scalable-residences-admission-queue.md) (§1–5), [`ADR-0012`](docs/adr/0012-client-version-compatibility-and-branching.md) | Compatibilidad `MinCompatibleClientVersion`, cola FIFO de admisión, reserva por caída e `IdleKick` |
| `Pelican4Ever.Core/Admin/*`, `Pelican4Ever.Core/Server/*`, `Pelican4Ever.Core/Config/*`, `Framework/{PatchRegistry,HostHealthTracker,SaveBackupService,SystemdWatchdogService}.cs`, `Modules/{AdminApiModule,AutoHostModule,CheckpointModule,CheckpointSpikeModule,HeadlessRenderModule,HeadlessEventGuardModule,PersistenceModule,StatusCommandModule,HostEventRelayModule}.cs`, `Pelican4Ever/PelicanApi.cs` | [`host-api.md`](docs/host-api.md), [`ADR-0013`](docs/adr/0013-fail-closed-health-and-backup-contract.md) | Rutas REST v1 (`/api/v1/host/*`), arranque *fail-closed* `Lockdown`, `/health` fuera del hilo principal, `sd_notify` y `p4e-backup.json` |
| `Core/World/{RecipeFallbackLogic,FurnaceUnlockLogic}.cs`, `Modules/{FurnaceUnlockModule,RecipeFallbackModule}.cs` | [`recipe-unlock-parity.md`](docs/backlog/recipe-unlock-parity.md) | Entrega de `mailForTomorrow` en rollover y redes de seguridad de recetas de evento |
| `Pelican4Ever.Core/{Chat,Festivals,Pets,Localization}/*`, `Pelican4Ever/i18n/*`, `Modules/{ChatChannelModule,Festival*,ClientPetAdoptionModule,NitvProgression}.cs` | [`design.md`](design.md) (§4.3–4.5, `RNF-04`), [`architecture.md`](architecture.md) (§4.4–4.5) | Chat espacial/global, festivales desacoplados, mascotas de cliente, colección individual NITV e `i18n` |
| `Pelican4Ever/Modules/AgentCosmeticsModule.cs`, `Core/Players/{AgentBodyPreset,AgentToolOffset,FarmerAppearancePreset}.cs`, `Pelican4Ever/assets/skins/*`, `agents/*`, `scripts/dr4co_*` | [`agents/README.md`](agents/README.md), `.agents/skills/pixel-art/SKILL.md` | Hojas 126 cuadros de `Dr4co`/`Putre`, offsets de herramientas y auditoría de pixel art |
| `deploy/*`, `docker-compose.yml`, `config.example.json`, `.github/*` | [`host-vps.md`](docs/despliegue/host-vps.md), [`host-local.md`](docs/despliegue/host-local.md), [`cliente.md`](docs/despliegue/cliente.md), [`servidor-preview.md`](docs/servidor-preview.md), [`ADR-0012`](docs/adr/0012-client-version-compatibility-and-branching.md) | Despliegue 24/7 en Linux/VPS, firewall `24642/udp`, CI, cascada `main -> dev` y ramas cortas |

---

## 4. Protocolo de Cierre y *Write-Back* al Brain

El Brain es memoria viva: no cierres ninguna tarea técnica ni abras una PR sin sincronizarlo.

1. **Al cerrar tarea o antes de abrir PR:** activa la skill `.agents/skills/brain-cierre-tarea/SKILL.md` y ejecuta:
   ```bash
   python3 scripts/verify_brain.py --diff
   dotnet test Pelican4Ever.Tests/Pelican4Ever.Tests.csproj -c Release
   ```
2. **Regla obligatoria de *write-back*:**
   > Si esta tarea cambia reglas de negocio, cifras normativas, API o arquitectura, propone y aplica la actualización del ADR correspondiente (o redacta un nuevo ADR `0014+` si sustituye una decisión `Aceptada`) o de [`docs/host-api.md`](docs/host-api.md) **en el mismo commit** antes de abrir la PR.
3. **Verificación automática pre-PR:** El hook `.agents/hooks.json` (respaldado por [`scripts/brain_pre_pr_hook.py`](scripts/brain_pre_pr_hook.py)) bloquea `gh pr create` sobre código gobernado si no incluye cambios en `docs/adr/` o `docs/host-api.md` ni declara explícitamente en el cuerpo de la PR `Brain: sin cambios normativos (<motivo>)`.
4. **Informes de laboratorio:** cualquier reporte nuevo generado desde [`live-agent-tests-backlog.md`](live-agent-tests-backlog.md) va en `docs/verifications/`, nunca en la raíz de `docs/`.
