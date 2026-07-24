# 雀渣 → salasasa 牌谱转换

将 [tziakcha.net](https://tziakcha.net) 国标牌谱转为 salasasa / open_mahjong 使用的 `game_title` + `game_round` JSON。

## 依据（非推测）

解码逻辑直接对齐雀渣牌谱页内联脚本（`parse_script` / `parse_wall` / `parse_dice` / `parse_acts` / `FAN` / `S2O` / `_play` 动作分支），以及同源的 [tziakcha-fetcher](https://www.npmjs.com/package/tziakcha-fetcher) 动作位域说明。

salasasa 输出字段对齐仓库文档：

- `open_mahjong_server/server/gamestate/public/game_record_format.md`
- `game_record_example_guobiao.jsonc`

## 用法

```bash
# 本地已下载的整场 JSON（推荐，other/2u8pwkTG.json）
python convert.py ../2u8pwkTG.json -o ../2u8pwkTG.salasasa.json

# 单局
python convert.py ../wtWnLbRT.json -o ../wtWnLbRT.salasasa.json

# 在线拉取（偶发连不上时加几次重试即可）
python convert.py "https://tziakcha.net/record/?id=wtWnLbRT" --fetch -o out.json
```

## 输出说明

| 项 | 处理 |
|---|---|
| 牌 id | 雀渣 0–143 → 万饼条/字/花（注意雀渣墙序为 万→条→饼，三元 C/F/P=中/发/白 → 45/47/46） |
| 座位 | `S2O[round_i]` → `seats[original]=player_index`，庄家恒为当局 `player_index=0` |
| 动作 | 摸切杠吃碰补花按网页位域展开；吃碰写出真实手牌 id |
| 和牌 | 用 `step.b` / `step.y` / `step.s` / `FAN[]` 生成 `hu_*` + 番种列表 |
| 错和 | `b` 错和位且非和牌位时落库 `错和` 番种，无 `end` |
| uid | 雀渣短 id 映射为稳定占位 int（`p*_tziakcha_id` 保留原 id） |

## 目录

- `convert.py` — 转换器与 CLI
- 参考脚本可从雀渣页另存；本目录不捆绑站点压缩 JS
