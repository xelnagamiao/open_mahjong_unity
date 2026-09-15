# Built-in layered tile artwork

`rebuild_table_faces.py` is the maintained generator. Install `Pillow` and
`resvg-py`, then run from the repository root:

```powershell
python tools/tilepack/rebuild_table_faces.py --export-root other/tiles
```

The generator writes official hand/table faces, Fluffy's shared flower faces,
and the other built-in table faces under Unity `Resources/image/Cards/Faces`.
It preserves source artwork, existing Unity GUIDs, non-flower Fluffy/HK hand
artwork, and all PNG importer fields except `spriteMeshType: 0` (FullRect).
Transparent hand sprites also require FullRect; otherwise Unity can trim the
authored padding when a sprite's `textureRect` is used for previews.

| Pack | Original artwork | Hand | Table |
| --- | --- | --- | --- |
| Official / Xuefun | `other/tiles/sources/official/hand-framed` | 46 transparent 272 × 389 faces, original artwork size and position | 46 transparent 400 × 532 faces, plus atlas-only empty `0` |
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

Each numbered suit also shares one ink palette, built from its original
sources before extraction. Equal source RGB with the same interior/boundary
classification therefore produces equal RGBA across the suit. A different
upper numeral cannot change the alpha of the repeated lower 萬 character.

[`snow_artwork_layout.json`](snow_artwork_layout.json) uses version 3 family
transforms. `families.manzu`, `families.pinzu`, and `families.souzu` each list
their `tile_ids`, including the corresponding red five. Each family's `table`
contains the final uniform `scale` and two-component `offset`:

```text
output_position = scale * original_source_position + offset
```

Source positions use the original **272 × 389** canvas; `scale` is output
pixels per source pixel and `offset` is in output pixels, with positive X
right and positive Y down. These are complete source-to-output transforms,
with no hidden crop-fit multiplier. Family members share the entire matrix,
preserving the original numeral, repeated character, and row/column positions.

All `hand` matrices are fixed at `scale: 1, offset: [0, 0]`. The 2D hand
copies the extracted original artwork without resampling, enlargement, or
optical repositioning. This includes honors and flowers. Table layout changes
and exceptions never affect hand artwork; the generator rejects nonidentity
hand matrices to prevent accidental coupling.

Family `exceptions` may contain only a documented `source_translation`;
they cannot override scale. Currently only `32` (two bamboo) moves up by
12.5 source pixels in the table layer only, before the family's transform. Original differences such
as the smaller three-row bamboo motifs and the linked eight-bamboo shapes
remain part of the artwork. Different tiles are not forced into equal bounds.

`independent_artwork` records final matrices for honors, flowers, and blank.
Their table optical placement and previous individual ink extraction are
retained. Their hand matrices preserve original size and position, like all
other Snow hand art. The preserved source PNGs remain untouched.

Snow table art uses a **.93** complete-image mapping area; ordinary uploads
and the other packs retain **.86**. The larger Snow area restores more of the
pre-redesign face coverage while respecting the current model. This mapping
is unchanged by the family-alignment update. To adjust a numbered suit,
change its shared matrix and validate every member together; never add an
individual fit to rescue one tile that exceeds the available face.

The two layers are generated independently from the extracted original art.
The table transform uses **one** premultiplied bicubic resample; hand art is
copied without resampling. Generated hand PNGs are never inputs to table generation.
Snow bounds are validation only; they never independently resize or recenter
a tile. The shared manzu matrix preserves the lower character baseline.
The build also compares the complete repeated 萬 region in all ten sources,
extracted RGBA images, and both rendered layers; differing pixels or baselines
fail the build, even if the numeric transform settings happen to match.
Hand output is checked pixel by pixel against the original blank face: no
nontransparent artwork may overlap its bevel/back strip. Table output must
retain four pixels of transparent margin. Invalid edits fail instead of silently
cropping or changing the requested layout.
Snow also checks every nontransparent pixel's four corners against the
measured rounded front of `Resources/Materials/Tiles/3DTile.fbx`, reserving
two final texture pixels of sampling clearance. With `FrontRotation=270`,
image X uses UV0.V and image Y uses UV0.U: the flat front is about 88% of
the body's width and 91.4% of its height. Update `SNOW_FRONT_OUTLINE` if that
model's front UV changes.

Snow's original crop `(18,45)-(255,369)` is used only to extract the known
face region; it does not determine the output size or position. Snow no longer
uses `safe_artwork_fit`. The other packs retain their accepted 84% baseline
fit and requested **18% enlargement**, with `safe_artwork_fit` preserving
four output pixels of margin including filter support. Raster art uses one
premultiplied-alpha bicubic resample; Fluffy SVGs
are rasterized once at the final size. Runtime mapping must not add another
legacy crop or independent X/Y stretch.

Foregrounds live in `Cards/Faces/{official,fluffy,hkmahjong}/{hand,table}`.
The official atlas is `Cards/Faces/official/TableAtlas.spriteatlasv2` and
uses that same table directory, with rotation/tight packing disabled and a
4096 atlas limit. Its 47 full-size sprites must fit on one page. Atlas-only
`table/0.png` and blank `2.png` contain no foreground ink; they do not encode
a card back or force a face color.

All official table PNGs and Fluffy table flowers 51–58 contain the PNG tEXt
entry **`om-table-image-scale-v1=0.93`**. Built-in sources select the same
scale; uploaded copies read the entry once at loading and share it with their
previews, so the exported PNG has the same displayed
coverage as the installed pack. It is placement metadata, not an instruction
to stretch the pixels. Keep this entry when editing/re-exporting these table
files. Files without the entry use the standard .86 image area. Hand images
and unrelated packs do not receive this entry.

Tile-body resources are separate:

- `Cards/Surfaces/backgrounds/hand-default.png`: vertical default body.
- `Cards/Surfaces/backgrounds/hand-horizontal.png`: horizontal body, formerly `1.png`.
- `Cards/Surfaces/backs/hand-default.png`: navy back, formerly `0.png`.

`--output-root <directory>` creates candidate PNGs without overwriting the
main project. `--report <json>` writes per-image source/output hashes,
placement, alpha validation, and metadata changes. The default report is
`.om_workspace/tilepack-build/face-build.json`, outside shared assets.

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
