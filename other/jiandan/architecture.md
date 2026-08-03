# PR A 实现边界

## 权威边界

Python 服务端是简单麻将的权威实现。客户端计算只服务于即时提示，不参与最终合法性和结算决策。

服务端模块职责：

- `game_calculation/jiandan`：牌型、番种、听牌和分数计算。
- `gamestate/game_jiandan/action_check.py`：合法动作与响应优先级。
- `gamestate/game_jiandan/settlement.py`：支付矩阵。
- `gamestate/game_jiandan/JiandanGameState.py`：对局时序与固定首家和牌终局。
- `gamestate/game_jiandan/boardcast.py`：沿用普通客户端可消费的单次 `show_result` 协议。
- `database/jiandan`：牌谱、规则专用统计表、稳定番种 ID 聚合与查询。

## 固定首家和牌终局

- 自摸确认后立即结束本局。
- 弃牌或抢加杠存在多名可和玩家时，按责任玩家之后的固定座次选择最近的首个合法赢家（头跳）。
- 首个赢家确认后不再处理后续和牌响应，也不再发牌。
- 迟到或重复的和牌动作不能增加第二名赢家。
- 牌墙耗尽且无人和牌时按普通流局结束。

房间、`game_info` 和 `show_result` 不包含任何赢家阶段、续打或展示队列配置。

## 通用客户端同步路径

- 与青雀一致，每个手牌动作窗口和弃牌响应窗口都会通知所有四个视角。
- 只有需要作出选择的玩家收到非空 `action_list`；其他玩家收到同一标准消息中的当前行动者和服务端权威余牌数。
- Unity 继续使用既有 `broadcast_hand_action -> AskHandAction -> ShowCurrentPlayer` 路径刷新行动方和中央余牌，不需要 Jiandan 专用客户端刷新函数。
- 对手摸牌只下发已有的视角脱敏 `deal_tile`，不额外伪造 `deal_tiles` 计数。

## 跨局与统计

- 每局开始恢复房间配置的局时；准备阶段超时不会消耗下一局局时。
- 座次轮转采用青雀的玩家身份换位模型，当前座位始终以东家索引 `0` 开始；结算分数变化按稳定的 `original_player_index` 映射到客户端身份。
- 排名、和牌次数、放铳、和牌巡目、副露和稳定番种 ID 在整场结束时写入 Jiandan 专用统计表。
- 含机器人或比赛场的对局只保留既有牌谱处理，不写入玩家历史统计。

## Unity 允许域

Unity 只改动：

- 建房规则选项、房间配置显示和静态字典；
- 轮数、牌桌中央信息和计分板所需的最小静态规则分派；
- `room/create_Jiandan_room` 与 `gamestate/jiandan/*` 路由；
- `data/get_jiandan_stats` 路由与玩家资料页统计展示；
- 独立 `JiandanAssembly` 内的提示计算内核，以及 `CalculationScriptAssembly` 内的 `JiandanExternal` 桥接；
- 四个从现有 UI/回放入口调用计算程序集的最小桥接点。

未修改 `Game3DManager*`、`NormalGameStateManager*`、`RoundEndPresentation*`、结果面板或牌谱主流程；计分板仅补充 Jiandan 静态映射与番数显示格式，统计只进入数据路由和玩家资料页。

## 后续扩展

若需要多阶段和牌、特殊动画或结果布局修复，应先确定协议和可复用公共方法，再分别提交独立 PR；PR A 不预埋这些能力字段。
