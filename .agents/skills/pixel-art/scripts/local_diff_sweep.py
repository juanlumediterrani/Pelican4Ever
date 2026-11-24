#!/usr/bin/env python3
"""
Local Diffusion Parameter Sweep & Auto-Scoring Pipeline (ComfyUI HTTP API).

Runs a configurable parameter matrix against a running ComfyUI server
(SDXL + pixel-art-xl or FLUX.1 Kontext-dev GGUF), applies the skill's
post-diffusion pipeline (grid_snap.py -> palette_remap.py -> quality_audit.py),
scores every candidate objectively, and generates a labeled contact sheet + JSON.

Usage:
    python3 scripts/local_diff_sweep.py \
      --workflow sdxl \
      --prompt "pixel art retro tamagotchi robot mascot, centered, clean silhouette, solid white background" \
      --palette palettes/pico-8.json \
      --cfgs 5.5,7.0 --steps-list 20,26 --lora-weights 0.85,1.0 \
      --output-dir ./sweep_robot
"""

import argparse
import io
import itertools
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from PIL import Image, ImageDraw, ImageFont

SCRIPTS_DIR = Path(__file__).resolve().parent


def http_json(url: str, data: Optional[Dict[str, Any]] = None, timeout: int = 30) -> Any:
    body = None
    headers = {}
    if data is not None:
        body = json.dumps(data).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=body, headers=headers)
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


def http_bytes(url: str, timeout: int = 60) -> bytes:
    with urllib.request.urlopen(url, timeout=timeout) as resp:
        return resp.read()


