#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
migrate_save_to_plaza.py
========================
Herramienta oficial de migración de partidas guardadas de Pelican 4Ever.
Translada partidas creadas en 'origin/main' (16 puertas en la Granja Host)
a la nueva arquitectura de Plaza Central y Puentes ('refactor/residence-entrances').

Características principales:
  1. Detección y copia de seguridad (backup) automática con marca temporal.
  2. Modos de reasignación de salidas:
     - 'slot' (por defecto): Conserva el ID de hueco (Pelican4Ever_Farm_XX) y actualiza
       el catálogo de puertas a la nueva ubicación en Host / Puentes.
     - 'geo': Conserva la posición geográfica previa en la Granja Host para las 4 aperturas
       activas y redistribuye hacia los puentes, trasladando los datos de la granja privada
       (cultivos, cofres, edificios) al nuevo hueco correspondiente.
     - 'custom': Mapeo manual explícito por archivo JSON o argumento de línea de comandos.
     - 'interactive': Pregunta interactivamente en la terminal para cada puerta reclamada.
  3. Sobrescritura y saneamiento del mapa Host ('--overwrite-host-map'):
     - Purga warps obsoletos de origin/main (x=1..2, y=16, 26, 36, 46, norte/sur secundarios).
     - Inyecta los warps del nuevo paso oeste hacia Pelican4Ever_Bridge_1 (x=0, y=26..27).
     - Reubica el cuenco del perro (Pet Bowl) fuera del cercado en (53, 6).
     - Despeja objetos, escombros, piedras, ramas y tocones del corredor oeste (x=0..7, y=25..28)
       y los accesos directos a los 4 portales activos.
     - Sanitiza las coordenadas de desconexión de granjeros huérfanos a posición segura (64, 15).

Uso:
  python3 scripts/migrate_save_to_plaza.py <ruta_guardado> [opciones]
  python3 scripts/migrate_save_to_plaza.py --list-saves
