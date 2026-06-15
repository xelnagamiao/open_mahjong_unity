# 立直麻将数据库表设计

## riichi_history_stats 立直麻将对局统计表

专门用于立直麻将（riichi）规则的基础统计数据，按照 `rule`（规则，如 riichi）与 `mode`（模式，如 `1/4`、`2/4`、`3/4`、`4/4`、`1/4_rank` 等）区分不同维度。客户端展示排行榜/统计时需与 `riichi_fan_stats` 表关联查询。

| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| user_id | BIGINT | NOT NULL REFERENCES users(user_id) ON DELETE CASCADE | 用户 ID |
| rule | VARCHAR(10) | NOT NULL | 规则标识（riichi） |
| mode | VARCHAR(20) | NOT NULL | 数据模式（`1/4`、`2/4`、`3/4`、`4/4`、`1/4_rank` 等） |
| total_games | INT | NOT NULL DEFAULT 0 | 总对局数 |
| total_rounds | INT | NOT NULL DEFAULT 0 | 累计回合数 |
| win_count | INT | NOT NULL DEFAULT 0 | 和牌次数 |
| self_draw_count | INT | NOT NULL DEFAULT 0 | 自摸次数 |
| deal_in_count | INT | NOT NULL DEFAULT 0 | 放铳次数 |
| total_fan_score | INT | NOT NULL DEFAULT 0 | 累计番数 |
| total_win_turn | INT | NOT NULL DEFAULT 0 | 累计和巡（用于计算平均巡目） |
| total_fangchong_score | INT | NOT NULL DEFAULT 0 | 累计放铳分 |
| first_place_count | INT | NOT NULL DEFAULT 0 | 一位次数 |
| second_place_count | INT | NOT NULL DEFAULT 0 | 二位次数 |
| third_place_count | INT | NOT NULL DEFAULT 0 | 三位次数 |
| fourth_place_count | INT | NOT NULL DEFAULT 0 | 四位次数 |
| fulu_round_count | INT | NOT NULL DEFAULT 0 | 副露局数 |
| created_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 创建时间 |
| updated_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 最近更新时间 |
| PRIMARY KEY | (user_id, rule, mode) | 复合主键 | 每个用户每个规则每个模式一条记录 |

## riichi_fan_stats 立直麻将役种统计表

基于 `FanTextDictionary.FanToDisplayRiichi` 与 `FanToDisplayRiichiInactive` 的役名，记录各役在和牌中的出现次数（不含错和）。

| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| user_id | BIGINT | NOT NULL REFERENCES users(user_id) ON DELETE CASCADE | 用户 ID |
| rule | VARCHAR(10) | NOT NULL | 规则标识（riichi） |
| mode | VARCHAR(20) | NOT NULL | 数据模式 |
| riichi … sashikomi | INT | DEFAULT 0 | 各役累计次数（见 store_riichi.py `FAN_NAME_TO_FIELD`） |
| created_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 创建时间 |
| updated_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 最近更新时间 |
| PRIMARY KEY | (user_id, rule, mode) | 复合主键 | |

**说明：**

- 宝牌 / 里宝牌 / 赤宝牌支持 `宝牌*N` 形式累加。
- 错和不计入役种统计。
- 门清/食下变体（如 `一气通贯（门清）`）单独计列。
