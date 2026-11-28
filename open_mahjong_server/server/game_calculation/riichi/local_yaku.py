"""本项目可选古役的牌型与事件判定。

古役不修改上游 mahjong 包的 yaku 表，避免不同房间配置互相污染。每次
计算时按当前手牌创建一次轻量 Yaku 对象，再交给现有计番与计分流程。
"""
from collections import Counter
from typing import Iterable, Mapping

from mahjong.hand_calculating.yaku import Yaku


class LocalYaku(Yaku):
    """带固定番数的项目古役。"""

    _YAKU_IDS = {
        "燕返": 1101,
        "杠振": 1102,
        "十二落抬": 1103,
        "三连刻": 1104,
        "五门齐": 1105,
        "一色三同顺": 1106,
        "大车轮": 1107,
    }

    def __init__(self, name: str, han_closed: int, han_open: int = 0, *, is_yakuman: bool = False):
        # Python 的 hash() 会按进程随机化，不能用于回放、日志或客户端缓存的稳定键。
        self.yaku_id = self._YAKU_IDS[name]
        self.name = name
        self.han_closed = han_closed
        self.han_open = han_open
        self.is_yakuman = is_yakuman

    def is_condition_met(self, *args, **kwargs) -> bool:
        return True


def _is_sequence(group: Iterable[int]) -> bool:
    tiles = tuple(group)
    return len(tiles) == 3 and tiles[1] == tiles[0] + 1 and tiles[2] == tiles[0] + 2


def _is_triplet_or_kan(group: Iterable[int]) -> bool:
    tiles = tuple(group)
    return len(tiles) >= 3 and len(set(tiles)) == 1


def _group_categories(groups: Iterable[Iterable[int]]) -> set[int]:
    categories = set()
    for group in groups:
        tiles = tuple(group)
        if not tiles:
            continue
        tile = tiles[0]
        if tile < 9:
            categories.add(0)  # 万子
        elif tile < 18:
            categories.add(1)  # 筒子
        elif tile < 27:
            categories.add(2)  # 索子
        elif tile < 31:
            categories.add(3)  # 风牌
        else:
            categories.add(4)  # 三元牌
    return categories


def _has_twelve_falling_pillows(hand, melds) -> bool:
    if len(melds) != 4 or not all(meld.opened for meld in melds):
        return False
    return sum(len(group) == 2 for group in hand) == 1


def _has_three_consecutive_triplets(hand) -> bool:
    starts_by_suit = {0: set(), 1: set(), 2: set()}
    for group in hand:
        if not _is_triplet_or_kan(group):
            continue
        tile = tuple(group)[0]
        if tile < 27:
            suit, rank = divmod(tile, 9)
            starts_by_suit[suit].add(rank)
    return any({rank, rank + 1, rank + 2} <= starts
               for starts in starts_by_suit.values()
               for rank in range(7))


def _has_three_identical_sequences(hand) -> bool:
    sequences = Counter()
    for group in hand:
        if not _is_sequence(group):
            continue
        first = tuple(group)[0]
        sequences[(first // 9, first % 9)] += 1
    return any(count >= 3 for count in sequences.values())


def _has_daisharin(tiles_34, is_open_hand: bool) -> bool:
    if is_open_hand or len(tiles_34) < 17:
        return False
    return sum(tiles_34) == 14 and all(tiles_34[tile] == 2 for tile in range(10, 17)) and all(
        tiles_34[tile] == 0 for tile in list(range(0, 10)) + list(range(17, 34))
    )


def evaluate_local_yaku(hand, tiles_34, melds, rules: Mapping[str, object], events: Mapping[str, object], is_open_hand: bool):
    """返回当前分解可添加的古役及是否应压掉一杯口/二杯口。

    `hand` 是 HandDivider 的当前分解，因此已经包含副露组。事件类古役由
    action_check 在本巡切牌时写入 `events`，结构类古役只依赖当前分解。
    """
    yaku = []
    if rules.get("local_riichi_discard_ron") and events.get("is_riichi_declaration_ron"):
        yaku.append(LocalYaku("燕返", 1, 1))
    if rules.get("local_post_kan_discard_ron") and events.get("is_post_kan_discard_ron"):
        yaku.append(LocalYaku("杠振", 1, 1))
    if rules.get("local_four_open_melds") and _has_twelve_falling_pillows(hand, melds):
        yaku.append(LocalYaku("十二落抬", 1, 1))
    if rules.get("local_three_consecutive_triplets") and _has_three_consecutive_triplets(hand):
        yaku.append(LocalYaku("三连刻", 2, 2))
    if rules.get("local_five_tile_groups") and len(_group_categories(hand)) == 5:
        yaku.append(LocalYaku("五门齐", 2, 2))
    if rules.get("local_three_identical_sequences") and _has_three_identical_sequences(hand):
        yaku.append(LocalYaku("一色三同顺", 3, 2))
    if rules.get("local_daisharin") and _has_daisharin(tiles_34, is_open_hand):
        yaku.append(LocalYaku("大车轮", 13, 0, is_yakuman=True))
    return yaku, any(item.name == "一色三同顺" for item in yaku)
