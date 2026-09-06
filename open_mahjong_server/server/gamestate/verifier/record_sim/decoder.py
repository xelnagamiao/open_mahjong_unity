"""复刻 Unity GameRecordJsonDecoder 的 tick 解析。"""
from __future__ import annotations

import json
from typing import List, Optional, Sequence


def parse_tick_int(tick: Sequence[str], index: int) -> int:
    if tick is None or index < 0 or index >= len(tick) or tick[index] in (None, ""):
        return 0
    try:
        return int(str(tick[index]).strip())
    except (TypeError, ValueError):
        return 0


def parse_buhua_mo_flag(tick: Sequence[str]) -> bool:
    if tick is None or len(tick) < 4 or not tick[3]:
        return False
    flag = str(tick[3]).strip().upper()
    if flag not in ("T", "F"):
        return False
    return flag == "T"


def parse_kan_mo_gang_flag(tick: Sequence[str]) -> bool:
    if tick is None or len(tick) < 3:
        raise ValueError(f"暗杠/加杠 tick 缺少摸杠/手杠标记: {list(tick or [])}")
    flag = str(tick[2]).strip().upper()
    if flag not in ("T", "F"):
        raise ValueError(f"暗杠/加杠 tick 第三段必须为 T 或 F: {list(tick)}")
    return flag == "T"


def validate_kan_mo_gang_tick(tick: Sequence[str], context: str = "") -> None:
    if not tick:
        return
    if tick[0] not in ("ag", "jg"):
        return
    try:
        parse_kan_mo_gang_flag(tick)
    except ValueError as exc:
        raise ValueError(f"{context}: {exc}" if context else str(exc)) from exc


def resolve_acting_player(tick: Sequence[str], action: str, default_player: int) -> int:
    if tick is None or not tick:
        return default_player
    if action in ("bh", "bd", "cl", "cm", "cr", "p", "g") and len(tick) >= 3:
        return parse_tick_int(tick, 2)
    if action == "ca" and len(tick) >= 2:
        return parse_tick_int(tick, 1)
    if action == "state" and len(tick) >= 3:
        return parse_tick_int(tick, 2)
    if action in ("hu_self", "hu_first", "hu_second", "hu_third", "riichi") and len(tick) >= 2:
        return parse_tick_int(tick, 1)
    return default_player


def parse_hu_fan_list(tick: Sequence[str], index: int) -> List[str]:
    if tick is None or index >= len(tick) or not tick[index]:
        return []
    raw = tick[index]
    if isinstance(raw, list):
        return [str(item) for item in raw]
    text = str(raw).strip()
    if not text.startswith("["):
        return [text] if text else []
    try:
        parsed = json.loads(text)
        if isinstance(parsed, list):
            return [str(item) for item in parsed]
    except (TypeError, ValueError, json.JSONDecodeError):
        pass
    return []


def hu_fan_contains_cuohe(tick: Sequence[str]) -> bool:
    return "错和" in parse_hu_fan_list(tick, 3)


def resolve_buhua_recipient(tick: Sequence[str], action_player: int) -> int:
    if tick is not None and len(tick) >= 5:
        recipient = parse_tick_int(tick, 4)
        if 0 <= recipient < 4:
            return recipient
    return action_player


def try_resolve_buhua_transfer(tick: Sequence[str]) -> Optional[tuple[int, int]]:
    if tick is None or len(tick) < 7:
        return None
    from_player = parse_tick_int(tick, 5)
    tile_id = parse_tick_int(tick, 6)
    if 0 <= from_player < 4 and 51 <= tile_id <= 58:
        return from_player, tile_id
    return None


def parse_inline_gang_score_changes(tick: Sequence[str]) -> Optional[List[int]]:
    """川麻 gs 后四家分变。对照 C# GameRecordJsonDecoder.ParseInlineGangScoreChanges。"""
    if tick is None:
        return None
    text = [str(item) for item in tick]
    try:
        gs_index = len(text) - 1 - text[::-1].index("gs")
    except ValueError:
        return None
    if gs_index + 4 >= len(text):
        return None
    arr = [parse_tick_int(text, gs_index + 1 + i) for i in range(4)]
    if arr == [0, 0, 0, 0]:
        return None
    return arr


def parse_score_changes_array(tick: Sequence[str], index: int) -> Optional[List[int]]:
    if tick is None or index >= len(tick) or tick[index] in (None, ""):
        return None
    raw = tick[index]
    if isinstance(raw, list):
        try:
            return [int(item) for item in raw]
        except (TypeError, ValueError):
            return None
    text = str(raw).strip()
    if not text.startswith("["):
        return None
    try:
        parsed = json.loads(text)
    except (TypeError, ValueError, json.JSONDecodeError):
        return None
    if not isinstance(parsed, list):
        return None
    try:
        return [int(item) for item in parsed]
    except (TypeError, ValueError):
        return None


def convert_score_changes_to_original(by_player_index: Optional[List[int]], seats: Optional[Sequence[int]]) -> Optional[List[int]]:
    if by_player_index is None or seats is None or len(seats) < 4:
        return None
    by_original = [0, 0, 0, 0]
    for orig in range(4):
        seat = int(seats[orig])
        if 0 <= seat < len(by_player_index):
            by_original[orig] = by_player_index[seat]
    return by_original


def accumulate_score_changes_from_tick(score_changes: Optional[List[int]], tick: Sequence[str], seats: Sequence[int]) -> Optional[List[int]]:
    """对照 C# GameRecordJsonDecoder.AccumulateScoreChangesFromTick。"""
    if tick is None or not tick:
        return score_changes
    act = str(tick[0])
    sc = None
    if len(tick) >= 5 and act in ("hu_self", "hu_first", "hu_second", "hu_third"):
        sc = parse_score_changes_array(tick, 4)
    elif act == "hu_riichi" and len(tick) >= 7:
        sc = parse_score_changes_array(tick, 6)
    elif act == "ryuukyoku" and len(tick) >= 3:
        sc = parse_score_changes_array(tick, 2)
    elif act in ("ag", "jg", "g", "gr"):
        sc = parse_inline_gang_score_changes(tick)
    elif act == "liuju" and len(tick) >= 2:
        step = str(tick[1])
        if step == "settle_hu":
            sc = parse_score_changes_array(tick, 6)
        elif step == "chajiao":
            sc = parse_score_changes_array(tick, 5)
        elif step == "cha_refund":
            sc = parse_score_changes_array(tick, 2)
    by_original = convert_score_changes_to_original(sc, seats)
    if by_original is None:
        return score_changes
    if score_changes is None:
        return list(by_original)
    return [score_changes[i] + by_original[i] for i in range(4)]
