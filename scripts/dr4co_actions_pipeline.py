#!/usr/bin/env python3
"""
Pipeline de procesamiento de las poses de acción (herramientas, regadera, combate y carga)
para la skin de Dr4co en Stardew Valley / Fashion Sense.

Toma la hoja de 9 poses de acción generada por el LLM (`dr4co_action_3x3_llm.png`),
las cuantiza a la paleta de 12 colores de Dr4co, aplica contorno 'sel-out' y genera:
  1. Recortes individuales 1x (24x32) y alta resolución.
  2. Lámina comparativa de acciones (1x y 4x) frente al granjero vanilla.
  3. Animaciones GIF de las acciones (golpe de herramienta, regar, ataque con espada) a 4x.
  4. Showcase integral de la skin completa (Caminar + Acciones en 4 direcciones).
"""
import os
import sys
import shutil
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from scripts.build_farmer_reference import FarmerAssembler, FRAME_FACING, scale_nn
SRC_ACTIONS_PNG = os.path.join(ROOT, "agents", "Dr4co sprites", "fuente", "dr4co_action_3x3_llm.png")

DR4CO_DIR = os.path.join(ROOT, "agents", "Dr4co sprites")
ACTIONS_GRANDES_DIR = os.path.join(DR4CO_DIR, "grandes_acciones")
ACTIONS_1X_DIR = os.path.join(DR4CO_DIR, "acciones_1x")
OUT_DIR = os.path.join(DR4CO_DIR, "pruebas", "pipeline_acciones")

os.makedirs(ACTIONS_GRANDES_DIR, exist_ok=True)
os.makedirs(ACTIONS_1X_DIR, exist_ok=True)
os.makedirs(OUT_DIR, exist_ok=True)

# Paleta fija de 12 colores de Dr4co
PALETTE = np.array([
    [ 53,  20,  18],  # 0: contorno oscuro calido
    [ 92,  26,  24],  # 1: rojo sombra profunda
    [138,  34,  30],  # 2: rojo sombra media
    [182,  45,  36],  # 3: rojo base escamas
    [218,  68,  45],  # 4: rojo luz
    [238, 108,  68],  # 5: coral / membrana ala
    [156, 108,  78],  # 6: crema sombra (cuerno/panza)
    [206, 162, 118],  # 7: crema medio
    [240, 212, 168],  # 8: crema luz
    [246, 198,  36],  # 9: amarillo ojo brillante
    [196, 128,  28],  # 10: ambar ojo
    [ 28,  10,  10],  # 11: pupila / negro calido
], dtype=np.uint8)

CW, CH = 24, 32
FOOTX = 9.5
FOOTY = 30

ACTION_NAMES_GRID = [
    ["frontal_tool", "frontal_water", "frontal_sword"],
    ["lateral_tool", "lateral_water", "lateral_sword"],
    ["trasera_tool", "trasera_water", "trasera_sword"],
]


def extract_9_action_poses(src_path: str):
    """Extrae las 9 figuras de la hoja de acciones 3x3 generada por IA."""
    im = Image.open(src_path).convert("RGBA")
    w, h = im.size
    cw, ch = w // 3, h // 3
    extracted = {}

    for r in range(3):
        for c in range(3):
            name = ACTION_NAMES_GRID[r][c]
            margin = 16
            sub = np.array(im.crop((c * cw + margin, r * ch + margin, (c + 1) * cw - margin, (r + 1) * ch - margin)))
            rgb = sub[:, :, :3].astype(np.int16)
            brightness = rgb.mean(axis=2)
            sat = rgb.max(axis=2) - rgb.min(axis=2)
            bg = (brightness > 232) & (sat < 22)
            sub[bg] = [0, 0, 0, 0]

            ys, xs = np.where(sub[:, :, 3] > 0)
            crop = Image.fromarray(sub[ys.min():ys.max() + 1, xs.min():xs.max() + 1])
            crop.save(os.path.join(ACTIONS_GRANDES_DIR, f"{name}.png"))
            extracted[name] = crop

    return extracted


