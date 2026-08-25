#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把官方 / FluffyStuff / HkMahjong 整理进 Resources/image/CardFacePacks。
手牌牌面：源图不裁切，按原图比例缩小后贴进 272×389 透明画布，避开顶部牌沿。
3D牌面：宽度略拉宽、高度拉到接近官方牌顶高度后居中贴进 220×366。
"""
from __future__ import annotations

import io
import shutil
import uuid
from pathlib import Path

from PIL import Image

TABLE_W, TABLE_H = 220, 366
BEIGE = (245, 246, 247, 255)
TABLE_FIT_SCALE_X = 0.94
TABLE_FIT_SCALE_Y = 0.90
HAND_W, HAND_H = 272, 389
HAND_RIM_TOP = 50
HAND_PAD_X = 24
HAND_PAD_BOTTOM = 20

CODE_TO_ID = {}
for n in range(1, 10):
    CODE_TO_ID[f"{n}m"] = 10 + n
    CODE_TO_ID[f"{n}s"] = 20 + n
    CODE_TO_ID[f"{n}p"] = 30 + n
CODE_TO_ID.update({
    "1z": 41, "2z": 42, "3z": 43, "4z": 44,
    "5z": 47, "6z": 46, "7z": 45,
    "0m": 105, "0p": 205, "0s": 305,
})
for n in range(1, 9):
    CODE_TO_ID[f"{n}f"] = 50 + n
CODE_TO_ID["blank"] = 2
# FluffyStuff 日式四君子是 梅蘭菊竹（7f=菊、8f=竹）。
# 国标/台湾 ID 是 梅兰竹菊：57=竹、58=菊。只在 fluffy 导出时对调，港式花牌自带 3/4 数字不能对调。
FLUFFY_FLOWER_ID_REMAP = {57: 58, 58: 57}

ROOT = Path(__file__).resolve().parents[2]
FLUFFY = Path(r"C:\Users\Administrator\Downloads\tiles\FluffyStuff")
HK = Path(r"C:\Users\Administrator\Downloads\tiles\HkMahjong")
UNITY = ROOT / "open_mahjong_unity" / "Assets"
IMAGE_ROOT = UNITY / "Resources" / "image"
PACKS = IMAGE_ROOT / "CardFacePacks"
OFFICIAL_HAND = IMAGE_ROOT / "CardFaceImage_xuefun"
OFFICIAL_TABLE = IMAGE_ROOT / "CardFaceMaterial_xuefun"
HAND_DIR = "手牌牌面"
TABLE_DIR = "3D牌面"

STANDARD_IDS = (
    [suit * 10 + rank for suit in range(1, 4) for rank in range(1, 10)]
    + list(range(41, 48))
    + list(range(51, 59))
    + [105, 205, 305, 2]
)

FOLDER_META = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

TEXTURE_META = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 0
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 4
    buildTarget: Standalone
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 4
    buildTarget: WebGL
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 4
    buildTarget: Android
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 4
    buildTarget: iOS
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    customData: 
    physicsShape: []
    bones: []
    spriteID: {sprite_id}
    internalID: 0
    vertices: []
    indices: 
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName: 
  pSDRemoveMatte: 0
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def new_guid() -> str:
    return uuid.uuid4().hex


def write_folder_meta(path: Path):
    meta = path.with_name(path.name + ".meta")
    if not meta.exists():
        meta.write_text(FOLDER_META.format(guid=new_guid()), encoding="utf-8")


def write_texture_meta(png_path: Path):
    meta = png_path.with_suffix(".png.meta")
    if meta.exists():
        return
    meta.write_text(
        TEXTURE_META.format(guid=new_guid(), sprite_id=new_guid()),
        encoding="utf-8",
    )


def ensure_dir(path: Path):
    path.mkdir(parents=True, exist_ok=True)
    write_folder_meta(path)


def fit_hand(src: Image.Image) -> Image.Image:
    """源图不裁切，缩小放进官方牌面白色区域内，避免花纹盖住牌沿或溢出两侧。"""
    canvas = Image.new("RGBA", (HAND_W, HAND_H), (0, 0, 0, 0))
    rgba = src.convert("RGBA")
    if rgba.size[0] <= 0 or rgba.size[1] <= 0:
        return canvas
    inner_w = HAND_W - HAND_PAD_X * 2
    inner_h = HAND_H - HAND_RIM_TOP - HAND_PAD_BOTTOM
    scale = min(inner_w / rgba.size[0], inner_h / rgba.size[1])
    nw = max(1, int(round(rgba.size[0] * scale)))
    nh = max(1, int(round(rgba.size[1] * scale)))
    resized = rgba.resize((nw, nh), Image.Resampling.LANCZOS)
    ox = (HAND_W - nw) // 2
    oy = HAND_RIM_TOP + (inner_h - nh) // 2
    canvas.alpha_composite(resized, (ox, oy))
    return canvas


def fit_table(src: Image.Image) -> Image.Image:
    """宽度略拉宽，高度拉到接近官方 220×366 牌顶高度，居中贴进米色画布。"""
    canvas = Image.new("RGBA", (TABLE_W, TABLE_H), BEIGE)
    rgba = src.convert("RGBA")
    width, height = rgba.size
    if width <= 0 or height <= 0:
        return canvas
    nw = max(1, min(TABLE_W, int(round(TABLE_W * TABLE_FIT_SCALE_X))))
    nh = max(1, min(TABLE_H, int(round(TABLE_H * TABLE_FIT_SCALE_Y))))
    resized = rgba.resize((nw, nh), Image.Resampling.LANCZOS)
    ox = (TABLE_W - nw) // 2
    oy = (TABLE_H - nh) // 2
    canvas.alpha_composite(resized, (ox, oy))
    return canvas.convert("RGB").convert("RGBA")


def save_png(im: Image.Image, dest: Path):
    dest.parent.mkdir(parents=True, exist_ok=True)
    im.save(dest, format="PNG", optimize=True)
    write_texture_meta(dest)


def copy_png(src: Path, dest: Path):
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(src, dest)
    write_texture_meta(dest)


def collect_sources(folder: Path) -> dict[int, Path]:
    found = {}
    for path in folder.glob("*"):
        if path.suffix.lower() != ".png":
            continue
        tile_id = CODE_TO_ID.get(path.stem.lower())
        if tile_id is None:
            print("skip unknown", path.name)
            continue
        found[tile_id] = path
    return found


def write_source_pack(pack_id: str, sources: dict[int, Path]):
    hand_dir = PACKS / pack_id / HAND_DIR
    table_dir = PACKS / pack_id / TABLE_DIR
    ensure_dir(PACKS / pack_id)
    ensure_dir(hand_dir)
    ensure_dir(table_dir)
    for tile_id, src_path in sorted(sources.items()):
        out_id = FLUFFY_FLOWER_ID_REMAP.get(tile_id, tile_id) if pack_id == "fluffy" else tile_id
        src = Image.open(src_path)
        save_png(fit_hand(src), hand_dir / f"{out_id}.png")
        save_png(fit_table(src), table_dir / f"{out_id}.png")
        note = f" -> {out_id}" if out_id != tile_id else ""
        print(f"  {pack_id} {tile_id}{note} <- {src_path.name} {src.size}")
    print("wrote", PACKS / pack_id, "tiles", len(sources))


def write_official():
    pack = PACKS / "official"
    hand_dir = pack / HAND_DIR
    table_dir = pack / TABLE_DIR
    ensure_dir(pack)
    ensure_dir(hand_dir)
    ensure_dir(table_dir)
    count = 0
    for tile_id in STANDARD_IDS:
        hand = OFFICIAL_HAND / f"{tile_id}.png"
        table = OFFICIAL_TABLE / f"{tile_id}.png"
        if hand.is_file():
            copy_png(hand, hand_dir / f"{tile_id}.png")
            count += 1
        if table.is_file():
            copy_png(table, table_dir / f"{tile_id}.png")
    print("wrote official copies", count)


def main():
    ensure_dir(PACKS)
    write_official()

    fluffy = collect_sources(FLUFFY)
    if 47 in fluffy and 2 not in fluffy:
        fluffy[2] = fluffy[47]
    hk = collect_sources(HK)
    if not fluffy:
        raise SystemExit("FluffyStuff PNG not found")
    if not hk:
        raise SystemExit("HkMahjong PNG not found")
    write_source_pack("fluffy", fluffy)
    write_source_pack("hkmahjong", hk)

    official_blank = OFFICIAL_HAND / "2.png"
    if official_blank.is_file():
        copy_png(official_blank, PACKS / "hand-bg-default.png")
        print("wrote", PACKS / "hand-bg-default.png")


if __name__ == "__main__":
    main()
