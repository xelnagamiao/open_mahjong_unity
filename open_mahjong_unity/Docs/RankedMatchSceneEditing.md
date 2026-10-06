# 段位匹配面板：场景编辑说明

入口：`Assets/Scenes/MainScene.unity` 中的 `MatchPanel/MatchLayout`。

当前四规则配置、评分与维护入口见 [四规则段位与 Elo 匹配](RankedRules.md)。国标原 12 个入口保留，新增立直 6 个级段入口、青雀 1 个全庄 Elo 入口和川麻血战 1 个全庄 Elo 入口。

当前采用浅石灰背景。规则页、场次方块、底部四个场次槽位、匹配中状态和选中描边都已保存为场景对象。运行时不 Instantiate/Destroy 面板，不生成 UI，不重设颜色与字体。过渡动画临时调整槽位的布局宽度和透明度，结束后恢复场景中配置的静止尺寸。

## 手动调整

| 对象 | 修改位置 |
| --- | --- |
| MatchPanel | Image / Color：全屏背景 |
| MatchLayout | RectTransform：整体宽度和外边距；Scale X/Y 为 1.12，整组按原比例放大；Vertical Layout Group：分区间距 |
| HeaderBar | 规则和段位信息并排；Layout Element 固定高度 70；Horizontal Layout Group 控制横向间距 |
| HeaderBar/RuleTabs/Rule_0 | Image：普通标签底色；SelectedBackground：选中底色；Label：字体和字号 |
| HeaderBar/RankSummary | Image：段位区底色；ProgressTrack/ProgressFill：进度条颜色 |
| RulePages/Guobiao | 每页 Row/Column 下的 MatchButton：原始四色、标题、人数条、条件条、选中描边 |
| 每个 MatchButton | Layout Element 的 Preferred/Min Width 和 Height 控制尺寸，目前统一 370 × 176；父 Layout Group 控制间距，三行总高 556 |
| AvailableContent、RestrictedContent / Level、Round | 两种状态使用相同的顶部锚点；标题距顶 12.8、高 49.6，局制距顶 59.2、高 35.2，比原位置上移 4，收紧标题与描述的间距 |
| MatchButton/AvailableContent | 满足进入条件时显示，包含 Level、Round 和 OnlineCount；人数栏距顶 114.08、高 41.92，位于 y=20–61.92，与受限场次“条件条＋人数栏”整组垂直居中；原字体、配色不变 |
| MatchButton/RestrictedContent | 未满足条件时显示，包含 Level、Round 和 OnlineCount；人数栏位于 y=6–47.92（41.92 高），与条件条共同组成 69.92 高的状态区 |
| MatchButton/EligibilityStrip | 黑色条件提示行位于人数栏上方 y=47.92–75.92（28 高），与人数栏零间距贴合；仅不满足入场条件时显示，点击仍打开条件详情 |
| MatchButton/HoverFrame、SelectedFrame | HoverFrame 的白色边线用于悬停；按下立即显示 SelectedFrame 的橙色边线，入队后保持。SelectedFrame/Contrast_Edge_* 为细深色衬边，确保橙色场次也能区分选中状态。各边线颜色可在 Image 中修改 |
| QueueBar | Layout Element：占位高度 84；Horizontal Layout Group：整体右对齐；旧整条背景 Image 已停用 |
| QueueBar/CoupledGroup | Horizontal Layout Group：左右内边距 24、上下 12、条目与状态区间距 12；随可见条目及动画自动计算宽度 |
| QueueBar/CoupledGroup/CapsuleOutline | Match Capsule Graphic：Color 为内底颜色，“描边颜色”“描边宽度”控制外框；圆角半径自动适配高度，默认描边 2.5 |
| QueueBar/CoupledGroup/QueueSlots/Slot_1…4 | Layout Element：270 × 48（含右侧 6 像素留白）；Match Queue Slot / Transition Duration：默认 0.24 秒；Canvas Group 与 Rect Mask 2D：用于淡入与展开裁切 |
| Slot_1…4/Visual | RectTransform：固定 264 × 48 的可见方块；TierColor_0…3：原段位配色；Tier/RuleAndMode/Remove：场次、局制、单项取消。修改方块宽度时同步调整外层 Layout Element 宽度 |
| Slot_1…4/Visual/Remove | Button 点击区域拉伸覆盖整块 Visual；悬停与按下通过透明白色 Image 高亮整块，点击任意位置取消该场次；右侧 × 保留。Button/Colors 可调整反馈强度 |
| QueueBar/StartMatch | 保留的停用对象；现在点击场次直接匹配，无需开始按钮 |
| QueueBar/CoupledGroup/MatchStatus | Layout Element：234 × 48；原矩形 Image 已停用，状态文字、Elapsed、CancelMatch 直接位于胶囊内 |
| MatchStatus/CancelMatch | 64 × 64 点击区域；Feedback 是圆形悬停/按下底色，Button/Colors 控制透明度。按当前整体 1.12 倍缩放，1920×1080 下点击区域约 72 × 72 |

