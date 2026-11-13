> **Documento archivado (2026-11-13).** No lo actualices: Duplicado de cinco líneas de `deployment-lines.md`. Sin valor propio.
>
> Se conserva como registro histórico.

# Development line

`pelican/desarrollo` is the active implementation line. It is separate from `main`, which is the preproduction line; never point this worktree’s deploy target at the preproduction game. The default ModBuildConfig deployment can mutate `GamePath`, so developers must use the development install or pass `-p:EnableModDeploy=false`.

The root global.json pins .NET SDK 8.0.427 exactly; system SDK 8.0.132/Roslyn 4.8 fails CS9057 with the Roslyn 4.9 analyzer in SMAPI ModBuildConfig. SDK 8.0.427 builds/tests with analyzers enabled. Preproduction is not promoted by branch name alone. Use `docs/deployment-lines.md` in the preproduction worktree for exact gates. Preserve the live development save/profile and never copy it to server Saves.
