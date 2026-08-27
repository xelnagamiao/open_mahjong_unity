#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""去掉官方 5 饼 / 赤 5 饼贴图四边白边，裁切花纹后重新叠到手牌背景和 3D 米色底。"""
from __future__ import annotations

import shutil
from pathlib import Path

from PIL import Image, ImageFilter

TABLE_W, TABLE_H = 220, 366
BEIGE = (245, 246, 247, 255)
HAND_W, HAND_H = 272, 389
TILE_IDS = (25, 205)
CROP_PAD = 1

ROOT = Path(__file__).resolve().parents[2]
IMAGE_ROOT = ROOT / "open_mahjong_unity" / "Assets" / "Resources" / "image"
PACKS = IMAGE_ROOT / "CardFacePacks"
HAND_BG = PACKS / "hand-bg-default.png"
OFFICIAL_HAND = IMAGE_ROOT / "CardFaceImage_xuefun"
OFFICIAL_TABLE = IMAGE_ROOT / "CardFaceMaterial_xuefun"
OFFICIAL_PACK_HAND = PACKS / "official" / "手牌牌面"
OFFICIAL_PACK_TABLE = PACKS / "official" / "3D牌面"


def is_navy_or_rim(r: int, g: int, b: int) -> bool:
    return abs(r - 24) + abs(g - 36) + abs(b - 70) <= 40 or abs(r - 100) + abs(g - 106) + abs(b - 108) <= 40


def is_beige(r: int, g: int, b: int) -> bool:
    return abs(r - 245) + abs(g - 246) + abs(b - 247) <= 18


def is_fringe(r: int, g: int, b: int) -> bool:
    return abs(r - 215) + abs(g - 217) + abs(b - 217) <= 14


def extract_pattern(im: Image.Image) -> Image.Image:
    src = im.convert("RGBA")
    width, height = src.size
    px = src.load()
    ink = Image.new("L", (width, height), 0)
    ink_px = ink.load()
    for y in range(height):
        for x in range(width):
            r, g, b, a = px[x, y]
            if a < 80 or is_navy_or_rim(r, g, b) or is_beige(r, g, b) or is_fringe(r, g, b):
                continue
            if max(r, g, b) - min(r, g, b) >= 40:
                ink_px[x, y] = 255
    dilated = ink.filter(ImageFilter.MaxFilter(5))
    dil_px = dilated.load()
    out = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    out_px = out.load()
    for y in range(height):
        for x in range(width):
            if dil_px[x, y] < 128:
                continue
            r, g, b, a = px[x, y]
            if a < 80 or is_beige(r, g, b) or is_fringe(r, g, b) or is_navy_or_rim(r, g, b):
                continue
            out_px[x, y] = (r, g, b, a)
    return out


def crop_pattern(pattern: Image.Image) -> tuple[Image.Image, tuple[int, int]]:
    bbox = pattern.getchannel("A").getbbox()
    if bbox is None:
        return pattern, (0, 0)
    left, top, right, bottom = bbox
    left = max(0, left - CROP_PAD)
    top = max(0, top - CROP_PAD)
    right = min(pattern.size[0], right + CROP_PAD)
    bottom = min(pattern.size[1], bottom + CROP_PAD)
    return pattern.crop((left, top, right, bottom)), (left, top)


def save_png(im: Image.Image, dest: Path) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    im.save(dest, format="PNG", optimize=True)


def main() -> None:
    bg = Image.open(HAND_BG).convert("RGBA")
    if bg.size != (HAND_W, HAND_H):
        raise SystemExit(f"hand-bg size {bg.size}")

    for tile_id in TILE_IDS:
        hand_src = Image.open(OFFICIAL_HAND / f"{tile_id}.png")
        table_src = Image.open(OFFICIAL_TABLE / f"{tile_id}.png")

        hand_art, hand_xy = crop_pattern(extract_pattern(hand_src))
        hand = bg.copy()
        hand.alpha_composite(hand_art, hand_xy)

        table_art, table_xy = crop_pattern(extract_pattern(table_src))
        table = Image.new("RGBA", (TABLE_W, TABLE_H), BEIGE)
        table.alpha_composite(table_art, table_xy)
        table = table.convert("RGB").convert("RGBA")

        hand_dests = (OFFICIAL_HAND / f"{tile_id}.png", OFFICIAL_PACK_HAND / f"{tile_id}.png")
        table_dests = (OFFICIAL_TABLE / f"{tile_id}.png", OFFICIAL_PACK_TABLE / f"{tile_id}.png")
        save_png(hand, hand_dests[0])
        save_png(table, table_dests[0])
        shutil.copyfile(hand_dests[0], hand_dests[1])
        shutil.copyfile(table_dests[0], table_dests[1])
        print(
            f"{tile_id} hand_art={hand_art.size}@{hand_xy} "
            f"table_art={table_art.size}@{table_xy}"
        )


if __name__ == "__main__":
    main()
