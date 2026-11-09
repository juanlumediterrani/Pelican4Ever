#!/usr/bin/env python3
"""
Pipeline completo de generación y post-procesado de sprites de Dr4co para Stardew Valley (Fashion Sense).

Demuestra los dos pilares para generar sprites 16-bit consistentes con un LLM de imagen:
  1. Entrada estructurada 3x3 sin herramientas (`llm_pose_guide_3x3.png` + `dr4co-sid-v2.png`),
     que produjo `agents/Dr4co sprites/fuente/dr4co_3x3_llm.png` con 9 poses consistentes.
  2. Post-procesado determinista estilo Stardew Valley (`FarmerRenderer`):
     - Reducción por área/cúbica + cuantización a paleta fija de 12 colores.
     - Reconstrucción de ojos nítidos a 1x (amarillo `#f6c624` + pupila `#351412`).
     - "Head-Lock" (cabeza fija tomada de la pose `quieto` inyectada en `paso1` y `paso2`)
       con desplazamiento vertical de `+1 px` al dar el paso (`featureYOffsetPerFrame`),
       evitando que los cuernos o los ojos tiemblen al caminar.
     - Contorno coloreado ("sel-out") e integración en hoja Fashion Sense `96x128` (`24x32` por celda).
"""
import os
import shutil
from PIL import Image, ImageDraw, ImageFilter
import numpy as np

ROOT = "/home/mtx/Documentos/Projects/Stardew Valley MORPG"
ARTIFACT_DIR = "/home/mtx/.gemini/antigravity/brain/388369fa-4859-4b2e-9a31-3c90c4a2d744"
LLM_JPG_SRC = os.path.join(ARTIFACT_DIR, "dr4co_3x3_llm_1794193495219.jpg")

DR4CO_DIR = os.path.join(ROOT, "agents", "Dr4co sprites")
FUENTE_DIR = os.path.join(DR4CO_DIR, "fuente")
GRANDES_3X3_DIR = os.path.join(DR4CO_DIR, "grandes_3x3")
OUT_DIR = os.path.join(DR4CO_DIR, "pruebas", "pipeline_ia")

os.makedirs(FUENTE_DIR, exist_ok=True)
os.makedirs(GRANDES_3X3_DIR, exist_ok=True)
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

NAMES_GRID = [
    ["frontal_quieto", "frontal_paso1", "frontal_paso2"],
    ["lateral_quieto", "lateral_paso1", "lateral_paso2"],
    ["trasera_quieto", "trasera_paso1", "trasera_paso2"],
]


def extract_9_from_llm_3x3(src_path: str):
    """Extrae las 9 figuras de la hoja 3x3 generada por IA eliminando fondo blanco y líneas de rejilla."""
    im = Image.open(src_path).convert("RGBA")
    # Guardar copia en fuente/dr4co_3x3_llm.png
    im.save(os.path.join(FUENTE_DIR, "dr4co_3x3_llm.png"))
    w, h = im.size
    cw, ch = w // 3, h // 3
    extracted = {}

    for r in range(3):
        for c in range(3):
            name = NAMES_GRID[r][c]
            # Ignorar 16px de borde de celda para descartar la línea gris de la cuadrícula
            margin = 16
            sub = np.array(im.crop((c * cw + margin, r * ch + margin, (c + 1) * cw - margin, (r + 1) * ch - margin)))
            # Limpiar fondo blanco (R>232, G>232, B>232) y semitransparencias de borde JPEG
            rgb = sub[:, :, :3].astype(np.int16)
            brightness = rgb.mean(axis=2)
            sat = rgb.max(axis=2) - rgb.min(axis=2)
            bg = (brightness > 232) & (sat < 22)
            sub[bg] = [0, 0, 0, 0]

            ys, xs = np.where(sub[:, :, 3] > 0)
            crop = Image.fromarray(sub[ys.min():ys.max() + 1, xs.min():xs.max() + 1])
            crop.save(os.path.join(GRANDES_3X3_DIR, f"{name}.png"))
            extracted[name] = crop

    return extracted


