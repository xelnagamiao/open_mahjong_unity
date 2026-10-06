# Bot API 使用说明

面向 QQ 机器人等第三方集成的查询接口。挂载在现有 `/api` 反代下，**无需额外 Nginx 配置**。

`info`、`records`、`rank-stats` 与 `scope-counts` 的**响应 JSON 结构与公开 `/api/player` 完全一致**；额外提供 `rank` 多规则评级查询。原有国标字段继续保留，新增字段可按需读取。

基础路径：

```
https://salasasa.cn/api/bot/player/
```

本地开发：

```
http://localhost:3000/api/bot/player/
```

## 鉴权

所有 `/api/bot` 请求必须在 HTTP Header 中携带 JWT：

```
Authorization: Bearer <你的 Bot API 令牌>
```

- 令牌由站点管理员使用 `scripts/issue-bot-token.js` 签发后提供，**请勿泄露**。
- 服务端通过环境变量 `BOT_API_JWT_SECRET` 校验签名；载荷须包含 `"aud": "botapi"`。
- 无效或过期令牌返回 `401`。

### 管理员签发令牌

在 `open_mahjong_web` 目录下：

```bash
# 永不过期（推荐交给长期运行的 Bot）
node scripts/issue-bot-token.js my-qq-bot

# 可选：指定有效期（秒）
node scripts/issue-bot-token.js my-qq-bot 86400
```

## 限流

生产环境下，每个 Bot 名称（JWT 中的 `bot_name`）**每分钟最多 120 次成功请求**；超限返回 `429`。

## 接口列表

| 方法 | 路径 | 对应公开 API | 说明 |
|------|------|--------------|------|
| GET | `/api/bot/player/info/:key` | `/api/player/info/:key` | 玩家信息与各规则战绩 |
| GET | `/api/bot/player/records/:key` | `/api/player/records/:key` | 对局记录列表（分页） |
| GET | `/api/bot/player/rank-stats/:key` | `/api/player/rank-stats/:key` | 顺位统计（可按 tier 筛选） |
| GET | `/api/bot/player/rank/:key` | — | **Bot 独有**：多规则段位、PT、Elo 与评级局数 |
| GET | `/api/bot/player/scope-counts/:key` | `/api/player/scope-counts/:key` | 各场次对局数，包含 Elo 场 |

`:key` 支持 **数字 user_id** 或 **用户名 username**。

### 示例文件

每个接口在 `examples/` 下提供 **请求** 与 **响应** 示例：

| 接口 | 请求示例 | 响应示例 |
|------|----------|----------|
| info | [`info.request.json`](./examples/info.request.json) | [`info.response.json`](./examples/info.response.json) |
| records | [`records.request.json`](./examples/records.request.json) | [`records.response.json`](./examples/records.response.json) |
| rank-stats | [`rank-stats.request.json`](./examples/rank-stats.request.json) | [`rank-stats.response.json`](./examples/rank-stats.response.json) |
| rank | [`rank.request.json`](./examples/rank.request.json) | [`rank.response.json`](./examples/rank.response.json) |
| scope-counts | [`scope-counts.request.json`](./examples/scope-counts.request.json) | [`scope-counts.response.json`](./examples/scope-counts.response.json) |

### info / rank 中的多规则评级

两个接口都在 `data.ratings` 返回六种独立评级，以评级规则 ID 为键：

| 键 | 展示名称 | `system` |
|----|----------|----------|
| `guobiao` | 国标 | `grade` |
| `riichi` | 四人立直 | `grade` |
| `riichi_sanma` | 三人立直 | `grade` |
| `qingque` | 青雀 | `elo` |
| `sichuan` | 川麻血战 | `elo` |
| `sichuan_xueliu_exchange` | 川麻血流换三张 | `elo` |

每项包含 `rule`、`system`、`rank_name`、`rank_score`、`games`、`updated_at`、`bounds`、`progress`；`elo` 仅在 `system=elo` 时存在。
`games` 是该评级规则的局数，各项独立累计。未参加过评级时局数为 `0`；段位规则默认为 `10级`、PT `0`，Elo 规则默认为 `1500`。
`grade` 规则展示段位、PT 和 `progress`，不返回 `elo`；`elo` 规则的 `rank_name` 为 `""`、`rank_score` 为 `0`，`bounds/progress` 为 `null`，展示 Elo。
Bot 应按 `system` 选择显示字段，不能要求每项都有 Elo。四人立直和三人立直的段位、PT、局数彼此独立；川麻血战和血流换三张的 Elo、局数也彼此独立。
国标段位/PT 以现有 `rank_data` 为准，包含管理员调整。

旧 `rank` 接口的 `guobiao_rank`、`guobiao_score`、`bounds`、`progress`，以及 `info.data.rank` 的含义和结构保持不变。

