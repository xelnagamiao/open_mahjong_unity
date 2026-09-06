"""复刻 Unity GameRecordMeldCodec。"""
from __future__ import annotations

from typing import List, Optional, Sequence


def normalize_tile(tile_id: int) -> int:
    if tile_id == 105:
        return 15
    if tile_id == 205:
        return 25
    if tile_id == 305:
        return 35
    return int(tile_id)


def hand_tile_count(record_action: str) -> int:
    return 3 if record_action == "g" else 2


ANGANG_HAND_TILE_COUNT = 4


def parse_tick_int(tick: Sequence[str], index: int) -> int:
    if tick is None or index < 0 or index >= len(tick) or tick[index] in (None, ""):
        return 0
    try:
        return int(str(tick[index]).strip())
    except (TypeError, ValueError):
        return 0


def remove_exact_tiles(tile_list: list[int], tile_ids: Sequence[int]) -> None:
    for tid in tile_ids:
        try:
            tile_list.remove(int(tid))
        except ValueError:
            continue


def remove_n_by_normalized(
    tile_list: list[int],
    normalized_tile: int,
    count: int,
    prefer_draw_slot_first: bool = False,
) -> list[int]:
    removed: list[int] = []
    norm = normalize_tile(normalized_tile)
    if prefer_draw_slot_first and tile_list:
        last = tile_list[-1]
        if normalize_tile(last) == norm:
            removed.append(tile_list.pop())
    for _ in range(count - len(removed)):
        found = -1
        for i, tile in enumerate(tile_list):
            if tile == normalized_tile:
                found = i
                break
        if found < 0:
            for i, tile in enumerate(tile_list):
                if normalize_tile(tile) == norm:
                    found = i
                    break
        if found < 0:
            break
        removed.append(tile_list.pop(found))
    return removed


def resolve_angang_removed_tiles(
    tick: Sequence[str],
    tile_list: list[int],
    angang_tile_id: int,
    is_mo_gang: bool,
) -> list[int]:
    if tick is None or len(tick) < 3 + ANGANG_HAND_TILE_COUNT:
        return remove_n_by_normalized(
            tile_list, angang_tile_id, ANGANG_HAND_TILE_COUNT, prefer_draw_slot_first=is_mo_gang
        )
    stored = [parse_tick_int(tick, i) for i in range(3, 3 + ANGANG_HAND_TILE_COUNT)]
    remove_exact_tiles(tile_list, stored)
    return stored


def resolve_hand_tiles(tick: Sequence[str], record_action: str, mingpai_tile_id: int) -> list[int]:
    expected = hand_tile_count(record_action)
    if tick is not None and len(tick) >= 3 + expected:
        return [parse_tick_int(tick, i) for i in range(3, 3 + expected)]
    return [mingpai_tile_id] * expected


def build_combination_target(record_action: str, mingpai_tile_id: int) -> str:
    norm = normalize_tile(mingpai_tile_id)
    if record_action == "cl":
        return f"s{norm - 1}"
    if record_action == "cm":
        return f"s{norm}"
    if record_action == "cr":
        return f"s{norm + 1}"
    if record_action == "p":
        return f"k{norm}"
    return f"g{norm}"


def build_mingpai_mask(
    record_action: str,
    mingpai_tile_id: int,
    hand_tiles: Sequence[int],
    relative: str,
) -> list[int]:
    if record_action in ("cl", "cm", "cr"):
        return [1, mingpai_tile_id, 0, hand_tiles[0], 0, hand_tiles[1]]
    r1, r2 = hand_tiles[0], hand_tiles[1]
    if record_action == "p":
        if relative == "left":
            return [1, mingpai_tile_id, 0, r1, 0, r2]
        if relative == "right":
            return [0, r1, 0, r2, 1, mingpai_tile_id]
        return [0, r1, 1, mingpai_tile_id, 0, r2]
    r3 = hand_tiles[2]
    if relative == "left":
        return [1, mingpai_tile_id, 0, r1, 0, r2, 0, r3]
    if relative == "right":
        return [0, r1, 0, r2, 0, r3, 1, mingpai_tile_id]
    return [0, r1, 1, mingpai_tile_id, 0, r2, 0, r3]


from .flags import resolve_record_flags


def build_angang_mask_from_removed(removed_tiles: Sequence[int], rule: str) -> list[int]:
    r0 = removed_tiles[0] if removed_tiles else 0
    r1 = removed_tiles[1] if len(removed_tiles) > 1 else r0
    r2 = removed_tiles[2] if len(removed_tiles) > 2 else r0
    r3 = removed_tiles[3] if len(removed_tiles) > 3 else r0
    flags = resolve_record_flags(rule=rule)
    if flags.face_down_ankan:
        return [0, r0, 0, r1, 0, r2, 0, r3]
    if flags.peek_ankan:
        return [2, r0, 0, r1, 0, r2, 2, r3]
    return [2, r0, 2, r1, 2, r2, 2, r3]


def find_combination_index(combination_tiles: Sequence[str], target: str) -> int:
    if not combination_tiles or not target or len(target) < 2:
        return -1
    prefix = target[0]
    want = normalize_tile(int(target[1:]))
    for i, key in enumerate(combination_tiles):
        if not key or key[0] != prefix:
            continue
        try:
            if normalize_tile(int(key[1:])) == want:
                return i
        except ValueError:
            continue
    return -1
