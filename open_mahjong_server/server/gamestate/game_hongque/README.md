# 虹雀服务端

虹雀编排方式对齐国标：`submit_action` 只入队，主循环按 `game_status` 推进，
超时在 `wait_action` 等待结束之后处理。客户端牌码与 Unity 的 HQv3.1 资源名一致（`AX1` 至 `GY9`）。
终局无机器人时写入 `game_records`；无论是否有机器人都会在 `game_end` 附带 `record_detail` 供本地落盘。
观战接入共享的 180 秒延迟牌谱与好友实时观战；房间开关和机器人数量限制沿用公共策略。
实时观战按被观战者座位发送手牌与摸牌信息，其他座位的暗牌保持隐藏，观战客户端不能提交对局操作。

## 局数与显示

- Elo 匹配提供半庄 8 局（K=16）与全庄 16 局（K=32）两个队列，共用虹雀 Elo 分数；自定义局数仍为 4、8、12、16 局。
- 对局手牌保持固定牌表顺序，摸牌槽与手动排列保持原行为。
- 鸣牌与和牌页面按整组色环连续显示，例如红—紫—红紫显示为红—红紫—紫；仅展示排序，不改变协议牌码和计分数据。
- 点和使用公共河牌移入流程；多家点和只取走一次实体河牌，后续赢家使用展示副本。
- 重连和实时观战初次进入时，完整快照携带独立摸牌槽及仍在牌河中的末张弃牌来源；后续增量继续沿用同一事件链。
- 全量重连可直接建立空的本地会话；同一 action_tick 重连也重建操作栏、提示和结算面板。多家和历史按赢家逐行对齐服务端，重连不重复记分；重建桌面时取消旧的结算演出。
- 暂停保留出牌、鸣牌和战术窗口的剩余时间；恢复后重新询问真人与机器人。结算确认任务等待暂停和恢复广播结束后才发下一局。
- 牌谱听牌、危险牌和剩余张数使用虹雀本地计分与完整变长副露；每种牌只有一张，摸牌由独立摸牌槽判断，不套用普通麻将的手牌张数规则。

## 2026-10-18 计分调整

全纯色、全半色由 1 番调整为 2 番；服务端、Web、Unity 本地提示及阅读番表保持一致。
规则书沿用原入口，并同步更新总表、定义和受影响牌例。
历史牌谱中的显式番值及得分保留原值；旧牌谱补全脚本的原版 v1.6 番值表继续冻结为当时数值。

## 目录职责

### 对局编排

- `HongqueGameState.py`：房间生命周期、主循环、回合结算。
- `state_machine.py`：权威对局状态及合法迁移。
- `player.py`：玩家领域模型。
- `init_tiles.py`：牌山、发牌、摸牌和调试牌例初始化。
- `boardcast.py`：按观察者裁剪；开局/重连发完整桌面，对局只发增量事件。
- `wait_action.py`：阻塞等待手牌/鸣牌；超时在循环结束后走默认切、pass 或执行当前申请。

### 行动处理

- `action_check.py`：无副作用的合法行动检查。
- `action_priority.py`：和、虹、碰、吃的唯一优先级表。
- `ron_resolution.py`：多家荣和收集与结算。
- `get_action.py`：机器人决策后入队。

同类型机器人共用 `user_id`，内部调用 `submit_action` 必须同时传入 `player_index`，
保证出牌、补牌、杠和鸣牌回应进入各自座位的队列。真人网络入口仍按认证用户定位。
普通机器人在初次鸣牌询问和战术重询时都自动 pass。

### 规则与计算

- `tile.py`：牌码模型。
- `group_index.py`：合法牌组索引。
- `rules.py`：牌组、鸣牌和杠候选。
- `win_check.py`、`tenpai_check.py`：和牌与听牌检查。
- `scoring.py`：虹雀计分。
- `efficiency_bot.py`、`heuristic_bot.py`：虹雀专用机器人。
- `hongque_debug.py`：显式启用的人工测试牌例。

## 状态机

运行时代码只能通过 `state_machine.py` 执行以下迁移：

```text
waiting
  -> waiting_hand_action          # 开局摸牌后
  -> resolving_discard            # 切牌
  -> waiting_action_after_cut     # 有人可鸣牌
  -> onlycut_after_action         # 亮牌落地：切 / 加杠 / 补（补牌后才能和）
  -> deal_card                    # 无人鸣牌后历时摸牌
  -> waiting_hand_action          # 摸牌或补牌后
  -> waiting_ready / END
```

网络字段 `phase` 仅用于兼容旧客户端；`game_status` 是权威状态，
`state_version` 用于识别开局/重连全量包的新旧。

亮牌后若已经不能补牌，吃碰虹或加杠必须保留至少一张手牌可切，
避免清空手牌后陷入无合法动作的等待；此时仍可按原规则荣和。

## 鸣牌优先级

- 多家和：`hu_first/second/third = 12`，同级收集并支持多家荣和。
- 虹：`hong_first/second/third = 11/10/9`。
- 碰：`peng_first/second/third = 8/7/6`。
- 吃：`chi_first/second/third = 5/4/3`。

`first/second/third` 表示相对出牌者的顺时针距离。同一张弃牌上的虹、碰、吃
按座次而不是请求到达顺序仲裁。最终仲裁前不会修改手牌、河牌或副露。

战术鸣牌始终保留弃牌开窗时的完整候选快照。有人亮牌后，只从该快照剔除
不高于当前申请的动作并重新询问；当前申请者不会立即追问自己，但被另一家
更高优先级动作打断后，可以重新选择仍然有效的碰、虹或和。

同批入队的鸣牌响应在执行前会再次核对候选。先执行的申请淘汰旧候选时，
只丢弃该旧响应，保留玩家在新窗口改选更高优先级动作的机会。

## 测试与资料

- 测试：`open_mahjong_server/server/gamestate/game_hongque/test_*.py`
- 机器人研究记录：`open_mahjong_server/docs/hongque/`

从 `open_mahjong_server` 运行：

```powershell
python -m pytest server/gamestate/game_hongque -q -p no:cacheprovider
```
