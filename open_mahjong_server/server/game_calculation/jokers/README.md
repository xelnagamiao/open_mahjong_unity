# 共用癞子结构求解器（API 1.0）

本包只判断牌形并输出可计分的真实拆分。起和条件、软硬和、番数、财神锁、宝牌可见性、动作与支付由各地方规则负责。服务端为权威；客户端和牌谱不得另造一套判和算法。

## 牌的三种身份

- **实体牌**使用项目协议码：万 `11..19`、筒 `21..29`、索 `31..39`、东南西北中白发 `41..47`；特别注意 **白板是 46，发财是 47**。花 `51..58` 仅在显式加入 `physical_tiles` 后可用。
- **自然身份**为实体牌经过 `natural_map` 的固定映射。例如温州白板代表财神原牌。
- **逻辑身份**为本次拆分中实际代表的牌。癞子保留实体码和自然身份，不能事先永久改成它所替代的牌。

默认每种普通实体牌四张，每种花一张。实体库存检查包括手牌与传入的副露。`closed_cap=4` 则仅限制闭手的逻辑同牌数；它不包含已副露的逻辑牌。听牌返回可实际摸入的实体牌，不能生成第五张实体牌。

## 用法

```python
from game_calculation.jokers import (
    JokerPolicy, TILES, can_win, structural_waits, iter_winning_shapes,
)

policy = JokerPolicy(joker_tiles=(46,), shapes=("standard", "seven_pairs"))
hand = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41, 45, 46]
assert can_win(hand, policy=policy)
waits = structural_waits(hand[:-1], policy=policy)
for shape in iter_winning_shapes(hand, policy=policy, winning_index=13):
    # 计分器应取合法候选的最高分，或证明若干候选的计分等价后归并。
    print(shape.kind, shape.assignment, shape.winning_group)
```

`JokerPolicy` 是不可变且可哈希的配置；输入列表会转成元组。字段如下：

| 字段 | 默认值及语义 |
| --- | --- |
| `joker_tiles` | `()`；作为万能资源的实体牌种 |
| `natural_map` | `()`；`((实体码, 自然码), ...)`，适用于固定牌面映射 |
| `logical_tiles` | `TILES`；可组成牌形的普通牌域 |
| `physical_tiles` | `TILES`；本规则实体牌域 |
| `physical_limits` | `()`；`((实体码, 张数), ...)`，覆盖默认库存 |
| `joker_targets` | `None`；所有 `logical_tiles` 均可替代，也可指定受限域 |
| `meld_count` | `4`；完整普通形的面子数，支持 `0..5`，温州设为 `5` |
| `closed_cap` | `None`；闭手逻辑同牌上限，广东/红中设为 `4` |
| `shapes` | `("standard",)`；可选 `seven_pairs`、`eight_pairs`、`thirteen_orphans` |
| `pair_tiles` | `None`；普通形与十三幺雀头可用牌种；不限制七对/八对中的每一对 |

同一 policy 的所有癞子共用 `joker_targets`。不支持不同癞子种类分别具有不同替代域；长春一条特殊杠也不应加入普通万能判和。

## 稳定接口

- `can_win(hand, melds=(), policy=None) -> bool`：`hand` 包括和牌张，不含副露。错误牌数、实体超量或无解返回 `False`。
- `structural_waits(hand, melds=(), policy=None) -> frozenset[int]`：输入摸牌前手牌，输出结构听牌，不包含地方规则的行动/起和过滤。
- `iter_winning_shapes(hand, melds=(), policy=None, winning_tile=None, *, winning_index=None, equivalence="full")`：默认惰性遍历所有合法拆分。`winning_index` 是输入手牌位置；仅给 `winning_tile` 时采用最后一个同码位置；两者都不给时不指定和牌组件。非法手牌返回空迭代器。
- `can_form_melds(hand, melds=(), policy=None, *, meld_count=None) -> bool`：无雀头的完整面子形，用于爆头的预摸牌检查。
- `can_form_pairs(hand, pair_count, policy=None) -> bool`：`0..8` 对，无单张。四同牌可作两对；是否算豪华七对由计分器根据实体牌决定。
- `clear_caches()`、`cache_info()`：验证/性能诊断接口。不要在每个业务请求前清缓存。

