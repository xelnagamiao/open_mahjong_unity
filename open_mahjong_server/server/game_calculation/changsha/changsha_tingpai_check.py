"""Changsha Mahjong tenpai check."""
from collections import Counter
from typing import List, Set

try:
    from .changsha_hepai_check import Changsha_Hepai_Check, VALID_TILES, _JIANG_PAIR_TILES, _expand_all
    from ..standard_solver import standard_waiting_tiles
except ImportError:
    from changsha_hepai_check import Changsha_Hepai_Check, VALID_TILES, _JIANG_PAIR_TILES, _expand_all  # type: ignore
    from standard_solver import standard_waiting_tiles  # type: ignore


class Changsha_Tingpai_Check:
    def __init__(self):
        self._hepai = Changsha_Hepai_Check()

    def tingpai_check(self, hand_tile_list: List[int], combination_list: List[str]) -> Set[int]:
        hand = list(hand_tile_list)
        melds = list(combination_list)
        sets_needed = 4 - len(melds)
        if sets_needed < 0 or len(hand) != sets_needed * 3 + 1:
            return set()
        physical = Counter(_expand_all(hand, melds))
        if any(tile not in VALID_TILES for tile in physical):
            return set()

        candidates: Set[int] = standard_waiting_tiles(
            hand,
            allow_honors=False,
            allow_fifth_copy=True,
        )
        if not melds and len(hand) == 13:
            hand_counts = Counter(hand)
            odds = [t for t, c in hand_counts.items() if c % 2 == 1]
            if len(odds) == 1:
                candidates.add(odds[0])
        if physical and all(tile in _JIANG_PAIR_TILES for tile in physical):
            candidates.update(_JIANG_PAIR_TILES)

        waits: Set[int] = set()
        for tile in candidates:
            if physical[tile] >= 4:
                continue
            score, fan_list = self._hepai.hepai_check(
                hand + [tile],
                melds,
                [],
                tile,
            )
            if score > 0 and fan_list:
                waits.add(tile)
        return waits
