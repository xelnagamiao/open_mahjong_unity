# 牌面资源

`packs/` 是可用牌面，`sources/` 保存原画，`surfaces/` 单独管理牌体背景与牌背。

| 目录 | 用途 |
| --- | --- |
| `packs/official/hand`、`table` | 雪枫透明牌面，各 46 张；手牌 272 × 389，桌面 400 × 532，已逐牌调整图案大小与位置 |
| `packs/fluffy/hand`、`table` | Fluffy 牌面，各 46 张；花牌 51–58 与雪枫对应层的 PNG 完全相同 |
| `packs/hkmahjong/hand`、`table` | HK 原有牌面，各 42 张；空白和赤五沿用游戏的雪枫回退 |
| `sources/official/hand-framed` | 雪枫原始有框 PNG，保留原分辨率和全部像素，用于重新生成透明图案 |
| `sources/fluffy` | Fluffy 原始 SVG、PNG 和原说明；生成桌面牌面优先使用 SVG |
| `sources/hkmahjong` | HK 原始 PNG |
| `surfaces/backgrounds/hand-default.png` | 竖向默认牌体底图 |
| `surfaces/backgrounds/hand-horizontal.png` | 横向牌体底图；原文件 `1.png`，不是牌背 |
| `surfaces/backs/hand-default.png` | 深蓝牌背；原文件 `0.png` |

上传牌面包时，将某个 `packs/<包名>/` 内的 `hand` 和 `table` 一起压入 ZIP。
不要把 `sources` 或 `surfaces` 混入牌面 ZIP；背景和牌背在各自设置页管理。
官方 `table` 导出不含 Unity 图集专用的透明占位 `0.png`，避免被识别为未知牌号。

透明图案没有固定底色。雪枫原底色 `(245,246,247)` 与筒饼内部真正的白色
笔画、白色填充不同；透明提取保留白色图案，只去除牌体和实际内孔的底色。
半透明边缘经过原底色去混色处理，换深色背景不会带出白边。透明提取在缩放前
与原图逐像素核对；旧文件彼此略有不同的牌体外轮廓统一使用默认底。

桌面牌面在已接受的 18% 目标放大基线上，按牌号做等比大小与位置微调，保留
至少 4 像素安全边距。手牌单独调整，并检查图案没有侵入原牌体边框。二索上移，
字牌按字形校正重心，万子统一基准以保持“萬”字对齐，不进行左右拉伸。
参数保存在 [`snow_artwork_layout.json`](../../tools/tilepack/snow_artwork_layout.json)：
`hand_scale`、`table_scale` 分别控制两层倍率；`offset` 使用原图像素，负 Y 向上。
每次都从保留的原画一次采样生成，重复运行不会累计放大或损失画质。
原画不因重新生成而被覆盖。所有源文件整理前已按 SHA-256 校验备份，当前
`sources` 是后续制作应使用的原画位置。

重新生成并同步这些可用包：

```powershell
python tools/tilepack/rebuild_table_faces.py --export-root other/tiles
```

运行位置请使用仓库根目录。依赖和转换细节见
[`tools/tilepack/README.md`](../../tools/tilepack/README.md)。
逐图来源、输出 SHA-256、放大比例和透明提取验证可在运行生成器时通过
`--report <本地报告路径>` 生成；报告属于本机产物，不放入共享素材目录。
未启用的其他历史牌面来源已移入 `.om_workspace/cleanup-quarantine-20260912/small/tiles_unused`，不参与此生成流程；仓库内只保留当前可用包、原画源文件和牌体表面资源。
