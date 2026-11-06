"""Reduce las versiones grandes de Dr4co a tamaño de juego con dos métodos y compara.

Uso (desde la raíz del repositorio, después de scripts/dr4co_sprites_grandes.py):
    python3 -I scripts/dr4co_reducir.py [alto_px]

Métodos (los dos terminan con contorno coloreado "sel-out" y la misma paleta fija):
  kcentroid: cada píxel final es el color dominante (k-means, k=3) de su bloque;
  cubica:    reducción bicúbica con alfa premultiplicado (equivalente a la cúbica de GIMP).

Salida en agents/Dr4co sprites/pruebas/:
  <metodo>/<pose>.png            cada pose reducida
  <metodo>/body.png              hoja 96x128 con el mismo formato que
                                 Dr4co.FashionSense/Bodies/Dr4co/body.png (para probar en el juego)
  comparativa.png                las dos hojas ampliadas x6, una encima de otra
  caminar.gif                    ciclo de paso de las tres direcciones, los dos métodos

Geometría como en build_dr4co_body.py: celdas de 24x32, pies en la última fila,
frontal/trasera estrechadas un 15 %, fila izquierda = lateral sin espejar.
Los ojos no sobreviven a la reducción: hay que repintarlos a mano.
"""
import colorsys, os, sys
import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
BIG = os.path.join(ROOT, "agents", "Dr4co sprites", "grandes")
OUT = os.path.join(ROOT, "agents", "Dr4co sprites", "pruebas")
CW, CH = 24, 32
SQUASH = 0.85
ROWS = [("frontal", True), ("lateral", False), ("trasera", True), ("lateral", False)]  # 4ª fila = izquierda
CYCLE = ["quieto", "paso1", "quieto", "paso2"]

def lum(c): return c[..., 0] * .3 + c[..., 1] * .59 + c[..., 2] * .11

# ---------- reducción ----------
def kcentroid(img, W, H, k=3, it=8):
    a = np.asarray(img).astype(np.float32); h, w = a.shape[:2]
    ys = np.linspace(0, h, H + 1).round().astype(int); xs = np.linspace(0, w, W + 1).round().astype(int)
    out = np.zeros((H, W, 4), np.float32)
    for j in range(H):
        for i in range(W):
            px = a[ys[j]:ys[j + 1], xs[i]:xs[i + 1]].reshape(-1, 4)
            op = px[px[:, 3] > 128]
            if len(op) < len(px) * 0.5: continue
            c = op[np.linspace(0, len(op) - 1, k).astype(int), :3].copy()
            for _ in range(it):
                lab = ((op[:, None, :3] - c[None]) ** 2).sum(-1).argmin(1)
                for q in range(k):
                    if (lab == q).any(): c[q] = op[lab == q, :3].mean(0)
            out[j, i] = np.r_[c[np.bincount(lab, minlength=k).argmax()], 255]
    return out

def cubica(img, W, H):
    r = img.convert("RGBa").resize((W, H), Image.BICUBIC).convert("RGBA")
    a = np.asarray(r).astype(np.float32).copy(); a[..., 3] = np.where(a[..., 3] >= 128, 255, 0)
    return a

# ---------- contorno coloreado ----------
def shade(rgb, v=0.5, s=1.25, hshift=-0.015):
    h, ss, vv = colorsys.rgb_to_hsv(*(np.asarray(rgb, float) / 255))
    if ss < 0.45 and vv > 0.6:            # claros (vientre, cuernos): marrón cálido
        h, ss, vv = 0.04, 0.65, vv * 0.5
    else:
        h, ss, vv = (h + hshift) % 1, min(1, ss * s), vv * v
    return np.array(colorsys.hsv_to_rgb(h, ss, vv)) * 255

def selout(a, dark_thr=60):
    op = a[..., 3] > 0; dark = op & (lum(a) < dark_thr)
    p = np.pad(op, 1); edge = op & ~(p[:-2, 1:-1] & p[2:, 1:-1] & p[1:-1, :-2] & p[1:-1, 2:])
    target = dark | edge; out = a.copy()
    for y, x in zip(*np.nonzero(target)):
        best = None
        for r in (1, 2, 3):
            ys, xs = slice(max(0, y - r), y + r + 1), slice(max(0, x - r), x + r + 1)
            m = op[ys, xs] & ~target[ys, xs]
            if m.any():
                cols = a[ys, xs][m][:, :3]; best = cols[lum(cols).argmin()]; break
        out[y, x, :3] = shade(best if best is not None else a[y, x, :3])
    return out

