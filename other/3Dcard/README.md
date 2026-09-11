# 当前麻将模型源

唯一现用可编辑模型是 [3DCardNew2026.8.5.blend](D:/open_mahjong_unity/other/3Dcard/3DCardNew2026.8.5.blend)，规范导出是 [3DCardNew2026.8.5.fbx](D:/open_mahjong_unity/other/3Dcard/3DCardNew2026.8.5.fbx)。文件名保留以兼容引用。主工程直接使用同内容的 [FBX](D:/open_mahjong_unity/open_mahjong_unity/Assets/Resources/3D/3DCardNew2026.8.5.fbx)，没有运行时程序网格替换。

当前为我方原创分面倒角模型：**360 三角形，Unity 导入 378 顶点，四套 UV**。宽∶高∶厚为 **1∶1.33∶0.65**，Unity 局部尺寸为 `0.02 × 0.0266 × 0.013`，预制体缩放 600 后为 `12 × 15.96 × 7.8`。模型没有顶面人工灰阶，也没有移植竞品几何或纹理。

2026-09-11 的邻牌描边调整使用当时的旧尺寸；随后本次比例调整更新 `.blend`、规范 FBX 和主工程 FBX，并按尺寸变换更新受光法线与第四 UV 描边法线。前三套 UV、顶点色及拓扑不变。轮廓补线已从新 FBX 重新烘焙，仍为独立共享 Mesh，不需要 UV5–8。

| Blender UV 名称 | Unity Mesh 属性 | Shader 通道 | 用途 |
| --- | --- | --- | --- |
| TileFace | uv | TEXCOORD0 | 牌面与图集接口 |
| TileBack | uv2 | TEXCOORD1 | 牌背 |
| TileEdge | uv3 | TEXCOORD2 | 侧面贴图 |
| OutlineSmoothNormal | uv4 | TEXCOORD3 | 描边连续法线的八面体 XY 编码 |

上表使用 `Mesh.uv / uv2 / uv3 / uv4` 属性名称；从零计数的 UV 通道编号依次为 0、1、2、3。第四通道即 `GetUVs(3)`，不要与 `Mesh.uv3` 混淆。

受光使用自定义分面法线，描边使用第四 UV 的连续法线。第四 UV 保存的是 **Unity 对象空间单位法线的八面体编码，值域 [0,1]**，不是普通贴图 UV。同一空间点因 UV 或硬法线拆分时，其第四 UV 应一致。修改拓扑或形体时应同时维护这些数据；导出脚本只保留现有数据，不会自动修复失效的描边法线。

顶点颜色仍是材质分区：红=正面、绿=背面、蓝=正面侧体、黑=背侧体；Alpha 全为 1，以线性颜色导出。不要把这些权重当作可见颜色、AO 或顶面暗色。

当前沿总厚度的分区为 **白体 55%、背侧 45%**（含各自倒角）；背侧由 37.5% 加厚至 45%。直侧分界从 Unity Z `0.001625` 移到 `0.00065`，外形、倒角、总尺寸及贴桌高度不变。TileEdge 分界 V 从 `0.625` 同步到 `0.55`，维持沿厚度连续映射；TileFace、TileBack、OutlineSmoothNormal、顶点色和存储自定义法线保持。

日常修改流程：

1. 编辑并保存规范 `.blend`，保持 `Cube` 对象和网格、`Material` 单材质槽、零位移/旋转、单位缩放，以及四套 UV 和自定义法线。
2. 使用 [export_edited_blend.py](D:/open_mahjong_unity/other/3Dcard/Tools/export_edited_blend.py) 导出。它依赖 Blender 内置 `bpy` 与 Python 标准库，不依赖已清理的实验目录；不会重新生成模型，也不会保存或改写 `.blend`。
3. 将验证后的 FBX 同步到规范导出与主工程，**保留主工程原 `.meta`**。在 Unity 核对尺寸、单材质、四 UV、牌面方向和近景描边，再提交模型变更。
4. Unity 重导入规范 FBX 后，[TileOutlineRibbonBaker.cs](D:/open_mahjong_unity/open_mahjong_unity/Assets/Scripts/Editor/TileOutlineRibbonBaker.cs) 会按源 GUID 和依赖哈希更新已有的 [TileOutlineRibbon.asset](D:/open_mahjong_unity/open_mahjong_unity/Assets/Resources/3D/TileOutlineRibbon.asset)。也可手动运行 **Tools → Mahjong → Tile Outline → Rebuild Ribbon Mesh**。更新采用保留对象的方式，不删除重建资源 GUID；不要手工修改派生网格来代替源模型修改。

