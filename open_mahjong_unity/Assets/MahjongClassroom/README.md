# 琪露诺的麻雀教室示例

在 Unity 中打开 `Assets/Scenes/CirnoMahjongClassroom.unity`，按 Play。

- 鼠标左键点击画面任意位置或“下一页”按钮：前进一页。
- “上一页”：返回一页；第一页禁用。
- “从头学习”：回到第一页。
- 第九页再次左键点击：重新开始课程。
- 右键和中键不推进。一次点击只前进一页。

场景不依赖登录、联网或 MainScene。独立启动时加载现有 ConfigManager，读取玩家保存的牌面与牌体设置；从主场景进入时复用已有配置。未加入默认构建场景列表，不改变现有游戏启动流程。

## 内容与修改

`MahjongBasicsLesson.asset` 保存 9 页可编辑内容：总览、万牌、筒牌、条牌、字牌、花牌、重复张数、基础组合、复习。每页包含标题、黑板说明、琪露诺讲解、记忆提示和牌例。

在 Inspector 修改该资源的 Pages。每页最多 3 组牌例，每组最多 9 张。Tiles 表仅保存 42 种牌的编号和名称：11—19 万、21—29 筒、31—39 条、41—44 东南西北、45 中、46 白、47 发、51—58 春夏秋冬梅兰竹菊。

牌例通过 TileFaceResolver 和 TileFaceFit 与主游戏共用牌面包、手牌底图、白板样式及花纹位置和缩放。牌张按所选底图的宽高比适配槽位，名称独立放在牌张下方；切换牌面或自定义素材加载完成后刷新，不重置当前课程页。

课程按含花牌的 144 张国标麻将讲解，只介绍牌面与组合，不是完整和牌及计番课程。花牌每种一张，其他牌每种四张。

场景 UI 已保存，按 Background、Header、LessonBoard、Teacher、DialoguePanel/Navigation 分组，可直接在 Hierarchy/Inspector 中调整。Canvas 以 1920×1080 为基准，使用 Expand 缩放保持完整课堂可见，非 16:9 窗口保留外围背景。

`Tools > Mahjong > Classroom > Create or Open Example` 可打开已有示例；场景不存在时才生成，不覆盖已编辑的示例。编辑器工具使用 Additive 打开，保留其他场景的未保存修改。测试时请只运行课堂场景，或将其设为编辑器的 Play Mode Start Scene。

新增角色美术集中在 `Assets/Art/MahjongClassroom/`。琪露诺立绘由 **afensorm** 为 **Taisei Project** 绘制，使用 **CC BY 4.0**；完整许可、来源链接、源文件及修改说明均保存在 `Cirno/` 中，分发时请一并保留。琪露诺是 ZUN / 上海爱丽丝幻乐团的东方 Project 角色，本示例为非官方二次创作。画面中的操作提示和素材说明已移至本说明文件。
