# 虹雀服务端

虹雀采用纯内存、事件驱动的服务端状态机，不创建牌谱或统计记录。
客户端牌码与 Unity 的 HQv3.1 资源名一致（`AX1` 至 `GY9`）。

## 目录职责

### 对局编排

- `HongqueGameState.py`：房间生命周期、回合结算和组件入口。
- `state_machine.py`：权威对局状态及合法迁移。
- `player.py`：玩家领域模型。
- `init_tiles.py`：牌山、发牌、摸牌和调试牌例初始化。
- `boardcast.py`：按观察者裁剪并广播权威快照。

### 行动处理

- `action_check.py`：无副作用的合法行动检查。
- `action_priority.py`：和、虹、碰、吃的唯一优先级表。
- `wait_action.py`：弃牌响应、战术鸣牌、超时及最终仲裁。
- `ron_resolution.py`：多家荣和收集与结算。
- `get_action.py`：机器人异步决策和 action tick 校验。

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
`state_version` 用于识别状态快照的新旧。

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

## 测试与资料

- 测试：`open_mahjong_server/server/gamestate/game_hongque/test_*.py`
- 机器人研究记录：`open_mahjong_server/docs/hongque/`

从 `open_mahjong_server` 运行：

```powershell
python -m pytest server/gamestate/game_hongque -q -p no:cacheprovider
```
