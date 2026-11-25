"""Qingque 13-tile waiting tile (tingpai) checker.

Pure Python, stateless port of Qingque13Tingpai.cs.
"""

from __future__ import annotations

from typing import Dict, List, Optional, Sequence, Set, Tuple

_YAOJIU: frozenset[int] = frozenset(
    (11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47)
)
_EXCLUDE_TILES: frozenset[int] = frozenset((10, 20, 30, 40))


def _gs_check(hand_tiles: Sequence[int], waiting_tiles: Set[int]) -> None:
    gs_step_set: Set[int] = set()
    for tile_id in hand_tiles:
        if tile_id in _YAOJIU:
            gs_step_set.add(tile_id)
        else:
            return

    if len(gs_step_set) == 12:
        for t in _YAOJIU:
            if t not in gs_step_set:
                waiting_tiles.add(t)
    elif len(gs_step_set) == 13:
        waiting_tiles.update(_YAOJIU)


def _qd_check(hand_tiles: Sequence[int], waiting_tiles: Set[int]) -> None:
    tile_counts: Dict[int, int] = {}
    for tile_id in hand_tiles:
        tile_counts[tile_id] = tile_counts.get(tile_id, 0) + 1

    single = 0
    waiting_tile: Optional[int] = None
    for tile_id, count in tile_counts.items():
        if count == 1 or count == 3:
            single += 1
            waiting_tile = tile_id
        elif single >= 2:
            return

    if single == 1 and waiting_tile is not None:
        waiting_tiles.add(waiting_tile)


def _normal_check_block(sorted_hand: Sequence[int], comb_count: int) -> bool:
    if not sorted_hand:
        return False
    block_count = comb_count
    pointer = sorted_hand[0]
    for tile_id in sorted_hand:
        if tile_id != pointer and tile_id != pointer + 1:
            block_count += 1
        pointer = tile_id
    return block_count <= 6


def _normal_check(
    sorted_hand: List[int],
    combination_list: Sequence[str],
    complete_step: int,
    waiting_tiles: Set[int],
) -> None:
    if not _normal_check_block(sorted_hand, len(combination_list)):
        return

    # State: (hand_tiles, complete_step)
    stack: List[Tuple[List[int], int]] = []
    quetou_pointer = 0
    for tile_id in sorted_hand:
        if tile_id != quetou_pointer and sorted_hand.count(tile_id) >= 2:
            rem = sorted_hand.copy()
            rem.remove(tile_id)
            rem.remove(tile_id)
            stack.append((rem, complete_step + 2))
            quetou_pointer = tile_id
    stack.append((sorted_hand.copy(), complete_step))

    end_list: List[Tuple[List[int], int]] = []
    while stack:
        cur_hand, cur_step = stack.pop()

        # Traverse kezi (triplets)
        same_id = 0
        for tile_id in cur_hand:
            if tile_id != same_id and cur_hand.count(tile_id) >= 3:
                rem = cur_hand.copy()
                rem.remove(tile_id)
                rem.remove(tile_id)
                rem.remove(tile_id)
                stack.append((rem, cur_step + 3))
                same_id = tile_id

        # Traverse dazi (sequences)
        same_id = 0
        for tile_id in cur_hand:
            if tile_id <= 37 and (tile_id % 10) <= 7:
                if (
                    tile_id != same_id
                    and (tile_id + 1) in cur_hand
                    and (tile_id + 2) in cur_hand
                ):
                    rem = cur_hand.copy()
                    rem.remove(tile_id)
                    rem.remove(tile_id + 1)
                    rem.remove(tile_id + 2)
                    stack.append((rem, cur_step + 3))
                    same_id = tile_id

        if cur_step >= 11:
            end_list.append((cur_hand, cur_step))

    for rem_hand, _ in end_list:
        if len(rem_hand) == 1:
            waiting_tiles.add(rem_hand[0])
        elif len(rem_hand) == 2:
            tile1, tile2 = rem_hand[0], rem_hand[1]
            if tile1 == tile2:
                waiting_tiles.add(tile1)
            elif tile1 <= 39 and tile2 <= 39:
                if tile1 == tile2 - 1:
                    if tile1 // 10 == tile2 // 10:
                        waiting_tiles.add(tile1 - 1)
                        waiting_tiles.add(tile1 + 2)
                elif tile1 == tile2 - 2:
                    if tile1 // 10 == tile2 // 10:
                        waiting_tiles.add(tile1 + 1)


def qingque_tingpai_check(
    hand_tile_list: Sequence[int],
    combination_list: Sequence[str],
    debug: bool = False,
) -> Set[int]:
    """Calculate the set of waiting tiles for a Qingque 13-tile hand.

    Matches `Qingque13Tingpai.TingpaiCheck` and `call_tingpai_check` semantics.
    """
    sorted_hand = sorted(hand_tile_list)
    complete_step = len(combination_list) * 3
    waiting_tiles: Set[int] = set()

    if len(sorted_hand) == 13:
        _gs_check(sorted_hand, waiting_tiles)
        _qd_check(sorted_hand, waiting_tiles)

    _normal_check(sorted_hand, combination_list, complete_step, waiting_tiles)

    # Enforce physical 4-copy limits across concealed hand and declared melds
    counts: Dict[int, int] = {}
    for t in sorted_hand:
        counts[t] = counts.get(t, 0) + 1
    for meld in combination_list:
        if len(meld) < 2:
            continue
        tile = int(meld[1:])
        mtype = meld[0].lower()
        if mtype == "s":
            counts[tile - 1] = counts.get(tile - 1, 0) + 1
            counts[tile] = counts.get(tile, 0) + 1
            counts[tile + 1] = counts.get(tile + 1, 0) + 1
        elif mtype == "k":
            counts[tile] = counts.get(tile, 0) + 3
        elif mtype == "g":
            counts[tile] = counts.get(tile, 0) + 4

    if any(c > 4 for c in counts.values()):
        waiting_tiles.clear()
    else:
        waiting_tiles.difference_update(
            t for t in list(waiting_tiles) if counts.get(t, 0) >= 4
        )

    waiting_tiles.difference_update(_EXCLUDE_TILES)

    if debug:
        print(f"等待牌: {', '.join(map(str, sorted(waiting_tiles)))}")

    return waiting_tiles
