# round_meta 四规则示例牌谱

本目录下的示例 JSON 描述 **planned** 牌谱格式：每局 `round_index_N` 含 `round_meta`，客户端不再推理座位。

| 文件 | 规则 | 演示要点 |
|------|------|----------|
| [game_record_example_guobiao.json](game_record_example_guobiao.json) | 国标 | 第 2 局 `seats: [3,0,1,2]`（每局 back 轮转） |
| [game_record_example_qingque.json](game_record_example_qingque.json) | 青雀 | 第 5 局 `seats: [2,0,3,1]`（随机换位，不可公式推导） |
| [game_record_example_classical.json](game_record_example_classical.json) | 古典 | 连庄：`round_index_2` 仍 `current_round: 1`，`seats` 不变 |
| [game_record_example_riichi.json](game_record_example_riichi.json) | 立直 | 连庄：`honba: 1`，`dealer_index` / `seats` 不变 |

> 示例中的 `_comment`、`_round_meta_note` 字段仅作说明，**服务端写入时应省略**。

---

## round_meta 字段

### 全部规则必填

| 字段 | 类型 | 说明 |
|------|------|------|
| `seats` | int[4] | `seats[original_i]` = 本局 `player_index`。与 `p0_tiles`～`p3_tiles`、`action_ticks` 中的 seat 一致 |
| `dealer_index` | int | 本局亲家/庄家的 `player_index` |
| `start_player_index` | int | 回放起手行动 seat（日麻=亲家；其余通常为 0） |

### 立直麻将额外必填

| 字段 | 类型 | 说明 |
|------|------|------|
| `honba` | int | 本局**开场**本场棒数 |
| `riichi_sticks` | int | 本局**开场**场供（立直棒）数 |

---

## 与 pN_tiles / action_ticks 的关系

```
game_title.pK_uid  →  original 座位 K（整局不变）

round_meta.seats[original_i]  →  本局 player_index

pN_tiles  →  本局 player_index == N 的初始手牌（player_list[N]）

action_ticks 里的 action_player  →  本局 player_index
```

**客户端映射：**

```text
originalPlayerIndex = K（来自 pK_uid）
playerIndex         = round_meta.seats[K]
手牌                = p{playerIndex}_tiles
```

---

## 四规则 round_meta 典型值

| 规则 | dealer_index | seats 变化 | honba / riichi_sticks |
|------|--------------|------------|------------------------|
| guobiao | 0 | 每局 back 轮转；5/9/13 固定换位 | 不写 |
| qingque | 0 | 每局 back；5/9/13 **随机**换位 | 不写 |
| classical | 0（庄家恒 seat0） | 连庄不变；过庄轮转 | 不写 |
| riichi | 亲家 seat | **整局 seats 通常不变** | 每局写入 |

---

## 兼容策略

- 新牌谱：**必须**含 `round_meta.seats`（长度 4）
- 无 `round_meta` 的旧牌谱：**拒绝加载**（不 fallback 客户端推理）

实现状态：示例先行；服务端 `init_game_round` 与客户端解析待按计划落地。详见 [game_record_format.md](game_record_format.md)。
