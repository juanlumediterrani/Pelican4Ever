"""Ensambla el sprite del granjero de Stardew Valley a partir de los PNG extraídos del juego.

Usa la misma geometría y tablas de offsets que StardewValley.FarmerRenderer:
  - featureXOffsetPerFrame y featureYOffsetPerFrame (126 cuadros)
  - orden de capas real por dirección (cuerpo -> pantalón -> camisa -> pelo -> brazos;
    en vista trasera Up los brazos van detrás del pelo)
  - recoloreado de pantalón (peto azul clásico) y pelo (castaño clásico)

Genera dos conjuntos de muestras en agents/farmer_reference/:
  1. dr4co_template/:
     - farmer_body_1x.png (96x128, celdas 24x32, formato Fashion Sense igual que Dr4co)
     - farmer_body_4x.png (384x512, celdas 96x128, tamaño real de renderizado x4)
     - farmer_vs_dr4co_x6.png (rejilla 24x32 y sombra x=8, y=30 comparada con Dr4co)
     - farmer_vs_dr4co_caminar_4x.gif (ciclo de paso en 4 direcciones a zoom x4)
     - poses_9/ (las 9 poses únicas frontal/lateral/trasera x quieto/paso1/paso2 a 1x y 4x)
  2. assembled/:
     - farmer_full_sheet_1x.png (96x672, los 126 cuadros de 16x32 ensamblados)
     - farmer_full_sheet_4x.png (384x2688, los 126 cuadros a tamaño de pantalla 64x128)
     - farmer_full_sheet_carry_1x.png (96x672, con brazos de llevar objeto armOffset=12)
     - farmer_animations_showcase_1x.png (lámina de secuencias de animación en 4 direcciones, 1x)
     - farmer_animations_showcase_4x.png (lámina de secuencias de animación en 4 direcciones, 4x)
"""
import os
import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
REF_DIR = os.path.join(ROOT, "agents", "farmer_reference")
UNPACKED = os.path.join(REF_DIR, "unpacked")
OUT_TPL = os.path.join(REF_DIR, "dr4co_template")
OUT_ASM = os.path.join(REF_DIR, "assembled")

# Tablas exactas de StardewValley.FarmerRenderer (Stardew Valley 1.6)
FEATURE_Y_OFFSET = [
    1, 2, 2, 0, 5, 6, 1, 2, 2, 1, 0, 2, 0, 1, 1, 0, 2, 2, 3, 3, 2, 2, 1, 1,
    0, 0, 2, 2, 4, 4, 0, 0, 1, 2, 1, 1, 1, 1, 0, 0, 1, 1, 1, 0, 0, -2, -1, 1,
    1, 0, -1, -2, -1, -1, 5, 4, 0, 0, 3, 2, -1, 0, 4, 2, 0, 0, 2, 1, 0, -1, 1, -2,
    0, 0, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 1, -1, -1, -1, -1, 1, 1, 0, 0, 0, 0, 4,
    1, 0, 1, 2, 1, 0, 1, 0, 1, 2, -3, -4, -1, 0, 0, 2, 1, -4, -1, 0, 0, -3, 0, 0,
    -1, 0, 0, 2, 1, 1,
]

FEATURE_X_OFFSET = [
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    -1, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 4, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0,
    0, 0, 0, 0, 0, 0,
]

