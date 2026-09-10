"""Normalize player beta-01 ImageGen sources into auditable actor strips.

This helper is intentionally technical. It preserves each generator output in
the batch raw directory, maps the subject to the fixed actor contract, and
records that a single generated pose was repeated when a clip source did not
contain independently authored frames. It does not claim visual or motion
approval.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import shutil
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage


PROJECT = Path(r"C:\ws\asset-production_system")
CANONICAL_SIZE = (1254, 1254)
CANVAS = (64, 64)
PIVOT = [32, 56]
BODY_SIZE = (24, 42)
PALETTE_HEX = [
    "#161820", "#272B36", "#414653", "#626777",
    "#655344", "#9B8564", "#D0B88A", "#F1DDB0",
    "#754719", "#B87825", "#E9B94A", "#FFF1B8",
    "#332C40", "#54435D", "#7A607D", "#AA879E",
]
PALETTE = np.array(
    [[int(c[i : i + 2], 16) for i in (1, 3, 5)] for c in PALETTE_HEX],
    dtype=np.int32,
)


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def rel(path: Path) -> str:
    return path.resolve().relative_to(PROJECT.resolve()).as_posix()


def canonicalize(source: Image.Image) -> tuple[Image.Image, dict]:
    rgba = source.convert("RGBA")
    original_size = list(rgba.size)
    scale = min(CANONICAL_SIZE[0] / rgba.width, CANONICAL_SIZE[1] / rgba.height)
    resized_size = (
        max(1, round(rgba.width * scale)),
        max(1, round(rgba.height * scale)),
    )
    resized = rgba.resize(resized_size, Image.Resampling.NEAREST)
    canvas = Image.new("RGBA", CANONICAL_SIZE, (0, 0, 0, 0))
    offset = ((CANONICAL_SIZE[0] - resized.width) // 2,
              (CANONICAL_SIZE[1] - resized.height) // 2)
    canvas.alpha_composite(resized, offset)
    return canvas, {
        "originalSize": original_size,
        "originalMode": source.mode,
        "canonicalSize": list(CANONICAL_SIZE),
        "fit": "nearest; preserve aspect ratio; centered on transparent canvas",
        "scale": scale,
        "resizedSize": list(resized_size),
        "offsetXY": list(offset),
    }


def foreground_mask(image: Image.Image) -> tuple[np.ndarray, dict]:
    rgba = np.asarray(image.convert("RGBA"), dtype=np.uint8)
    alpha = rgba[:, :, 3]
    if int(alpha.max()) > 0 and int(alpha.min()) < 128:
        mask = alpha >= 128
        return mask, {
            "method": "source alpha threshold >=128",
            "alphaInputRange": [int(alpha.min()), int(alpha.max())],
        }

    rgb = rgba[:, :, :3].astype(np.int16)
    spread = rgb.max(axis=2) - rgb.min(axis=2)
    luminance = rgb.mean(axis=2)
    border = np.concatenate((rgb[0], rgb[-1], rgb[:, 0], rgb[:, -1]), axis=0)
    border_median = np.median(border, axis=0)
    border_luminance = float(np.median(border.mean(axis=1)))
    border_spread = float(np.median(border.max(axis=1) - border.min(axis=1)))
    if border_luminance > 140 and border_spread < 30:
        # ImageGen sometimes bakes a light gray/white transparency checkerboard.
        # It is not alpha, so remove every near-neutral light pixel directly.
        mask = ~((spread <= 35) & (luminance >= 110))
        return mask, {
            "method": "opaque light checkerboard removal",
            "thresholds": {"channelSpreadLte": 35, "meanGte": 110},
            "borderMedian": [int(x) for x in border_median],
            "alphaInputRange": [int(alpha.min()), int(alpha.max())],
        }
    if border_luminance < 90 and border_spread < 35:
        # For a uniform dark matte, remove only matte-colored pixels that are
        # connected to the image edge; enclosed dark pixels remain part of the actor.
        distance = np.sqrt(((rgb - border_median[None, None, :]) ** 2).sum(axis=2))
        candidate = (distance <= 55) & (spread <= 45) & (luminance <= 110)
        labels, _ = ndimage.label(candidate, structure=np.ones((3, 3), dtype=np.uint8))
        edge_labels = np.unique(np.concatenate((labels[0], labels[-1], labels[:, 0], labels[:, -1])))
        mask = ~np.isin(labels, edge_labels)
        return mask, {
            "method": "opaque dark matte edge-connected removal",
            "thresholds": {"borderDistanceLte": 55, "channelSpreadLte": 45, "meanLte": 110},
            "borderMedian": [int(x) for x in border_median],
            "alphaInputRange": [int(alpha.min()), int(alpha.max())],
        }
    mask = (spread > 12) | (luminance < 180)
    return mask, {
        "method": "fallback colored-or-dark foreground mask for opaque source",
        "thresholds": {"channelSpreadGt": 12, "meanLt": 180},
        "borderMedian": [int(x) for x in border_median],
        "alphaInputRange": [int(alpha.min()), int(alpha.max())],
    }


def crop_for_bbox(mask: np.ndarray, render_mode: str = "body") -> tuple[tuple[int, int, int, int], dict]:
    ys, xs = np.where(mask)
    if len(xs) == 0:
        raise ValueError("foreground mask is empty")
    left, right = int(xs.min()), int(xs.max() + 1)
    top, bottom = int(ys.min()), int(ys.max() + 1)
    if render_mode == "content":
        padding_x, padding_top, padding_bottom = 12, 12, 8
        crop_left = max(0, left - padding_x)
        crop_top = max(0, top - padding_top)
        crop_right = min(CANONICAL_SIZE[0], right + padding_x)
        crop_bottom = min(CANONICAL_SIZE[1], bottom + padding_bottom)
        return (crop_left, crop_top, crop_right, crop_bottom), {
            "sourceBBoxXYXY": [left, top, right, bottom],
            "cropRectXYXY": [crop_left, crop_top, crop_right, crop_bottom],
            "cropSize": [crop_right - crop_left, crop_bottom - crop_top],
            "footAlignment": "source foreground bottom mapped to actor pivot y=56",
            "renderMode": "content; preserve weapon silhouette within actor gutter",
        }
    crop_w, crop_h = 464, 821
    if right - left > crop_w or bottom - top > crop_h:
        crop_w = max(crop_w, right - left + 16)
        crop_h = max(crop_h, bottom - top + 16)
    crop_w = min(crop_w, CANONICAL_SIZE[0])
    crop_h = min(crop_h, CANONICAL_SIZE[1])
    cx = (left + right) / 2
    crop_left = int(round(cx - crop_w / 2))
    crop_bottom = bottom
    crop_top = crop_bottom - crop_h
    crop_left = max(0, min(CANONICAL_SIZE[0] - crop_w, crop_left))
    crop_top = max(0, min(CANONICAL_SIZE[1] - crop_h, crop_top))
    crop_right = crop_left + crop_w
    crop_bottom = crop_top + crop_h
    return (crop_left, crop_top, crop_right, crop_bottom), {
        "sourceBBoxXYXY": [left, top, right, bottom],
        "cropRectXYXY": [crop_left, crop_top, crop_right, crop_bottom],
        "cropSize": [crop_w, crop_h],
        "footAlignment": "source foreground bottom mapped to actor pivot y=56",
    }


def normalize(source: Path, output: Path, recipe_path: Path, asset_id: str,
              frame_count: int, durations: list[int], raw_path: Path,
              render_mode: str = "body") -> dict:
    with Image.open(source) as opened:
        canonical, canonical_meta = canonicalize(opened)
        mask, mask_meta = foreground_mask(canonical)
    crop_rect, crop_meta = crop_for_bbox(mask, render_mode)
    crop = canonical.crop(crop_rect)
    crop_rgb = np.asarray(crop.convert("RGB"), dtype=np.int32)
    visible = mask[crop_rect[1]:crop_rect[3], crop_rect[0]:crop_rect[2]]
    distance = ((crop_rgb[:, :, None, :] - PALETTE[None, None, :, :]) ** 2).sum(axis=3)
    mapped = PALETTE[distance.argmin(axis=2)].astype(np.uint8)
    rgba = np.zeros((crop.height, crop.width, 4), dtype=np.uint8)
    rgba[:, :, :3] = mapped
    rgba[:, :, 3] = visible.astype(np.uint8) * 255
    if render_mode == "content":
        max_size = (60, 54)
        fit = min(max_size[0] / max(1, crop.width), max_size[1] / max(1, crop.height))
        scaled_size = (max(1, round(crop.width * fit)), max(1, round(crop.height * fit)))
        scaled = Image.fromarray(rgba, "RGBA").resize(scaled_size, Image.Resampling.NEAREST)
        placement = ((64 - scaled.width) // 2, 56 - scaled.height)
        scaled_source_window = list(scaled.size)
    else:
        scaled = Image.fromarray(rgba, "RGBA").resize(BODY_SIZE, Image.Resampling.NEAREST)
        placement = (20, 14)
        scaled_source_window = list(BODY_SIZE)
    frame = Image.new("RGBA", CANVAS, (0, 0, 0, 0))
    frame.alpha_composite(scaled, placement)
    frame.save(output, "PNG", optimize=False)

    with Image.open(output) as checked:
        checked = checked.convert("RGBA")
        bbox = checked.getchannel("A").getbbox()
        visible_colors = sorted({tuple(px[:3]) for px in checked.getdata() if px[3]})
        alpha_values = sorted({int(px[3]) for px in checked.getdata()})
    recipe = {
        "schemaVersion": 1,
        "recipeVersion": "source-to-runtime.v2.player-beta01",
        "assetId": asset_id,
        "source": rel(raw_path),
        "sourceSha256": sha256(raw_path),
        "sourceToRuntime": {
            "canonicalization": canonical_meta,
            "mask": mask_meta,
            "crop": crop_meta,
            "resize": "nearest",
            "renderMode": render_mode,
            "scaledSourceWindow": scaled_source_window,
            "placementXY": list(placement),
            "canvas": list(CANVAS),
            "pivot": PIVOT,
            "palette": PALETTE_HEX,
            "alpha": "binary 0/255",
            "sharedAcrossClip": True,
        },
        "frameGeneration": {
            "frameCount": frame_count,
            "frameDurationMs": durations,
            "method": "one generated source pose repeated across strip",
            "motionQa": False,
            "note": "Repeated frames are a technical candidate only; motion continuity remains pending user review.",
        },
        "output": rel(output),
        "outputSha256": sha256(output),
        "outputChecks": {
            "mode": "RGBA",
            "size": list(CANVAS),
            "alphaValues": alpha_values,
            "bboxXYXY": list(bbox) if bbox else None,
            "visibleColors": len(visible_colors),
            "withinTwoPixelGutter": bool(bbox and bbox[0] >= 2 and bbox[1] >= 2 and bbox[2] <= 62 and bbox[3] <= 62),
        },
    }
    recipe_path.parent.mkdir(parents=True, exist_ok=True)
    recipe_path.write_text(json.dumps(recipe, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return recipe


def frame_checks(path: Path, frame_count: int, durations: list[int]) -> list[dict]:
    with Image.open(path) as opened:
        strip = opened.convert("RGBA")
    result = []
    for index in range(frame_count):
        frame = strip.crop((index * 64, 0, (index + 1) * 64, 64))
        bbox = frame.getchannel("A").getbbox()
        visible = {tuple(px[:3]) for px in frame.getdata() if px[3]}
        result.append({
            "frame": index,
            "size": [64, 64],
            "mode": "RGBA",
            "alphaValues": sorted({int(px[3]) for px in frame.getdata()}),
            "bboxXYXY": list(bbox) if bbox else None,
            "visibleColors": len(visible),
            "binaryAlpha": all(px[3] in (0, 255) for px in frame.getdata()),
            "withinTwoPixelGutter": bool(bbox and bbox[0] >= 2 and bbox[1] >= 2 and bbox[2] <= 62 and bbox[3] <= 62),
            "durationMs": durations[index],
        })
    return result


def make_preview(batch_dir: Path, entries: list[dict], output: Path) -> None:
    scale = 3
    cell_w, cell_h = 64 * scale, 64 * scale
    cols = 4
    rows = max(1, math.ceil(len(entries) / cols))
    canvas = Image.new("RGBA", (cols * cell_w, rows * (cell_h + 18)), (20, 22, 28, 255))
    draw = ImageDraw.Draw(canvas)
    try:
        font = ImageFont.load_default()
    except Exception:
        font = None
    for index, entry in enumerate(entries):
        with Image.open(batch_dir / entry["file"]) as opened:
            frame = opened.convert("RGBA").crop((0, 0, 64, 64))
        x = (index % cols) * cell_w
        y = (index // cols) * (cell_h + 18)
        bg = (35, 38, 48, 255) if index % 2 == 0 else (232, 229, 218, 255)
        draw.rectangle((x, y, x + cell_w - 1, y + cell_h + 17), fill=bg)
        canvas.alpha_composite(frame.resize((cell_w, cell_h), Image.Resampling.NEAREST), (x, y))
        color = (240, 240, 240, 255) if index % 2 == 0 else (30, 30, 30, 255)
        draw.text((x + 2, y + cell_h + 2), entry["assetId"], fill=color, font=font)
    output.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(output, "PNG", optimize=False)


def process(jobs_path: Path, batch_dir: Path, manifest_path: Path, profile: str) -> dict:
    jobs = json.loads(jobs_path.read_text(encoding="utf-8-sig"))
    if not isinstance(jobs, list) or not jobs:
        raise ValueError("jobs file must contain a non-empty list")
    raw_dir = batch_dir / "raw"
    out_dir = batch_dir / "outputs"
    recipe_dir = batch_dir / "recipes"
    raw_dir.mkdir(parents=True, exist_ok=True)
    out_dir.mkdir(parents=True, exist_ok=True)
    recipe_dir.mkdir(parents=True, exist_ok=True)
    manifest_assets = []
    for job in jobs:
        asset_id = job["assetId"]
        source = Path(job["source"])
        if not source.is_file():
            raise ValueError(f"Missing generator source: {source}")
        local_job_id = job["localJobId"]
        raw_path = raw_dir / f"{asset_id}.source-{local_job_id}.png"
        shutil.copy2(source, raw_path)
        output = out_dir / f"{asset_id}.png"
        recipe_path = recipe_dir / f"{asset_id}.recipe.json"
        frame_count = int(job["frameCount"])
        durations = [int(x) for x in job["durations"]]
        recipe = normalize(source, output, recipe_path, asset_id, frame_count, durations, raw_path, job.get("renderMode", "body"))
        with Image.open(output) as first:
            frame = first.convert("RGBA")
        strip = Image.new("RGBA", (64 * frame_count, 64), (0, 0, 0, 0))
        for index in range(frame_count):
            strip.alpha_composite(frame, (index * 64, 0))
        strip.save(output, "PNG", optimize=False)
        checks = frame_checks(output, frame_count, durations)
        qa = {
            "technical": True,
            "visual": False,
            "frameIsolation": all(x["binaryAlpha"] and x["withinTwoPixelGutter"] for x in checks),
            "motion": False,
            "inEngine": False,
            "evidencePaths": ["preview/contact-sheet.png", f"recipes/{asset_id}.recipe.json"],
            "note": "Technical candidate only; one generated pose repeated across frames, so motion and user review remain pending.",
        }
        entry = {
            "assetId": asset_id,
            "file": f"outputs/{asset_id}.png",
            "sha256": sha256(output),
            "role": "actor",
            "representation": "ui",
            "rank": None,
            "direction": job["direction"],
            "clip": job["clip"],
            "frameSize": [64, 64],
            "frameCount": frame_count,
            "pivot": PIVOT,
            "frameDurationMs": durations,
            "source": {
                "tool": "built-in image_gen",
                "model": None,
                "requestId": None,
                "localJobId": local_job_id,
                "referenceHashes": job["referenceHashes"],
                "rawOutputHint": job["rawOutputHint"],
                "prompt": job["prompt"],
            },
            "referenceIdsUsed": ["style_master_scene", "player_scale_master"],
            "masterIdsUsed": [
                {"masterId": "style_master_scene", "sha256": job["referenceHashes"][0]},
                {"masterId": "player_scale_master", "sha256": job["referenceHashes"][1]},
            ],
            "status": "review_ready",
            "qa": qa,
            "normalization": {
                "rawSourcePath": rel(raw_path),
                "rawSourceSha256": sha256(raw_path),
                "recipe": rel(recipe_path),
                "checks": checks,
            },
        }
        manifest_assets.append(entry)
        recipe["outputSha256"] = sha256(output)
        recipe["strip"] = {"size": [64 * frame_count, 64], "sha256": sha256(output)}
        recipe_path.write_text(json.dumps(recipe, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    preview_path = batch_dir / "preview" / "contact-sheet.png"
    make_preview(batch_dir, manifest_assets, preview_path)
    manifest = {
        "schemaVersion": 1,
        "styleId": "svm-ash-soulfire",
        "styleVersion": "0.2-calibrated",
        "batchId": batch_dir.name,
            "profile": profile,
        "group": "player",
        "generatedAt": datetime.now(timezone.utc).isoformat(),
        "technicalNote": "Generated source candidates normalized to actor contract. Visual, motion and in-engine review remain pending.",
        "assets": manifest_assets,
        "preview": {"file": "preview/contact-sheet.png", "sha256": sha256(preview_path)},
    }
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return {
        "manifest": rel(manifest_path),
        "preview": rel(preview_path),
        "assets": [{"assetId": a["assetId"], "file": a["file"], "sha256": a["sha256"]} for a in manifest_assets],
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--jobs", required=True, type=Path)
    parser.add_argument("--batch-dir", required=True, type=Path)
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--profile", default="beta_01")
    args = parser.parse_args()
    result = process(args.jobs.resolve(), args.batch_dir.resolve(), args.manifest.resolve(), args.profile)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