def upload_image(
    comfy_url: str,
    image_path: Path,
    nearest_upscale_min: int = 0,
    pad_square_bg: Tuple[int, int, int] = (255, 255, 255),
) -> str:
    """Upload a local sprite/image to ComfyUI input/, flattening alpha onto a clean solid background."""
    img = Image.open(image_path).convert("RGBA")
    if nearest_upscale_min > 0 and max(img.size) < nearest_upscale_min:
        scale = max(1, (nearest_upscale_min * 4) // (max(img.size) * 5))
        scale = max(scale, nearest_upscale_min // max(img.size) if max(img.size) * 2 <= nearest_upscale_min else 1)
        if scale > 1:
            img = img.resize((img.width * scale, img.height * scale), Image.NEAREST)

    # Flatten transparency onto solid background and center in a clean square canvas when target >= max(size)
    side = max(nearest_upscale_min, img.width, img.height) if nearest_upscale_min > 0 else max(img.width, img.height)
    canvas = Image.new("RGBA", (side, side), (*pad_square_bg, 255))
    ox = (side - img.width) // 2
    oy = (side - img.height) // 2
    canvas.alpha_composite(img, (ox, oy))
    img = canvas.convert("RGB")

    buf = io.BytesIO()
    img.save(buf, format="PNG")
    png_bytes = buf.getvalue()

    boundary = f"----AntigravityBoundary{uuid.uuid4().hex}"
    filename = f"sweep_ref_{uuid.uuid4().hex[:8]}_{image_path.name}"
    parts = [
        f"--{boundary}\r\n"
        f'Content-Disposition: form-data; name="image"; filename="{filename}"\r\n'
        f"Content-Type: image/png\r\n\r\n".encode("utf-8"),
        png_bytes,
        f"\r\n--{boundary}\r\n"
        f'Content-Disposition: form-data; name="overwrite"\r\n\r\ntrue'
        f"\r\n--{boundary}--\r\n".encode("utf-8"),
    ]
    body = b"".join(parts)
    req = urllib.request.Request(
        f"{comfy_url.rstrip('/')}/upload/image",
        data=body,
        headers={"Content-Type": f"multipart/form-data; boundary={boundary}"},
    )
    with urllib.request.urlopen(req, timeout=30) as resp:
        res = json.loads(resp.read().decode("utf-8"))
    return res["name"]


def check_server(comfy_url: str) -> Dict[str, Any]:
    return http_json(f"{comfy_url.rstrip('/')}/system_stats", timeout=10)


def run_prompt_and_wait(comfy_url: str, workflow: Dict[str, Any], timeout_s: int = 900) -> bytes:
    """Queue workflow to ComfyUI /prompt, poll /history until finished, return output PNG bytes."""
    client_id = uuid.uuid4().hex
    queued = http_json(
        f"{comfy_url.rstrip('/')}/prompt",
        {"prompt": workflow, "client_id": client_id},
        timeout=30,
    )
    prompt_id = queued["prompt_id"]
    deadline = time.monotonic() + timeout_s

    while time.monotonic() < deadline:
        history = http_json(f"{comfy_url.rstrip('/')}/history/{prompt_id}", timeout=15)
        if prompt_id in history:
            entry = history[prompt_id]
            status = entry.get("status", {})
            if status.get("status_str") == "error":
                raise RuntimeError(f"ComfyUI execution error: {json.dumps(status, indent=2)}")
            outputs = entry.get("outputs", {})
            for node_out in outputs.values():
                images = node_out.get("images")
                if images:
                    info = images[0]
                    qs = urllib.parse.urlencode(
                        {
                            "filename": info["filename"],
                            "subfolder": info.get("subfolder", ""),
                            "type": info.get("type", "output"),
                        }
                    )
                    return http_bytes(f"{comfy_url.rstrip('/')}/view?{qs}", timeout=60)
            raise RuntimeError(f"ComfyUI finished prompt {prompt_id} without image outputs.")
        time.sleep(1.5)

    raise TimeoutError(f"Timed out waiting for ComfyUI prompt {prompt_id} after {timeout_s}s.")


def build_sdxl_workflow(
    prompt: str,
    negative: str,
    width: int,
    height: int,
    cfg: float,
    steps: int,
    lora_weight: float,
    sampler: str,
    scheduler: str,
    seed: int,
    checkpoint: str,
    vae: str,
    lora: str,
    ref_image_name: Optional[str] = None,
    denoise: float = 1.0,
    pixeloe_pixel_size: int = 0,
) -> Dict[str, Any]:
    wf: Dict[str, Any] = {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": checkpoint}},
        "2": {"class_type": "VAELoader", "inputs": {"vae_name": vae}},
        "3": {
            "class_type": "LoraLoaderModelOnly",
            "inputs": {"model": ["1", 0], "lora_name": lora, "strength_model": lora_weight},
        },
        "4": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["1", 1], "text": prompt}},
        "5": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["1", 1], "text": negative}},
    }

    if ref_image_name:
        wf["6"] = {"class_type": "LoadImage", "inputs": {"image": ref_image_name}}
        wf["7"] = {"class_type": "VAEEncode", "inputs": {"pixels": ["6", 0], "vae": ["2", 0]}}
        latent_link = ["7", 0]
        actual_denoise = denoise
    else:
        wf["6"] = {
            "class_type": "EmptyLatentImage",
            "inputs": {"width": width, "height": height, "batch_size": 1},
        }
        latent_link = ["6", 0]
        actual_denoise = 1.0

    wf["8"] = {
        "class_type": "KSampler",
        "inputs": {
            "model": ["3", 0],
            "positive": ["4", 0],
            "negative": ["5", 0],
            "latent_image": latent_link,
            "seed": seed,
            "steps": steps,
            "cfg": cfg,
            "sampler_name": sampler,
            "scheduler": scheduler,
            "denoise": actual_denoise,
        },
    }
    wf["9"] = {"class_type": "VAEDecode", "inputs": {"samples": ["8", 0], "vae": ["2", 0]}}
    img_out = ["9", 0]

    if pixeloe_pixel_size > 0:
        wf["10"] = {
            "class_type": "PixelOE",
            "inputs": {
                "pixel_size": pixeloe_pixel_size,
                "thickness": 2,
                "img": img_out,
                "mode": "contrast",
                "color_quant": False,
                "no_post_upscale": True,
                "num_colors": 64,
                "quant_mode": "kmeans",
                "dither_mode": "none",
                "weight_mapping": "current",
                "device": "default",
            },
        }
        img_out = ["10", 0]

    wf["11"] = {
        "class_type": "SaveImage",
        "inputs": {"images": img_out, "filename_prefix": "pixel_sweep_sdxl"},
    }
    return wf


