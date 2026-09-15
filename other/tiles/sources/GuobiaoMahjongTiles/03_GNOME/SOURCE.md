# GNOME Mahjongg：Smooth 与 Postmodern

整理日期：2026-09-09。本目录收集两个完整风格，每种均包含 34 种普通牌和 8 种花季牌，共 42 种不同牌面。

## 来源与固定版本

- [GNOME 官方项目](https://gitlab.gnome.org/GNOME/gnome-mahjongg)。
- [GitHub 镜像](https://github.com/GNOME/gnome-mahjongg)。
- GNOME 固定提交：`9b79392aa447cc97fe01bd88659a5f8693cb4e06`，记录于 `UPSTREAM_COMMIT.txt`。
- [Postmodern 原始 SVG](https://github.com/GNOME/gnome-mahjongg/blob/9b79392aa447cc97fe01bd88659a5f8693cb4e06/data/themes/postmodern.svg) 与 [对应授权声明](https://github.com/GNOME/gnome-mahjongg/blob/9b79392aa447cc97fe01bd88659a5f8693cb4e06/data/themes/postmodern.svg.license)。
- [Smooth 原始 PNG](https://github.com/GNOME/gnome-mahjongg/blob/9b79392aa447cc97fe01bd88659a5f8693cb4e06/data/themes/smooth.png) 与 [对应授权声明](https://github.com/GNOME/gnome-mahjongg/blob/9b79392aa447cc97fe01bd88659a5f8693cb4e06/data/themes/smooth.png.license)。

Smooth 的 GNOME 授权旁注同时指向原作者 [Jaye Evins 的 smooth-tileset](https://github.com/j-evins/smooth-tileset)。本目录还保存该作者仓库的固定提交 `82b0d6704de67876863a721de6b21c27dc549573`，记录于 `SMOOTH_SOURCE_COMMIT.txt`；[固定版本压缩包](https://codeload.github.com/j-evins/smooth-tileset/zip/82b0d6704de67876863a721de6b21c27dc549573) 保存为 `smooth-source.zip`，解压内容位于 `smooth-source/`。

## 许可证与版权记录

GNOME 对两个原始素材文件均明确标注：

```text
SPDX-FileCopyrightText: 2004-2025 Mahjongg Contributors
SPDX-License-Identifier: GPL-2.0-or-later
```

两个原始 `.license` 文件与 `GPL-2.0-or-later.txt` 均保留在本目录。Smooth 作者的 [固定版本 README](https://github.com/j-evins/smooth-tileset/blob/82b0d6704de67876863a721de6b21c27dc549573/README.md) 声明版权归 Jaye Evins（2004–2014），采用 GPL 第 2 版或后续版本；其原始 `LICENSE` 也保存在 `smooth-source/`。

本次导出的单牌 PNG 继续沿用 **GPL-2.0-or-later**。发布或修改这些素材时，应保留对应版权与许可证，并按 GPL 对源素材及修改版本履行相应要求。它们没有被改为 CC0 或 CC BY。许可证原文见 [GNU GPL v2](https://www.gnu.org/licenses/old-licenses/gpl-2.0.html) 及随附本地文件。

## 文件结构

| 路径 | 内容 |
| --- | --- |
| `original/postmodern.svg` | GNOME 原始矢量图集 |
| `original/smooth.png` | GNOME 原始位图图集 |
| `original/*.license` | 两个原始图集的逐文件许可证声明 |
| `postmodern/png/` | 42 张普通状态单牌 PNG，256 × 352 |
| `smooth/png/` | 42 张普通状态单牌 PNG，250 × 344 |
| `postmodern/preview.png` | Postmodern 的 42 种单牌整套预览 |
| `smooth/preview.png` | Smooth 的 42 种单牌整套预览 |
| `tile-map.csv` | 两套共 84 行牌名、文件路径、原始图集坐标对照 |
| `smooth-source/face-images/` | 作者提供的独立牌面 PNG 和 XCF 源素材 |
| `smooth-source/samples/` | 作者提供的渲染样例 |
| `smooth-source/build-smooth.pl`、`Makefile` | 作者的原始生成脚本及构建文件，作为源文件保留 |

单牌 PNG 从 GNOME 图集的普通状态导出：Smooth 裁切原始 PNG；Postmodern 将原始 SVG 栅格化后裁切。为便于使用，单牌采用牌种名称命名，并生成对照表及预览；没有重新绘制牌面。作者仓库作为附加源素材保留，导出的 Smooth 单牌直接来自 `original/smooth.png`。

## 国标麻将选材说明

两个风格的牌种均可组成国标麻将使用的 144 张牌：34 种普通牌各 4 张，8 种花季牌各 1 张。它们来自 GNOME 的麻将消除游戏，画法并非官方国标指定样式。

Smooth 花季牌直接显示梅、兰、竹、菊及春、夏、秋、冬的中文名称，便于识别。Postmodern 的花季牌为两组图案配「一二三四」编号，不印具体中文花名或季节名；对照表以季 1–4、花 1–4 的槽位区分，不把它们直接认定为某个已核实的具体花季名称。Smooth 与 Postmodern 的一条都画作单竹节，与常见的鸟形一条不同。

原始图集中的风牌顺序不是常见的东南西北顺序，两套花季牌的列顺序也不同：以 0 为起点，Smooth 的 33–36 列为兰、菊、梅、竹，37 列为白，38–41 列为春、夏、秋、冬；Postmodern 的 33–36 列为蓝色季 1–4，37 列为白，38–41 列为金色花 1–4。切割位置、牌名与导出路径以本目录 `tile-map.csv` 为准，接入时不要按另一套的连续位置套用对应关系。当前只整理素材、源文件和预览，尚未接入 Unity 项目。
