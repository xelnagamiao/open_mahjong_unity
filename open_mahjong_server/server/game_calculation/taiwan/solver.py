"""十六张麻将结构上的台湾牌形拆分与听牌枚举。"""

from collections import Counter
from functools import lru_cache
from typing import Iterable, List, Optional, Sequence, Set, Tuple

from ..hand_structure import SIXTEEN_TILE_MAHJONG
from .rules import Decomposition, Meld, STRUCTURE_TILES, TaiwanRules


HAND_STRUCTURE = SIXTEEN_TILE_MAHJONG


def is_structure_tile(tile: int) -> bool:
    return tile in STRUCTURE_TILES


def is_number_tile(tile: int) -> bool:
    return 11 <= tile <= 39 and tile // 10 in (1, 2, 3) and 1 <= tile % 10 <= 9


def parse_meld_code(code: str) -> Meld:
    if not isinstance(code, str) or len(code) < 2:
        raise ValueError(f"无效组合码: {code!r}")
    sign = code[0]
    try:
        tile = int(code[1:])
    except ValueError as exc:
        raise ValueError(f"无效组合码: {code!r}") from exc
    if sign == "s":
        if not is_number_tile(tile) or tile % 10 not in range(2, 9):
            raise ValueError(f"顺子组合码的中张非法: {code!r}")
        return Meld("sequence", tile, False, code, True)
    if tile not in STRUCTURE_TILES:
        raise ValueError(f"刻子或杠子的牌种非法: {code!r}")
    if sign == "k":
        return Meld("triplet", tile, False, code, True)
    if sign == "g":
        return Meld("kong", tile, False, code, True)
    if sign == "G":
        return Meld("kong", tile, True, code, True)
    raise ValueError(f"未知组合码: {code!r}")


def parse_melds(codes: Iterable[str]) -> Tuple[Meld, ...]:
    melds = tuple(parse_meld_code(code) for code in codes)
    if len(melds) > HAND_STRUCTURE.meld_count:
        raise ValueError(f"副露数不能超过 {HAND_STRUCTURE.meld_count} 组")
    return melds


def _counter_key(counter: Counter) -> Tuple[Tuple[int, int], ...]:
    return tuple(sorted((tile, count) for tile, count in counter.items() if count))


@lru_cache(maxsize=32768)
def _meld_partitions(
    counter_key: Tuple[Tuple[int, int], ...],
    needed: int,
) -> Tuple[Tuple[Tuple[str, int], ...], ...]:
    counter = Counter(dict(counter_key))
    remaining_count = sum(counter.values())
    if needed == 0:
        return ((),) if remaining_count == 0 else ()
    if remaining_count != needed * 3:
        return ()

    tile = min(counter)
    results: List[Tuple[Tuple[str, int], ...]] = []

    if counter[tile] >= 3:
        next_counter = counter.copy()
        next_counter[tile] -= 3
        if next_counter[tile] == 0:
            del next_counter[tile]
        for rest in _meld_partitions(_counter_key(next_counter), needed - 1):
            results.append((("triplet", tile),) + rest)

    if is_number_tile(tile) and tile % 10 <= 7:
        second, third = tile + 1, tile + 2
        if counter[second] and counter[third] and second // 10 == tile // 10 == third // 10:
            next_counter = counter.copy()
            for member in (tile, second, third):
                next_counter[member] -= 1
                if next_counter[member] == 0:
                    del next_counter[member]
            for rest in _meld_partitions(_counter_key(next_counter), needed - 1):
                results.append((("sequence", tile + 1),) + rest)

    # 同一手在重复顺子路径下可能产生同构结果，统一去重并稳定排序。
    return tuple(sorted(set(results)))


@lru_cache(maxsize=131072)
def _can_form_melds(
    counter_key: Tuple[Tuple[int, int], ...],
    needed: int,
) -> bool:
    """只判断余牌能否组成指定数量的面子，不构造完整拆分。"""
    counter = Counter(dict(counter_key))
    remaining_count = sum(counter.values())
    if needed == 0:
        return remaining_count == 0
    if remaining_count != needed * 3:
        return False

    tile = min(counter)
    if counter[tile] >= 3:
        next_counter = counter.copy()
        next_counter[tile] -= 3
        if next_counter[tile] == 0:
            del next_counter[tile]
        if _can_form_melds(_counter_key(next_counter), needed - 1):
            return True

    if is_number_tile(tile) and tile % 10 <= 7:
        second, third = tile + 1, tile + 2
        if counter[second] and counter[third] and second // 10 == tile // 10 == third // 10:
            next_counter = counter.copy()
            for member in (tile, second, third):
                next_counter[member] -= 1
                if next_counter[member] == 0:
                    del next_counter[member]
            if _can_form_melds(_counter_key(next_counter), needed - 1):
                return True

    return False


@lru_cache(maxsize=65536)
def _has_standard_shape(
    counter_key: Tuple[Tuple[int, int], ...],
    concealed_needed: int,
) -> bool:
    """判断暗手是否满足配置的标准面子加一将结构。"""
    counter = Counter(dict(counter_key))
    if sum(counter.values()) != concealed_needed * 3 + 2:
        return False
    for pair in sorted(tile for tile, count in counter.items() if count >= 2):
        remainder = counter.copy()
        remainder[pair] -= 2
        if remainder[pair] == 0:
            del remainder[pair]
        if _can_form_melds(_counter_key(remainder), concealed_needed):
            return True
    return False