Layout Group 管理的对象应通过 Layout Element 修改尺寸，避免仅修改 RectTransform 后被 Unity 的布局组件重新计算。没有 Layout Group 管理的对象可直接修改 RectTransform。

选中、队列槽位、其他规则页和匹配状态默认可能处于隐藏状态；在编辑模式勾选 GameObject Active 可预览和编辑，运行时会根据真实状态切换。运行时颜色不会被控制器重设。

每个场次都有场景中绘制好的 AvailableContent 和 RestrictedContent，两者按 CanEnter 的结果互斥显示，EligibilityStrip 跟随 RestrictedContent。可分别启用对应对象手动调整位置、颜色和字体；脚本只切换显示并同步两套人数文字，不在运行时重算样式。编辑模式默认显示初级场有权限、其余等级未满足条件的示例；运行时以真实入场资格为准。

`LegacyPresentation` 保存旧背景和标题的停用对象。旧 `MatchQueueingPanel` 场景对象、脚本及调用已移除；排队由 `MatchLobbyView` 的底部胶囊显示，状态和计时仍由 `MatchStateManager` 维护。原 `MatchDescribePanel` 和 OverlayCanvas 上的 `MatchFoundedPanel` 继续复用。

## 段位详情与规则表

入口为 `OverlayCanvas/NotificationPanel/DescribePos/DescribePanel`，子对象 `DescribePanel` 是蓝紫色内容面板。内容面板居中，RectTransform 尺寸为 1136 × 840。外层遮罩铺满画布；面板初始隐藏，由场次感叹号及“查看条件”打开。

标题固定在顶部，下面的 `Scroll View/Viewport/Content` 按说明文字、计分表、段位表的顺序纵向排列。国标使用 `MatchScoreTable`、`RankProgressionTable`；立直使用独立的 `RiichiMatchScoreTable`、`RiichiRankProgressionTable`，两套表互斥显示。切换场次或重新打开会回到顶端。

| 对象 | 手动修改位置 |
| --- | --- |
| 内容面板 / HeaderBackground | Image / Color：蓝紫正文底色与深紫标题栏 |
| Title / Describe | TextMeshPro：标题 37 号，说明 30 号；保留原项目字体 |
| CloseButton | 56 × 56 点击区域；CrossLeft / CrossRight 为白色关闭符号；Button 的 Colors 控制悬停与按下反馈 |
| MatchScoreTable / RankProgressionTable | 两张表均为场景原生单元格，宽度随 Content；Vertical Layout Group 自动汇总行高 |
| TableTitle / ColumnHeaders / Group_01… | Layout Element 控制高度，分别为 48 / 72 / 40；标题 26 号，其余 22 号；标题、列头和分组行使用统一暖色底色 |
| DataRow_01… / FixedRank_10 | 两表正文统一 44 行高、22 号字；Row 的 Image 为网格线色，Cell 的 Image 为交替行底色 |
| Cell_01… / Label | RectTransform 的横向锚点控制列宽；TextMeshPro 修改单元格文字；相邻行的同列需同步调整 |

两表间距为 24，表格行间距为 -1，用于让相邻网格共用细边线。旧表格 Image 组件已停用，旧 PNG 素材保留；实际显示的文字、边框和底色均可在场景或对应预制体里编辑。运行时不生成或重设表格样式。四个预制体保存在 `Assets/Resources/UI/Description/`。

两张表的标题居中显示。段位升降表的四个场次名称、“分段”表头及所有段位名称均左对齐，Label 的左边距统一为 32，使文字起点沿同一条竖线排列。计分表的局制分组名称仍居中。表中数值按当前国标配置核对：九段升段分数为 7000，十段说明仅显示“荣誉称号”。此次仅更新展示，没有修改实际升降段算法。

立直表按 M 规争一计分：`PT = q × (k × M - C)`。计分表包含顺位奖励、终局点数示例和六个场次倍率；段位表包含起始／升段 PT、掉段和三种场次的 C，不能进入的场次显示“—”。负 C 是保护奖励，十段固定 100/100 PT。立直计分表正文行高 62、段位表正文 44、说明分组行 66，列宽由各 Cell 的横向锚点控制，文字 22 号。

修改计分配置后，使用 `Tools/Mahjong/Main Scene/Update Riichi Ranked Descriptions` 更新两张立直表、机制说明和 `RankChangePanel/RatingDetails` 序列化引用。该工具只增量维护这些对象；结算明细在原面板 PT 变化下方显示 M、k、C、q，仅新算法立直结算可见。

