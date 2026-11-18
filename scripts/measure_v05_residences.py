#!/usr/bin/env python3
"""
measure_v05_residences.py
=========================
Automated measurement harness for ADR-0010 (v0.5) / TASK-2409 on the AWS VM via SSM.
Measures checkpoint duration, full day rollover + verified backup duration, save/backup
disk sizes, memory (VmRSS/VmHWM/VmSwap), and CPU usage for FarmSlotCount = 16, 32, and 64
populated residences (each with Coop + 2 chickens, 20 crops, and 2 annexes).
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import subprocess
import sys
import time
from pathlib import Path


def ssm_run(profile: str, region: str, instance_id: str, commands: list[str], timeout_s: int = 180) -> str:
    cmd_id = subprocess.check_output(
        [
            "aws", "ssm", "send-command",
            "--profile", profile,
            "--region", region,
            "--instance-ids", instance_id,
            "--document-name", "AWS-RunShellScript",
            "--parameters", json.dumps({"commands": commands}),
            "--query", "Command.CommandId",
            "--output", "text",
        ],
        text=True,
    ).strip()

    deadline = time.monotonic() + timeout_s
    while time.monotonic() < deadline:
        raw = subprocess.check_output(
            [
                "aws", "ssm", "get-command-invocation",
                "--profile", profile,
                "--region", region,
                "--command-id", cmd_id,
                "--instance-id", instance_id,
                "--output", "json",
            ],
            text=True,
        )
        res = json.loads(raw)
        status = res["Status"]
        if status not in ("Pending", "InProgress", "Delayed"):
            if status != "Success":
                raise RuntimeError(
                    f"SSM command {cmd_id} failed ({status}):\n"
                    f"STDOUT:\n{res.get('StandardOutputContent', '')}\n"
                    f"STDERR:\n{res.get('StandardErrorContent', '')}"
                )
            return res.get("StandardOutputContent", "")
        time.sleep(2)
    raise TimeoutError(f"SSM command {cmd_id} timed out after {timeout_s}s")


def run_tier(profile: str, region: str, instance_id: str, save_name: str, tier: int) -> dict:
    remote_py = f"""
import json, subprocess, time, urllib.request, urllib.error, datetime as dt
from pathlib import Path

SAVE_NAME = {save_name!r}
TIER = {tier}
CFG_PATH = Path("/opt/pelican/config/pelican4ever.config.json")
TOKEN = Path("/opt/pelican/secrets/token").read_text().strip()
SAVE_DIR = Path("/opt/pelican/xdg/StardewValley/Saves") / SAVE_NAME
BACKUPS_DIR = Path("/opt/pelican/xdg/StardewValley/Pelican4Ever_Backups")

def api_get(path):
    req = urllib.request.Request(f"http://127.0.0.1:18085{{path}}", headers={{"Authorization": f"Bearer {{TOKEN}}"}})
    with urllib.request.urlopen(req, timeout=10) as r:
        return json.loads(r.read().decode())

def api_post(path, body=b"{{}}"):
    req = urllib.request.Request(
        f"http://127.0.0.1:18085{{path}}",
        data=body,
        method="POST",
        headers={{"Authorization": f"Bearer {{TOKEN}}", "Content-Type": "application/json"}},
    )
    with urllib.request.urlopen(req, timeout=120) as r:
        return json.loads(r.read().decode())

def run_user_ctl(*args):
    uid = subprocess.check_output(["id", "-u", "pelican"], text=True).strip()
    subprocess.run(
        ["sudo", "-u", "pelican", "env", f"XDG_RUNTIME_DIR=/run/user/{{uid}}", "systemctl", "--user", *args],
        check=True,
    )

# 1. Stop host, reset dummy save cleanly from prod_backup_20261117, configure, and start host
run_user_ctl("stop", "pelican4ever-host")
PROD_BACKUP_SAVE = Path("/opt/pelican/xdg/StardewValley/archivo/prod_backup_20261117/Pelican4Ever_453869176")
subprocess.run(["sudo", "-u", "pelican", "rm", "-rf", str(SAVE_DIR)], check=True)
subprocess.run(["sudo", "-u", "pelican", "cp", "-a", str(PROD_BACKUP_SAVE), str(SAVE_DIR)], check=True)
subprocess.run(["sudo", "-u", "pelican", "mv", str(SAVE_DIR / "Pelican4Ever_453869176"), str(SAVE_DIR / SAVE_NAME)], check=True)
old_main = SAVE_DIR / "Pelican4Ever_453869176_old"
if old_main.exists():
    subprocess.run(["sudo", "-u", "pelican", "mv", str(old_main), str(SAVE_DIR / f"{{SAVE_NAME}}_old")], check=True)
