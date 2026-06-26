# 牌谱格式与数据库字段说明

本文档描述当前（已测试通过）的牌谱 JSON 结构、PostgreSQL 相关表字段，以及 Unity 客户端的解析与回放逻辑。实现入口见 `game_record_manager.py`（服务端写入）与 `GameRecordJsonDecoder.cs` / `GameRecordManager.cs`（客户端读取）。

---

## 1. 术语对照

服务端房间创建与对局运行时，有两组容易混淆的字段：

| 概念 | 服务端变量 | 牌谱 `game_title` 字段 | 典型取值 | 用途 |
|------|-----------|-------------------------|----------|------|
| **游戏规则** | `room_rule` | `rule` | `guobiao`、`qingque`、`classical`、`riichi` | 决定玩法逻辑、客户端回放分支 |
| **房间类型** | `room_type` | `room_type` | `custom`、`match` | 自定义房 / 排位赛等 |
| **子规则** | `sub_rule` | `sub_rule` | `guobiao/standard`、`guobiao/xiaolin`、`riichi/standard` 等 | 番表、国标固定换位、起和限制等 |
| **局数模式** | — | （不入 JSON） | — | 仅存 DB `match_type`，见 [§6.2.1](#621-match_type-完整取值) |

**历史问题（已修复）：** 旧牌谱曾把 `room_type`（`custom`）误写入 DB 的 `rule` 列或 `game_title.rule`，导致客户端 `rule == "guobiao"` 等分支不执行，风圈/风位 UI 不轮转。**新牌谱必须以 `game_title.rule` 存游戏规则。**

---

## 2. 牌谱 JSON 顶层结构

```json
{
  "game_title": { ... },
  "game_round": {
    "round_index_1": { ... },
    "round_index_2": { ... }
  }
}
```

整份 JSON 以 **JSONB** 形式存入 `game_records.record`，是唯一权威数据源；回放时应以 JSON 内 `game_title` 为准，而非仅依赖 API 顶层的 `rule` 字段。

---

## 3. `game_title` 字段

由 `init_game_record()` 初始化，各 `*GameState` 在开局后补充 `sub_rule` 等。

| 字段 | 类型 | 说明 |
|------|------|------|
| `rule` | string | **游戏规则**，取自 `self.room_rule` |
| `room_type` | string | **房间类型**，取自 `self.room_type`（`custom`、`match`） |
| `sub_rule` | string | 子规则，各 GameState 写入（如 `guobiao/standard`） |
| `commitment_hex` | string | 承诺值，64 位 hex 字符串；每局 `game_start` 广播，对局内不暴露主种子 |
| `salt` | string | 盐字符串，128 位 hex；与承诺值一同广播 |
| `game_random_seed` | int? | **旧格式**整局随机种子，仅历史牌谱兼容 |
| `max_round` | int | 风圈数（1=东风、2=半庄、4=全庄） |
| `start_time` / `end_time` | datetime | 对局起止时间 |
| `open_cuohe` | bool | 是否开启错和 |
| `tips` | bool | 是否开启提示 |
| `is_player_set_random_seed` | bool | 是否玩家指定种子 |
| `player_entry_order` | int[4] | **shuffle 前**对局入场顺序（user_id，自定义房/匹配通用），用于验证 `master_seed` 随机座位 |
| `hepai_limit` | int? | 起和番限制（国标等） |
| `p0_uid` … `p3_uid` | int | 随机座位分配后 original 0～3 的用户 ID（整局不变） |
| `p0_name` … `p3_name` | string | 对应用户名 |

**座位约定：** `player_entry_order` 记录 `master_seed` shuffle **之前**的对局入场顺序；`p0`～`p3` 为 shuffle **之后**的 original 座位（整局不变）。每局 `seats[original_i]` 给出当局 `player_index`；`p0_tiles`～`p3_tiles` 按 **当局 player_index** 存储；`action_ticks` 里的 `action_player` 亦为当局 **player_index**。

### 3.2 立直麻将 `game_title` 额外字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `red_dora` | bool | 是否启用赤宝牌 |
| `allow_kuikae` | bool | 是否允许食替（浪涌子规则不写，内置可食替） |
| `hepai_way` | string | 和牌方式 |
| `open_xiru` / `open_tobi` | bool | 西入 / 击飞 |
| `starting_score` | int | **统一起手分**。标准日麻 `25000`，浪涌 `50000`；房间可自定义覆盖 |
| `starting_scores` | int[4] | **可选**。按 original 0～3 的各自起手分；存在时优先于 `starting_score` |

旧牌谱无上述起手分字段时，客户端按 `sub_rule` 推断：`riichi/langyong` → 50000，其余立直 → 25000。

### 3.1 WebSocket 对局字段（`GameInfo` / 每局 `game_start`）

与牌谱表头不同，实时对局在**每局开始**广播以下字段（不含主种子）：

| 字段 | 类型 | 说明 |
|------|------|------|
| `commitment` | string | 承诺值（256 位，JSON 为十进制字符串） |
| `salt` | string | 盐字符串（128 位 hex） |
| `player_entry_order` | int[4] | shuffle 前对局入场顺序 user_id |
| `isPlayerSetRandomSeed` | bool | 是否玩家指定主种子（复式） |
| `round_random_seed` | — | **已废弃**，旧客户端字段 |

整局结束时 `Game_end_info` 额外公布 `master_seed`、`commitment`、`salt`，供验证 `SHA256(master_seed_hex + salt) == commitment`。

---

## 4. `game_round` / 单局结构

键名：`round_index_{N}`，`N` 从 1 递增。由 `init_game_round()` 创建。

| 字段 | 类型 | 说明 |
|------|------|------|
| `round_index` | int | 局序号（1 起） |
| `current_round` | int | 风圈内的局号（国标：1～4 为东，5～8 为南…） |
| `round_random_seed` | int? | **旧格式**本局随机种子；新牌谱不再写入 |
| `seats` | int[4] | **必填**。`seats[original_i]` = 本局 `player_index` |
| `dealer_index` | int | **必填**。本局庄家 `player_index` |
| `start_player_index` | int | **必填**。补花结束后首行动 `player_index` |
| `riichi` | object | 立直规则必填：`{ honba, riichi_sticks }` |
| `p0_tiles` … `p3_tiles` | int[] | 发牌后、补花前的初始手牌（**player_index** 顺序） |
| `tiles_list` | int[] | 洗牌后、发牌前的完整牌山 |
| `action_ticks` | array[] | 按时间顺序的操作序列（见下节） |

---

## 5. `action_ticks` 操作短码

数组第一个元素为动作类型，其余为参数。嵌套数组（如番种列表、分数变化）在 JSON 中为子数组。

### 5.1 通用操作

| 短码 | 含义 | 格式示例 |
|------|------|----------|
| `bh` | 补花 | `["bh", tile_id, action_player]` |
| `d` | 摸牌 | `["d", tile_id]` |
| `gd` | 杠后摸牌 | `["gd", tile_id]` |
| `bd` | 补花后摸牌 | `["bd", tile_id]` |
| `c` | 切牌 | `["c", tile_id, "T"\|"F"]` 或带 `"H"` 表示立直横置 |
| `cl` / `cm` / `cr` | 吃 | `[code, tile_id, action_player, h1, h2]`（`h1/h2` 为从手牌打出的**真实**牌 ID，含赤 5 的 105/205/305） |
| `p` | 碰 | `["p", tile_id, action_player, h1, h2]` |
| `g` | 明杠 | `["g", tile_id, action_player, h1, h2, h3]` |
| `ag` | 暗杠 | `["ag", tile_id, "T"\|"F"]`（必填第三段；`T`=摸杠 / `F`=手杠；客户端拒绝两段格式） |
| `jg` | 加杠 | `["jg", tile_id, "T"\|"F"]`（必填第三段；`T`=摸杠 / `F`=手杠；客户端拒绝两段格式） |
| `ca` | 战术鸣牌申请（未最终执行） | `["ca", player_index, apply_action, cut_tile]`（仅回放 display/音效，不改牌面；`apply_action` 与执行 tick 同码，如 `cl`/`p`/`hu_second`） |
| `liuju` | 流局 | `["liuju"]` |
| `end` | 本局结束 | `["end"]`（和牌/流局后紧跟；错和无 `end`，对局继续） |

**吃碰杠兼容：** 旧牌谱仅 3 段 `[code, tile_id, action_player]` 时，客户端按归一化后的 `tile_id±1` 算术回退推导手牌侧 ID。

**日麻赤 5 吃牌：** 105/205/305 与同点数 15/25/35 等价，但顺子仍须 456 三张不同点数。赤 5 只能作顺子的「5」位，故 `cm` 被鸣牌为 15 时手牌应为 `14+16` 而非 `14+105`；手牌出赤 5 时通常为 `cl`/`cr`（如打 16 手牌 14+105，或打 14 手牌 105+16），或 `cm` 被鸣牌为 105 且手牌 14+16。

### 5.2 国标和牌

`[hu_class, hepai_player_index, hu_score, hu_fan[], score_changes[]]`

- `hu_class`：`hu_self` / `hu_first` / `hu_second` / `hu_third` 等
- `score_changes`：`[seat0Δ, seat1Δ, seat2Δ, seat3Δ]`，按 **当局 player_index** 顺序

### 5.3 古典（数和尾）

`["shuhewei", fu_list, changes_list, fan_list, fu_type_list, hu_class, hepai_player_index]`

### 5.4 立直麻将

| 短码 | 格式 |
|------|------|
| `riichi` | `["riichi", player_index, is_daburu]` |
| `dora` | `["dora", tile_id]` |
| `ryuukyoku` | `["ryuukyoku", tenpai_flags[], score_changes[], reason]` |
| `hu_riichi` | `["hu_riichi", hepai_idx, hu_class, han, fu, yaku[], score_changes[], dora[], ura_dora[], aka_count, honba, riichi_sticks]` |

立直 tick 中 `score_changes` 同样按 **当局 player_index** 排列；客户端解码时经 `seats` 换算为 original 顺序累加 `Round.scoreChanges`。

### 5.5 其他

- `jiuzhongjiupai`：九老峰回流局

---

## 6. 数据库表

### 6.1 `game_records`

| 列 | 类型 | 说明 |
|----|------|------|
| `game_id` | VARCHAR(16) PK | base62 随机 ID（10 字符） |
| `record` | JSONB | 完整牌谱 JSON |
| `created_at` | TIMESTAMP | 创建时间 |

### 6.2 `game_player_records`

每局 4 行（每位玩家一行），用于列表查询与元数据展示。

| 列 | 类型 | 说明 |
|----|------|------|
| `game_id` | VARCHAR(16) FK | 关联 `game_records` |
| `user_id` | BIGINT | 用户 ID |
| `username` | VARCHAR | 用户名 |
| `score` | INT | 终局分数 |
| `rank` | INT | 名次 1～4 |
| `rule` | VARCHAR(10) | **游戏规则**，来自 `game_title.rule`（如 `guobiao`） |
| `sub_rule` | VARCHAR(32) | 来自 `game_title.sub_rule` |
| `match_type` | VARCHAR(24) | 局数/模式（**不在 JSON 内**），完整取值见下节 |
| `title_used` / `character_used` / `profile_used` / `voice_used` | INT | 对局使用的装扮 |

**写入逻辑（以国标为例，`store_guobiao.py`）：**

```python
rule = game_title.get("rule") or "guobiao"
sub_rule = game_title.get("sub_rule") or "guobiao/standard"
```

`match_type` 由 GameState 在对局结束时传入，与 `game_title.max_round` 的数字部分一致（排位赛带 `_rank` 后缀）。

**跳过保存：** 对局含机器人（`user_id <= 10`）时不写牌谱与统计。

#### 6.2.1 `match_type` 完整取值

格式分两类：**自定义房** `{N}/4`、**国标排位** `{N}/4_rank`。`N` 即 `max_round`（风圈数），与 `game_title.max_round` 相同。

**A. 自定义房 `{max_round}/4`**

各规则 GameState 统一写入 `f"{self.max_round}/4"`（`room_type == "custom"`）：

| match_type | max_round | 风圈 | 最多局数 | 客户端显示（`RoundTextDictionary`） |
|------------|-----------|------|----------|-------------------------------------|
| `1/4` | 1 | 东 | 4 | 东风战 |
| `2/4` | 2 | 东+南 | 8 | 东南战 |
| `3/4` | 3 | 东+南+西 | 12 | 西风战 |
| `4/4` | 4 | 东+南+西+北 | 16 | 全庄战 |

适用规则：`guobiao`、`qingque`、`classical`、`riichi`。创房时 `game_round` 可选 1～4（`CreatePanel` / `room_manager`）。

**B. 国标排位 `{max_round}/4_rank`**

仅国标 `GuobiaoGameState` 在 `room_type == "match"` 时，由 `queue_type_to_match_type(match_queue_type)` 写入（`rank_calculator.py`）：

| match_type | 排位局制（`game_type`） | max_round | 客户端显示 |
|------------|-------------------------|-----------|------------|
| `1/4_rank` | 东风战（`dongfeng`） | 1 | 东风战 |
| `2/4_rank` | 半庄战（`banzhuang`） | 2 | 东南战 |
| `4/4_rank` | 全庄战（`quanzhuang`） | 4 | 全庄战 |

排位队列共 12 种 `queue_type`（`beginner` / `intermediate` / `advanced` / `mcrpl` × 上表三种局制），**映射到同一 `match_type`**，场次差异不在 `match_type` 中体现。

当前排位**无** `3/4_rank`（无「东西战」排位队列）。

**C. 与统计表 `mode` 列的关系**

`guobiao_history_stats.mode` 等统计字段使用 `{max_round}/4`（**无** `_rank` 后缀），例如排位全庄对局统计为 `4/4`，牌谱列表元数据为 `4/4_rank`。

**D. 客户端解析**

`RecordPrefab` 经 `RoundTextDictionary.GetMatchTypeDisplay(match_type)` 显示局制名：取 `/` 前数字查表，`_rank` 后缀不影响显示。

### 6.3 统计表（简要）

| 表 | `rule` 列含义 |
|----|----------------|
| `guobiao_history_stats` / `guobiao_fan_stats` | 固定 `"guobiao"` |
| `qingque_*` / `classical_*` | 对应规则名 |
| `mode` 列 | 如 `4/4`，表示局数模式 |

统计与牌谱元数据分离；列表 UI 的 `match_type` 来自 `game_player_records`，不是 `history_stats.mode`。

---

## 7. API 与字段优先级

### 7.1 记录列表 `get_record_list`

从 `game_player_records` JOIN `game_records` 聚合，返回 `RecordInfo`：

- `game_id`、`rule`、`sub_rule`、`match_type`、`created_at`
- `players[]`：排名、分数、装扮等

列表展示用 `sub_rule` + `match_type`（客户端经 `RuleNameDictionary` 显示）。

### 7.2 单条详情 `get_record_by_id`

返回 `RecordDetail`：

- `record`：完整 JSON（dict）
- `players[]`：同上
- 顶层 `rule` / `sub_rule`：**优先** `record.game_title`，缺失时回退 `game_player_records` 行

---

## 8. 客户端解析流程

```
RecordPanel / RecordPrefab
  → DataNetworkManager.GetRecordById
  → RecordPanel.OnRecordDetailReceived(detail)
  → JsonConvert.SerializeObject(detail.record)
  → GameRecordManager.LoadRecord(recordJson, detail.players)
       → GameRecordJsonDecoder.ParseGameRecord   // JSON → GameRecord
       → InitGameRound(roundIndex)               // 座位、风位、牌山
       → 逐步执行 action_ticks 驱动 3D/UI
```

### 8.1 `GameRecordJsonDecoder`

- 解析 `game_title` → `Dictionary<string, object>`
- 顺序读取 `round_index_1`, `round_index_2`, … 直到键不存在
- 每局解析 `seats` / `dealer_index` / `start_player_index` / `riichi`（立直）；**缺少 `seats` 或长度≠4 则拒绝加载**
- 解析手牌、牌山、`action_ticks`（嵌套数组转为字符串以便统一存储）
- 从和牌/流局 tick 累加 `Round.scoreChanges`（经 `seats` 转为 original 顺序）

### 8.2 `GameRecordManager.LoadRecord`

- 调用 `GameSceneUIManager.InitGameRecord()` 清空临时 UI
- 用 `game_title.p*_uid` 与 API 传入的 `players_info` 对齐 **originalPlayerIndex**
- 未传 `players_info` 时仍可从 `game_title` 取 uid

### 8.3 回放分支依赖的字段

| 用途 | 读取字段 |
|------|----------|
| 规则分支（补花起点、和牌结算、局数按钮文案） | `game_title.rule` |
| 番表、子规则差异 | `game_title.sub_rule` |
| 风圈/局名显示 | `rule` + `current_round`（`RoundTextDictionary`） |
| 立直本场显示 | `round.riichi.honba` |
| 计分板规则 | `RefreshRecordScoreTable` 等读 `game_title.rule` |

**重要：** 回放逻辑 **不** 使用 API 顶层 `RecordDetail.rule`，只解析 JSON 内的 `game_title`。

### 8.4 座位映射

`InitGameRound(roundIndex)`：

1. **座次：** `playerIndex = round.seats[originalPlayerIndex]`（唯一入口，不推理轮转/固定换位）
2. **起手行动：** 跳过前缀 `bh`/`bd` 节点后，在 `startIndex` 处将 `currentPlayerIndex` 设为 `round.startPlayerIndex`
3. **手牌映射：** 读取 `p{playerIndex}_tiles` 渲染到 3D 四方
4. **累计分数：** 前面各局 `scoreChanges`（original 顺序）累加得到进入本局前的分数
5. **tick 分数：** `score_changes` 按 player_index 写入，回放时 `MapTickScoreChangesToDeltas` 直接用 `rp.playerIndex` 取下标

`current_round` 用于 UI 显示「东1局」「南2局」等，与 `round_index`（整局序号）不同。

局头字段详见 [game_record_round_meta_examples.md](game_record_round_meta_examples.md)。

---

## 9. 示例片段（新格式国标全庄）

```json
{
  "game_title": {
    "rule": "guobiao",
    "room_type": "custom",
    "sub_rule": "guobiao/standard",
    "commitment_hex": "a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456",
    "salt": "0123456789abcdef0123456789abcdef",
    "max_round": 4,
    "hepai_limit": 8,
    "p0_uid": 10000001,
    "p0_name": "玩家A",
    "...": "..."
  },
  "game_round": {
    "round_index_1": {
      "round_index": 1,
      "current_round": 1,
      "seats": [0, 1, 2, 3],
      "dealer_index": 0,
      "start_player_index": 0,
      "p0_tiles": [11, 12, 13, "..."]
    }
  }
}
```

对应 DB 示例（同一 JSON，`match_type` 随房间类型不同）：

| 场景 | game_id | rule | sub_rule | match_type |
|------|---------|------|----------|------------|
| 自定义全庄 | `aB3xK9mN2p` | guobiao | guobiao/standard | `4/4` |
| 自定义东风 | `xY7zQ2wR8k` | guobiao | guobiao/standard | `1/4` |
| 自定义半庄 | `mN4pL6sT1v` | qingque | qingque/standard | `2/4` |
| 自定义东西 | `kJ8hG5fD3a` | classical | classical/standard | `3/4` |
| 排位全庄 | `bC9nM2xK7p` | guobiao | guobiao/standard | `4/4_rank` |
| 排位东风 | `qW3eR5tY8u` | guobiao | guobiao/standard | `1/4_rank` |
| 排位半庄 | `zX1cV4bN6m` | guobiao | guobiao/standard | `2/4_rank` |

---

## 10. 相关源码索引

| 模块 | 路径 |
|------|------|
| 牌谱写入 API | `server/gamestate/public/game_record_manager.py` |
| 国标入库 | `server/database/guobiao/store_guobiao.py` |
| 详情查询 | `server/database/db_manager.py` → `get_record_by_id` |
| JSON 解码 | `Assets/Scripts/.../GameRecordJsonDecoder.cs` |
| 回放主逻辑 | `Assets/Scripts/.../GameRecordManager.cs` |
| 数据结构 | `Assets/Scripts/.../GameRecordData.cs` |
| 加载入口 | `Assets/Scripts/UI/Panel/RecordPanel.cs` |
| API 模型 | `Assets/Scripts/Network/Serialize/Response.cs` |
