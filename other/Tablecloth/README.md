# 桌布与缝线共享素材

这里是开源项目使用者可以直接取得、修改和继续扩展的最小共享素材集。只保留当前运行时素材和真实存在的 PSD；历史示例工程、日志、缓存、JSON、SVG、验证输出和早期 PNG 已移到本机 `.om_workspace/cleanup-quarantine-20260912/`。

## 当前运行时素材

- `Tablecloths/Runtime/`：13 张无缝桌布。
- `Edges/Runtime/`：12 张边框。
- `Seams/Runtime/`：7 张运行时缝线。

## 运行时压缩方案

- 运行时素材最长边统一不超过 2048px；原本为 1720px 或 2000px 的素材不放大。
- 含透明通道的桌布、边框和缝线保留为 PNG，避免 JPEG 破坏透明边缘。
- 完全不透明的三张旧桌布使用质量 92 的 JPEG：`Tablecloth_blue.jpg`、`Tablecloth_green.jpg`、`Tablecloth_green2.jpg`。
- Unity 内仍通过不带扩展名的 `Resources.Load` 加载，桌布名称和目录语义不变；编辑器验证脚本同时支持 PNG/JPEG。
- PSD 和真实源文件不参与压缩，修改时优先从 `PSD/OriginalSources/` 或对应运行时素材开始。

## 真实 PSD

- `PSD/OriginalSources/OfficialEarliest/`：最早官方桌布 PSD。
- `PSD/EarlySources/`：早期 `plain_work.psd` 与 `blue_test.psd`。
- `PSD/OriginalSources/OfficialNewSeams/`：官方新缝线 PSD。
- `PSD/OriginalSources/SeamSources/`：三款原始缝线和金属缝线 PSD。

当前真实 PSD 共 9 个。检查过所有 PSD 的图层记录，没有找到一个同时包含 13 张桌布、12 张边框和 7 张缝线的总合 PSD；因此没有伪造或重新生成扁平 PSD。

最早官方桌布 PSD 为 `PSD/OriginalSources/OfficialEarliest/Tablecloth_Earliest_Original.psd`。它与历史来源 `Tablecloth.psd`、`plain_work.psd` 内容完全一致：

`SHA-256 932B2BEF0CFA5FC1C6E4129357A6DFCC22280A34008A3B62EA91E5B3499A5865`

以后修改桌布或制作新缝线时，以这里的运行时 PNG/JPEG 和真实源 PSD 为基础；不要从游戏截图或烘焙后的组合图反向编辑。