def build_kontext_workflow(
    prompt: str,
    width: int,
    height: int,
    guidance: float,
    steps: int,
    sampler: str,
    scheduler: str,
    seed: int,
    unet_gguf: str,
    clip_l: str,
    t5xxl: str,
    vae: str,
    clip_device: str = "cpu",
    lora: Optional[str] = None,
    lora_weight: float = 1.0,
    ref_image_name: Optional[str] = None,
    pixeloe_pixel_size: int = 0,
) -> Dict[str, Any]:
    wf: Dict[str, Any] = {
        "1": {"class_type": "UnetLoaderGGUF", "inputs": {"unet_name": unet_gguf}},
        "2": {
            "class_type": "DualCLIPLoader",
            "inputs": {
                "clip_name1": clip_l,
                "clip_name2": t5xxl,
                "type": "flux",
                "device": clip_device,
            },
        },
        "3": {"class_type": "VAELoader", "inputs": {"vae_name": vae}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["2", 0], "text": prompt}},
        "5": {"class_type": "ConditioningZeroOut", "inputs": {"conditioning": ["4", 0]}},
    }

    model_link = ["1", 0]
    if lora:
        wf["12"] = {
            "class_type": "LoraLoaderModelOnly",
            "inputs": {"model": model_link, "lora_name": lora, "strength_model": lora_weight},
        }
        model_link = ["12", 0]

    pos_link = ["4", 0]
    if ref_image_name:
        wf["6"] = {"class_type": "LoadImage", "inputs": {"image": ref_image_name}}
        wf["7"] = {"class_type": "VAEEncode", "inputs": {"pixels": ["6", 0], "vae": ["3", 0]}}
        wf["8"] = {
            "class_type": "ReferenceLatent",
            "inputs": {"conditioning": pos_link, "latent": ["7", 0]},
        }
        pos_link = ["8", 0]

    wf["9"] = {
        "class_type": "FluxGuidance",
        "inputs": {"conditioning": pos_link, "guidance": guidance},
    }
    wf["10"] = {
        "class_type": "EmptySD3LatentImage",
        "inputs": {"width": width, "height": height, "batch_size": 1},
    }
    wf["11"] = {
        "class_type": "KSampler",
        "inputs": {
            "model": model_link,
            "positive": ["9", 0],
            "negative": ["5", 0],
            "latent_image": ["10", 0],
            "seed": seed,
            "steps": steps,
            "cfg": 1.0,
            "sampler_name": sampler,
            "scheduler": scheduler,
            "denoise": 1.0,
        },
    }
    wf["13"] = {"class_type": "VAEDecode", "inputs": {"samples": ["11", 0], "vae": ["3", 0]}}
    img_out = ["13", 0]

    if pixeloe_pixel_size > 0:
        wf["14"] = {
            "class_type": "PixelOE",
            "inputs": {
                "pixel_size": pixeloe_pixel_size,
                "thickness": 2,
                "img": img_out,
                "mode": "contrast",
                "color_quant": False,
                "no_post_upscale": True,
                "num_colors": 64,
                "quant_mode": "kmeans",
                "dither_mode": "none",
                "weight_mapping": "current",
                "device": "default",
            },
        }
        img_out = ["14", 0]

    wf["15"] = {
        "class_type": "SaveImage",
        "inputs": {"images": img_out, "filename_prefix": "pixel_sweep_kontext"},
    }
    return wf


def run_cli_json(cmd: List[str], allow_nonzero: bool = False) -> Tuple[int, Dict[str, Any]]:
    res = subprocess.run(cmd, capture_output=True, text=True)
    if res.returncode != 0 and not allow_nonzero:
        raise RuntimeError(f"Command failed ({res.returncode}): {' '.join(cmd)}\n{res.stderr}\n{res.stdout}")
    payload = json.loads(res.stdout) if res.stdout.strip().startswith("{") else {"raw_stdout": res.stdout}
    return res.returncode, payload


