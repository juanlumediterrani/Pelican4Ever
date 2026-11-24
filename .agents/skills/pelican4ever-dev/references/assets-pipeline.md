# Desarrollo de Assets en Pelican4Ever (Sprites, Skins, Mapas y Forja de Pixeles)

Flujo de arte pixelado, skins jugables, mapas TMX y compatibilidad NITV dentro del repositorio **Pelican4Ever**. Para QA genérico de PNG (`quality_audit.py`, `sheet_qa.py`, `palette_remap.py`) y extracción `.xnb` (`dump_xnb.sh`), carga `pixel-art` y `smapi-harmony-pelican-bridge`.

---

## 1. Skins Integrales Nativas en `Pelican4Ever` vs. Packs `Fashion Sense`

Existen **dos vías** documentadas en [`agents/README.md`](../../../agents/README.md):

### A. Vía Nativa Integrada (`Pelican4Ever/assets/skins/` — sin dependencias externas)
- **Ficheros:** hojas completas de **`126` cuadros** (`144×672` px) sobre `FarmerRenderer.draw`:
  - `dr4co_sheet.png`, `dr4co_carry_sheet.png`
  - `putre_sheet.png`, `putre_carry_sheet.png` *(personaje in-game, independiente del antiguo runtime externo ya retirado por DEC-0004)*
- **Comandos en juego / consola SMAPI (solo Host):**
  - `p4e_skin putre [jugador]` / `p4e_skin dr4co [jugador]` / `p4e_skin off [jugador]`
- **Regla multijugador:** cada cliente es dueño del `modData` de su propio granjero; para cambiar la skin de un remoto, el host envía mensaje de mod al cliente conectado.

### B. Vía Cosmética `Fashion Sense` (`agents/Dr4co.FashionSense/`)
- Requiere `Fashion Sense 7.5.0+` (`PeacefulEnd.FashionSense`).
- **Regla 1 (capas vanilla):** deben incluirse apariencias transparentes `None` para `Hair`, `Shirt`, `Pants`, `Shoes`, más `HideSleeves: true` en las 4 direcciones.
- **Regla 2 (API pública de Fashion Sense):** el cuerpo se asigna vía `modData`:
  ```csharp
  farmer.modData["FashionSense.CustomBody.Id"] = "Dr4co.Stardew.FashionSenseBody/Player/Dr4co";
  ```
- **Calibración geométrica:** hoja **`96×128`** (`4×4` celdas de **`24×32`**); punto de apoyo (sombra) **`x=8, y=30`** en filas frontal, derecha y trasera, y **`x=16`** en fila izquierda porque el juego voltea horizontalmente la celda.
- Ajuste de manos/herramientas: `Pelican4Ever/Modules/AgentCosmeticsModule.cs` + `Pelican4Ever.Core/Players/AgentToolOffset.cs`.

---

## 2. Scripts del Pipeline de Sprites (`scripts/`)

Ejecútalos desde la raíz del repositorio `Pelican4Ever/`:

1. **`scripts/build_farmer_reference.py`:** ensambla referencia vanilla de 126 cuadros con `featureXOffsetPerFrame` / `featureYOffsetPerFrame` reales.
2. **`scripts/dr4co_llm_pipeline.py`:** procesa hoja 3×3 (`#FFFFFF` puro) → paleta fija de 12 colores → *Head-Lock* (+1 px vertical) → contorno *sel-out*.
3. **`scripts/dr4co_actions_pipeline.py`:** recorte y ensamblado 1× / 4× para herramientas y combate.
4. **`scripts/build_dr4co_portrait.py`:** genera `portrait.png` (`64×64` a `1×` y `256×256` a `4×`).
5. **Auditoría obligatoria antes de integrar PNGs en `Pelican4Ever/assets/`:**
   ```bash
   python3 ".agents/skills/pixel-art/scripts/quality_audit.py" --image <ruta.png> --grid 8
   python3 ".agents/skills/pixel-art/scripts/sheet_qa.py" --image <body.png> --frame-width 24 --frame-height 32
   ```

---

## 3. Mapas `.tmx`, Minimapas y Compatibilidad NITV

- **Mapas propios del mod (`Pelican4Ever/assets/`):**
  - `anexo-forestal-north.tmx` y `doors.json` (mantener coherencia con `HostPlazaLayout.MaxFarmSlotCount` al escalar `16`/`32`/`64`).
- **`tools/MapRenderer`:**
  - Anclaje calibrado: `top = (tileY + 1) * 16 - spriteHeight`.
  - Orden exacto de capas: `Back, Buildings, Buildings2, Front, Front2, AlwaysFront, AlwaysFront2, AlwaysFront3` (omite `Paths`).
- **Pack `Pelican4Ever.NITVCompat` (`deploy/modpack/Pelican4Ever.NITVCompat/`):**
  - Content Patcher (`"HasMod": "Nature.NatureInTheValley"`); registra `"Nature.NIV.Outside"`, `"Nature.NIV.Forest"`, `"Nature.NIV.Beach"` y `"Nature.NIV.Underground"` en residencias dinámicas.

---

## 4. `Forja de Pixeles` (Incubadora hermanada)

- Proyecto en `../../Forja de Pixeles/` (micro-difusión `16×16` RGBA → ONNX estático para RPi 3B+); cuenta con su propia skill local en `Forja de Pixeles/.agents/skills/forja-tiny-diffusion/SKILL.md`.