# Dirección nativa (0=Up, 1=Right, 2=Down, 3=Left) de cada uno de los 126 cuadros de farmer_base.png
FRAME_FACING = [
    # Row 0 (0..5): Down walk & tool/faint
    2, 2, 2, 2, 2, 2,
    # Row 1 (6..11): Right walk & tool/run (frame 10 is front-facing jump/stand)
    1, 1, 1, 1, 2, 1,
    # Row 2 (12..17): Up walk & tool (12..16), 17 is Right run
    0, 0, 0, 0, 0, 1,
    # Row 3 (18..23): Run (18,19 Down; 20,21 Right; 22,23 Up)
    2, 2, 1, 1, 0, 0,
    # Row 4 (24..29): Sword swipe Down
    2, 2, 2, 2, 2, 2,
    # Row 5 (30..35): Sword swipe Right
    1, 1, 1, 1, 1, 1,
    # Row 6 (36..41): Tool / Sword swipe Up
    0, 0, 0, 0, 0, 0,
    # Row 7 (42..47): Special (42 Down, 43 Right, 44 Up, 45 Right, 46-47 empty body)
    2, 1, 0, 1, 0, 0,
    # Row 8 (48..53): Tool / Fishing Right
    1, 1, 1, 1, 1, 1,
    # Row 9 (54..59): Water / Harvest / Milk Down (54..57), Right (58..59)
    2, 2, 2, 2, 1, 1,
    # Row 10 (60..65): Harvest Right (60..61), Water / Harvest / Milk Up (62..65)
    1, 1, 0, 0, 0, 0,
    # Row 11 (66..71): Tool / Fishing Down (66..70), Right (71)
    2, 2, 2, 2, 2, 1,
    # Row 12 (72..77): Fishing / Panning (72,73 Right; 74,75 Down; 76,77 Up)
    1, 1, 2, 2, 0, 0,
    # Row 13 (78..83): Shearing (78,79 Down; 80,81 Right; 82,83 Up)
    2, 2, 1, 1, 0, 0,
    # Row 14 (84..89): Eating / Holding item Down (84..88), Right (89)
    2, 2, 2, 2, 2, 1,
    # Row 15 (90..95): Drinking / Emotes Down
    2, 2, 2, 2, 2, 2,
    # Row 16 (96..101): Cheer / Harp Down (96..100), Kiss Left (101)
    2, 2, 2, 2, 2, 3,
    # Row 17 (102..107): Emotes / Sick (102..105 Down), Sitting (106 Right, 107 Down)
    2, 2, 2, 2, 1, 2,
    # Row 18 (108..113): Bathing Down (108..110), Bottle Right (111,112), Sitting Up (113)
    2, 2, 2, 1, 1, 0,
    # Row 19 (114..119): Bathing Right (114..116), Sitting Right (117), 118-119 empty body
    1, 1, 1, 1, 0, 0,
    # Row 20 (120..125): Bathing Up (120..122), Bathing Down (123..125)
    0, 0, 0, 2, 2, 2,
]

# Colores clásicos del granjero por defecto
DEFAULT_PANTS_COLOR = (46, 85, 183)    # Azul peto vaquero (a juego con los tirantes de shirt 0)
DEFAULT_HAIR_COLOR = (193, 90, 50)     # Castaño cálido clásico


def tint_rgba(img: Image.Image, rgb: tuple[int, int, int]) -> Image.Image:
    """Multiplica los canales RGB por el color dado manteniendo el alfa (como SpriteBatch.Draw)."""
    a = np.asarray(img, dtype=np.float32).copy()
    a[..., 0] = np.floor(a[..., 0] * (rgb[0] / 255.0))
    a[..., 1] = np.floor(a[..., 1] * (rgb[1] / 255.0))
    a[..., 2] = np.floor(a[..., 2] * (rgb[2] / 255.0))
    return Image.fromarray(a.clip(0, 255).astype(np.uint8), "RGBA")