def quantize_and_selout(small_rgba: Image.Image, view_type: str) -> Image.Image:
    """
    Cuantiza un sprite 1x a la paleta de 12 colores de Dr4co, aplica contorno coloreado (sel-out)
    y reconstruye el brillo amarillo de los ojos para que no se pierda al reducir 10x.
    """
    arr = np.array(small_rgba)
    h, w, _ = arr.shape
    alpha = arr[:, :, 3] > 105
    rgb = arr[:, :, :3].astype(np.int16)

    out = np.zeros((h, w, 4), dtype=np.uint8)
    ys, xs = np.where(alpha)
    if len(ys) == 0:
        return Image.fromarray(out)

    # Separar rampa ROJA (índices 1..5) de rampa CREMA (índices 6..8) por croma/luminancia:
    # Al promediar líneas oscuras internas con escamas rojas, un RGB desaturado como (155, 95, 75)
    # caería por distancia euclídea en PALETTE[6] (crema sombra) si no exigimos brillo crema real.
    red_pal = PALETTE[1:6].astype(np.int16)
    cream_pal = PALETTE[6:9].astype(np.int16)

    for y, x in zip(ys, xs):
        r, g, b = int(rgb[y, x, 0]), int(rgb[y, x, 1]), int(rgb[y, x, 2])
        pix = np.array([r, g, b], dtype=np.int16)
        # Es crema (cuernos, panza, mandíbula inferior) solo si tiene G y B altos y baja diferencia R-G;
        # en vista trasera la panza está oculta, así que solo los cuernos superiores (y < 7) llevan crema.
        is_cream = (g >= 122 and b >= 82 and (r - g) < 62) and (view_type != "trasera" or y < 7)
        if is_cream:
            d = ((cream_pal - pix) ** 2).sum(axis=1)
            out[y, x, :3] = PALETTE[6 + d.argmin()]
        else:
            d = ((red_pal - pix) ** 2).sum(axis=1)
            out[y, x, :3] = PALETTE[1 + d.argmin()]
        out[y, x, 3] = 255

    # Rescatar ÚNICAMENTE los píxeles amarillos de los ojos en la zona ocular (y entre 7 y 11)
    if view_type in ("frontal", "lateral"):
        for y, x in zip(ys, xs):
            if not (7 <= y <= 11):
                continue
            r, g, b = int(rgb[y, x, 0]), int(rgb[y, x, 1]), int(rgb[y, x, 2])
            if r > 175 and g > 118 and b < 80 and (g - b) > 55:
                out[y, x, :3] = PALETTE[9] if g > 150 else PALETTE[10]

    # Contorno coloreado (sel-out): ningún píxel interior usa negro puro/contorno oscuro;
    # el borde exterior usa sombra de cuerno (6) junto a crema y contorno cálido (0) en el resto
    interior = np.zeros((h, w), dtype=bool)
    for y in range(1, h - 1):
        for x in range(1, w - 1):
            if alpha[y, x] and alpha[y - 1, x] and alpha[y + 1, x] and alpha[y, x - 1] and alpha[y, x + 1]:
                interior[y, x] = True
    border = alpha & (~interior)

    for y, x in zip(*np.where(interior)):
        c = out[y, x, :3]
        if np.array_equal(c, PALETTE[0]) or np.array_equal(c, PALETTE[11]):
            # Mantener pupila oscura justo al lado del ojo amarillo en frontal/lateral
            has_eye_neighbor = False
            if view_type in ("frontal", "lateral") and 6 <= y <= 12:
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        ny, nx = y + dy, x + dx
                        if 0 <= ny < h and 0 <= nx < w and np.array_equal(out[ny, nx, :3], PALETTE[9]):
                            has_eye_neighbor = True
            if not has_eye_neighbor:
                out[y, x, :3] = PALETTE[1]

    for y, x in zip(*np.where(border)):
        c = out[y, x, :3]
        if np.array_equal(c, PALETTE[7]) or np.array_equal(c, PALETTE[8]):
            out[y, x, :3] = PALETTE[6]
        else:
            out[y, x, :3] = PALETTE[0]

    # Asegurar ojos nítidos de 2px (amarillo brillante + ámbar/pupila) en frontal y lateral
    if view_type == "frontal":
        eye_ys, eye_xs = np.where((out[:, :, 3] > 0) & ((out[:, :, 0] == PALETTE[9][0]) | (out[:, :, 0] == PALETTE[10][0])))
        if len(eye_ys) >= 2:
            ey = int(np.median(eye_ys))
            left_x = int(eye_xs.min())
            right_x = int(eye_xs.max())
            out[ey, left_x, :3] = PALETTE[9]
            if left_x + 1 < w:
                out[ey, left_x + 1, :3] = PALETTE[10]
            out[ey, right_x, :3] = PALETTE[9]
            if right_x - 1 >= 0:
                out[ey, right_x - 1, :3] = PALETTE[10]
    elif view_type == "lateral":
        eye_ys, eye_xs = np.where((out[:, :, 3] > 0) & ((out[:, :, 0] == PALETTE[9][0]) | (out[:, :, 0] == PALETTE[10][0])))
        ey = int(np.median(eye_ys)) if len(eye_ys) else 9
        ex = int(np.max(eye_xs)) if len(eye_xs) else max(4, w - 6)
        if alpha[ey, ex]:
            out[ey, ex, :3] = PALETTE[9]
            if ex - 1 >= 0 and alpha[ey, ex - 1]:
                out[ey, ex - 1, :3] = PALETTE[10]
            if ex + 1 < w and alpha[ey, ex + 1]:
                out[ey, ex + 1, :3] = PALETTE[0]

    return Image.fromarray(out)


