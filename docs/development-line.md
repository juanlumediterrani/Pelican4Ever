# Development line

`pelican/desarrollo` is the active implementation line. It is separate from `pelican/preproduccion`; never point this worktree’s deploy target at the preproduction game. The default ModBuildConfig deployment can mutate `GamePath`, so developers must use the development install or pass `-p:EnableModDeploy=false`.

Preproduction is not promoted by branch name alone. Use `docs/deployment-lines.md` in the preproduction worktree for exact gates. Preserve the live development save/profile and never copy it to server Saves.