def snap(a, pal):
    rgb = a[..., :3].reshape(-1, 1, 3)
    idx = ((rgb - pal[None]) ** 2).sum(-1).argmin(1)
    out = a.copy(); out[..., :3] = pal[idx].reshape(a.shape[:2] + (3,)); return out

# ---------- montaje ----------
FOOTX = 9.5   # centro de los pies, medido en el juego (ver build_dr4co_body.py)

def place(cell_img, side, left=False):
    """Pies en la última fila. Frontal/trasera: centro de la caja en FOOTX.
    Lateral: centro de los pies en FOOTX; en la fila izquierda en CW-FOOTX-2
    (el juego voltea la celda; 2 px hacia la cola para no cortar el hocico)."""
    cell = Image.new("RGBA", (CW, CH)); a = np.asarray(cell_img)
    w, h = cell_img.size
    if side:
        feet = np.nonzero(a[-max(2, h // 8):, :, 3].any(0))[0]
        fx = (feet.min() + feet.max() + 1) / 2 if len(feet) else w / 2
    else:
        fx = w / 2   # coordenadas continuas, como en build_dr4co_body.py
    target = CW - FOOTX - 2 if left else FOOTX
    x = int(round(target - fx))
    cell.alpha_composite(cell_img, (min(max(0, x), CW - w), CH - h))
    return cell

def main():
    pal_fill = np.asarray(Image.open(os.path.join(BIG, "paleta.png")).convert("RGB"))[8, 8::16].astype(float)
    pal = np.concatenate([pal_fill, np.array([shade(c) for c in pal_fill])])
    height = int(sys.argv[1]) if len(sys.argv) > 1 else 26   # alto del frontal quieto en el juego
    ref = Image.open(os.path.join(BIG, "frontal_quieto.png"))
    f = height / ref.height
    sheets = {}
    for method, fn in (("kcentroid", kcentroid), ("cubica", cubica)):
        os.makedirs(os.path.join(OUT, method), exist_ok=True)
        small = {}
        for view, squash in ROWS[:3]:
            for pose in ("quieto", "paso1", "paso2"):
                big = Image.open(os.path.join(BIG, f"{view}_{pose}.png")).convert("RGBA")
                W = max(1, round(big.width * f * (SQUASH if squash else 1))); H = max(1, round(big.height * f))
                a = snap(selout(fn(big, W, H)), pal)
                im = Image.fromarray(a.clip(0, 255).astype(np.uint8), "RGBA")
                im.save(os.path.join(OUT, method, f"{view}_{pose}.png")); small[(view, pose)] = im
        sheet = Image.new("RGBA", (CW * 4, CH * 4))
        for r, (view, _) in enumerate(ROWS):
            for c, pose in enumerate(CYCLE):
                sheet.alpha_composite(place(small[(view, pose)], view == "lateral", r == 3), (c * CW, r * CH))
        sheet.save(os.path.join(OUT, method, "body.png")); sheets[method] = sheet
        print(method, {f"{v}_{p}": small[(v, p)].size for v, p in small})
    # comparativa estática
    Z = 6; bgc = (90, 140, 80, 255)
    cmp_ = Image.new("RGBA", (CW * 4 * Z + 40, (CH * 4 * Z + 40) * 2), bgc); d = ImageDraw.Draw(cmp_)
    for i, (m, sh) in enumerate(sheets.items()):
        y = i * (CH * 4 * Z + 40)
        d.text((20, y + 12), m + " + sel-out", fill="white")
        cmp_.alpha_composite(sh.resize((CW * 4 * Z, CH * 4 * Z), Image.NEAREST), (20, y + 30))
    cmp_.save(os.path.join(OUT, "comparativa.png"))
    # GIF del ciclo de paso: filas = métodos, columnas = frontal/lateral/trasera, a x4 (zoom del juego)
    Z = 4; frames = []
    for c in range(4):
        fr = Image.new("RGBA", (3 * CW * Z + 40, 2 * CH * Z + 30), bgc); d = ImageDraw.Draw(fr)
        for i, (m, sh) in enumerate(sheets.items()):
            d.text((4, i * (CH * Z + 15) + 2), m, fill="white")
            for r in range(3):
                cell = sh.crop((c * CW, r * CH, (c + 1) * CW, (r + 1) * CH)).resize((CW * Z, CH * Z), Image.NEAREST)
                fr.alpha_composite(cell, (10 + r * (CW * Z + 10), i * (CH * Z + 15) + 12))
        frames.append(fr.convert("RGB"))
    frames[0].save(os.path.join(OUT, "caminar.gif"), save_all=True, append_images=frames[1:], duration=140, loop=0)

if __name__ == "__main__":
    main()