subprocess.run([
    "sudo", "-u", "pelican", "sed", "-i",
    f"s/<uniqueIDForThisGame>453869176<\\/uniqueIDForThisGame>/<uniqueIDForThisGame>{{SAVE_NAME.split('_')[-1]}}<\\/uniqueIDForThisGame>/g",
    str(SAVE_DIR / SAVE_NAME), str(SAVE_DIR / "SaveGameInfo"),
], check=True)

cfg = json.loads(CFG_PATH.read_text())
cfg["AutoHostSaveName"] = SAVE_NAME
cfg["EnableTestCommands"] = True
cfg["FarmSlotCount"] = TIER
cfg["MaxConcurrentPlayers"] = 16
cfg["AdmissionQueueCapacity"] = 16
cfg["RequirePairingWhitelist"] = False
cfg["RequiredClientMods"] = []
CFG_PATH.write_text(json.dumps(cfg, indent=2) + "\\n")

run_user_ctl("start", "pelican4ever-host")

# 2. Wait for health Ok + SaveLoaded
deadline = time.monotonic() + 120
health = None
while time.monotonic() < deadline:
    try:
        health = api_get("/api/v1/host/health")
        if health.get("Status") == "Ok" and health.get("SaveLoaded") is True:
            break
    except Exception:
        pass
    time.sleep(2)
else:
    raise RuntimeError(f"Host did not become healthy for tier {{TIER}}: {{health}}")

time.sleep(3)

# 3. Populate tier residences
before_pop = api_get("/api/v1/host/events?since=0").get("LastEventId", 0)
cmd_file = SAVE_DIR / "p4e_test_cmd.txt"
subprocess.run(["sudo", "-u", "pelican", "tee", str(cmd_file)], input=f"p4e_farm_populate {{TIER}}\\n", text=True, check=True, stdout=subprocess.DEVNULL)

pop_event = None
pop_deadline = time.monotonic() + 180
while time.monotonic() < pop_deadline:
    evts = api_get(f"/api/v1/host/events?since={{before_pop}}").get("Events", [])
    for ev in evts:
        if ev.get("Type") == "host.test.command" and (ev.get("Data") or {{}}).get("command") == "p4e_farm_populate":
            pop_event = ev
            break
    if pop_event:
        break
    time.sleep(2)
if not pop_event:
    raise RuntimeError(f"p4e_farm_populate {{TIER}} did not emit host.test.command")
pop_summary = pop_event.get("Summary") or ""
if f"Pobladas {{TIER}}/{{TIER}}" not in pop_summary:
    raise RuntimeError(f"Unexpected populate summary for tier {{TIER}}: {{pop_summary}}")

time.sleep(3)

# 4. Checkpoint measurement
before_ckpt = api_get("/api/v1/host/events?since=0").get("LastEventId", 0)
ckpt_resp = api_post("/api/v1/host/checkpoint")
ckpt_event = None
ckpt_deadline = time.monotonic() + 180
while time.monotonic() < ckpt_deadline:
    evts = api_get(f"/api/v1/host/events?since={{before_ckpt}}").get("Events", [])
    for ev in evts:
        if ev.get("Type") == "checkpoint.created":
            ckpt_event = ev
            break
    if ckpt_event:
        break
    time.sleep(1)
if not ckpt_event:
    raise RuntimeError(f"Checkpoint did not finish for tier {{TIER}}")

time.sleep(2)

# 5. Full day rollover measurement
before_roll = api_get("/api/v1/host/events?since=0").get("LastEventId", 0)
t0 = time.monotonic()
roll_resp = api_post("/api/rollover")
roll_started = None
roll_completed = None
roll_backup_evt = None
roll_deadline = time.monotonic() + 300
while time.monotonic() < roll_deadline:
    evts = api_get(f"/api/v1/host/events?since={{before_roll}}").get("Events", [])
    for ev in evts:
        if ev.get("Type") == "rollover.started":
            roll_started = ev
        elif ev.get("Type") == "backup.created":
            roll_backup_evt = ev
        elif ev.get("Type") == "rollover.completed":
            roll_completed = ev
    if roll_completed:
        break
    time.sleep(1)
wall_roll_s = round(time.monotonic() - t0, 3)
if not roll_completed:
    raise RuntimeError(f"Rollover did not complete for tier {{TIER}}")

