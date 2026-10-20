"""复刻 Unity RecordHuHandBuilder。"""
from __future__ import annotations

from typing import Optional, Sequence

from .decoder import parse_tick_int
from .flags import resolve_record_flags

def try_parse_hepai_tile(tick: Sequence[str], rule: Optional[str]) -> int:
    if tick is None:
        return 0
    tick_index = resolve_record_flags(rule=rule).record_hu_tile_tick_index
    if len(tick) <= tick_index:
        return 0
    tile = parse_tick_int(tick, tick_index)
    return tile if tile >= 10 else 0


def is_flower_win(tick: Sequence[str], rule: Optional[str]) -> bool:
    if resolve_record_flags(rule=rule).rule_id == "vietnamese":
        return False
    tile = try_parse_hepai_tile(tick, rule)
    return 51 <= tile <= 58