def extract_metrics(audit: Dict[str, Any]) -> Dict[str, Any]:
    """Flatten nested quality_audit.py JSON report fields."""
    aa = audit.get("antialiasing") or {}
    orph = audit.get("orphan_pixels") or {}
    pal = audit.get("palette") or {}
    mixels = audit.get("mixels") or {}
    return {
        "semi_transparent": aa.get("semi_transparent_pixels", 0),
        "orphans": orph.get("orphan_pixels", 0),
        "unique_colors": pal.get("unique_colors", 0),
        "off_palette_pixels": pal.get("off_palette_pixels", 0),
        "mixel_clean": mixels.get("mixel_clean", True),
        "dirty_blocks": mixels.get("dirty_blocks", 0),
        "dirty_block_ratio": mixels.get("dirty_block_ratio", 0.0),
    }


def score_audit(audit: Dict[str, Any], snap: Optional[Dict[str, Any]] = None) -> float:
    """Higher is better (max 100.0)."""
    m = extract_metrics(audit)
    score = 100.0
    if not m["mixel_clean"]:
        score -= 35.0 + min(30.0, m["dirty_block_ratio"] * 200.0)
    score -= min(25.0, m["semi_transparent"] * 0.5)
    score -= min(25.0, m["orphans"] * 1.5)
    score -= min(25.0, m["off_palette_pixels"] * 0.5)
    if snap and snap.get("cell_size_used", 2) <= 1 and min(snap.get("input_size", [0, 0])) >= 512:
        score -= 15.0
    if audit.get("status") != "pass":
        score -= 5.0
    return round(max(0.0, score), 2)


def make_contact_sheet(records: List[Dict[str, Any]], output_path: Path, tile_size: int = 256) -> None:
    if not records:
        return
    cols = min(3, len(records))
    rows = (len(records) + cols - 1) // cols
    cell_w = tile_size * 2 + 12
    cell_h = tile_size + 56
    pad = 16

    sheet = Image.new(
        "RGBA",
        (cols * cell_w + (cols + 1) * pad, rows * cell_h + (rows + 1) * pad),
        (22, 24, 30, 255),
    )
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default()

    for idx, rec in enumerate(records):
        r = idx // cols
        c = idx % cols
        x0 = pad + c * (cell_w + pad)
        y0 = pad + r * (cell_h + pad)

        raw_img = Image.open(rec["raw_path"]).convert("RGBA").resize((tile_size, tile_size), Image.NEAREST)
        fin_img = Image.open(rec["final_path"]).convert("RGBA").resize((tile_size, tile_size), Image.NEAREST)

        # Checkerboard backing behind final sprite so alpha cuts are obvious
        bg = Image.new("RGBA", (tile_size, tile_size), (38, 42, 52, 255))
        bg.alpha_composite(fin_img)

        sheet.paste(raw_img, (x0, y0))
        sheet.paste(bg, (x0 + tile_size + 8, y0))

        audit = rec["audit"]
        m = extract_metrics(audit)
        clean_flag = "OK" if m["mixel_clean"] else f"MIX({m['dirty_blocks']})"
        lbl1 = f"#{idx+1} [{rec['score']:.1f}] {rec['label']}"
        lbl2 = (
            f"snap:{rec['snap'].get('snapped_size')} cell:{rec['snap'].get('cell_size_used')} | "
            f"{clean_flag} | orph:{m['orphans']} | col:{m['unique_colors']}"
        )
        color = (130, 240, 150, 255) if audit.get("status") == "pass" else (245, 200, 110, 255)
        draw.text((x0, y0 + tile_size + 6), lbl1[:72], fill=color, font=font)
        draw.text((x0, y0 + tile_size + 24), lbl2[:72], fill=(210, 215, 225, 255), font=font)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(output_path)


def parse_csv_floats(val: str) -> List[float]:
    return [float(x.strip()) for x in val.split(",") if x.strip()]


