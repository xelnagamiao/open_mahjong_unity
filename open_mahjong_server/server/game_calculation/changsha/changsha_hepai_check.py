"""长沙麻将和牌检测及基础计分。

牌组只使用万、筒、条一至九。小胡要求二五八作将，大胡按命中的牌型权重累加。
扎鸟归属、付款玩家和最终倍数由长沙对局状态负责。
"""
from typing import Dict, Iterable, List, Tuple


VALID_TILES = tuple(
    list(range(11, 20)) + list(range(21, 30)) + list(range(31, 40))
)
JIANG_RANKS = {2, 5, 8}

BIG_HU_NAMES = {
    "碰碰胡",
    "将将胡",
    "清一色",
    "全求人",
    "七小对",
    "豪华七小对",
    "双豪华七小对",
    "三豪华七小对",
    "天胡",
    "地胡",
    "海底",
    "杠上开花",
    "杠上炮",
    "抢杠胡",
}

BIG_HU_WEIGHTS = {
    "豪华七小对": 2,
    "双豪华七小对": 3,
    "三豪华七小对": 4,
}

INITIAL_HU_NAMES = {
    "siXi": "四喜",
    "banBanHu": "板板胡",
    "queYiSe": "缺一色",
    "liuLiuShun": "六六顺",
    "sanTong": "三同",
}


def _rank(tile: int) -> int:
    return tile % 10


def _suit(tile: int) -> int:
    return tile // 10


def _is_jiang(tile: int) -> bool:
    return _rank(tile) in JIANG_RANKS


def _count_tiles(tiles: Iterable[int]) -> Dict[int, int]:
    counts: Dict[int, int] = {}
    for tile in tiles:
        counts[tile] = counts.get(tile, 0) + 1
    return counts


def evaluate_changsha_initial_hu(tiles: List[int]) -> List[str]:
    counts = _count_tiles(tiles)
    if not counts or any(tile not in VALID_TILES for tile in tiles):
        return []

    result: List[str] = []
    if any(count >= 4 for count in counts.values()):
        result.append(INITIAL_HU_NAMES["siXi"])
    if all(not _is_jiang(tile) for tile in tiles):
        result.append(INITIAL_HU_NAMES["banBanHu"])
    if len({_suit(tile) for tile in tiles}) < 3:
        result.append(INITIAL_HU_NAMES["queYiSe"])
    if sum(1 for count in counts.values() if count >= 3) >= 2:
        result.append(INITIAL_HU_NAMES["liuLiuShun"])
    if any(
        all(counts.get(suit * 10 + rank, 0) >= 2 for suit in (1, 2, 3))
        for rank in range(1, 10)
    ):
        result.append(INITIAL_HU_NAMES["sanTong"])
    return result


def changsha_initial_hu_reveal_tiles(tiles: List[int], hu_types: List[str]) -> List[int]:
    """返回起手胡结算时需要亮出的最小牌组。"""
    hand = list(tiles or [])
    hu_type_set = set(hu_types or [])
    # 板板胡和缺一色需要整手牌才能表达牌型，不能裁成固定子集。
    if not hand or hu_type_set.intersection({INITIAL_HU_NAMES["banBanHu"], INITIAL_HU_NAMES["queYiSe"]}):
        return hand

    counts = _count_tiles(hand)
    required_counts: Dict[int, int] = {}

    if INITIAL_HU_NAMES["siXi"] in hu_type_set:
        quad_tile = next((tile for tile in sorted(counts) if counts[tile] >= 4), None)
        if quad_tile is not None:
            required_counts[quad_tile] = max(required_counts.get(quad_tile, 0), 4)

    if INITIAL_HU_NAMES["liuLiuShun"] in hu_type_set:
        triplet_tiles = [tile for tile in sorted(counts) if counts[tile] >= 3][:2]
        for tile in triplet_tiles:
            required_counts[tile] = max(required_counts.get(tile, 0), 3)

    if INITIAL_HU_NAMES["sanTong"] in hu_type_set:
        matching_rank = next(
            (
                rank
                for rank in range(1, 10)
                if all(counts.get(suit * 10 + rank, 0) >= 2 for suit in (1, 2, 3))
            ),
            None,
        )
        if matching_rank is not None:
            for suit in (1, 2, 3):
                tile = suit * 10 + matching_rank
                required_counts[tile] = max(required_counts.get(tile, 0), 2)

    if not required_counts:
        return hand

    reveal_tiles = []
    # 同一张牌同时命中多个牌型时取最大需求数，避免重复亮出不存在的牌。
    remaining_counts = dict(required_counts)
    for tile in hand:
        if remaining_counts.get(tile, 0) <= 0:
            continue
        reveal_tiles.append(tile)
        remaining_counts[tile] -= 1
    return reveal_tiles


