"""Normalize ImageGen soul candidates into a production.py recordable batch."""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import shutil
from pathlib import Path

from PIL import Image, ImageDraw


def read(path: Path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def parse_source(value: str):
    asset_id, source_path, prompt_b64 = value.split("::", 2)
    return asset_id, Path(source_path), base64.b64decode(prompt_b64).decode("utf-8")


def palette(style, role):
    if role == "pickup":
        names = ("ash", "bone", "soulAmber")
        limit = 12
    elif role == "icon":
        names = ("ash", "bone", "soulAmber", "cloth")
        limit = 16
    else:
        names = ("ash", "stone", "bone", "soulAmber", "cloth")
        limit = 16
    colors = []
    for name in names:
        colors.extend(tuple(bytes.fromhex(value.lstrip("#"))) for value in style["palette"][name])
    return colors[:limit]


def nearest(rgb, colors):
    return min(colors, key=lambda color: sum((int(rgb[i]) - color[i]) ** 2 for i in range(3)))


def binary_palette(image: Image.Image, colors):
    image = image.convert("RGBA")
    out = Image.new("RGBA", image.size, (0, 0, 0, 0))
    src, dst = image.load(), out.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, alpha = src[x, y]
            if alpha >= 128:
                dst[x, y] = (*nearest((r, g, b), colors), 255)
    return out


def bbox_union(frames):
    boxes = [frame.getchannel("A").getbbox() for frame in frames]
    boxes = [box for box in boxes if box]
    if not boxes:
        raise ValueError("source has no visible alpha subject")
    return (
        min(box[0] for box in boxes),
        min(box[1] for box in boxes),
        max(box[2] for box in boxes),
        max(box[3] for box in boxes),
    )


def visible_bbox(frame):
    alpha = frame.getchannel("A")
    return alpha.point(lambda value: 255 if value >= 128 else 0).getbbox()


def split_source(source: Path, frame_count: int):
    with Image.open(source) as loaded:
        image = loaded.convert("RGBA")
    alpha = image.getchannel("A")
    if alpha.getextrema() == (255, 255):
        raise ValueError("opaque full-canvas source; checkerboard/background cannot be safely separated")
    if image.width < frame_count:
        raise ValueError(f"source width {image.width} is too small for {frame_count} frames")
    frames, rects = [], []
    for index in range(frame_count):
        x0 = round(index * image.width / frame_count)
        x1 = round((index + 1) * image.width / frame_count)
        rects.append([x0, 0, x1, image.height])
        frames.append(image.crop((x0, 0, x1, image.height)))
    # ImageGen may return one centered subject instead of the requested strip.
    # Preserve the raw source but make a deterministic technical candidate by
    # repeating the full subject in each runtime frame cell. Visual/motion
    # approval remains pending and is never inferred from this fallback.
    if frame_count > 1 and any(visible_bbox(frame) is None for frame in frames):
        frames = [image.copy() for _ in range(frame_count)]
        rects = [[0, 0, image.width, image.height] for _ in range(frame_count)]
    return frames, rects


def normalize_actor(source: Path, frame_count: int, colors):
    frames, rects = split_source(source, frame_count)
    crop_boxes = [visible_bbox(frame) for frame in frames]
    if any(box is None for box in crop_boxes):
        raise ValueError("source has an empty actor frame")
    crop_w = max(box[2] - box[0] for box in crop_boxes)
    crop_h = max(box[3] - box[1] for box in crop_boxes)
    scale = min(44 / crop_h, 56 / crop_w)
    target_size = (max(1, round(crop_w * scale)), max(1, round(crop_h * scale)))
    output = Image.new("RGBA", (64 * frame_count, 64), (0, 0, 0, 0))
    for index, (frame, crop_box) in enumerate(zip(frames, crop_boxes)):
        box_w, box_h = crop_box[2] - crop_box[0], crop_box[3] - crop_box[1]
        frame_size = (max(1, round(box_w * scale)), max(1, round(box_h * scale)))
        resized = frame.crop(crop_box).resize(frame_size, Image.Resampling.NEAREST)
        dx = index * 64 + 32 - round((frame_size[0]) / 2)
        dy = 56 - frame_size[1]
        output.alpha_composite(binary_palette(resized, colors), (dx, dy))
    crop_box = [0, 0, crop_w, crop_h]
    return output, rects, crop_box, scale, [32 - round(target_size[0] / 2), 56 - target_size[1]], list(target_size), [64, 64], [32, 56]


def normalize_static(source: Path, role: str, colors, style):
    frames, rects = split_source(source, 1)
    crop_box = bbox_union(frames)
    crop_w, crop_h = crop_box[2] - crop_box[0], crop_box[3] - crop_box[1]
    canvas = style["roles"][role]["canvas"]
    pivot = style["roles"][role]["defaultPivot"]
    margin = 2
    max_w = canvas[0] - margin * 2
    max_h = canvas[1] - margin * 2
    scale = min(max_w / crop_w, max_h / crop_h)
    target_size = (max(1, round(crop_w * scale)), max(1, round(crop_h * scale)))
    output = Image.new("RGBA", tuple(canvas), (0, 0, 0, 0))
    resized = binary_palette(frames[0].crop(crop_box).resize(target_size, Image.Resampling.NEAREST), colors)
    output.alpha_composite(resized, (round(pivot[0] - target_size[0] / 2), round(pivot[1] - target_size[1] / 2)))
    return output, rects, crop_box, scale, [round(pivot[0] - target_size[0] / 2), round(pivot[1] - target_size[1] / 2)], list(target_size), list(canvas), list(pivot)


def contact_sheet(entries, destination: Path):
    scale = 3
    row_h = 64 * scale + 30
    width = max(1280, max((Image.open(entry["path"]).width for entry in entries), default=64) * scale + 24)
    sheet = Image.new("RGBA", (width, max(1, row_h * len(entries))), (22, 24, 32, 255))
    draw = ImageDraw.Draw(sheet)
    for row, entry in enumerate(entries):
        y = row * row_h
        draw.text((12, y + 5), entry["assetId"], fill=(241, 221, 176, 255))
        with Image.open(entry["path"]) as image:
            enlarged = image.resize((image.width * scale, image.height * scale), Image.Resampling.NEAREST)
            sheet.alpha_composite(enlarged, (12, y + 25))
    destination.parent.mkdir(parents=True, exist_ok=True)
    sheet.convert("RGB").save(destination, "PNG")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--plan", type=Path, required=True)
    parser.add_argument("--batch", type=int, required=True)
    parser.add_argument("--batch-dir", type=Path, required=True)
    parser.add_argument("--source", action="append", default=[])
    parser.add_argument("--asset-id", action="append", default=[])
    args = parser.parse_args()
    root = args.root.resolve()
    plan = read(args.plan.resolve())
    style_path = root / "solo-vs-mortal-art" / "references" / "style-lock.json"
    style = read(style_path)
    approvals = {item["referenceId"]: item for item in read(root / "art" / "production" / "approvals.json")["references"]}
    if args.asset_id:
        wanted = set(args.asset_id)
        jobs = [job for job in plan["jobs"] if job["assetId"] in wanted]
    else:
        jobs = plan["jobs"][(args.batch - 1) * plan["batchSize"] : args.batch * plan["batchSize"]]
    source_items = [parse_source(value) for value in args.source]
    sources = {item[0]: item for item in source_items}
    batch = args.batch_dir.resolve()
    raw_dir, out_dir = batch / "raw", batch / "outputs"
    raw_dir.mkdir(parents=True, exist_ok=True)
    out_dir.mkdir(parents=True, exist_ok=True)
    contacts, assets, rejected, records = [], [], [], []
    for job in jobs:
        asset_id = job["assetId"]
        selected = sources.get(asset_id)
        if selected is None:
            records.append({**job, "reusedPreviousOutput": job.get("previousOutput")})
            continue
        source, prompt = selected[1], selected[2]
        if not source.is_file():
            raise FileNotFoundError(source)
        raw = raw_dir / f"{asset_id.replace('.', '_')}.source.png"
        shutil.copy2(source, raw)
        brief = job["brief"]
        role, representation = brief["role"], brief["representation"]
        try:
            if role == "actor" or role == "vfx":
                normalized, rects, crop_box, scale, offset, target_size, frame_size, pivot = normalize_actor(raw, len(brief["durations"]), palette(style, role))
            elif role in ("pickup", "icon"):
                normalized, rects, crop_box, scale, offset, target_size, frame_size, pivot = normalize_static(raw, role, palette(style, role), style)
            else:
                raise ValueError(f"unsupported raster role: {role}")
        except ValueError as error:
            rejected.append({"assetId": asset_id, "sourcePath": raw.relative_to(root).as_posix(), "sourceOutputHint": str(source), "sha256": sha256(raw), "reason": str(error)})
            records.append({**job, "rawFile": raw.name, "status": "rejected", "reason": str(error)})
            continue
        output = out_dir / f"{asset_id.replace('.', '_')}.png"
        normalized.save(output, "PNG")
        ref_ids = [item["referenceId"] for item in job.get("references", []) if item["status"] == "reference_approved"]
        ref_ids = ref_ids or ["style_master_scene"]
        ref_hashes = [approvals[item]["sha256"] for item in ref_ids]
        source_meta = {
            "tool": "built-in image_gen",
            "model": None,
            "requestId": None,
            "localJobId": source.stem,
            "referenceHashes": ref_hashes,
            "rawOutputHint": str(source),
            "prompt": prompt,
        }
        asset = {
            "assetId": asset_id,
            "file": f"outputs/{output.name}",
            "sha256": sha256(output),
            "role": role,
            "speciesId": brief.get("family") if representation not in ("environment", "ui") else None,
            "rank": brief.get("rank"),
            "representation": representation,
            "direction": brief["direction"],
            "clip": brief["clip"],
            "frameSize": frame_size,
            "frameCount": len(brief["durations"]),
            "pivot": pivot,
            "frameDurationMs": brief["durations"],
            "source": source_meta,
            "referenceIdsUsed": ref_ids,
            "masterIdsUsed": [{"masterId": item, "sha256": approvals[item]["sha256"]} for item in ref_ids],
            "status": "review_ready",
            "qa": {"technical": False, "visual": False, "frameIsolation": False, "motion": False, "inEngine": False, "evidencePaths": [], "note": "ImageGen source normalized deterministically; visual/motion/user review pending."},
            "normalization": {"rawSourcePath": raw.relative_to(root).as_posix(), "rawSourceSha256": sha256(raw), "sourceFrameRects": rects, "commonCropBox": list(crop_box), "scale": scale, "placementOffset": offset, "normalizedSubjectSize": target_size, "resize": "nearest", "alpha": "binary threshold >=128", "palette": f"style-lock {role} fixed palette nearest-color mapping", "canvas": frame_size, "frameCount": len(brief["durations"]), "pivot": pivot, "recipe": "prepare_souls_beta_batch.py; common crop/scale/anchor across clip"}
        }
        assets.append(asset)
        contacts.append({"assetId": asset_id, "path": output})
        records.append({**job, "rawFile": raw.name, "localJobId": source.stem, "outputHint": str(source), "prompt": prompt})
    if not assets:
        raise ValueError(f"batch {args.batch} produced no technical candidate")
    manifest = {"schemaVersion": 1, "styleId": style["styleId"], "styleVersion": style["styleVersion"], "batchId": f"beta_01-batch-{args.batch:03d}", "group": plan["group"], "profile": plan["profile"], "assets": assets}
    (batch / "jobs.json").write_text(json.dumps(records, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (batch / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (batch / "rejected-candidates.json").write_text(json.dumps({"schemaVersion": 1, "candidates": rejected}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    contact_sheet(contacts, batch / "preview" / "batch_contact_sheet.png")
    (batch / "normalization_report.json").write_text(json.dumps({"batch": args.batch, "assets": [item["assetId"] for item in assets], "rejected": rejected, "technicalReview": "Run production.py record; visual/motion/user review remains pending."}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"batch": args.batch, "manifest": str(batch / "manifest.json"), "assets": [item["assetId"] for item in assets], "rejected": rejected, "preview": str(batch / "preview" / "batch_contact_sheet.png")}, ensure_ascii=False))


if __name__ == "__main__":
    main()
