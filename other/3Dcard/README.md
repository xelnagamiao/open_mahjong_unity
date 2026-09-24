# 麻将牌模型源文件

规范源为 [3DCardNew2026.8.5.blend](3DCardNew2026.8.5.blend)，规范导出为
[3DCardNew2026.8.5.fbx](3DCardNew2026.8.5.fbx)。主工程使用对应的
[FBX](../../open_mahjong_unity/Assets/Resources/3D/3DCardNew2026.8.5.fbx)。

模型采用分面倒角：360 三角形，Unity 导入 378 顶点，四套 UV。
宽∶高∶厚为 1∶1.33∶0.65，Unity 局部尺寸为 `0.02 × 0.0266 × 0.013`。
白体与背侧沿厚度的分区为 55%∶45%。

| Blender UV | Unity 属性 | 用途 |
| --- | --- | --- |
| TileFace | uv / TEXCOORD0 | 牌面与图集 |
| TileBack | uv2 / TEXCOORD1 | 牌背 |
| TileEdge | uv3 / TEXCOORD2 | 侧面贴图 |
| OutlineSmoothNormal | uv4 / TEXCOORD3 | 描边连续法线的八面体 XY 编码 |

第四 UV 保存 Unity 对象空间单位法线编码，值域为 [0,1]，不是普通贴图坐标。
顶点颜色用于材质分区：红为正面、绿为背面、蓝为正面侧体、黑为背侧体，Alpha 为 1。

制作时编辑规范 `.blend`，保留 `Cube` 对象、`Material` 单材质槽、单位缩放、
四套 UV、顶点颜色和自定义法线。使用 Blender 导出候选 FBX，与规范导出核对尺寸、
轴向、材质和法线后再同步到主工程；保留主工程原 `.meta`，不要重新生成 GUID。

主工程的 `TileOutlineRibbonBaker` 会按源 GUID 与依赖哈希更新补线网格；也可在
Unity 使用 `Tools > Mahjong > Tile Outline > Rebuild Ribbon Mesh`。
补线要求牌体闭合、朝外绕序一致、形状为凸体且局部原点在内部。Baker 不会修复源模型
失效的 UV 或法线，修改拓扑后仍需在 Blender 维护它们。

`old/` 保留人工保存的旧模型源文件供追溯。自动生成的 `.blend1` 备份、一次性加工脚本
及本机验证记录不属于共享素材，已移出本目录。材质说明见 [ContactShadows.md](ContactShadows.md)。
