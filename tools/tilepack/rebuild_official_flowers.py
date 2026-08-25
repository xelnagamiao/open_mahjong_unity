#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""用 fluffy 花牌 3D 素材重做官方 51–58。

3D：插画裁切后铺满 220×366 不透明米色底。
手牌：同一套插画铺满手牌白色牌面（避开顶沿）。

FluffyStuff 日式四君子是 梅蘭菊竹（7f=菊、8f=竹）。
国标/台湾 ID 是 梅兰竹菊：57=竹、58=菊。本脚本会把 fluffy 包里
对调的 57/58 纠正后再生成官方图。

位置微调只改下面几个常量后重跑本脚本。
"""
from __future__ import annotations

import shutil
from pathlib import Path

from PIL import Image

TABLE_W, TABLE_H = 220, 366
BEIGE = (245, 246, 247, 255)
HAND_W, HAND_H = 272, 389
HAND_RIM_TOP = 50
HAND_PAD_X = 8
HAND_PAD_BOTTOM = 8
TABLE_MARGIN = 3
ART_BBOX_PAD = 2
FLOWER_IDS = range(51, 59)

# 相对铺满后的额外缩放与像素偏移，正 x 向右、正 y 向下。
FLOWER_TABLE_SCALE = 1.0
FLOWER_TABLE_OFFSET_X = 0
FLOWER_TABLE_OFFSET_Y = 0
FLOWER_HAND_SCALE = 1.0
FLOWER_HAND_OFFSET_X = 0
FLOWER_HAND_OFFSET_Y = 0
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


def place_filled(
    art: Image.Image,
    canvas_size: tuple[int, int],
    box: tuple[int, int, int, int],
    scale: float,
    offset_x: int,
    offset_y: int,
) -> Image.Image:
    """把花纹拉伸进 box=(x, y, w, h)，再按 scale/offset 微调。"""
    layer = Image.new("RGBA", canvas_size, (0, 0, 0, 0))
    box_x, box_y, box_w, box_h = box
    nw = max(1, int(round(box_w * scale)))
    nh = max(1, int(round(box_h * scale)))
    resized = art.resize((nw, nh), Image.Resampling.LANCZOS)
    ox = box_x + (box_w - nw) // 2 + offset_x
    oy = box_y + (box_h - nh) // 2 + offset_y
    composite_at(layer, resized, (ox, oy))
    return layer


def make_table(art: Image.Image) -> Image.Image:
    canvas = Image.new("RGBA", (TABLE_W, TABLE_H), BEIGE)
    inner_w = TABLE_W - TABLE_MARGIN * 2
    inner_h = TABLE_H - TABLE_MARGIN * 2
    layer = place_filled(
        art,
        (TABLE_W, TABLE_H),
        (TABLE_MARGIN, TABLE_MARGIN, inner_w, inner_h),
        FLOWER_TABLE_SCALE,
        FLOWER_TABLE_OFFSET_X,
        FLOWER_TABLE_OFFSET_Y,
    )
    canvas.alpha_composite(layer)
    return canvas.convert("RGB").convert("RGBA")


def make_hand(art: Image.Image, bg: Image.Image) -> Image.Image:
    canvas = bg.copy()
    inner_w = HAND_W - HAND_PAD_X * 2
    inner_h = HAND_H - HAND_RIM_TOP - HAND_PAD_BOTTOM
    layer = place_filled(
        art,
        (HAND_W, HAND_H),
        (HAND_PAD_X, HAND_RIM_TOP, inner_w, inner_h),
        FLOWER_HAND_SCALE,
        FLOWER_HAND_OFFSET_X,
        FLOWER_HAND_OFFSET_Y,
    )
    canvas.alpha_composite(layer)
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
        src_path = FLUFFY_TABLE / f"{tile_id}.png"
        if not src_path.is_file():
            raise SystemExit(f"missing {src_path}")
        art = crop_art(knockout_beige(Image.open(src_path)))
        table = make_table(art)
        hand = make_hand(art, bg)

        table_dests = (
            OFFICIAL_TABLE / f"{tile_id}.png",
            OFFICIAL_PACK_TABLE / f"{tile_id}.png",
        )
        hand_dests = (
            OFFICIAL_HAND / f"{tile_id}.png",
            OFFICIAL_PACK_HAND / f"{tile_id}.png",
        )
        save_png(table, table_dests[0])
        save_png(hand, hand_dests[0])
        shutil.copyfile(table_dests[0], table_dests[1])
        shutil.copyfile(hand_dests[0], hand_dests[1])
        print(
            f"{tile_id} art={art.size} table={table.size} "
            f"hand={hand.size} <- {src_path.name}"
        )


if __name__ == "__main__":
    main()
