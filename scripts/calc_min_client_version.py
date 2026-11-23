#!/usr/bin/env python3
"""
Calcula y audita ClientVersionPolicy.MinCompatibleClientVersion según ADR-0012.

Uso habitual antes de etiquetar una release:
    python3 scripts/calc_min_client_version.py --since 0.5.3 --target 0.5.4 --commits --check

Si no se indica --since, usa la última etiqueta semver alcanzada desde HEAD (o la anterior a --target).
Si no se indica --target, lee Version desde Pelican4Ever/manifest.json.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path
from typing import Iterable

SEMVER_RE = re.compile(r"^(\d+)\.(\d+)\.(\d+)(?:[-+].*)?$")
MIN_VER_RE = re.compile(
    r"MinCompatibleClientVersion\s*=\s*new(?:\s+SemVer)?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)"
)
PROTOCOL_REV_RE = re.compile(r'ProtocolRevision\s*=\s*"([^"]+)"')

HOST_ONLY_FILES = {
    "Pelican4Ever/PelicanApi.cs",
    "Pelican4Ever/Framework/SystemdWatchdogService.cs",
    "Pelican4Ever/Modules/AdminApiModule.cs",
    "Pelican4Ever/Modules/AnnexReviewModule.cs",
    "Pelican4Ever/Modules/AutoHostModule.cs",
    "Pelican4Ever/Modules/CheckpointModule.cs",
    "Pelican4Ever/Modules/CheckpointSpikeModule.cs",
    "Pelican4Ever/Modules/ClientVersionGuardModule.cs",
    "Pelican4Ever/Modules/FarmTypeModule.cs",
    "Pelican4Ever/Modules/HeadlessEventGuardModule.cs",
    "Pelican4Ever/Modules/HeadlessRenderModule.cs",
    "Pelican4Ever/Modules/PersistenceModule.cs",
    "Pelican4Ever/Modules/ProbationTelemetryModule.cs",
    "Pelican4Ever/Modules/RolloverLockModule.cs",
    "Pelican4Ever.Core/Networking/ClientModRequirements.cs",
    "Pelican4Ever.Core/Networking/ClientVersionPolicy.cs",
    "Pelican4Ever.Core/Networking/StalePeerContext.cs",
}

CLIENT_UI_SAFE_FILES = {
    "Pelican4Ever/Framework/AndroidMenuCompat.cs",
    "Pelican4Ever/Modules/ModToggleModule.cs",
}

NON_RUNTIME_PREFIXES = (
    "docs/",
    "screenshots/",
    "audit/",
    "tools/",
    "scripts/",
    ".github/",
    ".agents/",
    "Pelican4Ever.Tests/",
    "Pelican4Ever/i18n/",
    "agents/",
)

NON_RUNTIME_EXACT = {
    "Pelican4Ever/manifest.json",
    "config.example.json",
    "docker-compose.yml",
    "global.json",
    "Pelican4Ever.sln",
    ".gitignore",
    ".editorconfig",
    ".env.example",
    "local.props.example",
}

HOST_ONLY_PREFIXES = (
    "Pelican4Ever.Core/Admin/",
    "Pelican4Ever.Core/Server/",
)

PROTOCOL_MODEL_PREFIXES = (
    "Pelican4Ever/Models/",
    "Pelican4Ever.Core/Security/",
)

WORLD_DEF_PREFIXES = (
    "Pelican4Ever.Core/World/",
    "Pelican4Ever/assets/",
    "deploy/modpack/",
)


def parse_semver(raw: str) -> tuple[int, int, int]:
    m = SEMVER_RE.match(raw.strip())
    if not m:
        raise ValueError(f"Versión semver inválida: {raw!r}")
    return int(m.group(1)), int(m.group(2)), int(m.group(3))


def fmt_semver(v: tuple[int, int, int]) -> str:
    return f"{v[0]}.{v[1]}.{v[2]}"


def git(repo: Path, *args: str, check: bool = True) -> str:
    res = subprocess.run(
        ["git", "-C", str(repo), *args],
        capture_output=True,
        text=True,
        check=False,
    )
    if check and res.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} falló: {res.stderr.strip()}")
    return res.stdout


def find_repo_root() -> Path:
    here = Path(__file__).resolve()
    for p in (here.parent, *here.parents):
        if (p / "Pelican4Ever" / "manifest.json").is_file():
            return p
    raise RuntimeError("No se encontró la raíz del repositorio Pelican4Ever.")


def read_manifest_version(repo: Path) -> tuple[int, int, int]:
    data = json.loads((repo / "Pelican4Ever" / "manifest.json").read_text(encoding="utf-8"))
    return parse_semver(data["Version"])


def read_policy_constants(repo: Path) -> tuple[tuple[int, int, int], str]:
    text = (repo / "Pelican4Ever.Core" / "Networking" / "ClientVersionPolicy.cs").read_text(encoding="utf-8")
    m_ver = MIN_VER_RE.search(text)
    if not m_ver:
        raise RuntimeError("No se encontró MinCompatibleClientVersion en ClientVersionPolicy.cs")
    m_proto = PROTOCOL_REV_RE.search(text)
    if not m_proto:
        raise RuntimeError("No se encontró ProtocolRevision en ClientVersionPolicy.cs")
    return (int(m_ver.group(1)), int(m_ver.group(2)), int(m_ver.group(3))), m_proto.group(1)


def read_protocol_at_ref(repo: Path, ref: str) -> str | None:
    for rel in (
        "Pelican4Ever.Core/Networking/ClientVersionPolicy.cs",
        "Pelican4Ever/Modules/ClientVersionGuardModule.cs",
    ):
        out = git(repo, "show", f"{ref}:{rel}", check=False)
        if out:
            m = PROTOCOL_REV_RE.search(out)
            if m:
                return m.group(1)
    return None


def default_base_ref(repo: Path, target: tuple[int, int, int]) -> str:
    tags = [t.strip() for t in git(repo, "tag", "--merged", "HEAD").splitlines() if t.strip()]
    semver_tags: list[tuple[tuple[int, int, int], str]] = []
    for t in tags:
        if SEMVER_RE.match(t):
            v = parse_semver(t)
            if v < target:
                semver_tags.append((v, t))
    if not semver_tags:
        raise RuntimeError("No se encontró ninguna etiqueta semver anterior a HEAD; especifica --since.")
    semver_tags.sort()
    return semver_tags[-1][1]


def classify_path(raw_path: str) -> tuple[str, str]:
    p = raw_path.strip().replace("\\", "/")
    while p.startswith("./"):
        p = p[2:]
    p_low = p.lower()

    if not p:
        return "None", "ruta vacía"

    if any(p_low.startswith(pref.lower()) for pref in NON_RUNTIME_PREFIXES):
        return "None", "documentación, tests, i18n, agentes o tooling"
    if p_low.endswith(".md"):
        return "None", "documentación Markdown"
    if p_low.startswith("deploy/") and not p_low.startswith("deploy/modpack/"):
        return "None", "configuración de despliegue del host"
    if p_low in {x.lower() for x in NON_RUNTIME_EXACT}:
        return "None", "metadato/configuración de ejemplo"

    if p_low in {x.lower() for x in HOST_ONLY_FILES} or any(
        p_low.startswith(pref.lower()) for pref in HOST_ONLY_PREFIXES
    ):
        return "None", "solo host (ModuleSide.Host / Core Admin/Server)"

    if p_low in {x.lower() for x in CLIENT_UI_SAFE_FILES}:
        return "None", "solo UI/plataforma cliente sin cambio de reglas ni protocolo"

    if p_low == "pelican4ever.core/networking/admissionlogic.cs" or any(
        p_low.startswith(pref.lower()) for pref in PROTOCOL_MODEL_PREFIXES
    ):
        return "ProtocolOrModelChange", "protocolo, modelos de red o seguridad compartida"

    if p_low == "pelican4ever/framework/mapclonehelper.cs" or any(
        p_low.startswith(pref.lower()) for pref in WORLD_DEF_PREFIXES
    ):
        return "WorldDefinitionChange", "definición de mundo, mapas, biomas o modpack"

    if p_low.startswith("pelican4ever/") or p_low.startswith("pelican4ever.core/"):
        return "ClientRuleChange", "módulo compartido (Both) o regla en Core (fail-safe)"

    return "None", "fuera del runtime del mod"


def evaluate_paths(
    paths: Iterable[str],
    audit_safe: set[str],
) -> tuple[list[tuple[str, str, str]], list[tuple[str, str, str]]]:
    blocking: list[tuple[str, str, str]] = []
    safe: list[tuple[str, str, str]] = []
    seen: set[str] = set()
    for raw in paths:
        p = raw.strip().replace("\\", "/")
        if not p or p.lower() in seen:
            continue
        seen.add(p.lower())
        kind, reason = classify_path(p)
        if kind != "None" and p.lower() in audit_safe:
            safe.append((p, "AuditedSafe", f"exención manual auditada (era {kind})"))
        elif kind != "None":
            blocking.append((p, kind, reason))
        else:
            safe.append((p, kind, reason))
    return blocking, safe


def main() -> int:
    parser = argparse.ArgumentParser(description="Calcula MinCompatibleClientVersion según ADR-0012.")
    parser.add_argument("--since", help="Referencia/etiqueta base (por defecto, última etiqueta semver < target).")
    parser.add_argument("--to", default="HEAD", help="Referencia destino (por defecto HEAD).")
    parser.add_argument("--target", help="Versión objetivo de la release (por defecto manifest.json).")
    parser.add_argument(
        "--audit-safe",
        action="append",
        default=[],
        help="Ruta en módulo Both auditada manualmente como UI-only sin impacto de protocolo/mundo/reglas.",
    )
    parser.add_argument("--commits", action="store_true", help="Desglosar el impacto por cada commit del rango.")
    parser.add_argument(
        "--check",
        action="store_true",
        help="Fallar con código != 0 si MinCompatibleClientVersion en código no cumple el cálculo.",
    )
    args = parser.parse_args()

    repo = find_repo_root()
    target = parse_semver(args.target) if args.target else read_manifest_version(repo)
    code_min, code_proto = read_policy_constants(repo)
    base_ref = args.since or default_base_ref(repo, target)
    base_ver = parse_semver(base_ref) if SEMVER_RE.match(base_ref) else code_min

    changed = [line for line in git(repo, "diff", "--name-only", f"{base_ref}..{args.to}").splitlines() if line.strip()]
    base_proto = read_protocol_at_ref(repo, base_ref)
    proto_changed = base_proto is not None and base_proto != code_proto

    audit_safe = {p.strip().replace("\\", "/").lower() for p in args.audit_safe}
    blocking, safe = evaluate_paths(changed, audit_safe)

    reasons: list[str] = []
    if target[0] != base_ver[0] or target[1] != base_ver[1]:
        reasons.append(f"MajorOrMinorBump: {fmt_semver(base_ver)} -> {fmt_semver(target)}")
    if proto_changed:
        reasons.append(f"ProtocolOrModelChange: ProtocolRevision {base_proto} -> {code_proto}")
    for path, kind, reason in blocking:
        reasons.append(f"{kind}: {path} ({reason})")

    must_bump = len(reasons) > 0
    recommended = target if must_bump else min(base_ver, target)

    print(f"Rango analizado : {base_ref}..{args.to} ({len(changed)} archivos)")
    print(f"Versión objetivo: {fmt_semver(target)} (protocolo {code_proto})")
    print(f"Suelo en código : {fmt_semver(code_min)}")
    print(f"Suelo calculado : {fmt_semver(recommended)} ({'SUBIR' if must_bump else 'MANTENER'})")

    if blocking:
        print("\nArchivos que exigen elevar MinCompatibleClientVersion:")
        for path, kind, reason in blocking:
            print(f"  - [{kind}] {path} — {reason}")
    else:
        print("\nSin archivos bloqueantes detectados.")

    if args.commits:
        print("\nDesglose por commit:")
        commits = git(repo, "log", "--reverse", "--format=%h\t%s", f"{base_ref}..{args.to}").splitlines()
        for line in commits:
            if "\t" not in line:
                continue
            sha, subject = line.split("\t", 1)
            c_files = [
                f for f in git(repo, "diff-tree", "--no-commit-id", "--name-only", "-r", sha).splitlines() if f.strip()
            ]
            c_block, _ = evaluate_paths(c_files, audit_safe)
            tag = "BUMP" if c_block else "SAFE"
            detail = ", ".join(f"{p} [{k}]" for p, k, _ in c_block[:3])
            if len(c_block) > 3:
                detail += f" (+{len(c_block) - 3} más)"
            suffix = f" -> {detail}" if detail else ""
            print(f"  - {sha} [{tag}] {subject}{suffix}")

    if args.check:
        ok = (
            code_min[0] == target[0]
            and code_min[1] == target[1]
            and code_min <= target
            and code_min >= recommended
        )
        if not ok:
            print(
                f"\nERROR: MinCompatibleClientVersion={fmt_semver(code_min)} no cumple el suelo calculado "
                f"{fmt_semver(recommended)} para la release {fmt_semver(target)}.",
                file=sys.stderr,
            )
            return 1
        print("\nOK: ClientVersionPolicy.MinCompatibleClientVersion cumple ADR-0012.")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
