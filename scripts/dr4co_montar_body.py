"""Monta un body.png de prueba: frontal dibujado a mano + lateral y trasera cúbicas automáticas.

Uso (desde la raíz del repositorio, después de scripts/dr4co_sprites_grandes.py):
    python3 -I scripts/dr4co_montar_body.py <carpeta_frontal> [alto_px]

<carpeta_frontal> contiene frontal_quieto.png, frontal_paso1.png y frontal_paso2.png
(ya reducidos a mano; sus XCF están en la misma carpeta). alto_px es el alto del frontal quieto (por defecto, el del PNG) y
fija la escala de la lateral y la trasera para que el dragón mida lo mismo en las tres vistas.

Salida en agents/Dr4co sprites/pruebas/montada/:
  body.png        hoja 96x128 con el formato de Dr4co.FashionSense/Bodies/Dr4co/body.png
  vista_x6.png    la hoja ampliada, con el punto de apoyo marcado
  caminar.gif     ciclo de paso de las cuatro filas al zoom del juego (x4)
"""
import os, sys
import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dr4co_reducir as R

OUT = os.path.join(R.ROOT, "agents", "Dr4co sprites", "pruebas", "montada")

def main():
    fdir = sys.argv[1]
    front = {}
    for pose in ("quieto", "paso1", "paso2"):
        a = np.asarray(Image.open(os.path.join(fdir, f"frontal_{pose}.png")).convert("RGBA")).copy()
        a[..., 3] = np.where(a[..., 3] >= 128, 255, 0)          # alfa todo o nada (sin halo en el juego)
        im = Image.fromarray(a, "RGBA"); front[pose] = im.crop(im.getbbox())
    height = int(sys.argv[2]) if len(sys.argv) > 2 else front["quieto"].height
    pal_fill = np.asarray(Image.open(os.path.join(R.BIG, "paleta.png")).convert("RGB"))[8, 8::16].astype(float)
    pal = np.concatenate([pal_fill, np.array([R.shade(c) for c in pal_fill])])
    f = height / Image.open(os.path.join(R.BIG, "frontal_quieto.png")).height
    small = {("frontal", p): im for p, im in front.items()}
    for view, squash in (("lateral", False), ("trasera", True)):
        for pose in ("quieto", "paso1", "paso2"):
            big = Image.open(os.path.join(R.BIG, f"{view}_{pose}.png")).convert("RGBA")
            W = max(1, round(big.width * f * (R.SQUASH if squash else 1))); H = max(1, round(big.height * f))
            a = R.snap(R.selout(R.cubica(big, W, H)), pal)
            small[(view, pose)] = Image.fromarray(a.clip(0, 255).astype(np.uint8), "RGBA")
    os.makedirs(OUT, exist_ok=True)
    sheet = Image.new("RGBA", (R.CW * 4, R.CH * 4))
    for r, (view, _) in enumerate(R.ROWS):
        for c, pose in enumerate(R.CYCLE):
            sheet.alpha_composite(R.place(small[(view, pose)], view == "lateral", r == 3), (c * R.CW, r * R.CH))
    sheet.save(os.path.join(OUT, "body.png"))
    for k, im in sorted(small.items()): print("%s_%s" % k, im.size)
    bgc = (90, 140, 80, 255)
    # vista ampliada con el punto de apoyo (x=8, y=30; en la fila izquierda x=CW-8)
    Z = 6; v = Image.new("RGBA", (R.CW * 4 * Z, R.CH * 4 * Z), bgc)
    d = ImageDraw.Draw(v)
    for r in range(4):
        for c in range(4):
            ax = (R.CW - 8 if r == 3 else 8) + c * R.CW; ay = 30 + r * R.CH
            d.ellipse(((ax - 3) * Z, (ay - 0.5) * Z, (ax + 3) * Z, (ay + 1.5) * Z), fill=(60, 95, 55, 255))
            d.rectangle((c * R.CW * Z, r * R.CH * Z, (c + 1) * R.CW * Z - 1, (r + 1) * R.CH * Z - 1), outline=(75, 120, 68, 255))
    v.alpha_composite(sheet.resize(v.size, Image.NEAREST))
    v.save(os.path.join(OUT, "vista_x6.png"))
    # GIF: frontal, derecha, trasera, izquierda (volteada como hace el juego)
    Z = 4; frames = []
    for c in range(4):
        fr = Image.new("RGBA", (4 * (R.CW * Z + 10) + 10, R.CH * Z + 20), bgc)
        for r in range(4):
            cell = sheet.crop((c * R.CW, r * R.CH, (c + 1) * R.CW, (r + 1) * R.CH))
            if r == 3: cell = cell.transpose(Image.FLIP_LEFT_RIGHT)
            fr.alpha_composite(cell.resize((R.CW * Z, R.CH * Z), Image.NEAREST), (10 + r * (R.CW * Z + 10), 10))
        frames.append(fr.convert("RGB"))
    frames[0].save(os.path.join(OUT, "caminar.gif"), save_all=True, append_images=frames[1:], duration=140, loop=0)

if __name__ == "__main__":
    main()
