#!/usr/bin/env python3
"""
run_v06_concurrency_lab.py
==========================
Reproducible multi-client load and telemetry harness for Pelican4Ever v0.6
(TASK-2601 / TASK-2602 / TASK-2604 / TASK-2609 / TASK-2610 / TASK-2611).

Launches isolated SMAPI client instances under Xephyr (with deterministic lab
bot identities, headless client rendering via P4E_CLIENT_LAB_HEADLESS=1 for
background bots and 0 for the visible observer client), polls
GET /api/v1/host/concurrency in 10s windows, runs reproducible scenarios
(residences, gathering, transitions), and exports comparable JSON/Markdown
reports including instrumentation overhead checks.
"""
from __future__ import annotations

import argparse
import json
import os
import shutil
import signal
import statistics
import subprocess
import sys
import time
import urllib.error
import urllib.request
from dataclasses import dataclass
from pathlib import Path


DEFAULT_GAME_DIR = Path("/home/mtx/.steam/debian-installation/steamapps/common/Stardew Valley")


@dataclass
class ClientProcess:
    index: int
    label: str
    visible: bool
    root_dir: Path
    proc: subprocess.Popen
    log_path: Path


def api_get(base_url: str, token: str, path: str, timeout: float = 15.0) -> dict:
    req = urllib.request.Request(
        f"{base_url.rstrip('/')}{path}",
        headers={"Authorization": f"Bearer {token}"},
    )
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


def api_post(base_url: str, token: str, path: str, body: dict | None = None, timeout: float = 120.0) -> dict:
    payload = json.dumps(body or {}).encode("utf-8")
    req = urllib.request.Request(
        f"{base_url.rstrip('/')}{path}",
        data=payload,
        method="POST",
        headers={
            "Authorization": f"Bearer {token}",
            "Content-Type": "application/json",
        },
    )
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


