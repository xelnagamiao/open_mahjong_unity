"""血流的弃三张（10/11张）与换三张（13/14张）和牌、听牌检查。"""

from __future__ import annotations

from collections import Counter
from typing import List, Sequence, Set, Tuple

from .xueliu_rules import calculate_xueliu_fan
from .xueliu_exchange_rules import evaluate_exchange


_TILES = tuple(
    tile for suit in (1, 2, 3) for tile in range(suit * 10 + 1, suit * 10 + 10)
)


def _meld_sign(meld: str) -> str:
    return meld[0] if isinstance(meld, str) and meld else ""


def _meld_tile(meld: str) -> int | None:
    try:
        return int(meld[1:])
    except (TypeError, ValueError):
        return None


def _meld_suit(meld: str) -> int | None:
    tile = _meld_tile(meld)
    return tile // 10 if tile is not None else None


def _logical_meld_tiles(meld: str) -> List[int]:
    """Return the suit tiles represented by an exposed meld.

    A kong is four physical tiles, but it occupies one logical three-tile
   面子 in the 10/11 hand shape.
    """

    sign = _meld_sign(meld)
    tile = _meld_tile(meld)
    if tile is None:
        return []
    if sign in ("k", "K", "g", "G"):
        return [tile, tile, tile]
    if sign in ("s", "S"):
        return [tile - 1, tile, tile + 1]
    return []


def _valid_melds(melds: Sequence[str]) -> bool:
    return all(_meld_sign(m) in {"k", "g", "G"} and _meld_tile(m) in _TILES for m in melds)


def _solve_groups(counts: Counter[int], groups_left: int, need_pair: bool):
    """Yield one decomposition of *counts* into groups and a pair."""

    if groups_left == 0:
        if need_pair and sum(counts.values()) == 2:
            first = next((tile for tile, count in counts.items() if count == 2), None)
            if first is not None:
                yield [f"q{first}"]
        elif not need_pair and sum(counts.values()) == 0:
            yield []
        return

    if sum(counts.values()) != groups_left * 3 + (2 if need_pair else 0):
        return
    first = next((tile for tile in sorted(counts) if counts[tile]), None)
    if first is None:
        return

    if need_pair and counts[first] >= 2:
        counts[first] -= 2
        for tail in _solve_groups(counts, groups_left, False):
            yield [f"q{first}"] + tail
        counts[first] += 2

    if counts[first] >= 3:
        counts[first] -= 3
        for tail in _solve_groups(counts, groups_left - 1, need_pair):
            yield [f"K{first}"] + tail
        counts[first] += 3

    if first % 10 <= 7 and counts[first + 1] and counts[first + 2]:
        counts[first] -= 1
        counts[first + 1] -= 1
        counts[first + 2] -= 1
        for tail in _solve_groups(counts, groups_left - 1, need_pair):
            yield [f"S{first + 1}"] + tail
        counts[first] += 1
        counts[first + 1] += 1
        counts[first + 2] += 1


class Xueliu_Hepai_Check:
    """Check the 3-meld-plus-pair shape and return fan values."""

    def _all_tiles(self, hand: Sequence[int], melds: Sequence[str]) -> List[int]:
        result = list(hand)
        for meld in melds:
            result.extend(_logical_meld_tiles(meld))
        return result

    def _decompositions(self, hand: Sequence[int], melds: Sequence[str], meld_count: int = 3):
        if meld_count not in (3, 4) or not _valid_melds(melds) or len(melds) > meld_count:
            return []
        required = meld_count - len(melds)
        if len(hand) != required * 3 + 2:
            return []
        counts = Counter(hand)
        return list(_solve_groups(counts, required, True))

    def _has_missing_suit(self, tiles: Sequence[int]) -> bool:
        return len({tile // 10 for tile in tiles}) <= 2

    def hepai_check(
        self,
        hand_list: Sequence[int],
        tiles_combination: Sequence[str],
        way_to_hepai: Sequence[str] | None = None,
        get_tile: int | None = None,
        dingque_suit: int = 0,
        meld_count: int = 3,
    ) -> Tuple[int, List[str]]:
        hand = list(hand_list)
        melds = list(tiles_combination or [])
        if not _valid_melds(melds) or any(type(t) is not int or t not in _TILES for t in hand):
            return 0, []
        physical = Counter(hand)
        for meld in melds:
            physical[_meld_tile(meld)] += 4 if _meld_sign(meld) in {"g", "G"} else 3
        if any(count > 4 for count in physical.values()):
            return 0, []
        all_tiles = self._all_tiles(hand, melds)
        if not all_tiles or not self._has_missing_suit(all_tiles):
            return 0, []
        if dingque_suit in (1, 2, 3) and any(tile // 10 == dingque_suit for tile in all_tiles):
            return 0, []
        decompositions = self._decompositions(hand, melds, meld_count)
        if meld_count == 4:
            return evaluate_exchange(hand, melds, decompositions, physical, way_to_hepai or ())
        if not decompositions:
            return 0, []

        zimo = way_to_hepai is not None and "自摸" in way_to_hepai
        # 门清只排除碰、明杠；暗杠仍可算门清自摸。
        menqing = not any(_meld_sign(m) in {"k", "g"} for m in melds)
        minggang = sum(1 for m in melds if _meld_sign(m) == "g")
        angang = sum(1 for m in melds if _meld_sign(m) == "G")

        best: Tuple[int, List[str]] = (0, [])
        for groups in decompositions:
            is_pengpeng = all(group.startswith("K") or group.startswith("q") for group in groups)
            suits = {tile // 10 for tile in all_tiles}
            fan, names = calculate_xueliu_fan(
                pengpeng=is_pengpeng,
                qingyise=len(suits) == 1,
                menqing=menqing,
                zimo=zimo,
                minggang_count=minggang,
                angang_count=angang,
            )
            if fan > best[0]:
                best = (fan, names)
        return best


class Xueliu_Tingpai_Check:
    """Enumerate waits for a 10-tile blood-flow hand."""

    def __init__(self):
        self._hepai = Xueliu_Hepai_Check()

    def tingpai_check(self, hand_tile_list: Sequence[int], combination_list: Sequence[str], meld_count: int = 3) -> Set[int]:
        hand = list(hand_tile_list)
        expected = (meld_count - len(combination_list)) * 3 + 1
        if len(hand) != expected:
            return set()
        waits: Set[int] = set()
        for tile in _TILES:
            if hand.count(tile) >= 4:
                continue
            fan, names = self._hepai.hepai_check(
                hand + [tile], combination_list, [], tile, 0, meld_count,
            )
            if names:
                waits.add(tile)
        return waits


__all__ = ["Xueliu_Hepai_Check", "Xueliu_Tingpai_Check"]
