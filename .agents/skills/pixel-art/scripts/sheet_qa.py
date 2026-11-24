#!/usr/bin/env python3
"""
Temporal and seamless-tiling QA for pixel art sprite sheets and tilesets.

Modes:
  walk-sheet : Splits a sprite sheet into cols x rows frames and checks foot-baseline
               drift, center-of-mass jitter, binary alpha, optional bottom padding, and
               Stardew Valley / Fashion Sense Head-Lock discipline across 4-frame rows.
  tile-wrap  : Checks seamless 4-edge wrap continuity between opposite tile borders
               (col 0 vs col W-1, row 0 vs row H-1).

Usage:
    python scripts/sheet_qa.py --mode walk-sheet --image body.png --cols 4 --rows 4 --head-lock --json
    python scripts/sheet_qa.py --mode walk-sheet --image sheet.png --cols 4 --rows 1 --bottom-padding 12 --json
    python scripts/sheet_qa.py --mode tile-wrap --image grass_tile.png --json
    python scripts/sheet_qa.py --help
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image


def analyze_walk_sheet(
    rgba: np.ndarray,
    cols: int,
    rows: int,
    head_lock: bool = False,
    head_rows: int = 14,
    max_foot_drift: int = 2,
    bottom_padding: int | None = None,
) -> dict:
    h, w, _ = rgba.shape
    if w % cols != 0 or h % rows != 0:
        return {
            "status": "fail",
            "error": f"Sheet {w}x{h} is not divisible by {cols}x{rows}",
        }

    cw, ch = w // cols, h // rows
    issues: list[str] = []
    row_reports: list[dict] = []

    for r in range(rows):
        frames_info = []
        foot_ys = []
        cxs = []
        frame_masks = []

        for c in range(cols):
            cell = rgba[r * ch : (r + 1) * ch, c * cw : (c + 1) * cw]
            alpha = cell[:, :, 3]
            semi = int(np.sum((alpha > 0) & (alpha < 255)))
            opaque = alpha > 0
            ys, xs = np.where(opaque)
            if len(ys) == 0:
                frames_info.append({"col": c, "empty": True, "semi_transparent": semi})
                frame_masks.append(opaque)
                continue

            min_x, max_x = int(xs.min()), int(xs.max())
            min_y, max_y = int(ys.min()), int(ys.max())
            cx = round(float(xs.mean()), 2)
            cy = round(float(ys.mean()), 2)
            pad_bottom = ch - 1 - max_y
            foot_ys.append(max_y)
            cxs.append(cx)
            frame_masks.append(opaque)

            if semi > 0:
                issues.append(f"row {r} col {c}: {semi} semi-transparent pixels")
            if bottom_padding is not None and pad_bottom != bottom_padding:
                issues.append(f"row {r} col {c}: bottom padding is {pad_bottom}px (expected {bottom_padding}px)")

            frames_info.append(
                {
                    "col": c,
                    "empty": False,
                    "bbox": [min_x, min_y, max_x, max_y],
                    "com": [cx, cy],
                    "foot_y": max_y,
                    "bottom_padding": pad_bottom,
                    "semi_transparent": semi,
                }
            )

        foot_drift = (max(foot_ys) - min(foot_ys)) if foot_ys else 0
        cx_drift = round(max(cxs) - min(cxs), 2) if cxs else 0.0
        if foot_drift > max_foot_drift:
            issues.append(f"row {r}: foot baseline drift {foot_drift}px > {max_foot_drift}px")

        head_lock_report = None
        if head_lock and cols >= 4 and len(frame_masks) >= 4:
            # Frame 0 head silhouette vs Step A (1) and Step B (3) allowing +0 or +1px vertical bounce
            hr = min(head_rows, ch)
            ref_head = frame_masks[0][:hr, :]
            step_scores = {}
            for step_col in (1, 3):
                step_mask = frame_masks[step_col]
                best_iou = 0.0
                best_dy = 0
                for dy in (0, 1):
                    cand = step_mask[dy : dy + hr, :]
                    if cand.shape != ref_head.shape:
                        continue
                    union = int(np.sum(ref_head | cand))
                    inter = int(np.sum(ref_head & cand))
                    iou = (inter / union) if union > 0 else 1.0
                    if iou > best_iou:
                        best_iou = iou
                        best_dy = dy
                step_scores[f"col_{step_col}"] = {"best_dy": best_dy, "head_iou": round(best_iou, 3)}
                if best_iou < 0.75:
                    issues.append(
                        f"row {r} col {step_col}: Head-Lock IoU {best_iou:.2f} < 0.75 (possible head jitter)"
                    )
            head_lock_report = step_scores

        row_reports.append(
            {
                "row": r,
                "foot_drift_px": foot_drift,
                "com_x_drift_px": cx_drift,
                "head_lock": head_lock_report,
                "frames": frames_info,
            }
        )

    return {
        "status": "pass" if not issues else "fail",
        "mode": "walk-sheet",
        "sheet_size": [w, h],
        "cell_size": [cw, ch],
        "cols": cols,
        "rows": rows,
        "issues": issues,
        "row_reports": row_reports,
    }


def analyze_tile_wrap(rgba: np.ndarray, max_edge_ratio: float = 1.85) -> dict:
    """Measure opposite-edge color discontinuity relative to interior neighbor diffs."""
    rgb = rgba[:, :, :3].astype(np.float32)
    h, w, _ = rgb.shape

    # Internal mean adjacent step
    internal_x = float(np.abs(rgb[:, 1:, :] - rgb[:, :-1, :]).mean()) + 1e-6
    internal_y = float(np.abs(rgb[1:, :, :] - rgb[:-1, :, :]).mean()) + 1e-6

    # Seam step across wrap boundary (col W-1 -> col 0, row H-1 -> row 0)
    seam_x = float(np.abs(rgb[:, 0, :] - rgb[:, -1, :]).mean())
    seam_y = float(np.abs(rgb[0, :, :] - rgb[-1, :, :]).mean())

    ratio_x = round(seam_x / internal_x, 3)
    ratio_y = round(seam_y / internal_y, 3)

    issues = []
    if ratio_x > max_edge_ratio:
        issues.append(f"Horizontal wrap seam discontinuity ratio {ratio_x} > {max_edge_ratio}")
    if ratio_y > max_edge_ratio:
        issues.append(f"Vertical wrap seam discontinuity ratio {ratio_y} > {max_edge_ratio}")

    return {
        "status": "pass" if not issues else "fail",
        "mode": "tile-wrap",
        "tile_size": [w, h],
        "horizontal_seam_delta": round(seam_x, 2),
        "vertical_seam_delta": round(seam_y, 2),
        "horizontal_ratio": ratio_x,
        "vertical_ratio": ratio_y,
        "max_edge_ratio": max_edge_ratio,
        "issues": issues,
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Temporal walk-sheet and seamless tile-wrap QA for pixel art."
    )
    parser.add_argument("--mode", required=True, choices=["walk-sheet", "tile-wrap"], help="QA mode")
    parser.add_argument("--image", required=True, help="Input PNG path")
    parser.add_argument("--cols", type=int, default=4, help="Sprite sheet columns (default: 4)")
    parser.add_argument("--rows", type=int, default=4, help="Sprite sheet rows (default: 4)")
    parser.add_argument("--head-lock", action="store_true", help="Verify Stardew Head-Lock silhouette stability")
    parser.add_argument("--head-rows", type=int, default=14, help="Top pixel rows forming head region (default: 14)")
    parser.add_argument("--max-foot-drift", type=int, default=2, help="Max allowed vertical foot drift px (default: 2)")
    parser.add_argument("--bottom-padding", type=int, help="Expected bottom padding in px (e.g. 12 for Tovarishchagotchi)")
    parser.add_argument("--max-edge-ratio", type=float, default=1.85, help="Max seam/interior ratio for tile-wrap (default: 1.85)")
    parser.add_argument("--json", action="store_true", help="Output result as JSON")
    args = parser.parse_args()

    path = Path(args.image)
    if not path.is_file():
        msg = f"Image not found: {args.image}"
        print(json.dumps({"status": "error", "message": msg}) if args.json else f"Error: {msg}", file=sys.stderr)
        return 1

    rgba = np.asarray(Image.open(path).convert("RGBA"))
    if args.mode == "walk-sheet":
        report = analyze_walk_sheet(
            rgba,
            cols=args.cols,
            rows=args.rows,
            head_lock=args.head_lock,
            head_rows=args.head_rows,
            max_foot_drift=args.max_foot_drift,
            bottom_padding=args.bottom_padding,
        )
    else:
        report = analyze_tile_wrap(rgba, max_edge_ratio=args.max_edge_ratio)

    report["file"] = str(path)
    if args.json:
        print(json.dumps(report, indent=2))
    else:
        print(f"Sheet QA ({args.mode}): {report['status'].upper()} — {path.name}")
        for issue in report.get("issues", []):
            print(f"  ⚠ {issue}")

    return 0 if report["status"] == "pass" else 1


if __name__ == "__main__":
    sys.exit(main())