## 当前正式能力

点击可进入的场次立即向服务端入队；继续点击其他规则或场次追加，客户端和服务端均限制最多 4 场。再次点击已加入的主场次方块会取消该场次；首次加入尚未确认时再次点击，取消请求会排在加入请求之后发送。底栏显示真实队列和等待确认的加入请求，点击小方块任意位置（包括右侧 ×）单独取消该场次，橙色状态区的关闭按钮取消全部。请求等待确认期间对应小方块暂不可点击且不显示关闭图标，仍可再次点击主场次方块取消，或取消全部。

追加、移除其中一个场次不重置匹配计时。任何一场凑齐四人，服务端会在通知之前移除这四名玩家的所有其他队列并锁定对局，底栏只显示最终匹配成功的场次。断线会清理全部等待队列；已成功配对的对局锁仍按原规则保留。大厅匹配人数按独立玩家统计。

`RulePages` 的 Flexible Height 为 0，固定高度 556；底栏紧接三行 176 高的方块，行间距保持 14。`Title` 停用，规则标签与段位信息并排放入 `HeaderBar`。MatchLayout 的 Vertical Layout Group 使用 Middle Center，RectTransform 底部预留 60、顶部 0；整组内容按收起后的聊天输入栏居中。MatchLayout 的 Scale X/Y 为 1.12，统一缩放顶部、场次和匹配底栏。1920×1080 下整组宽约 1707，左右各留 107；卡片加高后底栏仍与关闭状态聊天栏保留间距。聊天框展开或收起不驱动页面位置变化。规则切换不改变区域高度。所有静止颜色、尺寸和位置仍由场景对象控制。

匹配条目采用“场次名＋局制＋关闭按钮”的单行内容；保留规则前缀以区分跨规则队列。所有条目与“匹配中”、计时、取消操作包在同一个橙色描边胶囊中。状态区固定在右侧，新增条目向左扩展，外框同步伸缩。空闲时整组隐藏，不显示额外点击提示。原“匹配场次 n / 4”标题保留为停用对象。

过渡使用不受 Time Scale 影响的时间。入队展开并淡入，退队收起并淡出；移除中间条目时保留其他槽位的场次身份，由布局自然滑动补位。退出中再次加入可从当前宽度和透明度反向接续。关闭匹配页面会停止动画，重新打开时恢复权威队列的完整尺寸。Match Lobby View 的 Transition Duration 控制整组淡入淡出；各槽位的同名字段控制自身展开收起，建议保持一致。

真实多场匹配需要同时更新并重启 `open_mahjong_server`，旧服务端不支持新多队列协议。主场景保留国标 12 个场次，并提供立直、青雀和川麻血战的独立规则页，共 20 个真实队列入口。

## 交互维护与回归

服务端回归命令：`python -m unittest server.match.test_multi_queue server.event.test_auto_match`（在服务端目录执行）。

当前感叹号 Requirement/Image 的反馈范围完整拉伸至 Requirement 点击区域；自身不拦截射线，由父 Button 处理输入。EligibilityStrip 绑定 MatchButton.conditionButton，和感叹号共用说明入口，不改变场次入场资格。空闲提示 EmptySelection 已删除。HeaderBar 显示四规则标签及对应段位／Elo 信息，高度保持 70；机制说明按钮位于规则栏上方。

主场次的悬停、按下及已入队边框由 MatchButton 的指针事件和队列状态共同控制；退出页面时清除临时指针状态。底栏整块点击和取消全部沿用原 Button 监听与匹配接口。

## 人数文字和匹配成功面板

20个场次卡片的 OnlineCount 中，“等待”“对局”标签统一为25号，关闭自动字号；人数数字保持原设置。所有方块为370×176，人数栏为41.92高。准入场次的人数栏与受限场次的“条件条＋人数栏”整组共用垂直中心，中心距方块底部40.96。受限场次的28高条件条与人数栏零间距贴合，整组底部留白6；条件文字分别为19号和17号。标题和感叹号保留位置及尺寸，局制描述统一上移4，缩短标题与描述之间的距离。两种状态下都显示等待及对局人数；切换资格时同步切换布局，不影响人数更新、匹配资格判断或轮询接口。旧匹配中弹窗已删除，切页和返回时由新胶囊的状态刷新恢复显示。

OverlayCanvas/MatchFoundedPanel 的遮罩覆盖全屏，GameObject 内容卡片居中，尺寸640×300。三行文字分别为：标题48号（560×68，y=82）、场次30号（560×64，y=4）、进场状态26号（560×44，y=-82）。保留原底色，进场文字显示“正在进入对局…”，满员后直接开局，不再倒计时。