### info 中的基础统计与南雀

- 原有 `guobiao_stats`、`riichi_stats`、`qingque_stats`、`classical_stats`、`changsha_stats` 保留。
- 新增 `nanque_stats`：南雀基础统计和番种统计。统计存储沿用内部 `jiandan_*` 表，对外规则标识为 `rule=zhongyong`、`sub_rule=zhongyong/nanque`，不新增“简单麻将”规则。
- `fan_dict` 新增 `riichi` 和 `nanque` 中文番种字典。
- 基础统计行新增 `mode_fan_stats`，只统计该行 `mode` 的番种。原 `fan_stats` 仍为全历史番种合计，放在第一行；按匹配/自定义或局制筛选时请使用 `mode_fan_stats`。
- `ranked_stats` 以六种评级规则 ID 为键，各项为按 `mode` 分组的匹配统计数组。四人立直与三人立直、川麻血战与血流换三张分别统计，不混入自定义房。立直统计来自已保存的日麻统计，其他规则来自每玩家指标；缺少统计数据的历史对局可能无法补齐，与记录列表的对局数可能不同。
- `record_counts` 是各主规则的牌谱对局数；南雀记录计入 `zhongyong`。南雀记录应使用下面的子规则筛选查询。

以上 `info` 统计均为全历史数据，不接受日期或具体场次筛选。`rank-stats` 提供筛选后的结算与顺位统计。接口不提供高级分析结果。

### records / rank-stats 可选 Query 参数

`rank-stats` 的 `total_round_score` 为同一筛选范围内国标对局的结算净得分之和，除以 `total_games` 得到局均点。无对局时为 `0`；包含其他规则或缺失结算分时为 `null`，不把起始点数当作净得分。

与 `/api/player/records/:key`、`/api/player/rank-stats/:key` 相同：

| 参数 | 说明 |
|------|------|
| `limit` | 每页条数，1–50，默认 20 |
| `offset` | 偏移，默认 0 |
| `rule` | 主规则，如 `guobiao`、`riichi`、`qingque`、`sichuan`、`zhongyong` |
| `sub_rule` | 子规则，如 `riichi/standard`、`riichi/sanma`、`sichuan/standard`、`sichuan/xueliu_exchange`、`zhongyong/nanque` |
| `tier` | 场次：`rank`、`custom`、`events`、`beginner`、`intermediate`、`advanced`、`mcrpl`、`elo` |
| `event_id` | 具体赛事 ID（配合 `tier=events`） |
| `room_type` | 房间类型 |
| `match_tier` | 匹配档位 |
| `game_type` | 局制：`dongfeng`、`banzhuang`、`xifeng`、`quanzhuang` |
| `date_from` / `date_to` | 时间范围（ISO 8601） |

`tier=elo` 同时限定 `room_type=match` 和 `match_tier=elo`，不会包含自定义房。
`match_tier` 可与 `tier=rank` 或 `room_type=match` 同时使用。
`scope-counts` 接受 `rule`、`sub_rule`、`game_type`、`date_from`、`date_to`，返回 `rank`、`custom`、`beginner`、`intermediate`、`advanced`、`mcrpl`、`elo`、`events` 的对局数。
`rank` 已包含各具体匹配场的总数，不要与具体场次的数量相加。

评级键与记录的主规则/子规则对应如下。`records`、`rank-stats`、`scope-counts` 使用后两列筛选：

| 评级键 | `rule` | `sub_rule` |
|--------|--------|------------|
| `riichi` | `riichi` | `riichi/standard` |
| `riichi_sanma` | `riichi` | `riichi/sanma` |
| `sichuan` | `sichuan` | `sichuan/standard` |
| `sichuan_xueliu_exchange` | `sichuan` | `sichuan/xueliu_exchange` |

例如三麻东风排位使用 `rule=riichi&sub_rule=riichi/sanma&tier=rank&game_type=dongfeng`；血流换三张使用 `rule=sichuan&sub_rule=sichuan/xueliu_exchange&tier=elo`。仅指定 `rule=riichi` 或 `rule=sichuan` 会包含该主规则下的多个子规则。
`game_type=dongfeng`、`banzhuang` 同时支持四人和三人局制；三麻记录的 `match_type` 分别为 `1/4_sanma_rank`、`2/4_sanma_rank`（自定义房没有 `_rank` 后缀）。三麻顺位只有一至三位，`fourth_place_count` 为 `0`。

### 对局记录中的 PT 变更

`GET /api/bot/player/records/:key` 与公开 records 接口均在
`data.items[].players[].pt_change` 返回该玩家在该局**已保存的结算 PT 变更**。

