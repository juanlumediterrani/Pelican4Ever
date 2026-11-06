"""Recorta y limpia las poses útiles de la hoja de Dr4co generada por IA.

Uso (desde la raíz del repositorio):
    python3 -I scripts/dr4co_sprites_grandes.py

Entrada: agents/Dr4co sprites/fuente/dr4co_hoja_ia.jpg (2000x1493, fondo blanco).
Salida:  agents/Dr4co sprites/grandes/*.png (RGBA, alfa binaria, paleta compartida)
         agents/Dr4co sprites/grandes/paleta.png (los colores de relleno compartidos)

Qué hace con cada pose:
  1. recorta la figura con margen;
  2. quita el fondo blanco por relleno desde el borde (el crema interior no se toca);
  3. elimina el halo claro del JPG en el borde y las motas sueltas;
  4. filtro de mediana para el ruido de compresión;
  5. cuantiza todas las poses a la misma paleta (k-means) y limpia motas con filtro de moda;
  6. normaliza la escala (la última fila de la hoja está dibujada ~9 % más grande).

Poses elegidas (fila, columna de la mitad izquierda de la hoja):
  frontal: 00 quieto, 01 y 02 paso (00 tiene el ala izquierda cortada por el borde
           de la imagen: se reconstruye espejando la derecha);
  lateral (mirando a la derecha): 31 quieto, 32 y 12 paso;
  trasera: 21 quieto, 22 paso; el otro paso es 22 espejado.
Descartadas: 10 (hocico cortado y vista 3/4), 11 (3/4 trasera, no casa con el resto),
20 y 30 (repiten el frontal quieto). Las poses con herramienta no sirven: el juego
dibuja la herramienta por su cuenta.
"""
import os, sys
import numpy as np
from PIL import Image, ImageFilter

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SRC = os.path.join(ROOT, "agents", "Dr4co sprites", "fuente", "dr4co_hoja_ia.jpg")
OUT = os.path.join(ROOT, "agents", "Dr4co sprites", "grandes")
NCOL = 20          # colores de relleno de la paleta compartida
MARGIN = 6

# nombre: (caja x0, y0, x1, y1 en la hoja, escala, espejar)
ROW3 = 278 / 305   # la fila 3 está dibujada más grande que las demás
POSES = {
    "frontal_quieto": ((0, 11, 152, 289), 1.0, False),
    "frontal_paso1":  ((260, 11, 429, 289), 1.0, False),
    "frontal_paso2":  ((544, 11, 713, 289), 1.0, False),
    "lateral_quieto": ((211, 1188, 394, 1493), ROW3, False),
    "lateral_paso1":  ((512, 1188, 707, 1493), ROW3, False),
    "lateral_paso2":  ((530, 407, 705, 693), 1.0, False),
    "trasera_quieto": ((259, 807, 400, 1085), 1.0, False),
    "trasera_paso1":  ((548, 807, 689, 1085), 1.0, False),
}

def is_bg(a):
    return (a.min(2) > 215) & (a.max(2) - a.min(2) < 30)

def flood(seed, allowed):
    """Relleno por dilatación restringida (sin scipy)."""
    cur = seed & allowed
    while True:
        p = np.pad(cur, 1)
        nxt = (p[1:-1, 1:-1] | p[:-2, 1:-1] | p[2:, 1:-1] | p[1:-1, :-2] | p[1:-1, 2:]) & allowed
        if (nxt == cur).all():
            return cur
        cur = nxt

def lum(c):
    return c[..., 0] * .3 + c[..., 1] * .59 + c[..., 2] * .11

def cut(sheet, box):
    x0, y0, x1, y1 = box
    H, W = sheet.shape[:2]
    x0, y0, x1, y1 = max(0, x0 - MARGIN), max(0, y0 - MARGIN), min(W, x1 + MARGIN), min(H, y1 + MARGIN)
    rgb = sheet[y0:y1, x0:x1].copy()
    cand = is_bg(rgb)
    border = np.zeros_like(cand); border[0, :] = border[-1, :] = border[:, 0] = border[:, -1] = True
    bg = flood(border, cand)
    fg = ~bg
    # halo: píxeles claros mezclados con el blanco pegados al fondo
    # (el contorno de la figura es oscuro: todo lo claro pegado al fondo es mezcla con el blanco)
    for _ in range(4):
        p = np.pad(bg, 1)
        touch = p[:-2, 1:-1] | p[2:, 1:-1] | p[1:-1, :-2] | p[1:-1, 2:]
        halo = fg & touch & (lum(rgb.astype(float)) > 115)
        fg &= ~halo; bg |= halo
    # quedarse con la figura principal (la componente más grande, sembrada en su centro de masas)
    ys, xs = np.nonzero(fg)
    seed = np.zeros_like(fg)
    cy, cx = int(np.median(ys)), int(np.median(xs))
    k = np.argmin((ys - cy) ** 2 + (xs - cx) ** 2); seed[ys[k], xs[k]] = True
    fg = flood(seed, fg)
    return rgb, fg, (x0, x1, W)

