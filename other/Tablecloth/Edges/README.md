# 边框分层素材 · V7 / V8 通用 UV

## 当前运行时资源

十款 `Edge_Focus_*`、黑钛、青玉和原版蓝色使用颜色参数，不保留运行时底图。四张木纹底图统一放在主项目 `Assets/Resources/image/Board/Edge`，由 `Desktop` 加载，`TableFrameRenderer` 直接复用。`other` 的 PNG 与 PSD 作为美术源文件保留。

18 款边框的 256px 缩略图由 `TableSurfacePreviewBuilder` / `TableFramePreviewRenderer` 使用实际 V8 模型、分层材质和配色自动渲染，统一放在 `Assets/Resources/image/Board/Previews/Edge`。底纹、模型、材质或相关依赖变化后自动重建。默认边框引用同一张正式橙木底图，不另保留错误回退图。

运行时合成顺序为“底色/底纹 → 共享阴影 → 共享高光 → 接触描边”；已清理无效的饰线加载、缓存、计数器及着色器饰线分支。描边入口现在只在边框页，名称为“边框描边”。下面涉及饰线和PSD搭配的段落仅描述保留的美术源文件，不代表游戏仍加载这些图层。

`Edge.psd` 是 2048 × 2048、8 位 sRGB 文档，只保留三个顶层组：**01光照、02饰线、03边框底纹**。V7 将共享明暗适配到“短直壁、圆肩、水平顶面”的新模型；17 款既有底纹与 10 张可选饰线保留原文件和原像素。

```text
Edges/
├── Edge.psd
├── Lighting/    4 张：V7 默认高光/阴影 + 2 张旧版备选
├── Lines/       10 张可选饰线 RGBA PNG
├── Textures/    17 张纯底纹 RGB PNG
└── README.md
```

## 默认搭配

- **01光照**：默认开启 `UprightFrameHighlight_V7`（Screen）和 `UprightFrameShadow_V7`（Multiply）。旧 `OriginalBevelHighlight`、`OriginalBevelShadow` 保留为明确标注的备选，默认关闭。不要将两套光照同时打开，否则会重复压暗。
- **02饰线**：前十款各有一层，默认全部关闭。按当前底纹名称选择相应层；原 alpha 保留，不重新增强。
- **03边框底纹**：每次只显示一张。PSD 默认仍为原蓝色 `Edge_bule`；该默认不修改游戏中的选中项。

主项目的 `Assets/Resources/image/Board/Edge/Lighting` 只包含两张 V7 默认图。`other` 的 Lighting 目录与 PSD 保留四张，以便编辑和对照。

## V7 明暗层

V7 是本项目自行生成的预绘明暗：平顶为轻柔宽明暗，圆肩仅有克制亮条，直壁以同色 Multiply 压暗并保留原木纹，接触处使用很窄的暗线。

初始参数下，直壁有效颜色乘数约 **0.639**；接触线最暗采样仍保留约 **0.510** 的原色。圆肩使用低有效强度的灰色 Screen，峰值等效白色 Screen alpha 约 **0.0081**。使用灰色是为了让极弱高光有更平滑的 alpha 量化；它不会把整条边缘画成白线。最终效果以主项目实际相机验收为准。

该组参数已通过主项目原相机检查并锁定，浅橡木和樱木的内壁木纹保留，高光保持克制；橙木圆肩上的既有纹理不为追求额外光滑而重画。两张 PNG 的 alpha 已包含强度。按“底纹 → Normal 饰线 → Multiply 阴影 → Screen 高光”的顺序组合，常规强度为 1；饰线默认为 0。Unity Linear 项目采样 sRGB 图后，会按既有约定在 sRGB 显示数值中混合，再返回线性输出，使 PSD 与运行合成一致。

## 底纹与新 UV 的关系

17 张底纹均为 **2048 × 2048 RGB，无 alpha**。原图没有重采样或重绘；木纹、原橙木色、浅橡木色及十款主题基色保持原文件字节。10 张饰线也逐字节保留。原第 11 款 WalnutBrass 已删除，原第 13 款 CherryGunmetal 保留。当前设置列表顺序见下方“样式与接触线”。

