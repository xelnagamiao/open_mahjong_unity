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
| created_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 创建时间 |
| updated_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 最近更新时间 |
| PRIMARY KEY | (user_id, rule, mode) | 复合主键 | 每个用户每个规则每个模式一条记录 |

## riichi_fan_stats 立直麻将番种统计表

专门用于存储立直麻将的番种统计数据，每个用户一条记录，只存储番种累计次数。

> **注意**：立直麻将的番种字段待定义，此表结构预留。

| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| user_id | BIGINT | PRIMARY KEY, REFERENCES users(user_id) ON DELETE CASCADE | 用户 ID |
| created_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 创建时间 |
| updated_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 最近更新时间 |

