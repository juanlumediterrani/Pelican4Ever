#!/usr/bin/env python3
"""
Snap an AI-generated or upscaled image onto a strict discrete pixel grid.

Supports:
  - Automatic logical pixel-cell detection (gradient periodicity) or explicit --target-size / --cell-size
  - Contrast-aware outline-preserving downscale (PixelOE-style), median, mode, or center sampling
  - Optional border-connected checkerboard removal (--remove-checkerboard)
  - Strict binary alpha enforcement (0 or 255)

Usage:
    python scripts/grid_snap.py --image raw_1024.png --output snapped.png --json
    python scripts/grid_snap.py --image raw_1024.png --target-size 64x64 --method contrast --remove-checkerboard --output sprite_64.png --json
    python scripts/grid_snap.py --help
"""

from __future__ import annotations

import argparse
import json
import sys
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image


def parse_size(spec: str) -> tuple[int, int]:
    parts = spec.lower().split("x")
    if len(parts) != 2:
        raise ValueError(f"Invalid size '{spec}'; expected WxH (e.g. 64x64)")
    w, h = int(parts[0]), int(parts[1])
    if w <= 0 or h <= 0:
        raise ValueError("Target dimensions must be positive")
    return w, h


def estimate_cell_size(rgba: np.ndarray, min_k: int = 2, max_k: int = 32) -> dict:
    """Estimate logical pixel block size using periodic gradient energy along X and Y."""
    rgb = rgba[:, :, :3].astype(np.float32)
    h, w, _ = rgb.shape

    gx = np.abs(np.diff(rgb, axis=1)).mean(axis=(0, 2))  # len w-1, index i = boundary between i and i+1
    gy = np.abs(np.diff(rgb, axis=0)).mean(axis=(1, 2))  # len h-1

    def best_period(profile: np.ndarray) -> tuple[int, float]:
        n = len(profile) + 1
        mean_val = float(profile.mean()) + 1e-6
        best_k = 1
        best_score = 1.0
        for k in range(min_k, min(max_k, n // 4) + 1):
            if n % k != 0:
                continue
            # Boundary indices between k-pixel blocks are k-1, 2k-1, ...
            boundaries = profile[k - 1 :: k]
            interior_mask = np.ones(len(profile), dtype=bool)
            interior_mask[k - 1 :: k] = False
            interior = profile[interior_mask]
            if len(boundaries) == 0 or len(interior) == 0:
                continue
            b_mean = float(boundaries.mean())
            i_mean = float(interior.mean()) + 1e-6
            # Strong grid: high gradient on block boundaries, low gradient inside blocks
            score = (b_mean / i_mean) if b_mean > mean_val * 0.8 else 0.0
            # Keep the fundamental period; avoid promoting harmonics (2k, 4k) unless dramatically stronger
            is_harmonic = best_k > 1 and (k % best_k == 0)
            threshold_mult = 1.35 if is_harmonic else 1.05
            if score > best_score * threshold_mult:
                best_score = score
                best_k = k
        return best_k, round(best_score, 3)

    kx, sx = best_period(gx)
    ky, sy = best_period(gy)
    if kx == ky and kx > 1:
        k = kx
        conf = round((sx + sy) / 2.0, 3)
    elif kx > 1 and sy > 1.3 and w % ky == 0 and sx < sy * 1.15:
        k = ky
        conf = sy
    elif ky > 1 and sx > 1.3 and h % kx == 0 and sy < sx * 1.15:
        k = kx
        conf = sx
    else:
        k = kx if sx >= sy else ky
        conf = max(sx, sy)

    if conf < 1.35:
        k = 1
    return {"estimated_cell_size": int(k), "confidence": float(conf), "kx": int(kx), "ky": int(ky)}


def remove_checkerboard_bg(rgba: np.ndarray, tol: int = 16) -> tuple[np.ndarray, int]:
    """Flood-fill neutral gray/white background (including fake checkerboard) from image borders."""
    out = rgba.copy()
    h, w, _ = out.shape
    rgb = out[:, :, :3].astype(np.int16)
    alpha = out[:, :, 3]

    chroma = rgb.max(axis=2) - rgb.min(axis=2)
    lum = rgb.mean(axis=2)
    is_neutral = (alpha == 0) | ((chroma <= tol) & (lum >= 140))

    visited = np.zeros((h, w), dtype=bool)
    queue: deque[tuple[int, int]] = deque()

    for x in range(w):
        for y in (0, h - 1):
            if is_neutral[y, x] and not visited[y, x]:
                visited[y, x] = True
                queue.append((y, x))
    for y in range(h):
        for x in (0, w - 1):
            if is_neutral[y, x] and not visited[y, x]:
                visited[y, x] = True
                queue.append((y, x))

    cleared = 0
    while queue:
        cy, cx = queue.popleft()
        if out[cy, cx, 3] > 0:
            out[cy, cx] = [0, 0, 0, 0]
            cleared += 1
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            ny, nx = cy + dy, cx + dx
            if 0 <= ny < h and 0 <= nx < w and not visited[ny, nx] and is_neutral[ny, nx]:
                visited[ny, nx] = True
                queue.append((ny, nx))

    return out, cleared


def snap_to_grid(
    rgba: np.ndarray,
    target_w: int,
    target_h: int,
    method: str = "contrast",
    alpha_threshold: int = 128,
) -> np.ndarray:
    """Downsample RGBA image onto exact (target_w, target_h) discrete pixel grid."""
    src_h, src_w, _ = rgba.shape
    out = np.zeros((target_h, target_w, 4), dtype=np.uint8)

    # Precompute local contrast map for PixelOE-style outline preservation
    rgb_f = rgba[:, :, :3].astype(np.float32)
    lum = 0.2126 * rgb_f[:, :, 0] + 0.7152 * rgb_f[:, :, 1] + 0.0722 * rgb_f[:, :, 2]

    ys = np.linspace(0, src_h, target_h + 1, dtype=int)
    xs = np.linspace(0, src_w, target_w + 1, dtype=int)

    for ty in range(target_h):
        y0, y1 = ys[ty], max(ys[ty] + 1, ys[ty + 1])
        for tx in range(target_w):
            x0, x1 = xs[tx], max(xs[tx] + 1, xs[tx + 1])
            patch = rgba[y0:y1, x0:x1]
            alphas = patch[:, :, 3]
            opaque_mask = alphas >= alpha_threshold
            if int(opaque_mask.sum()) * 2 < alphas.size:
                out[ty, tx] = [0, 0, 0, 0]
                continue

            opaque_rgb = patch[:, :, :3][opaque_mask]

            if method == "center":
                cy = min((y0 + y1) // 2, src_h - 1)
                cx = min((x0 + x1) // 2, src_w - 1)
                px = rgba[cy, cx]
                if px[3] < alpha_threshold:
                    r, g, b = np.median(opaque_rgb, axis=0)
                    out[ty, tx] = [int(r), int(g), int(b), 255]
                else:
                    out[ty, tx] = [int(px[0]), int(px[1]), int(px[2]), 255]
            elif method == "median":
                med = np.median(opaque_rgb, axis=0)
                out[ty, tx] = [int(med[0]), int(med[1]), int(med[2]), 255]
            elif method == "mode":
                quant = (opaque_rgb // 4) * 4
                vals, counts = np.unique(quant, axis=0, return_counts=True)
                best_q = vals[int(np.argmax(counts))]
                match = np.all(quant == best_q, axis=1)
                med = np.median(opaque_rgb[match], axis=0)
                out[ty, tx] = [int(med[0]), int(med[1]), int(med[2]), 255]
            else:  # contrast (PixelOE-inspired outline & peak-contrast preservation)
                patch_lum = lum[y0:y1, x0:x1][opaque_mask]
                med_lum = float(np.median(patch_lum))
                min_lum = float(patch_lum.min())
                max_lum = float(patch_lum.max())
                span = max_lum - min_lum
                if span > 45.0:
                    # Prefer dark outlines when cell bridges outline + fill, else extreme contrast
                    dark_frac = float((patch_lum <= min_lum + span * 0.25).mean())
                    if dark_frac >= 0.2:
                        sel = patch_lum <= min_lum + span * 0.25
                        chosen = np.median(opaque_rgb[sel], axis=0)
                    else:
                        dists = np.abs(patch_lum - med_lum)
                        chosen = opaque_rgb[int(np.argmax(dists))]
                else:
                    chosen = np.median(opaque_rgb, axis=0)
                out[ty, tx] = [int(chosen[0]), int(chosen[1]), int(chosen[2]), 255]

    return out


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Snap an AI-generated or upscaled image onto a strict discrete pixel grid."
    )
    parser.add_argument("--image", required=True, help="Input PNG image path")
    parser.add_argument("--output", required=True, help="Output snapped PNG path")
    parser.add_argument("--target-size", help="Explicit output grid WxH (e.g. 64x64)")
    parser.add_argument("--cell-size", type=int, help="Explicit logical pixel block size (overrides auto-detection)")
    parser.add_argument(
        "--method",
        default="contrast",
        choices=["contrast", "median", "mode", "center"],
        help="Sampling method per grid cell (default: contrast)",
    )
    parser.add_argument(
        "--remove-checkerboard",
        action="store_true",
        help="Flood-fill and clear neutral gray/white background or fake checkerboard from borders",
    )
    parser.add_argument("--alpha-threshold", type=int, default=128, help="Binary alpha threshold 0-255 (default: 128)")
    parser.add_argument("--upscale", type=int, default=1, help="Optional integer nearest-neighbor upscale after snapping")
    parser.add_argument("--json", action="store_true", help="Output result as JSON")
    args = parser.parse_args()

    in_path = Path(args.image)
    if not in_path.is_file():
        msg = f"Image not found: {args.image}"
        print(json.dumps({"status": "error", "message": msg}) if args.json else f"Error: {msg}", file=sys.stderr)
        return 1

    img = Image.open(in_path).convert("RGBA")
    rgba = np.asarray(img).copy()
    src_h, src_w, _ = rgba.shape

    cleared_bg = 0
    if args.remove_checkerboard:
        rgba, cleared_bg = remove_checkerboard_bg(rgba)

    grid_info = estimate_cell_size(rgba)
    if args.target_size:
        target_w, target_h = parse_size(args.target_size)
        cell_used = round(src_w / target_w, 2)
    else:
        cell = args.cell_size if args.cell_size and args.cell_size > 0 else grid_info["estimated_cell_size"]
        cell_used = cell
        target_w = max(1, src_w // cell)
        target_h = max(1, src_h // cell)

    snapped = snap_to_grid(rgba, target_w, target_h, method=args.method, alpha_threshold=args.alpha_threshold)
    out_img = Image.fromarray(snapped, mode="RGBA")
    if args.upscale > 1:
        out_img = out_img.resize((target_w * args.upscale, target_h * args.upscale), Image.Resampling.NEAREST)

    out_path = Path(args.output)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_img.save(out_path)

    result = {
        "status": "ok",
        "input": str(in_path),
        "output": str(out_path),
        "input_size": [src_w, src_h],
        "snapped_size": [target_w, target_h],
        "output_size": list(out_img.size),
        "method": args.method,
        "cell_size_used": cell_used,
        "auto_grid": grid_info,
        "checkerboard_pixels_cleared": cleared_bg,
    }
    if args.json:
        print(json.dumps(result, indent=2))
    else:
        print(
            f"Snapped {in_path.name} ({src_w}x{src_h}) -> {out_path.name} ({out_img.size[0]}x{out_img.size[1]}) "
            f"[method={args.method}, cell={cell_used}]"
        )
    return 0


if __name__ == "__main__":
    sys.exit(main())
