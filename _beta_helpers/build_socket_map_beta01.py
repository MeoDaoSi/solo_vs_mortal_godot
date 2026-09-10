"""Derive a technical Player socket-map artifact from recorded runtime frames."""
from __future__ import annotations

import argparse
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

from PIL import Image


PROJECT = Path(r"C:\ws\asset-production-system")
STYLE_HASH = "59bc6b957d801804510db87c3a71cce3bfb63efd62c412091a6b9cb2045b7aa6"
SCALE_HASH = "fd8e96547e2130e619c373caafe244eb1712e2502045ac2ba6bfb48f4b553fd8"


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def clamp(value: int) -> int:
    return max(0, min(63, int(value)))


def derive_anchor(bbox: tuple[int, int, int, int] | None, side: int) -> list[int]:
    if not bbox:
        return [32, 40]
    left, top, right, bottom = bbox
    width = max(1, right - left)
    height = max(1, bottom - top)
    offset = max(2, width // 5)
    y = top + round(height * (0.47 if side == 0 else 0.55))
    x = ((left + right) // 2) + (-offset if side == 0 else offset)
    return [clamp(x), clamp(y)]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--manifest", required=True, type=Path)
    args = parser.parse_args()
    catalog = json.loads((PROJECT / "art/production/catalog.json").read_text(encoding="utf-8"))
    actors = [a for a in catalog["assets"] if a.get("group") == "player" and a.get("role") == "actor" and a.get("output")]
    entries = []
    for asset in sorted(actors, key=lambda x: x["assetId"]):
        output_path = PROJECT / asset["output"]["file"]
        with Image.open(output_path) as opened:
            strip = opened.convert("RGBA")
            frame_count = int(asset["output"]["frameCount"])
            for frame_index in range(frame_count):
                frame = strip.crop((frame_index * 64, 0, (frame_index + 1) * 64, 64))
                bbox = frame.getchannel("A").getbbox()
                entries.append({
                    "assetId": asset["assetId"],
                    "frame": frame_index,
                    "handR": derive_anchor(bbox, 0),
                    "handL": derive_anchor(bbox, 1),
                    "frontOrBackLayer": "front",
                    "weaponPoseId": f"{asset['assetId']}.frame{frame_index:02d}",
                    "sourceFrame": {
                        "file": asset["output"]["file"],
                        "sha256": asset["output"]["sha256"],
                        "bboxXYXY": list(bbox) if bbox else None,
                    },
                })
    payload = {
        "schemaVersion": 1,
        "assetId": "player.socket_map",
        "profile": "beta_01",
        "technicalStatus": "candidate",
        "note": "Anchors are derived from recorded frame alpha bounds for integration scaffolding only; no hitbox, reach, timing, or gameplay capability is authored here.",
        "entries": entries,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    manifest = {
        "schemaVersion": 1,
        "manifestKind": "artifact",
        "styleId": "svm-ash-soulfire",
        "styleVersion": "0.2-calibrated",
        "batchId": "batch-008-socket-map",
        "profile": "beta_01",
        "group": "player",
        "generatedAt": datetime.now(timezone.utc).isoformat(),
        "technicalNote": "Metadata candidate derived from recorded Player frame bounds. No gameplay values are authored; visual and in-engine review remain pending.",
        "assets": [{
            "assetId": "player.socket_map",
            "file": args.output.name,
            "sha256": digest(args.output),
            "source": {
                "tool": "Pillow frame-bound derivation",
                "model": None,
                "requestId": None,
                "localJobId": "socket-map-beta_01-derived-20260910",
                "referenceHashes": [STYLE_HASH, SCALE_HASH],
            },
            "qa": {
                "technical": False,
                "visual": False,
                "content": False,
                "inEngine": False,
                "evidencePaths": [],
                "note": "Derived integration scaffolding only; user gameplay and visual review pending.",
            },
        }],
    }
    args.manifest.parent.mkdir(parents=True, exist_ok=True)
    args.manifest.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"entries": len(entries), "output": str(args.output), "manifest": str(args.manifest)}, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