`eight_pairs` 为十七张的八对加一单张；`seven_pairs` 为十四张七对。特型必须无副露。`thirteen_orphans` 要求完整幺九字域。

副露接受既有字符串 `s12`（中心二万的顺子）、`k11`（碰一万）、`g11`（明杠）、`G11`（暗杠），或 `ExternalMeld(kind, physical, logical=(), concealed=False)`。显式副露的 `physical` 与 `logical` 逐位置对应，核心验证固定替代合法性。是否允许吃碰财神仍由动作规则负责。

## 拆分结果

`WinningShape` 含 `kind`、`groups`、`assignment`、`joker_uses`、`winning_group`、`winning_index`，以及向后兼容的 `external_joker_uses`。

- `assignment[i]` 对应原手牌第 `i` 张的逻辑身份。
- `ResolvedGroup` 含 `kind`、`logical`、`physical`、`indices`、`external`、`concealed`。副露排在前面，副露索引为 `-1`；其他索引恰好覆盖原闭手的每张牌。
- `JokerUse(index, physical, natural, logical)` 的 `substituted` 标明本次是否改变自然身份。
- `external_joker_uses` 保留副露中的固定癞子替代，`index=-1` 表示不在闭手索引内；具体组与位置可由 `groups` 对照。
- `is_natural` 说明本候选中闭手与副露的鬼均未改变自然身份；地方硬和可能还需独立的规则条件。
- `closed_logical_tiles` 排除副露；`logical_tiles` 包含副露；`pair` 仅在恰有一个对子时返回该牌，否则为 `0`。
- `concealed_pungs(self_draw)` 在点和时扣除和牌张所在刻子；原有暗杠仍计为暗刻。

相同非和牌实体在同组内的排列会归并；保留所有影响计分的癞子目标/所在组以及和牌组件。没有固定候选数量上限。调用方不可随意截断后声称取了最高分。

当计分已被证明**只依赖牌型、逻辑组模板及和牌张的逻辑身份**时，可显式传 `equivalence="logical_structure"`。该模式保留每个逻辑模板和每种可实现的和牌逻辑身份，每项提供一份合法实体分配；它不保留同一特征下的其他和牌组件或非和牌鬼所在组。例如 MIL 广东十九番的计分投影可用该模式。依赖和牌所在组、各组使用鬼的数量、不同实体鬼分配、或需遍历硬和身份的其他规则，应继续使用默认 `full`，不能直接套用此优化。无效 `equivalence` 值会抛出 `ValueError`。

## 设计、来源与测试

普通形按三个花色与字牌分组，以牌序、未完成顺子、雀头及癞子精确消耗的有限状态 DP 求解。没有逻辑张数上限、替代域或雀头限制时，结构判断使用最低资源快路：余下每三个癞子可组成额外面子；七对/八对则按奇数牌种与鬼的奇偶资源判断。其余政策仍走精确资源位集。结构缓存大小有界，计分时才生成实体拆分。每次普通形枚举内按花色、鬼数和将状态复用组模板，枚举结束即释放该临时缓存，不全局缓存完整计分见证。支持五面子，不依赖仅覆盖十四张的预计算表。配置、替代域与逻辑上限都进入缓存键。

实现由本项目独立编写。研究与交叉校验参考 [Sanbaiman 在线计算器](http://mahjong.sanbaiman.com/) 与 [esrrhs/majiang_algorithm](https://github.com/esrrhs/majiang_algorithm)；未复制网站源码或外部牌表。网站 `glmt=4` 与 MIL 闭手逻辑四张限制语义不同，不能直接作为同一规则的 oracle。GitHub 原版表在十七张部分手牌上也不适用。

从 `open_mahjong_server/server` 运行：

```console
python -m pytest game_calculation/jokers -q -p no:cacheprovider
```

测试含原书逻辑第五张反例、十七张、实体库存与副露边界、受限替代、固定白板映射、和牌组件和独立小癞子暴力对照。各规则必须继续验证自身的动作、计分和牌谱；结构测试不代表整个麻将规则通过。
