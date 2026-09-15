# 国标麻将开源牌面素材

整理日期：2026-09-09。这里收集了 **3 个来源、4 种包含 8 花牌的完整风格**，另附 1 种缺花牌的黑色风格。素材单独存放于 `other/GuobiaoMahjongTiles/`，尚未接入 Unity 项目。

建议先看 **香港风格 CC0**：中文传统画法、带边框白板，且原始 SVG 和高清透明 PNG 均完整。若希望花季牌直接带中文名称，可看 GNOME Smooth。

## 快速浏览

- `preview.png`：四种完整风格的对比图，每行 9 张代表牌。
- `tile-map.csv`：各套牌面路径与牌种 / 槽位对照表。
- 每个来源目录中的 `SOURCE.md`：上游链接、固定提交、授权范围和图案说明。

| 风格 | 主要文件目录 | 授权 | 选材说明 |
| --- | --- | --- | --- |
| 香港风格 | `01_HongKong_CC0/hongkong/png/`、`hongkong/svg/` | CC0 / Public Domain | 42 种；繁体字、蓝框白板；8 花季牌用植物和数字区分，没有直接标中文花名 |
| Xhokir Regular | `02_Xhokir_CC_BY4/Export/Regular/`、`Regular/` | CC BY 4.0 | 42 种，另有红五和辅助图；偏日麻，白板为空白，花季牌仅用编号区分 |
| GNOME Smooth | `03_GNOME/smooth/png/` | GPL-2.0-or-later | 42 张单牌；花季牌印有梅兰竹菊、春夏秋冬中文；一条画作单竹节 |
| GNOME Postmodern | `03_GNOME/postmodern/png/` | GPL-2.0-or-later | 42 张单牌及原始 SVG 图集；花季牌为两组带编号图案，不印具体花季名；一条画作单竹节 |

`02_Xhokir_CC_BY4/Black/` 与 `Export/Black/` 是额外的黑色版本，缺少 8 张花季牌，不计入上述四种完整风格。

## 文件怎么用

42 种不同牌面由 **34 种普通牌 + 8 种花季牌**组成。万、筒、条各 9 种，东南西北中发白共 7 种；每种普通牌复用 4 次，加上各使用 1 次的 8 花牌，即 `34 × 4 + 8 = 144` 张。文件数量不会按实体牌的重复张数扩充。

香港与 Xhokir 的独立 PNG 主要是透明底牌面图案，适合贴到牌身或 UI 底板；GNOME 的单牌从上游普通状态图集导出。各套尺寸与画法不同，路径和顺序以 `tile-map.csv` 为准。Xhokir 与 Postmodern 的花季牌按槽位编号使用，不能把编号直接当成已核实的梅兰竹菊、春夏秋冬文字对应关系。

GNOME 目录内另有 `smooth/preview.png`、`postmodern/preview.png` 两套整套预览，以及 `tile-map.csv`、原始图集和 Smooth 作者提供的 PNG、XCF、生成脚本。来源压缩包、许可证及固定版本记录均已保留。

## 授权说明

每个来源沿用自己的许可证，没有统一改为一种授权。CC0 套装保留原 Public Domain 声明；Xhokir 套装保留 CC BY 4.0 的署名与修改说明要求；GNOME 套装保留 GPL-2.0-or-later、版权声明和源素材。复制或发布选中的素材时，请一起保留对应授权文件，并按该许可证处理。具体原文见各目录的 `SOURCE.md` 和许可证文件。