def _expand_meld(comb: str) -> List[int]:
    if not comb:
        return []
    sign = comb[0]
    try:
        tile = int(comb[1:])
    except ValueError:
        return []
    if sign in ("g", "G"):
        return [tile] * 4
    if sign in ("k", "K"):
        return [tile] * 3
    if sign in ("s", "S"):
        return [tile - 1, tile, tile + 1]
    if sign == "q":
        return [tile] * 2
    return []


def _expand_all(hand_list: List[int], tiles_combination: List[str]) -> List[int]:
    tiles = list(hand_list)
    for comb in tiles_combination:
        tiles.extend(_expand_meld(comb))
    return tiles


def _first_remaining(counts: Dict[int, int]):
    for tile in sorted(counts):
        if counts.get(tile, 0) > 0:
            return tile
    return None


def _decrement(counts: Dict[int, int], tile: int, amount: int) -> None:
    next_count = counts.get(tile, 0) - amount
    if next_count <= 0:
        counts.pop(tile, None)
    else:
        counts[tile] = next_count


def _can_form_sets(counts: Dict[int, int], sets_needed: int, triplets_only: bool = False) -> bool:
    if sets_needed == 0:
        return all(v == 0 for v in counts.values())

    tile = _first_remaining(counts)
    if tile is None:
        return False

    if counts.get(tile, 0) >= 3:
        triplet_counts = dict(counts)
        _decrement(triplet_counts, tile, 3)
        if _can_form_sets(triplet_counts, sets_needed - 1, triplets_only):
            return True

    if triplets_only:
        return False

    if tile in VALID_TILES and _rank(tile) <= 7:
        sequence = (tile, tile + 1, tile + 2)
        if all(_suit(t) == _suit(tile) and counts.get(t, 0) > 0 for t in sequence):
            seq_counts = dict(counts)
            for seq_tile in sequence:
                _decrement(seq_counts, seq_tile, 1)
            if _can_form_sets(seq_counts, sets_needed - 1, triplets_only):
                return True

    return False


def _is_standard_shape(hand_list: List[int], tiles_combination: List[str], *, jiang_pair: bool = False) -> bool:
    sets_needed = 4 - len(tiles_combination)
    if sets_needed < 0 or len(hand_list) != sets_needed * 3 + 2:
        return False
    counts = _count_tiles(hand_list)
    for tile, count in list(counts.items()):
        if count < 2:
            continue
        if jiang_pair and not _is_jiang(tile):
            continue
        remaining = dict(counts)
        _decrement(remaining, tile, 2)
        if _can_form_sets(remaining, sets_needed):
            return True
    return False


def _is_all_triplets(hand_list: List[int], tiles_combination: List[str]) -> bool:
    if any(comb and comb[0] in ("s", "S") for comb in tiles_combination):
        return False
    sets_needed = 4 - len(tiles_combination)
    if sets_needed < 0 or len(hand_list) != sets_needed * 3 + 2:
        return False
    counts = _count_tiles(hand_list)
    for tile, count in list(counts.items()):
        if count < 2:
            continue
        remaining = dict(counts)
        _decrement(remaining, tile, 2)
        if _can_form_sets(remaining, sets_needed, triplets_only=True):
            return True
    return False


