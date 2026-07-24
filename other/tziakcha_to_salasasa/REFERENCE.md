# 雀渣脚本对照摘录

以下常量与函数语义来自 `https://tziakcha.net/record/?id=...` 页面内联脚本（抓取日 2026-07-23），供维护转换器时核对，避免“猜字段”。

## 动作类型 `combined & 15`

| 值 | 含义 |
|---|---|
| 0 | 开始出牌 / none |
| 1 | 补花（含换牌） |
| 2 | 切牌 |
| 3 | 吃 |
| 4 | 碰 |
| 5 | 杠（含加杠 `data&0x0300==0x0300`） |
| 6 | 和 |
| 7 | 摸牌（`data&0x100` 逆向/岭上） |
| 8 | 过 |
| 9 | 弃 |

`player = (combined>>4)&3`

## 切牌 data

- `tile = data & 0xff`
- 手打：`(data>>8)&1 == 1`（salasasa `"F"`）；否则摸打 `"T"`
- 模式：`(data>>9)&3` → 0 普通 / 1 自动 / 2 强制

## 吃 data

- `tl = (data&0x3f)<<2`
- `offer = (data>>6)&3`：默认/1 → 叫牌为最低（cl）；2 中间（cm）；3 最高（cr）
- 三张候选：`tl-4+(>>10&3)`, `tl+(>>12&3)`, `tl+4+(>>14&3)`

## 牌墙

- `w`：288 hex 字符，144 张，id 0–143
- 顺序：万 0–35，条 36–71，饼 72–107，东南西北 108–123，中发白(C/F/P) 124–135，花 136–143
- 骰子打断与发牌见 `tziakcha-fetcher` `setupWallAndDeal`（与网页 dice/pick 等价）

## 终局

- `b & 0x0f` 和牌座位掩码
- `(b>>4)&0x0f` 点炮座位掩码
- `(b>>8)&0x0f` 错和座位掩码
- `s[4]` 本盘分差
- `y[seat].f` 番数；`y[seat].t[fanIndex] = lo番 | ((mul-1)<<8)`
- `FAN[]` 81 番名表与网页一致

## API

- `POST /_qry/record/` body `id=...` → `{script}` zlib+base64
- `POST /_qry/game/?id=...` → session + records[]
