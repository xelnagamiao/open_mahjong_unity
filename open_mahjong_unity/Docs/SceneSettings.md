# 场景设置代码边界

## 共用职责

- `SceneConfigPanel` / `SceneConfigPanel.Defaults`：页面入口、显隐和恢复默认的编排。
- `SceneConfigUi`：公共按钮/Toggle 状态、HEX 解析及应用提示。HEX 成功后先调用面板动作，再提示；无效输入不调用动作。各面板继续决定是否强制 alpha=1、写入哪个配置和刷新哪些预览。
- `SceneConfigColorUi`：RGB/明暗滑条与数值同步，使用不通知的赋值；面板保留原有同步标记和布局刷新保护。
- `SceneConfigTextureCapture`：从 GPU 可用的贴图读取 sRGB RGBA 副本或 PNG，不要求源贴图 CPU 可读。牌背、牌面背景和牌边的编辑器拖图，以及桌布/边框的自定义缩略图共用此入口。
- `ConfigManager.ApplyColorBrightness`：卡牌、纯色桌布和纯色边框统一的明暗公式；基础 RGB 与明暗分开存储，向黑/白插值只产生显示色。
- `TableSurfaceColorEditor`：桌布/边框共用的“新建颜色 → 实时预览 → 返回/确认”界面。`TableClothColorControl` 仅保留与桌布 header 的接入，边框 header 使用同一个编辑器。
- `TableSurfaceColorLibrary`：保存独立的纯色项目，只存 RGB、明暗、名称和标识，不生成全尺寸图片。图库仅生成 128px 小预览。

## 桌布与边框新建颜色

左侧底色下拉框始终可用：第一项显示“图片模式”、官方纯色名称或当前自定义项名称，第二项为“新建颜色”。底栏的“新建桌布/新建边框”与其共用入口，位于上传按钮左侧。

调色盘修改只写入 `Desktop` 的临时预览，不改当前选择、RGB 存档或官方配色。明暗范围为 -100～100，不反写 RGB。点击“返回”或隐藏页面时清除临时预览，还原当前已保存的外观；确认保存成功后才选择新项目，更新 header 名称、追加图库末尾并滚动显示。保存失败留在调色盘供重试。

官方纯色固定使用 `TableClothStyles` / `TableFrameStyles` 的默认色，旧版按官方样式保存的颜色覆盖不再影响官方项目。新的用户颜色使用 `surface-color/cloth/<guid>` / `surface-color/frame/<guid>`，与上传图片的键和目录分离：WebGL 存入现有 IndexedDB，原生端存于 `persistentDataPath/TableSurfaceColors/*.json`。选择状态仍沿用现有桌布/边框配置；恢复默认不删除用户颜色，删除所选自定义颜色时回到默认选择。

纯色桌布仍经过 `TableSeamComposer` 合成缝线/阴影/光照；纯色边框仍使用现有 3D 边框材质的底色、内侧阴影、高光与描边。上传图片流程保持独立。

## 纹理所有权

`SceneConfigTextureCapture.Copy` 返回调用方拥有的新贴图，使用后由调用方按原生命周期释放。`EncodePng` 自己释放中间副本，只返回字节。不销毁传入的源图，不缓存结果。成功和异常出口都会恢复 `RenderTexture.active`、`GL.sRGBWrite` 并释放临时渲染目标。

图库继续决定缩略图最大尺寸、过滤方式、Clamp 和不可读设置；牌边拖图保留原有材质赋值和持有方式。该工具不承担加载、存档或材质刷新。

## 保留独立的部分

`Desktop`、`TableFrameRenderer` 和 `TableSeamComposer` 的加载/合成/释放职责不同，不应为了减少代码行数再引入一个总管理器。牌背与牌面背景的持久化、资源缓存和卡牌轮转也保持原来的所有者。共享的是转换、校验和显示算法，而不是所有面板的可变状态。

2026-09-15 的公共逻辑整理不修改场景、预制体、存档键、上传路径或轮转时机。

## 固定 UI 烘焙（2026-09-18）

`MainScene` 现在直接保存桌布/边框 header、调色盘、底栏按钮、各页恢复默认入口、卡牌预设/轮转/新建页、轮转行模板、中心盘选项及卡牌预览图层。运行时只绑定这些序列化引用、更新状态和响应操作，不再重建或反复排布这些固定控件。

固定 UI 构建与排版代码集中在各组件的 `*.Baking.cs` 中，并完整放在 `#if UNITY_EDITOR` 下；它们不进入玩家程序集。新增场景或修改固定结构后，在非播放模式执行 `Tools → Mahjong → Scene Settings → Bake Fixed UI`，检查布局并保存场景。重复烘焙不会追加重复控件。构建时 `SceneSettingsUiBuildCheck` 检查引用是否齐全，缺失时明确报错，不在玩家端偷偷补建。

用户桌布/边框图库、上传图片、颜色存档、卡牌预设目录等仍按数据动态更新；轮转列表实例化已烘焙的行模板。调色实时预览、返回取消还原、确认保存、IndexedDB 和轮转快照规则不变。新增中心盘样式后必须重新烘焙，使新增选项和预览引用进入场景。
