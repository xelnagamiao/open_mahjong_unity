# Xhokir 花季牌扩展（CC BY 4.0）

检索与下载日期：2026-09-09。

## 来源与固定版本

- 仓库账号：xhokir；由 [FluffyStuff/riichi-mahjong-tiles](https://github.com/FluffyStuff/riichi-mahjong-tiles) 派生。
- [本套项目主页](https://github.com/xhokir/riichi-mahjong-tiles)。
- 固定提交：`19d72ff5cf9ad9c401188734f80cef7e6c8c6140`（上游提交日期：2022-09-11）。
- [固定版本压缩包](https://codeload.github.com/xhokir/riichi-mahjong-tiles/zip/19d72ff5cf9ad9c401188734f80cef7e6c8c6140)，已保存为 `upstream.zip`。
- 压缩包 SHA-256：`79F2393B841393A49F6CEC1A53D1691281DEE77FC473B4E07F560FEC8AEC6D1B`。
- [固定版本的原始授权声明](https://github.com/xhokir/riichi-mahjong-tiles/blob/19d72ff5cf9ad9c401188734f80cef7e6c8c6140/LICENSE.md)。
- [CC BY 4.0 原文](https://creativecommons.org/licenses/by/4.0/legalcode)。

本次下载的 fork 的 `LICENSE.md` 和 `README.md` 均明确使用 Creative Commons Attribution 4.0 International（CC BY 4.0）。使用时应标明来源 / 作者、附许可证链接，并注明是否修改；允许商业使用。不能因为 FluffyStuff 主仓库的其他版本采用 Public Domain，就把本目录及新增花季牌也标为 CC0。

署名记录可使用：`Mahjong tile artwork: FluffyStuff / xhokir, https://github.com/xhokir/riichi-mahjong-tiles, CC BY 4.0, https://creativecommons.org/licenses/by/4.0/. Artwork unchanged.` 后续如修改图案，应把最后一句改为实际修改说明。

## 文件与覆盖范围

| 目录 | SVG 数 | PNG 数 | 内容 |
| --- | ---: | ---: | --- |
| `Regular/` | 48 | 0 | 普通风格牌面源文件 |
| `Export/Regular/` | 0 | 48 | 普通风格 PNG |
| `Black/` | 40 | 0 | 黑色风格牌面源文件 |
| `Export/Black/` | 0 | 40 | 黑色风格 PNG |

另保留上游 `ExampleRegular.png`、`ExampleBlack.png` 两张预览，以及 `README.md`、`LICENSE.md`、固定提交记录。合计 88 个 SVG、88 张独立 PNG、2 张预览 PNG。

`Regular` 包含万、筒、条各 1 至 9，东南西北中发白等 34 种普通牌，`Flower1` 至 `Flower4`、`Season1` 至 `Season4` 共 8 种花季牌，以及 3 张红五和 `Back`、`Blank`、`Front` 3 张辅助图，共 48 个文件。红五是上游日麻附加素材，组建国标牌集时无需使用。

`Black` 只有 34 种普通牌、3 张红五及 3 张辅助图；**黑色风格没有 8 张花季牌**。普通风格的花季牌 SVG 位于 `Regular/Flower1.svg` 至 `Flower4.svg`、`Regular/Season1.svg` 至 `Season4.svg`，对应 PNG 位于 `Export/Regular/`。

## 国标麻将适用性与图案说明

普通风格的牌种覆盖可组建 144 张麻将牌，但画法延续日麻风格。本套最值得参考的是新增的 8 张花季牌。花季牌均为植物图案，**没有直接印出「春夏秋冬梅兰竹菊」中文名称或数字标签**；上游文件仅以 Flower / Season 加编号区分，使用时应保留这一对应关系，不能把未标注的文字当作原图内容。

白牌 `Haku` 仍为空白透明图，没有中国风格白板边框；显示时需要牌身或底板。其他牌面也是透明背景图案，可搭配 `Front` / `Back` 或自行创建的牌身。上游两张示例图未展示新增的全部花季牌，检查花季牌请直接查看对应独立文件。

## 文件验证与尺寸

- 88 张独立牌面 / 辅助 PNG 以及 2 张预览 PNG 全部通过本地图片解码检查。
- 88 个 SVG 全部通过 XML 解析检查；解析时禁止 DTD 和外部实体。
- 8 张花季牌的 SVG 和 PNG 路径均逐一检查存在，共 16 个文件。
- 全部独立 PNG 为 600 × 800 像素、含透明通道。
- 全部 SVG 的 `viewBox` 为 `0 0 300 400`；其中 8 张花季 SVG 的宽高单位写作 `300mm` / `400mm`，其余为 `300` / `400`。直接栅格化或导入 SVG 时应按 viewBox 比例并显式指定输出像素尺寸。

仅下载和提取素材、许可证、README 和预览，保留上游目录结构；没有修改图案，也没有执行上游代码。