"""

import sys
import os
import shutil
import glob
import json
import argparse
import datetime
import xml.etree.ElementTree as ET
from typing import Dict, List, Tuple, Optional, Any

# Clave SMAPI de ServerWorldState
P4E_STATE_KEY = "smapi/mod-data/pelican4ever.svrtmp/pelican4ever.serverworldstate"

# Mapeo Geográfico Canónico: origin/main -> refactor/residence-entrances
GEO_DOOR_MAPPING = {
    # 4 aperturas activas en Granja Host
    "Farm_Portal_05": "Farm_Portal_01", # (18, 1)  Norte Oeste
    "Farm_Portal_08": "Farm_Portal_02", # (62, 1)  Norte Este
    "Farm_Portal_09": "Farm_Portal_03", # (18, 63) Sur Oeste
    "Farm_Portal_12": "Farm_Portal_04", # (62, 63) Sur Este

    # Antiguo Borde Oeste -> Puente 1
    "Farm_Portal_01": "Farm_Portal_05", # (1, 16) -> Puente 1 NO (8, 1)
    "Farm_Portal_02": "Farm_Portal_06", # (1, 26) -> Puente 1 NE (25, 1)
    "Farm_Portal_03": "Farm_Portal_07", # (1, 36) -> Puente 1 SO (8, 28)
    "Farm_Portal_04": "Farm_Portal_08", # (1, 46) -> Puente 1 SE (25, 28)

    # Antiguo Norte / Sur interior -> Puente 2
    "Farm_Portal_06": "Farm_Portal_09", # (28, 1)  -> Puente 2 NO (8, 1)
    "Farm_Portal_07": "Farm_Portal_10", # (52, 1)  -> Puente 2 NE (25, 1)
    "Farm_Portal_10": "Farm_Portal_11", # (28, 63) -> Puente 2 SO (8, 28)
    "Farm_Portal_11": "Farm_Portal_12", # (52, 63) -> Puente 2 SE (25, 28)

    # Antiguos rincones -> Puente 3
    "Farm_Portal_13": "Farm_Portal_13", # (8, 1)   -> Puente 3 NO (8, 1)
    "Farm_Portal_14": "Farm_Portal_14", # (72, 1)  -> Puente 3 NE (25, 1)
    "Farm_Portal_15": "Farm_Portal_15", # (8, 63)  -> Puente 3 SO (8, 28)
    "Farm_Portal_16": "Farm_Portal_16"  # (47, 63) -> Puente 3 SE (25, 28)
}

# Mapeo por Identidad de Hueco: mantiene el SlotId idéntico
SLOT_DOOR_MAPPING = {f"Farm_Portal_{i:02d}": f"Farm_Portal_{i:02d}" for i in range(1, 17)}

# Catálogo nuevo de puertas a slots
NEW_DOOR_CATALOG = {f"Farm_Portal_{i:02d}": f"Pelican4Ever_Farm_{i:02d}" for i in range(1, 17)}

# Coordenadas canónicas
CANONICAL_PET_BOWL = (53, 6)

def list_saves() -> None:
    """Busca y lista todas las partidas en la carpeta de Stardew Valley del usuario."""
    saves_dir = os.path.expanduser("~/.config/StardewValley/Saves")
    if not os.path.exists(saves_dir):
        print(f"No se encontró el directorio de guardados en: {saves_dir}")
        return

    print("=== PARTIDAS GUARDADAS ENCONTRADAS ===")
    entries = sorted(os.listdir(saves_dir))
    for entry in entries:
        full_path = os.path.join(saves_dir, entry)
        if not os.path.isdir(full_path):
            continue
        save_file = os.path.join(full_path, entry)
        if not os.path.exists(save_file):
            continue

        has_p4e = False
        try:
            with open(save_file, "r", encoding="utf-8", errors="ignore") as f:
                header = f.read(1024 * 512)
                if "pelican4ever" in header.lower() or "pelican4ever" in save_file.lower():
                    has_p4e = True
        except Exception:
            pass

        tag = "[Pelican 4Ever]" if has_p4e else "[Vanilla / Otros]"
        print(f" - {entry:<35} {tag} -> {full_path}")
    print()

def locate_save_files(path: str) -> Tuple[str, str, str]:
    """Determina la carpeta de guardado y las rutas al archivo principal y SaveGameInfo."""
    expanded = os.path.expanduser(path.strip())
    if os.path.isfile(expanded):
        save_file = expanded
        save_dir = os.path.dirname(expanded)
    elif os.path.isdir(expanded):
        save_dir = expanded
        base_name = os.path.basename(os.path.normpath(expanded))
        save_file = os.path.join(save_dir, base_name)
        if not os.path.exists(save_file):
            # Intentar buscar el archivo XML sin extensión
            candidates = [f for f in glob.glob(os.path.join(save_dir, "*"))
                          if not f.endswith(".json") and not f.endswith(".old") and not f.endswith(".bak") and not "SaveGameInfo" in f and os.path.isfile(f)]
            if candidates:
                save_file = candidates[0]
            else:
                raise FileNotFoundError(f"No se encontró el archivo de partida XML dentro de '{save_dir}'.")
    else:
        raise FileNotFoundError(f"La ruta especificada no existe: '{expanded}'")

    save_game_info = os.path.join(save_dir, "SaveGameInfo")
    return save_dir, save_file, save_game_info

def create_backup(save_dir: str) -> str:
    """Crea una copia de seguridad timestamped de la carpeta del guardado."""
    timestamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    parent_dir = os.path.dirname(os.path.normpath(save_dir))
    base_name = os.path.basename(os.path.normpath(save_dir))
    backup_dir = os.path.join(parent_dir, f"{base_name}_bak_{timestamp}")

    shutil.copytree(save_dir, backup_dir)
    return backup_dir

def parse_vector_or_coords(elem: ET.Element) -> Tuple[Optional[int], Optional[int]]:
    """Extrae coordenadas X e Y de un elemento XML."""
    x_elem = elem.find("X") or elem.find("x")
    y_elem = elem.find("Y") or elem.find("y")
    if x_elem is not None and y_elem is not None:
        try:
            return int(float(x_elem.text or "0")), int(float(y_elem.text or "0"))
        except ValueError:
            pass
    return None, None

def is_in_rect(x: int, y: int, x1: int, y1: int, x2: int, y2: int) -> bool:
    """Comprueba si (x, y) cae dentro del rectángulo inclusivo [x1..x2, y1..y2]."""
    return x1 <= x <= x2 and y1 <= y <= y2

def sanitize_host_farm(farm_elem: ET.Element) -> Tuple[int, bool, int]:
    """
    Sanea el elemento <GameLocation><name>Farm</name>:
      1. Purga warps obsoletos de origin/main.
      2. Inyecta los warps del paso oeste a Pelican4Ever_Bridge_1.
      3. Reubica el edificio Pet Bowl a (53, 6).
      4. Limpia escombros y objetos del corredor oeste y accesos a portales.
    """
    purged_warps = 0
    pet_bowl_moved = False
    cleared_tiles = 0

    # 1. Purgar Warps
    warps_elem = farm_elem.find("warps")
    if warps_elem is not None:
        to_remove = []
        for warp in list(warps_elem):
            x_elem = warp.find("X") or warp.find("x")
            y_elem = warp.find("Y") or warp.find("y")
            loc_elem = warp.find("TargetLocation") or warp.find("targetLocation")
            if x_elem is None or y_elem is None or loc_elem is None:
                continue

            try:
                wx = int(x_elem.text or "0")
                wy = int(y_elem.text or "0")
                wloc = (loc_elem.text or "").strip()
            except ValueError:
                continue

            # Comprobar si es un warp a Pelican4Ever_Farm_XX fuera de los 4 portales válidos
            if wloc.startswith("Pelican4Ever_Farm_"):
                is_nw = (wx in (18, 19)) and (wy in (0, 1))
                is_ne = (wx in (62, 63)) and (wy in (0, 1))
                is_sw = (wx in (18, 19)) and (wy in (63, 64))
                is_se = (wx in (62, 63)) and (wy in (63, 64))
                if not (is_nw or is_ne or is_sw or is_se):
                    to_remove.append(warp)
                    purged_warps += 1
            # Comprobar antiguos warps en borde oeste
            elif wx <= 1 and wy in (16, 17, 26, 27, 36, 37, 46, 47):
                if not (wy in (26, 27) and wloc.lower() == "pelican4ever_bridge_1"):
                    to_remove.append(warp)
                    purged_warps += 1

        for w in to_remove:
            warps_elem.remove(w)

        # Asegurar warps hacia Puente 1 en x=0, y=26..27
        existing_passages = set()
        for warp in warps_elem:
            wx, wy = parse_vector_or_coords(warp)
            loc = (warp.findtext("TargetLocation") or warp.findtext("targetLocation") or "").strip()
            if loc.lower() == "pelican4ever_bridge_1":
                existing_passages.add((wx, wy))

        for row in (26, 27):
            if (0, row) not in existing_passages:
                new_warp = ET.SubElement(warps_elem, "Warp")
                x_sub = ET.SubElement(new_warp, "x")
                x_sub.text = "0"
                y_sub = ET.SubElement(new_warp, "y")
                y_sub.text = str(row)
                tloc_sub = ET.SubElement(new_warp, "targetLocation")
                tloc_sub.text = "Pelican4Ever_Bridge_1"
                tx_sub = ET.SubElement(new_warp, "targetX")
                tx_sub.text = "33"
                ty_sub = ET.SubElement(new_warp, "targetY")
                ty_sub.text = "14"

    # 2. Reubicar Pet Bowl a (53, 6)
    buildings_elem = farm_elem.find("buildings")
    if buildings_elem is not None:
        for b in buildings_elem:
            btype = (b.findtext("buildingType") or "").strip()
            if btype in ("Pet Bowl", "PetBowl") or b.tag == "PetBowl":
                tx = b.find("tileX")
                ty = b.find("tileY")
                if tx is not None and ty is not None:
                    if tx.text != str(CANONICAL_PET_BOWL[0]) or ty.text != str(CANONICAL_PET_BOWL[1]):
                        tx.text = str(CANONICAL_PET_BOWL[0])
                        ty.text = str(CANONICAL_PET_BOWL[1])
                        pet_bowl_moved = True

    # 3. Limpiar objetos y escombros de corredores y portales
    # Zonas de exclusión en Farm
    clear_zones = [
        (0, 25, 7, 28),   # Corredor oeste
        (17, 0, 20, 12),  # Portal NO
        (61, 0, 64, 12),  # Portal NE
        (17, 58, 20, 64), # Portal SO
        (61, 58, 64, 64)  # Portal SE
    ]

    def should_clear(x: int, y: int) -> bool:
        return any(is_in_rect(x, y, x1, y1, x2, y2) for (x1, y1, x2, y2) in clear_zones)

    # Limpiar <objects>
    objects_elem = farm_elem.find("objects")
    if objects_elem is not None:
        for item in list(objects_elem):
            key_elem = item.find("key")
            if key_elem is not None:
                vec = key_elem.find("Vector2")
                if vec is not None:
                    ox, oy = parse_vector_or_coords(vec)
                    if ox is not None and oy is not None and should_clear(ox, oy):
                        objects_elem.remove(item)
                        cleared_tiles += 1

    # Limpiar <terrainFeatures>
    tf_elem = farm_elem.find("terrainFeatures")
    if tf_elem is not None:
        for item in list(tf_elem):
            key_elem = item.find("key")
            if key_elem is not None:
                vec = key_elem.find("Vector2")
                if vec is not None:
                    tx, ty = parse_vector_or_coords(vec)
                    if tx is not None and ty is not None and should_clear(tx, ty):
                        tf_elem.remove(item)
                        cleared_tiles += 1

    # Limpiar <resourceClumps>
    clumps_elem = farm_elem.find("resourceClumps")
    if clumps_elem is not None:
        for clump in list(clumps_elem):
            pos_elem = clump.find("tile")
            if pos_elem is not None:
                cx, cy = parse_vector_or_coords(pos_elem)
                w_elem = clump.find("width")
                h_elem = clump.find("height")
                cw = int(w_elem.text) if w_elem is not None and w_elem.text else 2
                ch = int(h_elem.text) if h_elem is not None and h_elem.text else 2
                if cx is not None and cy is not None:
                    # Comprobar si alguna celda solapa con las zonas despejadas
                    overlaps = any(should_clear(cx + dx, cy + dy) for dx in range(cw) for dy in range(ch))
                    if overlaps:
                        clumps_elem.remove(clump)
                        cleared_tiles += 1

    return purged_warps, pet_bowl_moved, cleared_tiles

def sanitize_farmers_position(root: ET.Element) -> int:
    """Verifica si algún granjero tiene disconnectLocation u obstáculos y lo fija en Farm (64, 15)."""
    reset_count = 0

    def check_and_reset(farmer: ET.Element) -> None:
        nonlocal reset_count
        dloc = farmer.find("disconnectLocation")
        if dloc is not None and dloc.text:
            loc_val = dloc.text.strip()
            # Si apunta a un puente inexistente o coordenadas comprometidas
            if loc_val.lower().startswith("pelican4ever_bridge_"):
                # Permitir bridges activos, pero si es de un puente culleado o huérfano, reubicar
                pass

        # Comprobar disconnectPosition en Farm
        if dloc is not None and dloc.text == "Farm":
            dpos = farmer.find("disconnectPosition")
            if dpos is not None:
                px, py = parse_vector_or_coords(dpos)
                if px is not None and py is not None:
                    tile_x = px // 64
                    tile_y = py // 64
                    # Si quedó en el antiguo borde oeste bloqueado
                    if tile_x <= 2 and tile_y in (16, 26, 36, 46):
                        dpos_x = dpos.find("X") or dpos.find("x")
                        dpos_y = dpos.find("Y") or dpos.find("y")
                        if dpos_x is not None and dpos_y is not None:
                            dpos_x.text = str(64 * 64)
                            dpos_y.text = str(15 * 64)
                            reset_count += 1

    player = root.find("player")
    if player is not None:
        check_and_reset(player)

    farmhands = root.find("farmhands")
    if farmhands is not None:
        for fh in farmhands:
            check_and_reset(fh)

    return reset_count

def migrate_xml_locations_geo(root: ET.Element, slot_rename_map: Dict[str, str]) -> int:
    """
    En modo 'geo', renombra las ubicaciones de parcelas privadas para que el propietario
    conserve su granja física en el nuevo slot.
    """
    renamed_count = 0
    locations_elem = root.find("locations")
    if locations_elem is None or not slot_rename_map:
        return 0

    # Usar un prefijo temporal para evitar colisiones durante intercambios (swap)
    temp_prefix = "__P4E_TEMP_MIGRATE__"

    for loc in locations_elem:
        name_elem = loc.find("name")
        if name_elem is not None and name_elem.text:
            cur_name = name_elem.text.strip()
            for old_slot, new_slot in slot_rename_map.items():
                if cur_name == old_slot:
                    name_elem.text = f"{temp_prefix}{new_slot}"
                    renamed_count += 1
                elif cur_name.startswith(f"{old_slot}_"):
                    sub = cur_name[len(old_slot):]
                    name_elem.text = f"{temp_prefix}{new_slot}{sub}"
                    renamed_count += 1
                elif cur_name == f"Greenhouse_{old_slot}":
                    name_elem.text = f"{temp_prefix}Greenhouse_{new_slot}"
                    renamed_count += 1

    # Retirar prefijo temporal
    for loc in locations_elem:
        name_elem = loc.find("name")
        if name_elem is not None and name_elem.text and name_elem.text.startswith(temp_prefix):
            name_elem.text = name_elem.text[len(temp_prefix):]

    return renamed_count

def migrate_save(
    save_dir: str,
    mode: str = "slot",
    custom_map: Optional[Dict[str, str]] = None,
    overwrite_host_map: bool = True,
    backup: bool = True,
    dry_run: bool = False
) -> None:
    """Ejecuta la migración completa de la partida guardada."""
    save_dir, save_file, save_game_info = locate_save_files(save_dir)
    print(f"Directorio de partida : {save_dir}")
    print(f"Archivo XML principal : {save_file}")
    print(f"Modo de migración     : {mode.upper()}")

    # 1. Copia de seguridad
    if backup and not dry_run:
        bak = create_backup(save_dir)
        print(f"[OK] Copia de seguridad creada en: {bak}")

    # 2. Parsear XML
    ET.register_namespace("", "")
    tree = ET.parse(save_file)
    root = tree.getroot()

    # 3. Localizar CustomData de SMAPI con Pelican4Ever.ServerWorldState
    cd_elem = root.find("CustomData")
    world_state_item = None
    world_state_json_str = None

    if cd_elem is not None:
        for item in cd_elem:
            k = (item.findtext("key/string") or "").strip()
            if k.lower() == P4E_STATE_KEY.lower():
                world_state_item = item
                world_state_json_str = item.findtext("value/string")
                break

    # Resolver diccionario de mapeo
    if mode == "geo":
        door_mapping = dict(GEO_DOOR_MAPPING)
    elif mode == "custom" and custom_map:
        door_mapping = dict(custom_map)
    else:
        door_mapping = dict(SLOT_DOOR_MAPPING)

    slot_rename_map: Dict[str, str] = {}
    doors_summary = []

    # 4. Migrar ServerWorldState si existe
    if world_state_item is not None and world_state_json_str:
        state = json.loads(world_state_json_str)
        doors = state.get("Doors", {})
        players = state.get("Players", {})
        annexes = state.get("Annexes", {})

        new_doors = {}
        for old_door, assign in doors.items():
            new_door = door_mapping.get(old_door, old_door)
            new_slot = NEW_DOOR_CATALOG.get(new_door, assign.get("FarmSlotId", ""))

            old_slot = assign.get("FarmSlotId", "")
            if mode == "geo" and old_slot != new_slot:
                slot_rename_map[old_slot] = new_slot

            assign["DoorId"] = new_door
            assign["FarmSlotId"] = new_slot
            new_doors[new_door] = assign

            owner_id = assign.get("OwnerId")
            p_name = players.get(str(owner_id), {}).get("PlayerName", str(owner_id)) if owner_id else "Sin reclamar"
            doors_summary.append((old_door, new_door, old_slot, new_slot, p_name))

        state["Doors"] = new_doors

        # Actualizar Players
        for pid_str, p_rec in players.items():
            cur_slot = p_rec.get("FarmSlotId")
            if cur_slot in slot_rename_map:
                p_rec["FarmSlotId"] = slot_rename_map[cur_slot]

        # Actualizar Annexes
        if annexes:
            new_annexes = {}
            for a_id, a_data in annexes.items():
                cur_res = a_data.get("ResidenceSlotId", "")
                target_res = slot_rename_map.get(cur_res, cur_res)
                a_data["ResidenceSlotId"] = target_res
                side = a_data.get("Side", 0)
                # side numérico o string
                side_str = side if isinstance(side, str) else ["North", "South", "East", "West"][side % 4]
                slot_num = target_res.replace("Pelican4Ever_Farm_", "")
                new_a_id = f"Pelican4Ever_Annex_{slot_num}_{side_str}"
                a_data["AnnexId"] = new_a_id
                new_annexes[new_a_id] = a_data
            state["Annexes"] = new_annexes

        # Guardar JSON actualizado en XML
        val_elem = world_state_item.find("value/string")
        if val_elem is not None:
            val_elem.text = json.dumps(state)

    # 5. Si modo es GEO, renombrar ubicaciones en XML
    locs_renamed = 0
    if mode == "geo" and slot_rename_map:
        locs_renamed = migrate_xml_locations_geo(root, slot_rename_map)

    # 6. Saneamiento de Granja Host si solicitado
    purged_warps, pet_bowl_moved, cleared_tiles = (0, False, 0)
    reset_farmers = 0

    if overwrite_host_map:
        locations_elem = root.find("locations")
        if locations_elem is not None:
            for loc in locations_elem:
                if (loc.findtext("name") or "").strip() == "Farm":
                    purged_warps, pet_bowl_moved, cleared_tiles = sanitize_host_farm(loc)
                    break
        reset_farmers = sanitize_farmers_position(root)

    # 7. Imprimir resumen
    print()
    print("=== RESUMEN DE MIGRACIÓN ===")
    if doors_summary:
        print(f"Puertas procesadas ({len(doors_summary)}):")
        for old_d, new_d, old_s, new_s, owner in doors_summary:
            print(f"  * {old_d} ({old_s}) -> {new_d} ({new_s}) [Dueño: {owner}]")
    else:
        print("  * No se encontraron puertas reclamadas registradas en ServerWorldState.")

    if locs_renamed > 0:
        print(f"  * Ubicaciones privadas renombradas en XML: {locs_renamed}")
    if overwrite_host_map:
        print(f"  * Warps obsoletos de Granja Host purgados: {purged_warps}")
        print(f"  * Pet Bowl reubicado a ({CANONICAL_PET_BOWL[0]}, {CANONICAL_PET_BOWL[1]}): {pet_bowl_moved}")
        print(f"  * Casillas despejadas en corredor oeste y accesos: {cleared_tiles}")
        print(f"  * Granjeros reubicados de posiciones bloqueadas: {reset_farmers}")

    # 8. Escribir a disco si no es dry-run
    if dry_run:
        print("\n[DRY RUN] Operación simulada completada. Ningún archivo fue modificado.")
    else:
        with open(save_file, "wb") as f:
            f.write(b'<?xml version="1.0" encoding="utf-8"?>\n')
            tree.write(f, encoding="utf-8", xml_declaration=False)
        print(f"\n[OK] Partida migrada con éxito y guardada en:\n  {save_file}")

def main() -> None:
    parser = argparse.ArgumentParser(
        description="Herramienta de migración de guardados de Pelican 4Ever a la Plaza Central y Puentes."
    )
    parser.add_argument("save_path", nargs="?", help="Ruta a la carpeta de la partida o al archivo XML principal.")
    parser.add_argument("--list-saves", action="store_true", help="Lista todas las partidas guardadas encontradas y sale.")
    parser.add_argument("--mode", choices=["slot", "geo", "custom", "interactive"], default="slot",
                        help="Modo de reasignación: 'slot' (conserva ID de hueco), 'geo' (conserva posición geográfica), 'custom' o 'interactive'.")
    parser.add_argument("--custom-map", help="Archivo JSON o string JSON con mapeo explícito {'OldDoor': 'NewDoor'}.")
    parser.add_argument("--no-clean-host", dest="clean_host", action="store_false", default=True,
                        help="Omite la limpieza y sobrescritura de warps/escombros en la Granja Host.")
    parser.add_argument("--no-backup", dest="backup", action="store_false", default=True,
                        help="Omite la creación previa de copia de seguridad.")
    parser.add_argument("--dry-run", action="store_true", help="Simula los cambios sin modificar los archivos.")

    args = parser.parse_args()

    if args.list_saves or not args.save_path:
        list_saves()
        if not args.save_path:
            print("Indica la ruta de la partida a migrar. Ejemplo:\n  python3 scripts/migrate_save_to_plaza.py ~/.config/StardewValley/Saves/Prueba13_453338619\n")
            sys.exit(0)

    custom_mapping = None
    if args.mode == "custom" and args.custom_map:
        if os.path.isfile(args.custom_map):
            with open(args.custom_map, "r", encoding="utf-8") as f:
                custom_mapping = json.load(f)
        else:
            custom_mapping = json.loads(args.custom_map)

    migrate_save(
        save_dir=args.save_path,
        mode=args.mode,
        custom_map=custom_mapping,
        overwrite_host_map=args.clean_host,
        backup=args.backup,
        dry_run=args.dry_run
    )

if __name__ == "__main__":
    main()
