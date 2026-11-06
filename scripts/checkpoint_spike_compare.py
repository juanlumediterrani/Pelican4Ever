#!/usr/bin/env python3
"""Compara el informe de `p4e_checkpoint_spike` con el XML que se guardó de verdad (TASK-1601, gap G4).

Para cada farmhand conectado indica si el XML guardado contiene su estado vivo, la copia
retrasada de `farmhandData` o ninguna de las dos.

Uso:
    python3 -I scripts/checkpoint_spike_compare.py <carpeta_del_spike | report.json>

La carpeta del spike está en `~/.config/StardewValley/Pelican4Ever_Backups/spike/<partida>_<fecha>/`.
"""

import json
import re
import sys
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path

PREFIX = re.compile(r"^\([A-Za-z]+\)")


def items_counter(items: str) -> Counter:
    """Convierte "(O)388x5,(T)Axex1" en un multiconjunto de (id sin prefijo, cantidad)."""
    counter: Counter = Counter()
    for entry in filter(None, items.split(",")):
        item_id, _, stack = entry.rpartition("x")
        counter[(PREFIX.sub("", item_id), int(stack))] += 1
    return counter


def children(parent: ET.Element, tag: str) -> list:
    node = parent.find(tag)
    return list(node) if node is not None else []


def xml_state(farmer: ET.Element) -> dict:
    nil = "{http://www.w3.org/2001/XMLSchema-instance}nil"
    items: Counter = Counter()
    for item in children(farmer, "items"):
        if item.get(nil) == "true":
            continue
        item_id = item.findtext("itemId") or item.findtext("name") or "?"
        items[(item_id, int(item.findtext("stack") or 1))] += 1
    friendship = sum(
        int(entry.findtext("value/Friendship/Points") or 0)
        for entry in children(farmer, "friendshipData")
    )
    return {
        "items": items,
        "stamina": float(farmer.findtext("stamina") or 0),
        "experience": sum(int(x.text or 0) for x in children(farmer, "experiencePoints")),
        "mail": len(children(farmer, "mailReceived")),
        "friendship": friendship,
        "tile": (
            int(float(farmer.findtext("Position/X") or 0) // 64),
            int(float(farmer.findtext("Position/Y") or 0) // 64),
        ),
    }


def probe_state(state: dict) -> dict:
    return {
        "items": items_counter(state["Items"]),
        "stamina": float(state["Stamina"]),
        "experience": state["TotalExperience"],
        "mail": state["MailReceived"],
        "friendship": state["FriendshipPoints"],
        "tile": (state["TileX"], state["TileY"]),
    }


def differences(saved: dict, other: dict) -> list:
    diff = []
    if saved["items"] != other["items"]:
        diff.append("inventario")
    if abs(saved["stamina"] - other["stamina"]) > 0.5:
        diff.append(f"energía {saved['stamina']:.0f} vs {other['stamina']:.0f}")
    for key, label in (("experience", "experiencia"), ("mail", "correo"), ("friendship", "amistad")):
        if saved[key] != other[key]:
            diff.append(f"{label} {saved[key]} vs {other[key]}")
    return diff


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__)
        return 2

    target = Path(sys.argv[1]).expanduser()
    report_path = target / "report.json" if target.is_dir() else target
    report = json.loads(report_path.read_text(encoding="utf-8"))
    save_dir = report_path.parent / "save"
    save_files = [p for p in save_dir.iterdir() if p.is_file() and "_" in p.name and not p.name.endswith("_old") and not p.name.startswith("SaveGameInfo")]
    if len(save_files) != 1:
        print(f"No encuentro un único archivo de partida en {save_dir}: {[p.name for p in save_files]}")
        return 1

    root = ET.parse(save_files[0]).getroot()
    farmhands = {f.findtext("UniqueMultiplayerID"): f for f in children(root, "farmhands")}

    print(f"Spike del día {report['GameDay']} ({report['GameDate']}) a las {report['GameTime']}, clonar={report['CloneRequested']}")
    print(f"Guardado: {report['TotalMs']:.0f} ms, tick más largo {report['LongestTickMs']:.0f} ms, "
          f"{report['SaveFileBytes'] / 1e6:.1f} MB. Saving={report['SavingEventFired']} Saved={report['SavedEventFired']}")

    probes = report.get("FarmhandsAfterClone") or report["FarmhandsBefore"]
    if not probes:
        print("No había farmhands conectados: G4 no se puede evaluar con este spike.")
        return 0

    for probe in probes:
        farmer = farmhands.get(str(probe["Id"]))
        if farmer is None:
            print(f"- {probe['Name']}: NO está en el XML guardado.")
            continue
        saved = xml_state(farmer)
        live_diff = differences(saved, probe_state(probe["Live"]))
        stored_diff = differences(saved, probe_state(probe["Stored"])) if probe.get("Stored") else None
        if not live_diff:
            verdict = "el XML tiene el estado VIVO"
        elif stored_diff == []:
            verdict = "el XML tiene la copia RETRASADA de farmhandData"
        else:
            verdict = "el XML no coincide con ninguno"
        print(f"- {probe['Name']}: {verdict}.")
        if live_diff:
            print(f"    frente al vivo: {', '.join(live_diff)}")
        if stored_diff:
            print(f"    frente a la copia: {', '.join(stored_diff)}")
        print(f"    casilla guardada {saved['tile']}, casilla viva {(probe['Live']['TileX'], probe['Live']['TileY'])} en {probe['Live']['Location']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
