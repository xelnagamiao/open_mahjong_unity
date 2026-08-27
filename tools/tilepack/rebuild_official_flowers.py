#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""用 fluffy 花牌素材重做官方 51–58。

3D：按原图比例 cover 裁切进 220×366 米色底，不拉伸。
手牌：花纹按原比例叠在牌体上，再放大 HAND_SCALE。

源图用手牌牌面（等比过的花纹），不用已拉高的 3D PNG。
"""
from __future__ import annotations

import shutil
from pathlib import Path

from PIL import Image

TABLE_W, TABLE_H = 220, 366
BEIGE = (245, 246, 247, 255)
HAND_W, HAND_H = 272, 389
ART_BBOX_PAD = 12
FLOWER_IDS = range(51, 59)

# 3D 目标框占画布比例。高度低于 1，避免为铺满 366 而把左右裁掉太多。
TABLE_COVER_W = 0.92
TABLE_COVER_H = 0.82
FLOWER_TABLE_OFFSET_X = 0
FLOWER_TABLE_OFFSET_Y = 0
HAND_SCALE = 1.06
BEIGE_KNOCKOUT = 16

ROOT = Path(__file__).resolve().parents[2]
IMAGE_ROOT = ROOT / "open_mahjong_unity" / "Assets" / "Resources" / "image"
PACKS = IMAGE_ROOT / "CardFacePacks"
FLUFFY_TABLE = PACKS / "fluffy" / "3D牌面"
FLUFFY_HAND = PACKS / "fluffy" / "手牌牌面"
HAND_BG = PACKS / "hand-bg-default.png"
OFFICIAL_HAND = IMAGE_ROOT / "CardFaceImage_xuefun"
OFFICIAL_TABLE = IMAGE_ROOT / "CardFaceMaterial_xuefun"
OFFICIAL_PACK_HAND = PACKS / "official" / "手牌牌面"
OFFICIAL_PACK_TABLE = PACKS / "official" / "3D牌面"
UNITY_SVG = (
    ROOT / "open_mahjong_web" / "client" / "public" / "game2d-assets"
    / "textures" / "riichi-mahjong-tiles" / "Unity"
)


def knockout_beige(im: Image.Image, thresh: int = BEIGE_KNOCKOUT) -> Image.Image:
    src = im.convert("RGBA")
    px = src.load()
    width, height = src.size
    for y in range(height):
        for x in range(width):
            r, g, b, a = px[x, y]
            if a < 8 or abs(r - 245) + abs(g - 246) + abs(b - 247) <= thresh:
                px[x, y] = (245, 246, 247, 0)
    return src


def crop_art(im: Image.Image, pad: int = ART_BBOX_PAD) -> Image.Image:
    bbox = im.getchannel("A").getbbox()
    if bbox is None:
        return im
    left, top, right, bottom = bbox
    left = max(0, left - pad)
    top = max(0, top - pad)
    right = min(im.size[0], right + pad)
    bottom = min(im.size[1], bottom + pad)
    return im.crop((left, top, right, bottom))


def resize_cover(
    art: Image.Image,
    dest_w: int,
    dest_h: int,
    offset_x: int = 0,
    offset_y: int = 0,
) -> Image.Image:
    """等比放大到盖住目标框，居中裁切多出的边。"""
    src_w, src_h = art.size
    if src_w <= 0 or src_h <= 0:
        return Image.new("RGBA", (dest_w, dest_h), (0, 0, 0, 0))
    scale = max(dest_w / src_w, dest_h / src_h)
    nw = max(dest_w, int(round(src_w * scale)))
    nh = max(dest_h, int(round(src_h * scale)))
    resized = art.resize((nw, nh), Image.Resampling.LANCZOS)
    left = (nw - dest_w) // 2 + offset_x
    top = (nh - dest_h) // 2 + offset_y
    left = max(0, min(left, nw - dest_w))
    top = max(0, min(top, nh - dest_h))
    return resized.crop((left, top, left + dest_w, top + dest_h))


def composite_at(dst: Image.Image, src: Image.Image, xy: tuple[int, int]) -> None:
    x, y = xy
    sw, sh = src.size
    dw, dh = dst.size
    if x >= 0 and y >= 0 and x + sw <= dw and y + sh <= dh:
        dst.alpha_composite(src, (x, y))
        return
    src_box = (
        max(0, -x),
        max(0, -y),
        min(sw, dw - x),
        min(sh, dh - y),
    )
    if src_box[2] <= src_box[0] or src_box[3] <= src_box[1]:
        return
    cropped = src.crop(src_box)
    dst.alpha_composite(cropped, (x + src_box[0], y + src_box[1]))


def make_table(art: Image.Image) -> Image.Image:
    canvas = Image.new("RGBA", (TABLE_W, TABLE_H), BEIGE)
    inner_w = max(1, int(round(TABLE_W * TABLE_COVER_W)))
    inner_h = max(1, int(round(TABLE_H * TABLE_COVER_H)))
    covered = resize_cover(
        art,
        inner_w,
        inner_h,
        FLOWER_TABLE_OFFSET_X,
        FLOWER_TABLE_OFFSET_Y,
    )
    ox = (TABLE_W - inner_w) // 2
    oy = (TABLE_H - inner_h) // 2
    canvas.alpha_composite(covered, (ox, oy))
    return canvas.convert("RGB").convert("RGBA")


def make_hand(src_hand: Image.Image, bg: Image.Image) -> Image.Image:
    canvas = bg.copy()
    art = knockout_beige(src_hand)
    bbox = art.getchannel("A").getbbox()
    if bbox is None:
        return canvas
    cropped = art.crop(bbox)
    nw = max(1, int(round(cropped.size[0] * HAND_SCALE)))
    nh = max(1, int(round(cropped.size[1] * HAND_SCALE)))
    resized = cropped.resize((nw, nh), Image.Resampling.LANCZOS)
    cx = (bbox[0] + bbox[2]) / 2
    cy = (bbox[1] + bbox[3]) / 2
    ox = int(round(cx - nw / 2))
    oy = int(round(cy - nh / 2))
    composite_at(canvas, resized, (ox, oy))
    return canvas


def save_png(im: Image.Image, dest: Path) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    im.save(dest, format="PNG", optimize=True)


def swap_bytes(a: Path, b: Path) -> None:
    data_a = a.read_bytes()
    data_b = b.read_bytes()
    a.write_bytes(data_b)
    b.write_bytes(data_a)


def fluffy_file_is_bamboo(path: Path) -> bool:
    """竹图中心是深绿；菊图中心多半是米色空档。"""
    im = Image.open(path).convert("RGBA")
    r, g, b, a = im.getpixel((im.size[0] // 2, im.size[1] // 2))
    return a > 80 and g > 50 and r < 80


def fix_fluffy_flower_order() -> None:
    table_57 = FLUFFY_TABLE / "57.png"
    table_58 = FLUFFY_TABLE / "58.png"
    if not table_57.is_file() or not table_58.is_file():
        raise SystemExit("missing fluffy 3D 57/58")
    if fluffy_file_is_bamboo(table_57):
        print("fluffy 57 already 竹, skip pack swap")
        return
    pairs = [
        (FLUFFY_TABLE / "57.png", FLUFFY_TABLE / "58.png"),
        (FLUFFY_HAND / "57.png", FLUFFY_HAND / "58.png"),
    ]
    for left, right in pairs:
        if left.is_file() and right.is_file():
            swap_bytes(left, right)
            print(f"swapped {left.parent.name}/57 <-> 58")


def fix_unity_svg_flower_order() -> None:
    svg_57 = UNITY_SVG / "57.svg"
    svg_58 = UNITY_SVG / "58.svg"
    if not svg_57.is_file() or not svg_58.is_file():
        print("skip unity svg, missing")
        return
    text_57 = svg_57.read_text(encoding="utf-8")
    if "aria-label=\"竹" in text_57 or svg_57.stat().st_size < svg_58.stat().st_size:
        print("unity 57.svg already 竹, skip svg swap")
        return
    swap_bytes(svg_57, svg_58)
    print("swapped Unity/57.svg <-> 58.svg")


def main() -> None:
    fix_fluffy_flower_order()
    fix_unity_svg_flower_order()

    bg = Image.open(HAND_BG).convert("RGBA")
    if bg.size != (HAND_W, HAND_H):
        raise SystemExit(f"hand-bg size {bg.size}, expected {(HAND_W, HAND_H)}")

    for tile_id in FLOWER_IDS:
        src_path = FLUFFY_HAND / f"{tile_id}.png"
        if not src_path.is_file():
            raise SystemExit(f"missing {src_path}")
        src_hand = Image.open(src_path)
        hand = make_hand(src_hand, bg)
        hand_dests = (
            OFFICIAL_HAND / f"{tile_id}.png",
            OFFICIAL_PACK_HAND / f"{tile_id}.png",
        )
        save_png(hand, hand_dests[0])
        shutil.copyfile(hand_dests[0], hand_dests[1])
        print(f"{tile_id} hand={hand.size} scale={HAND_SCALE} <- {src_path.name}")


if __name__ == "__main__":
    main()