```powershell
& 'D:/SteamLibrary/steamapps/common/Blender/blender.exe' --background --disable-autoexec 'D:/open_mahjong_unity/other/3Dcard/3DCardNew2026.8.5.blend' --python 'D:/open_mahjong_unity/other/3Dcard/Tools/export_edited_blend.py' -- --output 'D:/open_mahjong_unity/other/3Dcard/3DCardNew2026.8.5.fbx'
```

更换电脑时只需替换 Blender 可执行文件路径。导出脚本可用 `--output` 指向临时 FBX，先在 Unity 验证后再替换规范文件。

补线烘焙要求牌体在空间位置焊接后闭合、朝外绕序一致、形状为凸体，而且局部原点严格位于内部；这是补线 Shader 判断屏幕外侧方向的前提。Baker 会在写入派生资源前检查这些条件。后续若把牌体改成凹形或移到原点之外，应先调整渲染方案，不应跳过检查。原四 UV 与自定义法线仍由 Blender 源模型维护；Baker 只读取它们所属的牌体，不会重建或修复这些原始数据。

当前源模型的 540 条空间边中，220 条为共面三角形分割线，补线只使用其余 **320 条折面边**，派生出 **1280 顶点、640 三角形**。预制体通过独立子 Renderer 使用共享资源，运行时按两邻面的朝向选择可见外轮廓。原始顶点与 16 位索引数据约 74 KiB，由所有牌共享；这不包含 Unity 资源及管理开销。补线复用现有描边 RenderPass，不投射阴影、不增加全屏 RenderTexture，但会增加 Renderer 提交、顶点处理和像素绘制成本。

当前 FBX SHA-256 为 `1d4977529d1a2bad8d13b440d6409205026c978e8845e9e963b06b2d9606e499`。规范导出与主工程一致，原 GUID `337ea70c01567454dae53330eaef53c1`、Mesh localFileID `-5495902117074765545` 保留。[当前尺寸与自动贴桌验证](D:/open_mahjong_unity/other/TileStyleLab/ModelFit_20260911/README.md)包含尺寸调整时的实际导入和动画检查；[背侧比例与上传适配](D:/open_mahjong_unity/other/TileStyleLab/BackBandUpload_20260911/README.md)记录随后本次分区移动。`Final/canonical-model-validation.json` 是旧尺寸历史记录。

仅调整整体比例时可用 [resize_tile_proportions.py](D:/open_mahjong_unity/other/3Dcard/Tools/resize_tile_proportions.py)，参数为 `--height-ratio`、`--thickness-ratio`、`--output-blend`、`--report`。它按绝对比例输出独立候选并维护两类法线，不会重复乘比例或覆盖输入；再用原导出工具导出、验证。直接在 Blender 改拓扑或倒角时，仍需自行维护 UV 与法线。

仅调整白体/背侧分区时可用 [set_tile_back_band_ratio.py](D:/open_mahjong_unity/other/3Dcard/Tools/set_tile_back_band_ratio.py)，在 Blender 打开规范源后运行，参数为 `--back-ratio 0.45 --output-blend <候选.blend> --report <报告.json>`。它按绝对背侧比例移动已有直侧分界，并同步侧面 UV，重复执行不叠加变化；不会覆盖输入，也不会增加面数。候选仍需用上述导出工具导出并在 Unity 验证。

`old/`、`3DCardNew.blend` 和 `.blend1` 是既有旧源或 Blender 备份，保留供追溯，不能替代上述现用模型。旧候选、一次性建模生成器与实验工程已不作为模型维护入口。

[当前材质与描边说明](D:/open_mahjong_unity/other/TileStyleLab/README.md)：仅保留 2.4px 漫画外壳，另加约 2.13px 外轮廓补线。补线比例在 Renderer Feature 的 `Settings.silhouetteWidthRatio` 中调整，默认 8/9、范围 0–1，不需改模型或重新烘焙。当前牌材质 `_TileWhiteCompression` 为 0.10；0.28 属于 Shader 默认值及旧验收设置，后续数值以主工程材质资产为准。

[与雀魂默认桌面模型的比例分析](D:/open_mahjong_unity/other/TileStyleLab/Proportions_20260911/analysis.md)保留改型前 1∶1.4∶0.6 的测量。此后已应用 1∶1.33∶0.65，背侧分区调整至 45%；倒角重塑尚未应用。

[本次邻牌描边验证入口](D:/open_mahjong_unity/other/TileStyleLab/NeighborOutline_20260911/README.md)记录新方案的实际验证结论；`Final/` 保留此前牌体与原外壳方案的历史基线。
