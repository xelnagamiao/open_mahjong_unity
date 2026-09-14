#!/usr/bin/env python3
"""Rebuild layered built-in faces from preserved original artwork.

Requires Pillow and resvg-py. Official faces use a reviewed per-tile optical
layout, retaining 272x389 hand and 400x532 table canvases.
Official and Fluffy flowers share identical PNGs. Backgrounds and backs are
independent resources. Original artwork and existing Unity GUIDs are preserved.
"""
from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import io
import json
import math
import os
import re
import shutil
from pathlib import Path
import xml.etree.ElementTree as ET

from PIL import Image, ImageChops, ImageFilter
import resvg_py

ROOT = Path(__file__).resolve().parents[2]
TILES = ROOT / "other" / "tiles"
SOURCES = TILES / "sources"
SNOW_SOURCES = SOURCES / "official" / "hand-framed"
DEFAULT_OUTPUT = ROOT / "open_mahjong_unity" / "Assets" / "Resources" / "image"
CANVAS = (400, 532)
HAND_CANVAS = (272, 389)
# This rectangle is wholly inside the blank source's white face and encloses
# every standard source's visible artwork: union (26,49)-(250,365).
SNOW_CROP = (18, 45, 255, 369)
SNOW_FIT = .95
OTHER_FIT = .84
ARTWORK_ENLARGEMENT = 1.18
ARTWORK_MARGIN = 4
SVG_NS = "http://www.w3.org/2000/svg"
BG = (245, 246, 247, 255)
SNOW_LAYOUT = Path(__file__).with_name("snow_artwork_layout.json")

CODE_TO_ID = {f"{n}{s}": base + n for s, base in (("m", 10), ("p", 20), ("s", 30)) for n in range(1, 10)}
CODE_TO_ID.update({"1z": 41, "2z": 42, "3z": 43, "4z": 44,
                   "5z": 46, "6z": 47, "7z": 45,
                   "0m": 105, "0p": 205, "0s": 305})
CODE_TO_ID.update({f"{n}f": 50 + n for n in range(1, 9)})
STANDARD_IDS = sorted(CODE_TO_ID.values()) + [2]


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def source_record(path: Path) -> dict:
    return {"source": path.relative_to(ROOT).as_posix(), "source_sha256": digest(path.read_bytes())}


def fit_geometry(width: float, height: float, fraction: float) -> tuple[float, float, float]:
    scale = min(CANVAS[0] * fraction / width, CANVAS[1] * fraction / height)
    return scale, (CANVAS[0] - width * scale) / 2, (CANVAS[1] - height * scale) / 2


def safe_artwork_fit(size: tuple[float, float], bbox, base_fit: float,
                     sampling_pad: float = 2) -> tuple[float, dict]:
    """Enlarge about the authored canvas center, limited by visible artwork.

    Bounds are measured in the original artwork, not an already clipped render.
    Extra source-pixel support preserves bicubic / antialiased fringes. Each
    axis shares one scale, so the original artwork's proportions are unchanged.
    """
    requested = base_fit * ARTWORK_ENLARGEMENT
    if bbox is None:
        fraction = base_fit
    else:
        half_w, half_h = size[0] / 2, size[1] / 2
        extent_x = max(half_w - bbox[0], bbox[2] - half_w) + sampling_pad
        extent_y = max(half_h - bbox[1], bbox[3] - half_h) + sampling_pad
        maximum_scale = min((CANVAS[0] / 2 - ARTWORK_MARGIN) / extent_x,
                            (CANVAS[1] / 2 - ARTWORK_MARGIN) / extent_y)
        base_scale = min(CANVAS[0] / size[0], CANVAS[1] / size[1])
        fraction = min(requested, maximum_scale / base_scale)
    return fraction, {"requested_enlargement": ARTWORK_ENLARGEMENT,
                      "applied_enlargement": fraction / base_fit,
                      "safety_limited": bbox is not None and fraction < requested - 1e-9,
                      "artwork_bbox_source": bbox,
                      "artwork_margin_pixels": ARTWORK_MARGIN,
                      "source_sampling_pad": sampling_pad}


def assert_artwork_margin(bbox, source: Path):
    if bbox and (bbox[0] < ARTWORK_MARGIN or bbox[1] < ARTWORK_MARGIN
                 or bbox[2] > CANVAS[0] - ARTWORK_MARGIN
                 or bbox[3] > CANVAS[1] - ARTWORK_MARGIN):
        raise ValueError(f"Artwork exceeds the {ARTWORK_MARGIN}px safe inset: {source}: {bbox}")


