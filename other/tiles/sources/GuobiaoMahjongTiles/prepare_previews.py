"""Export GNOME atlas cells and make browsing previews; never edits upstream files.

Requires Pillow and resvg-py. Run: python prepare_previews.py
"""
from pathlib import Path
from io import BytesIO
import csv
import hashlib
import json
from PIL import Image, ImageDraw, ImageFont
import resvg_py

ROOT = Path(__file__).resolve().parent
GNOME = ROOT / '03_GNOME'
FONT = 'C:/Windows/Fonts/msyh.ttc'

tiles = [(f'{i}m', f'{i}万') for i in range(1, 10)]
tiles += [(f'{i}p', f'{i}筒') for i in range(1, 10)]
tiles += [(f'{i}s', f'{i}条') for i in range(1, 10)]
tiles += list(zip(['east', 'south', 'west', 'north', 'red', 'green', 'white'],
                  ['东', '南', '西', '北', '中', '发', '白']))
tiles += list(zip(['spring', 'summer', 'autumn', 'winter', 'plum', 'orchid', 'chrysanthemum', 'bamboo'],
                  ['春', '夏', '秋', '冬', '梅', '兰', '菊', '竹']))

# GNOME Smooth order. Postmodern exchanges the flower/season groups.
# The second atlas row is the selected state.
gnome_order = [f'{i}p' for i in range(1, 10)]
gnome_order += ['north', 'west', 'south', 'east', 'red', 'green']
gnome_order += [f'{i}m' for i in range(1, 10)]
gnome_order += [f'{i}s' for i in range(1, 10)]
gnome_order += ['orchid', 'chrysanthemum', 'plum', 'bamboo', 'white', 'spring', 'summer', 'autumn', 'winter', 'blank']
assert len(gnome_order) == 43

def write_csv(path, rows):
    with path.open('w', encoding='utf-8-sig', newline='') as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)

