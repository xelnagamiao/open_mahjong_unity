# Jiandan Fan Scoring

Chinese version: [简单番种计分表](fan_scoring.zh-CN.md)

Per-fan detail notes: [Jiandan Fan Details](fan_details.md)

## Fan Table

| 1 point | 3 points | 8 points | 20 points |
| --- | --- | --- | --- |
| 二风刻 | 小三风 | 大三风 | 小四喜 / 大四喜 |
| 中 / 发 / 白 | 二元刻 | 小三元 | 大三元 |
| 断幺九 | 一气通贯 / 混全带幺 | 混幺九 / 纯全带幺 | 清幺九 / 十三幺九 |
|  | 混一色 | 清一色 | 字一色 / 九莲宝灯 |
| 二色同刻 | 小三色同刻 / 三色同顺 | 三色同刻 |  |
| 门前清 |  |  | 天和 / 地和 |
| 平和 | 对对和 / 七对子 |  |  |
| 海底捞月 / 河底捞鱼 |  |  |  |
| 杠上开花 / 抢杠 |  |  |  |
| 二暗刻 |  | 三暗刻 | 四暗刻 |
| 二连刻 |  | 三连刻 | 四连刻 |
| 一般高 |  | 二般高 / 一色三同顺 | 一色四同顺 |
| 一杠 | 二杠 | 三杠 | 四杠 |

## Scoring Formula

Completed winning shapes may score 0 points if they match no fan in the table.
Such hands are legal wins and end the hand, but produce no point transfer.

For rows other than the three-suit-number row, each row only scores one pattern.

The three-suit-number row (`二色同刻`, `小三色同刻`, `三色同顺`, `三色同刻`) is scored by resolved winning-structure units: a sequence, triplet, declared kong, or pair may participate in at most one pattern in this row. Score the highest total from non-overlapping patterns. This permits, for example, `小三色同刻` plus a disjoint `二色同刻`.

When the winning pattern is worth `X` points, where `X` is capped at 20:

- Discard win: `6X`
- Self-draw: each of the other three players pays `2X`

## Dragon Triplets

`中 / 发 / 白` means each individual dragon triplet is a separate 1-point pattern.

Because the dragon row only scores one pattern:

- One dragon triplet scores `中 / 发 / 白` as 1 point.
- Two dragon triplets score `二元刻` as 3 points instead of scoring two separate 1-point patterns.
- Two dragon triplets plus a dragon pair score `小三元` as 8 points.
- Three dragon triplets score `大三元` as 20 points.
