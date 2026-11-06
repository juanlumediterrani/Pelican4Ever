"""Genera Bodies/Dr4co/body.png a partir de agents/sheets/dr4co-sid-v2.png usando GIMP (gimp-console, Script-Fu).

Uso (desde agents/Dr4co.FashionSense):
    python3 ../../scripts/build_dr4co_body.py Bodies/Dr4co/body.png [n_colores]

Este script solo calcula la geometria; el tratamiento de imagen (recorte, escalado NoHalo,
umbral de alfa, paleta indexada) lo hace GIMP.

Geometria medida en el juego con una hoja de calibracion (Fashion Sense 7.5.0):
  * Fashion Sense ignora Scale; 1 px de arte = 1 px del juego;
  * la esquina superior izquierda de la celda va sobre la del marco vanilla de 16x32;
  * la sombra / punto de apoyo queda en x=8, y=30 de la celda (frontal, derecha, trasera) y en
    x=ancho-8 en la fila izquierda (el juego voltea la celda entera);
  * a la izquierda del punto de apoyo solo caben ~9 px.
Por eso los pies se centran en FOOTX (ancho-FOOTX en la izquierda), la fila izquierda es la
derecha sin espejar y frontal/trasera se estrechan un poco.
"""
import os, subprocess, sys, tempfile
import numpy as np
from PIL import Image

OUT=os.path.abspath(sys.argv[1]); NCOL=int(sys.argv[2]) if len(sys.argv)>2 else 32
CW,CH,FEET=24,32,30
FOOTX=9.5     # feet centre, counted from the anchor side
HS=0.95       # global size trim
SQUASH=0.85   # horizontal squash of front/back (wings would not fit left of the anchor)
TOP=40        # source rows above this are empty in every cell
SRC=os.path.join(os.path.dirname(os.path.abspath(__file__)),"..","agents","sheets","dr4co-sid-v2.png")   # hoja fuente de la v0.4.0
src=Image.open(SRC).convert("RGBA")

FOOT_H=5      # rows of the front foot that are moved to make the step
STEP={0:(0,0),1:(1,-1),2:(0,0),3:(-1,0)}   # front-foot (dx,dy) per walk frame: 1 px forward+raised, planted, 1 px back
feet_regions=[]   # (x, y, w, h, dx, dy) in sheet px
specs=[]   # (src_x, src_y, src_w, src_h, target_w, target_h, x_in_cell, y_in_cell, cell_x, cell_y)
for r in range(4):
    sr=1 if r==3 else r                       # left row = right row, unflipped
    cells=[src.crop((c*64,sr*128+TOP,c*64+64,sr*128+128)) for c in range(4)]
    alph=[np.asarray(cl.getchannel("A"))>60 for cl in cells]
    boxes=[cl.getchannel("A").point(lambda v:255 if v>60 else 0).getbbox() for cl in cells]
    ut=min(b[1] for b in boxes); sh=cells[0].height
    f=FEET/(sh-ut)*HS; sx=SQUASH if r in (0,2) else 1.0
    feet=[]
    for a in alph:
        cols=np.where(a[-12:].any(0))[0]; feet.append((cols.min()+cols.max())/2)
    feet_row=float(np.mean(feet)); target=FOOTX if r!=3 else CW-FOOTX-2   # left row: 2 px towards the tail so the snout tip is not clipped by the cell edge
    tw,th=round(64*f*sx),round(sh*f)
    for c in range(4):
        centre=(boxes[c][0]+boxes[c][2])/2 if r in (0,2) else feet_row
        ox=round(target-centre*f*sx)
        # every frame's lowest foot rests on the last row of the cell (where vanilla feet are),
        # so the dragon never floats above its shadow / collision tile
        oy=CH-round(boxes[c][3]*f)
        specs.append((c*64,sr*128+TOP,64,sh,tw,th,ox,oy,c*CW,r*CH))
        if r in (1,3):
            # front foot (the one on the snout side): bottom rows, right of the feet centre
            a=alph[c]; bot=boxes[c][3]
            cols=np.where(a[bot-10:bot].any(0))[0]; cols=cols[cols>feet_row+2]
            if len(cols):
                fx0=c*CW+ox+int(np.floor(cols.min()*f)); fx1=c*CW+ox+int(np.ceil(cols.max()*f))+1
                feet_regions.append((fx0, r*CH+CH-FOOT_H, fx1-fx0, FOOT_H)+STEP[c])
    print("row",r,"scale %.3f x %.3f"%(f*sx,f),"target %dx%d"%(tw,th))