def quantize_and_selout_action(small_rgba: Image.Image, view_type: str) -> Image.Image:
    """Cuantiza una pose de acción 1x a la paleta de 12 colores de Dr4co con sel-out."""
    arr = np.array(small_rgba)
    h, w, _ = arr.shape
    alpha = arr[:, :, 3] > 105
    rgb = arr[:, :, :3].astype(np.int16)

    out = np.zeros((h, w, 4), dtype=np.uint8)
    ys, xs = np.where(alpha)
    if len(ys) == 0:
        return Image.fromarray(out)

    red_pal = PALETTE[1:6].astype(np.int16)
    cream_pal = PALETTE[6:9].astype(np.int16)

    for y, x in zip(ys, xs):
        r, g, b = int(rgb[y, x, 0]), int(rgb[y, x, 1]), int(rgb[y, x, 2])
        pix = np.array([r, g, b], dtype=np.int16)
        is_cream = (g >= 122 and b >= 82 and (r - g) < 62) and (view_type != "trasera" or y < 7)
        if is_cream:
            d = ((cream_pal - pix) ** 2).sum(axis=1)
            out[y, x, :3] = PALETTE[6 + d.argmin()]
        else:
            d = ((red_pal - pix) ** 2).sum(axis=1)
            out[y, x, :3] = PALETTE[1 + d.argmin()]
        out[y, x, 3] = 255

    # Rescatar ojos en frontal y lateral
    if view_type in ("frontal", "lateral"):
        for y, x in zip(ys, xs):
            if not (6 <= y <= 12):
                continue
            r, g, b = int(rgb[y, x, 0]), int(rgb[y, x, 1]), int(rgb[y, x, 2])
            if r > 170 and g > 115 and b < 85 and (g - b) > 50:
                out[y, x, :3] = PALETTE[9] if g > 145 else PALETTE[10]

    # Contorno sel-out
    interior = np.zeros((h, w), dtype=bool)
    for y in range(1, h - 1):
        for x in range(1, w - 1):
            if alpha[y, x] and alpha[y - 1, x] and alpha[y + 1, x] and alpha[y, x - 1] and alpha[y, x + 1]:
                interior[y, x] = True
    border = alpha & (~interior)

    for y, x in zip(*np.where(interior)):
        c = out[y, x, :3]
        if np.array_equal(c, PALETTE[0]) or np.array_equal(c, PALETTE[11]):
            out[y, x, :3] = PALETTE[1]

    for y, x in zip(*np.where(border)):
        c = out[y, x, :3]
        if np.array_equal(c, PALETTE[7]) or np.array_equal(c, PALETTE[8]):
            out[y, x, :3] = PALETTE[6]
        else:
            out[y, x, :3] = PALETTE[0]

    return Image.fromarray(out)


def place_action_in_cell(im: Image.Image, foot_x_target: float = FOOTX) -> Image.Image:
    """Coloca la pose de acción en la celda 24x32 apoyando los pies en FOOTY=30."""
    arr = np.array(im)
    alpha = arr[:, :, 3] > 128
    ys, xs = np.where(alpha)
    y_max = int(ys.max())
    bottom_xs = xs[ys >= y_max - 2]
    cx = float(np.mean(bottom_xs))

    ox = int(round(foot_x_target - cx))
    ox = max(0, min(CW - im.width, ox))
    oy = int(FOOTY - 1 - y_max)

    cell = Image.new("RGBA", (CW, CH), (0, 0, 0, 0))
    cell.alpha_composite(im, (ox, oy))
    return cell