def _counter_is_eight_pairs_half(counter: Counter) -> bool:
    """判断完整暗手是否由一组三张与其余七对组成；四张同牌按两对处理。"""
    if sum(counter.values()) != HAND_STRUCTURE.complete_hand_tile_count:
        return False
    if any(count > 4 for count in counter.values()):
        return False
    triplets = [tile for tile, count in counter.items() if count == 3]
    if len(triplets) != 1:
        return False
    return sum(
        count // 2
        for tile, count in counter.items()
        if tile != triplets[0]
    ) == 7


@lru_cache(maxsize=32768)
def _structural_waits_cached(
    counter_key: Tuple[Tuple[int, int], ...],
    concealed_needed: int,
    allow_eight_pairs_half: bool,
) -> Tuple[int, ...]:
    counter = Counter(dict(counter_key))
    waits = set()
    for tile in STRUCTURE_TILES:
        if counter[tile] >= 4:
            continue
        candidate = counter.copy()
        candidate[tile] += 1
        if (
            allow_eight_pairs_half
            and concealed_needed == HAND_STRUCTURE.meld_count
            and _counter_is_eight_pairs_half(candidate)
        ):
            waits.add(tile)
            continue
        if _has_standard_shape(_counter_key(candidate), concealed_needed):
            waits.add(tile)
    return tuple(sorted(waits))


def enumerate_decompositions(
    hand_tiles: Sequence[int],
    meld_codes: Sequence[str],
    winning_tile: Optional[int] = None,
) -> List[Decomposition]:
    external = parse_melds(meld_codes)
    concealed_needed = HAND_STRUCTURE.concealed_meld_count(len(external))
    if concealed_needed < 0:
        return []
    if len(hand_tiles) != HAND_STRUCTURE.concealed_tile_count(
        len(external),
        complete=True,
    ):
        return []
    if any(not is_structure_tile(tile) for tile in hand_tiles):
        return []

    counter = Counter(hand_tiles)
    external_counter = Counter(
        tile
        for meld in external
        for tile in meld.tiles
    )
    if any(
        counter[tile] + external_counter[tile] > 4
        for tile in set(counter) | set(external_counter)
    ):
        return []

    results: List[Decomposition] = []
    for pair in sorted(tile for tile, count in counter.items() if count >= 2):
        remainder = counter.copy()
        remainder[pair] -= 2
        if remainder[pair] == 0:
            del remainder[pair]
        for partition in _meld_partitions(_counter_key(remainder), concealed_needed):
            concealed = tuple(
                Meld(kind, tile, True, ("s" if kind == "sequence" else "K") + str(tile))
                for kind, tile in partition
            )
            melds = external + concealed
            base = Decomposition(pair=pair, melds=melds)
            if winning_tile is None:
                results.append(base)
                continue

            if pair == winning_tile:
                results.append(
                    Decomposition(pair, melds, winning_component=("pair", -1))
                )
            for index, meld in enumerate(melds):
                if meld.external or winning_tile not in meld.tiles:
                    continue
                results.append(
                    Decomposition(pair, melds, winning_component=(meld.kind, index))
                )

    unique = {result.stable_key(): result for result in results}
    return [unique[key] for key in sorted(unique)]


def is_eight_pairs_half(hand_tiles: Sequence[int], meld_codes: Sequence[str]) -> bool:
    if meld_codes or len(hand_tiles) != HAND_STRUCTURE.complete_hand_tile_count:
        return False
    counter = Counter(hand_tiles)
    if any(tile not in STRUCTURE_TILES or count > 4 for tile, count in counter.items()):
        return False
    return _counter_is_eight_pairs_half(counter)


def structural_waits(
    hand_tiles: Sequence[int],
    meld_codes: Sequence[str],
    rules: Optional[TaiwanRules] = None,
) -> Set[int]:
    rules = rules or TaiwanRules()
    external = parse_melds(meld_codes)
    concealed_needed = HAND_STRUCTURE.concealed_meld_count(len(external))
    expected = HAND_STRUCTURE.concealed_tile_count(len(external), complete=False)
    if len(hand_tiles) != expected or any(tile not in STRUCTURE_TILES for tile in hand_tiles):
        return set()
    counter = Counter(hand_tiles)
    external_counter = Counter(
        tile
        for meld in external
        for tile in meld.tiles
    )
    if any(
        counter[tile] + external_counter[tile] > 4
        for tile in set(counter) | set(external_counter)
    ):
        return set()
    available_counts = Counter(counter)
    available_counts.update(external_counter)
    waits = set(
        tile
        for tile in STRUCTURE_TILES
        if available_counts[tile] < 4
    )
    return set(
        _structural_waits_cached(
            _counter_key(counter),
            concealed_needed,
            rules.eight_and_a_half_pairs_enabled,
        )
    ).intersection(waits)


def derive_pre_win_tiles(hand_tiles: Sequence[int], winning_tile: int) -> List[int]:
    tiles = list(hand_tiles)
    try:
        tiles.remove(winning_tile)
    except ValueError:
        return []
    return tiles


def winning_use_is_single_wait(decomposition: Decomposition, winning_tile: int) -> bool:
    component, index = decomposition.winning_component
    if component == "pair":
        return True
    if component != "sequence" or index < 0:
        return False
    meld = decomposition.melds[index]
    low, middle, high = meld.tiles
    if winning_tile == middle:
        return True
    rank = winning_tile % 10
    return (winning_tile == high and rank == 3) or (winning_tile == low and rank == 7)


def decomposition_is_all_sequences(decomposition: Decomposition) -> bool:
    return all(meld.kind == "sequence" for meld in decomposition.melds)


def decomposition_is_all_triplets(decomposition: Decomposition) -> bool:
    return all(meld.kind in ("triplet", "kong") for meld in decomposition.melds)
