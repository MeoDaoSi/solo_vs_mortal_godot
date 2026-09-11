#!/usr/bin/env python3
"""Normalize real ImageGen Player walk poses into fixed-pivot Godot strips.

This is an asset-production utility, not a gameplay test.  It keeps every raw
source image, records its masking/normalization transform, and intentionally
uses one scale for all frames: per-frame fitting would create scale jitter.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import numpy
from PIL import Image
from scipy.ndimage import binary_propagation


CANVAS = (64, 64)
PIVOT = (32, 56)
CANONICAL_SIZE = (1254, 1254)
TARGET_BODY_HEIGHT = 42
PALETTE = (
    "#161820", "#272B36", "#414653", "#626777",
    "#655344", "#9B8564", "#D0B88A", "#F1DDB0",
    "#754719", "#B87825", "#E9B94A", "#FFF1B8",
    "#332C40", "#54435D", "#7A607D", "#AA879E",
)
PALETTE_RGB = tuple(tuple(bytes.fromhex(value[1:])) for value in PALETTE)
DIRECTIONS = ("s", "w", "e", "n")


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def canonicalize(source: Image.Image) -> tuple[Image.Image, dict]:
    source = source.convert("RGBA")
    old_size = source.size
    if old_size == CANONICAL_SIZE:
        return source, {
            "originalSize": old_size,
            "canonicalSize": CANONICAL_SIZE,
            "fit": "identity",
            "scale": 1.0,
            "resizedSize": old_size,
            "offsetXY": (0, 0),
        }

    factor = min(CANONICAL_SIZE[0] / old_size[0], CANONICAL_SIZE[1] / old_size[1])
    resized_size = (round(old_size[0] * factor), round(old_size[1] * factor))
    resized = source.resize(resized_size, Image.Resampling.NEAREST)
    offset = ((CANONICAL_SIZE[0] - resized_size[0]) // 2, (CANONICAL_SIZE[1] - resized_size[1]) // 2)
    canvas = Image.new("RGBA", CANONICAL_SIZE, (0, 0, 0, 0))
    canvas.alpha_composite(resized, offset)
    return canvas, {
        "originalSize": old_size,
        "canonicalSize": CANONICAL_SIZE,
        "fit": "nearest; preserve aspect ratio; centered on transparent canvas",
        "scale": factor,
        "resizedSize": resized_size,
        "offsetXY": offset,
    }


def foreground_mask(image: Image.Image) -> Image.Image:
    pixels = numpy.asarray(image)
    rgb = pixels[..., :3]
    alpha = pixels[..., 3]
    channel_min = rgb.min(axis=2)
    channel_max = rgb.max(axis=2)
    # Generated previews occasionally bake either a checkerboard or pure-black
    # display backdrop. Restrict checkerboard detection to bright neutrals and
    # remove only edge-connected candidates, preserving character highlights.
    candidates = (
        (alpha < 128)
        | ((channel_min >= 120) & ((channel_max - channel_min) <= 30))
        | (channel_max <= 6)
    )
    seed = numpy.zeros(candidates.shape, dtype=bool)
    seed[0, :] = candidates[0, :]
    seed[-1, :] = candidates[-1, :]
    seed[:, 0] |= candidates[:, 0]
    seed[:, -1] |= candidates[:, -1]
    background = binary_propagation(seed, mask=candidates)
    foreground = (alpha >= 128) & ~background
    return Image.fromarray((foreground * 255).astype(numpy.uint8), "L")


def palette_quantize(image: Image.Image) -> Image.Image:
    out = Image.new("RGBA", image.size, (0, 0, 0, 0))
    input_pixels = image.load()
    output_pixels = out.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = input_pixels[x, y]
            if a < 128:
                continue
            nearest = min(PALETTE_RGB, key=lambda color: (r - color[0]) ** 2 + (g - color[1]) ** 2 + (b - color[2]) ** 2)
            output_pixels[x, y] = (*nearest, 255)
    return out


def save_mask(mask: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    mask.save(path)


def make_frame(canonical: Image.Image, mask: Image.Image, scale: float, bbox: tuple[int, int, int, int]) -> Image.Image:
    masked = canonical.copy()
    masked.putalpha(mask)
    scaled_size = (round(CANONICAL_SIZE[0] * scale), round(CANONICAL_SIZE[1] * scale))
    scaled = masked.resize(scaled_size, Image.Resampling.NEAREST)
    center_x = (bbox[0] + bbox[2]) / 2
    offset = (round(PIVOT[0] - center_x * scale), round(PIVOT[1] - bbox[3] * scale))
    frame = Image.new("RGBA", CANVAS, (0, 0, 0, 0))
    frame.alpha_composite(scaled, offset)
    return align_foot_to_pivot(palette_quantize(frame))


def opaque_bbox(image: Image.Image) -> tuple[int, int, int, int]:
    bbox = image.getchannel("A").getbbox()
    if bbox is None:
        raise ValueError("normalization produced an empty frame")
    return bbox


def align_foot_to_pivot(frame: Image.Image) -> Image.Image:
    """Correct nearest-neighbor rounding with translation only, never rescaling."""
    bbox = opaque_bbox(frame)
    foot_y = bbox[3] - 1
    offset_y = PIVOT[1] - foot_y
    if offset_y == 0:
        return frame
    anchored = Image.new("RGBA", CANVAS, (0, 0, 0, 0))
    anchored.alpha_composite(frame, (0, offset_y))
    return anchored


def make_preview(frames: list[Image.Image], direction: str, preview_dir: Path) -> dict:
    background = (32, 33, 42, 255)
    one_x = Image.new("RGBA", (CANVAS[0] * len(frames), CANVAS[1]), background)
    for index, frame in enumerate(frames):
        one_x.alpha_composite(frame, (index * CANVAS[0], 0))
    one_path = preview_dir / f"player.base.move.{direction}.contact-1x.png"
    one_x.convert("RGB").save(one_path)
    three_path = preview_dir / f"player.base.move.{direction}.contact-3x.png"
    one_x.resize((one_x.width * 3, one_x.height * 3), Image.Resampling.NEAREST).convert("RGB").save(three_path)

    gif_frames = []
    for frame in frames:
        preview = Image.new("RGBA", (CANVAS[0] * 3, CANVAS[1] * 3), background)
        preview.alpha_composite(frame.resize((CANVAS[0] * 3, CANVAS[1] * 3), Image.Resampling.NEAREST))
        gif_frames.append(preview.convert("P", palette=Image.Palette.ADAPTIVE))
    gif_path = preview_dir / f"player.base.move.{direction}.gif"
    gif_frames[0].save(gif_path, save_all=True, append_images=gif_frames[1:], loop=0, duration=100, disposal=2)
    return {
        "contact1x": one_path.name,
        "contact3x": three_path.name,
        "gif": gif_path.name,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", required=True, type=Path)
    parser.add_argument("--mapping", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--runtime-textures", type=Path)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    mapping = json.loads(args.mapping.read_text(encoding="utf-8"))
    frame_map = mapping.get("directions", mapping)
    reference_hashes = mapping.get("referenceHashes", [])
    reference_ids_used = mapping.get("referenceIdsUsed", ["style_master_scene"])
    source_root: Path = args.source_root
    out: Path = args.out
    prepared: dict[str, list[dict]] = {direction: [] for direction in DIRECTIONS}

    for direction in DIRECTIONS:
        for index, source_spec in enumerate(frame_map[direction]):
            source_id = source_spec["sourceId"] if isinstance(source_spec, dict) else source_spec
            source_path = source_root / f"exec-{source_id}.png"
            if not source_path.is_file():
                raise FileNotFoundError(source_path)
            canonical, canonicalization = canonicalize(Image.open(source_path))
            mask = foreground_mask(canonical)
            bbox = mask.getbbox()
            if bbox is None:
                raise ValueError(f"{source_path.name}: no foreground after controlled mask extraction")
            prepared[direction].append({
                "sourceId": source_id,
                "sourcePath": source_path,
                "sourceSha256": sha256(source_path),
                "prompt": source_spec.get("prompt") if isinstance(source_spec, dict) else None,
                "canonical": canonical,
                "canonicalization": canonicalization,
                "mask": mask,
                "bbox": bbox,
                "frame": index,
            })

    direction_scales = {
        direction: TARGET_BODY_HEIGHT / max(entry["bbox"][3] - entry["bbox"][1] for entry in rows)
        for direction, rows in prepared.items()
    }
    plan = {
        "canonicalSize": CANONICAL_SIZE,
        "pivot": PIVOT,
        "targetBodyHeight": TARGET_BODY_HEIGHT,
        "directionScales": direction_scales,
        "sourceBboxes": {direction: [entry["bbox"] for entry in rows] for direction, rows in prepared.items()},
    }
    if args.dry_run:
        print(json.dumps(plan, indent=2))
        return

    raw_dir = out / "raw"
    masks_dir = out / "masks"
    frames_dir = out / "frames"
    outputs_dir = out / "outputs"
    preview_dir = out / "preview"
    recipes_dir = out / "recipes"
    for directory in (raw_dir, masks_dir, frames_dir, outputs_dir, preview_dir, recipes_dir):
        directory.mkdir(parents=True, exist_ok=True)
    source_map_path = out / "source-frame-map.json"
    source_map_path.write_text(json.dumps(mapping, indent=2) + "\n", encoding="utf-8")

    manifest_assets = []
    for direction, rows in prepared.items():
        asset_id = f"player.base.move.{direction}"
        frames: list[Image.Image] = []
        recipe_frames = []
        direction_scale = direction_scales[direction]
        for entry in rows:
            raw_path = raw_dir / f"{asset_id}.frame{entry['frame']}.png"
            raw_path.write_bytes(entry["sourcePath"].read_bytes())
            mask_path = masks_dir / f"{asset_id}.frame{entry['frame']}.mask.png"
            save_mask(entry["mask"], mask_path)
            frame = make_frame(entry["canonical"], entry["mask"], direction_scale, entry["bbox"])
            bbox = opaque_bbox(frame)
            if bbox[0] < 2 or bbox[1] < 2 or bbox[2] > CANVAS[0] - 2 or bbox[3] > CANVAS[1] - 2:
                raise ValueError(f"{asset_id} frame {entry['frame']}: violates 2px gutter: {bbox}")
            frame_path = frames_dir / f"{asset_id}.frame{entry['frame']}.png"
            frame.save(frame_path)
            frames.append(frame)
            recipe_frames.append({
                "frame": entry["frame"],
                "rawSource": raw_path.relative_to(out).as_posix(),
                "rawSourceSha256": sha256(raw_path),
                "canonicalization": entry["canonicalization"],
                "mask": {
                    "path": mask_path.relative_to(out).as_posix(),
                    "sha256": sha256(mask_path),
                    "method": "edge-connected flood fill of alpha<128, bright neutral checkerboard, or pure black preview backdrop",
                },
                "sourceOpaqueBounds": entry["bbox"],
                "clipScale": direction_scale,
                "anchor": {"sourceBottom": entry["bbox"][3], "targetPivot": PIVOT},
                "output": frame_path.relative_to(out).as_posix(),
                "outputSha256": sha256(frame_path),
                "outputOpaqueBounds": bbox,
            })

        strip = Image.new("RGBA", (CANVAS[0] * len(frames), CANVAS[1]), (0, 0, 0, 0))
        for index, frame in enumerate(frames):
            strip.alpha_composite(frame, (CANVAS[0] * index, 0))
        output_path = outputs_dir / f"{asset_id}.png"
        strip.save(output_path)
        if args.runtime_textures:
            args.runtime_textures.mkdir(parents=True, exist_ok=True)
            runtime_path = args.runtime_textures / output_path.name
            runtime_path.write_bytes(output_path.read_bytes())
        preview = make_preview(frames, direction, preview_dir)
        recipe = {
            "schemaVersion": 1,
            "recipeVersion": "source-to-runtime.v2.player-walk-rework",
            "assetId": asset_id,
            "frameSize": CANVAS,
            "frameCount": len(frames),
            "frameDurationMs": [100] * len(frames),
            "pivot": PIVOT,
            "sharedTransform": {
                "canonicalSize": CANONICAL_SIZE,
                "clipScale": direction_scale,
                "targetBodyHeight": TARGET_BODY_HEIGHT,
                "placement": "source bbox horizontal center -> pivot x; source opaque bottom -> pivot y",
                "palette": PALETTE,
                "alpha": "binary 0/255",
            },
            "sourceMap": {
                "path": source_map_path.relative_to(out).as_posix(),
                "sha256": sha256(source_map_path),
            },
            "frames": recipe_frames,
            "output": output_path.relative_to(out).as_posix(),
            "outputSha256": sha256(output_path),
            "preview": preview,
        }
        recipe_path = recipes_dir / f"{asset_id}.recipe.json"
        recipe_path.write_text(json.dumps(recipe, indent=2) + "\n", encoding="utf-8")
        frame_rects = [{"rect": [index * 64, 0, 64, 64], "durationMs": 100} for index in range(len(frames))]
        manifest_assets.append({
            "assetId": asset_id,
            "file": output_path.relative_to(out).as_posix(),
            "sha256": sha256(output_path),
            "role": "actor",
            "representation": "ui",
            "rank": None,
            "direction": direction,
            "clip": "move",
            "frameSize": CANVAS,
            "frameCount": len(frames),
            "frames": frame_rects,
            "pivot": PIVOT,
            "frameDurationMs": [100] * len(frames),
            "source": {
                "tool": "built-in image_gen",
                "model": None,
                "requestId": None,
                "referenceHashes": reference_hashes,
                "localJobId": f"exec-{rows[0]['sourceId']}",
                "localJobIds": [f"exec-{entry['sourceId']}" for entry in rows],
            },
            "referenceIdsUsed": reference_ids_used,
            "frameSources": [{
                "frame": entry["frame"],
                "rawSource": f"raw/{asset_id}.frame{entry['frame']}.png",
                "rawSourceSha256": entry["sourceSha256"],
                "localJobId": f"exec-{entry['sourceId']}",
                "prompt": entry["prompt"],
            } for entry in rows],
            "status": "review_ready",
            "qa": {
                "technical": True,
                "visual": False,
                "frameIsolation": True,
                "motion": False,
                "inEngine": False,
                "evidencePaths": [
                    recipe_path.relative_to(out).as_posix(),
                    *(f"preview/{name}" for name in preview.values()),
                ],
                "note": "Technical source-to-runtime normalization only. User animation and in-engine review remain pending.",
            },
        })

    manifest = {
        "schemaVersion": 1,
        "styleId": "svm-ash-soulfire",
        "styleVersion": "0.2-calibrated",
        "batchId": out.name,
        "profile": "slice_01",
        "group": "player",
        "technicalNote": "Real ImageGen walk poses normalized with one shared scale and fixed foot pivot; this does not constitute user motion or in-engine acceptance.",
        "assets": manifest_assets,
    }
    (out / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"ok": True, "out": str(out), "manifest": str(out / "manifest.json"), **plan}, indent=2))


if __name__ == "__main__":
    main()