def process_action_poses(extracted_9: dict):
    """Procesa y reduce las 9 poses de acción a 1x."""
    poses_1x = {}
    for r in range(3):
        prefix = ["frontal", "lateral", "trasera"][r]
        target_h = 27 if prefix != "lateral" else 26
        for c in range(3):
            action_type = ["tool", "water", "sword"][c]
            name = f"{prefix}_{action_type}"
            hi = extracted_9[name]

            # Limpiar trazos oscuros internos antes de reducir
            hi_arr = np.array(hi)
            fg = hi_arr[:, :, 3] > 128
            mask_im = Image.fromarray((fg * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(9))
            deep_in = np.array(mask_im) > 128
            dark_internal = deep_in & (hi_arr[:, :, 0] < 95) & (hi_arr[:, :, 1] < 45) & (hi_arr[:, :, 2] < 45)
            hi_arr[dark_internal, :3] = [148, 38, 32]
            hi_clean = Image.fromarray(hi_arr)

            tw = max(1, round(hi_clean.width * target_h / hi_clean.height))
            small = hi_clean.resize((tw, target_h), Image.BOX)
            q_im = quantize_and_selout_action(small, prefix)
            q_im.save(os.path.join(ACTIONS_1X_DIR, f"{name}.png"))
            poses_1x[name] = q_im

    return poses_1x


def build_action_comparison(poses_1x: dict, farmer_asm: FarmerAssembler):
    """Genera una lámina comparativa de acciones (Golpe, Regar, Espada) en las 4 direcciones a 6x."""
    scale = 6
    actions_list = [
        ("Golpe Abajo", "frontal_tool", (68, 2, 6, False)),
        ("Regar Abajo", "frontal_water", (55, 2, 6, False)),
        ("Espada Abajo", "frontal_sword", (26, 2, 6, False)),
        ("Golpe Derecha", "lateral_tool", (50, 1, 6, False)),
        ("Regar Derecha", "lateral_water", (59, 1, 6, False)),
        ("Espada Derecha", "lateral_sword", (32, 1, 6, False)),
        ("Golpe Arriba", "trasera_tool", (38, 0, 6, False)),
        ("Regar Arriba", "trasera_water", (63, 0, 6, False)),
        ("Espada Arriba", "trasera_sword", (38, 0, 6, False)),
        ("Golpe Izquierda", "lateral_tool_flip", (50, 3, 6, True)),
        ("Regar Izquierda", "lateral_water_flip", (59, 3, 6, True)),
        ("Espada Izquierda", "lateral_sword_flip", (32, 3, 6, True)),
    ]

    cols = len(actions_list)
    cell_w = CW * scale
    cell_h = CH * scale
    pad = 16
    header_h = 44
    canvas_w = pad + cols * (cell_w + pad)
    canvas_h = header_h + 2 * (cell_h + pad)

    panel = Image.new("RGBA", (canvas_w, canvas_h), (26, 28, 34, 255))
    d = ImageDraw.Draw(panel)

    for idx, (label, d_key, (ff, facing, arm_off, flip)) in enumerate(actions_list):
        cx0 = pad + idx * (cell_w + pad)

        # Granjero vanilla (fila superior)
        f_cell_16 = farmer_asm.render_frame(ff, facing=facing, arm_offset=arm_off, flip=flip)
        f_cell_24 = Image.new("RGBA", (24, 32), (0, 0, 0, 0))
        f_cell_24.alpha_composite(f_cell_16, (4, 0))

        # Dr4co (fila inferior)
        if "_flip" in d_key:
            base_key = d_key.replace("_flip", "")
            d_im = poses_1x[base_key].transpose(Image.FLIP_LEFT_RIGHT)
        else:
            d_im = poses_1x[d_key]
        d_cell_24 = place_action_in_cell(d_im, foot_x_target=FOOTX)

        for r_idx, (c24, src_name) in enumerate([(f_cell_24, "Granjero"), (d_cell_24, "Dr4co")]):
            cy0 = header_h + r_idx * (cell_h + pad)
            d.rectangle([cx0, cy0, cx0 + cell_w - 1, cy0 + cell_h - 1], fill=(42, 46, 54, 255))
            d.line([cx0, cy0 + FOOTY * scale, cx0 + cell_w - 1, cy0 + FOOTY * scale], fill=(85, 105, 70, 200))
            up = c24.resize((cell_w, cell_h), Image.NEAREST)
            panel.alpha_composite(up, (cx0, cy0))

        d.text((cx0 + 2, 8), label, fill=(230, 230, 235, 255))

    out_panel_path = os.path.join(OUT_DIR, "comparativa_acciones_x6.png")
    panel.save(out_panel_path)
    return out_panel_path


def build_action_animations_gif(walk_poses_1x: dict, action_poses_1x: dict):
    """Genera animaciones GIF a 4x de Dr4co ejecutando las acciones (Herramienta, Regadera, Ataque) en bucle."""
    scale = 4
    cw4, ch4 = CW * scale, CH * scale
    pad = 12

    # 3 secuencias de acción:
    # 1. Tool Strike: quieto -> action_tool -> quieto
    # 2. Watering: quieto -> action_water -> quieto
    # 3. Sword Slash: quieto -> action_sword -> quieto
    action_types = [
        ("Golpe de Herramienta", "tool"),
        ("Regar Cultivos", "water"),
        ("Tajo de Combate", "sword"),
    ]
    directions = [("Abajo", "frontal"), ("Derecha", "lateral"), ("Arriba", "trasera"), ("Izquierda", "lateral_flip")]

    total_w = pad + len(action_types) * (4 * cw4 + pad)
    total_h = 36 + ch4 + pad

    frames = []
    # 4 fases de animación por ciclo: (quieto, acción, acción, quieto)
    cycle_phases = [0, 1, 1, 0]

    for phase in cycle_phases:
        fr = Image.new("RGBA", (total_w, total_h), (28, 30, 36, 255))
        d = ImageDraw.Draw(fr)

        for a_idx, (a_label, a_key) in enumerate(action_types):
            bx = pad + a_idx * (4 * cw4 + pad)
            d.text((bx + 4, 10), a_label, fill=(235, 235, 240, 255))

            for dir_idx, (dir_name, dir_prefix) in enumerate(directions):
                cx0 = bx + dir_idx * cw4
                cy0 = 36
                d.rectangle([cx0, cy0, cx0 + cw4 - 1, cy0 + ch4 - 1], fill=(44, 48, 56, 255))
                d.line([cx0, cy0 + FOOTY * scale, cx0 + cw4 - 1, cy0 + FOOTY * scale], fill=(85, 105, 70, 255))

                # Elegir pose según la fase
                if phase == 0:
                    base_im = walk_poses_1x[f"{dir_prefix.replace('_flip', '')}_quieto"]
                else:
                    base_im = action_poses_1x[f"{dir_prefix.replace('_flip', '')}_{a_key}"]

                if "_flip" in dir_prefix:
                    base_im = base_im.transpose(Image.FLIP_LEFT_RIGHT)

                cell24 = place_action_in_cell(base_im, foot_x_target=FOOTX)
                up = cell24.resize((cw4, ch4), Image.NEAREST)
                fr.alpha_composite(up, (cx0, cy0))

        frames.append(fr.convert("RGB"))

    gif_path = os.path.join(OUT_DIR, "dr4co_acciones_animadas_4x.gif")
    frames[0].save(gif_path, save_all=True, append_images=frames[1:], duration=240, loop=0)
    return gif_path


def build_complete_skin_showcase(walk_poses_1x: dict, action_poses_1x: dict):
    """
    Construye la lámina maestra de toda la skin de Dr4co (Caminar + Acciones en 4 direcciones)
    a tamaño real 1x y a zoom 4x de pantalla.
    """
    # 4 filas (Abajo, Derecha, Arriba, Izquierda) x 7 columnas:
    # Col 0: Quieto
    # Col 1: Paso 1
    # Col 2: Paso 2
    # Col 3: Golpe herramienta
    # Col 4: Regar
    # Col 5: Ataque espada
    # Col 6: Sostener/Cargar objeto
    cols_n = 7
    rows_n = 4

    showcase_1x = Image.new("RGBA", (cols_n * CW, rows_n * CH), (0, 0, 0, 0))

    directions = [
        (0, "frontal", False),
        (1, "lateral", False),
        (2, "trasera", False),
        (3, "lateral", True),
    ]

    for r, prefix, flip in directions:
        row_sprites = [
            walk_poses_1x[f"{prefix}_quieto"],
            walk_poses_1x[f"{prefix}_paso1"],
            walk_poses_1x[f"{prefix}_paso2"],
            action_poses_1x[f"{prefix}_tool"],
            action_poses_1x[f"{prefix}_water"],
            action_poses_1x[f"{prefix}_sword"],
            walk_poses_1x[f"{prefix}_quieto"], # Carry base
        ]
        for c, sprite in enumerate(row_sprites):
            sp = sprite.transpose(Image.FLIP_LEFT_RIGHT) if flip else sprite
            cell = place_action_in_cell(sp, foot_x_target=FOOTX)
            showcase_1x.alpha_composite(cell, (c * CW, r * CH))

    out_1x = os.path.join(OUT_DIR, "dr4co_skin_showcase_1x.png")
    out_4x = os.path.join(OUT_DIR, "dr4co_skin_showcase_4x.png")
    showcase_1x.save(out_1x)
    scale_nn(showcase_1x, 4).save(out_4x)

    return out_1x, out_4x


def main():
    if not os.path.exists(SRC_ACTIONS_PNG):
        print("Error: No existe", SRC_ACTIONS_PNG)
        return

    extracted_9 = extract_9_action_poses(SRC_ACTIONS_PNG)
    action_poses_1x = process_action_poses(extracted_9)

    # Cargar las poses de caminar ya procesadas
    walk_dir = os.path.join(DR4CO_DIR, "pruebas", "pipeline_ia")
    walk_poses_1x = {}
    for prefix in ("frontal", "lateral", "trasera"):
        for p in ("quieto", "paso1", "paso2"):
            p_path = os.path.join(walk_dir, f"llm3x3_{prefix}_{p}_1x.png")
            if os.path.exists(p_path):
                walk_poses_1x[f"{prefix}_{p}"] = Image.open(p_path).convert("RGBA")
            else:
                # Fallback a cubica
                fallback_path = os.path.join(DR4CO_DIR, "pruebas", "cubica", f"{prefix}_{p}.png")
                walk_poses_1x[f"{prefix}_{p}"] = Image.open(fallback_path).convert("RGBA")

    farmer_asm = FarmerAssembler()
    cmp_path = build_action_comparison(action_poses_1x, farmer_asm)
    gif_path = build_action_animations_gif(walk_poses_1x, action_poses_1x)
    sc_1x, sc_4x = build_complete_skin_showcase(walk_poses_1x, action_poses_1x)

    print("Pipeline de acciones completado:")
    print(" - Comparativa 6x:", cmp_path)
    print(" - GIF animado 4x:", gif_path)
    print(" - Showcase 1x:", sc_1x)
    print(" - Showcase 4x:", sc_4x)


if __name__ == "__main__":
    main()