def lock_head_across_walk(idle_im: Image.Image, step_im: Image.Image, split_y: int) -> Image.Image:
    """
    Reproduce la técnica de Stardew Valley (`FarmerRenderer`):
    Fija la cabeza exacta de la pose `quieto` (`y < split_y`) sobre los frames de paso (`paso1`, `paso2`),
    alineándola horizontalmente por centroide de la cabeza, para que los cuernos y los ojos
    sean 100% idénticos píxel por píxel durante toda la caminata.
    """
    idle_arr = np.array(idle_im)
    step_arr = np.array(step_im)
    h, w, _ = step_arr.shape

    # Calcular centro X de la cabeza en idle y en step
    idle_head_xs = np.where(idle_arr[:split_y, :, 3] > 0)[1]
    step_head_xs = np.where(step_arr[:split_y, :, 3] > 0)[1]
    dx = 0
    if len(idle_head_xs) and len(step_head_xs):
        dx = int(round(np.mean(step_head_xs) - np.mean(idle_head_xs)))

    res = step_arr.copy()
    res[:split_y, :, :] = 0
    for y in range(min(split_y, idle_arr.shape[0])):
        for x in range(idle_arr.shape[1]):
            nx = x + dx
            if 0 <= nx < w and idle_arr[y, x, 3] > 0:
                res[y, nx] = idle_arr[y, x]

    return Image.fromarray(res)


def place_in_cell(im: Image.Image, flip_x: bool = False, foot_x_target: float = FOOTX, y_bob: int = 0) -> Image.Image:
    """Coloca el sprite dentro de su celda Fashion Sense 24x32 con soporte de bobbing vertical (`+1px` en pasos)."""
    if flip_x:
        im = im.transpose(Image.FLIP_LEFT_RIGHT)
    arr = np.array(im)
    alpha = arr[:, :, 3] > 128
    ys, xs = np.where(alpha)
    y_max = int(ys.max())
    bottom_xs = xs[ys >= y_max - 2]
    cx = float(np.mean(bottom_xs))

    ox = int(round(foot_x_target - cx))
    ox = max(0, min(CW - im.width, ox))
    oy = int(FOOTY - 1 - y_max) + y_bob

    cell = Image.new("RGBA", (CW, CH), (0, 0, 0, 0))
    cell.alpha_composite(im, (ox, oy))
    return cell


