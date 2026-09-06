"""复刻 Unity RecordHuHandBuilder。"""
from __future__ import annotations

from typing import Optional, Sequence

from .decoder import parse_tick_int
from .flags import resolve_record_flags

HONGQUE_BASE_ID = 1000


def is_hongque_id(tile_id: int) -> bool:
    value = int(tile_id) - HONGQUE_BASE_ID
    colour = value // 10
    number = value % 10
    return colour >= 0 and colour < 14 and 1 <= number <= 9


def is_hongque_hand(hand: Optional[Sequence[int]], rule: Optional[str]) -> bool:
    if resolve_record_flags(rule=rule).rule_id == "hongque":
        return True
    if not hand:
        return False
    return any(is_hongque_id(tile) for tile in hand)


def needs_ron_tile(hand: Optional[Sequence[int]], rule: Optional[str] = None) -> bool:
    if hand is None:
        return False
    return len(hand) % 3 == 2 if is_hongque_hand(hand, rule) else len(hand) % 3 == 1


def try_parse_hepai_tile(tick: Sequence[str], rule: Optional[str]) -> int:
    if tick is None:
        return 0
    tick_index = resolve_record_flags(rule=rule).record_hu_tile_tick_index
    if len(tick) <= tick_index:
        return 0
    tile = parse_tick_int(tick, tick_index)
    return tile if tile >= 10 else 0


def is_flower_win(tick: Sequence[str], rule: Optional[str]) -> bool:
    tile = try_parse_hepai_tile(tick, rule)
    return 51 <= tile <= 58