def ensure_xephyr(display: str, log_dir: Path) -> subprocess.Popen | None:
    check = subprocess.run(
        ["xdpyinfo", "-display", display],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    if check.returncode == 0:
        return None

    log_dir.mkdir(parents=True, exist_ok=True)
    xephyr_log = (log_dir / "xephyr.log").open("w", encoding="utf-8")
    proc = subprocess.Popen(
        ["Xephyr", display, "-screen", "1280x720", "-nolisten", "tcp", "-ac"],
        stdout=xephyr_log,
        stderr=subprocess.STDOUT,
        stdin=subprocess.DEVNULL,
    )
    for _ in range(30):
        probe = subprocess.run(
            ["xdpyinfo", "-display", display],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        if probe.returncode == 0:
            return proc
        time.sleep(0.25)
    raise RuntimeError(f"Xephyr failed to start on {display}")


def prepare_client_dir(
    client_root: Path,
    game_mods_dir: Path,
    p4e_mod_dir: Path,
    lab_client_dll_dir: Path | None,
    host_ip: str,
    scenario: str,
    bot_index: int,
    total_bots: int,
    cadence_ms: int,
) -> None:
    xdg = client_root / "xdg"
    cache = client_root / "cache"
    mods = client_root / "mods"
    xdg.mkdir(parents=True, exist_ok=True)
    cache.mkdir(parents=True, exist_ok=True)
    if mods.exists():
        shutil.rmtree(mods)
    mods.mkdir(parents=True, exist_ok=True)

    # Disabling SMAPI console input prevents 100% CPU busy-loop on EOF.
    (mods / "SMAPI-config.json").write_text(json.dumps({"ListenForConsoleInput": False}), encoding="utf-8")

    # Link required base mods except Pelican4Ever (which gets an isolated config copy).
    if game_mods_dir.exists():
        for entry in game_mods_dir.iterdir():
            if entry.name in ("Pelican4Ever", "SMAPI-config.json"):
                continue
            target = mods / entry.name
            if not target.exists():
                target.symlink_to(entry)

    p4e_dest = mods / "Pelican4Ever"
    p4e_dest.mkdir(parents=True, exist_ok=True)
    for item in p4e_mod_dir.iterdir():
        if item.name in ("config.json", "device_key.json"):
            continue
        dst = p4e_dest / item.name
        if not dst.exists():
            dst.symlink_to(item)

    p4e_cfg = {
        "AutoHostSaveName": "",
        "AdminApiPort": 0,
        "HeadlessRendering": False,
        "ClientLabHeadlessRendering": True,
        "Modules": {"AdminApi": False},
    }
    (p4e_dest / "config.json").write_text(json.dumps(p4e_cfg, indent=2), encoding="utf-8")

    if lab_client_dll_dir and lab_client_dll_dir.exists():
        lab_dst = mods / lab_client_dll_dir.name
        if not lab_dst.exists():
            lab_dst.symlink_to(lab_client_dll_dir)

    script_cfg = {
        "HostIp": host_ip,
        "BotIndex": bot_index,
        "TotalBots": total_bots,
        "Scenario": scenario,
        "ActionCadenceMs": cadence_ms,
        "IdentitySeed": f"v06-lab-bot-{bot_index:02d}",
    }
    (client_root / "lab_bot_config.json").write_text(json.dumps(script_cfg, indent=2), encoding="utf-8")


def launch_clients(
    count: int,
    game_dir: Path,
    p4e_mod_dir: Path,
    lab_client_dir: Path | None,
    work_dir: Path,
    display: str,
    host_ip: str,
    scenario: str,
    cadence_ms: int,
    stagger_s: float,
    visible_observer: bool,
) -> list[ClientProcess]:
    smapi_bin = game_dir / "StardewModdingAPI"
    if not smapi_bin.exists():
        raise FileNotFoundError(f"SMAPI binary not found at {smapi_bin}")

    game_mods_dir = game_dir / "Mods"
    launched: list[ClientProcess] = []
    for idx in range(1, count + 1):
        label = f"c{idx:02d}"
        c_root = work_dir / label
        is_visible = visible_observer and idx == 1
        prepare_client_dir(
            c_root,
            game_mods_dir,
            p4e_mod_dir,
            lab_client_dir,
            host_ip,
            scenario,
            idx,
            count,
            cadence_ms,
        )

        env = os.environ.copy()
        env["XDG_CONFIG_HOME"] = str(c_root / "xdg")
        env["XDG_DATA_HOME"] = str(c_root / "xdg")
        env["XDG_CACHE_HOME"] = str(c_root / "cache")
        env["DISPLAY"] = display
        env["LP_NUM_THREADS"] = "1"
        env["SDL_AUDIODRIVER"] = "dummy"
        env["ALSOFT_DRIVERS"] = "null"
        env["P4E_CLIENT_LAB_HEADLESS"] = "0" if is_visible else "1"
        env["P4E_LAB_BOT_CONFIG"] = str(c_root / "lab_bot_config.json")

        log_path = c_root / "client.log"
        log_file = log_path.open("w", encoding="utf-8")
        proc = subprocess.Popen(
            [str(smapi_bin), "--mods-path", str(c_root / "mods")],
            env=env,
            cwd=str(game_dir),
            stdout=log_file,
            stderr=subprocess.STDOUT,
            stdin=subprocess.DEVNULL,
        )
        launched.append(ClientProcess(idx, label, is_visible, c_root, proc, log_path))
        if stagger_s > 0:
            time.sleep(stagger_s)

    return launched


def collect_windows(
    base_url: str,
    token: str,
    duration_s: int,
    poll_interval_s: float = 10.0,
) -> list[dict]:
    samples: list[dict] = []
    deadline = time.monotonic() + duration_s
    while time.monotonic() < deadline:
        try:
            snap = api_get(base_url, token, "/api/v1/host/concurrency")
            samples.append(snap)
        except Exception as exc:
            print(f"[warn] concurrency poll failed: {exc}", file=sys.stderr)
        time.sleep(poll_interval_s)
    return samples


def summarize_samples(label: str, samples: list[dict]) -> dict:
    if not samples:
        return {"label": label, "windows": 0}

    windows = [s.get("LatestWindow") or {} for s in samples]
    p50_ticks = [w.get("TickMedianMs", 0.0) for w in windows if w.get("TickCount", 0) > 0]
    p95_ticks = [w.get("TickP95Ms", 0.0) for w in windows if w.get("TickCount", 0) > 0]
    max_ticks = [w.get("TickMaxMs", 0.0) for w in windows if w.get("TickCount", 0) > 0]
    overheads = [w.get("InstrumentationOverheadPercent", 0.0) for w in windows if w.get("TickCount", 0) > 0]
    up_bps = [w.get("TransportBytesUpPerSecond", 0.0) for w in windows]
    payload_bps = [w.get("PayloadBytesPerSecond", 0.0) for w in windows]
    t0_bytes = [w.get("Type0FarmerDeltaBytes", 0) for w in windows]
    t6_bytes = [w.get("Type6LocationDeltaBytes", 0) for w in windows]
    clients = [s.get("ConnectedClientsCount", 0) for s in samples]

    return {
        "label": label,
        "windows": len(windows),
        "max_connected_clients": max(clients) if clients else 0,
        "tick_p50_median_ms": round(statistics.median(p50_ticks), 3) if p50_ticks else 0.0,
        "tick_p95_median_ms": round(statistics.median(p95_ticks), 3) if p95_ticks else 0.0,
        "tick_max_ms": round(max(max_ticks), 3) if max_ticks else 0.0,
        "overhead_median_pct": round(statistics.median(overheads), 3) if overheads else 0.0,
        "payload_bps_mean": round(statistics.mean(payload_bps), 1) if payload_bps else 0.0,
        "transport_up_bps_mean": round(statistics.mean(up_bps), 1) if up_bps else 0.0,
        "type0_bytes_mean_per_window": round(statistics.mean(t0_bytes), 1) if t0_bytes else 0.0,
        "type6_bytes_mean_per_window": round(statistics.mean(t6_bytes), 1) if t6_bytes else 0.0,
    }


def stop_clients(clients: list[ClientProcess]) -> None:
    for c in clients:
        if c.proc.poll() is None:
            c.proc.send_signal(signal.SIGTERM)
    deadline = time.monotonic() + 10.0
    for c in clients:
        remaining = max(0.1, deadline - time.monotonic())
        try:
            c.proc.wait(timeout=remaining)
        except subprocess.TimeoutExpired:
            c.proc.kill()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Pelican4Ever v0.6 reproducible concurrency lab harness")
    parser.add_argument("--api-url", default="http://127.0.0.1:8085", help="Host AdminApi base URL")
    parser.add_argument("--token", default=os.environ.get("P4E_ADMIN_TOKEN", ""), help="AdminApi Bearer token")
    parser.add_argument("--host-ip", default="127.0.0.1", help="Target Stardew server IP")
    parser.add_argument("--clients", type=int, default=16, help="Number of SMAPI client processes to spawn")
    parser.add_argument("--scenario", choices=["residences", "gathering", "transitions"], default="residences")
    parser.add_argument("--duration-s", type=int, default=1800, help="Observation duration in seconds (default 30 min)")
    parser.add_argument("--cadence-ms", type=int, default=250, help="Bot action cadence in milliseconds")
    parser.add_argument("--stagger-s", type=float, default=1.5, help="Delay between launching clients")
    parser.add_argument("--display", default=":98", help="X11 display for Xephyr")
    parser.add_argument("--game-dir", type=Path, default=DEFAULT_GAME_DIR)
    parser.add_argument("--mod-dir", type=Path, default=DEFAULT_GAME_DIR / "Mods" / "Pelican4Ever")
    parser.add_argument("--lab-client-dir", type=Path, default=None, help="Optional bot mod folder to symlink")
    parser.add_argument("--work-dir", type=Path, default=Path("/tmp/p4e-v06-lab"))
    parser.add_argument("--no-visible", action="store_true", help="Run all clients headless without visible observer")
    parser.add_argument("--compare-overhead", action="store_true", help="Measure with instrumentation off vs on")
    parser.add_argument("--dry-run", action="store_true", help="Prepare client directories without launching SMAPI")
    parser.add_argument("--output", type=Path, default=None, help="Optional JSON report path")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    args.work_dir.mkdir(parents=True, exist_ok=True)

    if args.dry_run:
        for idx in range(1, args.clients + 1):
            c_root = args.work_dir / f"c{idx:02d}"
            prepare_client_dir(
                c_root,
                args.game_dir / "Mods",
                args.mod_dir,
                args.lab_client_dir,
                args.host_ip,
                args.scenario,
                idx,
                args.clients,
                args.cadence_ms,
            )
        print(f"Prepared {args.clients} isolated client directories in {args.work_dir}")
        return 0

    if not args.token:
        print("Error: --token or P4E_ADMIN_TOKEN is required when running against a live host.", file=sys.stderr)
        return 2

    xephyr_proc = ensure_xephyr(args.display, args.work_dir)
    clients: list[ClientProcess] = []
    try:
        clients = launch_clients(
            count=args.clients,
            game_dir=args.game_dir,
            p4e_mod_dir=args.mod_dir,
            lab_client_dir=args.lab_client_dir,
            work_dir=args.work_dir,
            display=args.display,
            host_ip=args.host_ip,
            scenario=args.scenario,
            cadence_ms=args.cadence_ms,
            stagger_s=args.stagger_s,
            visible_observer=not args.no_visible,
        )

        # Wait up to 90s for clients to join.
        join_deadline = time.monotonic() + 90.0
        while time.monotonic() < join_deadline:
            snap = api_get(args.api_url, args.token, "/api/v1/host/concurrency")
            if snap.get("ConnectedClientsCount", 0) >= args.clients:
                break
            time.sleep(2.0)

        report: dict = {
            "scenario": args.scenario,
            "requested_clients": args.clients,
            "cadence_ms": args.cadence_ms,
            "runs": [],
        }

        if args.compare_overhead:
            api_get(args.api_url, args.token, "/api/v1/host/concurrency?instrumentation=off")
            off_samples = collect_windows(args.api_url, args.token, min(60, args.duration_s))
            report["runs"].append({"summary": summarize_samples("instrumentation_off", off_samples), "samples": off_samples})

        api_get(args.api_url, args.token, "/api/v1/host/concurrency?instrumentation=on")
        api_get(args.api_url, args.token, "/api/v1/host/concurrency?instrumentation=reset")
        on_samples = collect_windows(args.api_url, args.token, args.duration_s)
        report["runs"].append({"summary": summarize_samples("instrumentation_on", on_samples), "samples": on_samples})

        if args.scenario == "transitions":
            ckpt = api_post(args.api_url, args.token, "/api/v1/host/checkpoint")
            report["checkpoint"] = ckpt

        for run in report["runs"]:
            print(json.dumps(run["summary"], ensure_ascii=False))

        if args.output:
            args.output.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    finally:
        stop_clients(clients)
        if xephyr_proc is not None and xephyr_proc.poll() is None:
            xephyr_proc.terminate()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