def parse_csv_ints(val: str) -> List[int]:
    return [int(x.strip()) for x in val.split(",") if x.strip()]


def parse_csv_strs(val: str) -> List[str]:
    return [x.strip() for x in val.split(",") if x.strip()]


def main() -> None:
    parser = argparse.ArgumentParser(description="ComfyUI local diffusion parameter sweep for pixel art")
    parser.add_argument("--comfy-url", default="http://127.0.0.1:8188", help="ComfyUI base URL")
    parser.add_argument("--workflow", choices=["sdxl", "kontext"], default="sdxl")
    parser.add_argument("--prompt", required=True, help="Positive prompt")
    parser.add_argument(
        "--negative",
        default="blurry, smooth shading, anti-aliasing, photorealistic, 3d render, watermark, text, soft gradients",
        help="Negative prompt (SDXL)",
    )
    parser.add_argument("--ref-image", help="Optional reference sprite for img2img (SDXL) or ReferenceLatent (Kontext)")
    parser.add_argument("--ref-upscale-min", type=int, default=1024, help="Nearest-upscale small refs up to at least N px")
    parser.add_argument("--width", type=int, default=1024)
    parser.add_argument("--height", type=int, default=1024)
    parser.add_argument("--cfgs", default=None, help="CSV of CFG (SDXL default: 5.5,7.0) or FluxGuidance (Kontext default: 3.5)")
    parser.add_argument("--steps-list", default=None, help="CSV of step counts (SDXL default: 22; Kontext default: 20)")
    parser.add_argument("--lora-weights", default="0.85,1.0", help="CSV of LoRA strengths")
    parser.add_argument("--samplers", default=None, help="CSV of samplers (SDXL default: dpmpp_2m; Kontext default: euler)")
    parser.add_argument("--schedulers", default=None, help="CSV of schedulers (SDXL default: karras; Kontext default: simple)")
    parser.add_argument("--denoise-list", default="1.0", help="CSV of denoise strengths (SDXL img2img)")
    parser.add_argument("--seed", type=int, default=42)

    # Model overrides
    parser.add_argument("--sdxl-ckpt", default="sd_xl_base_1.0.safetensors")
    parser.add_argument("--sdxl-vae", default="sdxl_vae.safetensors")
    parser.add_argument("--sdxl-lora", default="pixel-art-xl.safetensors")
    parser.add_argument("--kontext-unet", default="flux1-kontext-dev-Q8_0.gguf")
    parser.add_argument("--kontext-clip-l", default="clip_l.safetensors")
    parser.add_argument("--kontext-t5", default="t5xxl_fp16.safetensors")
    parser.add_argument("--kontext-vae", default="ae.safetensors")
    parser.add_argument("--kontext-lora", default=None)
    parser.add_argument("--clip-device", choices=["cpu", "default"], default="cpu", help="Keep T5-XXL in RAM (cpu) or auto")
    parser.add_argument("--pixeloe-size", type=int, default=0, help="If >0, run PixelOE node inside ComfyUI before SaveImage")

    # Post-processing & QA
    parser.add_argument("--target-size", help="Force grid_snap output WxH (e.g. 64x64 or 128x128)")
    parser.add_argument("--cell-size", type=int, default=0, help="Force grid_snap cell size (0 = auto)")
    parser.add_argument(
        "--fallback-cell-size",
        type=int,
        default=None,
        help="Fallback cell size if auto returns 1 on >=512px images (default: 16 kontext, 8 sdxl; 0 disables)",
    )
    parser.add_argument("--snap-method", choices=["contrast", "median", "mode", "center"], default="contrast")
    parser.add_argument("--remove-checkerboard", action="store_true", default=True)
    parser.add_argument("--no-remove-checkerboard", dest="remove_checkerboard", action="store_false")
    parser.add_argument(
        "--palette",
        default=None,
        help="Palette JSON path or 'auto' to extract from --ref-image (defaults to 'auto' when --ref-image is set)",
    )
    parser.add_argument("--max-ref-colors", type=int, default=32, help="Max colors for --palette auto extraction")
    parser.add_argument("--dither", choices=["none", "bayer2", "bayer4"], default="none")
    parser.add_argument("--dither-strength", type=float, default=0.35)
    parser.add_argument("--output-dir", required=True, help="Directory for sweep outputs, sheet, and JSON")
    parser.add_argument("--timeout", type=int, default=900, help="Per-generation timeout in seconds")
    args = parser.parse_args()

    out_dir = Path(args.output_dir).resolve()
    raw_dir = out_dir / "raw"
    snap_dir = out_dir / "snapped"
    fin_dir = out_dir / "final"
    for d in (raw_dir, snap_dir, fin_dir):
        d.mkdir(parents=True, exist_ok=True)

    check_server(args.comfy_url)

    ref_name = None
    if args.ref_image:
        ref_name = upload_image(args.comfy_url, Path(args.ref_image), nearest_upscale_min=args.ref_upscale_min)

    palette_path = args.palette
    if (palette_path == "auto" or (palette_path is None and args.ref_image)) and args.ref_image:
        extracted_pal = out_dir / "ref_palette.json"
        _, pal_res = run_cli_json([
            sys.executable,
            str(SCRIPTS_DIR / "palette_extract.py"),
            "--image", str(Path(args.ref_image).resolve()),
            "--max-colors", str(args.max_ref_colors),
            "--output", str(extracted_pal),
            "--json",
        ])
        palette_path = str(extracted_pal)
        print(f"Auto-extracted {pal_res.get('extracted_colors')} colors from ref -> {extracted_pal}", file=sys.stderr)
    elif palette_path == "auto":
        palette_path = None

    if args.workflow == "kontext":
        cfgs = parse_csv_floats(args.cfgs or "3.5")
        steps_list = parse_csv_ints(args.steps_list or "20")
        samplers = parse_csv_strs(args.samplers or "euler")
        schedulers = parse_csv_strs(args.schedulers or "simple")
        fallback_cell = 16 if args.fallback_cell_size is None else args.fallback_cell_size
    else:
        cfgs = parse_csv_floats(args.cfgs or "5.5,7.0")
        steps_list = parse_csv_ints(args.steps_list or "22")
        samplers = parse_csv_strs(args.samplers or "dpmpp_2m")
        schedulers = parse_csv_strs(args.schedulers or "karras")
        fallback_cell = 8 if args.fallback_cell_size is None else args.fallback_cell_size

    lora_weights = parse_csv_floats(args.lora_weights) if (args.workflow == "sdxl" or args.kontext_lora) else [1.0]
    denoises = parse_csv_floats(args.denoise_list) if (args.workflow == "sdxl" and ref_name) else [1.0]

    combos = list(itertools.product(cfgs, steps_list, lora_weights, samplers, schedulers, denoises))
    records: List[Dict[str, Any]] = []

    for idx, (cfg, steps, lw, sampler, scheduler, denoise) in enumerate(combos, start=1):
        tag = f"{args.workflow}_c{cfg}_s{steps}_l{lw}_{sampler}_{scheduler}_d{denoise}".replace(".", "p")
        label = f"cfg={cfg} st={steps} lw={lw} {sampler}/{scheduler}"
        if denoise < 1.0:
            label += f" dn={denoise}"

        print(f"[{idx}/{len(combos)}] Generating {label}...", file=sys.stderr)
        t0 = time.monotonic()

        if args.workflow == "sdxl":
            wf = build_sdxl_workflow(
                prompt=args.prompt,
                negative=args.negative,
                width=args.width,
                height=args.height,
                cfg=cfg,
                steps=steps,
                lora_weight=lw,
                sampler=sampler,
                scheduler=scheduler,
                seed=args.seed,
                checkpoint=args.sdxl_ckpt,
                vae=args.sdxl_vae,
                lora=args.sdxl_lora,
                ref_image_name=ref_name,
                denoise=denoise,
                pixeloe_pixel_size=args.pixeloe_size,
            )
        else:
            wf = build_kontext_workflow(
                prompt=args.prompt,
                width=args.width,
                height=args.height,
                guidance=cfg,
                steps=steps,
                sampler=sampler,
                scheduler=scheduler,
                seed=args.seed,
                unet_gguf=args.kontext_unet,
                clip_l=args.kontext_clip_l,
                t5xxl=args.kontext_t5,
                vae=args.kontext_vae,
                clip_device=args.clip_device,
                lora=args.kontext_lora,
                lora_weight=lw,
                ref_image_name=ref_name,
                pixeloe_pixel_size=args.pixeloe_size,
            )

        png_bytes = run_prompt_and_wait(args.comfy_url, wf, timeout_s=args.timeout)
        elapsed = round(time.monotonic() - t0, 2)

        raw_path = raw_dir / f"{tag}.png"
        raw_path.write_bytes(png_bytes)

        snap_path = snap_dir / f"{tag}.png"
        snap_cmd = [
            sys.executable,
            str(SCRIPTS_DIR / "grid_snap.py"),
            "--image", str(raw_path),
            "--method", args.snap_method,
            "--output", str(snap_path),
            "--json",
        ]
        if args.target_size:
            snap_cmd.extend(["--target-size", args.target_size])
        elif args.cell_size > 0:
            snap_cmd.extend(["--cell-size", str(args.cell_size)])
        if args.remove_checkerboard:
            snap_cmd.append("--remove-checkerboard")
        _, snap_info = run_cli_json(snap_cmd)

        if (
            not args.target_size
            and args.cell_size == 0
            and fallback_cell > 0
            and snap_info.get("cell_size_used", 0) <= 1
            and min(snap_info.get("input_size", [0, 0])) >= 512
        ):
            _, snap_info = run_cli_json(snap_cmd + ["--cell-size", str(fallback_cell)])

        final_path = fin_dir / f"{tag}.png"
        if palette_path:
            remap_cmd = [
                sys.executable,
                str(SCRIPTS_DIR / "palette_remap.py"),
                "--image", str(snap_path),
                "--palette", palette_path,
                "--metric", "oklab",
                "--dither", args.dither,
                "--dither-strength", str(args.dither_strength),
                "--output", str(final_path),
                "--json",
            ]
            run_cli_json(remap_cmd)
        else:
            Image.open(snap_path).save(final_path)

        audit_cmd = [
            sys.executable,
            str(SCRIPTS_DIR / "quality_audit.py"),
            "--image", str(final_path),
            "--check-mixels",
            "--json",
        ]
        if palette_path:
            audit_cmd.extend(["--palette", palette_path])
        _, audit_info = run_cli_json(audit_cmd, allow_nonzero=True)

        sc = score_audit(audit_info, snap=snap_info)
        records.append(
            {
                "tag": tag,
                "label": label,
                "elapsed_s": elapsed,
                "params": {
                    "workflow": args.workflow,
                    "cfg": cfg,
                    "steps": steps,
                    "lora_weight": lw,
                    "sampler": sampler,
                    "scheduler": scheduler,
                    "denoise": denoise,
                    "seed": args.seed,
                },
                "raw_path": str(raw_path),
                "snap_path": str(snap_path),
                "final_path": str(final_path),
                "snap": snap_info,
                "audit": audit_info,
                "score": sc,
            }
        )

    records.sort(key=lambda r: (-r["score"], extract_metrics(r["audit"])["orphans"]))
    sheet_path = out_dir / "sweep_contact_sheet.png"
    make_contact_sheet(records, sheet_path)

    summary = {
        "workflow": args.workflow,
        "prompt": args.prompt,
        "count": len(records),
        "best": records[0] if records else None,
        "contact_sheet": str(sheet_path),
        "results": records,
    }
    summary_path = out_dir / "sweep_results.json"
    summary_path.write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(json.dumps({"best_tag": records[0]["tag"], "best_score": records[0]["score"], "contact_sheet": str(sheet_path), "results_json": str(summary_path)}, indent=2))


if __name__ == "__main__":
    main()
