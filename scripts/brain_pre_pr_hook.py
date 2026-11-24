#!/usr/bin/env python3
"""
Hook PreToolUse para run_command (.agents/hooks.json).

Permite sin demora cualquier comando que no invoque `gh pr create`.
Cuando detecta `gh pr create`, verifica:
  1. Que la estructura del Brain pase `verify_brain.py --check`.
  2. Que, si el diff respecto a la rama base toca código runtime gobernado
     (Pelican4Ever/ o Pelican4Ever.Core/), o bien actualiza docs/adr/*.md o
     docs/host-api.md, o bien declara en el cuerpo de la PR `Brain: ...`.
"""
from __future__ import annotations

import json
import re
import shlex
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import verify_brain  # noqa: E402

GH_PR_CREATE_RE = re.compile(r"\bgh\s+pr\s+create\b")
BRAIN_NOTE_RE = re.compile(r"\bBrain\s*:", re.IGNORECASE)


def allow() -> int:
    print(json.dumps({"decision": "allow"}))
    return 0


def deny(reason: str) -> int:
    print(json.dumps({"decision": "deny", "reason": reason}, ensure_ascii=False))
    return 0


def detect_base_ref(repo_root: Path) -> str | None:
    for candidate in ("origin/main", "main", "HEAD~1"):
        res = subprocess.run(
            ["git", "-C", str(repo_root), "rev-parse", "--verify", "--quiet", candidate],
            capture_output=True,
            text=True,
            check=False,
        )
        if res.returncode == 0:
            return candidate
    return None


def extract_pr_body_text(command_line: str, repo_root: Path) -> str:
    fragments = [command_line]
    try:
        tokens = shlex.split(command_line)
    except ValueError:
        return command_line

    for i, tok in enumerate(tokens):
        file_arg: str | None = None
        if tok in ("--body-file", "-F") and i + 1 < len(tokens):
            file_arg = tokens[i + 1]
        elif tok.startswith("--body-file="):
            file_arg = tok.split("=", 1)[1]
        if file_arg:
            p = Path(file_arg)
            if not p.is_absolute():
                p = repo_root / p
            if p.is_file():
                fragments.append(p.read_text(encoding="utf-8", errors="replace"))
    return "\n".join(fragments)


def main() -> int:
    try:
        payload = json.load(sys.stdin)
    except Exception:
        return allow()

    tool_call = payload.get("toolCall") or {}
    args = tool_call.get("args") or {}
    command_line = str(args.get("CommandLine") or "")

    if not GH_PR_CREATE_RE.search(command_line):
        return allow()

    try:
        repo_root = verify_brain.find_repo_root()
    except RuntimeError:
        return allow()

    errors = verify_brain.run_check(repo_root)
    if errors:
        return deny(
            "Integridad del Brain rota antes de crear la PR: "
            + "; ".join(errors[:4])
            + " (ejecuta `python3 scripts/verify_brain.py --check`)."
        )

    base_ref = detect_base_ref(repo_root)
    if not base_ref:
        return allow()

    changed = verify_brain.git_changed_paths_since(repo_root, base_ref)
    analysis = verify_brain.analyze_changed_paths(changed)

    if analysis["writeback_reminder"]:
        body_text = extract_pr_body_text(command_line, repo_root)
        if not BRAIN_NOTE_RE.search(body_text):
            return deny(
                "Write-back del Brain pendiente: esta PR toca código gobernado por ADRs/host-api.md "
                "sin actualizar docs/adr/ ni docs/host-api.md. Si cambia reglas de negocio, API o "
                "arquitectura, actualiza el ADR o docs/host-api.md en el mismo commit; si es compatible "
                "sin cambios normativos, decláralo en el cuerpo de la PR con "
                "'Brain: sin cambios normativos (<motivo>)'."
            )

    return allow()


if __name__ == "__main__":
    raise SystemExit(main())