def contact_sheet(paths, labels, title, subtitle, out, columns=9):
    cell_w, cell_h = 132, 182
    rows = (len(paths) + columns - 1) // columns
    canvas = Image.new('RGB', (columns * cell_w + 40, rows * cell_h + 112), '#eff2f2')
    draw = ImageDraw.Draw(canvas)
    draw.text((22, 16), title, fill='#15362e', font=ImageFont.truetype(FONT, 26))
    draw.text((22, 55), subtitle, fill='#526360', font=ImageFont.truetype(FONT, 16))
    for i, (path, label) in enumerate(zip(paths, labels)):
        x, y = 20 + (i % columns) * cell_w, 94 + (i // columns) * cell_h
        draw.rounded_rectangle((x + 4, y, x + 126, y + 156), 8, fill='white')
        im = Image.open(path).convert('RGBA')
        im.thumbnail((106, 142), Image.Resampling.LANCZOS)
        canvas.paste(im, (x + (cell_w - im.width) // 2, y + (150 - im.height) // 2), im)
        draw.text((x + cell_w // 2, y + 159), label, anchor='mt', fill='#253e36', font=ImageFont.truetype(FONT, 16))
    canvas.save(out)

gnome_rows = []
for style in ('smooth', 'postmodern'):
    order = gnome_order if style == 'smooth' else gnome_order[:33] + ['spring','summer','autumn','winter','white','plum','orchid','chrysanthemum','bamboo','blank']
    if style == 'smooth':
        atlas = Image.open(GNOME / 'original/smooth.png').convert('RGBA')
    else:
        atlas = Image.open(BytesIO(resvg_py.svg_to_bytes(
            svg_path=str(GNOME / 'original/postmodern.svg'), width=11008))).convert('RGBA')
    assert atlas.width % 43 == 0 and atlas.height % 2 == 0, atlas.size
    w, h = atlas.width // 43, atlas.height // 2
    output = GNOME / style / 'png'
    output.mkdir(parents=True, exist_ok=True)
    for col, key in enumerate(order[:42]):
        name = dict(tiles)[key]
        filename = f'{key}.png'
        atlas.crop((col * w, 0, (col + 1) * w, h)).save(output / filename)
        gnome_rows.append(dict(style=style, tile_id=key, name_zh=name,
                               source_column_zero_based=col, source_row_zero_based=0,
                               x=col*w, y=0, width=w, height=h,
                               file=f'{style}/png/{filename}',
                               note='Postmodern花季牌按一至四编号映射槽位，图案不表示单独的植物或季节。' if style == 'postmodern' and col in list(range(33,37))+list(range(38,42)) else ''))
    bonus_labels = dict(zip(['spring','summer','autumn','winter','plum','orchid','chrysanthemum','bamboo'], ['季1','季2','季3','季4','花1','花2','花3','花4']))
    contact_sheet([output / f'{k}.png' for k, _ in tiles], [bonus_labels.get(k,n) if style == 'postmodern' else n for k,n in tiles],
                  f'GNOME {style.title()} · 42 种牌面',
                  'GPL-2.0-or-later · Mahjongg Contributors · 原始图集普通状态；单张导出',
                  GNOME / style / 'preview.png')
write_csv(GNOME / 'tile-map.csv', gnome_rows)

hk_order = ['white', 'green', 'red', 'east', 'south', 'west', 'north']
hk_order += [f'{i}m' for i in range(1, 10)] + [f'{i}p' for i in range(1, 10)] + [f'{i}s' for i in range(1, 10)]
hk_order += ['spring', 'summer', 'autumn', 'winter', 'plum', 'orchid', 'chrysanthemum', 'bamboo']
hk_files = sorted((ROOT / '01_HongKong_CC0/hongkong/png').glob('*.png'))
assert len(hk_files) == 42
hk = dict(zip(hk_order, hk_files))
xh = {f'{i}{s}': f'{prefix}{i}' for s, prefix in [('m','Man'),('p','Pin'),('s','Sou')] for i in range(1,10)}
xh.update(dict(zip(['east','south','west','north','red','green','white'], ['Ton','Nan','Shaa','Pei','Chun','Hatsu','Haku'])))
xh.update(dict(zip(['spring','summer','autumn','winter'], [f'Season{i}' for i in range(1,5)])))
xh.update(dict(zip(['plum','orchid','chrysanthemum','bamboo'], [f'Flower{i}' for i in range(1,5)])))
sets = [
    ('香港风格 · CC0', 'samoheen · 42 SVG + 42 PNG；推荐先看这套', hk),
    ('Xhokir Regular · CC BY 4.0', 'FluffyStuff / xhokir · 偏日麻；花季牌仅编号；白板为空白图层',
     {key: ROOT / f'02_Xhokir_CC_BY4/Export/Regular/{value}.png' for key,value in xh.items()}),
    ('GNOME Smooth · GPL-2.0-or-later', 'Jaye Evins / Mahjongg Contributors · 中文花季牌；另存 PNG / XCF 原始素材',
     {key: GNOME / f'smooth/png/{key}.png' for key,_ in tiles}),
    ('GNOME Postmodern · GPL-2.0-or-later', 'Mahjongg Contributors · 矢量图集，已拆成 42 张 PNG',
     {key: GNOME / f'postmodern/png/{key}.png' for key,_ in tiles}),
]
map_rows = []
for key, name in tiles:
    row = dict(tile_id=key, name_zh=name)
    for column, (_, _, paths) in zip(['hongkong_png','xhokir_regular_png','gnome_smooth_png','gnome_postmodern_png'], sets):
        assert paths[key].is_file(), paths[key]
        row[column] = paths[key].relative_to(ROOT).as_posix()
    row['note'] = 'Xhokir及Postmodern花季牌按编号槽位对应，名称不代表图案已核实为该植物/季节。' if key in hk_order[-8:] else ''
    map_rows.append(row)
write_csv(ROOT / 'tile-map.csv', map_rows)

samples = ['1m','9m','1p','1s','east','red','white','spring','plum']
overview = Image.new('RGB', (1228, 1176), '#eff2f2')
for i, (title, subtitle, paths) in enumerate(sets):
    preview_file = (ROOT / ['01_HongKong_CC0','02_Xhokir_CC_BY4','03_GNOME/smooth','03_GNOME/postmodern'][i]) / 'sample-preview.png'
    contact_sheet([paths[k] for k in samples], [dict(tiles)[k] if not (i in (1,3) and k in ('spring','plum')) else ('季1' if k == 'spring' else '花1') for k in samples], title, subtitle, preview_file)
    overview.paste(Image.open(preview_file), (0, i * 294))
overview.save(ROOT / 'preview.png')

verified = {'png':0, 'svg':0, 'files':0, 'bytes':0}
hashes = []
import xml.etree.ElementTree as ET
for p in sorted(ROOT.rglob('*')):
    if not p.is_file() or p.name in ('SHA256SUMS.txt','verification.json'):
        continue
    raw = p.read_bytes()
    verified['files'] += 1
    verified['bytes'] += len(raw)
    hashes.append(f'{hashlib.sha256(raw).hexdigest()}  {p.relative_to(ROOT).as_posix()}')
    if p.suffix.lower() == '.png':
        with Image.open(p) as im:
            im.verify()
        verified['png'] += 1
    elif p.suffix.lower() == '.svg':
        ET.fromstring(raw)
        verified['svg'] += 1
(ROOT / 'SHA256SUMS.txt').write_text('\n'.join(hashes)+'\n', encoding='utf-8')
(ROOT / 'verification.json').write_text(json.dumps(verified, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print(json.dumps(verified))
