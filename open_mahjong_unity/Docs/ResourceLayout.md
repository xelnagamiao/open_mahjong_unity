# 桌面与说明资源位置

运行时资源按类型统一在 `Assets/Resources`，移动已有资源时保留 `.meta` / GUID，并同步更新 `Resources.Load`、导入器和预览生成器中的路径。

| 资源 | 位置 |
| --- | --- |
| 桌布图片 | `image/Board/TableCloth/` |
| 边框底纹 | `image/Board/Edge/`：四张底图对应五个图片样式 |
| 边框共享光照 | `image/Board/Edge/Lighting/` |
| 接缝与桌布光照 | `image/Board/TableSeams/`、`image/Board/TableLighting/` |
| 桌面选择缩略图和 catalog | `image/Board/Previews/` |
| 桌边模型和纯桌布网格 | `3D/TableFrame_Upright_V8.fbx`、`3D/DeskTopNew_ClothOnly_V8.asset` |
| 桌边材质和着色器 | `Materials/Board/Frame_OrangeWood_V8.mat`、`Materials/Board/TableFrameLayered.shader` |
| 场次计分表 | `UI/Description/MatchScoreTable.prefab` |
| 段位升降表 | `UI/Description/RankProgressionTable.prefab` |
| 四套手牌原图 | `image/Cards/Faces/{official,fluffy,hkmahjong,hongque}/hand/` |
| 四套手牌压缩图集 | 各牌面目录下的 `HandAtlas.spriteatlasv2`，每套独立 |

两张说明表是可编辑的 uGUI 预制体，主场景直接使用它们的实例；无需生成或保留旧表格 PNG。修改文字、行列和排版时编辑相应预制体。

边框共 18 个样式，其中 13 个用纯色参数，5 个图片样式共用四张底纹。深色橙木与原版橙木使用同一张图。默认边框也使用正式底纹，没有额外的回退图片；失败的自定义图片加载显示正式默认边框。用户正常上传的平面图片仍使用平面网格显示。

`TableSurfacePreviewBuilder` / `TableFramePreviewRenderer` 根据正式模型、材质与底纹生成缩略图；缩略图是设置界面的必要资源，不是备用底图。素材修改后可执行 `Tools > Mahjong > Table Surfaces > Rebuild 256px Previews`。

`other/Tablecloth` 保留可编辑美术源文件和制作说明；本机迁移脚本、验证及可恢复备份存放于 Git 忽略的 `.om_workspace/`。

手牌保留原始 PNG、272×389 的显示尺寸和原有引用，通过 SpriteAtlas V2 在构建时压缩；同一张手牌不会再额外打包独立纹理。`HandTileAtlasBuild` 在构建前检查并配置四套图集，桌面使用最高质量 BC7，Android/iOS 使用 ASTC 4×4，保持原始分辨率，不使用 Crunch。四套牌面的图片与图集各自独立，包括 official 与 fluffy 的花牌。

虹雀手牌按 Sprite 加载和预热，保留 Mipmap、16 倍各向异性过滤和 -0.5 的 Mipmap Bias。不可从手牌图集中取整张 Texture2D 再创建 Sprite，否则会显示整张图集。3D 的 `table/` 图片和加载逻辑不受此次手牌压缩影响。