def assemble_fashion_sense_sheet(poses_1x: dict, apply_bob: bool = True) -> Image.Image:
    """
    Ensambla la hoja 4x4 (96x128 px) para Fashion Sense:
      Fila 0 (Abajo):     quieto, paso1, quieto, paso2
      Fila 1 (Derecha):   quieto, paso1, quieto, paso2
      Fila 2 (Arriba):    quieto, paso1, quieto, paso2
      Fila 3 (Izquierda): sin voltear con pivote X simétrico (CW - FOOTX - 2) porque Stardew voltea la celda 24x32
    """
    sheet = Image.new("RGBA", (CW * 4, CH * 4), (0, 0, 0, 0))
    bob = 1 if apply_bob else 0

    rows_cfg = [
        (0, "frontal", FOOTX),
        (1, "lateral", FOOTX),
        (2, "trasera", FOOTX),
        (3, "lateral", CW - FOOTX - 2),
    ]
    for r, prefix, fx in rows_cfg:
        seq = [
            (poses_1x[f"{prefix}_quieto"], 0),
            (poses_1x[f"{prefix}_paso1"], bob),
            (poses_1x[f"{prefix}_quieto"], 0),
            (poses_1x[f"{prefix}_paso2"], bob),
        ]
        for c, (sprite, y_bob) in enumerate(seq):
            cell = place_in_cell(sprite, flip_x=False, foot_x_target=fx, y_bob=y_bob)
            sheet.alpha_composite(cell, (c * CW, r * CH))
    return sheet


def build_variant_v1_refined_existing() -> Image.Image:
    """
    Variante 1: Mejora la hoja actual de Dr4co usando:
      - Las poses frontales con cabeza fija (`agents/Dr4co sprites/1/frontal_*_cabeza_fija.png`).
      - Las poses laterales y traseras reducidas con ojos reconstruidos y Head-Lock (`y < 14`).
      - El bobbing vertical de `+1px` en los pasos (`paso1` y `paso2`), igual que el granjero de Stardew.
    """
    cubica_dir = os.path.join(DR4CO_DIR, "pruebas", "cubica")
    fixed_front_dir = os.path.join(DR4CO_DIR, "1")

    poses = {}
    # Frontales con cabeza fija ya validada y recortada a su bounding box
    for p in ("quieto", "paso1", "paso2"):
        im = Image.open(os.path.join(fixed_front_dir, f"frontal_{p}_cabeza_fija.png")).convert("RGBA")
        arr = np.array(im)
        ys, xs = np.where(arr[:, :, 3] > 0)
        poses[f"frontal_{p}"] = Image.fromarray(arr[ys.min():ys.max() + 1, xs.min():xs.max() + 1])

    # Laterales y traseras con ojos nítidos y cabeza bloqueada desde `quieto`
    for prefix in ("lateral", "trasera"):
        for p in ("quieto", "paso1", "paso2"):
            im = Image.open(os.path.join(cubica_dir, f"{prefix}_{p}.png")).convert("RGBA")
            if prefix == "lateral":
                # Hacer el ojo amarillo bien visible en la vista lateral (y=9, x=9..10)
                arr = np.array(im)
                arr[9, 10] = [246, 198, 36, 255]
                arr[9, 9] = [196, 128, 28, 255]
                arr[9, 11] = [53, 20, 18, 255]
                im = Image.fromarray(arr)
            poses[f"{prefix}_{p}"] = im

        # Bloquear cabeza de quieto (y < 14) en paso1 y paso2
        poses[f"{prefix}_paso1"] = lock_head_across_walk(poses[f"{prefix}_quieto"], poses[f"{prefix}_paso1"], split_y=14)
        poses[f"{prefix}_paso2"] = lock_head_across_walk(poses[f"{prefix}_quieto"], poses[f"{prefix}_paso2"], split_y=14)

    return assemble_fashion_sense_sheet(poses, apply_bob=True)


