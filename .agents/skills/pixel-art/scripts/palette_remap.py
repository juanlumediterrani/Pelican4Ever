#!/usr/bin/env python3
"""
Remap image colors to the nearest color in a target palette using perceptual
Oklab (default) or Euclidean RGB distance, with optional ordered Bayer dithering.

Usage:
    python scripts/palette_remap.py --image input.png --palette palettes/pico-8.json --output remapped.png --json
    python scripts/palette_remap.py --image input.png --palette palettes/gameboy.json --dither bayer4 --dither-strength 0.35 --output gb.png --json
    python scripts/palette_remap.py --help
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

BAYER_2X2 = np.array([[0, 2], [3, 1]], dtype=np.float32) / 4.0 - 0.375
BAYER_4X4 = (
    np.array(
        [
            [0, 8, 2, 10],
            [12, 4, 14, 6],
            [3, 11, 1, 9],
            [15, 7, 13, 5],
        ],
        dtype=np.float32,
    )
    / 16.0
    - 0.46875
)


def hex_to_rgb(h: str) -> tuple[int, int, int]:
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))


def srgb_to_oklab(rgb: np.ndarray) -> np.ndarray:
    """Convert uint8/float sRGB [..., 3] in 0..255 to Oklab [..., 3]."""
    c = rgb.astype(np.float32) / 255.0
    lin = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    r, g, b = lin[..., 0], lin[..., 1], lin[..., 2]

    l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b
    m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b
    s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b

    l_ = np.cbrt(np.maximum(l, 0.0))
    m_ = np.cbrt(np.maximum(m, 0.0))
    s_ = np.cbrt(np.maximum(s, 0.0))

    L = 0.2104542553 * l_ + 0.7936177850 * m_ - 0.0040720468 * s_
    a = 1.9779984951 * l_ - 2.4285922050 * m_ + 0.4505937099 * s_
    b_lab = 0.0259040371 * l_ + 0.7827717662 * m_ - 0.8086757660 * s_
    return np.stack([L, a, b_lab], axis=-1)


def remap_image(
    img: Image.Image,
    palette_rgbs: list[tuple[int, int, int]],
    metric: str = "oklab",
    dither: str = "none",
    dither_strength: float = 0.35,
) -> Image.Image:
    rgba = np.asarray(img.convert("RGBA")).copy()
    h, w, _ = rgba.shape
    pal_rgb = np.array(palette_rgbs, dtype=np.uint8)

    alpha = rgba[:, :, 3]
    opaque = alpha > 0
    if not np.any(opaque):
        return img.copy()

    if metric == "oklab":
        src_feat = srgb_to_oklab(rgba[:, :, :3])
        pal_feat = srgb_to_oklab(pal_rgb)
    else:
        src_feat = rgba[:, :, :3].astype(np.float32) / 255.0
        pal_feat = pal_rgb.astype(np.float32) / 255.0

    if dither != "none" and dither_strength > 0.0:
        matrix = BAYER_4X4 if dither == "bayer4" else BAYER_2X2
        m = matrix.shape[0]
        tiled = np.tile(matrix, (h // m + 1, w // m + 1))[:h, :w]
        if metric == "oklab":
            # Perturb perceptual lightness L proportional to palette lightness spacing
            l_sorted = np.sort(pal_feat[:, 0])
            step = float(np.median(np.diff(l_sorted))) if len(l_sorted) > 1 else 0.25
            src_feat[:, :, 0] += tiled * step * dither_strength * 2.0
        else:
            src_feat += tiled[:, :, None] * (dither_strength * 0.25)

    # Vectorized nearest-palette lookup on opaque pixels
    flat_src = src_feat[opaque]  # (N, 3)
    dists = np.sum((flat_src[:, None, :] - pal_feat[None, :, :]) ** 2, axis=2)
    nearest_idx = np.argmin(dists, axis=1)

    out = rgba.copy()
    out_rgb = out[:, :, :3]
    out_rgb[opaque] = pal_rgb[nearest_idx]
    out[:, :, 3] = np.where(opaque, 255, 0)
    return Image.fromarray(out, mode="RGBA")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Remap image colors to the nearest color in a target palette."
    )
    parser.add_argument("--image", type=str, required=True, help="Source PNG image path")
    parser.add_argument("--palette", type=str, required=True, help="Palette JSON file path")
    parser.add_argument("--output", type=str, required=True, help="Output PNG image path")
    parser.add_argument(
        "--metric",
        default="oklab",
        choices=["oklab", "rgb"],
        help="Color distance metric: perceptual oklab (default) or euclidean rgb",
    )
    parser.add_argument(
        "--dither",
        default="none",
        choices=["none", "bayer2", "bayer4"],
        help="Optional ordered Bayer dithering (default: none)",
    )
    parser.add_argument(
        "--dither-strength",
        type=float,
        default=0.35,
        help="Ordered dithering intensity when --dither != none (default: 0.35)",
    )
    parser.add_argument("--json", action="store_true", help="Output result as JSON")
    args = parser.parse_args()

    image_path = Path(args.image)
    palette_path = Path(args.palette)

    if not image_path.exists():
        msg = f"Image not found: {args.image}"
        print(json.dumps({"status": "error", "message": msg}) if args.json else f"Error: {msg}")
        return 1

    if not palette_path.exists():
        msg = f"Palette not found: {args.palette}"
        print(json.dumps({"status": "error", "message": msg}) if args.json else f"Error: {msg}")
        return 1

    try:
        with open(palette_path, "r", encoding="utf-8") as f:
            palette_data = json.load(f)
        palette_rgbs = [hex_to_rgb(c) for c in palette_data["colors"]]

        img = Image.open(image_path).convert("RGBA")
        remapped = remap_image(
            img,
            palette_rgbs,
            metric=args.metric,
            dither=args.dither,
            dither_strength=args.dither_strength,
        )

        output_path = Path(args.output)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        remapped.save(output_path)

        result = {
            "status": "ok",
            "input": str(image_path),
            "output": str(output_path),
            "palette": palette_data["name"],
            "palette_colors": len(palette_data["colors"]),
            "metric": args.metric,
            "dither": args.dither,
        }

        if args.json:
            print(json.dumps(result, indent=2))
        else:
            print(
                f"Remapped {image_path.name} → {output_path.name} "
                f"using {palette_data['name']} ({args.metric}, dither={args.dither})"
            )

        return 0

    except Exception as e:  # noqa: BLE001
        msg = str(e)
        print(json.dumps({"status": "error", "message": msg}) if args.json else f"Error: {msg}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