class FarmerAssembler:
    def __init__(self, shirt_idx=0, pants_idx=0, hair_idx=0,
                 pants_color=DEFAULT_PANTS_COLOR, hair_color=DEFAULT_HAIR_COLOR):
        self.base = Image.open(os.path.join(UNPACKED, "farmer_base.png")).convert("RGBA")
        self.shirts = Image.open(os.path.join(UNPACKED, "shirts.png")).convert("RGBA")
        self.pants = Image.open(os.path.join(UNPACKED, "pants.png")).convert("RGBA")
        self.hairs = Image.open(os.path.join(UNPACKED, "hairstyles.png")).convert("RGBA")
        self.shirt_idx = shirt_idx
        self.pants_idx = pants_idx
        self.hair_idx = hair_idx
        self.pants_color = pants_color
        self.hair_color = hair_color

    def render_frame(self, frame: int, facing: int | None = None,
                     arm_offset: int = 6, flip: bool = False,
                     pos_offset_y: int = 0, pos_offset_x: int = 0) -> Image.Image:
        """Compone un cuadro de 16x32 siguiendo exactamente FarmerRenderer.draw."""
        if facing is None:
            facing = FRAME_FACING[frame]
        if facing in (1, 3):
            facing = 3 if flip else 1

        bathing = 108 <= frame <= 122
        fx = FEATURE_X_OFFSET[frame]
        fy = FEATURE_Y_OFFSET[frame]

        canvas = Image.new("RGBA", (16, 32), (0, 0, 0, 0))
        bx = (frame % 6) * 16
        by = (frame // 6) * 32

        # 1. Cuerpo base (16x32)
        body = self.base.crop((bx, by, bx + 16, by + 32))
        if body.getbbox() is None:
            return canvas
        if flip:
            body = body.transpose(Image.FLIP_LEFT_RIGHT)

        # 2. Pantalones (16x32)
        px = (self.pants_idx % 10) * 192 + bx
        py = (self.pants_idx // 10) * 688 + by
        pants = tint_rgba(self.pants.crop((px, py, px + 16, py + 32)), self.pants_color)
        if flip:
            pants = pants.transpose(Image.FLIP_LEFT_RIGHT)

        # 3. Camisa (8x8)
        sx = (self.shirt_idx * 8) % 128
        sy = ((self.shirt_idx * 8) // 128) * 32
        shirt_dy = {2: 0, 1: 8, 3: 16, 0: 24}[facing]
        shirt = self.shirts.crop((sx, sy + shirt_dy, sx + 8, sy + shirt_dy + 8))
        shirt_pos_x = (4 - fx) if facing == 3 else (4 + fx)
        shirt_pos_y = 14 + fy

        # 4. Pelo (16x32)
        hx = (self.hair_idx * 16) % 128
        hy = ((self.hair_idx * 16) // 128) * 96
        hair_dy = {2: 0, 1: 32, 3: 32, 0: 64}[facing]
        hair = tint_rgba(self.hairs.crop((hx, hy + hair_dy, hx + 16, hy + hair_dy + 32)), self.hair_color)
        if facing == 3:
            hair = hair.transpose(Image.FLIP_LEFT_RIGHT)
        hair_gender_y = -1 if self.hair_idx >= 16 else 0
        hair_pos_x = -fx if facing == 3 else fx
        hair_pos_y = fy + (1 if facing == 0 else 0) + hair_gender_y

        # 5. Brazos (16x32)
        arm = None
        if arm_offset > 0 and bx + arm_offset * 16 + 16 <= self.base.width:
            ax = bx + arm_offset * 16
            arm = self.base.crop((ax, by, ax + 16, by + 32))
            if flip:
                arm = arm.transpose(Image.FLIP_LEFT_RIGHT)

        # Composición en un lienzo temporal y luego desplazamiento (pos_offset_x, pos_offset_y)
        tmp = Image.new("RGBA", (16, 32), (0, 0, 0, 0))
        tmp.alpha_composite(body, (0, 0))
        if frame != 5:
            tmp.alpha_composite(pants, (0, 0))
        if not bathing:
            tmp.alpha_composite(shirt, (shirt_pos_x, shirt_pos_y))

        # En vista trasera (facing == 0), el brazo (capa 8) va DEBAJO del pelo (capa 10)
        if facing == 0 and arm is not None:
            tmp.alpha_composite(arm, (0, 0))

        tmp.alpha_composite(hair, (hair_pos_x, hair_pos_y))

        if facing != 0 and arm is not None:
            tmp.alpha_composite(arm, (0, 0))
        if frame == 5:
            tmp.alpha_composite(pants, (0, 0))

        canvas.alpha_composite(tmp, (pos_offset_x, pos_offset_y))
        return canvas


def scale_nn(img: Image.Image, factor: int) -> Image.Image:
    return img.resize((img.width * factor, img.height * factor), Image.NEAREST)


def build_dr4co_template(asm: FarmerAssembler):
    """Genera las plantillas 4x4 (celdas 24x32) y las 9 poses únicas equivalentes al flujo de Dr4co."""
    os.makedirs(OUT_TPL, exist_ok=True)
    poses_dir = os.path.join(OUT_TPL, "poses_9")
    os.makedirs(poses_dir, exist_ok=True)

    # En Stardew Valley, el marco de 16x32 tiene su esquina superior izquierda en (0, 0) de la celda 24x32
    # de Fashion Sense (por eso la sombra del granjero cae en x=8, y=30).
    # En la fila izquierda (r=3), el juego voltea toda la celda de 24x32, por lo que para que al voltear
    # quede centrada en x=8, antes de voltear debe ir en x = 24 - 16 = 8 (y sin espejar, como en Dr4co).
    cw, ch = 24, 32

    # Las 9 poses únicas (3 vistas x 3 estados del paso)
    unique_poses = {
        ("frontal", "quieto"): asm.render_frame(0, facing=2, arm_offset=6, flip=False),
        ("frontal", "paso1"):  asm.render_frame(1, facing=2, arm_offset=6, flip=False),
        ("frontal", "paso2"):  asm.render_frame(2, facing=2, arm_offset=6, flip=False),
        ("lateral", "quieto"): asm.render_frame(6, facing=1, arm_offset=6, flip=False),
        ("lateral", "paso1"):  asm.render_frame(7, facing=1, arm_offset=6, flip=False),
        ("lateral", "paso2"):  asm.render_frame(8, facing=1, arm_offset=6, flip=False),
        ("trasera", "quieto"): asm.render_frame(12, facing=0, arm_offset=6, flip=False),
        ("trasera", "paso1"):  asm.render_frame(13, facing=0, arm_offset=6, flip=False),
        ("trasera", "paso2"):  asm.render_frame(14, facing=0, arm_offset=6, flip=False),
    }

    for (view, pose), im1x in unique_poses.items():
        im1x.save(os.path.join(poses_dir, f"{view}_{pose}_1x.png"))
        scale_nn(im1x, 4).save(os.path.join(poses_dir, f"{view}_{pose}_4x.png"))

    # Hoja 4x4 (96x128 a 1x) con la misma geometría que Bodies/Dr4co/body.png
    rows = ["frontal", "lateral", "trasera", "lateral"]
    cycle = ["quieto", "paso1", "quieto", "paso2"]
    sheet_1x = Image.new("RGBA", (cw * 4, ch * 4), (0, 0, 0, 0))
    for r, view in enumerate(rows):
        for c, pose in enumerate(cycle):
            ox = (cw - 16) if r == 3 else 0
            sheet_1x.alpha_composite(unique_poses[(view, pose)], (c * cw + ox, r * ch))

    sheet_1x.save(os.path.join(OUT_TPL, "farmer_body_1x.png"))
    scale_nn(sheet_1x, 4).save(os.path.join(OUT_TPL, "farmer_body_4x.png"))

    # Vista ampliada x6 comparando el Granjero Vanilla con Dr4co sobre la sombra (x=8, y=30)
    dr4co_path = os.path.join(ROOT, "agents", "Dr4co.FashionSense", "Bodies", "Dr4co", "body.png")
    dr4co_1x = Image.open(dr4co_path).convert("RGBA") if os.path.exists(dr4co_path) else None

    z = 6
    bgc = (90, 140, 80, 255)
    grid_w = cw * 4 * z
    grid_h = ch * 4 * z
    total_w = grid_w * 2 + 36 if dr4co_1x else grid_w
    cmp_img = Image.new("RGBA", (total_w, grid_h + 28), bgc)
    d = ImageDraw.Draw(cmp_img)

    panels = [("Granjero Vanilla (1x -> x6)", sheet_1x, 0)]
    if dr4co_1x:
        panels.append(("Dr4co FashionSense (1x -> x6)", dr4co_1x, grid_w + 36))

    for title, sh, x0 in panels:
        d.text((x0 + 8, 6), title, fill=(255, 255, 255, 255))
        panel = Image.new("RGBA", (grid_w, grid_h), bgc)
        pd = ImageDraw.Draw(panel)
        for r in range(4):
            for c in range(4):
                ax = (cw - 8 if r == 3 else 8) + c * cw
                ay = 30 + r * ch
                pd.ellipse(
                    ((ax - 3) * z, (ay - 0.5) * z, (ax + 3) * z, (ay + 1.5) * z),
                    fill=(60, 95, 55, 255),
                )
                pd.rectangle(
                    (c * cw * z, r * ch * z, (c + 1) * cw * z - 1, (r + 1) * ch * z - 1),
                    outline=(75, 120, 68, 255),
                )
        panel.alpha_composite(scale_nn(sh, z), (0, 0))
        cmp_img.alpha_composite(panel, (x0, 28))

    cmp_img.save(os.path.join(OUT_TPL, "farmer_vs_dr4co_x6.png"))

    # GIF animado a 4x (tamaño real de renderizado del juego)
    z_gif = 4
    frames = []
    rows_count = 2 if dr4co_1x else 1
    for c in range(4):
        fr = Image.new("RGBA", (4 * (cw * z_gif + 10) + 10, rows_count * (ch * z_gif + 16) + 10), bgc)
        for idx, sh in enumerate([sheet_1x] + ([dr4co_1x] if dr4co_1x else [])):
            y0 = 10 + idx * (ch * z_gif + 16)
            for r in range(4):
                cell = sh.crop((c * cw, r * ch, (c + 1) * cw, (r + 1) * ch))
                if r == 3:
                    cell = cell.transpose(Image.FLIP_LEFT_RIGHT)
                fr.alpha_composite(scale_nn(cell, z_gif), (10 + r * (cw * z_gif + 10), y0))
        frames.append(fr.convert("RGB"))
    frames[0].save(
        os.path.join(OUT_TPL, "farmer_vs_dr4co_caminar_4x.gif"),
        save_all=True,
        append_images=frames[1:],
        duration=160,
        loop=0,
    )


def build_assembled_sheets(asm: FarmerAssembler):
    """Genera la sábana completa de 126 cuadros y el showcase de animaciones en 1x y 4x."""
    os.makedirs(OUT_ASM, exist_ok=True)

    # 1. Hoja completa de 126 cuadros (6 columnas x 21 filas = 96x672 a 1x)
    for suffix, arm_off in (("1x", 6), ("carry_1x", 12)):
        full = Image.new("RGBA", (96, 672), (0, 0, 0, 0))
        for f in range(126):
            cell = asm.render_frame(f, facing=FRAME_FACING[f], arm_offset=arm_off, flip=False)
            full.alpha_composite(cell, ((f % 6) * 16, (f // 6) * 32))
        full.save(os.path.join(OUT_ASM, f"farmer_full_sheet_{suffix}.png"))
        if suffix == "1x":
            scale_nn(full, 4).save(os.path.join(OUT_ASM, "farmer_full_sheet_4x.png"))

    # 2. Showcase organizado por animaciones reales (incluyendo las 4 direcciones con Left volteado)
    # Cada entrada: (etiqueta, [(frame, facing, arm_offset, flip), ...])
    sequences = [
        ("Walk Down",       [(0, 2, 6, False), (1, 2, 6, False), (0, 2, 6, False), (2, 2, 6, False)]),
        ("Walk Right",      [(6, 1, 6, False), (7, 1, 6, False), (6, 1, 6, False), (8, 1, 6, False)]),
        ("Walk Up",         [(12, 0, 6, False), (13, 0, 6, False), (12, 0, 6, False), (14, 0, 6, False)]),
        ("Walk Left",       [(6, 3, 6, True),  (7, 3, 6, True),  (6, 3, 6, True),  (8, 3, 6, True)]),
        ("Run Down/R/Up/L", [(18, 2, 6, False), (19, 2, 6, False), (20, 1, 6, False), (21, 1, 6, False),
                             (22, 0, 6, False), (23, 0, 6, False), (20, 3, 6, True),  (21, 3, 6, True)]),
        ("Carry Walk 4-Dir",[(0, 2, 12, False), (1, 2, 12, False), (6, 1, 12, False), (7, 1, 12, False),
                             (12, 0, 12, False), (13, 0, 12, False), (6, 3, 12, True), (7, 3, 12, True)]),
        ("Tool Swing Down", [(66, 2, 6, False), (67, 2, 6, False), (68, 2, 6, False), (69, 2, 6, False), (70, 2, 6, False)]),
        ("Tool Swing Right",[(48, 1, 6, False), (49, 1, 6, False), (50, 1, 6, False), (51, 1, 6, False), (52, 1, 6, False)]),
        ("Tool Swing Up",   [(36, 0, 6, False), (37, 0, 6, False), (38, 0, 6, False), (39, 0, 6, False), (40, 0, 6, False)]),
        ("Tool Swing Left", [(48, 3, 6, True),  (49, 3, 6, True),  (50, 3, 6, True),  (51, 3, 6, True),  (52, 3, 6, True)]),
        ("Water / Harvest", [(54, 2, 6, False), (55, 2, 6, False), (58, 1, 6, False), (59, 1, 6, False),
                             (62, 0, 6, False), (63, 0, 6, False), (58, 3, 6, True),  (59, 3, 6, True)]),
        ("Sword Swipe D/R", [(24, 2, 6, False), (25, 2, 6, False), (26, 2, 6, False), (27, 2, 6, False),
                             (30, 1, 6, False), (31, 1, 6, False), (32, 1, 6, False), (33, 1, 6, False)]),
        ("Shear / Eat / Sit",[(78, 2, 6, False), (80, 1, 6, False), (82, 0, 6, False), (84, 2, 6, False),
                              (85, 2, 6, False), (90, 2, 6, False), (107, 2, 6, False), (106, 1, 6, False)]),
    ]

    max_cols = max(len(seq) for _, seq in sequences)
    rows_n = len(sequences)

    # Showcase 1x puro (sin texto, celdas exactas de 16x32 para poder recortar directamente)
    sc_1x = Image.new("RGBA", (max_cols * 16, rows_n * 32), (0, 0, 0, 0))
    for r, (_, seq) in enumerate(sequences):
        for c, (f, facing, arm_off, flip) in enumerate(seq):
            cell = asm.render_frame(f, facing=facing, arm_offset=arm_off, flip=flip)
            sc_1x.alpha_composite(cell, (c * 16, r * 32))
    sc_1x.save(os.path.join(OUT_ASM, "farmer_animations_showcase_1x.png"))

    # Showcase 4x (tamaño real en pantalla: cada celda 64x128 px, con etiquetas y rejilla)
    label_w = 150
    cell_w4, cell_h4 = 64, 128
    bgc = (48, 56, 64, 255)
    sc_4x = Image.new("RGBA", (label_w + max_cols * cell_w4, rows_n * cell_h4), bgc)
    d = ImageDraw.Draw(sc_4x)
    for r, (label, seq) in enumerate(sequences):
        y0 = r * cell_h4
        d.line([(0, y0), (sc_4x.width, y0)], fill=(70, 82, 94, 255), width=1)
        d.text((10, y0 + 54), label, fill=(235, 240, 245, 255))
        for c, (f, facing, arm_off, flip) in enumerate(seq):
            x0 = label_w + c * cell_w4
            d.rectangle([x0, y0, x0 + cell_w4 - 1, y0 + cell_h4 - 1], outline=(62, 72, 82, 255))
            cell = asm.render_frame(f, facing=facing, arm_offset=arm_off, flip=flip)
            sc_4x.alpha_composite(scale_nn(cell, 4), (x0, y0))
            d.text((x0 + 4, y0 + 4), f"f{f}{'*' if flip else ''}", fill=(160, 175, 190, 255))

    sc_4x.save(os.path.join(OUT_ASM, "farmer_animations_showcase_4x.png"))


def main():
    asm = FarmerAssembler()
    build_dr4co_template(asm)
    build_assembled_sheets(asm)
    print("OK: referencias generadas en", REF_DIR)


if __name__ == "__main__":
    main()
