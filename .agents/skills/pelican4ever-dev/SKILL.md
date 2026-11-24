---
name: pelican4ever-dev
description: >-
  Playbook técnico del repositorio Pelican4Ever (mod SMAPI + Core + Tests .NET 6/8,
  protección de WIP en dev/worktrees, paridad 1:1 HostApiContract <-> docs/host-api.md,
  decompilación offline de Stardew/Lidgren, renderizado de mapas TMX/NITVCompat, skins
  de 126 frames / Fashion Sense y laboratorio multicliente Xephyr). Úsalo al desarrollar,
  auditar o probar cualquier módulo C# o asset del repositorio Pelican4Ever.
---

# Pelican4Ever — Playbook de Desarrollo y Laboratorio

Guía operativa local de **Pelican 4Ever** alineada con `AGENTS.md` y el estándar corporativo `EST-001` v1.2 de Niu D'Ocells. Carga los detalles en `references/` y los scripts en `scripts/` solo cuando correspondan (*Progressive Disclosure*).

> Para auditorías Harmony/SMAPI/`GetApi` usa `smapi-harmony-pelican-bridge` (`../MCP Pelican/skills/`); para QA de sprites PNG usa `pixel-art`; para operar la instancia AWS EC2 + **Yuki** o preparar releases oficiales (`DEC-0002` / `DEC-0004`) usa la skill privada `pelican-aws-ops` en `MCP Pelican`.

---

## 1. Reglas de Oro en Desarrollo Local (C# y Git)

1. **Proteger el checkout activo y el runtime del jugador:**
   - Ejecuta siempre `git status -s` antes de compilar o cambiar de rama para no pisar trabajo en curso (WIP).
   - Para compilar o probar otra rama sin perturbar el checkout actual, crea un `git worktree` temporal y elimínalo al acabar.
   - En compilaciones y suites de prueba añade **siempre** `-p:EnableModDeploy=false -p:EnableModZip=false` para no sobrescribir `Mods/Pelican4Ever` ni `config.json` / `device_key.json` en `~/.steam/.../Stardew Valley/Mods/`.
2. **Builds `.NET 6` multiplataforma (SDK `.NET 8.0.427` en `global.json`):**
   - `Pelican4Ever.csproj` compila IL `AnyCPU` sobre `net6.0` con `TreatWarningsAsErrors = true` y `Nullable = enable`.
   - Toda lógica pura de dominio vive en `Pelican4Ever.Core/` (cero dependencias de `StardewValley`, `SMAPI`, `MonoGame` o `HarmonyLib`).
3. **Paridad contractual 1:1:**
   - Cada ruta en `HostApiCatalog.Routes` (`Pelican4Ever.Core/Admin/HostApiContract.cs`) debe figurar en [`docs/host-api.md`](../../../docs/host-api.md) con cabecera `### \`METHOD /api/v1/host/...\`` (`AdminApiLogicTests` falla si diverge).
   - Todo parche Harmony nuevo se registra en `Pelican4Ever/Framework/PatchRegistry.cs` (`Security` | `Integrity` | `Experience`).

---

## 2. Verificación, Decompilación y Herramientas

```bash
# Brain + auditoría estática Harmony + suite unitaria (desde raíz del repo)
python3 scripts/verify_brain.py --check
python3 "../MCP Pelican/skills/smapi-harmony-pelican-bridge/scripts/check_harmony_patches.py" .   # añade --strict sobre archivos C# tocados
dotnet test Pelican4Ever.Tests/Pelican4Ever.Tests.csproj -c Release -p:EnableModDeploy=false -p:EnableModZip=false --nologo
```

- **Decompilar `Stardew Valley.dll`, `Lidgren.Network.dll` o `StardewModdingAPI.dll` offline:**
  ```bash
  python3 "./scripts/p4e_decomp.py" StardewValley.Network.LidgrenClient StardewValley.Menus.FarmhandMenu
  ```
- **Renderizado estático de mapas sin abrir el juego (`tools/MapRenderer`):**
  `cd tools/MapRenderer && dotnet run` (requiere `Content (unpacked)/Maps`).
- **Inyección de comandos SMAPI headless en partida de laboratorio (`EnableTestCommands: true`):**
  Escribe líneas en `Saves/<SaveFolderName>/p4e_test_cmd.txt`; `FarmTestCommandsModule` las drena y borra cada segundo.

---

## 3. Referencias Modulares

1. **Skins 126 cuadros, Fashion Sense, mapas TMX y NITVCompat:** [references/assets-pipeline.md](./references/assets-pipeline.md).
2. **Laboratorio multicliente (`Xephyr` + `LabClient`), clonación de saves dummy y gotchas Lidgren/SMAPI:** [references/live-lab-testing.md](./references/live-lab-testing.md).