新模型继续使用本项目自己的 `FrameSurfaceUV`：保留既有 20 个 UV 岛及原分区边界。新增圆角面在相应旧区内按弧长细分；原内坡底纹区域现在映射到短直壁。只有表面结构和新的独立光照发生变化，不把暗带重新烘进底纹。

模型源文件位于相邻 `../model/`；准确尺寸与闭合网格说明以该目录 README 为准。设置面板使用自动生成的 **256px 模型缩略图**；用户上传的平面边框使用原平面网格显示，加载失败时使用正式默认边框。

当前场景使用加高后的 V8 模型。它完整保留 V7 的 20 岛 UV，因此本目录的 PNG 与 PSD 均原样复用。V8 内壁从 3.53 增至 5.30，顶面收窄以补偿当前游戏相机中的视觉宽度；尺寸变化仅发生在模型上。

主项目 `MainScene` 直接保存 `DeskTopNew/Table Frame (3D)`、模型引用和默认材质，停止 Play 后仍显示 3D 边框。模型在 `Assets/Resources/3D`，默认材质为 `Assets/Resources/Materials/Board/Frame_OrangeWood_V8.mat`，包含橙木底纹及两张 V7 默认光照。运行时只创建材质实例并切换底纹或纯色，不创建边框对象、复制网格或加载模型元数据。

## 样式与接触线

主项目提供 **18 个样式：5 个图片样式共用 4 张底纹，另有 13 个纯色参数预设**。图片样式为原版橙木、深色橙木、白橡木、樱木和象牙漆木。顺序及保存的选择名称保持不变。

`Edge_orange_wood_deep` 复用 `Edge_orange_wood` 的完整木纹，在共享光照合成之前乘显示 sRGB 系数 **(0.60, 0.64, 0.70)**。使用较深的棕木配色。此款是材质配色预设，**没有额外复制 2048px PNG，也没有把调色烘入原 PSD**。唯一额外位图为自动生成的 256px 设置缩略图。

新增黑色接触线由 `TableFrameLayered.shader` 绘制：只作用于朝向桌布的竖壁根部，布面为模型 Y=0，宽度参数 `_ContactWidth=0.5`，不透明度 `_ContactOpacity=0.98`，上缘按像素导数抗锯齿。原来的暗棕木纹内壁及圆肩高光仍保留。接触线参数已保存在默认场景材质，全部内置 3D 样式继承；不改变模型几何或桌布。这条线是材质效果，未覆盖本目录旧共享光照 PNG / PSD。

桌布设置页提供独立的 **接触描边：关闭 / 开启** 下拉框，默认开启，并以 `TableContactOutlineEnabled` 保存选择。关闭时只将边框运行材质实例的 `_ContactOpacity` 设为 0；开启时恢复场景材质保存的强度。切换桌布、边框及重新打开设置页都会保留选择，不改动素材或重载纹理。

`MainScene` 中 `DeskTopNew/Table Frame (3D)` 的 MeshRenderer 将 **Cast Shadows 设为 Off**。边框自身的立体明暗由共享光照层表现，避免实时投影在左侧桌布上额外形成一条不受“接触描边”控制的暗线。桌布仍接收其他物体的阴影，麻将牌的投影设置不变。新场景复用该边框时也应保留此设置。

列表、资源别名与配色统一在主项目 `Assets/Scripts/GameSceneConfig/TableFrameStyles.cs`。缩略图由 `TableSurfacePreviewBuilder` 自动维护，配色变化会更新该变体的缩略图，普通款继续复用原缩略图。材质 `_BaseTone` 为 Vector，按显示 sRGB 数值计算，勿当作线性空间的 Color 属性转换。

## 编辑与导出

PSD 保持三组独立，不使用隔离的 Normal 光照组。光照与饰线组为 PassThrough，允许 Screen / Multiply 正确作用于下方底纹；底纹组为 Normal。

导出完整合成图用于 V7 / V8 UV0 时，应关闭材质额外的同一套叠加层，避免重复。若修改底纹，请保持 2048 尺寸及既有 UV 区域；若只调整明暗，请编辑 Lighting 图层，不将修改直接涂入全部 17 款底纹。

旧备选光照由此前本项目真实 Board.psd 原生分离后映射而来。既有基础素材的授权及历史来源保持原记录。
