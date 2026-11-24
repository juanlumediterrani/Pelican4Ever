# Difusión Local Abierta para Pixel Art (RTX 3060 · 12 GB VRAM)

Guía de modelos libres y ajuste de parámetros para generar materia prima en local sin depender de APIs de pago, seguida siempre del pipeline determinista (`grid_snap.py` → `palette_remap.py` → `quality_audit.py`).

---

## 1. Por qué NO generar a 64×64 en modelos latentes

Los VAE de SDXL y FLUX comprimen la imagen por un factor de 8 en espacio latente. Generar directamente a `64×64` o `128×128` produce ruido irreconocible.

**Flujo correcto:**
1. Genera a resolución latente nativa limpia (`1024×1024` en SDXL/FLUX, o `512×512`/`768×768` en SD 1.5), siempre en **múltiplos de 64**.
2. Pasa la salida por `scripts/grid_snap.py` (`--method contrast --remove-checkerboard`) o por un nodo **PixelOE** para encajar cada bloque lógico en una rejilla exacta (`32×32`, `64×64`, `128×128`).
3. Cuantiza a la paleta del proyecto con `scripts/palette_remap.py --metric oklab`.
4. Valida con `scripts/quality_audit.py --palette ... --check-mixels`.

---

## 2. Selección de Modelos para 12 GB VRAM

| Stack | VRAM típica | Mejor uso | Notas |
| :--- | :---: | :--- | :--- |
| **SDXL 1.0 + LoRA `pixel-art-xl`** + `sdxl-vae-fp16-fix` | ~7–8 GB (FP16) | Objetos, iconos, tilesets, fondos e isométrico | El punto dulce de velocidad, calidad y control (`ControlNet Canny/Depth`). |
| **FLUX.1 Kontext-dev (`Q8_0.gguf`) + `T5-XXL fp16` (RAM)** | ~11.8 GB modelo (+ ~9 GB T5 en RAM) | Ediciones consistentes de personaje/poses desde un sprite base (`ReferenceLatent`) | Usa tus 32 GB de RAM (`--clip-device cpu`) para no saturar los 12 GB de VRAM. |
| **SD 1.5 + `All-In-One-Pixel-Model` + ControlNet Tile/Lineart** | ~3.5–4.5 GB | `img2img` rápido y variaciones sobre sprites existentes | Opcional ligero para barrer decenas de `seeds` por minuto. |
| **Chroma1-Radiance (`Q6_K_L` / `Q8_0`)** *(opcional)* | ~8.5–11 GB | Generación nativa en espacio de píxeles sin VAE | Útil si se quiere eliminar por completo la pérdida por compresión VAE. |

---

## 3. Recetas de Parámetros Calibradas (RTX 3060 12 GB + 32 GB RAM)

### A. SDXL + `pixel-art-xl` (Text-to-Image)
- **Resolución:** `1024×1024` (o `768×1024` / `1024×768`) · **Tiempo medido:** ~18–20 s/imagen
- **Sampler / Scheduler:** `dpmpp_2m` + `karras` (estable) o `euler_a` (formas más orgánicas antes de `grid_snap.py`)
- **Steps:** `22` (pasar de `35` añade micro-ruido que empeora el snapping)
- **CFG Scale:** `5.5` como punto dulce (`7.0+` introduce sombras proyectadas en el suelo y halos)
- **LoRA weight:** `0.85` – `0.95` (bajar a `0.65 – 0.75` si apilas un LoRA de personaje)
- **Prompt / Negative:** incluye `floating on flat pure #FFFFFF background, no ground shadow, thick dark outline` y evita `soft shadows, drop shadow` para que `--remove-checkerboard` recorte limpiamente bajo los pies.

### B. `img2img` sobre Sprite Base (Variaciones / Poses / Ropa)
1. Escala primero tu sprite nativo con **nearest-neighbor** (`sprite_resize.py --scale 8` o `--scale 16`) hasta `512×512` o `1024×1024`.
2. **Denoising strength:**
   - `0.30 – 0.45`: cambia paleta, textura o pequeños accesorios conservando pose.
   - `0.45 – 0.60`: cambia vestimenta o expresión manteniendo silueta.
   - `> 0.65`: rediseña el personaje (evitar salvo exploración).
3. Combina con **ControlNet Canny / Lineart** (`weight 0.55 – 0.75`) cuando la silueta de animación sea estricta.

### C. FLUX.1 Kontext-dev (`Q8_0.gguf` + `T5-XXL fp16` en RAM)
- **Tiempo medido:** ~207 s en primera carga (incluye `T5-XXL fp16` en RAM) y ~119 s por generación siguiente.
- **Sampler / Scheduler:** `euler` + `simple` (o `beta`)
- **KSampler `cfg`:** `1.0` (obligatorio en FLUX dev/Kontext; la guía se controla con `FluxGuidance`)
- **FluxGuidance:** **`3.5`** recomendado (en pruebas a `2.5` pierde alineación periódica de rejilla y `grid_snap.py` requiere fallback)
- **Steps:** `20 – 24`
- **Referencia (`--ref-image`) y paleta automática:** `local_diff_sweep.py` centra y reescala la referencia con `NEAREST` sobre `#FFFFFF` (evitando deformación y fondos negros en `VAEEncode`), y si no indicas `--palette`, extrae automáticamente la paleta exacta del sprite base (`--palette auto`, `--max-ref-colors 32`) para no alterar contornos ni tonos originales.
- **Rejilla obligatoria:** pasa `--target-size 64x64` (o `--cell-size 16`); si alguna semilla sale sin rejilla clara (`cell=1`), el sweep aplica `--fallback-cell-size 16`.

---

## 4. Pipeline Post-Difusión y Barrido Automático

### Barrido automatizado contra ComfyUI local (`127.0.0.1:8188`)
```bash
# 1. Arrancar servidor local ComfyUI (configurado mediante $COMFYUI_START_SCRIPT o servicio local)
# 2. Barrido SDXL + pixel-art-xl + contact sheet puntuada (defaults ya calibrados)
python3 scripts/local_diff_sweep.py \
  --workflow sdxl \
  --prompt "pixel, pixel art cute retro mascot, centered, thick dark outline, no ground shadow, floating on pure #FFFFFF background" \
  --palette palettes/gameboy.json --dither bayer4 \
  --output-dir ./sweep_out

# 3. Edición consistente de sprite/pose con FLUX.1 Kontext-dev (paleta extraída automáticamente de mi_sprite.png)
python3 scripts/local_diff_sweep.py \
  --workflow kontext \
  --ref-image mi_sprite.png \
  --target-size 64x64 \
  --prompt "pixel art sprite of the exact same character walking sideways facing right, full body, crisp hard pixel edges, no ground shadow, floating on pure white #FFFFFF background" \
  --output-dir ./sweep_kontext
```

### Pipeline manual paso a paso (si ya tienes la imagen bruta)
```bash
# 1. Encajar en rejilla discreta y limpiar damero/fondo
python3 scripts/grid_snap.py --image raw_1024.png --target-size 64x64 --method contrast --remove-checkerboard --output snapped_64.png --json

# 2. Cuantizar a la paleta del proyecto en Oklab (con dithering opcional para 1-bit / bitonal)
python3 scripts/palette_remap.py --image snapped_64.png --palette palettes/pico-8.json --metric oklab --output final_64.png --json

# 3. Auditar pertenencia exacta a paleta y cero semitransparencias
python3 scripts/quality_audit.py --image final_64.png --grid 16 --max-colors 16 --palette palettes/pico-8.json --json
```