def build_variant_v2_from_llm_3x3(extracted_9: dict) -> Image.Image:
    """
    Variante 2: Pipeline 100% desde la nueva hoja 3x3 generada por IA (`dr4co_3x3_llm.png`)
    usando la guía de poses del granjero (`llm_pose_guide_3x3.png`) + referencia de Dr4co (`dr4co-sid-v2.png`).
    """
    poses_1x = {}
    for prefix, target_h in (("frontal", 27), ("lateral", 26), ("trasera", 27)):
        for p in ("quieto", "paso1", "paso2"):
            name = f"{prefix}_{p}"
            hi = extracted_9[name]
            # Reemplazar trazos negros/marrones muy oscuros INTERNOS en alta resolución por rojo sombra
            # antes de reducir, para que las líneas de las costillas/alas no creen ruido al bajar de 230px a 27px
            hi_arr = np.array(hi)
            fg = hi_arr[:, :, 3] > 128
            # Erosión rápida de 5px para detectar interior profundo en 230px
            from PIL import ImageOps
            mask_im = Image.fromarray((fg * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(9))
            deep_in = np.array(mask_im) > 128
            dark_internal = deep_in & (hi_arr[:, :, 0] < 95) & (hi_arr[:, :, 1] < 45) & (hi_arr[:, :, 2] < 45)
            hi_arr[dark_internal, :3] = [148, 38, 32]
            hi_clean = Image.fromarray(hi_arr)

            tw = max(1, round(hi_clean.width * target_h / hi_clean.height))
            small = hi_clean.resize((tw, target_h), Image.BOX)
            q_im = quantize_and_selout(small, prefix)
            poses_1x[name] = q_im

        # Aplicar Head-Lock estilo Stardew Valley (y < 14) desde `quieto` hacia `paso1` y `paso2`
        poses_1x[f"{prefix}_paso1"] = lock_head_across_walk(
            poses_1x[f"{prefix}_quieto"], poses_1x[f"{prefix}_paso1"], split_y=14
        )
        poses_1x[f"{prefix}_paso2"] = lock_head_across_walk(
            poses_1x[f"{prefix}_quieto"], poses_1x[f"{prefix}_paso2"], split_y=14
        )
        for p in ("quieto", "paso1", "paso2"):
            poses_1x[f"{prefix}_{p}"].save(os.path.join(OUT_DIR, f"llm3x3_{prefix}_{p}_1x.png"))

    return assemble_fashion_sense_sheet(poses_1x, apply_bob=True)


def render_comparison_panel(farmer_sheet: Image.Image, old_sheet: Image.Image, v1_sheet: Image.Image, v2_sheet: Image.Image):
    """Genera una lámina comparativa a 6x y un GIF animado a 4x con las 4 columnas."""
    scale = 6
    cols = [
        ("1. Granjero Base SDV\n(16x32 en 24x32)", farmer_sheet),
        ("2. Dr4co Anterior\n(Hoja IA 24 poses)", old_sheet),
        ("3. Dr4co Refinado v1\n(Head-Lock + Ojos + Bob)", v1_sheet),
        ("4. Dr4co Nuevo LLM 3x3\n(Guía 3x3 + Head-Lock)", v2_sheet),
    ]
    sw, sh = CW * 4 * scale, CH * 4 * scale
    pad = 18
    header_h = 48
    canvas_w = pad + len(cols) * (sw + pad)
    canvas_h = header_h + sh + pad

    panel = Image.new("RGBA", (canvas_w, canvas_h), (28, 30, 36, 255))
    draw = ImageDraw.Draw(panel)

    for idx, (title, sheet) in enumerate(cols):
        x0 = pad + idx * (sw + pad)
        y0 = header_h
        draw.text((x0 + 4, 8), title, fill=(235, 235, 240, 255))

        # Fondo ajedrezado por celda
        for r in range(4):
            for c in range(4):
                cx0 = x0 + c * CW * scale
                cy0 = y0 + r * CH * scale
                bg_col = (44, 48, 56, 255) if (r + c) % 2 == 0 else (52, 56, 66, 255)
                draw.rectangle([cx0, cy0, cx0 + CW * scale - 1, cy0 + CH * scale - 1], fill=bg_col)
                # Línea de suelo (FOOTY = 30)
                fy = cy0 + FOOTY * scale
                draw.line([cx0, fy, cx0 + CW * scale - 1, fy], fill=(95, 110, 75, 180), width=1)

        up = sheet.resize((sw, sh), Image.NEAREST)
        panel.alpha_composite(up, (x0, y0))

    panel_path = os.path.join(OUT_DIR, "comparativa_4_hojas_x6.png")
    panel.save(panel_path)

    # Generar GIF animado caminando en las 4 direcciones (zoom 4x real de pantalla)
    gif_scale = 4
    cell_s = CW * gif_scale
    cell_h = CH * gif_scale
    gif_w = pad + len(cols) * (cell_s * 4 + pad)
    gif_h = 36 + cell_h + pad
    frames = []

    for step_col in range(4):
        fr = Image.new("RGBA", (gif_w, gif_h), (30, 32, 38, 255))
        d = ImageDraw.Draw(fr)
        for idx, (title, sheet) in enumerate(cols):
            bx = pad + idx * (cell_s * 4 + pad)
            by = 36
            short_title = title.split("\n")[0]
            d.text((bx + 4, 10), short_title, fill=(235, 235, 240, 255))
            for dir_row in range(4):
                cx0 = bx + dir_row * cell_s
                d.rectangle([cx0, by, cx0 + cell_s - 1, by + cell_h - 1], fill=(48, 52, 62, 255))
                d.line([cx0, by + FOOTY * gif_scale, cx0 + cell_s - 1, by + FOOTY * gif_scale], fill=(95, 110, 75, 255))
                crop = sheet.crop((step_col * CW, dir_row * CH, (step_col + 1) * CW, (dir_row + 1) * CH))
                if dir_row == 3:
                    # En el juego real, la fila 3 (Izquierda) la voltea Stardew Valley al renderizar
                    crop = crop.transpose(Image.FLIP_LEFT_RIGHT)
                up = crop.resize((cell_s, cell_h), Image.NEAREST)
                fr.alpha_composite(up, (cx0, by))
        frames.append(fr.convert("RGB"))

    gif_path = os.path.join(OUT_DIR, "comparativa_caminar_4x.gif")
    frames[0].save(gif_path, save_all=True, append_images=frames[1:], duration=180, loop=0)
    return panel_path, gif_path


def main():
    extracted_9 = extract_9_from_llm_3x3(LLM_JPG_SRC)

    farmer_sheet = Image.open(os.path.join(ROOT, "agents", "farmer_reference", "dr4co_template", "farmer_body_1x.png")).convert("RGBA")
    old_sheet = Image.open(os.path.join(DR4CO_DIR, "pruebas", "montada", "body.png")).convert("RGBA")

    v1_sheet = build_variant_v1_refined_existing()
    v1_path = os.path.join(OUT_DIR, "body_v1_refinado.png")
    v1_sheet.save(v1_path)

    v2_sheet = build_variant_v2_from_llm_3x3(extracted_9)
    v2_path = os.path.join(OUT_DIR, "body_v2_llm3x3.png")
    v2_sheet.save(v2_path)

    # También guardar vistas x6 individuales
    v1_sheet.resize((CW * 4 * 6, CH * 4 * 6), Image.NEAREST).save(os.path.join(OUT_DIR, "body_v1_refinado_x6.png"))
    v2_sheet.resize((CW * 4 * 6, CH * 4 * 6), Image.NEAREST).save(os.path.join(OUT_DIR, "body_v2_llm3x3_x6.png"))

    # Actualizar el body.png activo del pack Fashion Sense con la versión refinada v1 (y dejar v2 lista para elegir)
    fs_body_path = os.path.join(ROOT, "agents", "Dr4co.FashionSense", "Bodies", "Dr4co", "body.png")
    shutil.copyfile(v1_path, fs_body_path)
    shutil.copyfile(v1_path, os.path.join(DR4CO_DIR, "pruebas", "montada", "body.png"))

    panel_path, gif_path = render_comparison_panel(farmer_sheet, old_sheet, v1_sheet, v2_sheet)
    print("Generados con éxito:")
    print(" -", os.path.join(FUENTE_DIR, "dr4co_3x3_llm.png"))
    print(" -", v1_path)
    print(" -", v2_path)
    print(" -", panel_path)
    print(" -", gif_path)


if __name__ == "__main__":
    main()