def q(p): return '"'+p.replace('\\','\\\\').replace('"','\\"')+'"'
foot_s=" ".join("(list %d %d %d %d %d %d)"%t for t in feet_regions if t[4] or t[5])
spec_s=" ".join("(list %d %d %d %d %d %d %d %d %d %d)"%s for s in specs)
scm=f'''
(let* ((srcimg (car (gimp-file-load RUN-NONINTERACTIVE {q(SRC)} {q(SRC)})))
       (sheet (car (gimp-image-new {CW*4} {CH*4} RGB)))
       (base (car (gimp-layer-new sheet {CW*4} {CH*4} RGBA-IMAGE "base" 100 LAYER-MODE-NORMAL))))
  (gimp-image-insert-layer sheet base 0 0)
  (gimp-drawable-fill base FILL-TRANSPARENT)
  (gimp-context-set-interpolation INTERPOLATION-NOHALO)
  (for-each
    (lambda (s)
      (let* ((dup (car (gimp-image-duplicate srcimg))))
        (gimp-image-crop dup (nth 2 s) (nth 3 s) (nth 0 s) (nth 1 s))
        (gimp-image-scale dup (nth 4 s) (nth 5 s))
        (let* ((dl (car (gimp-image-merge-visible-layers dup EXPAND-AS-NECESSARY)))
               (nl (car (gimp-layer-new-from-drawable dl sheet))))
          (gimp-image-insert-layer sheet nl 0 -1)
          ; clip the frame to its own cell so nothing spills into the neighbour frame
          (gimp-layer-resize nl {CW} {CH} (nth 6 s) (nth 7 s))
          (gimp-layer-set-offsets nl (nth 8 s) (nth 9 s)))
        (gimp-image-delete dup)))
    (list {spec_s}))
  (let* ((layer (car (gimp-image-merge-visible-layers sheet CLIP-TO-IMAGE))))
    (plug-in-threshold-alpha RUN-NONINTERACTIVE sheet layer 90)
    ; step: move the front foot of the side views (floating selection, then anchor)
    (for-each
      (lambda (t)
        (gimp-image-select-rectangle sheet CHANNEL-OP-REPLACE (nth 0 t) (nth 1 t) (nth 2 t) (nth 3 t))
        (gimp-image-select-item sheet CHANNEL-OP-INTERSECT layer)
        (let* ((fl (car (gimp-selection-float layer (nth 4 t) (nth 5 t)))))
          (gimp-floating-sel-anchor fl)))
      (list {foot_s}))
    (gimp-selection-none sheet)
    ; dark outline wherever the silhouette edge is bright (snout tip, horn tips, belly, highlights);
    ; edges that are already dark are left alone. Brightness mask = value-thresholded copy.
    (let* ((mask (car (gimp-layer-copy layer FALSE))))
      (gimp-image-insert-layer sheet mask 0 -1)
      (gimp-drawable-threshold mask HISTOGRAM-VALUE 0.6 1.0)
      (gimp-image-select-item sheet CHANNEL-OP-REPLACE layer)
      (gimp-selection-shrink sheet 1)
      (gimp-selection-invert sheet)
      (gimp-image-select-item sheet CHANNEL-OP-INTERSECT layer)
      (gimp-context-set-sample-threshold 0.1)
      (gimp-image-select-color sheet CHANNEL-OP-INTERSECT mask '(255 255 255))
      (gimp-image-remove-layer sheet mask))
    (gimp-context-set-foreground '(77 7 11))
    (gimp-drawable-edit-fill layer FILL-FOREGROUND)
    (gimp-selection-none sheet)
    (gimp-image-convert-indexed sheet CONVERT-DITHER-NONE CONVERT-PALETTE-GENERATE {NCOL} FALSE TRUE "")
    (gimp-image-convert-rgb sheet)
    (file-png-save RUN-NONINTERACTIVE sheet layer {q(OUT)} {q(OUT)} 0 9 0 0 0 0 0))
  (gimp-quit 0))
'''
with tempfile.NamedTemporaryFile("w",suffix=".scm",delete=False) as fh: fh.write(scm); path=fh.name
res=subprocess.run(["gimp-console-2.10","-i","-d","-f","-b",scm],capture_output=True,text=True,timeout=300)
print(res.stdout[-800:],res.stderr[-1500:])
print("ok" if os.path.exists(OUT) else "NO OUTPUT")
