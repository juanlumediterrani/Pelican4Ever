#!/usr/bin/env python3
"""
Procesamiento del retrato de diálogo (Portrait) de Dr4co para Stardew Valley / Fashion Sense.

Toma el retrato generado por IA (`dr4co_portrait_llm.png`), elimina el fondo blanco,
centra el busto y lo reduce al tamaño nativo de Stardew Valley (64x64 píxeles) y al zoom 4x de pantalla (256x256),
cuantizando con la paleta fija de 12 colores de Dr4co.
"""
import os
import shutil
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC_PORTRAIT = os.path.join(ROOT, "agents", "Dr4co sprites", "fuente", "dr4co_portrait_llm.png")
OUT_PORTRAIT_DIR = os.path.join(ROOT, "agents", "Dr4co sprites", "portrait")
FS_BODY_DIR = os.path.join(ROOT, "agents", "Dr4co.FashionSense", "Bodies", "Dr4co")

os.makedirs(OUT_PORTRAIT_DIR, exist_ok=True)
os.makedirs(FS_BODY_DIR, exist_ok=True)

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


def process_portrait():
    im = Image.open(SRC_PORTRAIT).convert("RGBA")
    arr = np.array(im)

    # Limpiar fondo blanco
    rgb = arr[:, :, :3].astype(np.int16)
    bg = (rgb[:, :, 0] > 230) & (rgb[:, :, 1] > 230) & (rgb[:, :, 2] > 230)
    arr[bg] = [0, 0, 0, 0]

    ys, xs = np.where(arr[:, :, 3] > 0)
    crop = Image.fromarray(arr[ys.min():ys.max() + 1, xs.min():xs.max() + 1])

    # Reducir a 60px de alto para centrar en caja de 64x64 con margen de 2px
    target_h = 60
    tw = max(1, round(crop.width * target_h / crop.height))
    small = crop.resize((tw, target_h), Image.BOX)

    # Cuantizar a la paleta de Dr4co
    s_arr = np.array(small)
    alpha = s_arr[:, :, 3] > 110
    s_rgb = s_arr[:, :, :3].astype(np.int16)

    q_arr = np.zeros((small.height, small.width, 4), dtype=np.uint8)
    ys_s, xs_s = np.where(alpha)

    red_pal = PALETTE[1:6].astype(np.int16)
    cream_pal = PALETTE[6:9].astype(np.int16)

    for y, x in zip(ys_s, xs_s):
        r, g, b = int(s_rgb[y, x, 0]), int(s_rgb[y, x, 1]), int(s_rgb[y, x, 2])
        pix = np.array([r, g, b], dtype=np.int16)
        is_cream = (g >= 120 and b >= 80 and (r - g) < 65)
        if is_cream:
            d = ((cream_pal - pix) ** 2).sum(axis=1)
            q_arr[y, x, :3] = PALETTE[6 + d.argmin()]
        else:
            d = ((red_pal - pix) ** 2).sum(axis=1)
            q_arr[y, x, :3] = PALETTE[1 + d.argmin()]
        q_arr[y, x, 3] = 255

    # Ojos amarillos con pupilas
    for y, x in zip(ys_s, xs_s):
        r, g, b = int(s_rgb[y, x, 0]), int(s_rgb[y, x, 1]), int(s_rgb[y, x, 2])
        if r > 160 and g > 110 and b < 80 and (g - b) > 40:
            q_arr[y, x, :3] = PALETTE[9] if g > 150 else PALETTE[10]

    # Contorno sel-out
    interior = np.zeros((small.height, small.width), dtype=bool)
    for y in range(1, small.height - 1):
        for x in range(1, small.width - 1):
            if alpha[y, x] and alpha[y - 1, x] and alpha[y + 1, x] and alpha[y, x - 1] and alpha[y, x + 1]:
                interior[y, x] = True
    border = alpha & (~interior)

    for y, x in zip(*np.where(interior)):
        c = q_arr[y, x, :3]
        if np.array_equal(c, PALETTE[0]) or np.array_equal(c, PALETTE[11]):
            # Mantener pupila
            is_pupil = False
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    ny, nx = y + dy, x + dx
                    if 0 <= ny < small.height and 0 <= nx < small.width and np.array_equal(q_arr[ny, nx, :3], PALETTE[9]):
                        is_pupil = True
            if not is_pupil:
                q_arr[y, x, :3] = PALETTE[1]

    for y, x in zip(*np.where(border)):
        c = q_arr[y, x, :3]
        if np.array_equal(c, PALETTE[7]) or np.array_equal(c, PALETTE[8]):
            q_arr[y, x, :3] = PALETTE[6]
        else:
            q_arr[y, x, :3] = PALETTE[0]

    portrait_spr = Image.fromarray(q_arr)

    # Lienzo 64x64 estándar de Stardew Valley con fondo cálido de diálogo
    canvas_64 = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    # Centrar horizontalmente y anclar a la base
    ox = (64 - portrait_spr.width) // 2
    oy = 64 - portrait_spr.height
    canvas_64.alpha_composite(portrait_spr, (ox, oy))

    p64_path = os.path.join(OUT_PORTRAIT_DIR, "dr4co_portrait_64x64.png")
    p256_path = os.path.join(OUT_PORTRAIT_DIR, "dr4co_portrait_256x256.png")
    canvas_64.save(p64_path)

    # Versión a tamaño 4x (256x256) con fondo de cuadro de diálogo
    dialog_bg = Image.new("RGBA", (256, 256), (228, 172, 108, 255))
    d = ImageDraw.Draw(dialog_bg)
    # Marco de madera estilo SDV
    d.rectangle([4, 4, 251, 251], outline=(112, 54, 24, 255), width=4)
    d.rectangle([8, 8, 247, 247], fill=(244, 214, 168, 255))
    up_portrait = canvas_64.resize((256, 256), Image.NEAREST)
    dialog_bg.alpha_composite(up_portrait, (0, 0))
    dialog_bg.save(p256_path)

    # Copiar también al pack de Fashion Sense
    shutil.copyfile(p64_path, os.path.join(FS_BODY_DIR, "portrait.png"))

    print("Retrato de Dr4co procesado:")
    print(" - 64x64 (nativo SDV):", p64_path)
    print(" - 256x256 (diálogo 4x):", p256_path)
    print(" - Copiado a Fashion Sense:", os.path.join(FS_BODY_DIR, "portrait.png"))


if __name__ == "__main__":
    process_portrait()
