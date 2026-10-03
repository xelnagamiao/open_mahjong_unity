# 卡牌贴图

统一在此管理手牌、3D 牌面及牌体贴图；模型和材质仍在 Resources/3D、Resources/Materials/Tiles。

- `Faces/{official,fluffy,hkmahjong}/{hand,table}`：标准麻将牌面花纹。
- `Faces/hongque/{hand,table}`：虹雀手牌 / 3D 牌面，各 126 张。
- `Faces/official/TableAtlas.spriteatlasv2`：官方 3D 牌面图集。
- `Surfaces/backgrounds`：手牌竖向、横向底图。
- `Surfaces/backs`：手牌牌背。

`hand-default` / `hand-horizontal` 为原版，保持不变。`hand-indigo`、`hand-amber`、`hand-jade` 为 272×424 的原创柔和倒角版，正反面同尺寸；可编辑 SVG 在工作区 `output/hand-tile-skins-v4`。设计依据和测量误差见该目录的对比报告。

上传的 2D 手牌背景与牌背由 `HandSurfaceLibrary` 管理：本地 `HandSurfaces/v1/*.hsi` 或 IndexedDB `hand-surface/v1/{id}`。一个记录包含自定义名称和图片；恢复原版只切换选项，不删除图库。旧单张上传在首次读取时迁移，旧文件保留。

标准包 `2.png` 是纯白白板使用的空白前景，叠在牌体底色上；`46.png` 保留该包原有的白板花纹。香港麻将保留原来的框式白板，同时提供 hand/table 两份空白 `2.png`。纯白模式不会强制覆盖用户自定义的牌面底色。

上传的 3D 牌背、背景和自定义牌面属于用户存档，不写入 Resources。
运行时资源路径集中在 `TilePackIds`；原画、牌包与布局参数见 [共享牌面素材](../../../../../other/tiles/README.md)。加工工具与构建报告保存在本机工作区。
移动资产时必须一起保留同名 `.meta`，以维持场景和图集的 GUID 引用。
