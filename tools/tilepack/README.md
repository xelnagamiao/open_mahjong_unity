# Built-in layered tile artwork

`rebuild_table_faces.py` is the maintained generator. Install `Pillow` and
`resvg-py`, then run from the repository root:

```powershell
python tools/tilepack/rebuild_table_faces.py --export-root other/tiles
```

The generator writes official hand/table faces, Fluffy's shared flower faces,
and the other built-in table faces under Unity `Resources/image/Cards/Faces`.
It preserves source artwork, existing Unity GUIDs, unchanged Fluffy/HK hand
artwork, and all PNG importer fields except `spriteMeshType: 0` (FullRect).
Transparent hand sprites also require FullRect; otherwise Unity can trim the
authored padding when a sprite's `textureRect` is used for previews.

| Pack | Original artwork | Hand | Table |
| --- | --- | --- | --- |
| Official / Xuefun | `other/tiles/sources/official/hand-framed` | 46 transparent 272 × 389 faces with reviewed optical placement | 46 transparent 400 × 532 faces, plus atlas-only empty `0` |
| Fluffy | Original SVG/PNG in `sources/fluffy` | Existing 272 × 389 art; flowers 51–58 exactly copy official hand PNGs | SVG rasterized once at final size; flowers exactly copy official table PNGs |
| HK Mahjong | Original PNG in `sources/hkmahjong` | Existing hand art | Original PNG uniformly resampled; no stretched axes |

The official source's blank face color is exactly **RGB (245, 246, 247)**.
This known surface color becomes transparent, including actual holes inside
characters and motifs. It is distinct from authored white ink: opaque white
details such as the circles' white rings and centers remain opaque white.
Opaque artwork interiors keep their original pixels. In a two-source-pixel
boundary band, the generator estimates alpha from the original ink palette
and removes the old matte from antialias colors. Each recovered pixel is
recomposited over the original surface color and must differ by at most one
8-bit code value. The accepted build's maximum error is zero.

Alpha extraction is checked against the original **artwork region before
placement**. The shared background supplies one consistent tile body, replacing
the old files' slightly different frame antialiasing. No legacy frame is baked
into the transparent foreground.

[`snow_artwork_layout.json`](snow_artwork_layout.json) stores the manually
reviewed Snow layout for every tile. `hand_scale` is relative to the original
hand artwork; `table_scale` is relative to the fixed table baseline below.
`pivot` and `offset` use original **272 × 389 source pixels**, with positive X
right and positive Y down. Table offsets are converted through the baseline
scale. Each layer has a uniform scale, never separate X/Y stretching.

The two layers are rendered independently from the extracted original art,
with the complete transform combined into **one** premultiplied bicubic
resample per output. Generated hand PNGs are never inputs to table generation.
Manzu share the same pivot and offset to preserve the lower character baseline.
Other tiles use explicit optical adjustments; the generator does not center
characters by ink centroid or force different glyphs into identical bounds.
Hand output is checked pixel by pixel against the original blank face: no
nontransparent artwork may overlap its bevel/back strip. Table output must
retain four pixels of transparent margin. Invalid edits fail instead of silently
cropping or changing the requested layout.

The table baseline uses a common original crop
`(18,45)-(255,369)`, baseline fit 95% for official and 84% for the other packs,
then a requested **18% enlargement**. `safe_artwork_fit` preserves at least
four output pixels of margin, including filter support; long artwork can be
limited before rendering. Snow adds its explicit manual layout to this baseline;
the other packs keep their accepted placement. Raster art uses one
premultiplied-alpha bicubic resample; Fluffy SVGs
are rasterized once at the final size. Runtime mapping must not add another
legacy crop or independent X/Y stretch.

Foregrounds live in `Cards/Faces/{official,fluffy,hkmahjong}/{hand,table}`.
The official atlas is `Cards/Faces/official/TableAtlas.spriteatlasv2` and
uses that same table directory, with rotation/tight packing disabled and a
4096 atlas limit. Its 47 full-size sprites must fit on one page. Atlas-only
`table/0.png` and blank `2.png` contain no foreground ink; they do not encode
a card back or force a face color.

Tile-body resources are separate:

- `Cards/Surfaces/backgrounds/hand-default.png`: vertical default body.
- `Cards/Surfaces/backgrounds/hand-horizontal.png`: horizontal body, formerly `1.png`.
- `Cards/Surfaces/backs/hand-default.png`: navy back, formerly `0.png`.

`--output-root <directory>` creates candidate PNGs without overwriting the
main project. `--report <json>` writes per-image source/output hashes,
placement, alpha validation, and metadata changes. The default report is
`other/tiles/face-build.json`.

`--export-root other/tiles` additionally copies a complete resource tree's
usable PNGs to `other/tiles/packs/{pack}/{hand,table}` and its independent
bodies/backs to `other/tiles/surfaces`. Official and Fluffy exports have 46
standard faces per layer; atlas-only `0.png` is excluded from upload packages.
HK preserves its 42 originals per layer and adds a separate blank white dragon
`2` (43 faces total). Only `105`, `205`, and `305` use the official fallback. An export requires the complete resource tree
because unchanged Fluffy/HK hand artwork is copied from there.

IDs match the game: `21 = 1p`, `31 = 1s`, `45 = red dragon`, `46 = white
dragon`, `47 = green dragon`, and `51–58` are the game's flower order. Fluffy
flowers now use official game IDs directly; the old SVG flower-order remap
is no longer applied. The unused original SVG flowers remain in the source
archive for provenance.

The legacy compatibility shim and the old one-off rebuild experiments are kept
only in the local cleanup quarantine. Use `rebuild_table_faces.py` as the sole
maintained entry point; do not recreate the obsolete output layouts.