roll_dur_s = wall_roll_s
if roll_started and roll_completed:
    ts0 = dt.datetime.fromisoformat(roll_started["TimestampUtc"].replace("Z", "+00:00"))
    ts1 = dt.datetime.fromisoformat(roll_completed["TimestampUtc"].replace("Z", "+00:00"))
    roll_dur_s = round((ts1 - ts0).total_seconds(), 3)

# 6. Process memory/CPU & disk usage
pid = subprocess.check_output(["pgrep", "-n", "-f", "StardewModdingAPI"], text=True).strip()
status_txt = Path(f"/proc/{{pid}}/status").read_text()
proc_metrics = {{}}
for line in status_txt.splitlines():
    if line.startswith(("VmHWM:", "VmRSS:", "VmSwap:")):
        k, v = line.split(":", 1)
        proc_metrics[k] = v.strip()
ps_out = subprocess.check_output(["ps", "-o", "pcpu=,rss=", "-p", pid], text=True).strip().split()
proc_metrics["pcpu"] = float(ps_out[0]) if ps_out else None
proc_metrics["rss_kb"] = int(ps_out[1]) if len(ps_out) > 1 else None

save_bytes = int(subprocess.check_output(["sudo", "du", "-sb", str(SAVE_DIR)], text=True).split()[0])
backup_name = (roll_completed.get("Data") or {{}}).get("backupFileName") or ""
backup_dir = BACKUPS_DIR / backup_name if backup_name else None
backup_disk_bytes = int(subprocess.check_output(["sudo", "du", "-sb", str(backup_dir)], text=True).split()[0]) if backup_dir and backup_dir.exists() else None

status_api = api_get("/api/status")
status_summary = {{k: v for k, v in status_api.items() if k not in ("Players", "Doors")}}
health_after = api_get("/api/v1/host/health")

result = {{
    "tier": TIER,
    "populate_summary": pop_event.get("Summary"),
    "checkpoint": ckpt_event.get("Data"),
    "checkpoint_reply": ckpt_resp.get("Message"),
    "rollover_duration_s": roll_dur_s,
    "rollover_wall_s": wall_roll_s,
    "rollover_data": roll_completed.get("Data"),
    "daily_backup": roll_backup_evt.get("Data") if roll_backup_evt else None,
    "daily_backup_disk_bytes": backup_disk_bytes,
    "save_dir_bytes": save_bytes,
    "proc": proc_metrics,
    "status": status_summary,
    "health": health_after,
}}
Path(f"/tmp/v05_tier_{{TIER}}.json").write_text(json.dumps(result, indent=2))
print("RESULT_JSON=" + json.dumps(result))
"""
    cmd = f"sudo python3 - <<'PY'\n{remote_py}\nPY"
    stdout = ssm_run(profile, region, instance_id, [cmd], timeout_s=600)
    for line in stdout.splitlines():
        if line.startswith("RESULT_JSON="):
            return json.loads(line[len("RESULT_JSON="):])
    raise RuntimeError(f"Missing RESULT_JSON in SSM output:\n{stdout}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Measure v0.5 residence tiers (16/32/64) on AWS VM via SSM.")
    parser.add_argument("--profile", default="androtuxiano")
    parser.add_argument("--region", default="eu-north-1")
    parser.add_argument("--instance-id", default="i-053e53c1536f541fd")
    parser.add_argument("--save-name", default="Pelican4Ever_999999999")
    parser.add_argument("--tiers", default="16,32,64")
    parser.add_argument("--output", default="")
    args = parser.parse_args()

    tiers = [int(t.strip()) for t in args.tiers.split(",") if t.strip()]
    results = []
    for tier in tiers:
        print(f"=== Running tier FarmSlotCount={tier} ===", flush=True)
        res = run_tier(args.profile, args.region, args.instance_id, args.save_name, tier)
        results.append(res)
        ckpt = res["checkpoint"] or {}
        proc = res["proc"] or {}
        print(
            f"Tier {tier}: ckpt={ckpt.get('totalMs')}ms (longestTick={ckpt.get('longestTickMs')}ms, "
            f"xml={ckpt.get('xmlTotalBytes')}B, backup={ckpt.get('bytes')}B), "
            f"rollover={res['rollover_duration_s']}s, saveDir={res['save_dir_bytes']}B, "
            f"dailyBackup={res['daily_backup_disk_bytes']}B, VmRSS={proc.get('VmRSS')}, "
            f"VmHWM={proc.get('VmHWM')}, CPU={proc.get('pcpu')}%",
            flush=True,
        )

    if args.output:
        Path(args.output).write_text(json.dumps(results, indent=2, ensure_ascii=False) + "\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
