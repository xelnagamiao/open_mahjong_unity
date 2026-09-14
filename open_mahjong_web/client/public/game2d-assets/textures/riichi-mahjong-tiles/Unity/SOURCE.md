These eight flower tile faces use the same FluffyStuff artwork as the Unity
hand pack (`Assets/Resources/image/Cards/Faces/fluffy/hand/51.png`–
`58.png`). That PNG pack is a downscaled raster, so 2D uses the original
vectors `1f.svg`–`8f.svg` fitted onto a 900×1200 (3× 300×400) canvas.

Files are named by Salasasa / 国标 tile id:

- 51-54 春夏秋冬
- 55-58 梅兰竹菊

FluffyStuff 源文件按日式四君子编号：`7f`=菊、`8f`=竹。
国标 ID 是梅兰竹菊，因此 `7f.svg` 写入 `58.svg`，`8f.svg` 写入 `57.svg`。

2026-09-10 display update: the existing vector paths, colors, and mapping are
unchanged. Removed the extra 6% inset on each side of the nested SVG; the
original 19×26 viewBox now fits the full 900×1200 canvas with its aspect ratio
preserved. The scene displays these transparent patterns at 94% of the tile
face instead of applying the 5/6 scale used for full tile images. Flower fade
rendering inherits the canvas resolution and antialiasing to retain detail
on high-DPI displays. These high-resolution textures use mipmaps for smooth
reduction to table size. The Unity PNG files are unchanged.
