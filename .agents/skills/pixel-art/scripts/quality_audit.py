#!/usr/bin/env python3
"""
Audit a pixel art PNG for common quality issues:
  - Semi-transparent pixels (anti-aliasing artifacts)
  - Orphan pixels (isolated with no opaque neighbors)
  - Palette violations (too many unique colors, or off-palette colors with --palette)
  - Grid alignment (dimensions not multiples of base grid)
  - Optional mixel / upscaled logical-grid check (--check-mixels)

Usage:
    python scripts/quality_audit.py --image sprite.png --json
    python scripts/quality_audit.py --image sprite.png --grid 16 --max-colors 16 --palette palettes/pico-8.json --json
    python scripts/quality_audit.py --help
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image


def hex_to_rgb(h: str) -> tuple[int, int, int]:
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))


def audit_antialiasing(rgba: np.ndarray) -> dict:
    """Check for semi-transparent pixels (0 < alpha < 255)."""
    alpha = rgba[:, :, 3]
    bad = int(np.sum((alpha > 0) & (alpha < 255)))
    total = int(alpha.size)
    return {
        "semi_transparent_pixels": bad,
        "total_pixels": total,
        "clean": bad == 0,
    }


def audit_orphan_pixels(rgba: np.ndarray) -> dict:
    """Check for isolated pixels with zero opaque 8-way neighbors."""
    opaque = rgba[:, :, 3] > 0
    h, w = opaque.shape
    padded = np.pad(opaque, 1, mode="constant", constant_values=False)
    neighbors = np.zeros((h, w), dtype=np.int32)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dy == 0 and dx == 0:
                continue
            neighbors += padded[1 + dy : 1 + dy + h, 1 + dx : 1 + dx + w]
    orphans = int(np.sum(opaque & (neighbors == 0)))
    return {"orphan_pixels": orphans, "clean": orphans == 0}


def audit_palette(rgba: np.ndarray, max_colors: int, palette_path: Path | None = None) -> dict:
    """Check unique opaque color count and optional exact palette membership."""
    opaque_mask = rgba[:, :, 3] > 0
    opaque_rgb = rgba[:, :, :3][opaque_mask]
    if len(opaque_rgb) == 0:
        unique_colors = set()
    else:
        uniq = np.unique(opaque_rgb, axis=0)
        unique_colors = {tuple(int(c) for c in row) for row in uniq}

    count = len(unique_colors)
    res: dict = {
        "unique_colors": count,
        "max_allowed": max_colors,
        "clean": count <= max_colors,
    }

    if palette_path is not None:
        with open(palette_path, "r", encoding="utf-8") as f:
            pdata = json.load(f)
        allowed = {hex_to_rgb(c) for c in pdata["colors"]}
        off_palette = [c for c in unique_colors if c not in allowed]
        off_pixels = 0
        if off_palette:
            off_set = set(off_palette)
            for c in off_set:
                off_pixels += int(np.sum(np.all(opaque_rgb == np.array(c, dtype=np.uint8), axis=1)))
        res["palette_name"] = pdata.get("name", palette_path.stem)
        res["off_palette_colors"] = len(off_palette)
        res["off_palette_pixels"] = off_pixels
        res["off_palette_samples"] = [f"#{r:02X}{g:02X}{b:02X}" for r, g, b in sorted(off_palette)[:8]]
        res["clean"] = res["clean"] and len(off_palette) == 0

    return res


def audit_grid(img: Image.Image, grid_size: int) -> dict:
    """Check if dimensions are exact multiples of grid_size."""
    w, h = img.size
    aligned = (w % grid_size == 0) and (h % grid_size == 0)
    return {
        "width": w,
        "height": h,
        "grid_size": grid_size,
        "aligned": aligned,
    }


def audit_mixels(rgba: np.ndarray) -> dict:
    """Detect whether an upscaled pixel art image contains mixed pixel sizes / blurry sub-blocks."""
    rgb = rgba[:, :, :3].astype(np.float32)
    h, w, _ = rgb.shape
    gx = np.abs(np.diff(rgb, axis=1)).mean(axis=(0, 2))
    gy = np.abs(np.diff(rgb, axis=0)).mean(axis=(1, 2))

    best_k = 1
    best_score = 0.0
    max_k = min(32, min(w, h) // 8)
    floor_x = float(gx.mean()) * 0.05 + 1e-3
    floor_y = float(gy.mean()) * 0.05 + 1e-3
    for k in range(2, max_k + 1):
        if w % k != 0 or h % k != 0:
            continue
        bx = float(gx[k - 1 :: k].mean())
        ix = float(np.delete(gx, np.arange(k - 1, len(gx), k)).mean())
        by = float(gy[k - 1 :: k].mean())
        iy = float(np.delete(gy, np.arange(k - 1, len(gy), k)).mean())
        score = min((bx - ix) / max(bx, floor_x), (by - iy) / max(by, floor_y))
        # Prefer larger exact tile scale when all true boundaries align (k=4 beats k=2 because k=2 averages 50% empty internal edges)
        if score > best_score * 1.02:
            best_score = score
            best_k = k

    if best_k == 1 or best_score < 0.45:
        return {"inferred_scale": 1, "mixel_clean": True, "dirty_block_ratio": 0.0}

    # Measure non-uniform blocks inside inferred k x k cells
    blocks = rgba.reshape(h // best_k, best_k, w // best_k, best_k, 4)
    block_std = blocks[:, :, :, :, :3].astype(np.float32).std(axis=(1, 3)).mean(axis=2)
    dirty_blocks = int(np.sum(block_std > 2.0))
    total_blocks = int(block_std.size)
    dirty_ratio = round(dirty_blocks / max(total_blocks, 1), 4)
    # If >20% of blocks are non-uniform, the image is native 1x pixel art (e.g. dithered) rather than upscaled with mixels
    if dirty_ratio > 0.20:
        return {"inferred_scale": 1, "mixel_clean": True, "dirty_block_ratio": 0.0}
    return {
        "inferred_scale": best_k,
        "dirty_blocks": dirty_blocks,
        "total_blocks": total_blocks,
        "dirty_block_ratio": dirty_ratio,
        "mixel_clean": dirty_blocks == 0,
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Audit a pixel art PNG for anti-aliasing, orphan pixels, palette violations, and grid alignment."
    )
    parser.add_argument("--image", type=str, required=True, help="Path to the PNG image to audit")
    parser.add_argument("--grid", type=int, default=8, help="Base grid size for alignment check (default: 8)")
    parser.add_argument("--max-colors", type=int, default=256, help="Maximum allowed unique colors (default: 256)")
    parser.add_argument("--palette", type=str, help="Optional palette JSON to enforce exact color membership")
    parser.add_argument("--check-mixels", action="store_true", help="Flag blurry sub-pixel blocks / mixels on upscaled sprites")
    parser.add_argument("--json", action="store_true", help="Output result as JSON")
    args = parser.parse_args()

    path = Path(args.image)
    if not path.exists():
        msg = f"File not found: {args.image}"
        if args.json:
            print(json.dumps({"status": "error", "message": msg}))
        else:
            print(f"Error: {msg}", file=sys.stderr)
        return 1

    palette_path = Path(args.palette) if args.palette else None
    if palette_path is not None and not palette_path.is_file():
        msg = f"Palette not found: {args.palette}"
        print(json.dumps({"status": "error", "message": msg}) if args.json else f"Error: {msg}", file=sys.stderr)
        return 1

    try:
        img = Image.open(path).convert("RGBA")
    except Exception as e:  # noqa: BLE001
        msg = f"Cannot open image: {e}"
        if args.json:
            print(json.dumps({"status": "error", "message": msg}))
        else:
            print(f"Error: {msg}", file=sys.stderr)
        return 1

    rgba = np.asarray(img)
    aa = audit_antialiasing(rgba)
    orphans = audit_orphan_pixels(rgba)
    palette = audit_palette(rgba, args.max_colors, palette_path=palette_path)
    grid = audit_grid(img, args.grid)

    passed = aa["clean"] and orphans["clean"] and palette["clean"] and grid["aligned"]
    report: dict = {
        "status": "pass" if passed else "fail",
        "file": str(path),
        "antialiasing": aa,
        "orphan_pixels": orphans,
        "palette": palette,
        "grid_alignment": grid,
    }

    if args.check_mixels:
        mixels = audit_mixels(rgba)
        report["mixels"] = mixels
        if not mixels["mixel_clean"]:
            passed = False
            report["status"] = "fail"

    if args.json:
        print(json.dumps(report, indent=2))
    else:
        print(f"Audit: {'PASS' if passed else 'FAIL'} — {path.name}")
        if not aa["clean"]:
            print(f"  ⚠ {aa['semi_transparent_pixels']} semi-transparent pixels found")
        if not orphans["clean"]:
            print(f"  ⚠ {orphans['orphan_pixels']} orphan pixels found")
        if not palette["clean"]:
            print(f"  ⚠ {palette['unique_colors']} colors found (max {palette['max_allowed']})")
            if palette.get("off_palette_colors", 0) > 0:
                print(
                    f"  ⚠ {palette['off_palette_colors']} off-palette colors "
                    f"({palette['off_palette_pixels']} px): {', '.join(palette['off_palette_samples'])}"
                )
        if not grid["aligned"]:
            print(f"  ⚠ {grid['width']}x{grid['height']} not aligned to {grid['grid_size']}px grid")
        if args.check_mixels and not report["mixels"]["mixel_clean"]:
            m = report["mixels"]
            print(
                f"  ⚠ Mixels detected at inferred scale {m['inferred_scale']}x: "
                f"{m['dirty_blocks']}/{m['total_blocks']} dirty blocks"
            )

    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
