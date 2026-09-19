#!/usr/bin/env python3
"""LD-3 -- centre crops and per-vantage contact sheets for the weenie-chain captures.

Every frame under docs/qa/LD-3/from-<v>-to-<t>/LD3-5s.png was shot with --capture-cam looking AT
the target's eye point, so the target is at the exact centre of the frame; at 75 degrees of FOV a
5 m pad 120 m away is ~60 px wide in 1920. This writes, next to each frame, a 640 x 360 crop of
that centre scaled x2 (`LD3-5s-centre.png`) -- the same pixels, just legible -- and one contact
sheet per vantage (`sheet-from-<v>.png`, the five crops labelled) so a reader can judge a row of
the matrix in one look. Reads frames, writes PNGs, touches nothing else. Idempotent.

    python tools/dev/ld3_crops.py
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
QA = ROOT / "docs/qa/LD-3"
CROP_W, CROP_H = 640, 360
ORDER = ["spawn", "hub", "red", "spur", "blue", "tangle"]


def main() -> None:
    for v in ORDER:
        tiles = []
        for t in ORDER:
            if t == v:
                continue
            tag = f"from-{v}-to-{t}"
            src = QA / tag / "LD3-5s.png"
            if not src.exists():
                print(f"[ld3-crops] missing {src}")
                continue
            im = Image.open(src).convert("RGB")
            w, h = im.size
            box = ((w - CROP_W) // 2, (h - CROP_H) // 2, (w + CROP_W) // 2, (h + CROP_H) // 2)
            crop = im.crop(box).resize((CROP_W * 2, CROP_H * 2), Image.NEAREST)
            d = ImageDraw.Draw(crop)
            # a thin reticle on the target's eye point: the exact look-at
            cx, cy = CROP_W, CROP_H
            d.line((cx - 40, cy, cx - 12, cy), fill=(255, 255, 0), width=2)
            d.line((cx + 12, cy, cx + 40, cy), fill=(255, 255, 0), width=2)
            d.line((cx, cy - 40, cx, cy - 12), fill=(255, 255, 0), width=2)
            d.line((cx, cy + 12, cx, cy + 40), fill=(255, 255, 0), width=2)
            crop.save(src.with_name("LD3-5s-centre.png"), optimize=True)
            label = crop.resize((CROP_W, CROP_H), Image.BILINEAR)
            dl = ImageDraw.Draw(label)
            dl.rectangle((0, 0, 300, 22), fill=(0, 0, 0))
            dl.text((6, 4), tag, fill=(255, 255, 255))
            tiles.append(label)
        if not tiles:
            continue
        cols = 2
        rows = (len(tiles) + cols - 1) // cols
        sheet = Image.new("RGB", (CROP_W * cols, CROP_H * rows), (20, 20, 20))
        for i, tile in enumerate(tiles):
            sheet.paste(tile, ((i % cols) * CROP_W, (i // cols) * CROP_H))
        out = QA / f"sheet-from-{v}.png"
        sheet.save(out, optimize=True)
        print(f"[ld3-crops] wrote {out.relative_to(ROOT)} ({len(tiles)} tiles)")


if __name__ == "__main__":
    main()
