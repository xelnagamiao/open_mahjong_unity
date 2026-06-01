# 局头字段说明与四规则示例牌谱

本目录下的示例 JSON 为当前牌谱格式：每局 `round_index_N` 顶层含 `seats`、`dealer_index`、`start_player_index`；立直另含 `riichi` 子对象。客户端直接读取，不再推理座位。

| 文件 | 规则 | 演示要点 |
|------|------|----------|
| [game_record_example_guobiao.jsonc](game_record_example_guobiao.jsonc) | 国标 | 第 2 局 `seats: [3,0,1,2]`（每局 back 轮转） |
| [game_record_example_qingque.jsonc](game_record_example_qingque.jsonc) | 青雀 | 第 2 局 `seats: [2,0,3,1]`（随机换位，不可公式推导） |
| [game_record_example_classical.jsonc](game_record_example_classical.jsonc) | 古典 | 连庄：`round_index_2` 仍 `current_round: 1`，`seats` 不变 |
| [game_record_example_riichi.jsonc](game_record_example_riichi.jsonc) | 立直 | 连庄：`riichi.honba: 1`，`dealer_index` / `seats` 不变 |

> 示例文件扩展名为 **`.jsonc`**（允许 `//` 行注释），仅供阅读；落库牌谱仍是纯 **`.json`**，**服务端写入时省略注释**。

---

## 局头必填字段（全部规则）

| 字段 | 类型 | 说明 |
|------|------|------|
| `seats` | int[4] | `seats[original_i]` = 本局 `player_index`。与 `p0_tiles`～`p3_tiles`、`action_ticks` 中的 seat 一致 |
| `dealer_index` | int | 本局亲家/庄家的 `player_index` |
| `start_player_index` | int | 回放起手行动 seat（补花结束后首行动玩家） |

## 立直麻将额外字段

嵌套对象 `riichi`（立直规则必填）：

| 字段 | 类型 | 说明 |
|------|------|------|
| `honba` | int | 本局**开场**本场棒数 |
| `riichi_sticks` | int | 本局**开场**场供（立直棒）数 |

国标/青雀/古典不写 `riichi` 对象。

---

## 与 pN_tiles / action_ticks 的关系

```
game_title.pK_uid  →  original 座位 K（整局不变）

seats[original_i]  →  本局 player_index

pN_tiles  →  本局 player_index == N 的初始手牌

action_ticks 里的 action_player  →  本局 player_index
```

**客户端映射：**

```text
originalPlayerIndex = K（来自 pK_uid）
playerIndex         = seats[K]
手牌                = p{playerIndex}_tiles
```

---

## 四规则局头典型值

| 规则 | dealer_index | seats 变化 | riichi |
|------|--------------|------------|--------|
| guobiao | 0 | 每局 back 轮转；5/9/13 固定换位 | 不写 |
| qingque | 0 | 每局 back；5/9/13 **随机**换位 | 不写 |
| classical | 0（庄家恒 seat0） | 连庄不变；过庄轮转 | 不写 |
| riichi | 亲家 seat | **整局 seats 通常不变** | 每局写入 |

---

## 加载策略

- 新牌谱：**必须**含顶层 `seats`（长度 4）、`dealer_index`、`start_player_index`
- 缺少上述字段的旧牌谱：**拒绝加载**（客户端抛错，不 fallback 推理）

详见 [game_record_format.md](game_record_format.md)。