def fit_raster(source: Image.Image, fraction: float) -> tuple[Image.Image, dict]:
    """One uniform affine resample; premultiplied alpha avoids dark color fringes."""
    scale, x, y = fit_geometry(*source.size, fraction)
    premultiplied = source.convert("RGBA").convert("RGBa")
    result = premultiplied.transform(
        CANVAS, Image.Transform.AFFINE,
        (1 / scale, 0, -x / scale, 0, 1 / scale, -y / scale),
        Image.Resampling.BICUBIC, fillcolor=(0, 0, 0, 0)).convert("RGBA")
    return result, {"operation": "single uniform bicubic affine on premultiplied RGBA",
                    "source_region_size": list(source.size), "uniform_scale": scale,
                    "canvas_offset": [x, y], "fit_fraction": fraction}


def fit_svg(path: Path, fraction: float) -> tuple[Image.Image, dict]:
    source = ET.fromstring(path.read_text(encoding="utf-8-sig"))
    box = [float(v) for v in source.attrib["viewBox"].replace(",", " ").split()]
    if len(box) != 4 or min(box[2:]) <= 0:
        raise ValueError(f"Invalid SVG viewBox: {path}")
    scale, x, y = fit_geometry(box[2], box[3], fraction)
    # Nest the original root inside the final canvas. The vector is rasterized
    # exactly once at its final size; no intermediate 300x400 PNG is rescaled.
    source.set("x", str(x)); source.set("y", str(y))
    source.set("width", str(box[2] * scale)); source.set("height", str(box[3] * scale))
    source.set("preserveAspectRatio", "xMidYMid meet")
    outer = ET.Element(f"{{{SVG_NS}}}svg", {"width": str(CANVAS[0]), "height": str(CANVAS[1]),
        "viewBox": f"0 0 {CANVAS[0]} {CANVAS[1]}"})
    outer.append(source)
    data = resvg_py.svg_to_bytes(svg_string=ET.tostring(outer, encoding="unicode"),
        resources_dir=str(path.parent), width=CANVAS[0], height=CANVAS[1],
        skip_system_fonts=True)
    result = Image.open(io.BytesIO(data)).convert("RGBA")
    return result, {"operation": "SVG rendered once into final transparent canvas with resvg",
                    "source_viewbox": box, "uniform_scale": scale,
                    "canvas_offset": [x, y], "fit_fraction": fraction}