def _seven_pairs_type(hand_list: List[int], tiles_combination: List[str]) -> str:
    if tiles_combination or len(hand_list) != 14:
        return "none"
    counts = list(_count_tiles(hand_list).values())
    if not counts or any(c not in (2, 4) for c in counts):
        return "none"
    if sum(c // 2 for c in counts) != 7:
        return "none"
    quad_count = sum(1 for c in counts if c == 4)
    if quad_count >= 3:
        return "triple_luxury"
    if quad_count == 2:
        return "double_luxury"
    if quad_count == 1:
        return "luxury"
    return "normal"


def _has_winning_shape(hand_list: List[int], tiles_combination: List[str]) -> bool:
    return (
        _is_standard_shape(hand_list, tiles_combination, jiang_pair=False)
        or _seven_pairs_type(hand_list, tiles_combination) != "none"
    )


def _all_jiang_tiles(all_tiles: List[int]) -> bool:
    return bool(all_tiles) and all(_is_jiang(tile) for tile in all_tiles)


def _is_flush(all_tiles: List[int]) -> bool:
    suits = {_suit(tile) for tile in all_tiles}
    return len(suits) == 1


def _is_quanqiuren(hand_list: List[int], tiles_combination: List[str]) -> bool:
    return len(tiles_combination) == 4 and _is_standard_shape(
        hand_list,
        tiles_combination,
        jiang_pair=False,
    )


def _append_context_fans(names: List[str], has_shape: bool, way_to_hepai: List[str]) -> None:
    if not has_shape:
        return
    token_to_name = (
        (("天胡", "天和"), "天胡"),
        (("地胡", "地和"), "地胡"),
        (("海底", "海底捞月", "海底漫游", "河底捞鱼"), "海底"),
        (("杠上开花", "杠上花"), "杠上开花"),
        (("杠上炮",), "杠上炮"),
        (("抢杠胡", "抢杠和", "抢杠"), "抢杠胡"),
    )
    for tokens, fan_name in token_to_name:
        if fan_name not in names and any(token in way_to_hepai for token in tokens):
            names.append(fan_name)


def changsha_base_from_fans(
    fan_list: List[str],
    dealer_related: bool = False,
    *,
    small_hu_score: int = 2,
    big_hu_score: int = 8,
    base_score_no_dealer: bool = False,
) -> int:
    """计算单个付款方在扎鸟翻倍前应付的长沙基础分。"""
    if not fan_list:
        return 0
    big_count = sum(BIG_HU_WEIGHTS.get(name, 1) for name in fan_list if name in BIG_HU_NAMES)
    if big_count > 0:
        if base_score_no_dealer:
            return max(1, int(big_hu_score)) * big_count
        return (7 if dealer_related else 6) * big_count
    if "小胡" in fan_list:
        if base_score_no_dealer:
            return max(1, int(small_hu_score))
        return 2 if dealer_related else 1
    return 0


class Changsha_Hepai_Check:
    def __init__(self, debug: bool = False):
        self.debug = debug

    def hepai_check(
        self,
        hand_list: List[int],
        tiles_combination: List[str],
        way_to_hepai: List[str],
        get_tile: int,
    ) -> Tuple[int, List[str]]:
        concealed = list(hand_list)
        melds = list(tiles_combination)
        ways = list(way_to_hepai or [])
        all_tiles = _expand_all(concealed, melds)

        if any(tile not in VALID_TILES for tile in all_tiles):
            return 0, []

        small_hu = _is_standard_shape(concealed, melds, jiang_pair=True)
        jiangjiang_hu = _all_jiang_tiles(all_tiles)
        has_shape = _has_winning_shape(concealed, melds) or jiangjiang_hu
        big_names: List[str] = []

        if has_shape and _is_all_triplets(concealed, melds):
            big_names.append("碰碰胡")
        if jiangjiang_hu:
            big_names.append("将将胡")
        if has_shape and _is_flush(all_tiles):
            big_names.append("清一色")
        if has_shape and _is_quanqiuren(concealed, melds):
            big_names.append("全求人")

        seven_pairs = _seven_pairs_type(concealed, melds)
        if seven_pairs == "normal":
            big_names.append("七小对")
        elif seven_pairs == "luxury":
            big_names.append("豪华七小对")
        elif seven_pairs == "double_luxury":
            big_names.append("双豪华七小对")
        elif seven_pairs == "triple_luxury":
            big_names.append("三豪华七小对")

        _append_context_fans(big_names, has_shape, ways)

        if big_names:
            return changsha_base_from_fans(big_names), big_names
        if small_hu:
            return changsha_base_from_fans(["小胡"]), ["小胡"]
        return 0, []