def fix_clipped_left(rgb, fg):
    """El frontal quieto toca el borde izquierdo de la hoja: completa ese lado espejando."""
    W = fg.shape[1]
    best, axis = -1, W / 2
    for ax2 in range(int(W * 0.8), int(W * 1.2)):       # eje*2, busca la mejor simetría
        m = np.zeros_like(fg)
        for x in range(W):
            xm = ax2 - x
            if 0 <= xm < W: m[:, x] = fg[:, xm]
        score = (m & fg).sum() - (m ^ fg)[:, W // 4:].sum()
        if score > best: best, axis = score, ax2
    pad = max(0, axis - (W - 1))                       # columnas que faltan a la izquierda
    pad = int(np.ceil(pad)) + 2
    H = fg.shape[0]
    rgb2 = np.full((H, W + pad, 3), 255, np.uint8); rgb2[:, pad:] = rgb
    fg2 = np.zeros((H, W + pad), bool); fg2[:, pad:] = fg
    ax2 = axis + 2 * pad
    clip_x = pad + 3                                   # solo se rellena a la izquierda del corte
    for x in range(clip_x):
        xm = ax2 - x
        if 0 <= xm < W + pad:
            fg2[:, x] = fg2[:, xm]; rgb2[:, x] = rgb2[:, xm]
    return rgb2, fg2

def kmeans(px, k, it=20, seed=0):
    rng = np.random.default_rng(seed)
    c = px[rng.choice(len(px), k, replace=False)].astype(float)
    for _ in range(it):
        lab = ((px[:, None] - c[None]) ** 2).sum(-1).argmin(1)
        for q in range(k):
            if (lab == q).any(): c[q] = px[lab == q].mean(0)
    return c

def main():
    os.makedirs(OUT, exist_ok=True)
    sheet = np.asarray(Image.open(SRC).convert("RGB"))
    figs = {}
    for name, (box, scale, mirror) in POSES.items():
        rgb, fg, (x0, x1, W) = cut(sheet, box)
        if name == "frontal_quieto":
            rgb, fg = fix_clipped_left(rgb, fg)
        rgb = np.asarray(Image.fromarray(rgb).filter(ImageFilter.MedianFilter(5)))
        ys, xs = np.nonzero(fg)
        rgb, fg = rgb[ys.min():ys.max() + 1, xs.min():xs.max() + 1], fg[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
        if scale != 1.0:
            w, h = round(fg.shape[1] * scale), round(fg.shape[0] * scale)
            rgb = np.asarray(Image.fromarray(rgb).resize((w, h), Image.LANCZOS))
            fg = np.asarray(Image.fromarray(fg.astype(np.uint8) * 255).resize((w, h), Image.LANCZOS)) > 127
        figs[name] = (rgb, fg)
    # paleta compartida
    samples = np.concatenate([r[f][::7] for r, f in figs.values()]).astype(float)
    pal = kmeans(samples, NCOL)
    pal = pal[np.argsort(lum(pal))]
    for name, (rgb, fg) in figs.items():
        idx = ((rgb.reshape(-1, 1, 3).astype(float) - pal[None]) ** 2).sum(-1).argmin(1).reshape(fg.shape)
        idx[~fg] = 0   # fuera de la figura: el tono de contorno, para que el filtro de moda no meta blanco en el borde
        idx_img = Image.fromarray(idx.astype(np.uint8), "L").filter(ImageFilter.ModeFilter(5))
        idx = np.asarray(idx_img)
        out = np.zeros(fg.shape + (4,), np.uint8)
        out[..., :3] = pal[idx].round().astype(np.uint8); out[..., 3] = np.where(fg, 255, 0)
        Image.fromarray(out, "RGBA").save(os.path.join(OUT, name + ".png"))
        if name == "trasera_paso1":
            Image.fromarray(out[:, ::-1].copy(), "RGBA").save(os.path.join(OUT, "trasera_paso2.png"))
        print(f"{name}: {fg.shape[1]}x{fg.shape[0]}")
    sw = Image.new("RGB", (NCOL * 16, 16))
    for i, c in enumerate(pal.round().astype(np.uint8)):
        sw.paste(tuple(int(v) for v in c), (i * 16, 0, i * 16 + 16, 16))
    sw.save(os.path.join(OUT, "paleta.png"))

if __name__ == "__main__":
    main()