def validate_snow_crop() -> dict:
    base = Image.open(SNOW_SOURCES / "2.png").convert("RGBA")
    left, top, right, bottom = SNOW_CROP
    if any(pixel != BG for pixel in base.crop(SNOW_CROP).getdata()):
        raise ValueError("Snow crop would retain part of the 2D frame")
    reference = list(base.getdata())
    rows = []
    for tile_id in STANDARD_IDS:
        path = SNOW_SOURCES / f"{tile_id}.png"
        source = Image.open(path).convert("RGBA")
        if source.size != base.size:
            raise ValueError(f"Unexpected snow source size: {path}")
        points = [(i % source.width, i // source.width)
                  for i, (blank, actual) in enumerate(zip(reference, source.getdata()))
                  if blank == BG and actual != BG]
        clipped = [(x, y) for x, y in points if not (left <= x < right and top <= y < bottom)]
        if clipped:
            raise ValueError(f"Snow crop cuts {len(clipped)} artwork pixels from {tile_id}")
        bbox = [min(x for x, y in points), min(y for x, y in points),
                max(x for x, y in points) + 1, max(y for x, y in points) + 1] if points else None
        rows.append({"tile_id": tile_id, "artwork_bbox_inside_original_face": bbox, "clipped_artwork_pixels": 0})
    return {"crop_xyxy": list(SNOW_CROP), "blank_crop_is_uniform_face_color": True,
            "method": "artwork differs from blank source inside its opaque face; transparent RGB and 2D frame ignored",
            "tiles": rows}


def extract_snow_artwork(source: Image.Image) -> tuple[Image.Image, dict]:
    """Separate the known blank face from ink, retaining authored white detail.

    Only the exact blank RGB becomes transparent, including genuine inner
    holes. Opaque artwork interiors remain byte-for-byte unchanged. In a two
    source-pixel boundary band, dominant ink colors estimate alpha; inverse
    compositing removes the original matte from antialias colors. Every pixel
    is then checked by compositing over the original blank color. This is not
    a threshold that removes arbitrary white pixels.
    """
    artwork = source.convert("RGBA").crop(SNOW_CROP)
    pixels = list(artwork.getdata())
    background = BG[:3]
    mask = Image.new("L", artwork.size)
    mask.putdata([255 if p[:3] != background else 0 for p in pixels])
    interior = list(mask.filter(ImageFilter.MinFilter(5)).getdata())
    counts = Counter(p[:3] for p in pixels
                     if max(abs(p[k] - background[k]) for k in range(3)) >= 100
                     or p[:3] == (255, 255, 255))
    anchors = []
    for color, frequency in counts.most_common(24):
        direction = [color[k] - background[k] for k in range(3)]
        anchors.append((direction, sum(v * v for v in direction), frequency))
    output = []
    max_error = white_count = changed_interior = 0
    for index, pixel in enumerate(pixels):
        color = pixel[:3]
        if color == background:
            output.append((0, 0, 0, 0))
            continue
        alpha = 1.0
        delta = [color[k] - background[k] for k in range(3)]
        if not interior[index] and anchors and color != (255, 255, 255):
            estimates = []
            for direction, norm, frequency in anchors:
                value = max(0.0, min(1.0, sum(delta[k] * direction[k] for k in range(3)) / norm))
                residual = sum((delta[k] - value * direction[k]) ** 2 for k in range(3))
                estimates.append((residual + .01 * value, -frequency, value))
            alpha = min(estimates)[2]
            # Lower bound required to recover an in-gamut straight RGB value.
            minimum = max((background[k] - color[k]) / background[k]
                          if color[k] < background[k]
                          else (color[k] - background[k]) / (255 - background[k])
                          for k in range(3))
            alpha = max(alpha, minimum)
        alpha_byte = max(1, round(alpha * 255))
        alpha = alpha_byte / 255
        foreground = tuple(round(max(0, min(255,
            (color[k] - (1 - alpha) * background[k]) / alpha))) for k in range(3))
        result = (*foreground, alpha_byte)
        output.append(result)
        error = max(abs(round(foreground[k] * alpha + background[k] * (1 - alpha)) - color[k])
                    for k in range(3))
        max_error = max(max_error, error)
        if interior[index] and result != pixel:
            changed_interior += 1
        if color == (255, 255, 255):
            if result != (255, 255, 255, 255):
                raise ValueError("Authored opaque white artwork must remain opaque white")
            white_count += 1
    if max_error > 1 or changed_interior:
        raise ValueError(f"Snow alpha separation changed ink: error={max_error}, interiors={changed_interior}")
    artwork.putdata(output)
    hand = Image.new("RGBA", source.size)
    hand.paste(artwork, SNOW_CROP[:2])
    return hand, {"alpha_method": "exact known blank removal; ink-palette dematting only at artwork boundary",
                  "blank_rgb": list(background), "opaque_white_pixels_preserved": white_count,
                  "opaque_interior_pixels_changed": changed_interior,
                  "maximum_original_background_recomposition_error": max_error,
                  "semi_transparent_pixels": sum(0 < p[3] < 255 for p in output)}


def write_png(image: Image.Image, path: Path) -> str:
    if image.size not in (CANVAS, HAND_CANVAS) or image.mode != "RGBA":
        raise ValueError(f"Unexpected output format: {path}")
    path.parent.mkdir(parents=True, exist_ok=True)
    data = io.BytesIO(); image.save(data, format="PNG", optimize=True)
    encoded = data.getvalue()
    if not path.exists() or path.read_bytes() != encoded:
        temporary = path.with_name(path.name + ".rebuild-temp")
        temporary.write_bytes(encoded)
        os.replace(temporary, path)
    return digest(encoded)


def layout_snow_artwork(source: Image.Image, layout: dict, kind: str,
                        blank_hand: Image.Image) -> tuple[Image.Image, dict]:
    """Compose the manual placement with the baseline into ONE source resample.

    Offsets and pivots are authored in the preserved 272x389 source pixels.
    Hand/table magnifications are independent; neither is an X/Y stretch.
    Bounds are assertions, never automatic recentering or silent cropping.
    """
    factor = float(layout[f"{kind}_scale"])
    px, py = layout["pivot"]
    dx, dy = layout["offset"]
    if not math.isfinite(factor) or factor <= 0 or not all(abs(v) < 1000 for v in (px, py, dx, dy)):
        raise ValueError(f"Invalid manual Snow layout: {layout}")
    scale, x, y = factor, px * (1 - factor) + dx, py * (1 - factor) + dy
    details = {"manual_layout": layout, "operation": "manual uniform placement; one source-to-output premultiplied bicubic affine"}
    canvas = HAND_CANVAS
    if kind == "table":
        crop = source.crop(SNOW_CROP)
        fraction, safety = safe_artwork_fit(crop.size, crop.getchannel("A").getbbox(), SNOW_FIT)
        baseline, bx, by = fit_geometry(*crop.size, fraction)
        scale *= baseline
        x = bx + baseline * (x - SNOW_CROP[0])
        y = by + baseline * (y - SNOW_CROP[1])
        details.update({"baseline_fit": safety, "baseline_uniform_scale": baseline,
                        "baseline_fit_fraction": fraction})
        canvas = CANVAS
    bounds = source.getchannel("A").getbbox()
    if bounds is None:
        result = Image.new("RGBA", canvas)
    else:
        # Check BEFORE rasterization: a clipped component (or completely lost
        # glyph) cannot be detected from the already rendered alpha bounds.
        padded = [scale * (bounds[0] - 2) + x, scale * (bounds[1] - 2) + y,
                  scale * (bounds[2] + 2) + x, scale * (bounds[3] + 2) + y]
        if padded[0] < 0 or padded[1] < 0 or padded[2] > canvas[0] or padded[3] > canvas[1]:
            raise ValueError(f"Manual Snow layout would crop source artwork/filter support: {layout}: {padded}")
        details["source_bounds_with_filter_support_output"] = padded
        result = source.convert("RGBa").transform(canvas, Image.Transform.AFFINE,
            (1 / scale, 0, -x / scale, 0, 1 / scale, -y / scale),
            Image.Resampling.BICUBIC, fillcolor=(0, 0, 0, 0)).convert("RGBA")
    if kind == "hand":
        # Allow reviewed use of the original face's rounded white area, while
        # rejecting even a faint filter fringe on the bevel or back strip.
        outside = sum(p[3] > 0 and background != BG
                      for p, background in zip(result.getdata(), blank_hand.getdata()))
        if outside:
            raise ValueError(f"Manual Snow hand layout overlaps {outside} frame pixels: {layout}")
        details["artwork_pixels_on_frame"] = outside
    else:
        assert_artwork_margin(result.getchannel("A").getbbox(), Path(f"snow/{layout}"))
    details.update({"uniform_scale": scale, "canvas_offset": [x, y],
                    "artwork_bbox_output": result.getchannel("A").getbbox()})
    return result, details


def ensure_full_rect(path: Path) -> bool:
    """Keep the original canvas in Sprite.textureRect, preserving each GUID.

    Tight source sprites may trim alpha even when the atlas disables tight
    packing. Only this mesh setting is changed; absent metadata is not created.
    """
    metadata = path.with_suffix(path.suffix + ".meta")
    if not metadata.exists():
        return False
    before = metadata.read_bytes()
    after, count = re.subn(rb"(?m)^(  spriteMeshType: )[01](\r?)$", rb"\g<1>0\2", before)
    if count != 1:
        raise ValueError(f"Expected one spriteMeshType in {metadata}")
    if after != before:
        metadata.write_bytes(after)
        return True
    return False


def rebuild(output: Path = DEFAULT_OUTPUT, report_path: Path | None = None) -> dict:
    manual_layout = json.loads(SNOW_LAYOUT.read_text(encoding="utf-8"))
    if set(manual_layout["tiles"]) != {str(tile_id) for tile_id in STANDARD_IDS}:
        raise ValueError("Snow manual layout must explicitly cover every standard tile")
    report = {"canvas": list(CANVAS), "hand_canvas": list(HAND_CANVAS),
              "snow_manual_layout": source_record(SNOW_LAYOUT),
              "snow_crop_validation": validate_snow_crop(), "images": [],
              "mapping": CODE_TO_ID, "fluffy_flowers": "51-58 share official hand/table PNG bytes",
              "untouched": ["non-flower Fluffy and all HK hand PNGs", "all original artwork", "all existing .meta GUIDs"],
              "metadata_policy": "spriteMeshType=0 (FullRect); every other PNG meta field and GUID preserved",
              "metadata_changed": []}

    def save(image: Image.Image, relative: str, source: Path, details: dict):
        target = output / relative
        record = source_record(source)
        record.update(details)
        record.update({"output": str(target.relative_to(ROOT)) if target.is_relative_to(ROOT) else str(target),
                       "output_sha256": write_png(image, target), "size": list(image.size),
                       "alpha_bbox": image.getchannel("A").getbbox()})
        if ensure_full_rect(target):
            report["metadata_changed"].append(record["output"] + ".meta")
        report["images"].append(record)

    blank_hand = Image.open(SNOW_SOURCES / "2.png").convert("RGBA")
    for tile_id in STANDARD_IDS:
        path = SNOW_SOURCES / f"{tile_id}.png"
        original = Image.open(path).convert("RGBA")
        artwork, alpha_details = extract_snow_artwork(original)
        recomposed = Image.alpha_composite(blank_hand, artwork)
        # Source files differ in RGB beneath fully transparent corners. Compare
        # premultiplied pixels so invisible RGB is not mistaken for changed art.
        difference = ImageChops.difference(recomposed.convert("RGBa"), original.convert("RGBa"))
        maximum_difference = max(maximum for minimum, maximum in difference.crop(SNOW_CROP).getextrema())
        if maximum_difference > 1:
            raise ValueError(f"Hand artwork plus background does not reproduce original face pixels: {path}")
        layout = manual_layout["tiles"][str(tile_id)]
        hand, placement = layout_snow_artwork(artwork, layout, "hand", blank_hand)
        hand_details = {**alpha_details, **placement, "pack": "official", "tile_id": tile_id,
                        "maximum_pre_transform_artwork_recomposition_error": maximum_difference,
                        "legacy_frame_maximum_difference": max(v[1] for v in difference.getextrema()),
                        "frame_policy": "independent shared hand-default replaces varying legacy frame antialiasing"}
        save(hand, f"Cards/Faces/official/hand/{tile_id}.png", path, hand_details)
        image, details = layout_snow_artwork(artwork, layout, "table", blank_hand)
        artwork_bbox = image.getchannel("A").getbbox()
        assert_artwork_margin(artwork_bbox, path)
        details["artwork_bbox_output"] = artwork_bbox
        details.update({**alpha_details, "pack": "official", "tile_id": tile_id, "crop_xyxy": list(SNOW_CROP)})
        save(image, f"Cards/Faces/official/table/{tile_id}.png", path, details)
        if 51 <= tile_id <= 58:
            save(hand, f"Cards/Faces/fluffy/hand/{tile_id}.png", path,
                 {**hand_details, "pack": "fluffy", "shared_artwork": f"official/hand/{tile_id}.png"})
            save(image, f"Cards/Faces/fluffy/table/{tile_id}.png", path,
                 {**details, "pack": "fluffy", "shared_artwork": f"official/table/{tile_id}.png"})

    # Atlas-only 0 has no foreground artwork. Its surface is controlled by the
    # material/background layer; the navy hand back is an independent resource.
    save(Image.new("RGBA", CANVAS), "Cards/Faces/official/table/0.png", SNOW_SOURCES / "2.png",
         {"pack": "official-atlas-only", "tile_id": 0, "operation": "fully transparent empty foreground"})

    for pack in ("fluffy", "hkmahjong"):
        written = set(range(51, 59)) if pack == "fluffy" else set()
        for code, tile_id in CODE_TO_ID.items():
            if pack == "fluffy" and 51 <= tile_id <= 58:
                continue
            png = SOURCES / pack / f"{code}.png"
            svg = png.with_suffix(".svg")
            if not png.exists() and not svg.exists():
                # HK has no red fives; existing official fallback remains authoritative.
                if pack == "hkmahjong" and code in ("0m", "0p", "0s"):
                    continue
                raise FileNotFoundError(f"Missing original {pack} artwork: {code}")
            source = svg if pack == "fluffy" and svg.exists() else png
            # Original PNG companions provide conservative alpha bounds in their
            # authored canvas. Fluffy's final pixels still come directly from SVG.
            original = Image.open(png).convert("RGBA")
            fraction, safety = safe_artwork_fit(original.size, original.getchannel("A").getbbox(), OTHER_FIT)
            if source.suffix == ".svg":
                image, details = fit_svg(source, fraction)
                safety.update({"bounds_source": png.relative_to(ROOT).as_posix(),
                               "bounds_source_sha256": digest(png.read_bytes())})
            else:
                image, details = fit_raster(original, fraction)
            artwork_bbox = image.getchannel("A").getbbox()
            assert_artwork_margin(artwork_bbox, source)
            details.update(safety)
            details["artwork_bbox_output"] = artwork_bbox
            out_id = tile_id
            details.update({"pack": pack, "tile_id": out_id, "source_code": code})
            save(image, f"Cards/Faces/{pack}/table/{out_id}.png", source, details)
            written.add(out_id)
            if pack == "fluffy" and code == "5z":
                save(image, "Cards/Faces/fluffy/table/2.png", source,
                     {**details, "tile_id": 2, "source_code": "5z (same authored blank face)"})
                written.add(2)
        if pack == "hkmahjong":
            # Blank white dragon is a separate empty foreground; retain the authored
            # framed 46. The shared hand/table body supplies its white surface.
            for kind, size in (("hand", HAND_CANVAS), ("table", CANVAS)):
                save(Image.new("RGBA", size), f"Cards/Faces/hkmahjong/{kind}/2.png",
                     SNOW_SOURCES / "2.png", {"pack": pack, "tile_id": 2,
                     "operation": "fully transparent blank white dragon foreground"})
            written.add(2)
        expected = 46 if pack == "fluffy" else 43
        if len(written) != expected:
            raise ValueError(f"Unexpected {pack} face count: {len(written)}")

    # Existing Fluffy/HK hand art is preserved, but transparent hand canvases
    # also need FullRect. Otherwise Sprite.textureRect silently trims padding.
    for pack in ("official", "fluffy", "hkmahjong"):
        for target in (output / "Cards/Faces" / pack / "hand").glob("*.png"):
            if Image.open(target).convert("RGBA").getchannel("A").getextrema()[0] < 255 and ensure_full_rect(target):
                relative = str(target.relative_to(ROOT)) if target.is_relative_to(ROOT) else str(target)
                report["metadata_changed"].append(relative + ".meta")

    if report_path is not None:
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    return report


def export_pack_assets(resource_root: Path, export_root: Path) -> dict:
    """Mirror usable hand/table PNGs and separate surfaces without Unity meta.

    Run against a complete resource tree: unchanged Fluffy/HK hand artwork is
    copied from that tree. Atlas-only zero is deliberately not an upload face.
    """
    rows = []
    counts = {}
    if (export_root / "packs" / "official" / "table" / "0.png").exists():
        raise ValueError("Upload export contains atlas-only official/table/0.png; move it out before exporting")
    for pack in ("official", "fluffy", "hkmahjong"):
        for kind in ("hand", "table"):
            folder = resource_root / "Cards/Faces" / pack / kind
            sources = [p for p in folder.glob("*.png") if p.stem.isdigit() and int(p.stem) in STANDARD_IDS]
            expected = 43 if pack == "hkmahjong" else 46
            if len(sources) != expected:
                raise ValueError(f"Export requires a complete resource tree: {folder}: expected {expected}, found {len(sources)}")
            counts[f"{pack}/{kind}"] = len(sources)
            for source in sources:
                relative = Path("packs") / pack / kind / source.name
                target = export_root / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                if not target.exists() or source.read_bytes() != target.read_bytes():
                    shutil.copyfile(source, target)
                rows.append({"path": relative.as_posix(), "sha256": digest(target.read_bytes())})
    for relative in ("backgrounds/hand-default.png", "backgrounds/hand-horizontal.png", "backs/hand-default.png"):
        source = resource_root / "Cards/Surfaces" / relative
        target = export_root / "surfaces" / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        if not target.exists() or source.read_bytes() != target.read_bytes():
            shutil.copyfile(source, target)
        rows.append({"path": target.relative_to(export_root).as_posix(), "sha256": digest(target.read_bytes())})
    return {"counts": counts, "files": rows, "atlas_only_0_excluded": True,
            "hk_missing_faces_use_runtime_official_fallback": [105, 205, 305]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-root", type=Path, default=DEFAULT_OUTPUT, help="directory corresponding to Resources/image")
    parser.add_argument("--report", type=Path, default=TILES / "face-build.json")
    parser.add_argument("--export-root", type=Path, help="also mirror complete packs/ and surfaces/ under this folder")
    args = parser.parse_args()
    report = rebuild(args.output_root.resolve(), args.report.resolve())
    if args.export_root is not None:
        exported = export_pack_assets(args.output_root.resolve(), args.export_root.resolve())
        report["exported_assets"] = exported
        args.report.resolve().write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Rebuilt {len(report['images'])} layered hand/table PNGs; original artwork and Unity GUIDs preserved.")
    print(f"Report: {args.report.resolve()}")


if __name__ == "__main__":
    main()