- 类型为 JSON `number | null`，例如 `11.76`、`0`、`-5.15`。
- `0` 表示已保存且实际没有 PT 变化；`null` 表示未保存或该对局不适用 PT，不能作为 0 使用。
- 不在查询时按当前段位重新计算，也不为读取 PT 获取完整牌谱 JSON。
- 查询玩家本人的 PT 时，以 `players[].user_id` 匹配该玩家；同桌玩家也有各自的 `pt_change`。

### 对局记录中的评级变化

`data.items[].players[]` 新增以下字段，均来自该局保存的结算结果：

| 字段 | 含义 |
|------|------|
| `rating_rule` / `rating_system` | 评级规则与体系（`grade` 或 `elo`）；未保存时为 `null` |
| `rating_pt` | 国标、四人立直或三人立直的本局 PT 加扣，`number | null` |
| `rank_before` / `rank_after` | 对局前后段位，`string | null` |
| `score_before` / `score_after` | 对局前后段位 PT 余额，`number | null` |
| `elo_before` / `elo_after` / `elo_delta` | 对局前后 Elo 与本局 Elo 加扣，`number | null` |
| `rating_games` | 本局结束后的该规则评级局数，`number | null` |

`pt_change` 优先使用已保存的 PT 字段；国标/立直未保存该字段时，读取本局结算中保存的 `rating_pt`。
青雀、川麻血战、川麻血流换三张的 PT 不适用，`rating_pt` 和 `pt_change` 为 `null`，应展示 `elo_delta`。国标、四人立直、三人立直的 `elo_before`、`elo_after`、`elo_delta` 为 `null`。
自定义房与未保存评级结算的历史对局，新增评级字段为 `null`；原来保存的国标 `pt_change` 仍保留。
`score_after - score_before` 可能跨越升降段，不等于 `rating_pt`。接口只按分页读取结算片段，不下载或返回完整牌谱，不按当前评级推算历史变化。
Bot 直接显示保存的 `rating_pt` 或 `elo_delta` 即可；四人立直、三人立直和 Elo 的计分公式由游戏服务执行，Bot 无需增加公式计算或高级分析。

## 错误响应

| HTTP | 含义 |
|------|------|
| 401 | 缺少或无效的 Bot API 令牌 |
| 404 | 用户不存在 |
| 429 | 请求过于频繁 |
| 500 | 服务器内部错误 |

```json
{ "success": false, "message": "错误说明" }
```

## curl 示例

```bash
TOKEN="你的JWT"

curl -s -H "Authorization: Bearer $TOKEN" \
  "https://salasasa.cn/api/bot/player/info/10000001"

curl -s -H "Authorization: Bearer $TOKEN" \
  "https://salasasa.cn/api/bot/player/records/10000001?tier=rank&limit=5"

curl -s -H "Authorization: Bearer $TOKEN" \
  "https://salasasa.cn/api/bot/player/rank-stats/10000001?tier=intermediate&rule=guobiao"

curl -s -H "Authorization: Bearer $TOKEN" \
  "https://salasasa.cn/api/bot/player/rank/10000001"

curl -s -H "Authorization: Bearer $TOKEN" \
  "https://salasasa.cn/api/bot/player/records/10000001?rule=qingque&tier=elo&limit=5"

curl -s -H "Authorization: Bearer $TOKEN" \
  "https://salasasa.cn/api/bot/player/records/10000001?rule=zhongyong&sub_rule=zhongyong/nanque"

curl -s -H "Authorization: Bearer $TOKEN" \
  "https://salasasa.cn/api/bot/player/scope-counts/10000001?rule=qingque"
```

## 与 `/api/player` 的区别

| 项目 | `/api/player` | `/api/bot/player` |
|------|---------------|-------------------|
| 鉴权 | 无 | JWT |
| info / records / rank-stats / scope-counts 响应 | 基准 | **相同** |
| rank（多规则评级） | 无 | 有 |
| 限流 | 按 IP 30 次/分钟 | 按 Bot 名 120 次/分钟 |

## 环境变量

| 变量 | 说明 |
|------|------|
| `BOT_API_JWT_SECRET` | **必填**，HS256 签名密钥 |

## 部署说明

Bot API 与 `/api/player` 等同走现有 `location /api/` 反代，部署 Node 新版本并配置 `BOT_API_JWT_SECRET` 即可，**无需修改 Nginx**。

评级和南雀统计使用游戏服务已有的数据库表，请先部署包含六种独立评级及南雀统计的 Python 版本，再更新 Node。
当前 Python 版本首次启动会按新 Elo 算法执行一次历史重算，并清除段位规则的旧 Elo；各评级池独立处理，段位与 PT 保留。重算在事务中保存备份及完成标记，失败时回滚并中止启动，成功后再次启动会跳过。更新前应备份数据库并检查启动日志。
Bot 查询只读取当前评级和已保存的结算片段，不触发重算；无法补齐的历史结算字段仍为 `null`。
